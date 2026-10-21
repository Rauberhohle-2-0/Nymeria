namespace Nymeria.Formats.Png;

/// <summary>
/// Bounds applied while decoding one PNG image, including while it is still
/// untrusted input.
/// </summary>
internal sealed class PngDecodeLimits
{
    public static PngDecodeLimits Default { get; } = new(
        PngImageDecoder.MaxInputBytes,
        PngImageDecoder.MaxDimension,
        PngImageDecoder.MaxPixelBytes);

    public PngDecodeLimits(
        int maxInputBytes,
        int maxDimension,
        int maxPixelBytes)
    {
        if (maxInputBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInputBytes));
        }

        if (maxDimension < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDimension));
        }

        if (maxPixelBytes < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPixelBytes));
        }

        MaxInputBytes = maxInputBytes;
        MaxDimension = maxDimension;
        MaxPixelBytes = maxPixelBytes;
    }

    public int MaxInputBytes { get; }

    public int MaxDimension { get; }

    public int MaxPixelBytes { get; }
}
