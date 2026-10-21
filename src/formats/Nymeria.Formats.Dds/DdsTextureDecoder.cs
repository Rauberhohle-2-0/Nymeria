using System.Buffers.Binary;
using Nymeria.Core.Materials;

namespace Nymeria.Formats.Dds;

/// <summary>
/// Decodes a documented subset of DDS textures into validated CPU texture data.
/// </summary>
/// <remarks>
/// <para>
/// Supported layouts are 2D textures with one layer: BC1, BC2, BC3, BC4, BC5,
/// and BC7 block data (legacy DXT1/DXT3/DXT5/ATI2/BC4U/BC5U fourCCs and the
/// corresponding DX10 DXGI formats, including the sRGB variants where they
/// exist), plus 24-bit and 32-bit uncompressed RGB/RGBA with the common channel
/// masks. Cubemaps, volumes, texture arrays, 3D textures, BC6H, and other DXGI
/// formats are rejected with an explicit error.
/// </para>
/// <para>
/// Compressed payloads are returned as <see cref="CompressedTextureData"/> so a
/// renderer can upload the blocks directly. The decoder never decompresses
/// blocks. Uncompressed payloads are converted to top-down RGBA
/// <see cref="TextureData"/>.
/// </para>
/// <para>
/// The stream is untrusted input: the signature, header sizes, flags,
/// dimensions, mip count, pixel format, and exact payload length are validated
/// before the data is used. Mip levels are returned in file order, base level
/// first, and the file must contain exactly the declared chain with no trailing
/// bytes. The decoder has no SDL3, GPU, window, or platform dependency.
/// </para>
/// </remarks>
public sealed class DdsTextureDecoder
{
    /// <summary>The largest accepted encoded DDS, in bytes.</summary>
    public const int MaxInputBytes = 32 * 1024 * 1024;

    /// <summary>The largest accepted width or height, in pixels.</summary>
    public const int MaxDimension = Texture.MaxDimension;

    /// <summary>The largest accepted decoded or compressed payload, in bytes.</summary>
    public const int MaxPayloadBytes = Texture.MaxPixelBytes;

    private const int HeaderLength = 128;
    private const int PixelFormatOffset = 76;
    private const int Dx10HeaderLength = 20;

    private const uint Magic = 0x20534444; // "DDS "
    private const uint HeaderSize = 124;
    private const uint PixelFormatSize = 32;

    private const uint FlagCaps = 0x00000001;
    private const uint FlagHeight = 0x00000002;
    private const uint FlagWidth = 0x00000004;
    private const uint FlagPixelFormat = 0x00001000;
    private const uint FlagMipMapCount = 0x00020000;
    private const uint FlagPitch = 0x00000008;
    private const uint FlagLinearSize = 0x00080000;

    private const uint PixelFormatAlphaPixels = 0x00000001;
    private const uint PixelFormatFourCc = 0x00000004;
    private const uint PixelFormatRgb = 0x00000040;

    private const uint CapsTexture = 0x00001000;
    private const uint Caps2Cubemap = 0x00000200;
    private const uint Caps2Volume = 0x00200000;

    private const uint Dx10ResourceDimensionTexture2D = 3;
    private const uint Dx10MiscFlagTextureCube = 0x4;

    private readonly string? _sourceName;
    private readonly DdsDecodeLimits _limits;

    /// <summary>
    /// Creates a DDS decoder.
    /// </summary>
    /// <param name="sourceName">
    /// An optional file or source name included in diagnostics. It does not open
    /// any file or affect decoding.
    /// </param>
    public DdsTextureDecoder(string? sourceName = null)
        : this(sourceName, DdsDecodeLimits.Default)
    {
    }

    internal DdsTextureDecoder(string? sourceName, DdsDecodeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _sourceName = string.IsNullOrWhiteSpace(sourceName) ? null : sourceName;
        _limits = limits;
    }

