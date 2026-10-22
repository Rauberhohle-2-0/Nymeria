namespace Nymeria.Core.Audio;

/// <summary>
/// Immutable, validated PCM audio decoded into interleaved 32-bit floating-point
/// samples.
/// </summary>
/// <remarks>
/// <para>
/// Samples are normalized to approximately <c>[-1, 1]</c> and stored
/// interleaved by frame: a stereo frame occupies two consecutive values. The
/// data is format independent, contains no SDL3 or GPU state, and is shared by
/// reference between playback voices.
/// </para>
/// <para>
/// The constructor takes ownership of the supplied array without copying it, and
/// the caller must not mutate that array afterwards. Construction is the
/// validation boundary: the channel count, sample rate, payload length, and
/// finiteness of every sample are checked once. Non-finite samples are rejected
/// because they would poison the mix; finite values outside <c>[-1, 1]</c> are
/// preserved so the mixer can apply one documented final clip.
/// </para>
/// <para>
/// This type is safe to read from several threads. It is only immutable as long
/// as the caller honors the array-ownership rule above.
/// </para>
/// </remarks>
public sealed class PcmAudioData
{
    /// <summary>The smallest accepted sample rate, in hertz.</summary>
    public const int MinSampleRate = 8000;

    /// <summary>The largest accepted sample rate, in hertz.</summary>
    public const int MaxSampleRate = 192000;

    /// <summary>The largest accepted channel count. Nymeria mixes mono or stereo only.</summary>
    public const int MaxChannels = 2;

    /// <summary>
    /// The largest accepted interleaved sample count, in samples (32 MiB of
    /// float data). Applied to decoded data and to resampled output.
    /// </summary>
    public const int MaxSampleCount = 32 * 1024 * 1024 / sizeof(float);

    private readonly float[] _samples;

    /// <summary>
    /// Creates validated interleaved PCM data.
    /// </summary>
    /// <param name="samples">
    /// Interleaved samples. The array is referenced, not copied, and must not be
    /// mutated afterwards. Its length must be a nonzero multiple of
    /// <paramref name="channels"/>.
    /// </param>
    /// <param name="sampleRate">The sample rate in hertz, from 8000 through 192000.</param>
    /// <param name="channels">The channel count: 1 (mono) or 2 (stereo).</param>
    /// <exception cref="ArgumentNullException"><paramref name="samples"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The sample rate is outside the supported range.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The channel count is unsupported, the payload is empty or not a whole
    /// number of frames, or a sample is not finite.
    /// </exception>
    public PcmAudioData(float[] samples, int sampleRate, int channels)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (channels is < 1 or > MaxChannels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channels),
                channels,
                $"The channel count must be 1 or {MaxChannels}.");
        }

        if (sampleRate is < MinSampleRate or > MaxSampleRate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate),
                sampleRate,
                $"The sample rate must be between {MinSampleRate} and {MaxSampleRate} hertz.");
        }

        if (samples.Length == 0)
        {
            throw new ArgumentException("The audio payload must not be empty.", nameof(samples));
        }

        if (samples.Length % channels != 0)
        {
            throw new ArgumentException(
                "The audio payload must contain a whole number of frames.",
                nameof(samples));
        }

        if (samples.Length > MaxSampleCount)
        {
            throw new ArgumentException(
                $"The audio payload has {samples.Length} samples, which exceeds " +
                $"the supported limit of {MaxSampleCount}.",
                nameof(samples));
        }

        for (int index = 0; index < samples.Length; index++)
        {
            if (!float.IsFinite(samples[index]))
            {
                throw new ArgumentException(
                    $"The audio payload contains a non-finite sample at index {index}.",
                    nameof(samples));
            }
        }

        _samples = samples;
        SampleRate = sampleRate;
        Channels = channels;
    }

    /// <summary>Gets the sample rate in hertz.</summary>
    public int SampleRate { get; }

    /// <summary>Gets the channel count: 1 for mono or 2 for stereo.</summary>
    public int Channels { get; }

    /// <summary>Gets the number of sample frames.</summary>
    public int FrameCount => _samples.Length / Channels;

    /// <summary>Gets the clip duration in seconds.</summary>
    public double DurationSeconds => (double)FrameCount / SampleRate;

    /// <summary>
    /// Gets the interleaved samples. The memory must not be mutated.
    /// </summary>
    public ReadOnlyMemory<float> Samples => _samples;

    /// <summary>
    /// Returns data at the requested rate using linear interpolation between
    /// neighboring frames.
    /// </summary>
    /// <remarks>
    /// This is the only resampling Nymeria performs. It is deterministic and
    /// bounded, but it is not a band-limited resampler: it is intended for
    /// ordinary game sound effects, not for high-fidelity pitch shifting. A
    /// matching rate returns this same instance. Mono and stereo layouts are
    /// preserved; channel conversion is handled by the mixer.
    /// </remarks>
    /// <param name="sampleRate">The target sample rate in hertz.</param>
    /// <returns>PCM data at the target rate.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The target rate is outside the supported range.
    /// </exception>
    public PcmAudioData Resample(int sampleRate)
    {
        if (sampleRate is < MinSampleRate or > MaxSampleRate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate),
                sampleRate,
                $"The sample rate must be between {MinSampleRate} and {MaxSampleRate} hertz.");
        }

        if (sampleRate == SampleRate)
        {
            return this;
        }

        int sourceFrames = FrameCount;
        int targetFrames = (int)Math.Max(
            1.0,
            Math.Min(
                Math.Ceiling((double)sourceFrames * sampleRate / SampleRate),
                int.MaxValue / Channels));

        long targetSampleCount = (long)targetFrames * Channels;

        if (targetSampleCount > MaxSampleCount)
        {
            throw new InvalidOperationException(
                $"Resampling {sourceFrames} frames from {SampleRate} Hz to " +
                $"{sampleRate} Hz would produce {targetSampleCount} samples, which " +
                $"exceeds the supported limit of {MaxSampleCount}.");
        }

        var resampled = new float[targetFrames * Channels];
        double step = (double)SampleRate / sampleRate;

        for (int frame = 0; frame < targetFrames; frame++)
        {
            double sourcePosition = frame * step;
            int firstFrame = (int)sourcePosition;

            if (firstFrame >= sourceFrames)
            {
                firstFrame = sourceFrames - 1;
            }

            int secondFrame = Math.Min(firstFrame + 1, sourceFrames - 1);
            float fraction = (float)(sourcePosition - firstFrame);

            int firstIndex = firstFrame * Channels;
            int secondIndex = secondFrame * Channels;
            int targetIndex = frame * Channels;

            for (int channel = 0; channel < Channels; channel++)
            {
                float first = _samples[firstIndex + channel];
                float second = _samples[secondIndex + channel];
                resampled[targetIndex + channel] = first + ((second - first) * fraction);
            }
        }

        return new PcmAudioData(resampled, sampleRate, Channels);
    }
}
