using System.Buffers.Binary;
using System.IO.Compression;
using Nymeria.Core.Materials;

namespace Nymeria.Formats.Png;

/// <summary>
/// Decodes a bounded subset of PNG into validated, top-down RGBA texture data.
/// </summary>
/// <remarks>
/// <para>
/// The decoder accepts non-interlaced, 8-bit PNG images with grayscale, RGB,
/// indexed, grayscale-alpha, or RGBA color, including palette images with a
/// <c>PLTE</c> chunk and <c>tRNS</c> transparency for grayscale, truecolor, and
/// indexed images. It rejects 16-bit images, interlaced images, unknown
/// critical chunks, invalid checksums, unsupported compression or filter
/// methods, malformed or misplaced transparency metadata, malformed image
/// data, and dimensions or payloads beyond its documented limits. Ancillary
/// chunks that do not affect the decoded pixels are ignored; alpha is preserved
/// in the pixel payload but the example renderer does not blend it.
/// </para>
/// <para>
/// The decoder treats the stream as untrusted input. It bounds the encoded
/// bytes, validates every chunk before use, uses checked arithmetic for sizes,
/// and limits decompression to the exact scanline payload implied by the
/// validated header. It does not dispose or close the caller's stream.
/// </para>
/// <para>
/// The supported subset, limits, and validation rules are documented in
/// <c>docs/MATERIALS.md</c>. The decoder has no SDL3, GPU, window, or platform
/// dependency.
/// </para>
/// </remarks>
public sealed class PngImageDecoder
{
    /// <summary>The largest accepted encoded PNG, in bytes.</summary>
    public const int MaxInputBytes = 32 * 1024 * 1024;

    /// <summary>
    /// The largest accepted width or height, in pixels.
    /// </summary>
    public const int MaxDimension = TextureData.MaxDimension;

    /// <summary>The largest accepted decoded RGBA payload, in bytes.</summary>
    public const int MaxPixelBytes = TextureData.MaxPixelBytes;

    private static ReadOnlySpan<byte> Signature =>
        [137, 80, 78, 71, 13, 10, 26, 10];

    private readonly string? _sourceName;
    private readonly PngDecodeLimits _limits;

    /// <summary>
    /// Creates a PNG decoder.
    /// </summary>
    /// <param name="sourceName">
    /// An optional file or source name included in diagnostics. It does not
    /// open any file or affect decoding.
    /// </param>
    public PngImageDecoder(string? sourceName = null)
        : this(sourceName, PngDecodeLimits.Default)
    {
    }

    internal PngImageDecoder(string? sourceName, PngDecodeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _sourceName = string.IsNullOrWhiteSpace(sourceName) ? null : sourceName;
        _limits = limits;
    }

    /// <summary>
    /// Decodes one PNG image from the supplied stream.
    /// </summary>
    /// <param name="stream">
    /// A readable stream positioned at the PNG signature. The caller owns and
    /// disposes it; the decoder leaves it open.
    /// </param>
    /// <returns>Validated, tightly packed, top-down RGBA pixels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">The stream is not readable.</exception>
    /// <exception cref="PngFormatException">
    /// The input is malformed, unsupported, or exceeds a documented limit.
    /// </exception>
    public TextureData Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
        {
            throw new ArgumentException("The stream must be readable.", nameof(stream));
        }

        using MemoryStream inputBuffer = ReadInput(stream);

        if (!inputBuffer.TryGetBuffer(out ArraySegment<byte> segment))
        {
            throw new InvalidOperationException("The bounded input buffer is not exposed.");
        }

        ReadOnlySpan<byte> input = segment.AsSpan(0, (int)inputBuffer.Length);

        if (input.Length < Signature.Length || !input[..Signature.Length].SequenceEqual(Signature))
        {
            throw Error("the input does not start with the PNG signature.");
        }

        uint width = 0;
        uint height = 0;
        byte colorType = 0;
        int channelCount = 0;
        byte[]? palette = null;
        int paletteEntryCount = 0;
        TransparencyInfo transparency = TransparencyInfo.None;
        bool hasHeader = false;
        bool hasPalette = false;
        bool hasTransparency = false;
        bool hasImageData = false;
        bool imageDataEnded = false;
        bool hasEnd = false;

