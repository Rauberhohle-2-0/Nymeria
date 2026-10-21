using Nymeria.Core.Materials;
using Nymeria.Formats.Dds;
using Nymeria.Formats.Jpeg;
using Nymeria.Formats.Png;
using Nymeria.Formats.Tga;

namespace Nymeria.Formats.Textures;

/// <summary>
/// Loads a texture from a stream by identifying its format from its content.
/// </summary>
/// <remarks>
/// <para>
/// PNG, JPEG, and DDS are recognized by their signatures, so a misleading file
/// extension cannot select the wrong decoder. TGA has no signature; it is
/// recognized only when the supplied source name ends in <c>.tga</c> or
/// <c>.targa</c>, and the TGA header and pixel data are still fully validated.
/// </para>
/// <para>
/// The loader only identifies a format and delegates decoding. It performs no
/// GPU calls, keeps no cache, and never opens files or resolves paths. The caller
/// owns and disposes the stream. A malformed file of a recognized format fails
/// with that decoder's exception; unrecognized input fails with
/// <see cref="TextureFormatException"/>.
/// </para>
/// </remarks>
public sealed class TextureLoader
{
    /// <summary>The largest accepted encoded texture, in bytes.</summary>
    public const int MaxInputBytes = 32 * 1024 * 1024;

    private readonly int _maxInputBytes;

    /// <summary>Creates a loader with the documented default input limit.</summary>
    public TextureLoader()
        : this(MaxInputBytes)
    {
    }

    internal TextureLoader(int maxInputBytes)
    {
        if (maxInputBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInputBytes));
        }

        _maxInputBytes = maxInputBytes;
    }

    private static ReadOnlySpan<byte> PngSignature =>
        [137, 80, 78, 71, 13, 10, 26, 10];

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    private static ReadOnlySpan<byte> DdsSignature => "DDS "u8;

    /// <summary>
    /// Loads one texture from a stream.
    /// </summary>
    /// <param name="stream">
    /// A readable stream positioned at the start of the texture. The caller owns
    /// and disposes it; the loader leaves it open.
    /// </param>
    /// <param name="sourceName">
    /// An optional file or source name. It is used for diagnostics and to
    /// recognize TGA, which has no signature. It is never opened.
    /// </param>
    /// <returns>Decoded RGBA pixels or block-compressed texture data.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">The stream is not readable.</exception>
    /// <exception cref="TextureFormatException">
    /// The content matches no supported format.
    /// </exception>
    public Texture Load(Stream stream, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
        {
            throw new ArgumentException("The stream must be readable.", nameof(stream));
        }

        string? name = string.IsNullOrWhiteSpace(sourceName) ? null : sourceName;
        byte[] input = ReadInput(stream, name);
        ReadOnlySpan<byte> content = input;

        if (content.StartsWith(PngSignature))
        {
            using var memory = new MemoryStream(input, writable: false);
            return new PngImageDecoder(name).Decode(memory);
        }

        if (content.StartsWith(JpegSignature))
        {
            using var memory = new MemoryStream(input, writable: false);
            return new JpegImageDecoder(name).Decode(memory);
        }

        if (content.StartsWith(DdsSignature))
        {
            using var memory = new MemoryStream(input, writable: false);
            return new DdsTextureDecoder(name).Decode(memory);
        }

        if (HasTgaExtension(name))
        {
            using var memory = new MemoryStream(input, writable: false);
            return new TgaImageDecoder(name).Decode(memory);
        }

        throw new TextureFormatException(
            name is null
                ? "The texture format could not be identified. Supported formats are " +
                  "PNG, JPEG, and DDS by signature, and TGA by a .tga source name."
                : $"The texture '{name}' is not a recognized PNG, JPEG, DDS, or TGA " +
                  "image. PNG, JPEG, and DDS are identified by their signatures; TGA " +
                  "requires a .tga or .targa source name and a valid TGA header.");
    }

    private static bool HasTgaExtension(string? sourceName)
    {
        if (sourceName is null)
        {
            return false;
        }

        return sourceName.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ||
               sourceName.EndsWith(".targa", StringComparison.OrdinalIgnoreCase);
    }

    private byte[] ReadInput(Stream stream, string? sourceName)
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

            if (remaining > _maxInputBytes)
            {
                throw Error(
                    sourceName,
                    $"the input is {remaining} bytes, which exceeds the supported " +
                    $"limit of {_maxInputBytes} bytes.");
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

            if (read > _maxInputBytes - total)
            {
                throw Error(
                    sourceName,
                    $"the input is larger than the supported limit of " +
                    $"{_maxInputBytes} bytes.");
            }

            output.Write(buffer, 0, read);
            total += read;
        }

        if (total == 0)
        {
            throw Error(sourceName, "the input is empty.");
        }

        return output.ToArray();
    }

    private static TextureFormatException Error(string? sourceName, string message)
    {
        string prefix = sourceName is null
            ? "The texture is invalid or unsupported"
            : $"The texture '{sourceName}' is invalid or unsupported";

        return new TextureFormatException($"{prefix}: {message}");
    }
}
