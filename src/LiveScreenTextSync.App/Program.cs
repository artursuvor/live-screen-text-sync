namespace LiveScreenTextSync.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--automation-worker"))
            return Task.Run(Automation.AutomationWorker.Run).GetAwaiter().GetResult();
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
