using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LiveScreenTextSync.App.Capture;
using LiveScreenTextSync.Core;

namespace LiveScreenTextSync.App.Views;

public sealed record SampleSourceInfo(ScreenRegion Region, int ProcessId);

public sealed class SampleSourceWindow : Window
{
    public SampleSourceWindow(string[] args)
    {
        Title = "Sample Text — Live Screen Text Sync";
        Width = 580; Height = 280; Left = 24; Top = 32;
        Background = Brushes.White; Foreground = Brushes.Black;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        var panel = new StackPanel { Margin = new Thickness(24) };
        Content = panel;
        panel.Children.Add(new TextBlock { Text = "Select the text below as your source region.", Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 16) });
        var tiny = args.Contains("--tiny-text");
        var text = new TextBlock { Text = "SCREEN TEXT READY", FontSize = tiny ? 11 : 28, Foreground = Brushes.Black, Padding = new Thickness(tiny ? 2 : 12) };
        var source = new Border { Background = Brushes.White, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = text };
        panel.Children.Add(source);
        var change = new Button { Content = "Change Text", Padding = new Thickness(14, 8, 14, 8), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) };
        change.Click += (_, _) => text.Text = text.Text.EndsWith("READY", StringComparison.Ordinal) ? "SCREEN TEXT UPDATED" : "SCREEN TEXT READY";
        panel.Children.Add(change);
        var index = Array.IndexOf(args, "--fixture-info");
        if (index < 0 || index + 1 >= args.Length) return;
        var infoPath = args[index + 1];
        Topmost = true;
        var commands = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        ContentRendered += (_, _) =>
        {
            var p1 = source.PointToScreen(new Point(2, 2));
            var p2 = source.PointToScreen(new Point(source.ActualWidth - 2, source.ActualHeight - 2));
            var monitor = Monitors.GetAll().First(m => p1.X >= m.X && p1.X < m.X + m.Width && p1.Y >= m.Y && p1.Y < m.Y + m.Height);
            File.WriteAllText(infoPath, JsonSerializer.Serialize(new SampleSourceInfo(
                new((int)p1.X, (int)p1.Y, (int)(p2.X - p1.X), (int)(p2.Y - p1.Y), monitor.Device), Environment.ProcessId)));
            commands.Start();
        };
        commands.Tick += (_, _) =>
        {
            if (!File.Exists(infoPath + ".command")) return;
            var command = File.ReadAllText(infoPath + ".command").Trim();
            File.Delete(infoPath + ".command");
            if (command == "dark")
            {
                source.Background = new SolidColorBrush(Color.FromRgb(25, 28, 32));
                text.Foreground = Brushes.White;
                text.Text = "SMALL DARK TEXT";
            }
            else text.Text = command == "empty" ? "" : command == "change" ? "SCREEN TEXT UPDATED" : "SCREEN TEXT READY";
        };
        Closed += (_, _) => commands.Stop();
    }
}
