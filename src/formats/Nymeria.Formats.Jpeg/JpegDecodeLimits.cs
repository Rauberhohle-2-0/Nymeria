namespace Nymeria.Formats.Jpeg;

/// <summary>
/// Bounds applied while decoding one JPEG image, including while it is still
/// untrusted input.
/// </summary>
internal sealed class JpegDecodeLimits
{
    public static JpegDecodeLimits Default { get; } = new(
        JpegImageDecoder.MaxInputBytes,
        JpegImageDecoder.MaxDimension,
        JpegImageDecoder.MaxPixelBytes);

    public JpegDecodeLimits(
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
