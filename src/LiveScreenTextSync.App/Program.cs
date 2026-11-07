namespace LiveScreenTextSync.App;

public static class Program
{
    public static string? RenderPath { get; private set; }
    public static string? SmokeReportPath { get; private set; }
    public static string? LocalCheckReportPath { get; private set; }
    public static bool IsDiagnostic => RenderPath is not null || SmokeReportPath is not null || LocalCheckReportPath is not null;
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
        // Also cover dotnet run / dotnet app.dll, whose host does not load this app's manifest.
        Interop.NativeMethods.SetProcessDpiAwarenessContext(-4);
        var render = Array.IndexOf(args, "--render-ui");
        if (render >= 0 && render + 1 < args.Length) RenderPath = System.IO.Path.GetFullPath(args[render + 1]);
        var smoke = Array.IndexOf(args, "--smoke-ui");
        if (smoke >= 0 && smoke + 1 < args.Length) SmokeReportPath = System.IO.Path.GetFullPath(args[smoke + 1]);
        var localCheck = Array.IndexOf(args, "--self-test-local");
        if (localCheck >= 0 && localCheck + 1 < args.Length) LocalCheckReportPath = System.IO.Path.GetFullPath(args[localCheck + 1]);
        var check = Array.IndexOf(args, "--self-test");
        if (check >= 0 && check + 1 < args.Length) return Diagnostics.IntegrationChecks.Run(System.IO.Path.GetFullPath(args[check + 1]));
        if (args.Contains("--automation-worker"))
            return Task.Run(Automation.AutomationWorker.Run).GetAwaiter().GetResult();
        if (args.Contains("--test-target"))
        {
            var fixtureApp = new System.Windows.Application();
            return fixtureApp.Run(new Views.TestTargetWindow(args));
        }
        if (args.Contains("--sample-source"))
        {
            var sampleApp = new System.Windows.Application();
            return sampleApp.Run(new Views.SampleSourceWindow(args));
        }
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
