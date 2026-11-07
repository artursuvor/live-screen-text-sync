using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Forms.Integration;
using LiveScreenTextSync.App.Capture;
using LiveScreenTextSync.Core;

namespace LiveScreenTextSync.App.Views;

public sealed record FixturePoint(int X, int Y);
public sealed record FixtureInfo(ScreenRegion Source, FixturePoint Target, FixturePoint ReadOnly,
    FixturePoint Password, FixturePoint Native, int ProcessId);
public sealed record FixtureState(string Text, int Changes);

public sealed class TestTargetWindow : Window
{
    private readonly TextBlock source = new() { Text = "SCREEN SYNC READY", FontSize = 28, Foreground = Brushes.Black, Margin = new Thickness(16) };
    private readonly TextBox target = new() { Text = "Keep this value during inspection", Height = 66, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 16 };
    private readonly TextBox readOnly = new() { Text = "Read-only field — must be rejected", IsReadOnly = true, Height = 36 };
    private readonly PasswordBox password = new() { Password = "test-only-password", Height = 36 };
    private readonly System.Windows.Forms.TextBox native = new() { Text = "Native edit test", Multiline = true, Dock = System.Windows.Forms.DockStyle.Fill };
    private readonly DispatcherTimer commands = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly string? infoPath;
    private int changes;

    public TestTargetWindow(string[] args)
    {
        Title = "Live Screen Text Sync — Test Target";
        Width = 650; Height = 690; Left = 24; Top = 32;
        Background = Brushes.White; Foreground = Brushes.Black;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        var panel = new StackPanel { Margin = new Thickness(24) };
        Content = panel;
        panel.Children.Add(new TextBlock { Text = "A safe place to try synchronization", FontSize = 23, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Select the source below, then point at the editable target and press F8.", Margin = new Thickness(0, 8, 0, 18), TextWrapping = TextWrapping.Wrap });
        var sourcePanel = new Border { Background = Brushes.White, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = source, Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(sourcePanel);
        var change = new Button { Content = "Change Source", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 18) };
        change.Click += (_, _) => source.Text = source.Text.EndsWith("READY", StringComparison.Ordinal) ? "SCREEN SYNC UPDATED" : "SCREEN SYNC READY";
        panel.Children.Add(change);
        AddLabel(panel, "Editable WPF target"); panel.Children.Add(target);
        AddLabel(panel, "Read-only field"); panel.Children.Add(readOnly);
        AddLabel(panel, "Password field"); panel.Children.Add(password);
        AddLabel(panel, "Standard native edit target (Win32 / MSAA)");
        var host = new WindowsFormsHost { Child = native, Height = 52 };
        panel.Children.Add(host);
        AutomationProperties.SetAutomationId(target, "FixtureEditableTarget");
        AutomationProperties.SetAutomationId(readOnly, "FixtureReadOnlyTarget");
        AutomationProperties.SetAutomationId(password, "FixturePasswordTarget");
        var index = Array.IndexOf(args, "--fixture-info");
        if (index >= 0 && index + 1 < args.Length) infoPath = args[index + 1];
        target.TextChanged += (_, _) => { changes++; SaveState(); };
        ContentRendered += (_, _) =>
        {
            if (infoPath is null) return;
            var p1 = sourcePanel.PointToScreen(new Point(2, 2));
            var p2 = sourcePanel.PointToScreen(new Point(sourcePanel.ActualWidth - 2, sourcePanel.ActualHeight - 2));
            var monitor = Monitors.GetAll().First(m => p1.X >= m.X && p1.X < m.X + m.Width && p1.Y >= m.Y && p1.Y < m.Y + m.Height);
            var info = new FixtureInfo(new((int)p1.X, (int)p1.Y, (int)(p2.X - p1.X), (int)(p2.Y - p1.Y), monitor.Device),
                Center(target), Center(readOnly), Center(password), Center(host), Environment.ProcessId);
            File.WriteAllText(infoPath, JsonSerializer.Serialize(info));
            SaveState();
            commands.Start();
        };
        commands.Tick += (_, _) =>
        {
            if (infoPath is null || !File.Exists(infoPath + ".command")) return;
            var command = File.ReadAllText(infoPath + ".command").Trim();
            File.Delete(infoPath + ".command");
            switch (command)
            {
                case "change": source.Text = "SCREEN SYNC UPDATED"; break;
                case "empty": source.Text = ""; break;
                case "readonly": target.IsReadOnly = true; break;
                case "remove": panel.Children.Remove(target); break;
                case "hang": Thread.Sleep(Timeout.Infinite); break;
            }
        };
        Closed += (_, _) => { commands.Stop(); native.Dispose(); };
    }

    private static void AddLabel(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 12, 0, 6), Foreground = Brushes.DimGray });
    private static FixturePoint Center(FrameworkElement element)
    {
        var point = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        return new((int)point.X, (int)point.Y);
    }
    private void SaveState()
    {
        if (infoPath is not null) File.WriteAllText(infoPath + ".state", JsonSerializer.Serialize(new FixtureState(target.Text, changes)));
    }
}
