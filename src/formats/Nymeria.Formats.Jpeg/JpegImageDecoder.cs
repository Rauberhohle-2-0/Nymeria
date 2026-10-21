using Nymeria.Core.Materials;
using StbImageSharp;

namespace Nymeria.Formats.Jpeg;

/// <summary>
/// Decodes JPEG images into validated, top-down RGBA texture data.
/// </summary>
/// <remarks>
/// <para>
/// Decoding uses the pure-managed <c>StbImageSharp</c> port of <c>stb_image</c>,
/// which reads baseline and progressive JPEG. The decoder still owns the safety
/// boundary: it bounds the encoded byte count, verifies the JPEG marker, reads
/// the dimensions before decoding, and rejects an image whose decoded RGBA
/// payload would exceed the documented limits. JPEG has no alpha channel, so
/// every decoded pixel is opaque.
/// </para>
/// <para>
/// The caller owns and disposes the stream; the decoder leaves it open. It has no
/// SDL3, GPU, window, or platform dependency. The stream is treated as
/// untrusted input.
/// </para>
/// <para>
/// EXIF orientation metadata is not applied. Decoded rows are top-down, which
/// matches the model texture-coordinate convention; a JPEG that relies on an EXIF
/// rotation appears in its stored orientation.
/// </para>
/// </remarks>
public sealed class JpegImageDecoder
{
    /// <summary>The largest accepted encoded JPEG, in bytes.</summary>
    public const int MaxInputBytes = 32 * 1024 * 1024;

    /// <summary>The largest accepted width or height, in pixels.</summary>
    public const int MaxDimension = Texture.MaxDimension;

    /// <summary>The largest accepted decoded RGBA payload, in bytes.</summary>
    public const int MaxPixelBytes = Texture.MaxPixelBytes;

    private static ReadOnlySpan<byte> Signature => [0xFF, 0xD8];

    private readonly string? _sourceName;
    private readonly JpegDecodeLimits _limits;

    /// <summary>
    /// Creates a JPEG decoder.
    /// </summary>
    /// <param name="sourceName">
    /// An optional file or source name included in diagnostics. It does not open
    /// any file or affect decoding.
    /// </param>
    public JpegImageDecoder(string? sourceName = null)
        : this(sourceName, JpegDecodeLimits.Default)
    {
    }

    internal JpegImageDecoder(string? sourceName, JpegDecodeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _sourceName = string.IsNullOrWhiteSpace(sourceName) ? null : sourceName;
        _limits = limits;
    }

    /// <summary>
    /// Decodes one JPEG image from the supplied stream.
    /// </summary>
    /// <param name="stream">
    /// A readable stream positioned at the start of the image. The caller owns
    /// and disposes it; the decoder leaves it open.
    /// </param>
    /// <returns>Validated, tightly packed, top-down RGBA pixels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">The stream is not readable.</exception>
    /// <exception cref="JpegFormatException">
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

        if (input.Length < Signature.Length ||
            !input.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            throw Error("the input does not start with the JPEG start-of-image marker.");
        }

        // stb_image is deliberately forgiving and can synthesize pixels for a
        // truncated file. Nymeria rejects truncation instead: walk the segment
        // chain so a missing end-of-image marker or a short segment is an error
        // before any decode work is done.
        ValidateStructure(input);

        ImageInfo? info;

        try
        {
            using var headerStream = new MemoryStream(input, writable: false);
            info = ImageInfo.FromStream(headerStream);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw Error($"the JPEG header could not be read: {exception.Message}", exception);
        }

        if (info is null)
        {
            throw Error("the input is not a supported JPEG image.");
        }

        int width = info.Value.Width;
        int height = info.Value.Height;

        if (width < 1 || height < 1)
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

        long decodedBytes;

        try
        {
            decodedBytes = checked((long)width * height * Texture.BytesPerPixel);
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

        ImageResult image;

        try
        {
            image = ImageResult.FromMemory(input, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw Error($"the JPEG data could not be decoded: {exception.Message}", exception);
        }

        if (image.Width != width || image.Height != height)
        {
            throw Error(
                $"the JPEG header declares {width} x {height}, but decoding produced " +
                $"{image.Width} x {image.Height}.");
        }

        byte[] pixels = image.Data;

        if (pixels.Length != decodedBytes)
        {
            throw Error(
                $"the decoded pixel payload is {pixels.Length} bytes, but " +
                $"{decodedBytes} bytes are required.");
        }

        // JPEG carries no alpha channel. Force opaque alpha even if a future
        // decoder wrapper leaves another value in the fourth component.
        for (int offset = 3; offset < pixels.Length; offset += Texture.BytesPerPixel)
        {
            pixels[offset] = 255;
        }

        return new TextureData(width, height, pixels, _sourceName);
    }

    private void ValidateStructure(ReadOnlySpan<byte> input)
    {
        int position = Signature.Length;
        bool hasEnd = false;

        while (position < input.Length)
        {
            if (input[position] != 0xFF)
            {
                throw Error(
                    $"the marker at byte {position} does not start with 0xFF.");
            }

            while (position < input.Length && input[position] == 0xFF)
            {
                position++;
            }

            if (position >= input.Length)
            {
                break;
            }

            byte marker = input[position++];

            if (marker == 0xD9)
            {
                hasEnd = true;
                break;
            }

            if (marker == 0xD8)
            {
                throw Error($"an unexpected start-of-image marker appears at byte {position - 2}.");
            }

            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                continue;
            }

            if (position + 2 > input.Length)
            {
                throw Error($"the segment at byte {position - 2} has no length.");
            }

            int segmentLength = (input[position] << 8) | input[position + 1];

            if (segmentLength < 2)
            {
                throw Error(
                    $"the segment at byte {position - 2} declares an invalid length " +
                    $"of {segmentLength}.");
            }

            if (segmentLength > input.Length - position)
            {
                throw Error(
                    $"the segment at byte {position - 2} declares {segmentLength} " +
                    "bytes, but the input is truncated.");
            }

            position += segmentLength;

            if (marker != 0xDA)
            {
                continue;
            }

            // Scan entropy-coded data until the next real marker. Stuffed zero
            // bytes and restart markers are part of the scan.
            while (position < input.Length)
            {
                if (input[position] != 0xFF)
                {
                    position++;
                    continue;
                }

                if (position + 1 >= input.Length)
                {
                    break;
                }

                byte following = input[position + 1];

                if (following == 0x00 || following is >= 0xD0 and <= 0xD7)
                {
                    position += 2;
                    continue;
                }

                break;
            }
        }

        if (!hasEnd)
        {
            throw Error(
                "the image has no end-of-image marker. The JPEG is truncated or " +
                "structurally incomplete.");
        }
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

    private JpegFormatException Error(string message, Exception? innerException = null)
    {
        string prefix = _sourceName is null
            ? "The JPEG image is invalid or unsupported"
            : $"The JPEG image '{_sourceName}' is invalid or unsupported";

        return innerException is null
            ? new JpegFormatException($"{prefix}: {message}")
            : new JpegFormatException($"{prefix}: {message}", innerException);
    }
}
