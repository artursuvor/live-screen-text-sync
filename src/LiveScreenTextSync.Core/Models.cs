namespace LiveScreenTextSync.Core;

public sealed record ScreenRegion(int X, int Y, int Width, int Height, string Monitor);

public sealed record Frame(int Width, int Height, byte[] Pixels, long Generation = 0);

public sealed record SyncOptions(int IntervalMs = 200, bool ConfirmTwice = true)
{
    public void Validate()
    {
        if (IntervalMs is < 100 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(IntervalMs), "Capture interval must be 100–5000 ms.");
    }
}

public interface IFrameSource
{
    Task<Frame> CaptureAsync(CancellationToken cancellationToken);
}

public interface ITextRecognizer
{
    Task<string> RecognizeAsync(Frame frame, CancellationToken cancellationToken);
}

public interface ITextWriter
{
    Task WriteAsync(string text, CancellationToken cancellationToken);
}

public enum SyncEventKind { Preview, Written, OcrError, Capture, Skipped }
public sealed record SyncEvent(SyncEventKind Kind, string? Text = null, string? Message = null);
