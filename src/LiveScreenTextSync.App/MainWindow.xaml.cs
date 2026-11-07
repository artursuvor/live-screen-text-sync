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
    private readonly IClipboardWriter clipboard;
    private readonly AutoCopyController autoCopy;
    internal RecordingClipboardWriter? TestClipboard => clipboard as RecordingClipboardWriter;
    private ScreenRegion? region;
    private SourceOutline? outline;
    private string? sourceTopology;
    private GlobalHotkeys? hotkeys;
    private CancellationTokenSource? runCancellation;
    private Task? running;
    private TextConfirmation? confirmation;
    private string state = "Idle";
    private bool closing, busy, settingsReady;
    private int frameCount, updateCount;

    public MainWindow()
    {
        InitializeComponent();
        clipboard = Program.IsDiagnostic ? new RecordingClipboardWriter() : new ClipboardWriter();
        autoCopy = new(clipboard);
        Title = $"Live Screen Text Sync · v{typeof(App).Assembly.GetName().Version?.ToString(3)}";
        var loaded = SettingsStore.Load();
        IntervalInput.Text = loaded.IntervalMs.ToString();
        PreprocessInput.IsChecked = loaded.Preprocess;
        AutoCopyInput.IsChecked = loaded.AutoCopy;
        autoCopy.SetEnabled(loaded.AutoCopy);
        ClipboardStatusLabel.Text = loaded.AutoCopy ? "On · each text change" : "Off";
        try
        {
            var languages = OcrLanguages.GetAvailable().ToArray();
            LanguageInput.ItemsSource = languages;
            LanguageInput.SelectedItem = languages.FirstOrDefault(l => loaded.Language is not null && (l.Id == loaded.Language || l.WindowsTag == loaded.Language)) ??
                languages.FirstOrDefault(l => l.Id == "en-US") ?? languages.FirstOrDefault();
            if (languages.Length == 0) StatusLabel.Text = "No OCR languages are available. Extract the full portable package including tessdata.";
        }
        catch (Exception ex)
        {
            Log.Warning("OCR language discovery failed: {ErrorType}", ex.GetType().Name);
            StatusLabel.Text = "OCR languages are unavailable. Extract the complete portable package.";
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
        metrics.Tick += (_, _) => { FramesLabel.Text = $"{Volatile.Read(ref frameCount):N0} frames"; TryAutoCopy(); };
        metrics.Start();
        Closing += OnClosing;
        settingsReady = true;
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

    private async Task StartRunAsync()
    {
        if (busy || running is { IsCompleted: false }) return;
        if (region is null || !Monitors.IsValid(region) || sourceTopology != Monitors.Fingerprint())
        { SetStatus("Error", "The source or display layout changed. Select the source again."); return; }
        if (!int.TryParse(IntervalInput.Text, out var interval) || interval is < 100 or > 5000)
        { SetStatus("Error", "Capture interval must be a whole number from 100 to 5000 ms."); return; }
        if (LanguageInput.SelectedItem is not OcrLanguage language)
        { SetStatus("Error", "Select an OCR language first."); return; }
        if (Math.Max(region.Width, region.Height) > Windows.Media.Ocr.OcrEngine.MaxImageDimension)
        { SetStatus("Error", $"Select a smaller source. OCR supports dimensions up to {Windows.Media.Ocr.OcrEngine.MaxImageDimension} pixels."); return; }
        var resuming = state == "Paused";
        var enhance = PreprocessInput.IsChecked == true;
        busy = true;
        runCancellation?.Dispose();
        runCancellation = new();
        var token = runCancellation.Token;
        DesktopCapture? capture = null;
        ITextRecognizer? ocr = null;
        SetStatus("Preparing", $"Loading {language.Display} OCR…");
        try
        {
            capture = new DesktopCapture(region);
            ocr = await Task.Run(() => OcrLanguages.Create(language, enhance), CancellationToken.None);
            token.ThrowIfCancellationRequested();
            if (!resuming) confirmation = new(false);
            var writer = new LocalWriter(text => _ = Dispatcher.BeginInvoke(() => DisplayText(text, token)));
            var engine = new SyncEngine(capture, ocr, writer, new(interval, false), confirmation);
            engine.Updated += update =>
            {
                if (update.Kind == SyncEventKind.Capture) Interlocked.Increment(ref frameCount);
                else if (update.Kind == SyncEventKind.OcrError)
                    _ = Dispatcher.BeginInvoke(() => { if (!token.IsCancellationRequested) StatusLabel.Text = "OCR failed. Keeping the last captured text and retrying."; });
                else if (update.Kind == SyncEventKind.Preview && string.IsNullOrWhiteSpace(update.Text))
                    _ = Dispatcher.BeginInvoke(() => { if (!token.IsCancellationRequested) StatusLabel.Text = "No text detected. Include the complete letters and a little space around them; check the OCR language. Keeping the last captured text."; });
            };
            outline?.Hide();
            SetStatus("Capturing", "Reading the source into this window. Use Copy Text whenever you need it.");
            Log.Information("Local capture started. Interval {IntervalMs}", interval);
            SaveSettings();
            var sessionCapture = capture;
            var sessionOcr = ocr;
            running = Task.Run(async () => { using (sessionCapture) using (sessionOcr as IDisposable) await engine.RunAsync(token); }, CancellationToken.None);
            capture = null;
            ocr = null;
            _ = ObserveRunAsync(running, token);
            UpdateControls();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { ReportError(ex); }
        finally { capture?.Dispose(); (ocr as IDisposable)?.Dispose(); busy = false; UpdateControls(); }
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
        autoCopy.Queue(text);
        TryAutoCopy();
        UpdateControls();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PreviewText.Text)) return;
        if (clipboard.TryWrite(PreviewText.Text))
        {
            autoCopy.CancelPending();
            ClipboardStatusLabel.Text = autoCopy.Enabled ? $"Copied {DateTime.Now:HH:mm:ss}" : "Off";
            StatusLabel.Text = "Text copied to the clipboard.";
        }
        else StatusLabel.Text = "The clipboard is busy. Try Copy Text again.";
    }

    private void AutoCopy_Changed(object sender, RoutedEventArgs e)
    {
        if (!settingsReady) return;
        autoCopy.SetEnabled(AutoCopyInput.IsChecked == true);
        ClipboardStatusLabel.Text = autoCopy.Enabled ? "On · each text change" : "Off";
        if (autoCopy.Enabled) { autoCopy.Queue(PreviewText.Text); TryAutoCopy(); }
        SaveSettings();
    }

    private void TryAutoCopy()
    {
        if (closing) return;
        var result = autoCopy.TryCopyPending();
        if (result == ClipboardCopyResult.Copied)
        {
            ClipboardStatusLabel.Text = $"Copied {DateTime.Now:HH:mm:ss}";
            StatusLabel.Text = "Text captured and copied to the clipboard.";
        }
        else if (result == ClipboardCopyResult.Busy) ClipboardStatusLabel.Text = "Clipboard busy · retrying latest text";
    }

    private void Language_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!settingsReady) return;
        UpdateControls();
        SaveSettings();
    }

    private async void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (state == "Paused") { await StartRunAsync(); return; }
        await StopRunAsync("Paused. The captured text is still available to copy.", "Paused");
    }
    private async void Stop_Click(object sender, RoutedEventArgs e) => await StopRunAsync("Stopped. The captured text is still available to copy.");

    private async Task StopRunAsync(string? message = null, string? nextState = null)
    {
        autoCopy.CancelPending();
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
        var active = state is "Capturing" or "Preparing" || running is { IsCompleted: false };
        SourceButton.IsEnabled = !busy && !active;
        SettingsPanel.IsEnabled = !busy && !active;
        StartButton.IsEnabled = !busy && !active && region is not null && LanguageInput.SelectedItem is not null;
        PauseButton.IsEnabled = !busy && (state == "Capturing" || state == "Paused");
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
                AutoCopy = autoCopy.Enabled, Language = (LanguageInput.SelectedItem as OcrLanguage)?.Id, Source = region, DisplayTopology = sourceTopology
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
