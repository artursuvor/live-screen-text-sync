namespace LiveScreenTextSync.Core;

public interface IClipboardWriter
{
    bool TryWrite(string text);
}

public enum ClipboardCopyResult { NothingPending, Busy, Copied }

public sealed class AutoCopyController(IClipboardWriter clipboard)
{
    private string? pending;
    public bool Enabled { get; private set; }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (!enabled) CancelPending();
    }

    public void Queue(string text)
    {
        if (Enabled && !string.IsNullOrWhiteSpace(text)) pending = text;
    }

    public ClipboardCopyResult TryCopyPending()
    {
        if (!Enabled || pending is null) return ClipboardCopyResult.NothingPending;
        if (!clipboard.TryWrite(pending)) return ClipboardCopyResult.Busy;
        pending = null;
        return ClipboardCopyResult.Copied;
    }

    public void CancelPending() => pending = null;
}
