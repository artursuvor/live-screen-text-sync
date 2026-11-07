using System.Windows;
using System.Windows.Interop;
using LiveScreenTextSync.App.Interop;

namespace LiveScreenTextSync.App.Services;

public sealed class GlobalHotkeys : IDisposable
{
    private readonly nint handle;
    private readonly HwndSource source;
    public bool StopRegistered { get; }
    public event Action? StopPressed;
    public event Action? PickPressed;
    public event Action? CancelPressed;

    public GlobalHotkeys(Window window, uint stopVirtualKey = 0x53)
    {
        handle = new WindowInteropHelper(window).Handle;
        source = HwndSource.FromHwnd(handle);
        source.AddHook(OnMessage);
        StopRegistered = NativeMethods.RegisterHotKey(handle, 1, 0x4007, stopVirtualKey); // Ctrl+Alt+Shift+S by default, no repeat
    }
    public bool BeginPicking()
    {
        if (!NativeMethods.RegisterHotKey(handle, 2, 0x4000, 0x77)) return false;
        NativeMethods.RegisterHotKey(handle, 3, 0x4000, 0x1B);
        return true;
    }
    public void EndPicking()
    {
        NativeMethods.UnregisterHotKey(handle, 2);
        NativeMethods.UnregisterHotKey(handle, 3);
    }
    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312)
        {
            switch ((int)wParam)
            {
                case 1: StopPressed?.Invoke(); break;
                case 2: PickPressed?.Invoke(); break;
                case 3: CancelPressed?.Invoke(); break;
            }
            handled = true;
        }
        return 0;
    }
    public void Dispose()
    {
        EndPicking();
        NativeMethods.UnregisterHotKey(handle, 1);
        source.RemoveHook(OnMessage);
    }
}
