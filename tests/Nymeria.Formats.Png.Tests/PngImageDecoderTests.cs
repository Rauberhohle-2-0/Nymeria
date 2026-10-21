using System.IO.Compression;
using Nymeria.Core.Materials;

namespace Nymeria.Formats.Png.Tests;

public sealed class PngImageDecoderTests
{
    [Fact]
    public void Decode_ReturnsTopDownRgbaPixels()
    {
        byte[] pixels =
        [
            255, 0, 0, 255,
            0, 255, 0, 255,
            0, 0, 255, 255,
            255, 255, 255, 255,
        ];
        byte[] png = PngTestFileBuilder.CreatePng(
            2,
            2,
            scanlines: PngTestFileBuilder.ApplyFilter(pixels, 2, 2, 4, 0));

        TextureData texture = Decode(png);

        Assert.Equal(2, texture.Width);
        Assert.Equal(2, texture.Height);
        Assert.Equal(8, texture.RowPitch);
        Assert.Equal(16, texture.ByteCount);
        Assert.Equal(pixels, texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_ConvertsRgbToOpaqueRgba()
    {
        byte[] png = PngTestFileBuilder.CreatePng(
            1,
            1,
            colorType: 2,
            scanlines: PngTestFileBuilder.ApplyFilter([12, 34, 56], 1, 1, 3, 0));

        TextureData texture = Decode(png);

        Assert.Equal(new byte[] { 12, 34, 56, 255 }, texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_ConvertsGrayscaleAndGrayAlpha()
    {
        byte[] gray = PngTestFileBuilder.CreatePng(
            1,
            1,
            colorType: 0,
            scanlines: [0, 200]);
        byte[] grayAlpha = PngTestFileBuilder.CreatePng(
            1,
            1,
            colorType: 4,
            scanlines: PngTestFileBuilder.ApplyFilter([200, 128], 1, 1, 2, 0));

        Assert.Equal(new byte[] { 200, 200, 200, 255 }, Decode(gray).Pixels.ToArray());
        Assert.Equal(new byte[] { 200, 200, 200, 128 }, Decode(grayAlpha).Pixels.ToArray());
    }

    [Fact]
    public void Decode_UsesPaletteEntries()
    {
        byte[] palette = [255, 0, 0, 0, 0, 255];
        byte[] png = PngTestFileBuilder.CreatePng(
            1,
            1,
            colorType: 3,
            scanlines: [0, 1],
            palette: palette);

        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Decode(png).Pixels.ToArray());
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    [InlineData((byte)2)]
    [InlineData((byte)3)]
    [InlineData((byte)4)]
    public void Decode_AppliesPngScanlineFilters(byte filter)
    {
        byte[] pixels =
        [
            10, 20, 30,
            40, 50, 60,
            70, 80, 90,
            15, 25, 35,
            45, 55, 65,
            75, 85, 95,
        ];
        byte[] scanlines = PngTestFileBuilder.ApplyFilter(
            pixels,
            3,
            2,
            3,
            filter);
        byte[] png = PngTestFileBuilder.CreatePng(
            3,
            2,
            colorType: 2,
            scanlines: scanlines);

        TextureData texture = Decode(png);
        byte[] rgba = texture.Pixels.ToArray();
        var expected = new byte[24];

        for (int i = 0; i < 6; i++)
        {
            expected[i * 4] = pixels[i * 3];
            expected[i * 4 + 1] = pixels[i * 3 + 1];
            expected[i * 4 + 2] = pixels[i * 3 + 2];
            expected[i * 4 + 3] = 255;
        }

        Assert.Equal(expected, rgba);
    }

    [Fact]
    public void Decode_AcceptsNonSeekableInput()
    {
        byte[] png = PngTestFileBuilder.CreatePng(1, 1, scanlines: [0, 1, 2, 3, 4]);
        using var stream = new NonSeekableStream(png);

        TextureData texture = new PngImageDecoder().Decode(stream);

        Assert.Equal(1, texture.Width);
        Assert.Equal(1, texture.Height);
    }

    [Fact]
    public void Decode_LeavesCallerStreamOpen()
    {
        byte[] png = PngTestFileBuilder.CreatePng(1, 1, scanlines: [0, 1, 2, 3, 4]);
        using var stream = new MemoryStream(png);

        new PngImageDecoder().Decode(stream);

        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Decode_RejectsNullAndUnreadableStreams()
    {
        var decoder = new PngImageDecoder();

        Assert.Throws<ArgumentNullException>(() => decoder.Decode(null!));
        Assert.Throws<ArgumentException>(
            () => decoder.Decode(new NonReadableStream()));
    }

    [Fact]
    public void Decode_RejectsInvalidOrEmptySignature()
    {
        var decoder = new PngImageDecoder();
        byte[] empty = [];
        byte[] invalid = PngTestFileBuilder.CreatePng(1, 1, scanlines: [0, 1, 2, 3, 4]);
        invalid[0] = 0;

        Assert.Throws<PngFormatException>(() => decoder.Decode(new MemoryStream(empty)));
        Assert.Throws<PngFormatException>(() => decoder.Decode(new MemoryStream(invalid)));
    }

    [Fact]
    public void Decode_RejectsTruncatedChunkHeaderAndPayload()
    {
        byte[] png = PngTestFileBuilder.CreatePng(1, 1, scanlines: [0, 1, 2, 3, 4]);
        var decoder = new PngImageDecoder();

        Assert.Throws<PngFormatException>(
            () => decoder.Decode(new MemoryStream(png[..^20])));
        Assert.Throws<PngFormatException>(
            () => decoder.Decode(new MemoryStream(png[..20])));
    }

    [Fact]
    public void Decode_RejectsInvalidCrc()
    {
        byte[] png = PngTestFileBuilder.CreatePng(1, 1, scanlines: [0, 1, 2, 3, 4]);
        png[^1] ^= 0xFF;

        PngFormatException exception = Assert.Throws<PngFormatException>(
            () => Decode(png));

        Assert.Contains("CRC", exception.Message);
    }

    [Fact]
    public void Decode_RejectsUnknownCriticalChunk()
    {
        byte[] png = InsertBeforeIend(
            PngTestFileBuilder.CreatePng(1, 1, scanlines: [0, 1, 2, 3, 4]),
            PngTestFileBuilder.CreateChunk("ABCD", []));

        PngFormatException exception = Assert.Throws<PngFormatException>(
            () => Decode(png));

        Assert.Contains("ABCD", exception.Message);
    }

    [Fact]
    public void Decode_IgnoresUnknownAncillaryChunk()
    {
        byte[] png = InsertBeforeIend(
            PngTestFileBuilder.CreatePng(1, 1, scanlines: [0, 1, 2, 3, 4]),
            PngTestFileBuilder.CreateChunk("tEXt", [0, 1]));

        Assert.Equal(1, Decode(png).Width);
    }

    [Fact]
    public void Decode_RejectsIhdrThatIsNotFirst()
    {
        byte[] png = PngTestFileBuilder.CreatePng(1, 1, scanlines: [0, 1, 2, 3, 4]);
        byte[] prefix = PngTestFileBuilder.CreateChunk("tEXt", []);
        byte[] withPrefix = [.. PngTestFileBuilder.Signature, .. prefix, .. png[8..]];

        PngFormatException exception = Assert.Throws<PngFormatException>(
            () => Decode(withPrefix));

        Assert.Contains("IHDR first", exception.Message);
    }

    [Fact]
    public void Decode_RejectsNonLetterChunkType()
    {
        byte[] png = PngTestFileBuilder.CreatePng(1, 1, scanlines: [0, 1, 2, 3, 4]);
        byte[] chunk = PngTestFileBuilder.CreateChunk("AB1D", []);
        byte[] malformed = InsertBeforeIend(png, chunk);

        Assert.Throws<PngFormatException>(() => Decode(malformed));
    }

    [Fact]
    public void Decode_RejectsInterlacedImages()
    {
        byte[] png = PngTestFileBuilder.CreatePng(
            1,
            1,
            scanlines: [0, 1, 2, 3, 4],
            interlace: true);

        PngFormatException exception = Assert.Throws<PngFormatException>(
            () => Decode(png));

        Assert.Contains("Interlaced", exception.Message);
    }

    [Theory]
    [InlineData((byte)4)]
    [InlineData((byte)16)]
    public void Decode_RejectsUnsupportedBitDepth(byte bitDepth)
    {
        byte[] png = PngTestFileBuilder.CreatePng(
            1,
            1,
            bitDepth: bitDepth,
            scanlines: [0, 1, 2, 3, 4]);

        Assert.Throws<PngFormatException>(() => Decode(png));
    }

    [Fact]
    public void Decode_RejectsUnsupportedColorType()
    {
        byte[] png = PngTestFileBuilder.CreatePng(
            1,
            1,
            colorType: 1,
            scanlines: [0, 1, 2, 3, 4]);

        Assert.Throws<PngFormatException>(() => Decode(png));
    }

    [Theory]
    [InlineData(0u, 1u)]
    [InlineData(1u, 0u)]
    public void Decode_RejectsZeroDimensions(uint width, uint height)
    {
        byte[] png = PngTestFileBuilder.CreatePng(
            width,
            height,
            scanlines: [0, 1, 2, 3, 4]);

        Assert.Throws<PngFormatException>(() => Decode(png));
    }

    [Fact]
    public void Decode_EnforcesDimensionBoundary()
    {
        var limits = new PngDecodeLimits(1024, 2, 64);
        var decoder = new PngImageDecoder(null, limits);
        byte[] accepted = PngTestFileBuilder.CreatePng(
            2,
            2,
            scanlines: PngTestFileBuilder.ApplyFilter(new byte[16], 2, 2, 4, 0));
        byte[] rejected = PngTestFileBuilder.CreatePng(
            3,
            1,
            scanlines: PngTestFileBuilder.ApplyFilter(new byte[12], 3, 1, 4, 0));

        Assert.Equal(2, decoder.Decode(new MemoryStream(accepted)).Width);
        Assert.Throws<PngFormatException>(
            () => decoder.Decode(new MemoryStream(rejected)));
    }

    [Fact]
    public void Decode_EnforcesDecodedPixelByteLimit()
    {
        var limits = new PngDecodeLimits(1024, 16, 12);
        var decoder = new PngImageDecoder(null, limits);
        byte[] png = PngTestFileBuilder.CreatePng(
            2,
            2,
            scanlines: PngTestFileBuilder.ApplyFilter(new byte[16], 2, 2, 4, 0));

        PngFormatException exception = Assert.Throws<PngFormatException>(
            () => decoder.Decode(new MemoryStream(png)));

        Assert.Contains("pixel limit", exception.Message);
    }

    [Fact]
    public void Decode_EnforcesEncodedInputBoundary()
    {
        byte[] png = PngTestFileBuilder.CreatePng(1, 1, scanlines: [0, 1, 2, 3, 4]);
        var exact = new PngImageDecoder(
            null,
            new PngDecodeLimits(png.Length, 16, 64));
        var tooSmall = new PngImageDecoder(
            null,
            new PngDecodeLimits(png.Length - 1, 16, 64));

        Assert.Equal(1, exact.Decode(new MemoryStream(png)).Width);
        Assert.Throws<PngFormatException>(
            () => tooSmall.Decode(new MemoryStream(png)));
    }

    [Fact]
    public void Decode_EnforcesEncodedInputBoundaryOnNonSeekableStream()
    {
        byte[] png = PngTestFileBuilder.CreatePng(1, 1, scanlines: [0, 1, 2, 3, 4]);
        var decoder = new PngImageDecoder(
            null,
            new PngDecodeLimits(png.Length - 1, 16, 64));

        Assert.Throws<PngFormatException>(
            () => decoder.Decode(new NonSeekableStream(png)));
    }

    [Fact]
    public void Decode_RejectsIndexedImageWithoutPalette()
    {
        byte[] png = PngTestFileBuilder.CreatePng(
            1,
            1,
            colorType: 3,
            scanlines: [0, 0]);

        PngFormatException exception = Assert.Throws<PngFormatException>(
            () => Decode(png));

        Assert.Contains("PLTE", exception.Message);
    }

    [Fact]
    public void Decode_RejectsInvalidPaletteIndex()
    {
        byte[] png = PngTestFileBuilder.CreatePng(
            1,
            1,
            colorType: 3,
            scanlines: [0, 1],
            palette: [10, 20, 30]);

        PngFormatException exception = Assert.Throws<PngFormatException>(
            () => Decode(png));

        Assert.Contains("palette index", exception.Message);
    }

    [Fact]
    public void Decode_RejectsPlteAfterIdat()
    {
        byte[] png = PngTestFileBuilder.CreatePng(
            1,
            1,
            colorType: 2,
            scanlines: PngTestFileBuilder.ApplyFilter([1, 2, 3], 1, 1, 3, 0));
        byte[] plte = PngTestFileBuilder.CreateChunk("PLTE", [1, 2, 3]);
        byte[] malformed = InsertBeforeIend(png, plte);

        PngFormatException exception = Assert.Throws<PngFormatException>(
            () => Decode(malformed));

        Assert.Contains("after IDAT", exception.Message);
    }

    [Fact]
    public void Decode_RejectsUnsupportedScanlineFilter()
    {
        byte[] png = PngTestFileBuilder.CreatePng(
            1,
            1,
            scanlines: [5, 1, 2, 3, 4]);

        PngFormatException exception = Assert.Throws<PngFormatException>(
            () => Decode(png));

        Assert.Contains("filter type", exception.Message);
    }

    [Fact]
    public void Decode_RejectsInvalidCompressedData()
    {
        byte[] png = PngTestFileBuilder.CreatePng(
            1,
            1,
            compressedOverride: [1, 2, 3, 4]);

        Assert.Throws<PngFormatException>(() => Decode(png));
    }

    [Fact]
    public void Decode_RejectsTruncatedCompressedData()
    {
        byte[] compressed = PngTestFileBuilder.Compress([0, 1, 2, 3, 4]);
        byte[] png = PngTestFileBuilder.CreatePng(
            1,
            1,
            compressedOverride: compressed[..(compressed.Length / 2)]);

        Assert.Throws<PngFormatException>(() => Decode(png));
    }

    [Fact]
    public void Decode_RejectsExtraDecompressedData()
    {
        byte[] png = PngTestFileBuilder.CreatePng(
            1,
            1,
            scanlines: [0, 1, 2, 3, 4, 5]);

        PngFormatException exception = Assert.Throws<PngFormatException>(
            () => Decode(png));

        Assert.Contains("beyond", exception.Message);
    }

    [Fact]
    public void Decode_RejectsMissingImageDataOrEnd()
    {
        var decoder = new PngImageDecoder();
        byte[] noIdat = PngTestFileBuilder.CreatePng(
            1,
            1,
            includeImageData: false);
        byte[] noEnd = PngTestFileBuilder.CreatePng(
            1,
            1,
            scanlines: [0, 1, 2, 3, 4],
            includeEnd: false);

        Assert.Throws<PngFormatException>(
            () => decoder.Decode(new MemoryStream(noIdat)));
        Assert.Throws<PngFormatException>(
            () => decoder.Decode(new MemoryStream(noEnd)));
    }

    [Fact]
    public void Decode_RejectsTrailingDataAfterEnd()
    {
        byte[] png = PngTestFileBuilder.CreatePng(1, 1, scanlines: [0, 1, 2, 3, 4]);
        byte[] trailing = [.. png, 1, 2, 3];

        Assert.Throws<PngFormatException>(() => Decode(trailing));
    }

    [Fact]
    public void Decode_RejectsDuplicateIhdr()
    {
        byte[] png = InsertBeforeIend(
            PngTestFileBuilder.CreatePng(1, 1, scanlines: [0, 1, 2, 3, 4]),
            PngTestFileBuilder.CreateChunk(
                "IHDR",
                PngTestFileBuilder.CreateHeader(1, 1)));

        PngFormatException exception = Assert.Throws<PngFormatException>(
            () => Decode(png));

        Assert.Contains("second IHDR", exception.Message);
    }

    [Fact]
    public void Decode_RejectsNonContiguousImageData()
    {
        byte[] scanlines = PngTestFileBuilder.ApplyFilter(
            [1, 2, 3, 4],
            1,
            1,
            4,
            0);
        byte[] compressed = PngTestFileBuilder.Compress(scanlines);
        int split = compressed.Length / 2;
        byte[] first = compressed[..split];
        byte[] second = compressed[split..];
        byte[] png =
        [
            .. PngTestFileBuilder.Signature,
            .. PngTestFileBuilder.CreateChunk(
                "IHDR",
                PngTestFileBuilder.CreateHeader(1, 1)),
            .. PngTestFileBuilder.CreateChunk("IDAT", first),
            .. PngTestFileBuilder.CreateChunk("tEXt", []),
            .. PngTestFileBuilder.CreateChunk("IDAT", second),
            .. PngTestFileBuilder.CreateChunk("IEND", []),
        ];

        PngFormatException exception = Assert.Throws<PngFormatException>(
            () => Decode(png));

        Assert.Contains("not contiguous", exception.Message);
    }

    [Fact]
    public void Decode_RejectsChunkLengthBeyondInput()
    {
        byte[] png = PngTestFileBuilder.CreatePng(1, 1, scanlines: [0, 1, 2, 3, 4]);
        png[8] = 0x7F;
        png[9] = 0xFF;
        png[10] = 0xFF;
        png[11] = 0xFF;

        Assert.Throws<PngFormatException>(() => Decode(png));
    }

    [Fact]
    public void Decode_IncludesSourceNameInDiagnostics()
    {
        var decoder = new PngImageDecoder("textures/broken.png");
        byte[] invalid = [1, 2, 3];

        PngFormatException exception = Assert.Throws<PngFormatException>(
            () => decoder.Decode(new MemoryStream(invalid)));

        Assert.Contains("textures/broken.png", exception.Message);
    }

    private static TextureData Decode(byte[] png)
    {
        using var stream = new MemoryStream(png);
        return new PngImageDecoder().Decode(stream);
    }

    private static byte[] InsertBeforeIend(byte[] png, byte[] chunk)
    {
        return [.. png[..^12], .. chunk, .. png[^12..]];
    }

    private sealed class NonReadableStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => 0;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
