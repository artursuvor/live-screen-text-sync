using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using LiveScreenTextSync.App.Capture;
using LiveScreenTextSync.App.Interop;
using LiveScreenTextSync.Core;

namespace LiveScreenTextSync.App.Views;

public sealed class RegionPicker : Window
{
    private readonly Canvas canvas = new();
    private readonly Rectangle selection = new() { Stroke = Brushes.Aquamarine, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(50, 100, 255, 210)) };
    private readonly DisplayMonitor monitor;
    private Point? anchor;
    public event Action<ScreenRegion>? Selected;
    public event Action? Cancelled;

    public RegionPicker(DisplayMonitor monitor)
    {
        this.monitor = monitor;
        Title = "Select source region — Esc to cancel";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(100, 5, 12, 20));
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        Content = canvas;
        canvas.Children.Add(new TextBlock
        {
            Text = "Drag to select a source region   ·   Esc to cancel", FontSize = 22, Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(12, 22, 32)), Padding = new Thickness(16), Margin = new Thickness(24)
        });
        canvas.Children.Add(selection);
        SourceInitialized += (_, _) => NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, -1,
            monitor.X, monitor.Y, monitor.Width, monitor.Height, 0x0040);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Cancelled?.Invoke(); };
        MouseLeftButtonDown += (_, e) => { anchor = e.GetPosition(canvas); CaptureMouse(); };
        MouseMove += (_, e) => { if (anchor is { } start) Draw(start, e.GetPosition(canvas)); };
        MouseLeftButtonUp += (_, e) =>
        {
            if (anchor is not { } start) return;
            ReleaseMouseCapture();
            var end = e.GetPosition(canvas);
            var p1 = PointToScreen(new Point(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y)));
            var p2 = PointToScreen(new Point(Math.Max(start.X, end.X), Math.Max(start.Y, end.Y)));
            var region = new ScreenRegion((int)p1.X, (int)p1.Y, (int)(p2.X - p1.X), (int)(p2.Y - p1.Y), monitor.Device);
            anchor = null;
            if (monitor.Contains(region) && (long)region.Width * region.Height <= 16_000_000) Selected?.Invoke(region);
        };
    }

    private void Draw(Point start, Point end)
    {
        Canvas.SetLeft(selection, Math.Min(start.X, end.X));
        Canvas.SetTop(selection, Math.Min(start.Y, end.Y));
        selection.Width = Math.Abs(end.X - start.X);
        selection.Height = Math.Abs(end.Y - start.Y);
    }

    public static Task<ScreenRegion?> PickAsync()
    {
        var result = new TaskCompletionSource<ScreenRegion?>();
        var windows = Monitors.GetAll().Select(m => new RegionPicker(m)).ToArray();
        void Complete(ScreenRegion? region)
        {
            if (!result.TrySetResult(region)) return;
            foreach (var window in windows) window.Close();
        }
        foreach (var window in windows)
        {
            window.Selected += region => Complete(region);
            window.Cancelled += () => Complete(null);
            window.Show();
        }
        if (windows.Length == 0) Complete(null);
        return result.Task;
    }
}
