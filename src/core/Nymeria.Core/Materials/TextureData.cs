namespace Nymeria.Core.Materials;

/// <summary>
/// A validated, format-independent 2D image held in CPU memory as tightly packed
/// 8-bit RGBA pixels.
/// </summary>
/// <remarks>
/// <para>
/// Pixels are stored row by row from the top row down, with four bytes
/// (red, green, blue, alpha) per pixel. <see cref="RowPitch"/> is the number of
/// bytes in one row. This matches the texture-coordinate convention in
/// <c>docs/MODEL_DATA.md</c>: <c>(0, 0)</c> is the top-left texel and <c>+V</c>
/// points down.
/// </para>
/// <para>
/// The constructor takes ownership of the supplied memory instead of copying
/// it. Callers must not modify the array after constructing a texture.
/// </para>
/// <para>
/// This type contains no GPU handle, SDL3, window, or file-format dependency. A
/// renderer decides when and how to upload the pixels.
/// </para>
/// </remarks>
public sealed class TextureData
{
    /// <summary>
    /// The largest supported texture width or height, in pixels.
    /// </summary>
    /// <remarks>
    /// This is a practical CPU and GPU allocation limit, not a PNG or GPU
    /// requirement. It is deliberately small enough to keep a decoded texture
    /// bounded while covering ordinary viewer content.
    /// </remarks>
    public const int MaxDimension = 4096;

    /// <summary>
    /// The largest supported pixel payload, in bytes.
    /// </summary>
    /// <remarks>
    /// A 4096 x 4096 RGBA image is exactly 64 MiB, so the byte limit and the
    /// dimension limit together bound a decoded image without relying on any
    /// single check.
    /// </remarks>
    public const int MaxPixelBytes = 64 * 1024 * 1024;

    /// <summary>The number of bytes per RGBA pixel.</summary>
    public const int BytesPerPixel = 4;

    private readonly ReadOnlyMemory<byte> _pixels;

    /// <summary>
    /// Creates a texture from tightly packed RGBA pixels.
    /// </summary>
    /// <param name="width">The width in pixels, from 1 through <see cref="MaxDimension"/>.</param>
    /// <param name="height">The height in pixels, from 1 through <see cref="MaxDimension"/>.</param>
    /// <param name="rgbaPixels">
    /// Exactly <c>width * height * 4</c> bytes of row-major RGBA pixels. The
    /// constructor takes ownership of the memory instead of copying it.
    /// </param>
    /// <param name="name">An optional source name used in diagnostics.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is outside the supported range, or the pixel payload exceeds
    /// <see cref="MaxPixelBytes"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The pixel payload length does not match the dimensions.
    /// </exception>
    public TextureData(
        int width,
        int height,
        ReadOnlyMemory<byte> rgbaPixels,
        string? name = null)
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

        long expectedBytes;

        try
        {
            expectedBytes = checked((long)width * height * BytesPerPixel);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "The texture dimensions overflow the pixel byte count.");
        }

        if (expectedBytes > MaxPixelBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rgbaPixels),
                expectedBytes,
                $"The texture requires {expectedBytes} bytes, which exceeds the " +
                $"{MaxPixelBytes}-byte pixel limit.");
        }

        if (rgbaPixels.Length != expectedBytes)
        {
            throw new ArgumentException(
                $"A {width} x {height} RGBA texture requires {expectedBytes} bytes, " +
                $"but the supplied pixel payload has {rgbaPixels.Length} bytes.",
                nameof(rgbaPixels));
        }

        Width = width;
        Height = height;
        _pixels = rgbaPixels;
        Name = string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>Gets the texture width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the texture height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the number of bytes in one tightly packed pixel row.</summary>
    public int RowPitch => Width * BytesPerPixel;

    /// <summary>Gets the total pixel payload size in bytes.</summary>
    public int ByteCount => (int)((long)Width * Height * BytesPerPixel);

    /// <summary>Gets the tightly packed top-down RGBA pixels.</summary>
    public ReadOnlyMemory<byte> Pixels => _pixels;

    /// <summary>Gets the optional source name used in diagnostics.</summary>
    public string? Name { get; }
}
