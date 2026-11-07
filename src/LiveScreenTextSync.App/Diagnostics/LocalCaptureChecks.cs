using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LiveScreenTextSync.App.Views;

namespace LiveScreenTextSync.App.Diagnostics;

public static class LocalCaptureChecks
{
    public static async Task RunAsync(MainWindow window, string reportPath)
    {
        var results = new List<object>();
        var directory = Path.GetDirectoryName(reportPath)!;
        Directory.CreateDirectory(directory);
        var infoPath = Path.Combine(directory, $"sample-{Guid.NewGuid():N}.json");
        Process? sample = null;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var token = deadline.Token;
            Require(window.FindName("TargetButton") is null && window.FindName("PreviewButton") is null, "The main workflow must not require an external target or preview mode.");
            results.Add(new { Check = "Main window has a single local capture workflow", Passed = true });
            sample = Program.StartChild("--sample-source", "--fixture-info", infoPath);
            await WaitUntilAsync(() => File.Exists(infoPath), token);
            var info = JsonSerializer.Deserialize<SampleSourceInfo>(File.ReadAllText(infoPath))!;
            await window.StartLocalCheckAsync(info.Region);
            var preview = (TextBox)window.FindName("PreviewText");
            await WaitUntilAsync(() => preview.Text.Contains("SCREEN TEXT READY", StringComparison.OrdinalIgnoreCase), token);
            Require(((Button)window.FindName("CopyButton")).IsEnabled, "Captured text must be available to copy.");
            results.Add(new { Check = "Visible source is recognized into the main window without selecting a target", Passed = true });
            File.WriteAllText(infoPath + ".command", "change");
            await WaitUntilAsync(() => preview.Text.Contains("SCREEN TEXT UPDATED", StringComparison.OrdinalIgnoreCase), token);
            results.Add(new { Check = "Changed source refreshes the local text field", Passed = true });
            File.WriteAllText(infoPath + ".command", "empty");
            await WaitUntilAsync(() => ((TextBlock)window.FindName("StatusLabel")).Text.Contains("No text detected", StringComparison.Ordinal), token);
            Require(preview.Text.Contains("SCREEN TEXT UPDATED", StringComparison.OrdinalIgnoreCase), "Blank OCR must preserve the last captured text.");
            results.Add(new { Check = "Empty OCR preserves the last useful text", Passed = true });
            await window.StopLocalCheckAsync();
            var stopped = preview.Text;
            File.WriteAllText(infoPath + ".command", "ready");
            await Task.Delay(450, token);
            Require(preview.Text == stopped && ((Button)window.FindName("CopyButton")).IsEnabled, "Stop must freeze the text and keep copying available.");
            Require(((Button)window.FindName("StartButton")).IsEnabled, "A selected source must be sufficient to start another capture.");
            results.Add(new { Check = "Stop freezes captured text; Copy Text and Start Capture remain available", Passed = true });
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Success = true, Checks = results }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Success = false, Error = ex.ToString(), Checks = results }, new JsonSerializerOptions { WriteIndented = true }));
            Environment.ExitCode = 1;
        }
        finally
        {
            await window.StopLocalCheckAsync();
            if (sample is not null)
            {
                if (!sample.HasExited) sample.Kill(entireProcessTree: true);
                sample.Dispose();
            }
            foreach (var suffix in new[] { "", ".command" })
                if (File.Exists(infoPath + suffix)) File.Delete(infoPath + suffix);
            window.Close();
        }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition()) await Task.Delay(100, token);
    }
}
