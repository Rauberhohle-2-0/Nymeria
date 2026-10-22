using System.Numerics;
using Nymeria.Core.Audio;

namespace Nymeria.Audio.Tests;

public sealed class AudioMixerTests
{
    private const int Rate = 48000;
    private const float Tolerance = 1e-5f;

    [Fact]
    public void Mix_WithoutVoicesIsSilent()
    {
        var mixer = new AudioMixer(8, Rate);

        float[] output = Mix(mixer, 4);

        Assert.All(output, value => Assert.Equal(0.0f, value));
    }

    [Fact]
    public void Play_MixesMonoToBothChannels()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.5f, 0.5f, 0.5f);

        using AudioVoice voice = mixer.Play(clip);
        float[] output = Mix(mixer, 2);

        Assert.Equal(0.5f, output[0], 6);
        Assert.Equal(0.5f, output[1], 6);
        Assert.Equal(0.5f, output[2], 6);
        Assert.Equal(0.5f, output[3], 6);
    }

    [Fact]
    public void Play_PreservesStereoChannels()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = mixer.CreateClip(
            new PcmAudioData([0.25f, -0.25f, 0.5f, -0.5f], Rate, 2));

        using AudioVoice voice = mixer.Play(clip);
        float[] output = Mix(mixer, 2);

        Assert.Equal(0.25f, output[0], 6);
        Assert.Equal(-0.25f, output[1], 6);
        Assert.Equal(0.5f, output[2], 6);
        Assert.Equal(-0.5f, output[3], 6);
    }

    [Fact]
    public void Play_AppliesVoiceAndMasterGain()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.5f);
        mixer.MasterGain = 0.5f;

        using AudioVoice voice = mixer.Play(clip);
        voice.Gain = 0.5f;

        float[] output = Mix(mixer, 1);

        Assert.Equal(0.125f, output[0], 6);
        Assert.Equal(0.125f, output[1], 6);
    }

    [Fact]
    public void Play_OverlappingVoicesSum()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.25f);

        using AudioVoice first = mixer.Play(clip);
        using AudioVoice second = mixer.Play(clip);

        float[] output = Mix(mixer, 1);

        Assert.Equal(0.5f, output[0], 6);
        Assert.Equal(0.5f, output[1], 6);
    }

    [Fact]
    public void Play_ClampsTheFinalMix()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 2.0f);

        using AudioVoice first = mixer.Play(clip);
        using AudioVoice second = mixer.Play(clip);

        float[] output = Mix(mixer, 1);

        Assert.Equal(1.0f, output[0]);
        Assert.Equal(1.0f, output[1]);
    }

    [Fact]
    public void Play_LoopsAtTheEndOfTheClip()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.1f, 0.2f);

        using AudioVoice voice = mixer.Play(clip, loop: true);
        float[] output = Mix(mixer, 4);

        Assert.Equal(0.1f, output[0], 6);
        Assert.Equal(0.2f, output[2], 6);
        Assert.Equal(0.1f, output[4], 6);
        Assert.Equal(0.2f, output[6], 6);
    }

    [Fact]
    public void NonLoopingVoice_EndsAndReleasesTheClip()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.1f, 0.2f);

        AudioVoice voice = mixer.Play(clip);
        float[] output = Mix(mixer, 4);

        Assert.Equal(0.1f, output[0], 6);
        Assert.Equal(0.2f, output[2], 6);
        Assert.Equal(0.0f, output[4]);
        Assert.Equal(0.0f, output[6]);
        Assert.Equal(AudioVoiceState.Ended, voice.State);
        Assert.Equal(1, mixer.ActiveVoiceCount);
        Assert.Equal(1, clip.ActiveVoiceCount);

        mixer.ReapFinished();

        Assert.Equal(AudioVoiceState.Ended, voice.State);
        Assert.Equal(0, mixer.ActiveVoiceCount);
        Assert.Equal(0, clip.ActiveVoiceCount);

        // Stop must not revive a terminal voice or its ended state.
        voice.Stop();
        Assert.Equal(AudioVoiceState.Ended, voice.State);

        clip.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = clip.SampleRate);
        voice.Dispose();
    }

    [Fact]
    public void Play_ReapsFinishedVoicesBeforeCheckingCapacity()
    {
        var mixer = new AudioMixer(1, Rate);
        AudioClip clip = MonoClip(mixer, 0.1f);

        AudioVoice first = mixer.Play(clip);
        Mix(mixer, 2);
        Assert.Equal(AudioVoiceState.Ended, first.State);

        using AudioVoice second = mixer.Play(clip);

        Assert.Equal(1, mixer.ActiveVoiceCount);
        Assert.Equal(AudioVoiceState.Playing, second.State);
        first.Dispose();
    }

    [Fact]
    public void Pause_IsSilentAndHoldsTheFramePosition()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.1f, 0.2f, 0.3f);

        using AudioVoice voice = mixer.Play(clip);
        Mix(mixer, 1);

        voice.Pause();
        float[] paused = Mix(mixer, 1);

        Assert.Equal(0.0f, paused[0]);
        Assert.Equal(AudioVoiceState.Paused, voice.State);

        voice.Resume();
        float[] resumed = Mix(mixer, 1);

        Assert.Equal(0.2f, resumed[0], 6);
    }

    [Fact]
    public void PauseAndResume_RejectInvalidStates()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.1f);

        using AudioVoice voice = mixer.Play(clip);

        Assert.Throws<InvalidOperationException>(voice.Resume);

        voice.Pause();

        Assert.Throws<InvalidOperationException>(voice.Pause);

        voice.Stop();

        Assert.Throws<InvalidOperationException>(voice.Resume);
        Assert.Throws<InvalidOperationException>(voice.Pause);
    }

    [Fact]
    public void Stop_IsTerminalAndReleasesTheClip()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.1f, 0.2f);

        AudioVoice voice = mixer.Play(clip);
        Mix(mixer, 1);
        voice.Stop();

        Assert.Equal(AudioVoiceState.Stopped, voice.State);
        Assert.Equal(0, clip.ActiveVoiceCount);
        Assert.Equal(0, mixer.ActiveVoiceCount);
        Assert.All(Mix(mixer, 2), value => Assert.Equal(0.0f, value));

        voice.Stop();
        clip.Dispose();
        voice.Dispose();
    }

    [Fact]
    public void DisposeVoice_IsIdempotentAndReportsDisposed()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.1f);
        AudioVoice voice = mixer.Play(clip);

        voice.Dispose();
        voice.Dispose();

        Assert.Equal(AudioVoiceState.Disposed, voice.State);
        Assert.Equal(0, clip.ActiveVoiceCount);
        Assert.Throws<ObjectDisposedException>(() => _ = voice.Gain);
        Assert.Throws<ObjectDisposedException>(() => voice.Pause());
        clip.Dispose();
    }

    [Fact]
    public void ClipDispose_IsRefusedWhileVoicesReferenceIt()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.1f);

        AudioVoice first = mixer.Play(clip);
        AudioVoice second = mixer.PlaySpatial(clip, Vector3.Zero, 1.0f, 10.0f, loop: false);

        Assert.Equal(2, clip.ActiveVoiceCount);
        Assert.Throws<InvalidOperationException>(clip.Dispose);

        first.Stop();
        Assert.Equal(1, clip.ActiveVoiceCount);
        Assert.Throws<InvalidOperationException>(clip.Dispose);

        second.Stop();
        Assert.Equal(0, clip.ActiveVoiceCount);
        clip.Dispose();
        clip.Dispose();

        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public void Clip_ExposesDecodedMetadata()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = mixer.CreateClip(
            new PcmAudioData([0.0f, 0.0f, 0.0f, 0.0f], Rate, 2));

        Assert.Equal(Rate, clip.SampleRate);
        Assert.Equal(2, clip.Channels);
        Assert.Equal(2.0 / Rate, clip.DurationSeconds, 12);
    }

    [Fact]
    public void VoiceLimit_IsBoundedAndReusable()
    {
        var mixer = new AudioMixer(1, Rate);
        AudioClip clip = MonoClip(mixer, 0.1f);

        AudioVoice first = mixer.Play(clip);

        Assert.Throws<InvalidOperationException>(() => mixer.Play(clip));

        first.Stop();

        using AudioVoice second = mixer.Play(clip);
        Assert.Equal(AudioVoiceState.Playing, second.State);
    }

    [Fact]
    public void Clips_CannotCrossAudioSystems()
    {
        var first = new AudioMixer(8, Rate);
        var second = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(first, 0.1f);
        AudioVoice voice = first.Play(clip);

        Assert.Throws<ArgumentException>(() => second.Play(clip));
        Assert.Throws<ArgumentException>(() => second.GetClipReferenceCount(clip));
        Assert.Throws<ArgumentException>(() => second.StopVoice(voice));
        Assert.Throws<ArgumentException>(() => second.DisposeVoice(voice));

        voice.Dispose();
        clip.Dispose();
    }

    [Fact]
    public void SpatialSource_AttenuatesAndPansRight()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.5f);

        using AudioVoice voice = mixer.PlaySpatial(
            clip,
            new Vector3(3.0f, 0.0f, 0.0f),
            minDistance: 1.0f,
            maxDistance: 5.0f);

        float[] output = Mix(mixer, 1);

        // Distance 3 of a 1..5 linear rolloff is 0.5, and a source directly on
        // the listener's right axis is fully right.
        Assert.Equal(0.0f, output[0], 6);
        Assert.Equal(0.25f, output[1], 6);
    }

    [Fact]
    public void SpatialSource_CoincidentWithListenerIsCenteredAndAudible()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.5f);

        using AudioVoice voice = mixer.PlaySpatial(
            clip,
            Vector3.Zero,
            minDistance: 1.0f,
            maxDistance: 5.0f);

        float[] output = Mix(mixer, 1);
        float expected = 0.5f * MathF.Sqrt(0.5f);

        Assert.Equal(expected, output[0], 6);
        Assert.Equal(expected, output[1], 6);
    }

    [Fact]
    public void SpatialSource_IsSilentAtAndBeyondMaximumDistance()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.5f);

        using AudioVoice atMaximum = mixer.PlaySpatial(
            clip,
            new Vector3(5.0f, 0.0f, 0.0f),
            minDistance: 1.0f,
            maxDistance: 5.0f);
        Assert.All(Mix(mixer, 1), value => Assert.Equal(0.0f, value));

        atMaximum.Position = new Vector3(50.0f, 0.0f, 0.0f);
        Assert.All(Mix(mixer, 1), value => Assert.Equal(0.0f, value));
    }

    [Fact]
    public void SpatialSource_UsesRotatedListenerOrientation()
    {
        var mixer = new AudioMixer(8, Rate);
        mixer.Listener.Forward = new Vector3(-1.0f, 0.0f, 0.0f);

        AudioClip clip = MonoClip(mixer, 0.5f);

        using AudioVoice voice = mixer.PlaySpatial(
            clip,
            new Vector3(0.0f, 0.0f, -5.0f),
            minDistance: 1.0f,
            maxDistance: 10.0f);

        float[] output = Mix(mixer, 1);
        float expected = 0.5f * (5.0f / 9.0f);

        Assert.Equal(0.0f, output[0], 6);
        Assert.Equal(expected, output[1], 6);
    }

    [Fact]
    public void NonSpatialVoice_IgnoresPosition()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.5f);

        using AudioVoice voice = mixer.Play(clip);
        voice.Position = new Vector3(1000.0f, 0.0f, 0.0f);

        float[] output = Mix(mixer, 1);

        Assert.Equal(0.5f, output[0], 6);
        Assert.Equal(0.5f, output[1], 6);
    }

    [Fact]
    public void ListenerAndSourceUpdatesApplyWithoutRecreatingTheVoice()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.5f);

        using AudioVoice voice = mixer.PlaySpatial(
            clip,
            new Vector3(3.0f, 0.0f, 0.0f),
            minDistance: 1.0f,
            maxDistance: 5.0f,
            loop: true);

        Mix(mixer, 1);

        mixer.Listener.Position = new Vector3(3.0f, 0.0f, 0.0f);
        voice.Position = new Vector3(3.0f, 0.0f, 0.0f);

        float[] output = Mix(mixer, 1);
        float expected = 0.5f * MathF.Sqrt(0.5f);

        Assert.Equal(expected, output[0], 6);
        Assert.Equal(expected, output[1], 6);
    }

    [Fact]
    public void GainPositionAndDistanceValidationRejectInvalidValues()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.1f);

        using AudioVoice voice = mixer.PlaySpatial(clip, Vector3.Zero, 1.0f, 25.0f);

        Assert.Throws<ArgumentOutOfRangeException>(() => voice.Gain = -0.1f);
        Assert.Throws<ArgumentOutOfRangeException>(() => voice.Gain = 1.1f);
        Assert.Throws<ArgumentOutOfRangeException>(() => voice.Gain = float.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.MasterGain = -1.0f);

        Assert.Throws<ArgumentException>(
            () => voice.Position = new Vector3(float.NaN, 0.0f, 0.0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => voice.MinDistance = -1.0f);
        Assert.Throws<ArgumentOutOfRangeException>(() => voice.MinDistance = 30.0f);
        Assert.Throws<ArgumentOutOfRangeException>(() => voice.MaxDistance = 1.0f);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => mixer.PlaySpatial(clip, Vector3.Zero, 5.0f, 5.0f));
    }

    [Fact]
    public void MixerRejectsInvalidConstructionAndMixArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioMixer(0, Rate));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioMixer(257, Rate));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioMixer(8, 0));

        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.1f);

        Assert.Throws<ArgumentException>(
            () => mixer.CreateClip(new PcmAudioData([0.0f], 8000, 1)));
        Assert.Throws<ArgumentNullException>(() => mixer.Play(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.Mix(new float[2], -1));
        Assert.Throws<ArgumentException>(() => mixer.Mix(new float[2], 2));
    }

    [Fact]
    public void Close_StopsAndDisposesVoicesAndRejectsNewPlayback()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.1f);
        AudioVoice voice = mixer.Play(clip);

        mixer.Close();
        mixer.Close();

        Assert.Equal(AudioVoiceState.Disposed, voice.State);
        Assert.Equal(0, clip.ActiveVoiceCount);
        Assert.Throws<InvalidOperationException>(() => mixer.Play(clip));
        Assert.Throws<InvalidOperationException>(() => mixer.CreateClip(
            new PcmAudioData([0.0f], Rate, 1)));
        Assert.All(Mix(mixer, 2), value => Assert.Equal(0.0f, value));

        clip.Dispose();
        voice.Dispose();
    }

    [Fact]
    public void MixAndControl_AreSafeOnConcurrentThreads()
    {
        var mixer = new AudioMixer(16, Rate);
        var samples = new float[4800];
        Array.Fill(samples, 0.01f);
        AudioClip clip = mixer.CreateClip(new PcmAudioData(samples, Rate, 1));

        bool stop = false;
        Exception? failure = null;

        var mixingThread = new Thread(() =>
        {
            var buffer = new float[64 * 2];

            try
            {
                while (!Volatile.Read(ref stop))
                {
                    mixer.Mix(buffer, 64);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        mixingThread.Start();

        for (int index = 0; index < 500; index++)
        {
            AudioVoice voice = mixer.Play(clip, loop: true);
            voice.Gain = (index % 100) / 100.0f;
            voice.Pause();
            voice.Resume();
            voice.Stop();
            voice.Dispose();
        }

        Volatile.Write(ref stop, true);
        mixingThread.Join();

        Assert.Null(failure);
        Assert.Equal(0, mixer.ActiveVoiceCount);
        clip.Dispose();
    }

    [Fact]
    public void Mix_DoesNotAllocatePerBlock()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.1f, 0.2f);
        using AudioVoice voice = mixer.Play(clip, loop: true);
        var buffer = new float[512 * 2];

        for (int index = 0; index < 10; index++)
        {
            mixer.Mix(buffer, 512);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int index = 0; index < 100; index++)
        {
            mixer.Mix(buffer, 512);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void StereoEffectStrength_DefaultsToFullPositionalSeparating()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.5f);

        Assert.Equal(1.0f, mixer.StereoEffectStrength);

        using AudioVoice voice = mixer.PlaySpatial(
            clip,
            new Vector3(3.0f, 0.0f, 0.0f),
            minDistance: 1.0f,
            maxDistance: 5.0f);

        float[] output = Mix(mixer, 1);

        // Distance attenuation 0.5 and a source fully on the right axis.
        Assert.Equal(0.0f, output[0], 6);
        Assert.Equal(0.25f, output[1], 6);
    }

    [Theory]
    [InlineData(0.0f, 0.1767767f, 0.1767767f)]
    [InlineData(0.5f, 0.09567086f, 0.23096987f)]
    [InlineData(1.0f, 0.0f, 0.25f)]
    public void StereoEffectStrength_BlendsPositionalPanning(
        float strength,
        float expectedLeft,
        float expectedRight)
    {
        var mixer = new AudioMixer(8, Rate) { StereoEffectStrength = strength };
        AudioClip clip = MonoClip(mixer, 0.5f);

        using AudioVoice voice = mixer.PlaySpatial(
            clip,
            new Vector3(3.0f, 0.0f, 0.0f),
            minDistance: 1.0f,
            maxDistance: 5.0f);

        float[] output = Mix(mixer, 1);

        Assert.Equal(expectedLeft, output[0], 6);
        Assert.Equal(expectedRight, output[1], 6);
    }

    [Fact]
    public void StereoEffectStrength_IsSymmetricForLeftAndRightSources()
    {
        var mixer = new AudioMixer(8, Rate) { StereoEffectStrength = 0.5f };
        AudioClip clip = MonoClip(mixer, 0.5f);

        using AudioVoice right = mixer.PlaySpatial(
            clip,
            new Vector3(3.0f, 0.0f, 0.0f),
            minDistance: 1.0f,
            maxDistance: 5.0f);

        float[] rightOutput = Mix(mixer, 1);

        right.Stop();

        using AudioVoice left = mixer.PlaySpatial(
            clip,
            new Vector3(-3.0f, 0.0f, 0.0f),
            minDistance: 1.0f,
            maxDistance: 5.0f);

        float[] leftOutput = Mix(mixer, 1);

        Assert.Equal(rightOutput[0], leftOutput[1], 6);
        Assert.Equal(rightOutput[1], leftOutput[0], 6);
        Assert.True(leftOutput[0] > leftOutput[1]);
    }

    [Fact]
    public void StereoEffectStrength_CentersCoincidentSources()
    {
        var mixer = new AudioMixer(8, Rate) { StereoEffectStrength = 0.0f };
        AudioClip clip = MonoClip(mixer, 0.5f);

        using AudioVoice voice = mixer.PlaySpatial(
            clip,
            Vector3.Zero,
            minDistance: 1.0f,
            maxDistance: 5.0f);

        float[] output = Mix(mixer, 1);
        float expected = 0.5f * MathF.Sqrt(0.5f);

        Assert.Equal(expected, output[0], 6);
        Assert.Equal(expected, output[1], 6);
    }

    [Fact]
    public void StereoEffectStrength_UpdatesApplyWithoutRestartingTheVoice()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = MonoClip(mixer, 0.5f);

        using AudioVoice voice = mixer.PlaySpatial(
            clip,
            new Vector3(3.0f, 0.0f, 0.0f),
            minDistance: 1.0f,
            maxDistance: 5.0f,
            loop: true);

        Assert.Equal(0.0f, Mix(mixer, 1)[0], 6);

        mixer.StereoEffectStrength = 0.0f;
        float[] centered = Mix(mixer, 1);

        Assert.Equal(0.1767767f, centered[0], 6);
        Assert.Equal(0.1767767f, centered[1], 6);
        Assert.Equal(AudioVoiceState.Playing, voice.State);

        mixer.StereoEffectStrength = 0.5f;
        float[] half = Mix(mixer, 1);

        Assert.Equal(0.09567086f, half[0], 6);
        Assert.Equal(0.23096987f, half[1], 6);

        mixer.StereoEffectStrength = 1.0f;
        float[] full = Mix(mixer, 1);

        Assert.Equal(0.0f, full[0], 6);
        Assert.Equal(0.25f, full[1], 6);
        Assert.Equal(AudioVoiceState.Playing, voice.State);
    }

    [Fact]
    public void StereoEffectStrength_IsIndependentOfDistanceAndGain()
    {
        var mixer = new AudioMixer(8, Rate) { StereoEffectStrength = 0.0f };
        mixer.MasterGain = 0.5f;
        AudioClip clip = MonoClip(mixer, 0.5f);

        using AudioVoice voice = mixer.PlaySpatial(
            clip,
            new Vector3(3.0f, 0.0f, 0.0f),
            minDistance: 1.0f,
            maxDistance: 5.0f,
            loop: true);
        voice.Gain = 0.5f;

        float[] nearOutput = Mix(mixer, 1);
        float expectedNear = 0.5f * 0.5f * 0.5f * 0.5f * MathF.Sqrt(0.5f);

        Assert.Equal(expectedNear, nearOutput[0], 6);
        Assert.Equal(expectedNear, nearOutput[1], 6);

        voice.Position = new Vector3(1.0f, 0.0f, 0.0f);
        float[] closerOutput = Mix(mixer, 1);
        float expectedCloser = expectedNear * 2.0f;

        Assert.Equal(expectedCloser, closerOutput[0], 6);
        Assert.Equal(expectedCloser, closerOutput[1], 6);
    }

    [Fact]
    public void StereoEffectStrength_KeepsStereoClipChannels()
    {
        var mixer = new AudioMixer(8, Rate);
        AudioClip clip = mixer.CreateClip(
            new PcmAudioData([0.25f, -0.25f], Rate, 2));

        using AudioVoice voice = mixer.PlaySpatial(
            clip,
            new Vector3(3.0f, 0.0f, 0.0f),
            minDistance: 1.0f,
            maxDistance: 5.0f,
            loop: true);

        Assert.Equal(0.0f, Mix(mixer, 1)[0], 6);

        mixer.StereoEffectStrength = 0.0f;
        float[] centered = Mix(mixer, 1);

        // Both source channels are still present and distinct: the left and
        // right content is multiplied by the same positional gain, not mixed.
        Assert.Equal(0.08838835f, centered[0], 6);
        Assert.Equal(-0.08838835f, centered[1], 6);
    }

    [Fact]
    public void StereoEffectStrength_RejectsInvalidValues()
    {
        var mixer = new AudioMixer(8, Rate);

        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.StereoEffectStrength = -0.1f);
        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.StereoEffectStrength = 1.1f);
        Assert.Throws<ArgumentOutOfRangeException>(() => mixer.StereoEffectStrength = float.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => mixer.StereoEffectStrength = float.PositiveInfinity);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => mixer.StereoEffectStrength = float.NegativeInfinity);

        Assert.Equal(1.0f, mixer.StereoEffectStrength);
        mixer.StereoEffectStrength = 0.0f;
        Assert.Equal(0.0f, mixer.StereoEffectStrength);
        mixer.StereoEffectStrength = 1.0f;
        Assert.Equal(1.0f, mixer.StereoEffectStrength);
    }

    private static AudioClip MonoClip(AudioMixer mixer, params float[] samples) =>
        mixer.CreateClip(new PcmAudioData(samples, Rate, 1));

    private static float[] Mix(AudioMixer mixer, int frameCount)
    {
        var buffer = new float[frameCount * 2];
        mixer.Mix(buffer, frameCount);
        return buffer;
    }
}
