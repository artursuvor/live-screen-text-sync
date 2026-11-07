using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using LiveScreenTextSync.Core;

namespace LiveScreenTextSync.App.Capture;

public sealed class WindowsOcr : ITextRecognizer
{
    private readonly OcrEngine engine;
    private readonly bool preprocess;
    public static IReadOnlyList<Language> Languages => OcrEngine.AvailableRecognizerLanguages.ToArray();

    public WindowsOcr(string language, bool preprocess)
    {
        engine = OcrEngine.TryCreateFromLanguage(new Language(language)) ??
            throw new InvalidOperationException("This OCR language is not installed. Add its OCR feature in Windows Settings > Time & language > Language & region.");
        this.preprocess = preprocess;
    }

    public async Task<string> RecognizeAsync(Frame frame, CancellationToken token)
    {
        if (Math.Max(frame.Width, frame.Height) > OcrEngine.MaxImageDimension)
            throw new InvalidOperationException($"Select a smaller region (each dimension must be at most {OcrEngine.MaxImageDimension} pixels).");
        var prepared = OcrImagePreparer.Prepare(frame, preprocess, (int)OcrEngine.MaxImageDimension);
        var text = await ReadAsync(prepared, token).ConfigureAwait(false);
        // An unchanged tiny image would otherwise not be tried again by the change detector.
        // Retry a blank result with grayscale/contrast and light-background normalization immediately.
        if (string.IsNullOrWhiteSpace(text) && !preprocess)
            text = await ReadAsync(OcrImagePreparer.Prepare(frame, true, (int)OcrEngine.MaxImageDimension), token).ConfigureAwait(false);
        return text;
    }

    private async Task<string> ReadAsync(Frame frame, CancellationToken token)
    {
        using var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, frame.Width, frame.Height, BitmapAlphaMode.Ignore);
        bitmap.CopyFromBuffer(frame.Pixels.AsBuffer());
        var result = await engine.RecognizeAsync(bitmap).AsTask(token).ConfigureAwait(false);
        return string.Join('\n', result.Lines.Select(line => line.Text));
    }
}
