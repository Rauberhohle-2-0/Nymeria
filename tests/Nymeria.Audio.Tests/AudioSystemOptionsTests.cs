using Nymeria.Audio;

namespace Nymeria.Audio.Tests;

public sealed class AudioSystemOptionsTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        var options = new AudioSystemOptions();

        Assert.Equal(48000, options.SampleRate);
        Assert.Equal(32, options.MaxVoices);
    }

    [Fact]
    public void CustomValues_ArePreserved()
    {
        var options = new AudioSystemOptions(sampleRate: 44100, maxVoices: 8);

        Assert.Equal(44100, options.SampleRate);
        Assert.Equal(8, options.MaxVoices);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7999)]
    [InlineData(192001)]
    public void InvalidSampleRates_AreRejected(int sampleRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AudioSystemOptions(sampleRate: sampleRate));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(257)]
    public void InvalidVoiceLimits_AreRejected(int maxVoices)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AudioSystemOptions(maxVoices: maxVoices));
    }
}
