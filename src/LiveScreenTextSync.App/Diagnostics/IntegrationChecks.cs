using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using LiveScreenTextSync.App.Automation;
using LiveScreenTextSync.App.Capture;
using LiveScreenTextSync.App.Interop;
using LiveScreenTextSync.App.Services;
using LiveScreenTextSync.App.Views;
using LiveScreenTextSync.Core;

namespace LiveScreenTextSync.App.Diagnostics;

public static class IntegrationChecks
{
    public static int Run(string reportPath)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            var results = new List<object>();
            Process? fixture = null;
            Window? witness = null;
            var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath))!;
            Directory.CreateDirectory(directory);
            var infoPath = Path.Combine(directory, $"fixture-{Guid.NewGuid():N}.json");
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                var token = deadline.Token;
                fixture = Program.StartChild("--test-target", "--fixture-info", infoPath);
                await WaitUntilAsync(() => File.Exists(infoPath), token);
                var info = JsonSerializer.Deserialize<FixtureInfo>(File.ReadAllText(infoPath))!;
                await Task.Delay(250, token);
                using var automation = new AutomationClient();
                var initial = ReadState(infoPath);
                var inspected = await automation.InspectAsync(info.Target.X, info.Target.Y, token);
                Require(inspected.Success, "Editable target inspection: " + inspected.Message);
                Require(inspected.Target?.ProcessId == fixture.Id && inspected.Target.AutomationId == "FixtureEditableTarget", "Fixture point resolved to an unexpected application or field.");
                Require(ReadState(infoPath).Text == initial.Text && ReadState(infoPath).Changes == initial.Changes, "Inspection must not modify the target.");
                results.Add(new { Check = "Target inspection preserves contents", Passed = true, inspected.Target!.Mechanism });

                foreach (var field in new[] { ("Read-only target rejected", info.ReadOnly), ("Password target rejected", info.Password) })
                {
                    var rejected = await automation.InspectAsync(field.Item2.X, field.Item2.Y, token);
                    Require(!rejected.Success, field.Item1);
                    results.Add(new { Check = field.Item1, Passed = true });
                }

                var nativeInspected = await automation.InspectAsync(info.Native.X, info.Native.Y, token);
                Require(nativeInspected.Success, "Native field inspection failed.");
                Require(nativeInspected.Target?.ProcessId == fixture.Id, "Native fixture point resolved to another application.");
                results.Add(new { Check = "Native target identity", Passed = true, nativeInspected.Target!.Mechanism, nativeInspected.Target.ClassName, nativeInspected.Target.AutomationId });
                Require(NativeEdit.Verify((nint)info.NativeHandle, "Native edit test"), "Native inspection changed contents.");
                // This second window is part of the test setup; production synchronization never activates windows.
                var monitor = Monitors.GetAll().First(m => m.Contains(info.Source));
                witness = new Window
                {
                    Title = "Integration check — focus witness", Width = 270, Height = 130,
                    Left = (monitor.X + monitor.Width) * 96.0 / monitor.Dpi - 300, Top = monitor.Y * 96.0 / monitor.Dpi + 40,
                    Content = new TextBox { Text = "Keyboard focus must stay here", Margin = new Thickness(12) },
                    Background = Brushes.White, Topmost = true
                };
                witness.Show();
                witness.Activate();
                ((TextBox)witness.Content).Focus();
                await Task.Delay(500, token);
                var foreground = NativeMethods.GetForegroundWindow();
                Require(foreground != 0, "The interactive desktop has no foreground window for the focus check.");
                var keyboard = System.Windows.Automation.AutomationElement.FocusedElement.GetRuntimeId();
                Require(NativeMethods.GetForegroundWindow() == foreground, "Reading the current focus changed the foreground window.");
                await Task.Delay(500, token);
                Require(NativeMethods.GetForegroundWindow() == foreground, "Foreground changed while waiting before the write.");
                NativeEdit.SetText((nint)info.NativeHandle, "Direct native focus check");
                Require(NativeMethods.GetForegroundWindow() == foreground, "Direct WM_SETTEXT changed the foreground window.");
                results.Add(new { Check = "Direct WM_SETTEXT preserves foreground", Passed = true });
                await automation.WriteAsync("Background write ✓\nSecond line", token);
                Require(NativeMethods.GetForegroundWindow() == foreground, $"Foreground window changed during synchronization. Before {foreground}, after {NativeMethods.GetForegroundWindow()}, fixture {fixture.MainWindowHandle}.");
                Require(System.Windows.Automation.AutomationElement.FocusedElement.GetRuntimeId().SequenceEqual(keyboard), "Keyboard focus changed during synchronization.");
                Require(NativeEdit.Verify((nint)info.NativeHandle, "Background write ✓\nSecond line"), "Unicode multiline readback.");
                results.Add(new { Check = "Background Unicode multiline write preserves foreground and keyboard focus", Passed = true });

                using (var capture = new DesktopCapture(info.Source))
                {
                    var language = WindowsOcr.Languages.FirstOrDefault(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException("Install an English OCR language for the integration fixture.");
                    var recognizer = new WindowsOcr(language.LanguageTag, false);
                    var frame = await capture.CaptureAsync(token);
                    var text = await recognizer.RecognizeAsync(frame, token);
                    Require(text.Contains("SCREEN SYNC READY", StringComparison.OrdinalIgnoreCase), "Local OCR did not recognize the fixture source.");
                    results.Add(new { Check = "Visible-region capture and Windows OCR", Passed = true, Language = language.LanguageTag });
                    var enhanced = await new WindowsOcr(language.LanguageTag, true).RecognizeAsync(frame, token);
                    Require(enhanced.Contains("SCREEN SYNC READY", StringComparison.OrdinalIgnoreCase), "Enhanced OCR did not recognize the fixture source.");
                    results.Add(new { Check = "OCR preprocessing", Passed = true });

                    using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
                    var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var duplicateSkipped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var writes = 0;
                    var ocrs = 0;
                    var frames = 0;
                    var engine = new SyncEngine(capture, recognizer, automation, new(200));
                    engine.Updated += e =>
                    {
                        if (e.Kind == SyncEventKind.Capture) Interlocked.Increment(ref frames);
                        if (e.Kind == SyncEventKind.Preview) Interlocked.Increment(ref ocrs);
                        if (e.Kind == SyncEventKind.Written)
                        {
                            if (Interlocked.Increment(ref writes) == 1) updated.TrySetResult();
                            else second.TrySetResult();
                        }
                        if (e.Kind == SyncEventKind.Skipped && Volatile.Read(ref writes) > 0) duplicateSkipped.TrySetResult();
                    };
                    var watch = Stopwatch.StartNew();
                    var running = Task.Run(() => engine.RunAsync(stop.Token), CancellationToken.None);
                    try
                    {
                        await updated.Task.WaitAsync(token);
                        var firstLatency = watch.ElapsedMilliseconds;
                        await duplicateSkipped.Task.WaitAsync(token);
                        Require(writes == 1, "Unchanged source produced a duplicate write.");
                        await Task.Delay(450, token);
                        Require(writes == 1, "Unchanged source mutated the target.");
                        File.WriteAllText(infoPath + ".command", "change");
                        await second.Task.WaitAsync(token);
                        Require(NativeEdit.Verify((nint)info.NativeHandle, "SCREEN SYNC UPDATED"), "Changed source was not synchronized.");
                        Require(NativeMethods.GetForegroundWindow() == foreground, "Pipeline changed foreground window.");
                        results.Add(new { Check = "End-to-end capture → confirmed OCR → background update", Passed = true, FirstUpdateMs = firstLatency, Frames = frames, OcrCalls = ocrs, Writes = writes });
                    }
                    finally
                    {
                        await stop.CancelAsync();
                        try { await running; } catch (OperationCanceledException) { }
                    }
                }

                Require(NativeEdit.IsWritable((nint)info.NativeHandle, info.ProcessId), "Native edit allowlist validation failed.");
                NativeEdit.SetText((nint)info.NativeHandle, "Native background ✓\r\nSecond line");
                Require(NativeEdit.Verify((nint)info.NativeHandle, "Native background ✓\nSecond line"), "WM_SETTEXT readback mismatch.");
                Require(NativeMethods.GetForegroundWindow() == foreground, "Native update changed foreground.");
                results.Add(new { Check = "Bounded native WM_SETTEXT with Unicode readback", Passed = true });

                var msaa = NativeEdit.GetAccessible((nint)info.NativeHandle);
                Require(msaa is not null, "Native edit does not expose MSAA.");
                msaa!.set_accValue(0, "MSAA background update");
                Require(NativeEdit.Verify((nint)info.NativeHandle, "MSAA background update"), "MSAA readback mismatch.");
                results.Add(new { Check = "MSAA IAccessible write", Passed = true });

                var wpf = await automation.InspectAsync(info.Target.X, info.Target.Y, token);
                Require(wpf.Success && wpf.Target?.ProcessId == fixture.Id && wpf.Target.AutomationId == "FixtureEditableTarget", "WPF compatibility check resolved to another target.");
                try
                {
                    await automation.WriteAsync("WPF provider compatibility check", token);
                    results.Add(new { Check = "WPF ValuePattern update preserves foreground", Passed = true });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("activated its window", StringComparison.Ordinal))
                {
                    Require(!automation.HasTarget, "A focus-changing provider must be invalidated.");
                    results.Add(new { Check = "Focus-changing WPF provider is detected and invalidated", Passed = true, Limitation = "On this Windows build, UIA SetValue activates the WPF target. Use the native edit target for strict background mode." });
                }
                var hangTarget = await automation.InspectAsync(info.Native.X, info.Native.Y, token);
                Require(hangTarget.Success && hangTarget.Target?.ProcessId == fixture.Id, "Hang check resolved to another target.");

                var hotkeyWindow = new Window { Width = 1, Height = 1, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Opacity = 0 };
                hotkeyWindow.Show();
                using (var keys = new GlobalHotkeys(hotkeyWindow))
                {
                    Require(keys.StopRegistered, "Emergency stop hotkey registration failed.");
                    Require(keys.BeginPicking(), "Target picker hotkey registration failed.");
                    keys.EndPicking();
                    results.Add(new { Check = "Global stop and target-selection hotkey registration", Passed = true });
                }
                hotkeyWindow.Close();

                File.WriteAllText(infoPath + ".command", "hang");
                await Task.Delay(250, token);
                var timeout = Stopwatch.StartNew();
                var failed = false;
                try { await automation.WriteAsync("Must not hang the controller", token); }
                catch (InvalidOperationException) { failed = true; }
                Require(failed && timeout.Elapsed < TimeSpan.FromSeconds(5) && !automation.HasTarget, "Hung target did not invalidate the worker within the deadline.");
                results.Add(new { Check = "Hung target is isolated, bounded, and invalidated", Passed = true, ElapsedMs = timeout.ElapsedMilliseconds });
                fixture.Kill(entireProcessTree: true);
                await fixture.WaitForExitAsync(token);
                var afterClose = await automation.RequestAsync(new("validate"), token);
                Require(!afterClose.Success && !automation.HasTarget, "Closed target must not be rebound.");
                results.Add(new { Check = "Closed target is rejected without automatic reselection", Passed = true });
                File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Success = true, Checks = results }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Success = false, Error = ex.ToString(), Checks = results }, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(1);
            }
            finally
            {
                witness?.Close();
                if (fixture is not null)
                {
                    if (!fixture.HasExited) fixture.Kill(entireProcessTree: true);
                    fixture.Dispose();
                }
                foreach (var suffix in new[] { "", ".state", ".command" })
                    if (File.Exists(infoPath + suffix)) File.Delete(infoPath + suffix);
            }
        };
        return app.Run();
    }

    private static FixtureState ReadState(string path) => JsonSerializer.Deserialize<FixtureState>(File.ReadAllText(path + ".state"))!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition()) await Task.Delay(100, token);
    }
}