        using var imageData = new MemoryStream();
        int offset = Signature.Length;

        while (offset < input.Length)
        {
            if (input.Length - offset < 12)
            {
                throw Error($"the chunk header at byte {offset} is truncated.");
            }

            uint declaredLength = BinaryPrimitives.ReadUInt32BigEndian(
                input.Slice(offset, 4));

            if (declaredLength > int.MaxValue)
            {
                throw Error(
                    $"the chunk at byte {offset} declares {declaredLength} data bytes, " +
                    "which is larger than the supported chunk size.");
            }

            int payloadLength = (int)declaredLength;

            if (payloadLength > input.Length - offset - 12)
            {
                throw Error(
                    $"the chunk at byte {offset} declares {payloadLength} data bytes, " +
                    "but the remaining input is too short.");
            }

            ReadOnlySpan<byte> chunkType = input.Slice(offset + 4, 4);
            ReadOnlySpan<byte> payload = input.Slice(offset + 8, payloadLength);
            ReadOnlySpan<byte> checksumBytes = input.Slice(
                offset + 8 + payloadLength,
                4);

            ValidateChunkType(chunkType, offset);

            uint expectedChecksum = BinaryPrimitives.ReadUInt32BigEndian(checksumBytes);
            uint actualChecksum = PngCrc32.Compute(chunkType, payload);

            if (actualChecksum != expectedChecksum)
            {
                throw Error(
                    $"the {TypeName(chunkType)} chunk at byte {offset} has an " +
                    $"invalid CRC (expected 0x{expectedChecksum:X8}, " +
                    $"computed 0x{actualChecksum:X8}).");
            }

            if (!hasHeader && !chunkType.SequenceEqual("IHDR"u8))
            {
                throw Error(
                    $"the first chunk at byte {offset} is {TypeName(chunkType)}, " +
                    "but PNG requires IHDR first.");
            }

            if (chunkType.SequenceEqual("IHDR"u8))
            {
                if (hasHeader)
                {
                    throw Error($"a second IHDR chunk appears at byte {offset}.");
                }

                if (payloadLength != 13)
                {
                    throw Error(
                        $"the IHDR chunk has {payloadLength} data bytes instead of 13.");
                }

                width = BinaryPrimitives.ReadUInt32BigEndian(payload[..4]);
                height = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(4, 4));
                byte bitDepth = payload[8];
                colorType = payload[9];
                byte compressionMethod = payload[10];
                byte filterMethod = payload[11];
                byte interlaceMethod = payload[12];

                ValidateHeader(
                    width,
                    height,
                    bitDepth,
                    colorType,
                    compressionMethod,
                    filterMethod,
                    interlaceMethod);

                channelCount = GetChannelCount(colorType);
                hasHeader = true;
            }
            else if (chunkType.SequenceEqual("PLTE"u8))
            {
                if (hasPalette)
                {
                    throw Error($"a second PLTE chunk appears at byte {offset}.");
                }

                if (hasImageData)
                {
                    throw Error($"the PLTE chunk at byte {offset} appears after IDAT.");
                }

                if (payloadLength == 0 || payloadLength > 768 || payloadLength % 3 != 0)
                {
                    throw Error(
                        $"the PLTE chunk has {payloadLength} bytes. A palette must " +
                        "contain 1 to 256 RGB entries.");
                }

                palette = payload.ToArray();
                paletteEntryCount = payloadLength / 3;
                hasPalette = true;
            }
            else if (chunkType.SequenceEqual("IDAT"u8))
            {
                if (!hasHeader)
                {
                    throw Error($"the IDAT chunk at byte {offset} appears before IHDR.");
                }

                if (imageDataEnded)
                {
                    throw Error(
                        $"the IDAT chunk at byte {offset} is not contiguous with " +
                        "the preceding image data.");
                }

                if (colorType == 3 && !hasPalette)
                {
                    throw Error(
                        $"the indexed image at byte {offset} has no PLTE chunk " +
                        "before its image data.");
                }

                imageData.Write(payload);
                hasImageData = true;
            }
            else if (chunkType.SequenceEqual("tRNS"u8))
            {
                if (hasTransparency)
                {
                    throw Error($"a second tRNS chunk appears at byte {offset}.");
                }

                if (hasImageData)
                {
                    throw Error(
                        $"the tRNS chunk at byte {offset} appears after the image " +
                        "data. PNG requires transparency before IDAT.");
                }

                transparency = ParseTransparency(
                    payload,
                    colorType,
                    hasPalette,
                    paletteEntryCount,
                    offset);
                hasTransparency = true;
            }
            else if (chunkType.SequenceEqual("IEND"u8))
            {
                if (payloadLength != 0)
                {
                    throw Error($"the IEND chunk at byte {offset} is not empty.");
                }

                if (!hasHeader)
                {
                    throw Error($"the IEND chunk at byte {offset} appears before IHDR.");
                }

                if (!hasImageData)
                {
                    throw Error($"the IEND chunk at byte {offset} appears before IDAT.");
                }

                int end = offset + 12 + payloadLength;

                if (end != input.Length)
                {
                    throw Error($"unexpected data follows IEND at byte {end}.");
                }

                hasEnd = true;
                offset = end;
                continue;
            }
            else
            {
                if (IsCritical(chunkType))
                {
                    throw Error(
                        $"the unsupported critical {TypeName(chunkType)} chunk " +
                        $"appears at byte {offset}.");
                }

                // Ancillary chunks such as gAMA or sRGB are ignored. The decoded
                // payload preserves alpha but the renderer does not blend and
                // performs no color-space conversion.
            }

