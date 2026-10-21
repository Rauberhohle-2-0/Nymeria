using Nymeria.Core.Materials;

namespace Nymeria.Formats.Bmp.Tests;

public sealed class BmpImageDecoderTests
{
    private static readonly BmpImageDecoder Decoder = new("fixture.bmp");

    [Fact]
    public void Decode_ConvertsTwentyFourBitBottomUpRowsToTopDownRgba()
    {
        byte[] bottomRow = BmpTestFileBuilder.Bgr(
            (0, 0, 255),
            (255, 255, 255));
        byte[] topRow = BmpTestFileBuilder.Bgr(
            (255, 0, 0),
            (0, 255, 0));
        byte[] file = BmpTestFileBuilder.Build(
            2,
            2,
            24,
            BmpTestFileBuilder.Rows(2, 24, bottomRow, topRow));

        TextureData texture = Decoder.Decode(new MemoryStream(file));

        Assert.Equal(2, texture.Width);
        Assert.Equal(2, texture.Height);
        Assert.Equal(1, texture.MipLevelCount);
        Assert.Equal(8, texture.RowPitch);
        Assert.Equal("fixture.bmp", texture.Name);
        Assert.Equal(
            new byte[]
            {
                255, 0, 0, 255,
                0, 255, 0, 255,
                0, 0, 255, 255,
                255, 255, 255, 255,
            },
            texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_HonorsNegativeHeightForTopDownRows()
    {
        byte[] topRow = BmpTestFileBuilder.Bgr(
            (255, 0, 0),
            (0, 255, 0));
        byte[] bottomRow = BmpTestFileBuilder.Bgr(
            (0, 0, 255),
            (255, 255, 255));
        byte[] file = BmpTestFileBuilder.Build(
            2,
            2,
            24,
            BmpTestFileBuilder.Rows(2, 24, topRow, bottomRow),
            declaredHeight: -2);

        TextureData texture = Decoder.Decode(new MemoryStream(file));

        Assert.Equal(
            new byte[]
            {
                255, 0, 0, 255,
                0, 255, 0, 255,
                0, 0, 255, 255,
                255, 255, 255, 255,
            },
            texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_SkipsFourByteRowPadding()
    {
        byte[] row = BmpTestFileBuilder.Bgr(
            (1, 2, 3),
            (4, 5, 6),
            (7, 8, 9));
        byte[] file = BmpTestFileBuilder.Build(
            3,
            1,
            24,
            BmpTestFileBuilder.Rows(3, 24, row));

        TextureData texture = Decoder.Decode(new MemoryStream(file));

        Assert.Equal(
            new byte[]
            {
                1, 2, 3, 255,
                4, 5, 6, 255,
                7, 8, 9, 255,
            },
            texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_PreservesAlphaFromV4HeaderMask()
    {
        byte[] pixel = BmpTestFileBuilder.Bgra((10, 20, 30, 40));
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            32,
            pixel,
            headerSize: 108,
            headerMasks: BmpTestFileBuilder.StandardBgraMasks);

        TextureData texture = Decoder.Decode(new MemoryStream(file));

        Assert.Equal(new byte[] { 10, 20, 30, 40 }, texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_TreatsThirtyTwoBitRgbWithoutAlphaMaskAsOpaque()
    {
        byte[] pixel = BmpTestFileBuilder.Bgra((10, 20, 30, 0));
        byte[] file = BmpTestFileBuilder.Build(1, 1, 32, pixel);

        TextureData texture = Decoder.Decode(new MemoryStream(file));

        Assert.Equal(new byte[] { 10, 20, 30, 255 }, texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_TreatsV4HeaderWithoutAlphaMaskAsOpaque()
    {
        byte[] pixel = BmpTestFileBuilder.Bgra((10, 20, 30, 0));
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            32,
            pixel,
            headerSize: 124,
            headerMasks: BmpTestFileBuilder.StandardBgrMasks);

        TextureData texture = Decoder.Decode(new MemoryStream(file));

        Assert.Equal(new byte[] { 10, 20, 30, 255 }, texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_HonorsBitFieldsWithTrailingAlphaMask()
    {
        byte[] pixel = BmpTestFileBuilder.Bgra((1, 2, 3, 4));
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            32,
            pixel,
            compression: BmpTestFileBuilder.BiBitFields,
            trailingMasks: BmpTestFileBuilder.StandardBgraMasks);

        TextureData texture = Decoder.Decode(new MemoryStream(file));

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_HonorsBitFieldsWithoutAlphaMask()
    {
        byte[] pixel = BmpTestFileBuilder.Bgra((1, 2, 3, 4));
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            32,
            pixel,
            compression: BmpTestFileBuilder.BiBitFields,
            trailingMasks: BmpTestFileBuilder.StandardBgrMasks);

        TextureData texture = Decoder.Decode(new MemoryStream(file));

        Assert.Equal(new byte[] { 1, 2, 3, 255 }, texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_RejectsNonStandardChannelMasks()
    {
        byte[] pixel = BmpTestFileBuilder.Bgra((1, 2, 3, 4));
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            32,
            pixel,
            compression: BmpTestFileBuilder.BiBitFields,
            trailingMasks: [0x0000FF00, 0x00FF0000, 0x000000FF]);

        BmpFormatException exception = DecodeError(file);

        Assert.Contains("channel masks", exception.Message);
    }

    [Fact]
    public void Decode_RejectsNonStandardAlphaMask()
    {
        byte[] pixel = BmpTestFileBuilder.Bgra((1, 2, 3, 4));
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            32,
            pixel,
            headerSize: 108,
            headerMasks: [0x00FF0000, 0x0000FF00, 0x000000FF, 0x0000000F]);

        BmpFormatException exception = DecodeError(file);

        Assert.Contains("alpha mask", exception.Message);
    }

    [Fact]
    public void Decode_ExpandsEightBitPaletteIndices()
    {
        byte[] palette = BmpTestFileBuilder.Palette(
            (0, 0, 255, 0),
            (255, 0, 0, 0));
        byte[] indices = BmpTestFileBuilder.Rows(2, 8, [1, 0]);
        byte[] file = BmpTestFileBuilder.Build(
            2,
            1,
            8,
            indices,
            palette: palette);

        TextureData texture = Decoder.Decode(new MemoryStream(file));

        Assert.Equal(
            new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 },
            texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_DefaultsEightBitPaletteToTwoHundredFiftySixEntries()
    {
        var entries = new (byte R, byte G, byte B, byte Reserved)[256];
        entries[255] = (12, 34, 56, 0);
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            8,
            BmpTestFileBuilder.Rows(1, 8, [255]),
            palette: BmpTestFileBuilder.Palette(entries));

        TextureData texture = Decoder.Decode(new MemoryStream(file));

        Assert.Equal(new byte[] { 12, 34, 56, 255 }, texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_RejectsPaletteIndexOutsideThePalette()
    {
        byte[] palette = BmpTestFileBuilder.Palette((0, 0, 255, 0));
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            8,
            BmpTestFileBuilder.Rows(1, 8, [1]),
            palette: palette,
            declaredPaletteCount: 1);

        BmpFormatException exception = DecodeError(file);

        Assert.Contains("palette index 1", exception.Message);
        Assert.Contains("only 1 entries", exception.Message);
    }

    [Fact]
    public void Decode_RejectsPaletteLargerThanTheImageDeclares()
    {
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            8,
            BmpTestFileBuilder.Rows(1, 8, [0]),
            palette: BmpTestFileBuilder.Palette((0, 0, 255, 0)),
            declaredPaletteCount: 257);

        BmpFormatException exception = DecodeError(file);

        Assert.Contains("257 palette entries", exception.Message);
    }

    [Fact]
    public void Decode_RejectsPaletteThatOverlapsThePixelArray()
    {
        byte[] palette = BmpTestFileBuilder.Palette(
            (0, 0, 255, 0),
            (0, 0, 255, 0));
        byte[] complete = BmpTestFileBuilder.Build(
            1,
            1,
            8,
            BmpTestFileBuilder.Rows(1, 8, [0]),
            palette: palette);
        byte[] file = BmpTestFileBuilder.WithPixelOffset(complete, 58);

        BmpFormatException exception = DecodeError(file);

        Assert.Contains("palette", exception.Message);
        Assert.Contains("after the pixel array offset", exception.Message);
    }

    [Fact]
    public void Decode_AcceptsDeclaredImageSizeThatMatchesTheStride()
    {
        byte[] row = BmpTestFileBuilder.Bgr((1, 2, 3), (4, 5, 6), (7, 8, 9));
        byte[] file = BmpTestFileBuilder.Build(
            3,
            1,
            24,
            BmpTestFileBuilder.Rows(3, 24, row),
            declaredImageSize: 12);

        TextureData texture = Decoder.Decode(new MemoryStream(file));

        Assert.Equal(3, texture.Width);
    }

    [Fact]
    public void Decode_RejectsDeclaredImageSizeThatDoesNotMatchTheStride()
    {
        byte[] file = BmpTestFileBuilder.Build(
            2,
            2,
            24,
            BmpTestFileBuilder.Rows(
                2,
                24,
                BmpTestFileBuilder.Bgr((1, 2, 3), (4, 5, 6)),
                BmpTestFileBuilder.Bgr((7, 8, 9), (1, 2, 3))),
            declaredImageSize: 7);

        BmpFormatException exception = DecodeError(file);

        Assert.Contains("declares a 7-byte image", exception.Message);
    }

    [Fact]
    public void Decode_RejectsOsTwoCoreHeaders()
    {
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            24,
            OneRow24(1, 2, 3));

        Assert.Contains(
            "BITMAPCOREHEADER",
            DecodeError(BmpTestFileBuilder.WithDibHeaderSize(file, 12)).Message);
    }

    [Theory]
    [InlineData(41u)]
    [InlineData(64u)]
    [InlineData(109u)]
    public void Decode_RejectsUnknownDibHeaderSizes(uint headerSize)
    {
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            24,
            OneRow24(1, 2, 3));

        Assert.Contains(
            "DIB header size",
            DecodeError(BmpTestFileBuilder.WithDibHeaderSize(file, headerSize)).Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(99)]
    public void Decode_RejectsUnsupportedCompression(uint compression)
    {
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            24,
            OneRow24(1, 2, 3),
            compression: compression);

        BmpFormatException exception = DecodeError(file);

        Assert.Contains("compression", exception.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(16)]
    public void Decode_RejectsUnsupportedPixelDepths(int bitCount)
    {
        byte[] file = BmpTestFileBuilder.Build(1, 1, bitCount, []);

        BmpFormatException exception = DecodeError(file);

        Assert.Contains($"{bitCount}-bit", exception.Message);
    }

    [Fact]
    public void Decode_RejectsBitFieldsForTwentyFourBitPixels()
    {
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            24,
            OneRow24(1, 2, 3),
            compression: BmpTestFileBuilder.BiBitFields,
            trailingMasks: BmpTestFileBuilder.StandardBgrMasks);

        Assert.Contains(
            "Only 32-bit BI_BITFIELDS",
            DecodeError(file).Message);
    }

    [Fact]
    public void Decode_RejectsMoreThanOneColorPlane()
    {
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            24,
            OneRow24(1, 2, 3),
            planeCount: 2);

        Assert.Contains("color planes", DecodeError(file).Message);
    }

    [Fact]
    public void Decode_RejectsBadSignature()
    {
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            24,
            OneRow24(1, 2, 3));
        file[0] = (byte)'C';

        Assert.Contains("signature", DecodeError(file).Message);
    }

    [Fact]
    public void Decode_RejectsFileSizeMismatch()
    {
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            24,
            OneRow24(1, 2, 3));

        BmpFormatException exception = DecodeError(
            BmpTestFileBuilder.WithFileSize(file, (uint)file.Length + 1));

        Assert.Contains("declares", exception.Message);
    }

    [Fact]
    public void Decode_RejectsZeroWidth()
    {
        byte[] file = BmpTestFileBuilder.Build(0, 1, 24, []);

        Assert.Contains("width is 0", DecodeError(file).Message);
    }

    [Fact]
    public void Decode_RejectsZeroHeight()
    {
        byte[] file = BmpTestFileBuilder.Build(1, 0, 24, []);

        Assert.Contains("height is 0", DecodeError(file).Message);
    }

    [Fact]
    public void Decode_RejectsDimensionsAboveTheLimit()
    {
        byte[] file = BmpTestFileBuilder.Build(
            4097,
            1,
            24,
            OneRow24(1, 2, 3));

        Assert.Contains("dimension limit", DecodeError(file).Message);
    }

    [Fact]
    public void Decode_RejectsAPixelOffsetInsideTheHeader()
    {
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            24,
            OneRow24(1, 2, 3));

        BmpFormatException exception = DecodeError(
            BmpTestFileBuilder.WithPixelOffset(file, 20));

        Assert.Contains("pixel array offset", exception.Message);
    }

    [Fact]
    public void Decode_RejectsPixelOffsetPastTheInput()
    {
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            24,
            OneRow24(1, 2, 3));

        BmpFormatException exception = DecodeError(
            BmpTestFileBuilder.WithPixelOffset(file, 1_000_000));

        Assert.Contains("outside", exception.Message);
    }

    [Fact]
    public void Decode_RejectsTruncatedPixelData()
    {
        byte[] complete = BmpTestFileBuilder.Build(
            2,
            1,
            24,
            BmpTestFileBuilder.Rows(
                2,
                24,
                BmpTestFileBuilder.Bgr((1, 2, 3), (4, 5, 6))));
        byte[] truncated = complete[..^1];
        truncated = BmpTestFileBuilder.WithFileSize(truncated, (uint)truncated.Length);

        BmpFormatException exception = DecodeError(truncated);

        Assert.Contains("pixel array", exception.Message);
        Assert.Contains("input ends", exception.Message);
    }

    [Fact]
    public void Decode_RejectsUnexpectedTrailingBytes()
    {
        byte[] complete = BmpTestFileBuilder.Build(
            1,
            1,
            24,
            OneRow24(1, 2, 3));
        byte[] trailing = new byte[complete.Length + 2];
        complete.CopyTo(trailing, 0);
        trailing = BmpTestFileBuilder.WithFileSize(trailing, (uint)trailing.Length);

        Assert.Contains("unexpected bytes", DecodeError(trailing).Message);
    }

    [Fact]
    public void Decode_RejectsTruncatedHeader()
    {
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            24,
            OneRow24(1, 2, 3));
        byte[] truncated = file[..10];

        Assert.Contains("file header is truncated", DecodeError(truncated).Message);
    }

    [Fact]
    public void Decode_RejectsEmptyInput()
    {
        Assert.Contains(
            "empty",
            DecodeError([]).Message);
    }

    [Fact]
    public void Decode_RejectsInputAboveTheConfiguredLimit()
    {
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            24,
            OneRow24(1, 2, 3));
        var limits = new BmpDecodeLimits(
            maxInputBytes: file.Length - 1,
            maxDimension: 4096,
            maxPixelBytes: 64 * 1024 * 1024);

        BmpFormatException exception = Assert.Throws<BmpFormatException>(
            () => new BmpImageDecoder("fixture.bmp", limits)
                .Decode(new MemoryStream(file)));

        Assert.Contains("limit", exception.Message);
    }

