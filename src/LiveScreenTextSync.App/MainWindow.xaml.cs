using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LiveScreenTextSync.App.Automation;
using LiveScreenTextSync.App.Capture;
using LiveScreenTextSync.App.Interop;
using LiveScreenTextSync.App.Services;
using LiveScreenTextSync.App.Views;
using LiveScreenTextSync.Core;
using Serilog;

namespace LiveScreenTextSync.App;

public partial class MainWindow : Window
{
    private sealed record LanguageOption(string Tag, string Display);
    private sealed class PreviewWriter : ITextWriter
    {
        public Task WriteAsync(string text, CancellationToken token) => Task.CompletedTask;
    }
    private readonly AutomationClient automation = new();
    private readonly DispatcherTimer health = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer metrics = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly UserSettings loaded;
    private ScreenRegion? region;
    private string? sourceTopology;
    private GlobalHotkeys? hotkeys;
    private CancellationTokenSource? runCancellation;
    private Task? running;
    private TextConfirmation? confirmation;
    private string state = "Idle";
    private bool previewOnly, picking, closing, busy, checkingHealth;
    private int frameCount, writeCount;

    public MainWindow()
    {
        InitializeComponent();
        loaded = SettingsStore.Load();
        IntervalInput.Text = loaded.IntervalMs.ToString();
        ConfirmInput.IsChecked = loaded.ConfirmTwice;
        PreprocessInput.IsChecked = loaded.Preprocess;
        try
        {
            var languages = WindowsOcr.Languages.Select(l => new LanguageOption(l.LanguageTag, $"{l.DisplayName} ({l.LanguageTag})")).ToArray();
            LanguageInput.ItemsSource = languages;
            LanguageInput.SelectedItem = languages.FirstOrDefault(l => l.Tag == loaded.Language) ??
                languages.FirstOrDefault(l => l.Tag.StartsWith("en", StringComparison.OrdinalIgnoreCase)) ?? languages.FirstOrDefault();
            if (languages.Length == 0) StatusLabel.Text = "No Windows OCR languages are installed. Add an OCR language feature in Windows Settings.";
        }
        catch (Exception ex)
        {
            Log.Warning("OCR language discovery failed: {ErrorType}", ex.GetType().Name);
            StatusLabel.Text = "Windows OCR is unavailable. Check installed Windows OCR language features.";
        }
        if (loaded.Source is { } saved && loaded.DisplayTopology == Monitors.Fingerprint() && Monitors.IsValid(saved))
        {
            region = saved;
            sourceTopology = loaded.DisplayTopology;
            ShowSource();
        }
        SourceInitialized += (_, _) =>
        {
            hotkeys = new(this);
            hotkeys.StopPressed += async () => await StopRunAsync("Stopped using the global hotkey.");
            hotkeys.PickPressed += async () => await FinishPickingAsync();
            hotkeys.CancelPressed += CancelPicking;
            if (!hotkeys.StopRegistered)
            {
                HotkeyLabel.Text = "Emergency stop hotkey is in use. Release Ctrl + Alt + Shift + S and restart this app.";
                SetStatus("Error", "Global stop hotkey could not be registered. Synchronization is disabled.");
            }
            UpdateControls();
        };
        health.Tick += async (_, _) => await CheckHealthAsync();
        metrics.Tick += (_, _) => FramesLabel.Text = $"{Volatile.Read(ref frameCount):N0} frames";
        metrics.Start();
        Closing += OnClosing;
        UpdateControls();
    }

