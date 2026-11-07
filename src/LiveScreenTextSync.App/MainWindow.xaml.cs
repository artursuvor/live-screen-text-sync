using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LiveScreenTextSync.App.Capture;
using LiveScreenTextSync.App.Services;
using LiveScreenTextSync.App.Views;
using LiveScreenTextSync.Core;
using Serilog;

namespace LiveScreenTextSync.App;

public partial class MainWindow : Window
{
    private sealed record LanguageOption(string Tag, string Display);
    private sealed class LocalWriter(Action<string> display) : ITextWriter
    {
        public Task WriteAsync(string text, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            display(text);
            return Task.CompletedTask;
        }
    }

    private readonly DispatcherTimer metrics = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private ScreenRegion? region;
    private SourceOutline? outline;
    private string? sourceTopology;
    private GlobalHotkeys? hotkeys;
    private CancellationTokenSource? runCancellation;
    private Task? running;
    private TextConfirmation? confirmation;
    private string state = "Idle";
    private bool closing, busy;
    private int frameCount, updateCount;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"Live Screen Text Sync · v{typeof(App).Assembly.GetName().Version?.ToString(3)}";
        var loaded = SettingsStore.Load();
        IntervalInput.Text = loaded.IntervalMs.ToString();
        PreprocessInput.IsChecked = loaded.Preprocess;
        try
        {
            var languages = WindowsOcr.Languages.Select(l => new LanguageOption(l.LanguageTag,
                $"{System.Globalization.CultureInfo.GetCultureInfo(l.LanguageTag).EnglishName} ({l.LanguageTag})")).ToArray();
            LanguageInput.ItemsSource = languages;
            LanguageInput.SelectedItem = languages.FirstOrDefault(l => l.Tag == loaded.Language) ??
                languages.FirstOrDefault(l => l.Tag.StartsWith("en", StringComparison.OrdinalIgnoreCase)) ?? languages.FirstOrDefault();
            if (languages.Length == 0) StatusLabel.Text = "Install a Windows OCR language in Settings > Time & language > Language & region.";
        }
        catch (Exception ex)
        {
            Log.Warning("OCR language discovery failed: {ErrorType}", ex.GetType().Name);
            StatusLabel.Text = "Windows OCR is unavailable. Check installed OCR language features.";
        }
        if (Program.RenderPath is null && loaded.Source is { } saved && loaded.DisplayTopology == Monitors.Fingerprint() && Monitors.IsValid(saved))
            SetSource(saved);
        SourceInitialized += (_, _) =>
        {
            hotkeys = new(this);
            hotkeys.StopPressed += async () => await StopRunAsync("Capture stopped using the global hotkey.");
            if (!hotkeys.StopRegistered)
                HotkeyLabel.Text = "Global shortcut unavailable (another copy may be running). Use Stop in this window. Keep the source visible.";
        };
        metrics.Tick += (_, _) => FramesLabel.Text = $"{Volatile.Read(ref frameCount):N0} frames";
        metrics.Start();
        Closing += OnClosing;
        UpdateControls();
    }

    private void SetSource(ScreenRegion source)
    {
        region = source;
        sourceTopology = Monitors.Fingerprint();
        SourceTitle.Text = $"{source.Width:N0} × {source.Height:N0} px";
        SourceDetail.Text = $"{source.Monitor}  ·  X {source.X}, Y {source.Y}\nKeep this area visible while capturing.";
        SetStatus("Ready", "Source selected. Press Start Capture to read text into this window.");
    }

    private async void SelectSource_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        await StopRunAsync();
        busy = true;
        UpdateControls();
        outline?.Hide();
        Hide();
        try
        {
            var selected = await RegionPicker.PickAsync();
            if (selected is not null) { SetSource(selected); SaveSettings(); }
        }
        catch (Exception ex) { ReportError(ex); }
        finally { Show(); ShowOutline(); busy = false; UpdateControls(); }
    }

    private void ShowOutline()
    {
        outline?.Close();
        outline = null;
        if (region is null || closing || !Monitors.IsValid(region) || Program.LocalCheckReportPath is not null) return;
        outline = new SourceOutline(region);
        outline.Show();
    }

    private async void Start_Click(object sender, RoutedEventArgs e) => await StartRunAsync();

    private Task StartRunAsync()
    {
        if (busy || running is { IsCompleted: false }) return Task.CompletedTask;
        if (region is null || !Monitors.IsValid(region) || sourceTopology != Monitors.Fingerprint())
        { SetStatus("Error", "The source or display layout changed. Select the source again."); return Task.CompletedTask; }
        if (!int.TryParse(IntervalInput.Text, out var interval) || interval is < 100 or > 5000)
        { SetStatus("Error", "Capture interval must be a whole number from 100 to 5000 ms."); return Task.CompletedTask; }
        if (LanguageInput.SelectedItem is not LanguageOption language)
        { SetStatus("Error", "Install and select a Windows OCR language first."); return Task.CompletedTask; }
        if (Math.Max(region.Width, region.Height) > Windows.Media.Ocr.OcrEngine.MaxImageDimension)
        { SetStatus("Error", $"Select a smaller source. OCR supports dimensions up to {Windows.Media.Ocr.OcrEngine.MaxImageDimension} pixels."); return Task.CompletedTask; }
        try
        {
            var capture = new DesktopCapture(region);
            WindowsOcr ocr;
            try { ocr = new(language.Tag, PreprocessInput.IsChecked == true); }
            catch { capture.Dispose(); throw; }
            if (state != "Paused") confirmation = new(false);
            runCancellation?.Dispose();
            runCancellation = new();
            var token = runCancellation.Token;
            var writer = new LocalWriter(text => _ = Dispatcher.BeginInvoke(() => DisplayText(text, token)));
            var engine = new SyncEngine(capture, ocr, writer, new(interval, false), confirmation);
            engine.Updated += update =>
            {
                if (update.Kind == SyncEventKind.Capture) Interlocked.Increment(ref frameCount);
                else if (update.Kind == SyncEventKind.OcrError)
                    _ = Dispatcher.BeginInvoke(() => { if (!token.IsCancellationRequested) StatusLabel.Text = "OCR failed. Keeping the last captured text and retrying."; });
                else if (update.Kind == SyncEventKind.Preview && string.IsNullOrWhiteSpace(update.Text))
                    _ = Dispatcher.BeginInvoke(() => { if (!token.IsCancellationRequested) StatusLabel.Text = "No text detected. Keeping the last captured text."; });
            };
            outline?.Hide();
            SetStatus("Capturing", "Reading the source into this window. Use Copy Text whenever you need it.");
            Log.Information("Local capture started. Interval {IntervalMs}", interval);
            SaveSettings();
            running = Task.Run(async () => { using (capture) await engine.RunAsync(token); }, CancellationToken.None);
            _ = ObserveRunAsync(running, token);
            UpdateControls();
        }
        catch (Exception ex) { ReportError(ex); }
        return Task.CompletedTask;
    }

    private async Task ObserveRunAsync(Task task, CancellationToken token)
    {
        try { await task; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { if (!closing) ReportError(ex); }
        finally { UpdateControls(); }
    }

    private void DisplayText(string text, CancellationToken token)
    {
        if (token.IsCancellationRequested || state != "Capturing" || string.IsNullOrWhiteSpace(text)) return;
        PreviewText.Text = text;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        UpdatesLabel.Text = $"{++updateCount:N0} text changes";
        LastReadLabel.Text = $"Read {DateTime.Now:HH:mm:ss}";
        StatusLabel.Text = "Text captured. Select it here or click Copy Text.";
        UpdateControls();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PreviewText.Text)) return;
        try { Clipboard.SetText(PreviewText.Text); StatusLabel.Text = "Text copied to the clipboard."; }
        catch (System.Runtime.InteropServices.ExternalException)
        { StatusLabel.Text = "The clipboard is busy. Try Copy Text again."; }
    }

    private async void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (state == "Paused") { await StartRunAsync(); return; }
        await StopRunAsync("Paused. The captured text is still available to copy.", "Paused");
    }
    private async void Stop_Click(object sender, RoutedEventArgs e) => await StopRunAsync("Stopped. The captured text is still available to copy.");

    private async Task StopRunAsync(string? message = null, string? nextState = null)
    {
        if (runCancellation is not null) await runCancellation.CancelAsync();
        if (running is { } task)
        {
            try { await task; }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }
        confirmation?.Reject();
        running = null;
        if (!closing) ShowOutline();
        SetStatus(nextState ?? (region is not null ? "Ready" : "Idle"), message ?? "Select a source or press Start Capture.");
    }

    private void SetStatus(string value, string message)
    {
        state = value;
        StateLabel.Text = $"●  {value}";
        StateLabel.Foreground = new SolidColorBrush(value == "Error" ? Color.FromRgb(255, 177, 151) : Color.FromRgb(142, 229, 194));
        StatusLabel.Text = message;
        UpdateControls();
    }
    private void UpdateControls()
    {
        var active = state == "Capturing" || running is { IsCompleted: false };
        SourceButton.IsEnabled = !busy && !active;
        SettingsPanel.IsEnabled = !busy && !active;
        StartButton.IsEnabled = !busy && !active && region is not null && LanguageInput.SelectedItem is not null;
        PauseButton.IsEnabled = !busy && (active || state == "Paused");
        PauseButton.Content = state == "Paused" ? "Resume" : "Pause";
        StopButton.IsEnabled = active || state == "Paused";
        CopyButton.IsEnabled = !string.IsNullOrWhiteSpace(PreviewText.Text);
    }
    private void ReportError(Exception ex)
    {
        Log.Warning("Capture failed: {ErrorType}", ex.GetType().Name);
        SetStatus("Error", ex is InvalidOperationException or ArgumentException ? ex.Message : "Capture failed. Check the source and OCR language, then try again.");
    }
    public async Task HandleUnexpectedErrorAsync()
    {
        await StopRunAsync();
        SetStatus("Error", "An unexpected error stopped capture. The last captured text is still available.");
    }
    private void SaveSettings()
    {
        if (Program.IsDiagnostic) return;
        if (!int.TryParse(IntervalInput.Text, out var interval) || interval is < 100 or > 5000) interval = 200;
        try
        {
            SettingsStore.Save(new()
            {
                IntervalMs = interval, Preprocess = PreprocessInput.IsChecked == true,
                Language = (LanguageInput.SelectedItem as LanguageOption)?.Tag, Source = region, DisplayTopology = sourceTopology
            });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        { Log.Warning("Settings could not be saved: {ErrorType}", ex.GetType().Name); }
    }
    private void SampleText_Click(object sender, RoutedEventArgs e)
    {
        try { Program.StartChild("--sample-source"); }
        catch (Exception ex) { ReportError(ex); }
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        e.Cancel = true;
        closing = true;
        IsEnabled = false;
        await StopRunAsync();
        SaveSettings();
        outline?.Close();
        hotkeys?.Dispose();
        runCancellation?.Dispose();
        metrics.Stop();
        _ = Dispatcher.BeginInvoke(Close);
    }

    internal Task StartLocalCheckAsync(ScreenRegion source) { SetSource(source); return StartRunAsync(); }
    internal Task StopLocalCheckAsync() => StopRunAsync();
}