    [Fact]
    public void Decode_RejectsDecodedPayloadAboveTheConfiguredLimit()
    {
        byte[] file = BmpTestFileBuilder.Build(
            2,
            2,
            24,
            BmpTestFileBuilder.Rows(
                2,
                24,
                BmpTestFileBuilder.Bgr((1, 2, 3), (4, 5, 6)),
                BmpTestFileBuilder.Bgr((7, 8, 9), (1, 2, 3))));
        var limits = new BmpDecodeLimits(1024, 4096, maxPixelBytes: 8);

        BmpFormatException exception = Assert.Throws<BmpFormatException>(
            () => new BmpImageDecoder(null, limits).Decode(new MemoryStream(file)));

        Assert.Contains("pixel limit", exception.Message);
    }

    [Fact]
    public void Decode_RejectsDimensionsAboveTheConfiguredLimit()
    {
        byte[] file = BmpTestFileBuilder.Build(
            2,
            1,
            24,
            BmpTestFileBuilder.Rows(
                2,
                24,
                BmpTestFileBuilder.Bgr((1, 2, 3), (4, 5, 6))));
        var limits = new BmpDecodeLimits(1024, maxDimension: 1, 64 * 1024 * 1024);

        BmpFormatException exception = Assert.Throws<BmpFormatException>(
            () => new BmpImageDecoder(null, limits).Decode(new MemoryStream(file)));

        Assert.Contains("dimension limit", exception.Message);
    }

