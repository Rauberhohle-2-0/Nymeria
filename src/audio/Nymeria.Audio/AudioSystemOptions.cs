using Nymeria.Core.Audio;

namespace Nymeria.Audio;

/// <summary>
/// Configuration for one <see cref="AudioSystem"/>.
/// </summary>
/// <remarks>
/// Nymeria always mixes to two channels of 32-bit floating-point samples. The
/// sample rate and voice limit are validated at construction. The default voice
/// limit bounds both mixing cost and memory use per update; a caller that needs
/// more simultaneous sounds can raise it up to
/// <see cref="MaxVoiceLimit"/>.
/// </remarks>
public sealed class AudioSystemOptions
{
    /// <summary>The default mixing sample rate, in hertz.</summary>
    public const int DefaultSampleRate = 48000;

    /// <summary>The default maximum number of simultaneous voices.</summary>
    public const int DefaultMaxVoices = 32;

    /// <summary>The largest configurable number of simultaneous voices.</summary>
    public const int MaxVoiceLimit = 256;

    /// <summary>
    /// Creates validated audio system options.
    /// </summary>
    /// <param name="sampleRate">
    /// The mixing sample rate in hertz. WAV clips are resampled to this rate when
    /// they are loaded.
    /// </param>
    /// <param name="maxVoices">The maximum number of simultaneous voices.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The sample rate is outside the supported range or the voice limit is
    /// outside 1 through <see cref="MaxVoiceLimit"/>.
    /// </exception>
    public AudioSystemOptions(
        int sampleRate = DefaultSampleRate,
        int maxVoices = DefaultMaxVoices)
    {
        if (sampleRate is < PcmAudioData.MinSampleRate or > PcmAudioData.MaxSampleRate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate),
                sampleRate,
                $"The sample rate must be between {PcmAudioData.MinSampleRate} and " +
                $"{PcmAudioData.MaxSampleRate} hertz.");
        }

        if (maxVoices is < 1 or > MaxVoiceLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxVoices),
                maxVoices,
                $"The voice limit must be between 1 and {MaxVoiceLimit}.");
        }

        SampleRate = sampleRate;
        MaxVoices = maxVoices;
    }

    /// <summary>Gets the mixing sample rate in hertz.</summary>
    public int SampleRate { get; }

    /// <summary>Gets the maximum number of simultaneous voices.</summary>
    public int MaxVoices { get; }
}
