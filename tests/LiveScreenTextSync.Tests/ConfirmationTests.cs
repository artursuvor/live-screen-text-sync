using LiveScreenTextSync.Core;

namespace LiveScreenTextSync.Tests;

public class ConfirmationTests
{
    [Fact]
    public void RequiresTwoMatchingResultsAndDeduplicatesCommittedText()
    {
        var state = new TextConfirmation(true);
        Assert.Null(state.Observe("first"));
        Assert.Null(state.Observe("second"));
        Assert.Equal("second", state.Observe("second"));
        state.Commit("second");
        Assert.Null(state.Observe("second"));
        Assert.False(state.NeedsConfirmation);
        Assert.Null(state.Observe("first"));
        Assert.Equal("first", state.Observe("first"));
    }

    [Fact]
    public void EmptyOrFailedRecognitionBreaksConfirmationWithoutClearingCommittedText()
    {
        var state = new TextConfirmation(true);
        state.Commit("saved");
        state.Observe("next");
        Assert.Null(state.Observe("  \r\n "));
        Assert.Null(state.Observe("next"));
        state.Reject();
        Assert.Null(state.Observe("next"));
        Assert.Null(state.Observe("saved"));
    }

    [Fact]
    public void NormalizesLineEndingsAndUnicodeButPreservesLineStructure()
    {
        Assert.Equal("Café\n  two words", TextConfirmation.Normalize(" Cafe\u0301  \r\n  two words\u00a0\r\n"));
    }

    [Fact]
    public void ConfirmationCanBeDisabled()
    {
        var state = new TextConfirmation(false);
        Assert.Equal("hello", state.Observe("hello"));
    }

    [Fact]
    public void IgnoresSmallPixelNoiseButDetectsChangedText()
    {
        var first = new Frame(1, 1, [255, 255, 255, 255]);
        Assert.False(FrameComparer.HasChanged(first, new(1, 1, [250, 250, 250, 0])));
        Assert.True(FrameComparer.HasChanged(first, new(1, 1, [0, 0, 0, 255])));
        Assert.True(FrameComparer.HasChanged(null, first));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(5001)]
    public void RejectsInvalidCaptureIntervals(int interval) => Assert.Throws<ArgumentOutOfRangeException>(() => new SyncOptions(interval).Validate());
}