    [Fact]
    public void Decode_LeavesTheStreamOpenOnSuccess()
    {
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            24,
            OneRow24(1, 2, 3));
        using var stream = new MemoryStream(file);

        Decoder.Decode(stream);

        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Decode_LeavesTheStreamOpenOnFailure()
    {
        using var stream = new MemoryStream([1, 2, 3]);

        Assert.Throws<BmpFormatException>(() => Decoder.Decode(stream));
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Decode_ReadsFromTheStreamsCurrentPosition()
    {
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            24,
            OneRow24(1, 2, 3));
        byte[] prefixed = new byte[file.Length + 5];
        file.CopyTo(prefixed, 5);
        using var stream = new MemoryStream(prefixed);
        stream.Position = 5;

        TextureData texture = Decoder.Decode(stream);

        Assert.Equal(new byte[] { 1, 2, 3, 255 }, texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_AcceptsANonSeekableStream()
    {
        byte[] file = BmpTestFileBuilder.Build(
            1,
            1,
            24,
            OneRow24(1, 2, 3));

        TextureData texture = Decoder.Decode(new NonSeekableStream(file));

        Assert.Equal(new byte[] { 1, 2, 3, 255 }, texture.Pixels.ToArray());
    }

    [Fact]
    public void Decode_RejectsNullAndUnreadableStreams()
    {
        Assert.Throws<ArgumentNullException>(() => Decoder.Decode(null!));
        Assert.Throws<ArgumentException>(() => Decoder.Decode(new UnreadableStream()));
    }

    [Fact]
    public void Decode_RejectsEveryTruncatedPrefixOfAValidFile()
    {
        byte[] complete = BmpTestFileBuilder.Build(
            3,
            2,
            32,
            BmpTestFileBuilder.Rows(
                3,
                32,
                BmpTestFileBuilder.Bgra(
                    (1, 2, 3, 4),
                    (5, 6, 7, 8),
                    (9, 10, 11, 12)),
                BmpTestFileBuilder.Bgra(
                    (13, 14, 15, 16),
                    (17, 18, 19, 20),
                    (21, 22, 23, 24))),
            headerSize: 108,
            headerMasks: BmpTestFileBuilder.StandardBgraMasks);

        for (int length = 0; length < complete.Length; length++)
        {
            byte[] prefix = complete[..length];
            byte[] sized = prefix.Length >= 6
                ? BmpTestFileBuilder.WithFileSize(prefix, (uint)prefix.Length)
                : prefix;

            Assert.Throws<BmpFormatException>(
                () => Decoder.Decode(new MemoryStream(sized)));
        }

        Decoder.Decode(new MemoryStream(complete));
    }

    [Fact]
    public void Decode_ParsesTheCheckedInTwentyFourBitFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "checker.bmp");

        using FileStream stream = File.OpenRead(path);
        TextureData texture = new BmpImageDecoder(path).Decode(stream);

        Assert.Equal(128, texture.Width);
        Assert.Equal(128, texture.Height);
        Assert.Equal(128 * 128 * 4, texture.ByteCount);

        // Red top-left quadrant, blue bottom-left quadrant, and opaque pixels.
        ReadOnlySpan<byte> pixels = texture.Pixels.Span;
        Assert.True(pixels[0] > pixels[2]);
        int bottomLeft = (127 * 128) * 4;
        Assert.True(pixels[bottomLeft + 2] > pixels[bottomLeft]);
        Assert.Equal(255, pixels[3]);
    }

    [Fact]
    public void Decode_ParsesTheCheckedInThirtyTwoBitTopDownFixture()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "checker-32.bmp");

        using FileStream stream = File.OpenRead(path);
        TextureData texture = new BmpImageDecoder(path).Decode(stream);

        Assert.Equal(128, texture.Width);
        Assert.Equal(128, texture.Height);
        ReadOnlySpan<byte> pixels = texture.Pixels.Span;
        Assert.True(pixels[0] > pixels[2]);
        int bottomLeft = (127 * 128) * 4;
        Assert.True(pixels[bottomLeft + 2] > pixels[bottomLeft]);
    }

    private static byte[] OneRow24(byte red, byte green, byte blue)
    {
        return BmpTestFileBuilder.Rows(
            1,
            24,
            BmpTestFileBuilder.Bgr((red, green, blue)));
    }

    private static BmpFormatException DecodeError(byte[] file)
    {
        return Assert.Throws<BmpFormatException>(
            () => Decoder.Decode(new MemoryStream(file)));
    }

    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableStream(byte[] data)
        {
            _inner = new MemoryStream(data, writable: false);
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            return _inner.Read(buffer);
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

    private sealed class UnreadableStream : MemoryStream
    {
        public override bool CanRead => false;
    }
}
