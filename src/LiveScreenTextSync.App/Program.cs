namespace LiveScreenTextSync.App;

public static class Program
{
    public static System.Diagnostics.Process StartChild(params string[] arguments)
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the application executable.");
        var info = new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true };
        if (System.IO.Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("Cannot start the test application.");
    }
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--automation-worker"))
            return Task.Run(Automation.AutomationWorker.Run).GetAwaiter().GetResult();
        if (args.Contains("--test-target"))
        {
            var fixtureApp = new System.Windows.Application();
            return fixtureApp.Run(new Views.TestTargetWindow(args));
        }
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