            if (hasImageData && !chunkType.SequenceEqual("IDAT"u8))
            {
                // A non-IDAT chunk after image data ends the contiguous IDAT
                // sequence. A later IDAT is rejected above.
                imageDataEnded = true;
            }

            offset += 12 + payloadLength;
        }

        if (!hasHeader)
        {
            throw Error("the input contains no IHDR chunk.");
        }

        if (!hasImageData)
        {
            throw Error("the input contains no IDAT chunk.");
        }

        if (!hasEnd)
        {
            throw Error("the input has no IEND chunk.");
        }

        if (colorType == 3 && (!hasPalette || palette is null || paletteEntryCount == 0))
        {
            throw Error("the indexed image has no usable palette.");
        }

        return DecodePixels(
            width,
            height,
            colorType,
            channelCount,
            palette,
            paletteEntryCount,
            transparency,
            imageData);
    }

    private MemoryStream ReadInput(Stream stream)
    {
        if (stream.CanSeek)
        {
            long remaining;

            try
            {
                remaining = stream.Length - stream.Position;
            }
            catch (NotSupportedException)
            {
                remaining = -1;
            }

            if (remaining > _limits.MaxInputBytes)
            {
                throw Error(
                    $"the input is {remaining} bytes, which exceeds the supported " +
                    $"limit of {_limits.MaxInputBytes} bytes.");
            }
        }

        var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int total = 0;

        while (true)
        {
            int read = stream.Read(buffer, 0, buffer.Length);

            if (read == 0)
            {
                break;
            }

            if (read > _limits.MaxInputBytes - total)
            {
                throw Error(
                    $"the input is larger than the supported limit of " +
                    $"{_limits.MaxInputBytes} bytes.");
            }

            output.Write(buffer, 0, read);
            total += read;
        }

        return output;
    }

    private void ValidateHeader(
        uint width,
        uint height,
        byte bitDepth,
        byte colorType,
        byte compressionMethod,
        byte filterMethod,
        byte interlaceMethod)
    {
        if (width == 0 || height == 0)
        {
            throw Error(
                $"the image dimensions are {width} x {height}. Width and height " +
                "must be at least one pixel.");
        }

        if (width > _limits.MaxDimension || height > _limits.MaxDimension)
        {
            throw Error(
                $"the image dimensions are {width} x {height}, which exceeds the " +
                $"{_limits.MaxDimension}-pixel dimension limit.");
        }

        if (bitDepth != 8)
        {
            throw Error(
                $"the image uses {bitDepth}-bit pixels. Only 8-bit PNG images are " +
                "supported.");
        }

        if (colorType is not (0 or 2 or 3 or 4 or 6))
        {
            throw Error(
                $"the image uses unsupported color type {colorType}. Supported " +
                "types are 0 (gray), 2 (RGB), 3 (indexed), 4 (gray-alpha), and " +
                "6 (RGBA).");
        }

        if (compressionMethod != 0)
        {
            throw Error(
                $"the image uses compression method {compressionMethod}. PNG " +
                "defines only method 0.");
        }

        if (filterMethod != 0)
        {
            throw Error(
                $"the image uses filter method {filterMethod}. PNG defines only " +
                "method 0.");
        }

        if (interlaceMethod != 0)
        {
            throw Error(
                $"the image uses interlace method {interlaceMethod}. Interlaced " +
                "PNG images are not supported.");
        }

        long pixelBytes;

        try
        {
            pixelBytes = checked((long)width * height * TextureData.BytesPerPixel);
        }
        catch (OverflowException)
        {
            throw Error("the image dimensions overflow the decoded pixel size.");
        }

        if (pixelBytes > _limits.MaxPixelBytes)
        {
            throw Error(
                $"the decoded image would require {pixelBytes} bytes, which " +
                $"exceeds the {_limits.MaxPixelBytes}-byte pixel limit.");
        }
    }

    private TextureData DecodePixels(
        uint widthValue,
        uint heightValue,
        byte colorType,
        int channelCount,
        byte[]? palette,
        int paletteEntryCount,
        TransparencyInfo transparency,
        MemoryStream imageData)
    {
        int width = (int)widthValue;
        int height = (int)heightValue;
        int rowBytes = checked(width * channelCount);
        int scanlineBytes = checked(rowBytes + 1);
        int expectedSize = checked(height * scanlineBytes);
        byte[] scanlines = new byte[expectedSize];

        imageData.Position = 0;

        try
        {
            using var zlib = new ZLibStream(
                imageData,
                CompressionMode.Decompress,
                leaveOpen: true);
            int total = 0;

            while (total < scanlines.Length)
            {
                int read = zlib.Read(scanlines, total, scanlines.Length - total);

                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            if (total != scanlines.Length)
            {
                throw Error(
                    $"the compressed image data ended after {total} bytes, but " +
                    $"{scanlines.Length} scanline bytes are required.");
            }

            if (zlib.ReadByte() != -1)
            {
                throw Error(
                    "the compressed image data expands beyond the scanline payload " +
                    "implied by the validated header.");
            }
        }
        catch (InvalidDataException exception)
        {
            throw Error(
                $"the compressed image data is invalid: {exception.Message}",
                exception);
        }

        Unfilter(scanlines, height, rowBytes, channelCount);
        return ConvertToRgba(
            scanlines,
            width,
            height,
            rowBytes,
            channelCount,
            colorType,
            palette,
            paletteEntryCount,
            transparency);
    }

    private TransparencyInfo ParseTransparency(
        ReadOnlySpan<byte> payload,
        byte colorType,
        bool hasPalette,
        int paletteEntryCount,
        int offset)
    {
        switch (colorType)
        {
            case 0:
                {
                    if (payload.Length != 2)
                    {
                        throw Error(
                            $"the tRNS chunk at byte {offset} has {payload.Length} data " +
                            "bytes, but a grayscale image requires exactly 2.");
                    }

                    int sample = BinaryPrimitives.ReadUInt16BigEndian(payload);

                    if (sample > 255)
                    {
                        throw Error(
                            $"the tRNS chunk at byte {offset} specifies gray sample " +
                            $"{sample}, which is outside the 8-bit range 0 to 255.");
                    }

                    return new TransparencyInfo { HasGray = true, Gray = (byte)sample };
                }

            case 2:
                {
                    if (payload.Length != 6)
                    {
                        throw Error(
                            $"the tRNS chunk at byte {offset} has {payload.Length} data " +
                            "bytes, but a truecolor image requires exactly 6.");
                    }

                    int red = BinaryPrimitives.ReadUInt16BigEndian(payload[..2]);
                    int green = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(2, 2));
                    int blue = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(4, 2));

                    if (red > 255 || green > 255 || blue > 255)
                    {
                        throw Error(
                            $"the tRNS chunk at byte {offset} specifies the color " +
                            $"({red}, {green}, {blue}), but each sample must be in the " +
                            "8-bit range 0 to 255.");
                    }

                    return new TransparencyInfo
                    {
                        HasRgb = true,
                        Red = (byte)red,
                        Green = (byte)green,
                        Blue = (byte)blue,
                    };
                }

            case 3:
                {
                    if (!hasPalette)
                    {
                        throw Error(
                            $"the tRNS chunk at byte {offset} appears before PLTE. An " +
                            "indexed image requires the palette first.");
                    }

                    if (payload.Length == 0)
                    {
                        throw Error(
                            $"the tRNS chunk at byte {offset} is empty. It must provide " +
                            "one to {paletteEntryCount} alpha values.");
                    }

                    if (payload.Length > paletteEntryCount)
                    {
                        throw Error(
                            $"the tRNS chunk at byte {offset} has {payload.Length} alpha " +
                            $"values, but the palette has only {paletteEntryCount} entries.");
                    }

                    return new TransparencyInfo { PaletteAlpha = payload.ToArray() };
                }

            default:
                throw Error(
                    $"the tRNS chunk at byte {offset} is not allowed with color " +
                    $"type {colorType}. PNG permits transparency only for grayscale, " +
                    "truecolor, and indexed images.");
        }
    }

    private void Unfilter(
        byte[] scanlines,
        int height,
        int rowBytes,
        int bytesPerPixel)
    {
        int stride = rowBytes + 1;

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * stride;
            int filter = scanlines[rowStart];
            int dataStart = rowStart + 1;
            int previousStart = dataStart - stride;

            if (filter == 0)
            {
                continue;
            }

            if (filter is < 1 or > 4)
            {
                throw Error(
                    $"scanline {y} uses unsupported filter type {filter}. PNG " +
                    "defines filter types 0 through 4.");
            }

            for (int x = 0; x < rowBytes; x++)
            {
                int left = x >= bytesPerPixel
                    ? scanlines[dataStart + x - bytesPerPixel]
                    : 0;
                int up = y > 0 ? scanlines[previousStart + x] : 0;
                int raw = scanlines[dataStart + x];
                int predictor = filter switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) >> 1,
                    4 => Paeth(left, up, y > 0 && x >= bytesPerPixel
                        ? scanlines[previousStart + x - bytesPerPixel]
                        : 0),
                    _ => 0,
                };

                scanlines[dataStart + x] = (byte)((raw + predictor) & 0xFF);
            }
        }
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        int estimate = left + up - upLeft;
        int distanceLeft = Math.Abs(estimate - left);
        int distanceUp = Math.Abs(estimate - up);
        int distanceUpLeft = Math.Abs(estimate - upLeft);

        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft)
        {
            return left;
        }

        return distanceUp <= distanceUpLeft ? up : upLeft;
    }

    private TextureData ConvertToRgba(
        byte[] scanlines,
        int width,
        int height,
        int rowBytes,
        int channelCount,
        byte colorType,
        byte[]? palette,
        int paletteEntryCount,
        TransparencyInfo transparency)
    {
        int outputLength = checked(width * height * TextureData.BytesPerPixel);
        byte[] rgba = new byte[outputLength];
        int stride = rowBytes + 1;
        int output = 0;

        for (int y = 0; y < height; y++)
        {
            int source = y * stride + 1;

            for (int x = 0; x < width; x++)
            {
                switch (colorType)
                {
                    case 0:
                        {
                            byte gray = scanlines[source];
                            rgba[output] = gray;
                            rgba[output + 1] = gray;
                            rgba[output + 2] = gray;
                            rgba[output + 3] =
                                transparency.HasGray && gray == transparency.Gray
                                    ? (byte)0
                                    : (byte)255;
                            source += 1;
                            break;
                        }

                    case 2:
                        {
                            rgba[output] = scanlines[source];
                            rgba[output + 1] = scanlines[source + 1];
                            rgba[output + 2] = scanlines[source + 2];
                            rgba[output + 3] =
                                transparency.HasRgb &&
                                scanlines[source] == transparency.Red &&
                                scanlines[source + 1] == transparency.Green &&
                                scanlines[source + 2] == transparency.Blue
                                    ? (byte)0
                                    : (byte)255;
                            source += 3;
                            break;
                        }

                    case 3:
                        {
                            int index = scanlines[source];

                            if (palette is null || index >= paletteEntryCount)
                            {
                                throw Error(
                                    $"pixel ({x}, {y}) uses palette index {index}, but " +
                                    $"the palette has {paletteEntryCount} entries.");
                            }

                            int paletteOffset = index * 3;
                            rgba[output] = palette[paletteOffset];
                            rgba[output + 1] = palette[paletteOffset + 1];
                            rgba[output + 2] = palette[paletteOffset + 2];
                            rgba[output + 3] = transparency.PaletteAlpha is { } paletteAlpha &&
                                index < paletteAlpha.Length
                                    ? paletteAlpha[index]
                                    : (byte)255;
                            source += 1;
                            break;
                        }

                    case 4:
                        {
                            byte gray = scanlines[source];
                            rgba[output] = gray;
                            rgba[output + 1] = gray;
                            rgba[output + 2] = gray;
                            rgba[output + 3] = scanlines[source + 1];
                            source += 2;
                            break;
                        }

                    case 6:
                        {
                            rgba[output] = scanlines[source];
                            rgba[output + 1] = scanlines[source + 1];
                            rgba[output + 2] = scanlines[source + 2];
                            rgba[output + 3] = scanlines[source + 3];
                            source += 4;
                            break;
                        }

                    default:
                        throw new InvalidOperationException(
                            $"The validated color type {colorType} is not handled.");
                }

                output += TextureData.BytesPerPixel;
            }
        }

        return new TextureData(width, height, rgba, _sourceName);
    }

    private static int GetChannelCount(byte colorType)
    {
        return colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(colorType)),
        };
    }

    private static void ValidateChunkType(ReadOnlySpan<byte> chunkType, int offset)
    {
        foreach (byte value in chunkType)
        {
            if (value is not ((>= (byte)'A' and <= (byte)'Z') or
                              (>= (byte)'a' and <= (byte)'z')))
            {
                throw new PngFormatException(
                    $"The PNG image is invalid: the chunk at byte {offset} has a " +
                    "non-letter chunk type.");
            }
        }
    }

    private static bool IsCritical(ReadOnlySpan<byte> chunkType)
    {
        // The first chunk-type letter's case marks a critical chunk. Ancillary
        // chunks are safe to ignore only when they do not affect the pixels.
        return (chunkType[0] & 0x20) == 0;
    }

    private static string TypeName(ReadOnlySpan<byte> chunkType)
    {
        Span<char> characters = stackalloc char[4];

        for (int i = 0; i < 4; i++)
        {
            characters[i] = (char)chunkType[i];
        }

        return new string(characters);
    }

    /// <summary>
    /// The validated <c>tRNS</c> transparency samples for one image. Only the
    /// field that matches the image color type is populated.
    /// </summary>
    private readonly struct TransparencyInfo
    {
        public static TransparencyInfo None => default;

        public bool HasGray { get; init; }

        public byte Gray { get; init; }

        public bool HasRgb { get; init; }

        public byte Red { get; init; }

        public byte Green { get; init; }

        public byte Blue { get; init; }

        public byte[]? PaletteAlpha { get; init; }
    }

    private PngFormatException Error(string message, Exception? innerException = null)
    {
        string prefix = _sourceName is null
            ? "The PNG image is invalid or unsupported"
            : $"The PNG image '{_sourceName}' is invalid or unsupported";

        return innerException is null
            ? new PngFormatException($"{prefix}: {message}")
            : new PngFormatException($"{prefix}: {message}", innerException);
    }
}
