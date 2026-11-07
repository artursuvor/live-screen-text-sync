using LiveScreenTextSync.Core;

namespace LiveScreenTextSync.App.Services;

public sealed class ClipboardWriter : IClipboardWriter
{
    public bool TryWrite(string text)
    {
        try
        {
            var data = new System.Windows.Forms.DataObject(System.Windows.Forms.DataFormats.UnicodeText, text);
            System.Windows.Forms.Clipboard.SetDataObject(data, true, 1, 0);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException) { return false; }
    }
}

internal sealed class RecordingClipboardWriter : IClipboardWriter
{
    public List<string> Writes { get; } = [];
    public bool TryWrite(string text) { Writes.Add(text); return true; }
}
