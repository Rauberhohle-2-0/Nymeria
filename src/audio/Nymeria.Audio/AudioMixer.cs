using System.Numerics;
using Nymeria.Core.Audio;

namespace Nymeria.Audio;

/// <summary>
/// A bounded, device-independent stereo mixer shared by the SDL audio callback
/// and the application thread.
/// </summary>
/// <remarks>
/// <para>
/// Mixing always produces interleaved 32-bit floating-point stereo frames at
/// <see cref="SampleRate"/>. Mono clips are copied to both channels; stereo
/// clips keep their channels. Each voice contributes
/// <c>sample × voice gain × master gain</c>, multiplied by distance attenuation
/// and equal-power pan gains when the voice is spatial. The pan is scaled by
/// <see cref="StereoEffectStrength"/> before the equal-power law, so <c>0</c>
/// centers positional sources and <c>1</c> preserves the full separation.
/// Contributions are accumulated and the final mix is hard-clipped to
/// <c>[-1, 1]</c>; there is no limiter or compressor.
/// </para>
/// <para>
/// One lock protects the voice slots, clip reference counts, listener, and
/// master gain. The lock is held only for state reads and sample mixing, never
/// for file or device I/O. The mixer itself never calls into SDL3; it is tested
/// headlessly.
/// </para>
/// </remarks>
internal sealed class AudioMixer
{
    private readonly object _sync = new();
    private readonly AudioVoice?[] _voices;
    private readonly AudioListener _listener = new();

    private float _masterGain = 1.0f;
    private float _stereoEffectStrength = 1.0f;
    private bool _closed;

    public AudioMixer(int maxVoices, int sampleRate)
    {
        if (maxVoices is < 1 or > AudioSystemOptions.MaxVoiceLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(maxVoices));
        }

