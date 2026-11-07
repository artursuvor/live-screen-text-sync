namespace LiveScreenTextSync.Core;

public static class FrameComparer
{
    public static bool HasChanged(Frame? previous, Frame current)
    {
        if (previous is null || previous.Width != current.Width || previous.Height != current.Height ||
            previous.Pixels.Length != current.Pixels.Length)
            return true;

        // Inspect every pixel, excluding alpha; do not miss changes in a small text field.
        var changed = 0;
        var threshold = Math.Max(1, current.Width * current.Height / 2000);
        for (var i = 0; i + 3 < current.Pixels.Length; i += 4)
        {
            if (Math.Abs(previous.Pixels[i] - current.Pixels[i]) > 20 ||
                Math.Abs(previous.Pixels[i + 1] - current.Pixels[i + 1]) > 20 ||
                Math.Abs(previous.Pixels[i + 2] - current.Pixels[i + 2]) > 20)
                if (++changed >= threshold) return true;
        }
        return false;
    }
}
