using System.Runtime.InteropServices;
using System.Text;
using Accessibility;
using LiveScreenTextSync.App.Interop;
using LiveScreenTextSync.Core;

namespace LiveScreenTextSync.App.Automation;

internal static class NativeEdit
{
    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(nint window, uint objectId, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible? accessible);

    public static bool IsWritable(nint handle, int process)
    {
        if (handle == 0 || !NativeMethods.IsWindow(handle)) return false;
        NativeMethods.GetWindowThreadProcessId(handle, out var owner);
        if (owner != process) return false;
        var name = new StringBuilder(128);
        NativeMethods.GetClassName(handle, name, name.Capacity);
        var allowed = name.ToString() is "Edit" or "RichEdit20W" or "RichEdit20A" or "RICHEDIT50W";
        var style = NativeMethods.GetWindowLong(handle, -16);
        return allowed && (style & (0x0020 | 0x0800)) == 0; // ES_PASSWORD / ES_READONLY
    }

    public static IAccessible? GetAccessible(nint handle)
    {
        var iid = typeof(IAccessible).GUID;
        if (AccessibleObjectFromWindow(handle, 0xFFFFFFFC, ref iid, out var accessible) < 0 || accessible is null) return null;
        var state = Convert.ToInt32(accessible.get_accState(0));
        // STATE_SYSTEM_UNAVAILABLE / READONLY / PROTECTED
        if ((state & (0x1 | 0x40 | 0x20000000)) != 0) return null;
        if (Convert.ToInt32(accessible.get_accRole(0)) != 0x2A) return null; // ROLE_SYSTEM_TEXT
        return accessible;
    }

    public static void SetText(nint handle, string text)
    {
        if (NativeMethods.SendText(handle, 0x000C, 0, text, 0x0022, 750, out var result) == 0 || result == 0)
            throw new InvalidOperationException("The native edit control rejected the update or stopped responding.");
    }

    public static bool Verify(nint handle, string text)
    {
        var buffer = new StringBuilder(text.Length + 2);
        if (NativeMethods.ReadText(handle, 0x000D, (nuint)buffer.Capacity, buffer, 0x0022, 750, out _) == 0)
            throw new InvalidOperationException("The native edit control did not respond to readback.");
        return TextConfirmation.Normalize(buffer.ToString()) == TextConfirmation.Normalize(text);
    }
}
