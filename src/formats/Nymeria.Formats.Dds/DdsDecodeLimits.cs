namespace Nymeria.Formats.Dds;

/// <summary>
/// Bounds applied while decoding one DDS texture, including while it is still
/// untrusted input.
/// </summary>
internal sealed class DdsDecodeLimits
{
    public static DdsDecodeLimits Default { get; } = new(
        DdsTextureDecoder.MaxInputBytes,
        DdsTextureDecoder.MaxDimension,
        DdsTextureDecoder.MaxPayloadBytes);

    public DdsDecodeLimits(
        int maxInputBytes,
        int maxDimension,
        int maxPayloadBytes)
    {
        if (maxInputBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInputBytes));
        }

        if (maxDimension < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDimension));
        }

        if (maxPayloadBytes < 8)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
        }

        MaxInputBytes = maxInputBytes;
        MaxDimension = maxDimension;
        MaxPayloadBytes = maxPayloadBytes;
    }

    public int MaxInputBytes { get; }

    public int MaxDimension { get; }

    public int MaxPayloadBytes { get; }
}
