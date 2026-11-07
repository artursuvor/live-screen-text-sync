using System.Threading.Channels;

namespace LiveScreenTextSync.Core;

public sealed class SyncEngine(IFrameSource source, ITextRecognizer recognizer, ITextWriter writer, SyncOptions options, TextConfirmation? confirmation = null)
{
    public event Action<SyncEvent>? Updated;
    private long latestGeneration;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        options.Validate();
        var frames = Channel.CreateBounded<Frame>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest, SingleWriter = true, SingleReader = true
        });
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var capture = CaptureAsync(frames.Writer, lifetime.Token);
        try
        {
            await ConsumeAsync(frames.Reader, lifetime.Token).ConfigureAwait(false);
            await capture.ConfigureAwait(false);
        }
        finally
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            try { await capture.ConfigureAwait(false); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        }
    }

    private async Task CaptureAsync(ChannelWriter<Frame> output, CancellationToken token)
    {
        Exception? failure = null;
        Frame? previous = null;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.IntervalMs));
            do
            {
                token.ThrowIfCancellationRequested();
                var frame = await source.CaptureAsync(token).ConfigureAwait(false);
                if (FrameComparer.HasChanged(previous, frame)) Interlocked.Increment(ref latestGeneration);
                previous = frame;
                output.TryWrite(frame with { Generation = Interlocked.Read(ref latestGeneration) });
                Updated?.Invoke(new(SyncEventKind.Capture));
            } while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        catch (Exception ex) { failure = ex; throw; }
        finally { output.TryComplete(failure); }
    }

    private async Task ConsumeAsync(ChannelReader<Frame> input, CancellationToken token)
    {
        var tracker = confirmation ?? new TextConfirmation(options.ConfirmTwice);
        Frame? previous = null;
        var retry = false;
        await foreach (var frame in input.ReadAllAsync(token).ConfigureAwait(false))
        {
            if (!retry && !tracker.NeedsConfirmation && !FrameComparer.HasChanged(previous, frame))
            {
                Updated?.Invoke(new(SyncEventKind.Skipped));
                continue;
            }
            previous = frame;
            string text;
            try
            {
                text = await recognizer.RecognizeAsync(frame, token).ConfigureAwait(false);
                retry = false;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                tracker.Reject();
                retry = true;
                Updated?.Invoke(new(SyncEventKind.OcrError, Message: "OCR failed. The target was left unchanged; retrying."));
                continue;
            }
            token.ThrowIfCancellationRequested();
            if (frame.Generation != Interlocked.Read(ref latestGeneration))
            {
                tracker.Reject();
                retry = true;
                continue;
            }
            Updated?.Invoke(new(SyncEventKind.Preview, Text: text));
            var update = tracker.Observe(text);
            if (update is null) continue;
            token.ThrowIfCancellationRequested();
            await writer.WriteAsync(update, token).ConfigureAwait(false);
            tracker.Commit(update);
            Updated?.Invoke(new(SyncEventKind.Written));
        }
    }
}
