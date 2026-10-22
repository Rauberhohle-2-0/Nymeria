using Nymeria.Core.Audio;

namespace Nymeria.Core.Tests;

public sealed class PcmAudioDataTests
{
    [Fact]
    public void StereoData_ExposesFramesAndDuration()
    {
        var data = new PcmAudioData(
            [0.0f, 0.5f, 0.25f, -0.5f, 1.0f, -1.0f],
            sampleRate: 48000,
            channels: 2);

        Assert.Equal(48000, data.SampleRate);
        Assert.Equal(2, data.Channels);
        Assert.Equal(3, data.FrameCount);
        Assert.Equal(3.0 / 48000.0, data.DurationSeconds, 12);
        Assert.Equal(6, data.Samples.Length);
    }

    [Fact]
    public void MonoData_ExposesFrames()
    {
        var data = new PcmAudioData([0.0f, 0.5f, -0.5f], 8000, 1);

        Assert.Equal(3, data.FrameCount);
        Assert.Equal(1, data.Channels);
    }

    [Fact]
    public void Constructor_RejectsUnsupportedChannelCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PcmAudioData([0.0f], 48000, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PcmAudioData([0.0f, 0.0f, 0.0f], 48000, 3));
    }

    [Theory]
    [InlineData(7999)]
    [InlineData(192001)]
    public void Constructor_RejectsUnsupportedSampleRates(int sampleRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PcmAudioData([0.0f], sampleRate, 1));
    }

    [Fact]
    public void Constructor_RejectsEmptyAndIncompletePayloads()
    {
        Assert.Throws<ArgumentException>(() => new PcmAudioData([], 48000, 1));
        Assert.Throws<ArgumentException>(
            () => new PcmAudioData([0.0f, 0.0f, 0.0f], 48000, 2));
    }

    [Fact]
    public void Constructor_RejectsNonFiniteSamples()
    {
        Assert.Throws<ArgumentException>(
            () => new PcmAudioData([float.NaN], 48000, 1));
        Assert.Throws<ArgumentException>(
            () => new PcmAudioData([0.0f, float.PositiveInfinity], 48000, 2));
    }

    [Fact]
    public void Constructor_AllowsFiniteValuesOutsideUnitRange()
    {
        var data = new PcmAudioData([2.0f, -3.0f], 48000, 1);

        Assert.Equal(2.0f, data.Samples.Span[0]);
        Assert.Equal(-3.0f, data.Samples.Span[1]);
    }

    [Fact]
    public void Resample_MatchingRateReturnsSameInstance()
    {
        var data = new PcmAudioData([0.0f, 0.5f], 48000, 1);

        Assert.Same(data, data.Resample(48000));
    }

    [Fact]
    public void Resample_UpsamplesWithLinearInterpolation()
    {
        var data = new PcmAudioData([0.0f, 1.0f, 0.0f, -1.0f], 8000, 1);
        PcmAudioData resampled = data.Resample(16000);

        Assert.Equal(8, resampled.FrameCount);
        Assert.Equal(
            [0.0f, 0.5f, 1.0f, 0.5f, 0.0f, -0.5f, -1.0f, -1.0f],
            resampled.Samples.ToArray());
    }

    [Fact]
    public void Resample_PreservesStereoChannels()
    {
        var data = new PcmAudioData([0.0f, 0.0f, 0.5f, -0.5f], 8000, 2);
        PcmAudioData resampled = data.Resample(16000);

        Assert.Equal(2, resampled.Channels);
        Assert.Equal(4, resampled.FrameCount);
        Assert.Equal(
            [0.0f, 0.0f, 0.25f, -0.25f, 0.5f, -0.5f, 0.5f, -0.5f],
            resampled.Samples.ToArray());
    }

    [Fact]
    public void Resample_DownsamplePreservesDuration()
    {
        var samples = new float[48000];
        var data = new PcmAudioData(samples, 48000, 1);
        PcmAudioData resampled = data.Resample(8000);

        Assert.Equal(8000, resampled.FrameCount);
        Assert.Equal(1.0, resampled.DurationSeconds, 6);
    }

    [Fact]
    public void Resample_RejectsOutputBeyondTheSampleLimit()
    {
        // 8000 Hz to 192000 Hz multiplies the frame count by 24, so these
        // 400,000 stereo frames would need 19.2 million output samples.
        var samples = new float[400_000 * 2];
        var data = new PcmAudioData(samples, 8000, 2);

        Assert.Throws<InvalidOperationException>(() => data.Resample(192000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7999)]
    [InlineData(192001)]
    public void Resample_RejectsUnsupportedRates(int sampleRate)
    {
        var data = new PcmAudioData([0.0f], 48000, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => data.Resample(sampleRate));
    }
}
