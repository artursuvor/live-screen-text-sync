using System.IO;
using System.Windows;
using LiveScreenTextSync.App.Services;
using Serilog;

namespace LiveScreenTextSync.App;

public partial class App : Application
{
    private bool smokeFailed;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Logger = new LoggerConfiguration().WriteTo.File(Path.Combine(SettingsStore.DataDirectory, "logs", "app-.log"),
            rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7).CreateLogger();
        Log.Information("Application started. Version {Version}", typeof(App).Assembly.GetName().Version);
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI operation failed: {ErrorType}", args.Exception.GetType().Name);
            smokeFailed = true;
            args.Handled = true;
            if (MainWindow is MainWindow main) _ = main.HandleUnexpectedErrorAsync();
        };
        var window = new MainWindow();
        if (Program.RenderPath is { } path)
        {
            Diagnostics.UiRenderer.Render(window, path);
            Shutdown();
            return;
        }
        window.Show();
        if (Program.SmokeReportPath is { } report)
        {
            window.Closed += (_, _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(report)!);
                File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new { Success = !smokeFailed, WindowClosed = true }));
            };
            _ = window.Dispatcher.BeginInvoke(async () => { await Task.Delay(250); window.Close(); });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application stopped.");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
