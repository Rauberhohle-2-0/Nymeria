using System.Buffers.Binary;
using Nymeria.Core.Materials;

namespace Nymeria.Formats.Bmp;

/// <summary>
/// Decodes a bounded subset of Windows BMP/DIB images into validated, top-down
/// RGBA texture data.
/// </summary>
/// <remarks>
/// <para>
/// The supported subset is uncompressed (<c>BI_RGB</c>) 24-bit BGR and 32-bit
/// BGRA/BGRX images, 8-bit indexed images with a Windows palette, and 32-bit
/// <c>BI_BITFIELDS</c> images with the standard BGRA channel masks. Rows may be
/// stored bottom-up or top-down; the result is always top-down with the model
/// texture-coordinate convention. Alpha is preserved only when the header
/// declares a standard alpha mask; otherwise a 32-bit image's fourth byte is
/// treated as reserved and every pixel is opaque.
/// </para>
/// <para>
/// RLE, embedded JPEG/PNG, 1/4/16-bit pixels, OS/2 core headers, non-standard
/// channel masks, and other variants are rejected explicitly instead of being
/// approximated. The decoder treats the stream as untrusted input: it bounds the
/// encoded byte count, validates the file and DIB headers, dimensions, stride,
/// palette, masks, offsets, and exact payload length with checked arithmetic
/// before allocating or copying pixels. It never returns partial pixel data and
/// does not dispose or close the caller's stream. It has no SDL3, GPU, window,
/// or platform dependency.
/// </para>
/// </remarks>
public sealed class BmpImageDecoder
{
    /// <summary>The largest accepted encoded BMP, in bytes.</summary>
    public const int MaxInputBytes = 32 * 1024 * 1024;

    /// <summary>The largest accepted width or height, in pixels.</summary>
    public const int MaxDimension = Texture.MaxDimension;

    /// <summary>The largest accepted decoded RGBA payload, in bytes.</summary>
    public const int MaxPixelBytes = Texture.MaxPixelBytes;

    private const int FileHeaderLength = 14;
    private const int CoreHeaderLength = 12;
    private const int InfoHeaderLength = 40;
    private const int V4HeaderLength = 108;
    private const int V5HeaderLength = 124;

    private const uint CompressionRgb = 0;
    private const uint CompressionRle8 = 1;
    private const uint CompressionRle4 = 2;
    private const uint CompressionBitFields = 3;
    private const uint CompressionJpeg = 4;
    private const uint CompressionPng = 5;
    private const uint CompressionAlphaBitFields = 6;

    private const uint DefaultRedMask = 0x00FF0000;
    private const uint DefaultGreenMask = 0x0000FF00;
    private const uint DefaultBlueMask = 0x000000FF;
    private const uint AlphaMask = 0xFF000000;

    private readonly string? _sourceName;
    private readonly BmpDecodeLimits _limits;

    /// <summary>
    /// Creates a BMP decoder.
    /// </summary>
    /// <param name="sourceName">
    /// An optional file or source name included in diagnostics. It does not open
    /// any file or affect decoding.
    /// </param>
    public BmpImageDecoder(string? sourceName = null)
        : this(sourceName, BmpDecodeLimits.Default)
    {
    }

    internal BmpImageDecoder(string? sourceName, BmpDecodeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _sourceName = string.IsNullOrWhiteSpace(sourceName) ? null : sourceName;
        _limits = limits;
    }

    /// <summary>
    /// Decodes one BMP image from the supplied stream.
    /// </summary>
    /// <param name="stream">
    /// A readable stream positioned at the start of the image. The caller owns
    /// and disposes it; the decoder leaves it open.
    /// </param>
    /// <returns>Validated, tightly packed, top-down RGBA pixels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">The stream is not readable.</exception>
    /// <exception cref="BmpFormatException">
    /// The input is malformed, unsupported, or exceeds a documented limit.
    /// </exception>
    public TextureData Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
        {
            throw new ArgumentException("The stream must be readable.", nameof(stream));
        }

        byte[] input = ReadInput(stream);

        if (input.Length == 0)
        {
            throw Error("the input is empty.");
        }

        if (input.Length < FileHeaderLength + sizeof(uint))
        {
            throw Error(
                $"the file header is truncated at {input.Length} bytes.");
        }

        if (input[0] != (byte)'B' || input[1] != (byte)'M')
        {
            throw Error(
                $"the file signature is {input[0]:X2} {input[1]:X2}, not 'BM'.");
        }

        uint declaredFileSize = ReadU32(input, 2);

        if (declaredFileSize != 0 && declaredFileSize != (uint)input.Length)
        {
            throw Error(
                $"the file header declares {declaredFileSize} bytes, but the input " +
                $"is {input.Length} bytes.");
        }

