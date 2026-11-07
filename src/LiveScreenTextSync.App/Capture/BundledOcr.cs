using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiveScreenTextSync.Core;
using Tesseract;

namespace LiveScreenTextSync.App.Capture;

public sealed class BundledOcr : ITextRecognizer, IDisposable
{
    private readonly TesseractEngine engine;
    private readonly bool enhance;

    public BundledOcr(string models, bool enhance)
    {
        if (!OcrLanguages.HasModels(models)) throw new InvalidOperationException("The selected language files are missing. Extract the complete portable package, including tessdata.");
        this.enhance = enhance;
        engine = new TesseractEngine(Path.Combine(AppContext.BaseDirectory, "tessdata"), models, EngineMode.LstmOnly);
    }

    public Task<string> RecognizeAsync(Frame frame, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        var image = OcrImagePreparer.Prepare(frame, enhance, 4096);
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        using var pixels = Pix.LoadFromMemory(memory.ToArray());
        using var page = engine.Process(pixels, frame.Height <= 64 ? PageSegMode.SingleLine : PageSegMode.Auto);
        var text = page.GetText();
        token.ThrowIfCancellationRequested();
        return text;
    }, token);

    public void Dispose() => engine.Dispose();
}
