namespace Nymeria.Formats.Bmp;

/// <summary>
/// Bounds applied while decoding one BMP image, including while it is still
/// untrusted input.
/// </summary>
internal sealed class BmpDecodeLimits
{
    public static BmpDecodeLimits Default { get; } = new(
        BmpImageDecoder.MaxInputBytes,
        BmpImageDecoder.MaxDimension,
        BmpImageDecoder.MaxPixelBytes);

    public BmpDecodeLimits(
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
