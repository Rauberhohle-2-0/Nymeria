using Nymeria.Core.Materials;

namespace Nymeria.Formats.Tga.Tests;

public sealed class TgaImageDecoderTests
{
    private static readonly TgaImageDecoder Decoder = new("fixture.tga");

    [Fact]
    public void Decode_ConvertsBgrToRgbaAndFlipsBottomOrigin()
    {
        // Two rows stored bottom-up: red, green (bottom), blue, white (top).
        byte[] pixels = TgaTestFileBuilder.Bgr(
            (255, 0, 0),
            (0, 255, 0),
            (0, 0, 255),
            (255, 255, 255));
        byte[] file = TgaTestFileBuilder.Uncompressed(2, 2, 24, pixels);

        TextureData texture = Decoder.Decode(new MemoryStream(file));

        Assert.Equal(2, texture.Width);
        Assert.Equal(2, texture.Height);
        Assert.Equal(1, texture.MipLevelCount);
        Assert.Equal(8, texture.RowPitch);
        Assert.Equal(
            new byte[] { 0, 0, 255, 255, 255, 255, 255, 255 },
            texture.Pixels.Span[..8].ToArray());
        Assert.Equal(
            new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 },
            texture.Pixels.Span[8..].ToArray());
    }

    [Fact]
    public void Decode_PreservesThirtyTwoBitAlpha()
    {
        byte[] pixels = TgaTestFileBuilder.Bgra((10, 20, 30, 40));
        // 0x28: top-left origin plus eight alpha bits.
        byte[] file = TgaTestFileBuilder.Uncompressed(1, 1, 32, pixels, descriptor: 0x28);

        TextureData texture = Decoder.Decode(new MemoryStream(file));

        Assert.Equal(new byte[] { 10, 20, 30, 40 }, texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_TreatsThirtyTwoBitPixelsAsOpaqueWithoutAlphaBits()
    {
        byte[] pixels = TgaTestFileBuilder.Bgra((1, 2, 3, 4));
        byte[] file = TgaTestFileBuilder.Uncompressed(1, 1, 32, pixels, descriptor: 0x20);

        TextureData texture = Decoder.Decode(new MemoryStream(file));

        Assert.Equal(new byte[] { 1, 2, 3, 255 }, texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_HonorsRightOrigin()
    {
        byte[] pixels = TgaTestFileBuilder.Bgr((255, 0, 0), (0, 255, 0));
        // 0x30: top-left plus right-to-left storage.
        byte[] file = TgaTestFileBuilder.Uncompressed(2, 1, 24, pixels, descriptor: 0x30);

        TextureData texture = Decoder.Decode(new MemoryStream(file));

        Assert.Equal(new byte[] { 0, 255, 0, 255, 255, 0, 0, 255 }, texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_ExpandsRunAndRawRlePackets()
    {
        byte[] packets = TgaTestFileBuilder.Combine(
            TgaTestFileBuilder.RleRun(2, 255, 0, 0),
            TgaTestFileBuilder.RleRaw((0, 255, 0, 255)));
        byte[] file = TgaTestFileBuilder.Rle(3, 1, 32, packets, descriptor: 0x28);

        TextureData texture = Decoder.Decode(new MemoryStream(file));

        Assert.Equal(
            new byte[] { 255, 0, 0, 255, 255, 0, 0, 255, 0, 255, 0, 255 },
            texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_UsesTheStandardRlePacketHeaderBits()
    {
        // Bit 7 set is a run packet: one BGRA pixel repeated twice. Bit 7
        // clear is a raw packet: one distinct BGRA pixel follows.
        byte[] packets =
        [
            0x81, 0, 0, 255, 255,
            0x00, 0, 255, 0, 255,
        ];
        byte[] file = TgaTestFileBuilder.Rle(3, 1, 32, packets, descriptor: 0x28);

        TextureData texture = Decoder.Decode(new MemoryStream(file));

        Assert.Equal(
            new byte[] { 255, 0, 0, 255, 255, 0, 0, 255, 0, 255, 0, 255 },
            texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_RejectsRlePacketBeyondPixelCount()
    {
        byte[] packets = TgaTestFileBuilder.RleRun(3, 1, 2, 3);
        byte[] file = TgaTestFileBuilder.Rle(2, 1, 32, packets, descriptor: 0x28);

        TgaFormatException exception = Assert.Throws<TgaFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("declares 3 pixels", exception.Message);
    }

    [Fact]
    public void Decode_RejectsTruncatedRleRawPacket()
    {
        var packets = new List<byte> { 0x01, 1, 2, 3, 4 }; // two raw pixels, one present
        byte[] file = TgaTestFileBuilder.Rle(2, 1, 32, packets.ToArray(), descriptor: 0x28);

        TgaFormatException exception = Assert.Throws<TgaFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("truncated", exception.Message);
    }

    [Fact]
    public void Decode_RejectsTruncatedRleRunPacket()
    {
        var packets = new List<byte> { 0x81 }; // two-pixel run, no pixel payload
        byte[] file = TgaTestFileBuilder.Rle(2, 1, 32, packets.ToArray(), descriptor: 0x28);

        TgaFormatException exception = Assert.Throws<TgaFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("truncated", exception.Message);
    }

    [Fact]
    public void Decode_RejectsUnexpectedTrailingBytes()
    {
        byte[] pixels = TgaTestFileBuilder.Bgr((1, 2, 3));
        byte[] file = TgaTestFileBuilder.Combine(
            TgaTestFileBuilder.Uncompressed(1, 1, 24, pixels),
            new byte[] { 0, 0, 0, 0 });

        TgaFormatException exception = Assert.Throws<TgaFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("unexpected bytes", exception.Message);
    }

    [Fact]
    public void Decode_AcceptsTgaTwoFooter()
    {
        byte[] pixels = TgaTestFileBuilder.Bgr((1, 2, 3));
        var footer = new byte[26];
        "TRUEVISION-XFILE.\0"u8.CopyTo(footer.AsSpan(8));
        byte[] file = TgaTestFileBuilder.Combine(
            TgaTestFileBuilder.Uncompressed(1, 1, 24, pixels),
            footer);

        TextureData texture = Decoder.Decode(new MemoryStream(file));

        Assert.Equal(new byte[] { 1, 2, 3, 255 }, texture.Pixels.ToArray());
    }

    [Theory]
    [InlineData(1, 0)] // color-mapped
    [InlineData(0, 1)] // color-mapped
    [InlineData(0, 3)] // grayscale
    [InlineData(0, 32)] // unsupported compressed type
    public void Decode_RejectsUnsupportedImageTypes(int colorMapType, int imageType)
    {
        byte[] header = TgaTestFileBuilder.Header(
            imageType,
            1,
            1,
            24,
            colorMapType: colorMapType);

        Assert.Throws<TgaFormatException>(
            () => Decoder.Decode(new MemoryStream(header)));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(16)]
    public void Decode_RejectsUnsupportedPixelDepths(int pixelDepth)
    {
        byte[] header = TgaTestFileBuilder.Header(
            TgaTestFileBuilder.UncompressedTrueColor,
            1,
            1,
            pixelDepth);

        TgaFormatException exception = Assert.Throws<TgaFormatException>(
            () => Decoder.Decode(new MemoryStream(header)));

        Assert.Contains($"{pixelDepth}-bit", exception.Message);
    }

    [Fact]
    public void Decode_RejectsInterleavedImages()
    {
        byte[] pixels = TgaTestFileBuilder.Bgr((1, 2, 3));
        byte[] file = TgaTestFileBuilder.Uncompressed(1, 1, 24, pixels, descriptor: 0x40);

        TgaFormatException exception = Assert.Throws<TgaFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("interleave", exception.Message);
    }

    [Fact]
    public void Decode_RejectsDimensionsBeyondTheLimit()
    {
        byte[] header = TgaTestFileBuilder.Header(
            TgaTestFileBuilder.UncompressedTrueColor,
            Texture.MaxDimension + 1,
            1,
            24);

        TgaFormatException exception = Assert.Throws<TgaFormatException>(
            () => Decoder.Decode(new MemoryStream(header)));

        Assert.Contains("dimension limit", exception.Message);
    }

    [Fact]
    public void Decode_RejectsZeroDimensions()
    {
        byte[] header = TgaTestFileBuilder.Header(
            TgaTestFileBuilder.UncompressedTrueColor,
            0,
            1,
            24);

        Assert.Throws<TgaFormatException>(() => Decoder.Decode(new MemoryStream(header)));
    }

    [Fact]
    public void Decode_RejectsTruncatedHeader()
    {
        TgaFormatException exception = Assert.Throws<TgaFormatException>(
            () => Decoder.Decode(new MemoryStream(new byte[10])));

        Assert.Contains("truncated", exception.Message);
    }

    [Fact]
    public void Decode_RejectsImageIdBeyondInput()
    {
        byte[] header = TgaTestFileBuilder.Header(
            TgaTestFileBuilder.UncompressedTrueColor,
            1,
            1,
            24,
            id: new byte[] { 1, 2, 3 });

        TgaFormatException exception = Assert.Throws<TgaFormatException>(
            () => Decoder.Decode(new MemoryStream(header[..(18 + 1)])));

        Assert.Contains("image ID", exception.Message);
    }

    [Fact]
    public void Decode_RejectsShortPixelData()
    {
        byte[] file = TgaTestFileBuilder.Combine(
            TgaTestFileBuilder.Header(TgaTestFileBuilder.UncompressedTrueColor, 2, 2, 24),
            new byte[4]);

        TgaFormatException exception = Assert.Throws<TgaFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));

        Assert.Contains("image data", exception.Message);
    }

    [Fact]
    public void Decode_HonorsConfiguredInputLimit()
    {
        byte[] file = TgaTestFileBuilder.Uncompressed(
            1,
            1,
            24,
            TgaTestFileBuilder.Bgr((1, 2, 3)));
        var decoder = new TgaImageDecoder(
            "fixture.tga",
            new TgaDecodeLimits(file.Length - 1, 16, 64));

        TgaFormatException exception = Assert.Throws<TgaFormatException>(
            () => decoder.Decode(new MemoryStream(file)));

        Assert.Contains("limit", exception.Message);
    }

    [Fact]
    public void Decode_HonorsConfiguredDimensionLimit()
    {
        byte[] file = TgaTestFileBuilder.Uncompressed(
            3,
            1,
            24,
            TgaTestFileBuilder.Bgr((1, 2, 3), (4, 5, 6), (7, 8, 9)));
        var decoder = new TgaImageDecoder(
            "fixture.tga",
            new TgaDecodeLimits(1024, 2, 64));

        TgaFormatException exception = Assert.Throws<TgaFormatException>(
            () => decoder.Decode(new MemoryStream(file)));

        Assert.Contains("dimension limit", exception.Message);
    }

    [Fact]
    public void Decode_HonorsConfiguredPixelLimit()
    {
        byte[] file = TgaTestFileBuilder.Uncompressed(
            2,
            1,
            24,
            TgaTestFileBuilder.Bgr((1, 2, 3), (4, 5, 6)));
        var decoder = new TgaImageDecoder(
            "fixture.tga",
            new TgaDecodeLimits(1024, 16, 4));

        TgaFormatException exception = Assert.Throws<TgaFormatException>(
            () => decoder.Decode(new MemoryStream(file)));

        Assert.Contains("pixel limit", exception.Message);
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
