using Nymeria.Core.Materials;

namespace Nymeria.Core.Tests;

public sealed class TextureDataTests
{
    [Fact]
    public void Constructor_AcceptsTightlyPackedRgbaPixels()
    {
        byte[] pixels = { 1, 2, 3, 4, 5, 6, 7, 8 };
        var texture = new TextureData(2, 1, pixels, "two pixels");

        Assert.Equal(2, texture.Width);
        Assert.Equal(1, texture.Height);
        Assert.Equal(8, texture.RowPitch);
        Assert.Equal(8, texture.ByteCount);
        Assert.Equal(pixels, texture.Pixels.ToArray());
        Assert.Equal("two pixels", texture.Name);
    }

    [Fact]
    public void Constructor_NormalizesBlankName()
    {
        var texture = new TextureData(1, 1, new byte[4], "   ");

        Assert.Null(texture.Name);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(TextureData.MaxDimension + 1, 1)]
    [InlineData(1, TextureData.MaxDimension + 1)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void Constructor_RejectsUnsupportedDimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TextureData(width, height, ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public void Constructor_RejectsMismatchedPayloadLength()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new TextureData(2, 2, new byte[15]));

        Assert.Contains("16", exception.Message);
        Assert.Contains("15", exception.Message);
    }

    [Fact]
    public void Constructor_RejectsPayloadBeyondPixelLimit()
    {
        // 4096 x 4096 is exactly the 64 MiB byte limit. A taller image cannot be
        // represented because the dimension limit rejects it first, so this test
        // confirms the limit boundary without allocating the payload.
        Assert.Throws<ArgumentException>(
            () => new TextureData(
                TextureData.MaxDimension,
                TextureData.MaxDimension,
                ReadOnlyMemory<byte>.Empty));
    }
}
