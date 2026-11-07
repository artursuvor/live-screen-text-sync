using LiveScreenTextSync.Core;

namespace LiveScreenTextSync.Tests;

public class AutoCopyControllerTests
{
    private sealed class Clipboard : IClipboardWriter
    {
        public bool Busy;
        public List<string> Writes { get; } = [];
        public bool TryWrite(string text) { if (Busy) return false; Writes.Add(text); return true; }
    }

    [Fact]
    public void DoesNotTouchClipboardUntilEnabled()
    {
        var clipboard = new Clipboard();
        var controller = new AutoCopyController(clipboard);
        controller.Queue("private text");
        Assert.Equal(ClipboardCopyResult.NothingPending, controller.TryCopyPending());
        Assert.Empty(clipboard.Writes);
    }

    [Fact]
    public void RetriesOnlyTheLatestResultAfterClipboardContention()
    {
        var clipboard = new Clipboard { Busy = true };
        var controller = new AutoCopyController(clipboard);
        controller.SetEnabled(true);
        controller.Queue("stale");
        Assert.Equal(ClipboardCopyResult.Busy, controller.TryCopyPending());
        controller.Queue("latest ✓\nsecond line");
        clipboard.Busy = false;
        Assert.Equal(ClipboardCopyResult.Copied, controller.TryCopyPending());
        Assert.Equal(["latest ✓\nsecond line"], clipboard.Writes);
        Assert.Equal(ClipboardCopyResult.NothingPending, controller.TryCopyPending());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DisablingOrStoppingCancelsPendingCopies(bool disable)
    {
        var clipboard = new Clipboard();
        var controller = new AutoCopyController(clipboard);
        controller.SetEnabled(true);
        controller.Queue("pending");
        if (disable) controller.SetEnabled(false); else controller.CancelPending();
        Assert.Equal(ClipboardCopyResult.NothingPending, controller.TryCopyPending());
        Assert.Empty(clipboard.Writes);
    }

    [Fact]
    public void EmptyRecognitionDoesNotClearClipboard()
    {
        var clipboard = new Clipboard();
        var controller = new AutoCopyController(clipboard);
        controller.SetEnabled(true);
        controller.Queue("  \r\n");
        Assert.Equal(ClipboardCopyResult.NothingPending, controller.TryCopyPending());
        Assert.Empty(clipboard.Writes);
    }
}