        uint declaredPixelOffset = ReadU32(input, 10);
        uint headerSize = ReadU32(input, FileHeaderLength);

        ValidateHeaderSize(headerSize, input.Length);

        long headerEnd = FileHeaderLength + (long)headerSize;

        if (headerEnd > input.Length)
        {
            throw Error(
                $"the DIB header declares {headerSize} bytes, but only " +
                $"{input.Length - FileHeaderLength} bytes remain.");
        }

        int width = ReadI32(input, FileHeaderLength + 4);
        int height = ReadI32(input, FileHeaderLength + 8);
        int planeCount = ReadU16(input, FileHeaderLength + 12);
        int bitCount = ReadU16(input, FileHeaderLength + 14);
        uint compression = ReadU32(input, FileHeaderLength + 16);
        uint declaredImageSize = ReadU32(input, FileHeaderLength + 20);
        uint paletteSize = ReadU32(input, FileHeaderLength + 32);

        ValidateDimensions(width, height);

        if (planeCount != 1)
        {
            throw Error(
                $"the DIB header declares {planeCount} color planes. Only one plane " +
                "is supported.");
        }

        ValidatePixelFormat(bitCount, compression);

        if (declaredPixelOffset > int.MaxValue ||
            declaredPixelOffset > (uint)input.Length)
        {
            throw Error(
                $"the pixel array offset {declaredPixelOffset} is outside the " +
                $"{input.Length}-byte input.");
        }

        int pixelOffset = (int)declaredPixelOffset;
        int bytesPerPixel = bitCount / 8;
        int pixelHeight = Math.Abs(height);
        bool topDown = height < 0;
        bool useAlpha = false;
        int paletteOffset = 0;
        int paletteCount = 0;
        int extraMaskBytes = 0;

        if (bitCount == 32)
        {
            useAlpha = ReadChannelMasks(
                input,
                headerSize,
                compression,
                pixelOffset,
                out extraMaskBytes);
        }

        long minimumPixelOffset = headerEnd + extraMaskBytes;

        if (pixelOffset < minimumPixelOffset)
        {
            throw Error(
                $"the pixel array offset {pixelOffset} points inside the " +
                $"{headerSize}-byte DIB header or its channel masks. At least " +
                $"{minimumPixelOffset} bytes are required.");
        }

        if (bitCount == 8)
        {
            ValidatePalette(
                input,
                headerSize,
                paletteSize,
                pixelOffset,
                out paletteOffset,
                out paletteCount);
        }

        long stride = ComputeStride(width, bitCount);
        long payloadBytes = stride * pixelHeight;

        if (payloadBytes > _limits.MaxInputBytes)
        {
            throw Error(
                $"the pixel rows require {payloadBytes} bytes, which exceeds the " +
                $"supported input limit of {_limits.MaxInputBytes} bytes.");
        }

        if (declaredImageSize != 0 && declaredImageSize != payloadBytes)
        {
            throw Error(
                $"the DIB header declares a {declaredImageSize}-byte image, but " +
                $"the validated dimensions and stride require {payloadBytes} bytes.");
        }

        long decodedBytes;

        try
        {
            decodedBytes = checked((long)width * pixelHeight * Texture.BytesPerPixel);
        }
        catch (OverflowException)
        {
            throw Error("the image dimensions overflow the decoded pixel size.");
        }

        if (decodedBytes > _limits.MaxPixelBytes)
        {
            throw Error(
                $"the decoded image would require {decodedBytes} bytes, which " +
                $"exceeds the {_limits.MaxPixelBytes}-byte pixel limit.");
        }

        long payloadEnd = pixelOffset + payloadBytes;

        if (payloadEnd > input.Length)
        {
            throw Error(
                $"the pixel array needs {payloadBytes} bytes from offset " +
                $"{pixelOffset}, but the input ends after {input.Length} bytes.");
        }

        if (payloadEnd < input.Length)
        {
            throw Error(
                $"{input.Length - payloadEnd} unexpected bytes follow the pixel " +
                "array.");
        }

