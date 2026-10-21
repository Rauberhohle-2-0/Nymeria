using Nymeria.Core.Materials;

namespace Nymeria.Core.Tests;

public sealed class TextureMipTests
{
    [Fact]
    public void Constructor_AcceptsFullRgbaMipChain()
    {
        var levels = new[]
        {
            new ReadOnlyMemory<byte>(new byte[4 * 4 * 4]),
            new ReadOnlyMemory<byte>(new byte[2 * 2 * 4]),
            new ReadOnlyMemory<byte>(new byte[1 * 1 * 4]),
        };

        var texture = new TextureData(4, 4, levels, "chain");

        Assert.Equal(3, texture.MipLevelCount);
        Assert.Equal(84, texture.ByteCount);
        Assert.Equal(16, texture.RowPitch);
        Assert.Equal(64, texture.Pixels.Length);
        Assert.Equal(16, texture.GetMipPixels(1).Length);
        Assert.Equal(4, texture.GetMipPixels(2).Length);
        Assert.Equal(2, texture.GetMipWidth(1));
        Assert.Equal(1, texture.GetMipHeight(2));
    }

    [Fact]
    public void Constructor_RejectsMismatchedMipLevel()
    {
        var levels = new[]
        {
            new ReadOnlyMemory<byte>(new byte[64]),
            new ReadOnlyMemory<byte>(new byte[15]),
        };

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new TextureData(4, 4, levels));

        Assert.Contains("Mip level 1", exception.Message);
    }

    [Fact]
    public void Constructor_RejectsMipCountBeyondTheChain()
    {
        var levels = new[]
        {
            new ReadOnlyMemory<byte>(new byte[16]),
            new ReadOnlyMemory<byte>(new byte[4]),
            new ReadOnlyMemory<byte>(new byte[4]),
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TextureData(2, 2, levels));
    }

    [Fact]
    public void GetMipPixels_RejectsMissingLevel()
    {
        var texture = new TextureData(1, 1, new byte[4]);

        Assert.Throws<ArgumentOutOfRangeException>(() => texture.GetMipPixels(1));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(8, 8, 4)]
    [InlineData(4096, 4096, 13)]
    [InlineData(4096, 1, 13)]
    public void GetMaxMipLevelCount_MatchesDimensionHalving(
        int width,
        int height,
        int expected)
    {
        Assert.Equal(expected, Texture.GetMaxMipLevelCount(width, height));
    }
}
