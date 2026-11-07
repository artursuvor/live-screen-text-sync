using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using LiveScreenTextSync.App.Interop;
using LiveScreenTextSync.Core;

namespace LiveScreenTextSync.App.Views;

public sealed class SourceOutline : Window
{
    public SourceOutline(ScreenRegion region)
    {
        Title = "Source region outline";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        Focusable = false;
        IsHitTestVisible = false;
        Content = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(142, 229, 194)), BorderThickness = new Thickness(2) };
        Loaded += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowLong(handle, -20, NativeMethods.GetWindowLong(handle, -20) | 0x080000A0);
            NativeMethods.SetWindowPos(handle, -1, region.X - 2, region.Y - 2, region.Width + 4, region.Height + 4, 0x0050);
        };
    }
}
