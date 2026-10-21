using Nymeria.Core.Materials;

namespace Nymeria.Formats.Dds.Tests;

public sealed class DdsTextureDecoderTests
{
    private static readonly DdsTextureDecoder Decoder = new("fixture.dds");

    [Fact]
    public void Decode_LegacyDxt1_ReturnsCompressedBc1()
    {
        byte[] payload = DdsTestFileBuilder.BlockPayload(8, 8, 8);
        byte[] file = DdsTestFileBuilder.Compressed(8, 8, "DXT1", payload);

        Texture texture = Decoder.Decode(new MemoryStream(file));
        var compressed = Assert.IsType<CompressedTextureData>(texture);

        Assert.Equal(8, compressed.Width);
        Assert.Equal(8, compressed.Height);
        Assert.Equal(CompressedTextureFormat.Bc1, compressed.Format);
        Assert.False(compressed.IsSrgb);
        Assert.Equal(1, compressed.MipLevelCount);
        Assert.Equal(32, compressed.ByteCount);
        Assert.Equal(payload, compressed.GetMipBlocks(0).ToArray());
        Assert.Equal("fixture.dds", compressed.Name);
    }

    [Theory]
    [InlineData("DXT3", CompressedTextureFormat.Bc2)]
    [InlineData("DXT5", CompressedTextureFormat.Bc3)]
    [InlineData("ATI2", CompressedTextureFormat.Bc5)]
    [InlineData("BC5U", CompressedTextureFormat.Bc5)]
    [InlineData("BC4U", CompressedTextureFormat.Bc4)]
    public void Decode_LegacyFourCc_MapsToFormat(string fourCc, CompressedTextureFormat expected)
    {
        int blockBytes = expected is CompressedTextureFormat.Bc1 or CompressedTextureFormat.Bc4
            ? 8
            : 16;
        byte[] payload = DdsTestFileBuilder.BlockPayload(8, 8, blockBytes);
        byte[] file = DdsTestFileBuilder.Compressed(
            8,
            8,
            fourCc,
            payload,
            blockBytes: blockBytes);

        var compressed = Assert.IsType<CompressedTextureData>(
            Decoder.Decode(new MemoryStream(file)));

        Assert.Equal(expected, compressed.Format);
        Assert.False(compressed.IsSrgb);
        Assert.Equal(payload.Length, compressed.GetMipBlocks(0).Length);
    }

    [Theory]
    [InlineData(98u, CompressedTextureFormat.Bc7, false)]
    [InlineData(99u, CompressedTextureFormat.Bc7, true)]
    [InlineData(72u, CompressedTextureFormat.Bc1, true)]
    [InlineData(75u, CompressedTextureFormat.Bc2, true)]
    [InlineData(78u, CompressedTextureFormat.Bc3, true)]
    public void Decode_Dx10Compressed_MapsFormatAndColorSpace(
        uint dxgi,
        CompressedTextureFormat expected,
        bool isSrgb)
    {
        int blockBytes = expected == CompressedTextureFormat.Bc1 ? 8 : 16;
        byte[] payload = DdsTestFileBuilder.BlockPayload(8, 8, blockBytes);
        byte[] file = DdsTestFileBuilder.Dx10(8, 8, dxgi, payload);

        var compressed = Assert.IsType<CompressedTextureData>(
            Decoder.Decode(new MemoryStream(file)));

        Assert.Equal(expected, compressed.Format);
        Assert.Equal(isSrgb, compressed.IsSrgb);
        Assert.Equal(1, compressed.MipLevelCount);
    }