        if (sampleRate is < PcmAudioData.MinSampleRate or > PcmAudioData.MaxSampleRate)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        _voices = new AudioVoice?[maxVoices];
        SampleRate = sampleRate;
    }

    public int SampleRate { get; }

    public int MaxVoices => _voices.Length;

    public AudioListener Listener => _listener;

    public float MasterGain
    {
        get
        {
            lock (_sync)
            {
                return _masterGain;
            }
        }

        set
        {
            ValidateGain(value, nameof(value));

            lock (_sync)
            {
                _masterGain = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets the positional stereo effect strength applied to spatial
    /// voices, from 0 through 1.
    /// </summary>
    /// <remarks>
    /// <c>1</c> keeps the full left/right separation, <c>0</c> centers a
    /// positional source in the stereo output, and intermediate values scale the
    /// pan toward the center. The change applies on the next mix block without
    /// restarting a voice. Stereo clips keep their own channel content; this is
    /// not a stereo-width or downmix control.
    /// </remarks>
    public float StereoEffectStrength
    {
        get
        {
            lock (_sync)
            {
                return _stereoEffectStrength;
            }
        }

        set
        {
            ValidateStereoEffectStrength(value);

            lock (_sync)
            {
                _stereoEffectStrength = value;
            }
        }
    }

    public int ActiveVoiceCount
    {
        get
        {
            lock (_sync)
            {
                int count = 0;

                for (int index = 0; index < _voices.Length; index++)
                {
                    if (_voices[index] is not null)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
    }

    public AudioClip CreateClip(PcmAudioData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.SampleRate != SampleRate)
        {
            throw new ArgumentException(
                $"The clip data must be at the mixer's {SampleRate} Hz sample rate.",
                nameof(data));
        }

        lock (_sync)
        {
            ThrowIfClosed();
            return new AudioClip(this, data);
        }
    }

    public AudioVoice Play(AudioClip clip, bool loop = false) =>
        PlayCore(
            clip,
            isSpatial: false,
            position: Vector3.Zero,
            minDistance: 1.0f,
            maxDistance: 25.0f,
            loop);

    public AudioVoice PlaySpatial(
        AudioClip clip,
        Vector3 position,
        float minDistance = 1.0f,
        float maxDistance = 25.0f,
        bool loop = false) =>
        PlayCore(clip, isSpatial: true, position, minDistance, maxDistance, loop);

    /// <summary>
    /// Mixes the next block into <paramref name="destination"/>. The destination
    /// is always fully overwritten with stereo frames.
    /// </summary>
    public void Mix(float[] destination, int frameCount)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);

        if ((long)frameCount * 2 > destination.Length)
        {
            throw new ArgumentException(
                "The destination is too small for the requested frame count.",
                nameof(destination));
        }

        int sampleCount = frameCount * 2;
        Array.Clear(destination, 0, sampleCount);

        lock (_sync)
        {
            if (_closed || frameCount == 0)
            {
                return;
            }

            float masterGain = _masterGain;
            float stereoEffectStrength = _stereoEffectStrength;
            Vector3 listenerPosition = _listener.Position;
            Vector3 listenerRight = _listener.Right;

            for (int index = 0; index < _voices.Length; index++)
            {
                AudioVoice? voice = _voices[index];

                if (voice is null ||
                    voice.Finished ||
                    voice.StateValue != AudioVoiceState.Playing ||
                    voice.ClipValue is null)
                {
                    continue;
                }

                float leftGain = voice.GainValue * masterGain;
                float rightGain = leftGain;

                if (voice.SpatialValue)
                {
                    float attenuation = AudioMath.DistanceAttenuation(
                        listenerPosition,
                        voice.PositionValue,
                        voice.MinDistanceValue,
                        voice.MaxDistanceValue);
                    float pan = AudioMath.StereoPan(
                        listenerPosition,
                        listenerRight,
                        voice.PositionValue);
                    (float leftPan, float rightPan) = AudioMath.SpatialGains(
                        pan,
                        stereoEffectStrength);

                    leftGain *= attenuation * leftPan;
                    rightGain *= attenuation * rightPan;
                }

                MixVoice(voice, destination, frameCount, leftGain, rightGain);
            }
        }

        for (int index = 0; index < sampleCount; index++)
        {
            destination[index] = LimitSample(destination[index]);
        }
    }

    /// <summary>
    /// Converts voices that reached the end of a non-looping clip to
    /// <see cref="AudioVoiceState.Ended"/> and releases their clip references.
    /// </summary>
    public void ReapFinished()
    {
        lock (_sync)
        {
            for (int index = 0; index < _voices.Length; index++)
            {
                AudioVoice? voice = _voices[index];

                if (voice is not null && voice.Finished)
                {
                    FinishLocked(voice);
                }
            }
        }
    }

    /// <summary>
    /// Stops every voice, releasing clip references. Voices remain usable for
    /// state inspection and can be disposed afterwards.
    /// </summary>
    public void StopAllVoices()
    {
        lock (_sync)
        {
            for (int index = 0; index < _voices.Length; index++)
            {
                AudioVoice? voice = _voices[index];

                if (voice is not null)
                {
                    StopLocked(voice);
                }
            }
        }
    }

    /// <summary>
    /// Stops and disposes every voice and prevents further playback. Used when
    /// the owning audio system is disposed.
    /// </summary>
    public void Close()
    {
        lock (_sync)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;

            for (int index = 0; index < _voices.Length; index++)
            {
                AudioVoice? voice = _voices[index];

                if (voice is not null)
                {
                    StopLocked(voice);
                    voice.DisposedValue = true;
                }
            }
        }
    }

    public int GetClipReferenceCount(AudioClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        lock (_sync)
        {
            RequireSameMixer(clip.Mixer);
            return clip.ReferenceCount;
        }
    }

    public void DisposeClip(AudioClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        lock (_sync)
        {
            RequireSameMixer(clip.Mixer);

            if (clip.IsDisposedValue)
            {
                return;
            }

            if (clip.ReferenceCount > 0)
            {
                throw new InvalidOperationException(
                    $"The audio clip is still referenced by {clip.ReferenceCount} " +
                    "playing or paused voice(s). Stop or dispose those voices first.");
            }

            clip.IsDisposedValue = true;
        }
    }

    public AudioVoiceState GetState(AudioVoice voice)
    {
        ArgumentNullException.ThrowIfNull(voice);

        lock (_sync)
        {
            RequireSameMixer(voice.Mixer);

            if (voice.DisposedValue)
            {
                return AudioVoiceState.Disposed;
            }

            return voice.Finished ? AudioVoiceState.Ended : voice.StateValue;
        }
    }

    public float GetGain(AudioVoice voice)
    {
        lock (_sync)
        {
            ThrowIfVoiceDisposed(voice);
            return voice.GainValue;
        }
    }

    public void SetGain(AudioVoice voice, float gain)
    {
        ValidateGain(gain, nameof(gain));

        lock (_sync)
        {
            ThrowIfVoiceDisposed(voice);
            voice.GainValue = gain;
        }
    }

    public bool GetLooping(AudioVoice voice)
    {
        lock (_sync)
        {
            ThrowIfVoiceDisposed(voice);
            return voice.LoopValue;
        }
    }

    public void SetLooping(AudioVoice voice, bool loop)
    {
        lock (_sync)
        {
            ThrowIfVoiceDisposed(voice);
            voice.LoopValue = loop;
        }
    }

    public Vector3 GetPosition(AudioVoice voice)
    {
        lock (_sync)
        {
            ThrowIfVoiceDisposed(voice);
            return voice.PositionValue;
        }
    }

    public void SetPosition(AudioVoice voice, Vector3 position)
    {
        ValidatePosition(position, nameof(position));

        lock (_sync)
        {
            ThrowIfVoiceDisposed(voice);
            voice.PositionValue = position;
        }
    }

    public float GetMinDistance(AudioVoice voice)
    {
        lock (_sync)
        {
            ThrowIfVoiceDisposed(voice);
            return voice.MinDistanceValue;
        }
    }

    public void SetMinDistance(AudioVoice voice, float minDistance)
    {
        lock (_sync)
        {
            ThrowIfVoiceDisposed(voice);
            ValidateDistanceRange(minDistance, voice.MaxDistanceValue, nameof(minDistance));
            voice.MinDistanceValue = minDistance;
        }
    }

    public float GetMaxDistance(AudioVoice voice)
    {
        lock (_sync)
        {
            ThrowIfVoiceDisposed(voice);
            return voice.MaxDistanceValue;
        }
    }

    public void SetMaxDistance(AudioVoice voice, float maxDistance)
    {
        lock (_sync)
        {
            ThrowIfVoiceDisposed(voice);
            ValidateDistanceRange(voice.MinDistanceValue, maxDistance, nameof(maxDistance));
            voice.MaxDistanceValue = maxDistance;
        }
    }

    public void PauseVoice(AudioVoice voice)
    {
        lock (_sync)
        {
            ThrowIfVoiceDisposed(voice);

            if (voice.Finished || voice.StateValue != AudioVoiceState.Playing)
            {
                throw new InvalidOperationException(
                    "Only a playing voice can be paused.");
            }

            voice.StateValue = AudioVoiceState.Paused;
        }
    }

    public void ResumeVoice(AudioVoice voice)
    {
        lock (_sync)
        {
            ThrowIfVoiceDisposed(voice);

            if (voice.StateValue != AudioVoiceState.Paused)
            {
                throw new InvalidOperationException(
                    "Only a paused voice can be resumed.");
            }

            voice.StateValue = AudioVoiceState.Playing;
        }
    }

    public void StopVoice(AudioVoice voice)
    {
        lock (_sync)
        {
            ThrowIfVoiceDisposed(voice);

            // Stopping an already stopped or naturally ended voice must not
            // move it back out of its terminal state.
            if (voice.StateValue is AudioVoiceState.Ended or AudioVoiceState.Stopped)
            {
                return;
            }

            StopLocked(voice);
        }
    }

    public void DisposeVoice(AudioVoice voice)
    {
        ArgumentNullException.ThrowIfNull(voice);

        lock (_sync)
        {
            if (voice.DisposedValue)
            {
                return;
            }

            RequireSameMixer(voice.Mixer);
            StopLocked(voice);
            voice.DisposedValue = true;
        }
    }

    private AudioVoice PlayCore(
        AudioClip clip,
        bool isSpatial,
        Vector3 position,
        float minDistance,
        float maxDistance,
        bool loop)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ValidatePosition(position, nameof(position));
        ValidateDistanceRange(minDistance, maxDistance, nameof(minDistance));

        lock (_sync)
        {
            ThrowIfClosed();
            RequireSameMixer(clip.Mixer);
            ReapFinishedLocked();

            if (clip.IsDisposedValue)
            {
                throw new ObjectDisposedException(nameof(clip));
            }

            int slot = FindFreeSlot();

            if (slot < 0)
            {
                throw new InvalidOperationException(
                    $"All {_voices.Length} audio voice slots are in use.");
            }

            clip.ReferenceCount++;
            var voice = new AudioVoice(
                this,
                clip,
                slot,
                isSpatial,
                loop,
                position,
                minDistance,
                maxDistance);

            _voices[slot] = voice;
            return voice;
        }
    }

    private void MixVoice(
        AudioVoice voice,
        float[] destination,
        int frameCount,
        float leftGain,
        float rightGain)
    {
        AudioClip clip = voice.ClipValue!;
        PcmAudioData data = clip.Data;
        ReadOnlySpan<float> samples = data.Samples.Span;
        int channelCount = data.Channels;
        int clipFrameCount = data.FrameCount;
        int frame = voice.Frame;

        for (int index = 0; index < frameCount; index++)
        {
            if (frame >= clipFrameCount)
            {
                if (!voice.LoopValue)
                {
                    voice.Finished = true;
                    break;
                }

                frame = 0;
            }

            int sourceIndex = frame * channelCount;
            int destinationIndex = index * 2;

            if (channelCount == 1)
            {
                float sample = samples[sourceIndex];
                destination[destinationIndex] += sample * leftGain;
                destination[destinationIndex + 1] += sample * rightGain;
            }
            else
            {
                destination[destinationIndex] += samples[sourceIndex] * leftGain;
                destination[destinationIndex + 1] += samples[sourceIndex + 1] * rightGain;
            }

            frame++;
        }

        voice.Frame = frame;
    }

    private void ReapFinishedLocked()
    {
        for (int index = 0; index < _voices.Length; index++)
        {
            AudioVoice? voice = _voices[index];

            if (voice is not null && voice.Finished)
            {
                FinishLocked(voice);
            }
        }
    }

    private void FinishLocked(AudioVoice voice)
    {
        voice.Finished = false;
        voice.StateValue = AudioVoiceState.Ended;
        ReleaseClipLocked(voice);
        ClearSlotLocked(voice);
    }

    private void StopLocked(AudioVoice voice)
    {
        ReleaseClipLocked(voice);
        ClearSlotLocked(voice);
        voice.Finished = false;
        voice.StateValue = AudioVoiceState.Stopped;
    }

    private void ReleaseClipLocked(AudioVoice voice)
    {
        AudioClip? clip = voice.ClipValue;

        if (clip is null)
        {
            return;
        }

        voice.ClipValue = null;

        if (clip.ReferenceCount > 0)
        {
            clip.ReferenceCount--;
        }
    }

    private void ClearSlotLocked(AudioVoice voice)
    {
        if (voice.Slot >= 0 && ReferenceEquals(_voices[voice.Slot], voice))
        {
            _voices[voice.Slot] = null;
        }

        voice.Slot = -1;
    }

    private int FindFreeSlot()
    {
        for (int index = 0; index < _voices.Length; index++)
        {
            if (_voices[index] is null)
            {
                return index;
            }
        }

        return -1;
    }

    private void ThrowIfClosed()
    {
        if (_closed)
        {
            throw new InvalidOperationException("The audio system has been disposed.");
        }
    }

    private void ThrowIfVoiceDisposed(AudioVoice voice)
    {
        ArgumentNullException.ThrowIfNull(voice);

        ObjectDisposedException.ThrowIf(voice.DisposedValue, voice);
        RequireSameMixer(voice.Mixer);
    }

    private void RequireSameMixer(AudioMixer mixer)
    {
        if (!ReferenceEquals(mixer, this))
        {
            throw new ArgumentException("The object belongs to a different audio system.");
        }
    }

    private static void ValidateStereoEffectStrength(float strength)
    {
        if (!float.IsFinite(strength) || strength is < 0.0f or > 1.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(strength),
                strength,
                "The stereo effect strength must be finite and between 0 and 1.");
        }
    }

    private static void ValidateGain(float gain, string parameterName)
    {
        if (!float.IsFinite(gain) || gain is < 0.0f or > 1.0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                gain,
                "The gain must be finite and between 0 and 1.");
        }
    }

    private static void ValidatePosition(Vector3 position, string parameterName)
    {
        if (!float.IsFinite(position.X) ||
            !float.IsFinite(position.Y) ||
            !float.IsFinite(position.Z))
        {
            throw new ArgumentException(
                "The position must contain only finite components.",
                parameterName);
        }
    }

    private static void ValidateDistanceRange(
        float minDistance,
        float maxDistance,
        string parameterName)
    {
        if (!float.IsFinite(minDistance) || minDistance < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                minDistance,
                "The minimum distance must be finite and nonnegative.");
        }

        if (!float.IsFinite(maxDistance) || maxDistance <= minDistance)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                maxDistance,
                "The maximum distance must be finite and greater than the minimum distance.");
        }
    }

    private static float LimitSample(float value)
    {
        if (float.IsNaN(value))
        {
            return 0.0f;
        }

        if (float.IsPositiveInfinity(value))
        {
            return 1.0f;
        }

        if (float.IsNegativeInfinity(value))
        {
            return -1.0f;
        }

        return Math.Clamp(value, -1.0f, 1.0f);
    }
}
