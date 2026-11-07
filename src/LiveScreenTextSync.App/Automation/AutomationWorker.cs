using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LiveScreenTextSync.App.Interop;
using LiveScreenTextSync.Core;
using UIA = Interop.UIAutomationClient;

namespace LiveScreenTextSync.App.Automation;

public sealed class AutomationWorker
{
    private readonly UIA.IUIAutomation automation = new UIA.CUIAutomation8();
    private UIA.IUIAutomationElement? element;
    private TargetInfo? identity;
    private const int ValuePatternId = 10002, LegacyPatternId = 10018;

    public static int Run()
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = new UTF8Encoding(false);
        using var parent = GetParentId() > 0 ? Process.GetProcessById(GetParentId()) : null;
        using var watchdog = new Timer(_ => { if (parent?.HasExited == true) Environment.Exit(0); }, null, 1000, 1000);
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
                    UnauthorizedAccessException => "Access denied. Use a target running at the same privilege level.",
                    InvalidOperationException => ex.Message,
                    _ => "The selected field is unavailable or its provider failed. Select the target again."
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
        var selected = automation.ElementFromPoint(new UIA.tagPOINT { x = x, y = y });
        var process = selected.CurrentProcessId;
        if (process == Environment.ProcessId || process == GetParentId())
            return new(false, "Select a field in another application, or open the separate test target.");

        // Providers may return a text leaf, a combo box, or an enclosing pane. Resolve only
        // descendants under the actual pointer; never select a nearby or unrelated input.
        selected = DeepestAtPoint(selected, process, x, y);
        var path = new List<UIA.IUIAutomationElement>();
        for (var level = 0; level < 20 && selected is not null; level++)
        {
            if (selected.CurrentProcessId != process) break;
            path.Add(selected);
            var legacy = Pattern<UIA.IUIAutomationLegacyIAccessiblePattern>(selected, LegacyPatternId);
            if (selected.CurrentIsPassword != 0 || (legacy is not null && (legacy.CurrentState & 0x20000000) != 0))
                return new(false, "Password and protected fields are not supported.");
            if (selected.CurrentControlType == 50032) break; // Do not walk outside the hit window.
            selected = automation.RawViewWalker.GetParentElement(selected);
        }

