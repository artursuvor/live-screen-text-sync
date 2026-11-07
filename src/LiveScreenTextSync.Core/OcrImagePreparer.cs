namespace LiveScreenTextSync.Core;

public static class OcrImagePreparer
{
    public static Frame Prepare(Frame source, bool enhance, int maxDimension)
    {
        if (source.Width < 1 || source.Height < 1 || source.Pixels.Length != checked(source.Width * source.Height * 4))
            throw new ArgumentException("The OCR frame dimensions and pixel buffer do not match.", nameof(source));
        var longest = Math.Max(source.Width, source.Height);
        if (longest > maxDimension) throw new ArgumentException("The source exceeds the OCR image-size limit.", nameof(source));
        var padding = Math.Min(12, (maxDimension - longest) / 2);
        var wanted = source.Height <= 32 ? 4 : source.Height <= 64 ? 3 : source.Height <= 160 || enhance ? 2 : 1;
        var fit = Math.Min((maxDimension - 2 * padding) / source.Width, (maxDimension - 2 * padding) / source.Height);
        var memoryFit = (int)Math.Sqrt(12_000_000.0 / ((long)source.Width * source.Height));
        var scale = Math.Max(1, Math.Min(wanted, Math.Min(fit, memoryFit)));
        var width = source.Width * scale + 2 * padding;
        var height = source.Height * scale + 2 * padding;
        var output = new byte[checked(width * height * 4)];

        // The corners estimate the local background without treating a thin letter as noise.
        var corners = new[] { 0, (source.Width - 1) * 4, (source.Height - 1) * source.Width * 4, (source.Width * source.Height - 1) * 4 };
        var background = new byte[3];
        for (var channel = 0; channel < 3; channel++) background[channel] = (byte)(corners.Sum(index => source.Pixels[index + channel]) / 4);
        var invert = enhance && Luminance(background[0], background[1], background[2]) < 128;
        var backgroundValue = Transform(background[0], background[1], background[2], enhance, invert);
        for (var i = 0; i < output.Length; i += 4)
        {
            output[i] = backgroundValue.B; output[i + 1] = backgroundValue.G;
            output[i + 2] = backgroundValue.R; output[i + 3] = 255;
        }

        // Bilinear enlargement keeps one-pixel stems. A median filter before resizing can erase them.
        for (var y = 0; y < source.Height * scale; y++)
        {
            var fy = Math.Clamp((y + 0.5) / scale - 0.5, 0, source.Height - 1);
            var y0 = (int)fy; var y1 = Math.Min(y0 + 1, source.Height - 1); var wy = fy - y0;
            for (var x = 0; x < source.Width * scale; x++)
            {
                var fx = Math.Clamp((x + 0.5) / scale - 0.5, 0, source.Width - 1);
                var x0 = (int)fx; var x1 = Math.Min(x0 + 1, source.Width - 1); var wx = fx - x0;
                byte Sample(int channel)
                {
                    var top = source.Pixels[(y0 * source.Width + x0) * 4 + channel] * (1 - wx) + source.Pixels[(y0 * source.Width + x1) * 4 + channel] * wx;
                    var bottom = source.Pixels[(y1 * source.Width + x0) * 4 + channel] * (1 - wx) + source.Pixels[(y1 * source.Width + x1) * 4 + channel] * wx;
                    return (byte)Math.Round(top * (1 - wy) + bottom * wy);
                }
                var value = Transform(Sample(0), Sample(1), Sample(2), enhance, invert);
                var index = ((y + padding) * width + x + padding) * 4;
                output[index] = value.B; output[index + 1] = value.G; output[index + 2] = value.R;
            }
        }
        return new(width, height, output, source.Generation);
    }

    private static int Luminance(byte b, byte g, byte r) => (b * 29 + g * 150 + r * 77) >> 8;
    private static (byte B, byte G, byte R) Transform(byte b, byte g, byte r, bool enhance, bool invert)
    {
        if (!enhance) return (b, g, r);
        var luminance = Luminance(b, g, r);
        if (invert) luminance = 255 - luminance;
        var value = (byte)Math.Clamp((luminance - 128) * 1.25 + 128, 0, 255);
        return (value, value, value);
    }
}
