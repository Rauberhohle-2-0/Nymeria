using Nymeria.Core.Materials;

namespace Nymeria.Formats.Tga;

/// <summary>
/// Decodes common true-color TGA images into validated, top-down RGBA texture
/// data.
/// </summary>
/// <remarks>
/// <para>
/// The supported subset is uncompressed and RLE-compressed true-color images with
/// 24-bit BGR or 32-bit BGRA pixels, no color map, and no interleaving. Both
/// image-origin flags are honored, so the result is always top-down with the
/// model texture-coordinate convention. Alpha is preserved for 32-bit images and
/// is opaque for 24-bit images.
/// </para>
/// <para>
/// Color-mapped, grayscale, 15/16-bit, indexed, and interleaved variants are
/// rejected explicitly instead of being approximated. RLE packets are validated
/// before their pixels are written: a packet may not exceed the pixel count, and
/// truncated raw or run packets fail the decode.
/// </para>
/// <para>
/// The decoder treats the stream as untrusted input. It bounds the encoded byte
/// count, uses checked arithmetic for dimensions and the decoded payload, and
/// does not dispose or close the caller's stream. It has no SDL3, GPU, window, or
/// platform dependency.
/// </para>
/// </remarks>
public sealed class TgaImageDecoder
{
    /// <summary>The largest accepted encoded TGA, in bytes.</summary>
    public const int MaxInputBytes = 32 * 1024 * 1024;

    /// <summary>The largest accepted width or height, in pixels.</summary>
    public const int MaxDimension = Texture.MaxDimension;

    /// <summary>The largest accepted decoded RGBA payload, in bytes.</summary>
    public const int MaxPixelBytes = Texture.MaxPixelBytes;

    private const int HeaderLength = 18;

    /// <summary>The optional TGA 2.0 footer length, including its signature.</summary>
    private const int FooterLength = 26;

    private static ReadOnlySpan<byte> FooterSignature => "TRUEVISION-XFILE.\0"u8;

    private readonly string? _sourceName;
    private readonly TgaDecodeLimits _limits;

    /// <summary>
    /// Creates a TGA decoder.
    /// </summary>
    /// <param name="sourceName">
    /// An optional file or source name included in diagnostics. It does not open
    /// any file or affect decoding.
    /// </param>
    public TgaImageDecoder(string? sourceName = null)
        : this(sourceName, TgaDecodeLimits.Default)
    {
    }

    internal TgaImageDecoder(string? sourceName, TgaDecodeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _sourceName = string.IsNullOrWhiteSpace(sourceName) ? null : sourceName;
        _limits = limits;
    }

    /// <summary>
    /// Decodes one TGA image from the supplied stream.
    /// </summary>
    /// <param name="stream">
    /// A readable stream positioned at the start of the image. The caller owns
    /// and disposes it; the decoder leaves it open.
    /// </param>
    /// <returns>Validated, tightly packed, top-down RGBA pixels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">The stream is not readable.</exception>
    /// <exception cref="TgaFormatException">
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

        if (input.Length < HeaderLength)
        {
            throw Error($"the header is truncated at {input.Length} bytes.");
        }

        int idLength = input[0];
        int colorMapType = input[1];
        int imageType = input[2];
        int colorMapLength = input[5] | (input[6] << 8);
        int colorMapEntryBits = input[7];
        int width = input[12] | (input[13] << 8);
        int height = input[14] | (input[15] << 8);
        int pixelDepth = input[16];
        int descriptor = input[17];

        if (colorMapType != 0)
        {
            throw Error(
                $"the image uses color map type {colorMapType}. Color-mapped TGA " +
                "images are not supported.");
        }

        if (colorMapLength != 0 || colorMapEntryBits != 0)
        {
            throw Error(
                "the header declares a color-map specification but no color map. " +
                "Color-mapped TGA images are not supported.");
        }

        bool isRle = imageType switch
        {
            2 => false,
            10 => true,
            _ => throw Error(
                $"the image uses type {imageType}. Supported types are 2 " +
                "(uncompressed true-color) and 10 (RLE true-color)."),
        };

        if (pixelDepth is not (24 or 32))
        {
            throw Error(
                $"the image uses {pixelDepth}-bit pixels. Supported true-color " +
                "depths are 24 (BGR) and 32 (BGRA).");
        }

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

        int bytesPerPixel = pixelDepth / 8;
        int alphaBits = descriptor & 0x0F;
        int origin = (descriptor >> 4) & 0x03;
        int interleave = (descriptor >> 6) & 0x03;

        if (interleave != 0)
        {
            throw Error(
                $"the image uses interleave mode {interleave}. Interleaved TGA " +
                "images are not supported.");
        }

        if (pixelDepth == 32 && alphaBits is not (0 or 8))
        {
            throw Error(
                $"the image declares {alphaBits} alpha bits for 32-bit pixels. " +
                "Only 0 or 8 alpha bits are supported.");
        }

        if (pixelDepth == 24 && alphaBits != 0)
        {
            throw Error(
                $"the image declares {alphaBits} alpha bits for 24-bit pixels, " +
                "which cannot carry an alpha channel.");
        }

        long pixelCount;