    private async void SelectSource_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        await StopRunAsync();
        CancelPicking();
        busy = true;
        UpdateControls();
        Hide();
        try
        {
            var selected = await RegionPicker.PickAsync();
            if (selected is not null)
            {
                region = selected;
                sourceTopology = Monitors.Fingerprint();
                ShowSource();
                SetStatus(automation.HasTarget ? "Ready" : "Idle", "Source selected. Keep this area visible while synchronizing.");
                SaveSettings();
            }
        }
        catch (Exception ex) { ReportError(ex); }
        finally { Show(); busy = false; UpdateControls(); }
    }

    private void ShowSource()
    {
        if (region is null) return;
        SourceTitle.Text = $"{region.Width:N0} × {region.Height:N0} px";
        SourceDetail.Text = $"{region.Monitor}  ·  X {region.X}, Y {region.Y}\nVisible desktop capture";
    }

    private async void SelectTarget_Click(object sender, RoutedEventArgs e)
    {
        if (picking) { CancelPicking(); return; }
        if (busy) return;
        await StopRunAsync();
        if (hotkeys?.BeginPicking() != true)
        {
            SetStatus("Error", "F8 is already registered by another application. Release it and try again.");
            return;
        }
        picking = true;
        TargetButton.Content = "Cancel Selection";
        SetStatus("Idle", "Hover over the target text field and press F8. Press Esc or Cancel Selection to cancel. The field will not be changed.");
    }

    private void CancelPicking()
    {
        if (!picking) return;
        picking = false;
        hotkeys?.EndPicking();
        TargetButton.Content = "Select Target";
        SetStatus(automation.HasTarget && region is not null ? "Ready" : "Idle", "Target selection cancelled.");
    }

    private async Task FinishPickingAsync()
    {
        if (!picking || busy) return;
        NativeMethods.GetCursorPos(out var point);
        picking = false;
        hotkeys?.EndPicking();
        TargetButton.Content = "Select Target";
        busy = true;
        UpdateControls();
        try
        {
            var result = await automation.InspectAsync(point.X, point.Y);
            if (result is { Success: true, Target: { } target })
            {
                TargetTitle.Text = target.ProcessName;
                TargetDetail.Text = $"{target.Mechanism}\nPID {target.ProcessId} · compatible, readback required";
                SetStatus(region is not null ? "Ready" : "Idle", result.Message);
                SaveSettings();
            }
            else
            {
                TargetTitle.Text = "Unsupported target";
                TargetDetail.Text = result.Message;
                SetStatus("Unsupported Target", result.Message);
            }
        }
        catch (Exception ex) { ReportError(ex); }
        finally { busy = false; UpdateControls(); }
    }

    private async void Start_Click(object sender, RoutedEventArgs e) => await StartRunAsync(false);
    private async void Preview_Click(object sender, RoutedEventArgs e) => await StartRunAsync(true);

    private async Task StartRunAsync(bool preview)
    {
        if (busy || running is { IsCompleted: false }) return;
        if (region is null || !Monitors.IsValid(region) || sourceTopology != Monitors.Fingerprint())
        { SetStatus("Error", "The source region or display layout changed. Select the source again."); return; }
        if (hotkeys?.StopRegistered != true) { SetStatus("Error", "The global stop hotkey must be available before starting."); return; }
        if (!preview && !automation.HasTarget) { SetStatus("Error", "Select a compatible target first."); return; }
        if (!int.TryParse(IntervalInput.Text, out var interval) || interval is < 100 or > 5000)
        { SetStatus("Error", "Capture interval must be a whole number from 100 to 5000 ms."); return; }
        if (LanguageInput.SelectedItem is not LanguageOption language) { SetStatus("Error", "Install and select a Windows OCR language first."); return; }
        if (Math.Max(region.Width, region.Height) > Windows.Media.Ocr.OcrEngine.MaxImageDimension)
        { SetStatus("Error", $"Select a smaller source. OCR supports dimensions up to {Windows.Media.Ocr.OcrEngine.MaxImageDimension} pixels."); return; }
        busy = true;
        UpdateControls();
        try
        {
            var resuming = state == "Paused";
            CancelPicking();
            if (!preview)
            {
                var validation = await automation.RequestAsync(new("validate"));
                if (!validation.Success) throw new InvalidOperationException(validation.Message);
            }
            var capture = new DesktopCapture(region);
            WindowsOcr ocr;
            try { ocr = new(language.Tag, PreprocessInput.IsChecked == true); }
            catch { capture.Dispose(); throw; }
            SaveSettings();
            if (!resuming || previewOnly != preview) confirmation = new(ConfirmInput.IsChecked == true);
            previewOnly = preview;
            runCancellation?.Dispose();
            runCancellation = new();
            var token = runCancellation.Token;
            var engine = new SyncEngine(capture, ocr, preview ? new PreviewWriter() : automation,
                new(interval, ConfirmInput.IsChecked == true), confirmation);
            engine.Updated += update =>
            {
                if (update.Kind == SyncEventKind.Capture) { Interlocked.Increment(ref frameCount); return; }
                if (update.Kind == SyncEventKind.Skipped) return;
                Dispatcher.BeginInvoke(() => ApplyUpdate(update, token));
            };
            SetStatus("Running", preview ? "Preview only: recognizing locally. No target writes." : "Synchronizing in the background. Your active window stays in place.");
            Log.Information("Session started. Preview {Preview}, interval {IntervalMs}", preview, interval);
            health.Start();
            running = Task.Run(async () => { using (capture) await engine.RunAsync(token); }, CancellationToken.None);
            _ = ObserveRunAsync(running, token);
        }
        catch (Exception ex) { ReportError(ex); }
        finally { busy = false; UpdateControls(); }
    }

    private async Task ObserveRunAsync(Task task, CancellationToken token)
    {
        try { await task; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { if (!closing) ReportError(ex); }
        finally { health.Stop(); UpdateControls(); }
    }

    private void ApplyUpdate(SyncEvent update, CancellationToken token)
    {
        if (token.IsCancellationRequested || state != "Running") return;
        if (update.Kind == SyncEventKind.Preview)
        {
            PreviewText.Text = update.Text;
            PreviewPlaceholder.Visibility = string.IsNullOrWhiteSpace(update.Text) ? Visibility.Visible : Visibility.Collapsed;
            if (string.IsNullOrWhiteSpace(update.Text)) StatusLabel.Text = "No text detected. The target was left unchanged.";
        }
        else if (update.Kind == SyncEventKind.Written)
        {
            if (!previewOnly)
            {
                WritesLabel.Text = $"{++writeCount:N0} updates";
                LastSyncLabel.Text = $"Synced {DateTime.Now:HH:mm:ss}";
                StatusLabel.Text = "Text synchronized and readback verified.";
                Log.Information("Target update verified.");
            }
            else StatusLabel.Text = "Preview confirmed. No text was written to another application.";
        }
        else if (update.Kind == SyncEventKind.OcrError) StatusLabel.Text = update.Message;
    }

    private async void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (state == "Paused") { await StartRunAsync(previewOnly); return; }
        await StopRunAsync("Paused. Start or Resume to continue.", "Paused");
    }
    private async void Stop_Click(object sender, RoutedEventArgs e) => await StopRunAsync("Stopped. No further updates will be sent.");

    private async Task StopRunAsync(string? message = null, string? nextState = null)
    {
        CancelPicking();
        health.Stop();
        if (runCancellation is not null) await runCancellation.CancelAsync();
        if (running is { } task)
        {
            try { await task; }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }
        confirmation?.Reject();
        running = null;
        SetStatus(nextState ?? (region is not null && automation.HasTarget ? "Ready" : "Idle"), message ?? "Ready to configure.");
    }

    private async Task CheckHealthAsync()
    {
        if (checkingHealth || previewOnly || state != "Running") return;
        checkingHealth = true;
        try
        {
            var response = await automation.RequestAsync(new("validate"), runCancellation?.Token ?? default);
            if (!response.Success) throw new InvalidOperationException(response.Message);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { await StopRunAsync(); ReportError(ex); }
        finally { checkingHealth = false; }
    }

    private void SetStatus(string value, string message)
    {
        state = value;
        StateLabel.Text = $"●  {value}";
        StateLabel.Foreground = new SolidColorBrush(value is "Error" or "Unsupported Target" ? Color.FromRgb(255, 177, 151) : Color.FromRgb(142, 229, 194));
        StatusLabel.Text = message;
        UpdateControls();
    }
    private void UpdateControls()
    {
        var active = state == "Running" || running is { IsCompleted: false };
        SourceButton.IsEnabled = !busy && !active;
        TargetButton.IsEnabled = !busy && !active;
        SettingsPanel.IsEnabled = !busy && !active;
        StartButton.IsEnabled = !busy && !active && !picking && region is not null && automation.HasTarget && hotkeys?.StopRegistered == true;
        PreviewButton.IsEnabled = !busy && !active && !picking && region is not null && hotkeys?.StopRegistered == true;
        PauseButton.IsEnabled = !busy && (active || state == "Paused");
        PauseButton.Content = state == "Paused" ? "Resume" : "Pause";
        StopButton.IsEnabled = active || state == "Paused" || picking;
    }
    private void ReportError(Exception ex)
    {
        Log.Warning("Operation failed: {ErrorType}", ex.GetType().Name);
        SetStatus("Error", ex is InvalidOperationException or ArgumentException ? ex.Message : "The operation failed. Check the source and select the target again.");
    }
    public async Task HandleUnexpectedErrorAsync()
    {
        await StopRunAsync();
        SetStatus("Error", "An unexpected error stopped synchronization. Select the source and target again.");
    }
    private void SaveSettings()
    {
        if (!int.TryParse(IntervalInput.Text, out var interval) || interval is < 100 or > 5000) interval = 200;
        try
        {
            SettingsStore.Save(new()
            {
                IntervalMs = interval, ConfirmTwice = ConfirmInput.IsChecked == true,
                Preprocess = PreprocessInput.IsChecked == true, Language = (LanguageInput.SelectedItem as LanguageOption)?.Tag,
                Source = region, DisplayTopology = sourceTopology,
                LastTarget = automation.Target is { } target ? target with { Name = "" } : loaded.LastTarget
            });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        { Log.Warning("Settings could not be saved: {ErrorType}", ex.GetType().Name); }
    }
    private void TestWindow_Click(object sender, RoutedEventArgs e)
    {
        try { Program.StartChild("--test-target"); }
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
        hotkeys?.Dispose();
        automation.Dispose();
        runCancellation?.Dispose();
        health.Stop();
        metrics.Stop();
        Close();
    }
}
