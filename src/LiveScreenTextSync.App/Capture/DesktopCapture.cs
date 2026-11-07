using System.Runtime.InteropServices;
using LiveScreenTextSync.App.Interop;
using LiveScreenTextSync.Core;

namespace LiveScreenTextSync.App.Capture;

public sealed class DesktopCapture : IFrameSource, IDisposable
{
    private readonly ScreenRegion region;
    private readonly string topology;
    private readonly nint screen, memory, bitmap, previous, bits;
    private bool disposed;

    public DesktopCapture(ScreenRegion region)
    {
        if (!Monitors.IsValid(region)) throw new InvalidOperationException("Select a region within one connected monitor (maximum 16 megapixels).");
        this.region = region;
        topology = Monitors.Fingerprint();
        screen = NativeMethods.GetDC(0);
        memory = NativeMethods.CreateCompatibleDC(screen);
        var info = new NativeMethods.BitmapInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfo>(), Width = region.Width,
            Height = -region.Height, Planes = 1, BitCount = 32
        };
        bitmap = NativeMethods.CreateDIBSection(screen, ref info, 0, out bits, 0, 0);
        if (screen == 0 || memory == 0 || bitmap == 0)
        {
            Dispose();
            throw new InvalidOperationException("Windows could not allocate the screen capture buffer.");
        }
        previous = NativeMethods.SelectObject(memory, bitmap);
    }

    public Task<Frame> CaptureAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (topology != Monitors.Fingerprint()) throw new InvalidOperationException("Display layout or DPI changed. Select the source again.");
        if (!NativeMethods.BitBlt(memory, 0, 0, region.Width, region.Height, screen, region.X, region.Y, 0x40CC0020))
            throw new InvalidOperationException("Screen capture is unavailable. Check that the desktop is unlocked.");
        var pixels = new byte[checked(region.Width * region.Height * 4)];
        Marshal.Copy(bits, pixels, 0, pixels.Length);
        return Task.FromResult(new Frame(region.Width, region.Height, pixels));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (previous != 0 && memory != 0) NativeMethods.SelectObject(memory, previous);
        if (bitmap != 0) NativeMethods.DeleteObject(bitmap);
        if (memory != 0) NativeMethods.DeleteDC(memory);
        if (screen != 0) NativeMethods.ReleaseDC(0, screen);
    }
}
