using LiveScreenTextSync.Core;

namespace LiveScreenTextSync.Tests;

public class PipelineTests
{
    private sealed class Source : IFrameSource
    {
        public byte Value;
        public Task<Frame> CaptureAsync(CancellationToken token) => Task.FromResult(new Frame(1, 1, [Value, Value, Value, 255]));
    }
    private sealed class Recognizer(Func<Frame, CancellationToken, Task<string>> action) : ITextRecognizer
    {
        public Task<string> RecognizeAsync(Frame frame, CancellationToken token) => action(frame, token);
    }
    private sealed class Writer(Func<string, Task> action) : ITextWriter
    {
        public Task WriteAsync(string text, CancellationToken token) => action(text);
    }

    [Fact]
    public async Task ConfirmsAnUnchangedFrameThenSkipsFurtherOcr()
    {
        var calls = 0;
        var writes = 0;
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var skipped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = new SyncEngine(new Source(), new Recognizer((_, _) => { calls++; return Task.FromResult("hello"); }),
            new Writer(_ => { writes++; return Task.CompletedTask; }), new(100));
        engine.Updated += e => { if (e.Kind == SyncEventKind.Skipped) skipped.TrySetResult(); };
        var running = engine.RunAsync(cancel.Token);
        await skipped.Task.WaitAsync(cancel.Token);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal(2, calls);
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task DiscardsOcrThatBecameStaleAndConsumesLatestFrame()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var source = new Source();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capturedNew = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var written = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var writes = new List<string>();
        var engine = new SyncEngine(source, new Recognizer(async (frame, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1) { entered.SetResult(); await release.Task.WaitAsync(token); }
            return frame.Pixels[0] == 0 ? "old" : "latest";
        }), new Writer(text => { writes.Add(text); written.TrySetResult(text); return Task.CompletedTask; }), new(100, false));
        engine.Updated += e => { if (e.Kind == SyncEventKind.Capture && source.Value == 255) capturedNew.TrySetResult(); };
        var running = engine.RunAsync(cancel.Token);
        await entered.Task.WaitAsync(cancel.Token);
        source.Value = 255;
        await capturedNew.Task.WaitAsync(cancel.Token);
        release.SetResult();
        Assert.Equal("latest", await written.Task.WaitAsync(cancel.Token));
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal(["latest"], writes);
    }

    [Fact]
    public async Task RetriesOcrErrorsOnUnchangedImages()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var engine = new SyncEngine(new Source(), new Recognizer((_, _) =>
        {
            if (++calls == 1) throw new InvalidOperationException("test");
            return Task.FromResult("recovered");
        }), new Writer(_ => { written.TrySetResult(); return Task.CompletedTask; }), new(100));
        var running = engine.RunAsync(cancel.Token);
        await written.Task.WaitAsync(cancel.Token);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task WriterFailureTerminatesPipeline()
    {
        var engine = new SyncEngine(new Source(), new Recognizer((_, _) => Task.FromResult("text")),
            new Writer(_ => throw new InvalidOperationException("Target is gone.")), new(100, false));
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RunAsync(cancel.Token));
        Assert.Equal("Target is gone.", error.Message);
    }
}
