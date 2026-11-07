using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LiveScreenTextSync.App.Capture;
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
        Process? tinySample = null;
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var token = deadline.Token;
            var autoCopy = (CheckBox)window.FindName("AutoCopyInput");
            autoCopy.IsChecked = false;
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

            var tinyPath = infoPath + ".tiny";
            tinySample = Program.StartChild("--sample-source", "--tiny-text", "--fixture-info", tinyPath);
            await WaitUntilAsync(() => File.Exists(tinyPath), token);
            var tinyInfo = JsonSerializer.Deserialize<SampleSourceInfo>(File.ReadAllText(tinyPath))!;
            ((CheckBox)window.FindName("PreprocessInput")).IsChecked = false;
            await window.StartLocalCheckAsync(tinyInfo.Region);
            await WaitUntilAsync(() => preview.Text.Contains("SCREEN TEXT READY", StringComparison.OrdinalIgnoreCase), token);
            results.Add(new { Check = "11-pixel text in a narrow single-line region is enlarged automatically", Passed = true, Width = tinyInfo.Region.Width, Height = tinyInfo.Region.Height });
            File.WriteAllText(tinyPath + ".command", "change");
            await WaitUntilAsync(() => preview.Text.Contains("SCREEN TEXT UPDATED", StringComparison.OrdinalIgnoreCase), token);
            results.Add(new { Check = "Small text changes refresh the local result", Passed = true });
            File.WriteAllText(tinyPath + ".command", "dark");
            await WaitUntilAsync(() => preview.Text.Contains("SMALL DARK TEXT", StringComparison.OrdinalIgnoreCase), token);
            results.Add(new { Check = "Small light text on a dark background is recognized", Passed = true });
            await window.StopLocalCheckAsync();
            tinySample.Kill(entireProcessTree: true);
            await tinySample.WaitForExitAsync(token);

            var languages = OcrLanguages.GetAvailable();
            Require(languages.Any(l => l.Id == "uk-UA") && languages.Any(l => l.Id == "ru+en"), "Additional and mixed OCR languages are missing.");
            var models = languages.Where(l => !string.IsNullOrWhiteSpace(l.Models) && !l.Models.Contains('+')).Select(l => l.Models).Distinct().ToArray();
            Require(models.Length >= 12, "Twelve bundled language models must be present.");
            foreach (var model in models) { using var recognizer = new BundledOcr(model, false); }
            results.Add(new { Check = "All twelve bundled OCR models load; mixed-language choices are available", Passed = true });

            var languageInput = (ComboBox)window.FindName("LanguageInput");
            languageInput.SelectedItem = languages.Single(l => l.Id == "uk-UA");
            File.WriteAllText(infoPath + ".command", "ukrainian");
            await WaitUntilAsync(() => !File.Exists(infoPath + ".command"), token);
            await Task.Delay(150, token);
            await window.StartLocalCheckAsync(info.Region);
            await WaitUntilAsync(() => preview.Text.Contains("ПРИВІТ СВІТ", StringComparison.OrdinalIgnoreCase), token);
            results.Add(new { Check = "Bundled Ukrainian OCR reads Cyrillic text into the app", Passed = true });
            await window.StopLocalCheckAsync();
            languageInput.SelectedItem = languages.Single(l => l.Id == "de-DE");
            File.WriteAllText(infoPath + ".command", "german");
            await WaitUntilAsync(() => !File.Exists(infoPath + ".command"), token);
            await Task.Delay(150, token);
            await window.StartLocalCheckAsync(info.Region);
            await WaitUntilAsync(() => preview.Text.Contains("München", StringComparison.OrdinalIgnoreCase), token);
            results.Add(new { Check = "Bundled German OCR preserves accented text", Passed = true });

            var clipboard = window.TestClipboard ?? throw new InvalidOperationException("Diagnostics must isolate the user's real clipboard.");
            Require(clipboard.Writes.Count == 0, "Auto-copy off must leave the clipboard untouched.");
            autoCopy.IsChecked = true;
            Require(clipboard.Writes.LastOrDefault() == preview.Text, "Enabling auto-copy must copy the current result.");
            results.Add(new { Check = "Auto-copy can be enabled while capturing and copies the current text", Passed = true });
            var copies = clipboard.Writes.Count;
            File.WriteAllText(infoPath + ".command", "german2");
            await WaitUntilAsync(() => clipboard.Writes.Count > copies && clipboard.Writes.Last().Contains("Wörter", StringComparison.OrdinalIgnoreCase), token);
            Require(clipboard.Writes.Last() == preview.Text, "Auto-copy must match the latest result.");
            results.Add(new { Check = "Updated recognition is automatically copied", Passed = true });
            autoCopy.IsChecked = false;
            copies = clipboard.Writes.Count;
            File.WriteAllText(infoPath + ".command", "german");
            await WaitUntilAsync(() => preview.Text.Contains("München", StringComparison.OrdinalIgnoreCase), token);
            Require(clipboard.Writes.Count == copies, "Disabling auto-copy must stop clipboard updates immediately.");
            results.Add(new { Check = "Disabling auto-copy stops further writes while capture continues", Passed = true });
            await window.StopLocalCheckAsync();
            languageInput.SelectedItem = languages.Single(l => l.Id == "ru+en");
            File.WriteAllText(infoPath + ".command", "mixed");
            await WaitUntilAsync(() => !File.Exists(infoPath + ".command"), token);
            await Task.Delay(150, token);
            await window.StartLocalCheckAsync(info.Region);
            await WaitUntilAsync(() => preview.Text.Contains("Hello", StringComparison.OrdinalIgnoreCase) && preview.Text.Contains("Привет", StringComparison.OrdinalIgnoreCase), token);
            results.Add(new { Check = "Russian + English mode recognizes both scripts in one line", Passed = true });
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
            foreach (var fixture in new[] { sample, tinySample })
            {
                if (fixture is null) continue;
                if (!fixture.HasExited) fixture.Kill(entireProcessTree: true);
                fixture.Dispose();
            }
            foreach (var suffix in new[] { "", ".command", ".tiny", ".tiny.command" })
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
