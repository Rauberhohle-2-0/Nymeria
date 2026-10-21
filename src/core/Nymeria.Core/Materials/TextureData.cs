namespace Nymeria.Core.Materials;

/// <summary>
/// A validated, format-independent 2D image held in CPU memory as tightly packed
/// 8-bit RGBA pixels.
/// </summary>
/// <remarks>
/// <para>
/// Pixels are stored row by row from the top row down, with four bytes
/// (red, green, blue, alpha) per pixel. <see cref="RowPitch"/> is the number of
/// bytes in one base-level row. This matches the texture-coordinate convention in
/// <c>docs/MODEL_DATA.md</c>: <c>(0, 0)</c> is the top-left texel and <c>+V</c>
/// points down.
/// </para>
/// <para>
/// The constructor takes ownership of the supplied memory instead of copying
/// it. Callers must not modify the arrays after constructing a texture.
/// </para>
/// <para>
/// This type contains no GPU handle, SDL3, window, or file-format dependency. A
/// renderer decides when and how to upload the pixels.
/// </para>
/// </remarks>
public sealed class TextureData : Texture
{
    private readonly ReadOnlyMemory<byte>[] _mipLevels;

    /// <summary>
    /// Creates a single-level texture from tightly packed RGBA pixels.
    /// </summary>
    /// <param name="width">The width in pixels, from 1 through <see cref="Texture.MaxDimension"/>.</param>
    /// <param name="height">The height in pixels, from 1 through <see cref="Texture.MaxDimension"/>.</param>
    /// <param name="rgbaPixels">
    /// Exactly <c>width * height * 4</c> bytes of row-major RGBA pixels. The
    /// constructor takes ownership of the memory instead of copying it.
    /// </param>
    /// <param name="name">An optional source name used in diagnostics.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is outside the supported range, or the pixel payload exceeds
    /// <see cref="Texture.MaxPixelBytes"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The pixel payload length does not match the dimensions.
    /// </exception>
    public TextureData(
        int width,
        int height,
        ReadOnlyMemory<byte> rgbaPixels,
        string? name = null)
        : this(width, height, new[] { rgbaPixels }, name)
    {
    }

    /// <summary>
    /// Creates a texture from a complete, top-down RGBA mip chain.
    /// </summary>
    /// <param name="width">The base-level width in pixels.</param>
    /// <param name="height">The base-level height in pixels.</param>
    /// <param name="mipLevels">
    /// One or more RGBA levels ordered from the base image down. Level <c>n</c>
    /// must contain exactly <c>max(1, width &gt;&gt; n) * max(1, height &gt;&gt; n) * 4</c>
    /// bytes. The constructor copies the list but takes ownership of the memory.
    /// </param>
    /// <param name="name">An optional source name used in diagnostics.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mipLevels"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The dimensions, mip count, or total payload are outside the supported
    /// range.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A mip level has the wrong payload length.
    /// </exception>
    public TextureData(
        int width,
        int height,
        IReadOnlyList<ReadOnlyMemory<byte>> mipLevels,
        string? name = null)
        : base(width, height, ValidateMipLevels(width, height, mipLevels), name)
    {
        ArgumentNullException.ThrowIfNull(mipLevels);

        _mipLevels = new ReadOnlyMemory<byte>[mipLevels.Count];

        for (int i = 0; i < mipLevels.Count; i++)
        {
            _mipLevels[i] = mipLevels[i];
        }
    }

    /// <summary>Gets the number of bytes in one tightly packed base-level pixel row.</summary>
    public int RowPitch => Width * BytesPerPixel;

    /// <summary>Gets the total pixel payload size across all stored mip levels.</summary>
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

    /// <summary>Gets the tightly packed top-down RGBA pixels of the base level.</summary>
    public ReadOnlyMemory<byte> Pixels => _mipLevels[0];

    /// <summary>Gets the RGBA payloads of every stored mip level, base level first.</summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> MipLevels => _mipLevels;

    /// <summary>Gets one mip level's tightly packed top-down RGBA pixels.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The level does not exist.</exception>
    public ReadOnlyMemory<byte> GetMipPixels(int level)
    {
        ValidateLevel(level);
        return _mipLevels[level];
    }

    private static int ValidateMipLevels(
        int width,
        int height,
        IReadOnlyList<ReadOnlyMemory<byte>>? mipLevels)
    {
        ArgumentNullException.ThrowIfNull(mipLevels);

        // This helper runs before the base constructor, so it must reject an
        // unsupported dimension itself. Otherwise an invalid dimension would be
        // reported as a payload-length mismatch.
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
                $"A {width} x {height} texture supports between 1 and " +
                $"{maximumMipLevels} mip levels.");
        }

        long totalBytes = 0;

        for (int level = 0; level < levelCount; level++)
        {
            int levelWidth = Math.Max(1, width >> level);
            int levelHeight = Math.Max(1, height >> level);
            long expectedBytes;

            try
            {
                expectedBytes = checked((long)levelWidth * levelHeight * BytesPerPixel);
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mipLevels),
                    level,
                    "The texture dimensions overflow the pixel byte count.");
            }

            ReadOnlyMemory<byte> pixels = mipLevels[level];

            if (pixels.Length != expectedBytes)
            {
                throw new ArgumentException(
                    $"Mip level {level} of a {width} x {height} RGBA texture requires " +
                    $"{expectedBytes} bytes, but the supplied payload has " +
                    $"{pixels.Length} bytes.",
                    nameof(mipLevels));
            }

            totalBytes += expectedBytes;

            if (totalBytes > MaxPixelBytes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mipLevels),
                    totalBytes,
                    $"The texture requires {totalBytes} bytes across its mip chain, " +
                    $"which exceeds the {MaxPixelBytes}-byte pixel limit.");
            }
        }

        return levelCount;
    }
}