        try
        {
            pixelCount = checked((long)width * height);
        }
        catch (OverflowException)
        {
            throw Error("the image dimensions overflow the pixel count.");
        }

        long decodedBytes;

        try
        {
            decodedBytes = checked(pixelCount * Texture.BytesPerPixel);
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

        int pixelDataStart = HeaderLength + idLength;

        if (pixelDataStart > input.Length)
        {
            throw Error(
                $"the header declares a {idLength}-byte image ID, but the input ends " +
                "before it.");
        }

        int pixelDataLength;

        try
        {
            pixelDataLength = checked((int)(pixelCount * bytesPerPixel));
        }
        catch (OverflowException)
        {
            throw Error("the image dimensions overflow the encoded pixel size.");
        }

        ReadOnlySpan<byte> pixelData = input.AsSpan(pixelDataStart);
        byte[] bgra;

        if (isRle)
        {
            bgra = DecodeRle(pixelData, (int)pixelCount, bytesPerPixel);
        }
        else
        {
            if (pixelData.Length < pixelDataLength)
            {
                throw Error(
                    $"the image data is {pixelData.Length} bytes, but {pixelDataLength} " +
                    "bytes are required for the declared dimensions.");
            }

            ValidateTrailingBytes(pixelData[pixelDataLength..]);
            bgra = pixelData[..pixelDataLength].ToArray();
        }

        return ConvertToRgba(bgra, width, height, bytesPerPixel, alphaBits, origin);
    }

    private byte[] DecodeRle(ReadOnlySpan<byte> input, int pixelCount, int bytesPerPixel)
    {
        byte[] output = new byte[checked(pixelCount * bytesPerPixel)];
        int source = 0;
        int written = 0;

        while (written < pixelCount)
        {
            if (source >= input.Length)
            {
                throw Error(
                    $"the RLE data ended after {written} of {pixelCount} pixels.");
            }

            int header = input[source++];
            int runLength = (header & 0x7F) + 1;

            if (runLength > pixelCount - written)
            {
                throw Error(
                    $"the RLE packet at byte {source - 1} declares {runLength} pixels, " +
                    $"but only {pixelCount - written} remain.");
            }

            // The TGA packet header uses bit 7 to distinguish a run-length
            // packet (one pixel repeated) from a raw packet (runLength pixels).
            if ((header & 0x80) != 0)
            {
                if (bytesPerPixel > input.Length - source)
                {
                    throw Error(
                        $"the run RLE packet at byte {source - 1} is truncated.");
                }

                ReadOnlySpan<byte> pixel = input.Slice(source, bytesPerPixel);
                source += bytesPerPixel;

                for (int i = 0; i < runLength; i++)
                {
                    pixel.CopyTo(output.AsSpan(written + i * bytesPerPixel, bytesPerPixel));
                }
            }
            else
            {
                int packetBytes = runLength * bytesPerPixel;

                if (packetBytes > input.Length - source)
                {
                    throw Error(
                        $"the raw RLE packet at byte {source - 1} is truncated.");
                }

                input.Slice(source, packetBytes).CopyTo(
                    output.AsSpan(written * bytesPerPixel, packetBytes));
                source += packetBytes;
            }

            written += runLength;
        }

        if (source < input.Length)
        {
            ValidateTrailingBytes(input[source..]);
        }

        return output;
    }

    private void ValidateTrailingBytes(ReadOnlySpan<byte> trailing)
    {
        if (trailing.IsEmpty)
        {
            return;
        }

        // A TGA 2.0 footer is the only accepted trailing data. It carries no
        // pixels and no paths that the decoder would follow.
        if (trailing.Length == FooterLength &&
            trailing[8..].SequenceEqual(FooterSignature))
        {
            return;
        }

        throw Error(
            $"{trailing.Length} unexpected bytes follow the image data.");
    }

    private TextureData ConvertToRgba(
        byte[] bgra,
        int width,
        int height,
        int bytesPerPixel,
        int alphaBits,
        int origin)
    {
        var rgba = new byte[checked(width * height * Texture.BytesPerPixel)];
        bool topOrigin = (origin & 0x02) != 0;
        bool rightOrigin = (origin & 0x01) != 0;

        for (int y = 0; y < height; y++)
        {
            int sourceRow = topOrigin ? y : height - 1 - y;

            for (int x = 0; x < width; x++)
            {
                int sourceColumn = rightOrigin ? width - 1 - x : x;
                int source = (sourceRow * width + sourceColumn) * bytesPerPixel;
                int destination = (y * width + x) * Texture.BytesPerPixel;

                // TGA stores blue, green, red (and alpha) in memory order.
                rgba[destination] = bgra[source + 2];
                rgba[destination + 1] = bgra[source + 1];
                rgba[destination + 2] = bgra[source];
                rgba[destination + 3] = bytesPerPixel == 4 && alphaBits == 8
                    ? bgra[source + 3]
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

    private TgaFormatException Error(string message, Exception? innerException = null)
    {
        string prefix = _sourceName is null
            ? "The TGA image is invalid or unsupported"
            : $"The TGA image '{_sourceName}' is invalid or unsupported";

        return innerException is null
            ? new TgaFormatException($"{prefix}: {message}")
            : new TgaFormatException($"{prefix}: {message}", innerException);
    }
}