    [Fact]
    public void Decode_CompressedMipChain_PreservesEveryLevel()
    {
        const int mipCount = 4;
        byte[] payload = DdsTestFileBuilder.MipChainPayload(8, 8, 8, mipCount);
        byte[] file = DdsTestFileBuilder.Compressed(8, 8, "DXT1", payload, mipCount);

        var compressed = Assert.IsType<CompressedTextureData>(
            Decoder.Decode(new MemoryStream(file)));

        Assert.Equal(mipCount, compressed.MipLevelCount);
        Assert.Equal(8, compressed.GetMipWidth(0));
        Assert.Equal(4, compressed.GetMipWidth(1));
        Assert.Equal(2, compressed.GetMipWidth(2));
        Assert.Equal(1, compressed.GetMipWidth(3));
        Assert.Equal(32, compressed.GetMipBlocks(0).Length);
        Assert.Equal(8, compressed.GetMipBlocks(1).Length);
        Assert.Equal(8, compressed.GetMipBlocks(2).Length);
        Assert.Equal(8, compressed.GetMipBlocks(3).Length);
        Assert.Equal(56, compressed.ByteCount);
    }

    [Fact]
    public void Decode_Dx10UncompressedRgba8_ReturnsTopDownRgba()
    {
        byte[] payload = { 10, 20, 30, 40, 50, 60, 70, 80 };
        byte[] file = DdsTestFileBuilder.Dx10(2, 1, 28, payload);

        var texture = Assert.IsType<TextureData>(Decoder.Decode(new MemoryStream(file)));

        Assert.Equal(2, texture.Width);
        Assert.Equal(1, texture.Height);
        Assert.Equal(payload, texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_UncompressedBgraMasks_SwapsChannels()
    {
        byte[] payload = { 30, 20, 10, 40 };
        byte[] file = DdsTestFileBuilder.Uncompressed(
            1,
            1,
            32,
            redMask: 0x00FF0000,
            greenMask: 0x0000FF00,
            blueMask: 0x000000FF,
            alphaMask: 0xFF000000,
            payload);

        var texture = Assert.IsType<TextureData>(Decoder.Decode(new MemoryStream(file)));

        Assert.Equal(new byte[] { 10, 20, 30, 40 }, texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_UncompressedRgbaMasks_KeepsChannelOrder()
    {
        byte[] payload = { 10, 20, 30, 40 };
        byte[] file = DdsTestFileBuilder.Uncompressed(
            1,
            1,
            32,
            redMask: 0x000000FF,
            greenMask: 0x0000FF00,
            blueMask: 0x00FF0000,
            alphaMask: 0xFF000000,
            payload);

        var texture = Assert.IsType<TextureData>(Decoder.Decode(new MemoryStream(file)));

        Assert.Equal(payload, texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_UncompressedTwentyFourBitBgr_IsOpaque()
    {
        byte[] payload = { 30, 20, 10 };
        byte[] file = DdsTestFileBuilder.Uncompressed(
            1,
            1,
            24,
            redMask: 0x00FF0000,
            greenMask: 0x0000FF00,
            blueMask: 0x000000FF,
            alphaMask: 0,
            payload,
            declaresAlpha: false);

        var texture = Assert.IsType<TextureData>(Decoder.Decode(new MemoryStream(file)));

        Assert.Equal(new byte[] { 10, 20, 30, 255 }, texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_UncompressedWithoutAlphaFlag_IsOpaque()
    {
        byte[] payload = { 10, 20, 30, 0 };
        byte[] file = DdsTestFileBuilder.Uncompressed(
            1,
            1,
            32,
            redMask: 0x000000FF,
            greenMask: 0x0000FF00,
            blueMask: 0x00FF0000,
            alphaMask: 0xFF000000,
            payload,
            declaresAlpha: false);

        var texture = Assert.IsType<TextureData>(Decoder.Decode(new MemoryStream(file)));

        Assert.Equal(new byte[] { 10, 20, 30, 255 }, texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_UncompressedMipChain_ConvertsEveryLevel()
    {
        const int mipCount = 3;
        byte[] payload = DdsTestFileBuilder.Pattern(64 + 16 + 4);
        byte[] file = DdsTestFileBuilder.Uncompressed(
            4,
            4,
            32,
            redMask: 0x000000FF,
            greenMask: 0x0000FF00,
            blueMask: 0x00FF0000,
            alphaMask: 0xFF000000,
            payload,
            mipCount,
            pitchOrLinearSize: 16);

        var texture = Assert.IsType<TextureData>(Decoder.Decode(new MemoryStream(file)));

        Assert.Equal(mipCount, texture.MipLevelCount);
        Assert.Equal(4, texture.GetMipWidth(0));
        Assert.Equal(2, texture.GetMipWidth(1));
        Assert.Equal(1, texture.GetMipWidth(2));
        Assert.Equal(64, texture.GetMipPixels(0).Length);
        Assert.Equal(16, texture.GetMipPixels(1).Length);
        Assert.Equal(4, texture.GetMipPixels(2).Length);
        Assert.Equal(84, texture.ByteCount);
    }

    [Theory]
    [InlineData("bc1.dds", CompressedTextureFormat.Bc1, false, 8)]
    [InlineData("bc2.dds", CompressedTextureFormat.Bc2, false, 16)]
    [InlineData("bc3.dds", CompressedTextureFormat.Bc3, false, 16)]
    [InlineData("bc4.dds", CompressedTextureFormat.Bc4, false, 8)]
    [InlineData("bc5.dds", CompressedTextureFormat.Bc5, false, 16)]
    [InlineData("bc7.dds", CompressedTextureFormat.Bc7, false, 16)]
    [InlineData("bc1-srgb.dds", CompressedTextureFormat.Bc1, true, 8)]
    public void Decode_CheckedInFixtures_MapEveryClaimedFormat(
        string fileName,
        CompressedTextureFormat expectedFormat,
        bool expectedSrgb,
        int blockBytes)
    {
        using FileStream stream = File.OpenRead(
            Path.Combine(AppContext.BaseDirectory, "Assets", fileName));

        var compressed = Assert.IsType<CompressedTextureData>(Decoder.Decode(stream));

        Assert.Equal(expectedFormat, compressed.Format);
        Assert.Equal(expectedSrgb, compressed.IsSrgb);
        Assert.Equal(8, compressed.Width);
        Assert.Equal(8, compressed.Height);
        Assert.Equal(4 * blockBytes, compressed.ByteCount);
        Assert.Equal(4 * blockBytes, compressed.GetMipBlocks(0).Length);
    }

    [Fact]
    public void Decode_RejectsWrongSignature()
    {
        byte[] file = DdsTestFileBuilder.Compressed(8, 8, "DXT1", new byte[32]);
        file[0] = (byte)'X';

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("signature", exception.Message);
    }

    [Fact]
    public void Decode_RejectsWrongHeaderSize()
    {
        byte[] file = DdsTestFileBuilder.PatchUInt32(
            DdsTestFileBuilder.Compressed(8, 8, "DXT1", new byte[32]),
            4,
            100);

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("header size", exception.Message);
    }

    [Fact]
    public void Decode_RejectsMissingCapsFlag()
    {
        byte[] file = DdsTestFileBuilder.ClearFlag(
            DdsTestFileBuilder.Compressed(8, 8, "DXT1", new byte[32]),
            DdsTestFileBuilder.FlagCaps);

        Assert.Throws<DdsFormatException>(() => Decoder.Decode(new MemoryStream(file)));
    }

    [Theory]
    [InlineData(0x200u)] // cubemap
    [InlineData(0x200000u)] // volume
    public void Decode_RejectsCubemapAndVolume(uint caps2)
    {
        byte[] file = DdsTestFileBuilder.Uncompressed(
            1,
            1,
            32,
            redMask: 0x000000FF,
            greenMask: 0x0000FF00,
            blueMask: 0x00FF0000,
            alphaMask: 0xFF000000,
            payload: new byte[4],
            caps2: caps2);

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("not supported", exception.Message);
    }

    [Fact]
    public void Decode_RejectsTextureDepthAboveOne()
    {
        byte[] file = DdsTestFileBuilder.Uncompressed(
            1,
            1,
            32,
            redMask: 0x000000FF,
            greenMask: 0x0000FF00,
            blueMask: 0x00FF0000,
            alphaMask: 0xFF000000,
            payload: new byte[4],
            depth: 2);

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("depth", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4097)]
    public void Decode_RejectsInvalidWidth(int width)
    {
        byte[] file = DdsTestFileBuilder.Uncompressed(
            width,
            1,
            32,
            redMask: 0x000000FF,
            greenMask: 0x0000FF00,
            blueMask: 0x00FF0000,
            alphaMask: 0xFF000000,
            payload: Array.Empty<byte>());

        Assert.Throws<DdsFormatException>(() => Decoder.Decode(new MemoryStream(file)));
    }

    [Fact]
    public void Decode_RejectsMipCountBeyondTheChain()
    {
        byte[] file = DdsTestFileBuilder.Compressed(8, 8, "DXT1", Array.Empty<byte>(), mipCount: 5);

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("mip levels", exception.Message);
    }

    [Fact]
    public void Decode_RejectsMipCountWithoutFlag()
    {
        byte[] payload = DdsTestFileBuilder.MipChainPayload(8, 8, 8, 2);
        byte[] file = DdsTestFileBuilder.SetMipCountWithoutFlag(
            DdsTestFileBuilder.Compressed(8, 8, "DXT1", payload, mipCount: 2),
            2);

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("MIPMAPCOUNT", exception.Message);
    }

    [Fact]
    public void Decode_RejectsTruncatedPayload()
    {
        byte[] payload = DdsTestFileBuilder.BlockPayload(8, 8, 8);
        byte[] file = DdsTestFileBuilder.Compressed(8, 8, "DXT1", payload[..^1]);

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("image data", exception.Message);
    }

    [Fact]
    public void Decode_RejectsTrailingBytes()
    {
        byte[] payload = DdsTestFileBuilder.BlockPayload(8, 8, 8);
        byte[] file = [.. DdsTestFileBuilder.Compressed(8, 8, "DXT1", payload), 0];

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("image data", exception.Message);
    }

    [Fact]
    public void Decode_RejectsUnsupportedFourCc()
    {
        byte[] file = DdsTestFileBuilder.Compressed(8, 8, "ABCD", Array.Empty<byte>());

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("ABCD", exception.Message);
    }

    [Fact]
    public void Decode_RejectsUnsupportedDxgiFormatWithGuidance()
    {
        byte[] file = DdsTestFileBuilder.Dx10(8, 8, 95, Array.Empty<byte>()); // BC6H

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("95", exception.Message);
        Assert.Contains("BC", exception.Message);
    }

    [Fact]
    public void Decode_RejectsDx10ThreeDimensionalTexture()
    {
        byte[] file = DdsTestFileBuilder.Dx10(
            8,
            8,
            98,
            Array.Empty<byte>(),
            resourceDimension: 4);

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("resource dimension", exception.Message);
    }

    [Fact]
    public void Decode_RejectsDx10TextureArray()
    {
        byte[] file = DdsTestFileBuilder.Dx10(
            8,
            8,
            98,
            Array.Empty<byte>(),
            arraySize: 2);

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("array size", exception.Message);
    }

    [Fact]
    public void Decode_RejectsDx10Cubemap()
    {
        byte[] file = DdsTestFileBuilder.Dx10(
            8,
            8,
            98,
            Array.Empty<byte>(),
            miscFlag: 4);

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("cubemap", exception.Message);
    }

    [Fact]
    public void Decode_RejectsUncompressedSixteenBit()
    {
        byte[] file = DdsTestFileBuilder.Uncompressed(
            1,
            1,
            16,
            redMask: 0xF800,
            greenMask: 0x07E0,
            blueMask: 0x001F,
            alphaMask: 0,
            payload: new byte[2],
            declaresAlpha: false);

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("16 bits", exception.Message);
    }

    [Fact]
    public void Decode_RejectsUnsupportedChannelMasks()
    {
        byte[] file = DdsTestFileBuilder.Uncompressed(
            1,
            1,
            32,
            redMask: 0xFFFF0000,
            greenMask: 0x0000FF00,
            blueMask: 0x000000FF,
            alphaMask: 0,
            payload: new byte[4],
            declaresAlpha: false);

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("channel masks", exception.Message);
    }

    [Fact]
    public void Decode_RejectsPaddedRowPitch()
    {
        byte[] file = DdsTestFileBuilder.Uncompressed(
            8,
            1,
            32,
            redMask: 0x000000FF,
            greenMask: 0x0000FF00,
            blueMask: 0x00FF0000,
            alphaMask: 0xFF000000,
            payload: new byte[32],
            pitchOrLinearSize: 100);

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("row pitch", exception.Message);
    }

    [Fact]
    public void Decode_RejectsWrongCompressedLinearSize()
    {
        byte[] payload = DdsTestFileBuilder.BlockPayload(8, 8, 8);
        byte[] file = DdsTestFileBuilder.Build(
            DdsTestFileBuilder.FlagCaps | DdsTestFileBuilder.FlagHeight |
            DdsTestFileBuilder.FlagWidth | DdsTestFileBuilder.FlagPixelFormat |
            DdsTestFileBuilder.FlagLinearSize,
            8,
            8,
            0,
            1,
            1,
            DdsTestFileBuilder.PixelFormatFourCc,
            DdsTestFileBuilder.FourCc("DXT1"),
            0,
            0,
            0,
            0,
            0,
            payload,
            linearSize: 999);

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("linear size", exception.Message);
    }

    [Fact]
    public void Decode_RejectsUncompressedSrgbDxgiFormat()
    {
        byte[] file = DdsTestFileBuilder.Dx10(1, 1, 29, new byte[4]);

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("sRGB", exception.Message);
    }

    [Fact]
    public void Decode_HonorsConfiguredInputLimit()
    {
        byte[] file = DdsTestFileBuilder.Compressed(
            8,
            8,
            "DXT1",
            DdsTestFileBuilder.BlockPayload(8, 8, 8));
        var decoder = new DdsTextureDecoder(
            "fixture.dds",
            new DdsDecodeLimits(file.Length - 1, 4096, 64 * 1024 * 1024));

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => decoder.Decode(new MemoryStream(file)));

        Assert.Contains("limit", exception.Message);
    }

    [Fact]
    public void Decode_HonorsConfiguredDimensionLimit()
    {
        byte[] file = DdsTestFileBuilder.Compressed(
            8,
            8,
            "DXT1",
            DdsTestFileBuilder.BlockPayload(8, 8, 8));
        var decoder = new DdsTextureDecoder(
            "fixture.dds",
            new DdsDecodeLimits(1024 * 1024, 4, 64 * 1024 * 1024));

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => decoder.Decode(new MemoryStream(file)));

        Assert.Contains("dimension limit", exception.Message);
    }

    [Fact]
    public void Decode_HonorsConfiguredPayloadLimit()
    {
        byte[] file = DdsTestFileBuilder.Compressed(
            8,
            8,
            "DXT1",
            DdsTestFileBuilder.BlockPayload(8, 8, 8));
        var decoder = new DdsTextureDecoder(
            "fixture.dds",
            new DdsDecodeLimits(1024 * 1024, 4096, 16));

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => decoder.Decode(new MemoryStream(file)));

        Assert.Contains("payload", exception.Message);
    }

    [Fact]
    public void Decode_RejectsUnrepresentableMipCountWithoutOverflowing()
    {
        byte[] payload = DdsTestFileBuilder.MipChainPayload(8, 8, 8, 2);
        byte[] file = DdsTestFileBuilder.PatchUInt32(
            DdsTestFileBuilder.Compressed(8, 8, "DXT1", payload, mipCount: 2),
            28,
            uint.MaxValue);

        DdsFormatException exception = Assert.Throws<DdsFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("mip levels", exception.Message);
    }

    [Fact]
    public void Decode_RejectsUnreadableStream()
    {
        Assert.Throws<ArgumentException>(
            () => Decoder.Decode(new UnreadableStream()));
    }

    private sealed class UnreadableStream : MemoryStream
    {
        public override bool CanRead => false;
    }
}