    /// <summary>
    /// Decodes one DDS texture from the supplied stream.
    /// </summary>
    /// <param name="stream">
    /// A readable stream positioned at the DDS signature. The caller owns and
    /// disposes it; the decoder leaves it open.
    /// </param>
    /// <returns>
    /// A <see cref="CompressedTextureData"/> for block-compressed formats, or a
    /// <see cref="TextureData"/> for uncompressed formats.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">The stream is not readable.</exception>
    /// <exception cref="DdsFormatException">
    /// The input is malformed, unsupported, or exceeds a documented limit.
    /// </exception>
    public Texture Decode(Stream stream)
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

        ReadOnlySpan<byte> data = input;

        if (ReadUInt32(data, 0) != Magic)
        {
            throw Error("the input does not start with the DDS signature.");
        }

        if (ReadUInt32(data, 4) != HeaderSize)
        {
            throw Error(
                $"the header size is {ReadUInt32(data, 4)} instead of {HeaderSize}.");
        }

        uint flags = ReadUInt32(data, 8);
        uint height = ReadUInt32(data, 12);
        uint width = ReadUInt32(data, 16);
        uint pitchOrLinearSize = ReadUInt32(data, 20);
        uint depth = ReadUInt32(data, 24);
        uint declaredMipCount = ReadUInt32(data, 28);
        uint pixelFormatFlags = ReadUInt32(data, PixelFormatOffset + 4);
        uint fourCc = ReadUInt32(data, PixelFormatOffset + 8);
        uint rgbBitCount = ReadUInt32(data, PixelFormatOffset + 12);
        uint redMask = ReadUInt32(data, PixelFormatOffset + 16);
        uint greenMask = ReadUInt32(data, PixelFormatOffset + 20);
        uint blueMask = ReadUInt32(data, PixelFormatOffset + 24);
        uint alphaMask = ReadUInt32(data, PixelFormatOffset + 28);
        uint caps = ReadUInt32(data, 108);
        uint caps2 = ReadUInt32(data, 112);

        if (!HasFlag(flags, FlagCaps) || !HasFlag(flags, FlagWidth) ||
            !HasFlag(flags, FlagHeight) || !HasFlag(flags, FlagPixelFormat))
        {
            throw Error(
                "the header lacks the required CAPS, WIDTH, HEIGHT, or PIXELFORMAT flag.");
        }

        if (!HasFlag(caps, CapsTexture))
        {
            throw Error("the header does not declare a texture in its caps.");
        }

        if (HasFlag(caps2, Caps2Cubemap))
        {
            throw Error("cubemap DDS textures are not supported.");
        }

        if (HasFlag(caps2, Caps2Volume))
        {
            throw Error("volume DDS textures are not supported.");
        }

