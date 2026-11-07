using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LiveScreenTextSync.App.Diagnostics;

public static class UiRenderer
{
    public static void Render(MainWindow window, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Save(window, path);
        ((Expander)window.FindName("SettingsExpander")).IsExpanded = true;
        Save(window, Path.Combine(Path.GetDirectoryName(path)!, "settings.png"));
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        Save(window, Path.Combine(Path.GetDirectoryName(path)!, "compact.png"));
    }

    private static void Save(Window window, string path)
    {
        var visual = (FrameworkElement)window.Content;
        var available = new Size(window.Width - 16, window.Height - 39);
        visual.Measure(available);
        visual.Arrange(new Rect(available));
        visual.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(visual);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(available.Width * dpi.DpiScaleX),
            (int)Math.Ceiling(available.Height * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(available));
        bitmap.Render(background);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        png.Save(output);
    }
}
