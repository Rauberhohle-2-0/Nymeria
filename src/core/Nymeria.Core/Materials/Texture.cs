namespace Nymeria.Core.Materials;

/// <summary>
/// A validated, format-independent 2D texture held in CPU memory.
/// </summary>
/// <remarks>
/// <para>
/// A texture is either decoded RGBA8 pixels (<see cref="TextureData"/>) or
/// compressed BC blocks (<see cref="CompressedTextureData"/>). Both are managed
/// CPU data with no GPU handle, SDL3, window, or file-format dependency. A
/// renderer decides when and how to upload a texture.
/// </para>
/// <para>
/// Every texture has one or more mip levels. Level 0 is the base image and each
/// following level halves each dimension, clamped to one pixel. Renderers must
/// upload only the levels that exist and must not sample from a level beyond
/// <see cref="MipLevelCount"/>.
/// </para>
/// </remarks>
public abstract class Texture
{
    /// <summary>
    /// The largest supported texture width or height, in pixels.
    /// </summary>
    /// <remarks>
    /// This is a practical CPU and GPU allocation limit, not a format or backend
    /// requirement. It is deliberately small enough to keep a decoded texture
    /// bounded while covering ordinary viewer content.
    /// </remarks>
    public const int MaxDimension = 4096;

    /// <summary>
    /// The largest supported decoded or compressed payload across all mip
    /// levels, in bytes.
    /// </summary>
    /// <remarks>
    /// A 4096 x 4096 RGBA level-0 image is exactly 64 MiB, so the byte limit and
    /// the dimension limit together bound a texture without relying on any single
    /// check.
    /// </remarks>
    public const int MaxPixelBytes = 64 * 1024 * 1024;

    /// <summary>The number of bytes per RGBA8 pixel.</summary>
    public const int BytesPerPixel = 4;

    /// <summary>
    /// Creates the shared validated state of a texture.
    /// </summary>
    /// <param name="width">The base-level width in pixels.</param>
    /// <param name="height">The base-level height in pixels.</param>
    /// <param name="mipLevelCount">The number of stored levels, at least one.</param>
    /// <param name="name">An optional source name used in diagnostics.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension or the mip count is outside the supported range.
    /// </exception>
    protected Texture(int width, int height, int mipLevelCount, string? name)
    {
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

        if (mipLevelCount < 1 || mipLevelCount > maximumMipLevels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mipLevelCount),
                mipLevelCount,
                $"A {width} x {height} texture supports between 1 and " +
                $"{maximumMipLevels} mip levels.");
        }

        Width = width;
        Height = height;
        MipLevelCount = mipLevelCount;
        Name = string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>Gets the base-level width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the base-level height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the number of stored mip levels, including the base level.</summary>
    public int MipLevelCount { get; }

    /// <summary>Gets the optional source name used in diagnostics.</summary>
    public string? Name { get; }

    /// <summary>
    /// Gets the largest valid mip count for a base-level size.
    /// </summary>
    public static int GetMaxMipLevelCount(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        int largest = Math.Max(width, height);
        int levels = 1;

        while (largest > 1)
        {
            largest >>= 1;
            levels++;
        }

        return levels;
    }

    /// <summary>Gets the width of one mip level in pixels.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The level does not exist.</exception>
    public int GetMipWidth(int level)
    {
        ValidateLevel(level);
        return Math.Max(1, Width >> level);
    }

    /// <summary>Gets the height of one mip level in pixels.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The level does not exist.</exception>
    public int GetMipHeight(int level)
    {
        ValidateLevel(level);
        return Math.Max(1, Height >> level);
    }

    /// <summary>
    /// Throws when a mip level is outside the stored range.
    /// </summary>
    protected void ValidateLevel(int level)
    {
        if (level < 0 || level >= MipLevelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                $"The texture has {MipLevelCount} mip level(s); level {level} does not exist.");
        }
    }
}
