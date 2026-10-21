namespace Nymeria.Core.Materials;

/// <summary>
/// The block-compressed formats Nymeria can describe on the CPU.
/// </summary>
/// <remarks>
/// These are the block-compressed families SDL3's GPU API names. Whether a
/// particular format can be uploaded is a separate, renderer-level capability
/// check; data may be constructed even when the active backend cannot sample it.
/// </remarks>
public enum CompressedTextureFormat
{
    /// <summary>BC1: RGB with optional one-bit alpha, 8 bytes per 4 x 4 block.</summary>
    Bc1,

    /// <summary>BC2: RGB with explicit four-bit alpha, 16 bytes per 4 x 4 block.</summary>
    Bc2,

    /// <summary>BC3: RGB with interpolated alpha, 16 bytes per 4 x 4 block.</summary>
    Bc3,

    /// <summary>BC4: one linear channel, 8 bytes per 4 x 4 block.</summary>
    Bc4,

    /// <summary>BC5: two linear channels, 16 bytes per 4 x 4 block.</summary>
    Bc5,

    /// <summary>BC7: high-quality RGBA, 16 bytes per 4 x 4 block.</summary>
    Bc7,
}

/// <summary>
/// A validated, format-independent block-compressed texture held in CPU memory.
/// </summary>
/// <remarks>
/// <para>
/// The payload is the encoded block data exactly as stored in the source file:
/// four-by-four blocks in row-major order, top row first, with no row padding.
/// The type does not decompress blocks, generate mipmaps, or upload anything.
/// </para>
/// <para>
/// <see cref="IsSrgb"/> records that the source declared an sRGB-encoded variant.
/// A renderer should upload the matching sRGB GPU format so the backend decodes
/// sampled values to linear space. It does not change the stored bytes.
/// </para>
/// </remarks>
public sealed class CompressedTextureData : Texture
{
    private readonly ReadOnlyMemory<byte>[] _mipLevels;

    /// <summary>
    /// Creates a compressed texture from a complete mip chain.
    /// </summary>
    /// <param name="width">The base-level width in pixels.</param>
    /// <param name="height">The base-level height in pixels.</param>
    /// <param name="format">The block-compressed format.</param>
    /// <param name="isSrgb">Whether the source declared an sRGB variant.</param>
    /// <param name="mipLevels">
    /// One or more encoded levels ordered from the base image down. Each level
    /// must contain exactly the block payload implied by its dimensions.
    /// </param>
    /// <param name="name">An optional source name used in diagnostics.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mipLevels"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The format, dimensions, mip count, or total payload are unsupported.
    /// </exception>
    /// <exception cref="ArgumentException">A mip level has the wrong payload length.</exception>
    public CompressedTextureData(
        int width,
        int height,
        CompressedTextureFormat format,
        bool isSrgb,
        IReadOnlyList<ReadOnlyMemory<byte>> mipLevels,
        string? name = null)
        : base(width, height, ValidateMipLevels(width, height, format, mipLevels), name)
    {
        ArgumentNullException.ThrowIfNull(mipLevels);

        _mipLevels = new ReadOnlyMemory<byte>[mipLevels.Count];

        for (int i = 0; i < mipLevels.Count; i++)
        {
            _mipLevels[i] = mipLevels[i];
        }

        Format = format;
        IsSrgb = isSrgb;
    }

    /// <summary>Gets the block-compressed format.</summary>
    public CompressedTextureFormat Format { get; }

    /// <summary>Gets whether the source declared an sRGB-encoded variant.</summary>
    public bool IsSrgb { get; }

    /// <summary>Gets the encoded payloads of every stored mip level, base level first.</summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> MipLevels => _mipLevels;

    /// <summary>Gets the total encoded payload size across all stored mip levels.</summary>
    public int ByteCount
    {
        get
        {
            long total = 0;

            foreach (ReadOnlyMemory<byte> level in _mipLevels)
            {
                total += level.Length;
            }

            return (int)total;
        }
    }