        if (depth > 1)
        {
            throw Error($"the texture declares depth {depth}. Only 2D textures are supported.");
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

        int maximumMips = Texture.GetMaxMipLevelCount((int)width, (int)height);

        // Compare before casting: a hostile header can declare more mip levels
        // than an int can represent.
        if (declaredMipCount > (uint)maximumMips)
        {
            throw Error(
                $"the header declares {declaredMipCount} mip levels, but a " +
                $"{width} x {height} texture supports between 1 and {maximumMips}.");
        }

        int mipCount = declaredMipCount <= 1 ? 1 : (int)declaredMipCount;

        // A count of zero or one both mean a single level, and writers commonly
        // omit the MIPMAPCOUNT flag in that case. Only a real chain must declare
        // the flag.
        if (declaredMipCount > 1 && !HasFlag(flags, FlagMipMapCount))
        {
            throw Error(
                $"the header declares {declaredMipCount} mip levels without the " +
                "MIPMAPCOUNT flag.");
        }

        int pixelFormatSize = (int)ReadUInt32(data, PixelFormatOffset);

        if (pixelFormatSize != PixelFormatSize)
        {
            throw Error(
                $"the pixel format size is {pixelFormatSize} instead of " +
                $"{PixelFormatSize}.");
        }

        int dx10Offset = HeaderLength;
        bool hasDx10Header = HasFlag(pixelFormatFlags, PixelFormatFourCc) &&
                             fourCc == FourCc("DX10");
        uint dxgiFormat = 0;

        if (hasDx10Header)
        {
            if (input.Length < dx10Offset + Dx10HeaderLength)
            {
                throw Error("the DX10 extension header is truncated.");
            }

            dxgiFormat = ReadUInt32(data, dx10Offset);
            uint resourceDimension = ReadUInt32(data, dx10Offset + 4);
            uint miscFlag = ReadUInt32(data, dx10Offset + 8);
            uint arraySize = ReadUInt32(data, dx10Offset + 12);

            if (resourceDimension != Dx10ResourceDimensionTexture2D)
            {
                throw Error(
                    $"the DX10 resource dimension is {resourceDimension}. Only 2D " +
                    "textures are supported.");
            }

            if (HasFlag(miscFlag, Dx10MiscFlagTextureCube))
            {
                throw Error("cubemap DDS textures are not supported.");
            }

            if (arraySize != 1)
            {
                throw Error(
                    $"the DX10 header declares an array size of {arraySize}. Only " +
                    "single-layer textures are supported.");
            }
        }

        int payloadOffset = hasDx10Header ? dx10Offset + Dx10HeaderLength : HeaderLength;
        CompressedTextureFormat compressedFormat = default;
        bool isSrgb = false;
        bool isUncompressed = false;
        bool hasAlpha = false;

        if (hasDx10Header &&
            TryResolveUncompressedDxgi(
                dxgiFormat,
                out rgbBitCount,
                out redMask,
                out greenMask,
                out blueMask,
                out alphaMask,
                out hasAlpha))
        {
            isUncompressed = true;
        }
        else if (hasDx10Header &&
                 TryResolveCompressedFormat(
                     dxgiFormat, isDx10: true, out compressedFormat, out isSrgb))
        {
            // The block layout is implied by the resolved format.
        }
        else if (hasDx10Header && dxgiFormat is 29 or 88)
        {
            throw Error(
                $"the DXGI format {dxgiFormat} is an sRGB uncompressed format, which " +
                "is not supported. Use a compressed sRGB format such as BC7, or " +
                "store the texture as non-sRGB RGBA8.");
        }
        else if (hasDx10Header && !HasFlag(pixelFormatFlags, PixelFormatFourCc))
        {
            throw Error(
                "the DX10 extension header is present without the FOURCC pixel " +
                "format flag.");
        }
        else if (hasDx10Header)
        {
            throw Error(
                $"the DXGI format {dxgiFormat} is not supported. Supported formats " +
                "are BC1, BC2, BC3, BC4, BC5, and BC7 (including sRGB where " +
                "available), plus 32-bit R8G8B8A8 and B8G8R8A8. Check that the " +
                "texture was exported as BC or RGBA8 rather than BC6H, BC7 SNORM, " +
                "or an array/cubemap.");
        }
        else if (HasFlag(pixelFormatFlags, PixelFormatFourCc))
        {
            if (!TryResolveCompressedFormat(fourCc, isDx10: false, out compressedFormat, out isSrgb))
            {
                throw Error(
                    $"the fourCC '{FourCcName(fourCc)}' is not supported. Supported " +
                    "fourCCs are DXT1, DXT3, DXT5, ATI2, BC4U, and BC5U.");
            }
        }
        else if (HasFlag(pixelFormatFlags, PixelFormatRgb))
        {
            if (rgbBitCount is not (24 or 32))
            {
                throw Error(
                    $"the uncompressed pixel format uses {rgbBitCount} bits per " +
                    "pixel. Only 24-bit and 32-bit RGB/RGBA are supported.");
            }

            if (!IsSupportedRgbMasks(
                    rgbBitCount,
                    redMask,
                    greenMask,
                    blueMask,
                    alphaMask,
                    HasFlag(pixelFormatFlags, PixelFormatAlphaPixels),
                    out hasAlpha))
            {
                throw Error(
                    $"the uncompressed channel masks (R 0x{redMask:X8}, " +
                    $"G 0x{greenMask:X8}, B 0x{blueMask:X8}, A 0x{alphaMask:X8}) are " +
                    "not a supported RGB or RGBA layout.");
            }

            isUncompressed = true;
        }
        else
        {
            throw Error(
                "the pixel format is neither a supported fourCC nor uncompressed RGB.");
        }

        bool isCompressed = !isUncompressed;
        int bytesPerPixel = isUncompressed ? (int)rgbBitCount / 8 : 0;
        long expectedPayload = 0;

        for (int level = 0; level < mipCount; level++)
        {
            int levelWidth = Math.Max(1, (int)width >> level);
            int levelHeight = Math.Max(1, (int)height >> level);

            try
            {
                expectedPayload += isCompressed
                    ? CompressedTextureData.GetLevelByteCount(
                        compressedFormat, levelWidth, levelHeight)
                    : checked((long)levelWidth * levelHeight * bytesPerPixel);
            }
            catch (OverflowException)
            {
                throw Error("the mip chain overflows the encoded payload size.");
            }

            if (expectedPayload > _limits.MaxPayloadBytes)
            {
                throw Error(
                    $"the texture payload would require {expectedPayload} bytes, " +
                    $"which exceeds the {_limits.MaxPayloadBytes}-byte limit.");
            }
        }

        long availablePayload = input.Length - payloadOffset;

        if (availablePayload != expectedPayload)
        {
            throw Error(
                $"the image data is {availablePayload} bytes, but the declared " +
                $"{mipCount}-level texture requires exactly {expectedPayload} bytes.");
        }

        ValidatePitchOrLinearSize(
            flags,
            pitchOrLinearSize,
            (int)width,
            (int)height,
            isCompressed,
            compressedFormat,
            bytesPerPixel);

        var payload = input.AsSpan(payloadOffset);

        if (isCompressed)
        {
            var levels = new ReadOnlyMemory<byte>[mipCount];
            int offset = 0;

            for (int level = 0; level < mipCount; level++)
            {
                int levelWidth = Math.Max(1, (int)width >> level);
                int levelHeight = Math.Max(1, (int)height >> level);
                int length = CompressedTextureData.GetLevelByteCount(
                    compressedFormat,
                    levelWidth,
                    levelHeight);
                levels[level] = payload.Slice(offset, length).ToArray();
                offset += length;
            }

            return new CompressedTextureData(
                (int)width,
                (int)height,
                compressedFormat,
                isSrgb,
                levels,
                _sourceName);
        }

        var rgbaLevels = new ReadOnlyMemory<byte>[mipCount];
        int sourceOffset = 0;

        for (int level = 0; level < mipCount; level++)
        {
            int levelWidth = Math.Max(1, (int)width >> level);
            int levelHeight = Math.Max(1, (int)height >> level);
            int levelBytes = checked(levelWidth * levelHeight * bytesPerPixel);
            rgbaLevels[level] = ConvertToRgba(
                payload.Slice(sourceOffset, levelBytes),
                levelWidth,
                levelHeight,
                bytesPerPixel,
                hasAlpha,
                redMask,
                greenMask,
                blueMask,
                alphaMask);
            sourceOffset += levelBytes;
        }

        return new TextureData((int)width, (int)height, rgbaLevels, _sourceName);
    }

