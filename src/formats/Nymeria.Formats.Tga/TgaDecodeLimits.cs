namespace Nymeria.Formats.Tga;

/// <summary>
/// Bounds applied while decoding one TGA image, including while it is still
/// untrusted input.
/// </summary>
internal sealed class TgaDecodeLimits
{
    public static TgaDecodeLimits Default { get; } = new(
        TgaImageDecoder.MaxInputBytes,
        TgaImageDecoder.MaxDimension,
        TgaImageDecoder.MaxPixelBytes);

    public TgaDecodeLimits(
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