    /// <summary>Gets one mip level's encoded blocks.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The level does not exist.</exception>
    public ReadOnlyMemory<byte> GetMipBlocks(int level)
    {
        ValidateLevel(level);
        return _mipLevels[level];
    }

    /// <summary>Gets the encoded block size in bytes for one 4 x 4 block.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The format is not defined.</exception>
    public static int GetBlockByteCount(CompressedTextureFormat format)
    {
        return format switch
        {
            CompressedTextureFormat.Bc1 => 8,
            CompressedTextureFormat.Bc2 => 16,
            CompressedTextureFormat.Bc3 => 16,
            CompressedTextureFormat.Bc4 => 8,
            CompressedTextureFormat.Bc5 => 16,
            CompressedTextureFormat.Bc7 => 16,
            _ => throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "The compressed texture format is not defined."),
        };
    }

    /// <summary>
    /// Computes the exact encoded byte count of one mip level.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive or the size overflows.
    /// </exception>
    public static int GetLevelByteCount(
        CompressedTextureFormat format,
        int width,
        int height)
    {
        if (width < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width must be positive.");
        }

        if (height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The height must be positive.");
        }

        // Compute the rounded-up block counts without adding three, which could
        // overflow for an extreme caller-supplied dimension.
        int blocksX = (width - 1) / 4 + 1;
        int blocksY = (height - 1) / 4 + 1;
        long bytes;

        try
        {
            bytes = checked((long)blocksX * blocksY * GetBlockByteCount(format));
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "The compressed block count overflows the encoded byte count.");
        }

        if (bytes > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "The compressed block payload is too large.");
        }

        return (int)bytes;
    }

    /// <summary>Gets the short human-readable format name used in diagnostics.</summary>
    public static string GetFormatName(CompressedTextureFormat format)
    {
        return format switch
        {
            CompressedTextureFormat.Bc1 => "BC1",
            CompressedTextureFormat.Bc2 => "BC2",
            CompressedTextureFormat.Bc3 => "BC3",
            CompressedTextureFormat.Bc4 => "BC4",
            CompressedTextureFormat.Bc5 => "BC5",
            CompressedTextureFormat.Bc7 => "BC7",
            _ => format.ToString(),
        };
    }

    private static int ValidateMipLevels(
        int width,
        int height,
        CompressedTextureFormat format,
        IReadOnlyList<ReadOnlyMemory<byte>>? mipLevels)
    {
        ArgumentNullException.ThrowIfNull(mipLevels);

        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "The compressed texture format is not defined.");
        }

        if (width < 1 || width > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                $"The texture width must be between 1 and {MaxDimension} pixels.");
        }

        if (height < 1 || height > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                $"The texture height must be between 1 and {MaxDimension} pixels.");
        }

        int maximumMipLevels = GetMaxMipLevelCount(width, height);
        int levelCount = mipLevels.Count;

        if (levelCount < 1 || levelCount > maximumMipLevels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mipLevels),
                levelCount,
                $"A {width} x {height} compressed texture supports between 1 and " +
                $"{maximumMipLevels} mip levels.");
        }

        long totalBytes = 0;

        for (int level = 0; level < levelCount; level++)
        {
            int levelWidth = Math.Max(1, width >> level);
            int levelHeight = Math.Max(1, height >> level);
            int expectedBytes = GetLevelByteCount(format, levelWidth, levelHeight);
            ReadOnlyMemory<byte> blocks = mipLevels[level];

            if (blocks.Length != expectedBytes)
            {
                throw new ArgumentException(
                    $"Mip level {level} of a {width} x {height} " +
                    $"{GetFormatName(format)} texture requires {expectedBytes} bytes, " +
                    $"but the supplied payload has {blocks.Length} bytes.",
                    nameof(mipLevels));
            }

            totalBytes += expectedBytes;

            if (totalBytes > MaxPixelBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mipLevels),
                    totalBytes,
                    $"The texture requires {totalBytes} bytes across its mip chain, " +
                    $"which exceeds the {MaxPixelBytes}-byte payload limit.");
            }
        }

        return levelCount;
    }
}