    private void ValidatePitchOrLinearSize(
        uint flags,
        uint pitchOrLinearSize,
        int width,
        int height,
        bool isCompressed,
        CompressedTextureFormat compressedFormat,
        int bytesPerPixel)
    {
        if (isCompressed)
        {
            if (HasFlag(flags, FlagLinearSize) && pitchOrLinearSize != 0)
            {
                int expected = CompressedTextureData.GetLevelByteCount(
                    compressedFormat,
                    width,
                    height);

                if (pitchOrLinearSize != expected)
                {
                    throw Error(
                        $"the header declares a linear size of {pitchOrLinearSize} " +
                        $"bytes, but the base level requires {expected}.");
                }
            }

            return;
        }

        if (HasFlag(flags, FlagPitch) && pitchOrLinearSize != 0)
        {
            long expected = (long)width * bytesPerPixel;

            if (pitchOrLinearSize != expected)
            {
                throw Error(
                    $"the header declares a row pitch of {pitchOrLinearSize} bytes, " +
                    $"but {width} pixels at {bytesPerPixel} bytes each require " +
                    $"{expected}. Padded rows are not supported.");
            }
        }
    }

    private static bool TryResolveUncompressedDxgi(
        uint dxgiFormat,
        out uint bitCount,
        out uint redMask,
        out uint greenMask,
        out uint blueMask,
        out uint alphaMask,
        out bool hasAlpha)
    {
        bitCount = 32;
        hasAlpha = true;

        switch (dxgiFormat)
        {
            case 28: // DXGI_FORMAT_R8G8B8A8_UNORM
                redMask = 0x000000FF;
                greenMask = 0x0000FF00;
                blueMask = 0x00FF0000;
                alphaMask = 0xFF000000;
                return true;

            case 87: // DXGI_FORMAT_B8G8R8A8_UNORM
                redMask = 0x00FF0000;
                greenMask = 0x0000FF00;
                blueMask = 0x000000FF;
                alphaMask = 0xFF000000;
                return true;

            default:
                redMask = 0;
                greenMask = 0;
                blueMask = 0;
                alphaMask = 0;
                return false;
        }
    }

