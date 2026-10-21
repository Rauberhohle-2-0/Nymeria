using Nymeria.Core.Materials;

namespace Nymeria.Core.Tests;

public sealed class CompressedTextureDataTests
{
    [Fact]
    public void Constructor_AcceptsBlockCompressedMipChain()
    {
        var levels = new[]
        {
            new ReadOnlyMemory<byte>(new byte[32]), // 8 x 8 BC1: 2 x 2 blocks
            new ReadOnlyMemory<byte>(new byte[8]), // 4 x 4
            new ReadOnlyMemory<byte>(new byte[8]), // 2 x 2
            new ReadOnlyMemory<byte>(new byte[8]), // 1 x 1
        };

        var texture = new CompressedTextureData(
            8,
            8,
            CompressedTextureFormat.Bc1,
            isSrgb: true,
            levels,
            "blocks");

        Assert.Equal(8, texture.Width);
        Assert.Equal(8, texture.Height);
        Assert.Equal(4, texture.MipLevelCount);
        Assert.Equal(CompressedTextureFormat.Bc1, texture.Format);
        Assert.True(texture.IsSrgb);
        Assert.Equal(56, texture.ByteCount);
        Assert.Equal(8, texture.GetMipBlocks(2).Length);
        Assert.Equal(2, texture.GetMipWidth(2));
        Assert.Equal(1, texture.GetMipHeight(3));
        Assert.Equal("blocks", texture.Name);
    }

    [Fact]
    public void Constructor_RejectsMismatchedLevelLength()
    {
        var levels = new[] { new ReadOnlyMemory<byte>(new byte[31]) };

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new CompressedTextureData(
                8,
                8,
                CompressedTextureFormat.Bc1,
                isSrgb: false,
                levels));

        Assert.Contains("32", exception.Message);
        Assert.Contains("31", exception.Message);
    }

    [Fact]
    public void Constructor_RejectsMissingOrExcessiveMipLevels()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CompressedTextureData(
                4,
                4,
                CompressedTextureFormat.Bc1,
                isSrgb: false,
                Array.Empty<ReadOnlyMemory<byte>>()));

        var tooMany = new[]
        {
            new ReadOnlyMemory<byte>(new byte[8]),
            new ReadOnlyMemory<byte>(new byte[8]),
            new ReadOnlyMemory<byte>(new byte[8]),
            new ReadOnlyMemory<byte>(new byte[8]),
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CompressedTextureData(
                4,
                4,
                CompressedTextureFormat.Bc1,
                isSrgb: false,
                tooMany));
    }

    [Fact]
    public void Constructor_RejectsUndefinedFormat()
    {
        var levels = new[] { new ReadOnlyMemory<byte>(new byte[16]) };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CompressedTextureData(
                4,
                4,
                (CompressedTextureFormat)99,
                isSrgb: false,
                levels));
    }

    [Theory]
    [InlineData(CompressedTextureFormat.Bc1, 8)]
    [InlineData(CompressedTextureFormat.Bc4, 8)]
    [InlineData(CompressedTextureFormat.Bc2, 16)]
    [InlineData(CompressedTextureFormat.Bc3, 16)]
    [InlineData(CompressedTextureFormat.Bc5, 16)]
    [InlineData(CompressedTextureFormat.Bc7, 16)]
    public void GetBlockByteCount_ReturnsFormatBlockSize(
        CompressedTextureFormat format,
        int expected)
    {
        Assert.Equal(expected, CompressedTextureData.GetBlockByteCount(format));
    }

    [Theory]
    [InlineData(1, 1, 8)]
    [InlineData(4, 4, 8)]
    [InlineData(5, 5, 32)]
    [InlineData(8, 8, 32)]
    [InlineData(9, 9, 72)]
    public void GetLevelByteCount_RoundsUpToWholeBlocks(int width, int height, int expected)
    {
        Assert.Equal(
            expected,
            CompressedTextureData.GetLevelByteCount(
                CompressedTextureFormat.Bc1,
                width,
                height));
    }

    [Fact]
    public void GetMipBlocks_RejectsMissingLevel()
    {
        var texture = new CompressedTextureData(
            4,
            4,
            CompressedTextureFormat.Bc3,
            isSrgb: false,
            new[] { new ReadOnlyMemory<byte>(new byte[16]) });

        Assert.Throws<ArgumentOutOfRangeException>(() => texture.GetMipBlocks(1));
    }
}
