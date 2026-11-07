using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using LiveScreenTextSync.Core;

namespace LiveScreenTextSync.App.Automation;

public sealed class AutomationWorker
{
    private AutomationElement? element;
    private TargetInfo? identity;

    public static int Run()
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = new UTF8Encoding(false);
        var worker = new AutomationWorker();
        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            WorkerResponse response;
            try
            {
                var request = JsonSerializer.Deserialize<WorkerRequest>(line) ?? throw new InvalidOperationException("Invalid automation request.");
                response = request.Operation switch
                {
                    "inspect" => worker.Inspect(request.X, request.Y),
                    "write" => worker.Write(request.Text ?? throw new InvalidOperationException("No text supplied.")),
                    "validate" => worker.Validate(),
                    "ping" => new(true, "Worker ready."),
                    _ => new(false, "Unknown automation operation.")
                };
            }
            catch (Exception ex)
            {
                worker.element = null;
                worker.identity = null;
                response = new(false, ex switch
                {
                    ElementNotAvailableException => "The selected field no longer exists. Select the target again.",
                    UnauthorizedAccessException => "Access denied. Use a target running at the same privilege level.",
                    InvalidOperationException => ex.Message,
                    _ => "The target provider failed. Select the target again."
                });
            }
            Console.WriteLine(JsonSerializer.Serialize(response));
        }
        return 0;
    }

    private WorkerResponse Inspect(int x, int y)
    {
        element = null;
        identity = null;
        var selected = AutomationElement.FromPoint(new Point(x, y));
        var process = selected.Current.ProcessId;
        if (process == Environment.ProcessId || process == GetParentId())
            return new(false, "Select a field in another application, or open the separate test target.");
        for (var level = 0; level < 4 && selected is not null; level++)
        {
            var current = selected.Current;
            if (current.ProcessId != process) break;
            if (current.IsPassword) return new(false, "Password and protected fields are not supported.");
            if (current.IsEnabled && current.ControlType is var type && (type == ControlType.Edit || type == ControlType.Document))
            {
                string? mechanism = null;
                if (selected.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && !((ValuePattern)pattern).Current.IsReadOnly)
                    mechanism = "UI Automation · ValuePattern";
                else if (NativeEdit.IsWritable(current.NativeWindowHandle, process) &&
                    AutomationElement.FromHandle(current.NativeWindowHandle).GetRuntimeId().SequenceEqual(selected.GetRuntimeId()))
                    mechanism = NativeEdit.GetAccessible(current.NativeWindowHandle) is not null ? "MSAA · IAccessible" : "Win32 · WM_SETTEXT";
                if (mechanism is not null)
                {
                    using var owner = Process.GetProcessById(process);
                    identity = new(process, owner.StartTime.ToUniversalTime().Ticks, owner.ProcessName,
                        current.Name, current.AutomationId, current.ClassName, mechanism, selected.GetRuntimeId());
                    element = selected;
                    return new(true, "Compatible. Inspection did not change the field.", identity);
                }
                return new(false, "This field does not expose a supported background write mechanism.");
            }
            selected = TreeWalker.ControlViewWalker.GetParent(selected);
        }
        return new(false, "Point at an editable text field. This element does not support background writing.");
    }

    private WorkerResponse Write(string text)
    {
        Validate();
        var selected = element!;
        var target = identity!;
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Empty updates are blocked.");
        var current = selected.Current;
        bool verified;
        if (target.Mechanism.StartsWith("UI Automation", StringComparison.Ordinal))
        {
            if (!selected.TryGetCurrentPattern(ValuePattern.Pattern, out var value) || ((ValuePattern)value).Current.IsReadOnly)
                throw new InvalidOperationException("The target is no longer writable.");
            var pattern = (ValuePattern)value;
            pattern.SetValue(text);
            verified = TextConfirmation.Normalize(pattern.Current.Value) == text;
        }
        else
        {
            var handle = (nint)current.NativeWindowHandle;
            if (!NativeEdit.IsWritable(handle, target.ProcessId)) throw new InvalidOperationException("The native field is no longer writable.");
            if (target.Mechanism.StartsWith("MSAA", StringComparison.Ordinal))
            {
                var accessible = NativeEdit.GetAccessible(handle) ?? throw new InvalidOperationException("The field no longer supports MSAA writing.");
                try { accessible.set_accValue(0, text); }
                catch (System.Runtime.InteropServices.COMException) { NativeEdit.SetText(handle, text); }
                catch (NotImplementedException) { NativeEdit.SetText(handle, text); }
                // Some providers expose accValue but do not implement its setter. Readback is required.
                if (!NativeEdit.Verify(handle, text)) NativeEdit.SetText(handle, text);
            }
            else NativeEdit.SetText(handle, text);
            verified = NativeEdit.Verify(handle, text);
        }
        if (!verified) throw new InvalidOperationException("Readback did not match the update. Synchronization stopped.");
        return new(true, "Text updated and readback verified.", target, true);
    }

    private WorkerResponse Validate()
    {
        if (element is null || identity is null) throw new InvalidOperationException("Select a compatible target first.");
        using var owner = Process.GetProcessById(identity.ProcessId);
        if (owner.HasExited || owner.StartTime.ToUniversalTime().Ticks != identity.ProcessStarted)
            throw new InvalidOperationException("The selected application was closed or restarted. Select the target again.");
        var current = element.Current;
        if (current.ProcessId != identity.ProcessId || current.IsPassword || !current.IsEnabled ||
            current.AutomationId != identity.AutomationId || current.ClassName != identity.ClassName ||
            !element.GetRuntimeId().SequenceEqual(identity.RuntimeId))
            throw new InvalidOperationException("The selected field changed identity or became unavailable. Select it again.");

        return new(true, "Target identity verified.", identity);
    }

    private static int GetParentId() => int.TryParse(Environment.GetEnvironmentVariable("LSTS_PARENT_PID"), out var pid) ? pid : -1;
}
