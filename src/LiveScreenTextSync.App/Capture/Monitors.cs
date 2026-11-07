using LiveScreenTextSync.App.Interop;
using LiveScreenTextSync.Core;

namespace LiveScreenTextSync.App.Capture;

public sealed record DisplayMonitor(string Device, int X, int Y, int Width, int Height, uint Dpi)
{
    public bool Contains(ScreenRegion region) => region.Monitor == Device && region.Width >= 8 && region.Height >= 8 &&
        region.X >= X && region.Y >= Y && (long)region.X + region.Width <= (long)X + Width &&
        (long)region.Y + region.Height <= (long)Y + Height;
}

public static class Monitors
{
    public static IReadOnlyList<DisplayMonitor> GetAll()
    {
        var result = new List<DisplayMonitor>();
        NativeMethods.EnumDisplayMonitors(0, 0, (nint handle, nint dc, ref NativeMethods.Rect rect, nint data) =>
        {
            var info = new NativeMethods.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
            if (NativeMethods.GetMonitorInfo(handle, ref info))
            {
                NativeMethods.GetDpiForMonitor(handle, 0, out var dpi, out _);
                result.Add(new(info.Device, info.Monitor.Left, info.Monitor.Top,
                    info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top, dpi));
            }
            return true;
        }, 0);
        return result;
    }

    public static string Fingerprint() => string.Join(";", GetAll().OrderBy(x => x.Device).Select(x => x.ToString()));
    public static bool IsValid(ScreenRegion region) => (long)region.Width * region.Height <= 16_000_000 && GetAll().Any(m => m.Contains(region));
}
