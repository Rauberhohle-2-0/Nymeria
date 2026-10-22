namespace Nymeria.Formats.Wav;

/// <summary>
/// Bounds applied while decoding one WAV clip, including while it is still
/// untrusted input.
/// </summary>
/// <remarks>
/// The defaults come from <see cref="WavAudioDecoder"/>. Tests use this type to
/// exercise limit checks without building maximum-size files.
/// </remarks>
internal sealed class WavDecodeLimits
{
    public static WavDecodeLimits Default { get; } = new(
        WavAudioDecoder.MaxInputBytes,
        WavAudioDecoder.MaxFrames,
        WavAudioDecoder.MaxDecodedBytes);

    public WavDecodeLimits(
        int maxInputBytes,
        int maxFrames,
        int maxDecodedBytes)
    {
        if (maxInputBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInputBytes));
        }

        if (maxFrames < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrames));
        }

        if (maxDecodedBytes < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDecodedBytes));
        }

        MaxInputBytes = maxInputBytes;
        MaxFrames = maxFrames;
        MaxDecodedBytes = maxDecodedBytes;
    }

    public int MaxInputBytes { get; }

    public int MaxFrames { get; }

    public int MaxDecodedBytes { get; }
}
