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
        var scale = preprocess && Math.Max(frame.Width, frame.Height) * 2 <= OcrEngine.MaxImageDimension ? 2 : 1;
        if (Math.Max(frame.Width, frame.Height) > OcrEngine.MaxImageDimension)
            throw new InvalidOperationException($"Select a smaller region (each dimension must be at most {OcrEngine.MaxImageDimension} pixels).");
        var pixels = preprocess ? Preprocess(frame, scale) : frame.Pixels;
        using var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, frame.Width * scale, frame.Height * scale, BitmapAlphaMode.Ignore);
        bitmap.CopyFromBuffer(pixels.AsBuffer());
        var result = await engine.RecognizeAsync(bitmap).AsTask(token).ConfigureAwait(false);
        return string.Join('\n', result.Lines.Select(line => line.Text));
    }

    private static byte[] Preprocess(Frame frame, int scale)
    {
        var width = frame.Width * scale;
        var height = frame.Height * scale;
        var result = new byte[width * height * 4];
        Span<byte> neighborhood = stackalloc byte[9];
        for (var y = 0; y < frame.Height; y++)
        for (var x = 0; x < frame.Width; x++)
        {
            // A median filter suppresses isolated pixel noise; a modest contrast boost keeps antialiasing.
            var count = 0;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var index = (Math.Clamp(y + dy, 0, frame.Height - 1) * frame.Width + Math.Clamp(x + dx, 0, frame.Width - 1)) * 4;
                neighborhood[count++] = (byte)((frame.Pixels[index] * 29 + frame.Pixels[index + 1] * 150 + frame.Pixels[index + 2] * 77) >> 8);
            }
            neighborhood.Sort();
            var value = (byte)Math.Clamp((neighborhood[4] - 128) * 1.15 + 128, 0, 255);
            for (var sy = 0; sy < scale; sy++)
            for (var sx = 0; sx < scale; sx++)
            {
                var index = ((y * scale + sy) * width + x * scale + sx) * 4;
                result[index] = result[index + 1] = result[index + 2] = value;
                result[index + 3] = 255;
            }
        }
        return result;
    }
}
