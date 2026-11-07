using System.IO;
using System.Windows;
using LiveScreenTextSync.App.Services;
using Serilog;

namespace LiveScreenTextSync.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Logger = new LoggerConfiguration().WriteTo.File(Path.Combine(SettingsStore.DataDirectory, "logs", "app-.log"),
            rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7).CreateLogger();
        Log.Information("Application started. Version {Version}", typeof(App).Assembly.GetName().Version);
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI operation failed: {ErrorType}", args.Exception.GetType().Name);
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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application stopped.");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