    private static bool TryResolveCompressedFormat(
        uint fourCc,
        bool isDx10,
        out CompressedTextureFormat format,
        out bool isSrgb)
    {
        format = default;
        isSrgb = false;

        if (isDx10)
        {
            switch (fourCc)
            {
                case 71: format = CompressedTextureFormat.Bc1; return true;
                case 72: format = CompressedTextureFormat.Bc1; isSrgb = true; return true;
                case 74: format = CompressedTextureFormat.Bc2; return true;
                case 75: format = CompressedTextureFormat.Bc2; isSrgb = true; return true;
                case 77: format = CompressedTextureFormat.Bc3; return true;
                case 78: format = CompressedTextureFormat.Bc3; isSrgb = true; return true;
                case 80: format = CompressedTextureFormat.Bc4; return true;
                case 83: format = CompressedTextureFormat.Bc5; return true;
                case 98: format = CompressedTextureFormat.Bc7; return true;
                case 99: format = CompressedTextureFormat.Bc7; isSrgb = true; return true;
                default: return false;
            }
        }

        if (fourCc == FourCc("DXT1"))
        {
            format = CompressedTextureFormat.Bc1;
            return true;
        }

        if (fourCc == FourCc("DXT3"))
        {
            format = CompressedTextureFormat.Bc2;
            return true;
        }

        if (fourCc == FourCc("DXT5"))
        {
            format = CompressedTextureFormat.Bc3;
            return true;
        }

        if (fourCc == FourCc("ATI2") || fourCc == FourCc("BC5U"))
        {
            format = CompressedTextureFormat.Bc5;
            return true;
        }

        if (fourCc == FourCc("BC4U"))
        {
            format = CompressedTextureFormat.Bc4;
            return true;
        }

        return false;
    }