        return ConvertToRgba(
            input,
            width,
            pixelHeight,
            bytesPerPixel,
            useAlpha,
            topDown,
            pixelOffset,
            (int)stride,
            paletteOffset,
            paletteCount);
    }

    private void ValidateHeaderSize(uint headerSize, int inputLength)
    {
        if (headerSize == CoreHeaderLength)
        {
            throw Error(
                "the image uses a 12-byte OS/2 BITMAPCOREHEADER. OS/2 core " +
                "headers are not supported; save the image as a Windows BMP with " +
                "a 40-byte, 108-byte, or 124-byte header.");
        }

        if (headerSize is not (InfoHeaderLength or V4HeaderLength or V5HeaderLength))
        {
            throw Error(
                $"the DIB header size {headerSize} is not supported. Supported " +
                $"sizes are {InfoHeaderLength}, {V4HeaderLength}, and " +
                $"{V5HeaderLength} bytes.");
        }

        if (FileHeaderLength + (long)headerSize > inputLength)
        {
            throw Error(
                $"the DIB header declares {headerSize} bytes, but the input ends " +
                $"after {inputLength} bytes.");
        }
    }

    private void ValidateDimensions(int width, int height)
    {
        if (width <= 0)
        {
            throw Error(
                $"the image width is {width}. Width must be at least one pixel.");
        }

        if (height == 0)
        {
            throw Error("the image height is 0. Height must be at least one pixel.");
        }

        // Negating int.MinValue overflows. The dimension limit rejects it before
        // any arithmetic that depends on the absolute height.
        if (height == int.MinValue)
        {
            throw Error("the image height is -2147483648, which cannot be negated.");
        }

        int pixelHeight = Math.Abs(height);

        if (width > _limits.MaxDimension || pixelHeight > _limits.MaxDimension)
        {
            throw Error(
                $"the image dimensions are {width} x {pixelHeight}, which exceeds " +
                $"the {_limits.MaxDimension}-pixel dimension limit.");
        }
    }

    private void ValidatePixelFormat(int bitCount, uint compression)
    {
        if (bitCount is not (8 or 24 or 32))
        {
            throw Error(
                $"the image uses {bitCount}-bit pixels. Supported depths are 8 " +
                "(indexed), 24 (BGR), and 32 (BGRA/BGRX).");
        }

        switch (compression)
        {
            case CompressionRgb:
                return;
            case CompressionRle8:
            case CompressionRle4:
                throw Error(
                    $"the image uses {(compression == CompressionRle8 ? "BI_RLE8" : "BI_RLE4")} " +
                    "run-length compression, which is not supported.");
            case CompressionJpeg:
            case CompressionPng:
                throw Error(
                    $"the image uses {(compression == CompressionJpeg ? "BI_JPEG" : "BI_PNG")} " +
                    $"compression ({compression}), which embeds a " +
                    $"{(compression == CompressionJpeg ? "JPEG" : "PNG")} stream and is " +
                    "not supported.");
            case CompressionBitFields:
                if (bitCount != 32)
                {
                    throw Error(
                        $"the image uses BI_BITFIELDS with {bitCount}-bit pixels. " +
                        "Only 32-bit BI_BITFIELDS images are supported.");
                }

                return;
            case CompressionAlphaBitFields:
                throw Error(
                    "the image uses BI_ALPHABITFIELDS compression, which is not " +
                    "supported. Save it as 32-bit BI_BITFIELDS with an alpha mask " +
                    "or as BI_RGB.");
            default:
                throw Error(
                    $"the image uses compression type {compression}, which is not " +
                    "supported. Supported compression types are BI_RGB (0) and " +
                    "32-bit BI_BITFIELDS (3).");
        }
    }

    private bool ReadChannelMasks(
        byte[] input,
        uint headerSize,
        uint compression,
        int pixelOffset,
        out int extraMaskBytes)
    {
        extraMaskBytes = 0;
        uint redMask;
        uint greenMask;
        uint blueMask;
        uint alphaMask;

        if (headerSize >= V4HeaderLength)
        {
            redMask = ReadU32(input, FileHeaderLength + 40);
            greenMask = ReadU32(input, FileHeaderLength + 44);
            blueMask = ReadU32(input, FileHeaderLength + 48);
            alphaMask = ReadU32(input, FileHeaderLength + 52);

            if (compression == CompressionRgb && (redMask | greenMask | blueMask) == 0)
            {
                return alphaMask == AlphaMask;
            }
        }
        else
        {
            // A 40-byte header has no mask fields. A BI_RGB image is implicitly
            // BGRX, so its fourth byte is opaque. BI_BITFIELDS stores three or
            // four masks between the header and the pixel array.
            if (compression == CompressionRgb)
            {
                return false;
            }

            int maskOffset = FileHeaderLength + (int)headerSize;
            int available = pixelOffset - maskOffset;

            if (available < 12)
            {
                throw Error(
                    "the image uses BI_BITFIELDS, but the pixel array offset " +
                    $"leaves only {Math.Max(available, 0)} bytes for the channel " +
                    "masks. At least 12 bytes are required.");
            }

            redMask = ReadU32(input, maskOffset);
            greenMask = ReadU32(input, maskOffset + 4);
            blueMask = ReadU32(input, maskOffset + 8);
            alphaMask = available >= 16 ? ReadU32(input, maskOffset + 12) : 0;
            extraMaskBytes = available >= 16 ? 16 : 12;
        }

        if (redMask != DefaultRedMask ||
            greenMask != DefaultGreenMask ||
            blueMask != DefaultBlueMask)
        {
            throw Error(
                $"the channel masks R=0x{redMask:X8}, G=0x{greenMask:X8}, " +
                $"B=0x{blueMask:X8} are not supported. Supported 32-bit masks are " +
                $"R=0x{DefaultRedMask:X8}, G=0x{DefaultGreenMask:X8}, and " +
                $"B=0x{DefaultBlueMask:X8}, with A=0x00000000 or 0x{AlphaMask:X8}.");
        }

        if (alphaMask is not (0 or AlphaMask))
        {
            throw Error(
                $"the alpha mask 0x{alphaMask:X8} is not supported. Use no alpha " +
                $"mask or 0x{AlphaMask:X8}.");
        }

        return alphaMask == AlphaMask;
    }

    private void ValidatePalette(
        byte[] input,
        uint headerSize,
        uint declaredPaletteSize,
        int pixelOffset,
        out int paletteOffset,
        out int paletteCount)
    {
        if (declaredPaletteSize > 256)
        {
            throw Error(
                $"the DIB header declares {declaredPaletteSize} palette entries. " +
                "An 8-bit image can use at most 256.");
        }

        // For an 8-bit image a zero count means the full 256-entry palette.
        paletteCount = declaredPaletteSize == 0 ? 256 : (int)declaredPaletteSize;

        paletteOffset = FileHeaderLength + (int)headerSize;
        long paletteEnd = paletteOffset + (long)paletteCount * 4;

        if (paletteEnd > input.Length)
        {
            throw Error(
                $"the {paletteCount}-entry palette needs {paletteCount * 4} bytes " +
                $"from offset {paletteOffset}, but the input ends after " +
                $"{input.Length} bytes.");
        }

        if (paletteEnd > pixelOffset)
        {
            throw Error(
                $"the {paletteCount}-entry palette ends at offset {paletteEnd}, " +
                $"after the pixel array offset {pixelOffset}.");
        }
    }

    private static long ComputeStride(int width, int bitCount)
    {
        // Every BMP row is padded to a four-byte boundary. Dimensions are
        // bounded before this is called, so the product cannot overflow.
        return ((long)width * bitCount + 31) / 32 * 4;
    }

    private TextureData ConvertToRgba(
        byte[] input,
        int width,
        int height,
        int bytesPerPixel,
        bool useAlpha,
        bool topDown,
        int pixelOffset,
        int stride,
        int paletteOffset,
        int paletteCount)
    {
        var rgba = new byte[checked(width * height * Texture.BytesPerPixel)];

        for (int y = 0; y < height; y++)
        {
            int sourceRow = topDown ? y : height - 1 - y;
            int rowStart = pixelOffset + sourceRow * stride;

            if (bytesPerPixel == 1)
            {
                for (int x = 0; x < width; x++)
                {
                    int paletteIndex = input[rowStart + x];

                    if (paletteIndex >= paletteCount)
                    {
                        throw Error(
                            $"pixel ({x}, {y}) uses palette index {paletteIndex}, " +
                            $"but only {paletteCount} entries are defined.");
                    }

                    int source = paletteOffset + paletteIndex * 4;
                    int destination = (y * width + x) * Texture.BytesPerPixel;

                    // BMP palettes store blue, green, red, and a reserved byte.
                    rgba[destination] = input[source + 2];
                    rgba[destination + 1] = input[source + 1];
                    rgba[destination + 2] = input[source];
                    rgba[destination + 3] = 255;
                }

                continue;
            }

            for (int x = 0; x < width; x++)
            {
                int source = rowStart + x * bytesPerPixel;
                int destination = (y * width + x) * Texture.BytesPerPixel;

                // BMP stores blue, green, red, and optionally alpha in memory
                // order, independent of the row direction.
                rgba[destination] = input[source + 2];
                rgba[destination + 1] = input[source + 1];
                rgba[destination + 2] = input[source];
                rgba[destination + 3] = bytesPerPixel == 4 && useAlpha
                    ? input[source + 3]
                    : (byte)255;
            }
        }

        return new TextureData(width, height, rgba, _sourceName);
    }

    private byte[] ReadInput(Stream stream)
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

        using var output = new MemoryStream();
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

        return output.ToArray();
    }

    private static ushort ReadU16(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
    }

    private static uint ReadU32(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    }

    private static int ReadI32(byte[] data, int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
    }

    private BmpFormatException Error(string message, Exception? innerException = null)
    {
        string prefix = _sourceName is null
            ? "The BMP image is invalid or unsupported"
            : $"The BMP image '{_sourceName}' is invalid or unsupported";

        return innerException is null
            ? new BmpFormatException($"{prefix}: {message}")
            : new BmpFormatException($"{prefix}: {message}", innerException);
    }
}
