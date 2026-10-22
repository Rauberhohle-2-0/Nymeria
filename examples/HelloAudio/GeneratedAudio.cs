using System.Text;

namespace HelloAudio;

/// <summary>
/// Generates the example's original test audio as in-memory 16-bit PCM WAV
/// data. No game or OMSI asset is used, and no binary fixture is checked in.
/// </summary>
/// <remarks>
/// Every tone completes a whole number of cycles over its loop duration and the
/// envelopes return to their starting value, so the loops do not click.
/// </remarks>
internal static class GeneratedAudio
{
    public const int SampleRate = 48000;

    private const int AmbientSeconds = 4;
    private const int SpatialSeconds = 2;

    public static byte[] CreateAmbientWav()
    {
        int frameCount = SampleRate * AmbientSeconds;
        var samples = new short[frameCount * 2];

        for (int frame = 0; frame < frameCount; frame++)
        {
            double time = (double)frame / SampleRate;
            double envelope = 0.55 + (0.45 * Math.Sin(Math.Tau * 0.5 * time));

            double left =
                Math.Sin(Math.Tau * 110.0 * time) +
                (0.60 * Math.Sin(Math.Tau * 165.0 * time)) +
                (0.35 * Math.Sin(Math.Tau * 220.0 * time));
            double right =
                Math.Sin(Math.Tau * 110.0 * time + 0.6) +
                (0.60 * Math.Sin(Math.Tau * 165.0 * time + 1.1)) +
                (0.35 * Math.Sin(Math.Tau * 220.0 * time + 1.7));

            samples[frame * 2] = ToPcm(left * envelope * 0.12);
            samples[(frame * 2) + 1] = ToPcm(right * envelope * 0.12);
        }

        return WriteWav(samples, channels: 2);
    }

    public static byte[] CreateSpatialToneWav(double frequency)
    {
        int frameCount = SampleRate * SpatialSeconds;
        var samples = new short[frameCount];

        for (int frame = 0; frame < frameCount; frame++)
        {
            double time = (double)frame / SampleRate;

            // Three pulses over two seconds. The raised-cosine envelope starts
            // and ends at zero, which makes the loop seamless.
            double envelope = 0.5 - (0.5 * Math.Cos(Math.Tau * 1.5 * time));
            double carrier =
                Math.Sin(Math.Tau * frequency * time) +
                (0.25 * Math.Sin(Math.Tau * frequency * 2.0 * time));

            samples[frame] = ToPcm(carrier * envelope * 0.55);
        }

        return WriteWav(samples, channels: 1);
    }

    private static short ToPcm(double value) =>
        (short)Math.Clamp(
            (int)Math.Round(value * short.MaxValue),
            short.MinValue,
            short.MaxValue);

    private static byte[] WriteWav(short[] samples, int channels)
    {
        int bytesPerSample = sizeof(short);
        int blockAlign = channels * bytesPerSample;
        int dataBytes = samples.Length * bytesPerSample;
        int byteRate = SampleRate * blockAlign;

        using var output = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)channels);
        writer.Write(SampleRate);
        writer.Write(byteRate);
        writer.Write((ushort)blockAlign);
        writer.Write((ushort)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);

        foreach (short sample in samples)
        {
            writer.Write(sample);
        }

        writer.Flush();
        return output.ToArray();
    }
}