    private static bool IsSupportedRgbMasks(
        uint bitCount,
        uint redMask,
        uint greenMask,
        uint blueMask,
        uint alphaMask,
        bool declaresAlpha,
        out bool hasAlpha)
    {
        hasAlpha = declaresAlpha;

        if (bitCount == 32)
        {
            bool bgra = redMask == 0x00FF0000 && greenMask == 0x0000FF00 &&
                        blueMask == 0x000000FF;
            bool rgba = redMask == 0x000000FF && greenMask == 0x0000FF00 &&
                        blueMask == 0x00FF0000;

            if (!bgra && !rgba)
            {
                return false;
            }

            if (declaresAlpha && alphaMask != 0xFF000000)
            {
                return false;
            }

            return true;
        }

        // 24-bit pixels cannot declare an alpha channel.
        if (declaresAlpha || alphaMask != 0)
        {
            hasAlpha = false;
            return false;
        }

        bool bgr = redMask == 0x00FF0000 && greenMask == 0x0000FF00 &&
                   blueMask == 0x000000FF;
        bool rgb = redMask == 0x000000FF && greenMask == 0x0000FF00 &&
                   blueMask == 0x00FF0000;
        hasAlpha = false;
        return bgr || rgb;
    }

    private static byte[] ConvertToRgba(
        ReadOnlySpan<byte> source,
        int width,
        int height,
        int bytesPerPixel,
        bool hasAlpha,
        uint redMask,
        uint greenMask,
        uint blueMask,
        uint alphaMask)
    {
        var rgba = new byte[checked(width * height * Texture.BytesPerPixel)];
        uint redShift = ShiftForMask(redMask);
        uint greenShift = ShiftForMask(greenMask);
        uint blueShift = ShiftForMask(blueMask);
        uint alphaShift = ShiftForMask(alphaMask);

        for (int pixel = 0; pixel < width * height; pixel++)
        {
            int sourceOffset = pixel * bytesPerPixel;
            uint value = bytesPerPixel == 4
                ? BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(sourceOffset, 4))
                : (uint)(source[sourceOffset] |
                         (source[sourceOffset + 1] << 8) |
                         (source[sourceOffset + 2] << 16));
            int destination = pixel * Texture.BytesPerPixel;

            rgba[destination] = ExtractChannel(value, redMask, redShift);
            rgba[destination + 1] = ExtractChannel(value, greenMask, greenShift);
            rgba[destination + 2] = ExtractChannel(value, blueMask, blueShift);
            rgba[destination + 3] = hasAlpha && alphaMask != 0
                ? ExtractChannel(value, alphaMask, alphaShift)
                : (byte)255;
        }

        return rgba;
    }

    private static byte ExtractChannel(uint value, uint mask, uint shift)
    {
        if (mask == 0)
        {
            return 255;
        }

        uint maximum = mask >> (int)shift;
        uint channel = (value & mask) >> (int)shift;
        return maximum == 0 ? (byte)0 : (byte)((channel * 255) / maximum);
    }

    private static uint ShiftForMask(uint mask)
    {
        uint shift = 0;

        while (shift < 32 && (mask & (1u << (int)shift)) == 0)
        {
            shift++;
        }

        return shift;
    }

    private static uint FourCc(string value)
    {
        return (uint)(value[0] | (value[1] << 8) | (value[2] << 16) | (value[3] << 24));
    }

    private static string FourCcName(uint value)
    {
        Span<char> characters = stackalloc char[4];

        for (int i = 0; i < 4; i++)
        {
            characters[i] = (char)((value >> (i * 8)) & 0xFF);
        }

        return new string(characters);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }

    private static bool HasFlag(uint value, uint flag)
    {
        return (value & flag) == flag;
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

    private DdsFormatException Error(string message, Exception? innerException = null)
    {
        string prefix = _sourceName is null
            ? "The DDS texture is invalid or unsupported"
            : $"The DDS texture '{_sourceName}' is invalid or unsupported";

        return innerException is null
            ? new DdsFormatException($"{prefix}: {message}")
            : new DdsFormatException($"{prefix}: {message}", innerException);
    }
}
