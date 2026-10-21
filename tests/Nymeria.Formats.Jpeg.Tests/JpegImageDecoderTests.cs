using Nymeria.Core.Materials;

namespace Nymeria.Formats.Jpeg.Tests;

public sealed class JpegImageDecoderTests
{
    private static readonly JpegImageDecoder Decoder = new("checker.jpg");

    private static string FixturePath =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "checker.jpg");

    private static byte[] FixtureBytes => File.ReadAllBytes(FixturePath);

    [Fact]
    public void Decode_ReturnsTopDownOpaqueRgbaPixels()
    {
        using FileStream stream = File.OpenRead(FixturePath);

        TextureData texture = Decoder.Decode(stream);

        Assert.Equal(128, texture.Width);
        Assert.Equal(128, texture.Height);
        Assert.Equal(1, texture.MipLevelCount);
        Assert.Equal(128 * 128 * 4, texture.ByteCount);

        for (int offset = 3; offset < texture.ByteCount; offset += 4)
        {
            Assert.Equal(255, texture.Pixels.Span[offset]);
        }
    }

    [Fact]
    public void Decode_KeepsQuadrantColorsInStoredOrientation()
    {
        TextureData texture = Decoder.Decode(new MemoryStream(FixtureBytes));

        // Sample light checker cells in each quadrant; odd cells are dimmed.
        (byte R, byte G, byte B, byte A) topLeft = ReadPixel(texture, 4, 4);
        (byte R, byte G, byte B, byte A) topRight = ReadPixel(texture, 68, 4);
        (byte R, byte G, byte B, byte A) bottomLeft = ReadPixel(texture, 4, 68);
        (byte R, byte G, byte B, byte A) bottomRight = ReadPixel(texture, 68, 68);

        Assert.True(topLeft.R > 150 && topLeft.G < 100 && topLeft.B < 100);
        Assert.True(topRight.G > 140 && topRight.R < 120);
        Assert.True(bottomLeft.B > 150 && bottomLeft.R < 120);
        Assert.True(bottomRight.R > 150 && bottomRight.G > 130);
    }

    [Fact]
    public void Decode_RejectsTruncatedImage()
    {
        byte[] truncated = FixtureBytes[..300];

        JpegFormatException exception = Assert.Throws<JpegFormatException>(
            () => Decoder.Decode(new MemoryStream(truncated)));

        Assert.Contains("JPEG", exception.Message);
    }

    [Fact]
    public void Decode_RejectsNonJpegSignature()
    {
        byte[] png = { 137, 80, 78, 71, 13, 10, 26, 10 };

        JpegFormatException exception = Assert.Throws<JpegFormatException>(
            () => Decoder.Decode(new MemoryStream(png)));

        Assert.Contains("start-of-image", exception.Message);
    }

    [Fact]
    public void Decode_RejectsOversizedDimensionsBeforeDecoding()
    {
        byte[] patched = PatchFrameDimensions(FixtureBytes, 5000, 5000);

        JpegFormatException exception = Assert.Throws<JpegFormatException>(
            () => Decoder.Decode(new MemoryStream(patched)));

        Assert.Contains("dimension limit", exception.Message);
    }

    [Fact]
    public void Decode_LeavesTheStreamOpen()
    {
        using var stream = new MemoryStream(FixtureBytes);

        Decoder.Decode(stream);

        Assert.True(stream.CanRead);
        Assert.Equal(FixtureBytes.Length, stream.Length);
    }

    [Fact]
    public void Decode_AcceptsNonSeekableInput()
    {
        using var stream = new NonSeekableStream(FixtureBytes);

        TextureData texture = Decoder.Decode(stream);

        Assert.Equal(128, texture.Width);
    }

    [Fact]
    public void Decode_RejectsEmptyInput()
    {
        Assert.Throws<JpegFormatException>(
            () => Decoder.Decode(new MemoryStream(Array.Empty<byte>())));
    }

    [Fact]
    public void Decode_HonorsConfiguredInputLimit()
    {
        byte[] input = FixtureBytes;
        var decoder = new JpegImageDecoder(
            "checker.jpg",
            new JpegDecodeLimits(input.Length - 1, 4096, 64 * 1024 * 1024));

        JpegFormatException exception = Assert.Throws<JpegFormatException>(
            () => decoder.Decode(new MemoryStream(input)));

        Assert.Contains("limit", exception.Message);
    }

    [Fact]
    public void Decode_HonorsConfiguredDimensionLimit()
    {
        var decoder = new JpegImageDecoder(
            "checker.jpg",
            new JpegDecodeLimits(32 * 1024 * 1024, 64, 64 * 1024 * 1024));

        JpegFormatException exception = Assert.Throws<JpegFormatException>(
            () => decoder.Decode(new MemoryStream(FixtureBytes)));

        Assert.Contains("dimension limit", exception.Message);
    }

    [Fact]
    public void Decode_HonorsConfiguredPixelLimit()
    {
        var decoder = new JpegImageDecoder(
            "checker.jpg",
            new JpegDecodeLimits(32 * 1024 * 1024, 4096, 4));

        JpegFormatException exception = Assert.Throws<JpegFormatException>(
            () => decoder.Decode(new MemoryStream(FixtureBytes)));

        Assert.Contains("pixel limit", exception.Message);
    }

    [Fact]
    public void Decode_RejectsUnreadableStream()
    {
        Assert.Throws<ArgumentException>(
            () => Decoder.Decode(new UnreadableStream()));
    }

    private static (byte R, byte G, byte B, byte A) ReadPixel(TextureData texture, int x, int y)
    {
        ReadOnlySpan<byte> pixels = texture.Pixels.Span;
        int offset = (y * texture.Width + x) * 4;
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }

    private static byte[] PatchFrameDimensions(byte[] jpeg, ushort width, ushort height)
    {
        byte[] copy = jpeg.ToArray();

        for (int i = 0; i + 9 < copy.Length; i++)
        {
            if (copy[i] != 0xFF || copy[i + 1] is not (0xC0 or 0xC1 or 0xC2))
            {
                continue;
            }

            // FF C0, length (2), precision (1), height (2), width (2)
            copy[i + 5] = (byte)(height >> 8);
            copy[i + 6] = (byte)height;
            copy[i + 7] = (byte)(width >> 8);
            copy[i + 8] = (byte)width;
            return copy;
        }

        throw new InvalidOperationException("The fixture has no frame header.");
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableStream(byte[] data)
        {
            _inner = new MemoryStream(data);
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class UnreadableStream : MemoryStream
    {
        public override bool CanRead => false;
    }
}