        foreach (var candidate in path)
        {
            if (candidate.CurrentIsEnabled == 0 || !Contains(candidate, x, y)) continue;
            var type = candidate.CurrentControlType;
            var textField = type is 50004 or 50030 or 50003; // Edit, Document, ComboBox
            var customField = type is 50025 or 50033 && candidate.CurrentIsKeyboardFocusable != 0;
            if (textField || customField)
            {
                string? mechanism = null;
                var value = Pattern<UIA.IUIAutomationValuePattern>(candidate, ValuePatternId);
                if (value is { CurrentIsReadOnly: not 0 })
                    return new(false, $"{Describe(candidate)} This field is read-only. Select an editable input.");
                // Native edit messages avoid the activation observed with UIA setters on this Windows build.
                if (NativeEdit.IsWritable(candidate.CurrentNativeWindowHandle, process) &&
                    automation.CompareElements(automation.ElementFromHandle(candidate.CurrentNativeWindowHandle), candidate) != 0)
                    mechanism = "Win32 · WM_SETTEXT";
                else if (value is { CurrentIsReadOnly: 0 })
                    mechanism = "UI Automation · ValuePattern";
                else if (WritableLegacy(candidate) is not null)
                    mechanism = "UI Automation · LegacyIAccessible";
                if (mechanism is not null)
                {
                    using var owner = Process.GetProcessById(process);
                    identity = new(process, owner.StartTime.ToUniversalTime().Ticks, owner.ProcessName,
                        candidate.CurrentName, candidate.CurrentAutomationId, candidate.CurrentClassName, mechanism, candidate.GetRuntimeId());
                    element = candidate;
                    return new(true, "Compatible. Inspection did not change the field.", identity);
                }
                // A child document may omit setters while its containing editable combo exposes them.
            }
        }
        var hit = path.FirstOrDefault();
        var description = hit is null ? "No accessible element was found." : Describe(hit);
        return new(false, $"{description} No writable ValuePattern or LegacyIAccessible was found under the pointer. Click inside the input, keep the pointer there, and select it again. Some custom editors expose text for reading only.");
    }

    private UIA.IUIAutomationElement DeepestAtPoint(UIA.IUIAutomationElement root, int process, int x, int y)
    {
        var current = root;
        var remaining = 96;
        for (var depth = 0; depth < 12 && remaining > 0; depth++)
        {
            UIA.IUIAutomationElement? best = null;
            long area = long.MaxValue;
            var child = automation.RawViewWalker.GetFirstChildElement(current);
            while (child is not null && remaining-- > 0)
            {
                if (child.CurrentProcessId == process && child.CurrentIsOffscreen == 0 && Contains(child, x, y))
                {
                    var bounds = child.CurrentBoundingRectangle;
                    var childArea = (long)(bounds.right - bounds.left) * (bounds.bottom - bounds.top);
                    if (childArea < area) { best = child; area = childArea; }
                }
                child = automation.RawViewWalker.GetNextSiblingElement(child);
            }
            if (best is null) break;
            current = best;
        }
        return current;
    }

    private static bool Contains(UIA.IUIAutomationElement selected, int x, int y)
    {
        var bounds = selected.CurrentBoundingRectangle;
        return x >= bounds.left && x < bounds.right && y >= bounds.top && y < bounds.bottom;
    }

    private static string Describe(UIA.IUIAutomationElement selected)
    {
        using var owner = Process.GetProcessById(selected.CurrentProcessId);
        var type = selected.CurrentControlType switch
        {
            50003 => "ComboBox", 50004 => "Edit", 50020 => "Text", 50025 => "Custom", 50030 => "Document",
            50032 => "Window", 50033 => "Pane", var id => $"Control {id}"
        };
        var value = Pattern<UIA.IUIAutomationValuePattern>(selected, ValuePatternId);
        var legacy = Pattern<UIA.IUIAutomationLegacyIAccessiblePattern>(selected, LegacyPatternId);
        return $"Detected {owner.ProcessName} / {type} / {selected.CurrentFrameworkId}. ValuePattern: {(value is null ? "unavailable" : value.CurrentIsReadOnly != 0 ? "read-only" : "writable")}; LegacyIAccessible: {(legacy is null ? "unavailable" : "available")}.";
    }

    private WorkerResponse Write(string text)
    {
        Validate();
        var selected = element!;
        var target = identity!;
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Empty updates are blocked.");
        var foreground = NativeMethods.GetForegroundWindow();
        var focusedBefore = automation.GetFocusedElement();
        bool verified;
        if (target.Mechanism.EndsWith("ValuePattern", StringComparison.Ordinal))
        {
            var pattern = Pattern<UIA.IUIAutomationValuePattern>(selected, ValuePatternId);
            if (pattern is null || pattern.CurrentIsReadOnly != 0) throw new InvalidOperationException("The target is no longer writable.");
            pattern.SetValue(text);
            verified = TextConfirmation.Normalize(pattern.CurrentValue) == TextConfirmation.Normalize(text);
        }
        else if (target.Mechanism.EndsWith("LegacyIAccessible", StringComparison.Ordinal))
        {
            var legacy = WritableLegacy(selected) ?? throw new InvalidOperationException("The target no longer exposes writable LegacyIAccessible.");
            legacy.SetValue(text);
            verified = TextConfirmation.Normalize(legacy.CurrentValue) == TextConfirmation.Normalize(text);
        }
        else
        {
            var handle = selected.CurrentNativeWindowHandle;
            if (!NativeEdit.IsWritable(handle, target.ProcessId)) throw new InvalidOperationException("The native field is no longer writable.");
            NativeEdit.SetText(handle, text);
            verified = NativeEdit.Verify(handle, text);
        }
        var after = NativeMethods.GetForegroundWindow();
        var focusedAfter = automation.GetFocusedElement();
        if (focusedAfter is not null && focusedAfter.CurrentProcessId == target.ProcessId &&
            (focusedBefore is null || automation.CompareElements(focusedBefore, focusedAfter) == 0))
            throw new InvalidOperationException("This target moved keyboard focus during the update. It is incompatible with strict background mode; synchronization stopped.");
        if (after != foreground)
        {
            NativeMethods.GetWindowThreadProcessId(after, out var activeProcess);
            if (activeProcess == target.ProcessId)
                throw new InvalidOperationException("This target activated its window during the update. It is incompatible with strict background mode; synchronization stopped.");
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
        if (element.CurrentProcessId != identity.ProcessId || element.CurrentIsPassword != 0 || element.CurrentIsEnabled == 0 ||
            element.CurrentAutomationId != identity.AutomationId || element.CurrentClassName != identity.ClassName ||
            !element.GetRuntimeId().SequenceEqual(identity.RuntimeId))
            throw new InvalidOperationException("The selected field changed identity or became unavailable. Select it again.");
        return new(true, "Target identity verified.", identity);
    }

    private static T? Pattern<T>(UIA.IUIAutomationElement selected, int pattern) where T : class
    {
        try { return selected.GetCurrentPattern(pattern) as T; }
        catch (COMException) { return null; }
    }
    private static UIA.IUIAutomationLegacyIAccessiblePattern? WritableLegacy(UIA.IUIAutomationElement selected)
    {
        var legacy = Pattern<UIA.IUIAutomationLegacyIAccessiblePattern>(selected, LegacyPatternId);
        return legacy is not null && legacy.CurrentRole is 0x2A or 0x2E && (legacy.CurrentState & (0x1 | 0x40 | 0x20000000)) == 0 ? legacy : null;
    }
    private static int GetParentId() => int.TryParse(Environment.GetEnvironmentVariable("LSTS_PARENT_PID"), out var pid) ? pid : -1;
}
