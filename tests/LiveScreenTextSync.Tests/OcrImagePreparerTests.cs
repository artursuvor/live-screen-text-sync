using LiveScreenTextSync.Core;

namespace LiveScreenTextSync.Tests;

public class OcrImagePreparerTests
{
    private static Frame ThinStem(bool darkBackground)
    {
        var pixels = new byte[16 * 8 * 4];
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 16; x++)
        {
            var value = (byte)((x == 8) == darkBackground ? 255 : 0);
            var index = (y * 16 + x) * 4;
            pixels[index] = pixels[index + 1] = pixels[index + 2] = value;
            pixels[index + 3] = 255;
        }
        return new(16, 8, pixels);
    }

    [Fact]
    public void EnlargesSmallRegionWithoutErasingOnePixelStems()
    {
        var image = OcrImagePreparer.Prepare(ThinStem(false), false, 4096);
        Assert.True(image.Height > 8 * 3);
        Assert.True(image.Pixels[(image.Height / 2 * image.Width + 12 + 8 * 4 + 2) * 4] < 128);
        Assert.Equal(255, image.Pixels[0]);
    }

    [Fact]
    public void NormalizesDarkBackgroundWithoutErasingLightText()
    {
        var image = OcrImagePreparer.Prepare(ThinStem(true), true, 4096);
        Assert.Equal(255, image.Pixels[0]);
        Assert.True(image.Pixels[(image.Height / 2 * image.Width + 12 + 8 * 4 + 2) * 4] < 128);
    }

    [Fact]
    public void LeavesRoomForPaddingWithinTheOcrDimensionLimit()
    {
        var image = OcrImagePreparer.Prepare(new(60, 8, new byte[60 * 8 * 4]), false, 64);
        Assert.InRange(image.Width, 1, 64);
        Assert.InRange(image.Height, 1, 64);
        Assert.Equal(image.Width * image.Height * 4, image.Pixels.Length);
    }

    [Fact]
    public void RejectsMismatchedOrOversizedBuffers()
    {
        Assert.Throws<ArgumentException>(() => OcrImagePreparer.Prepare(new(20, 10, []), false, 4096));
        Assert.Throws<ArgumentException>(() => OcrImagePreparer.Prepare(new(65, 1, new byte[65 * 4]), false, 64));
    }
}
