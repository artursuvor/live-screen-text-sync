using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LiveScreenTextSync.Core;

namespace LiveScreenTextSync.App.Automation;

public sealed class AutomationClient : ITextWriter, IDisposable
{
    private Process? worker;
    private readonly SemaphoreSlim gate = new(1, 1);
    public TargetInfo? Target { get; private set; }
    public bool HasTarget => Target is not null && worker is { HasExited: false };

    public async Task<WorkerResponse> InspectAsync(int x, int y, CancellationToken token = default)
    {
        Target = null;
        var response = await RequestAsync(new("inspect", x, y), token).ConfigureAwait(false);
        if (response.Success) Target = response.Target;
        return response;
    }

    public async Task WriteAsync(string text, CancellationToken token)
    {
        if (!HasTarget) throw new InvalidOperationException("The automation worker lost its target. Select the target again.");
        var response = await RequestAsync(new("write", Text: text), token).ConfigureAwait(false);
        if (!response.Success) { Target = null; throw new InvalidOperationException(response.Message); }
    }

    public async Task<WorkerResponse> RequestAsync(WorkerRequest request, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            if (worker is null || worker.HasExited)
            {
                if (request.Operation == "write") throw new InvalidOperationException("The selected target session was lost.");
                StartWorker();
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await worker!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token).ConfigureAwait(false);
            await worker.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
            var line = await worker.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            return JsonSerializer.Deserialize<WorkerResponse>(line ?? throw new InvalidOperationException("The automation worker exited unexpectedly."))
                ?? throw new InvalidOperationException("Invalid automation response.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            Reset();
            throw new InvalidOperationException("The target did not respond within 3 seconds. The automation worker was stopped. Select the target again.");
        }
        catch { Reset(); throw; }
        finally { gate.Release(); }
    }

    private void StartWorker()
    {
        Reset();
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the application executable.");
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
        };
        if (System.IO.Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(Program).Assembly.Location);
        info.ArgumentList.Add("--automation-worker");
        info.Environment["LSTS_PARENT_PID"] = Environment.ProcessId.ToString();
        worker = Process.Start(info) ?? throw new InvalidOperationException("Cannot start the automation worker.");
        worker.BeginErrorReadLine();
    }

    public void Reset()
    {
        Target = null;
        if (worker is null) return;
        try { if (!worker.HasExited) worker.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        worker.Dispose();
        worker = null;
    }

    public void Dispose() => Reset();
}
