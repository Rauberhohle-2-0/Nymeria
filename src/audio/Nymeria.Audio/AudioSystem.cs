using System.Numerics;
using System.Runtime.InteropServices;
using Nymeria.Core.Audio;
using Nymeria.Formats.Wav;
using SDL3;

namespace Nymeria.Audio;

/// <summary>
/// Owns one SDL3 playback device and a bounded stereo mixer for Nymeria audio.
/// </summary>
/// <remarks>
/// <para>
/// Creating a system initializes SDL's audio subsystem, opens the system default
/// playback device, and starts a float32 stereo stream at
/// <see cref="AudioSystemOptions.SampleRate"/>. Nothing is initialized merely by
/// referencing the assembly; a device exists only after
/// <see cref="Create"/> or <see cref="TryCreate"/> succeeds.
/// </para>
/// <para>
/// Mixing happens in SDL's audio callback, not in <see cref="Update"/>. The
/// callback mixes float32 stereo frames in bounded blocks, allocates no memory,
/// performs no file or device I/O, and takes one short lock that also protects
/// voice and clip ownership. Call <see cref="Update"/> once per frame from the
/// application thread to validate elapsed time, release voices that finished a
/// non-looping clip, and observe device loss. Playback timing is driven by the
/// audio device's sample clock, not by the frame delta.
/// </para>
/// <para>
/// Create, update, play, stop, load, and dispose on the process main thread.
/// The SDL audio callback runs on an internal audio thread and must never call
/// back into this API. See <c>docs/AUDIO.md</c> for the full contract and
/// limitations.
/// </para>
/// </remarks>
public sealed class AudioSystem : IDisposable
{
    private const int Channels = 2;
    private const int BytesPerFrame = sizeof(float) * Channels;
    private const int ScratchFrameCount = 2048;

    private static readonly SDL.AudioStreamCallback StreamCallback = OnAudioStreamCallback;

    private readonly AudioSystemOptions _options;
    private readonly AudioMixer _mixer;
    private readonly float[] _scratch = new float[ScratchFrameCount * Channels];

    private IntPtr _stream;
    private GCHandle _selfHandle;
    private volatile bool _disposed;
    private volatile bool _deviceLost;

    private AudioSystem(AudioSystemOptions options)
    {
        _options = options;
        _mixer = new AudioMixer(options.MaxVoices, options.SampleRate);
    }

    /// <summary>
    /// Gets the listener used to spatialize voices.
    /// </summary>
    /// <remarks>
    /// Update the listener from the application camera each frame. The listener
    /// validates its own values and synchronizes each property read and write.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The system has been disposed.</exception>
    public AudioListener Listener
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _mixer.Listener;
        }
    }

    /// <summary>Gets the mixing sample rate in hertz.</summary>
    public int SampleRate => _options.SampleRate;

    /// <summary>Gets the maximum number of simultaneous voices.</summary>
    public int MaxVoices => _options.MaxVoices;

    /// <summary>Gets the number of voice slots currently in use.</summary>
    /// <remarks>
    /// A voice that finished a non-looping clip keeps its slot until
    /// <see cref="Update"/> reaps it. Stopping or disposing a voice releases its
    /// slot immediately.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The system has been disposed.</exception>
    public int ActiveVoiceCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _mixer.ActiveVoiceCount;
        }
    }

    /// <summary>
    /// Gets or sets the linear master gain applied to every voice, from 0
    /// through 1.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The system has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is not finite or is outside 0 through 1.
    /// </exception>
    public float MasterGain
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _mixer.MasterGain;
        }

        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _mixer.MasterGain = value;
        }
    }

    /// <summary>
    /// Gets whether the stream lost its audio device or the audio callback failed.
    /// </summary>
    /// <remarks>
    /// This becomes true when SDL reports no bound device or when a callback
    /// cannot queue mixed frames. <see cref="Update"/> then stops all voices and
    /// releases their clip references; the system does not reopen a device
    /// automatically. Dispose the system and create a new one to recover.
    /// </remarks>
    public bool IsDeviceLost => _deviceLost;

    /// <summary>
    /// Creates an audio system or throws when no usable playback device exists.
    /// </summary>
    /// <param name="options">The system configuration, or null for the defaults.</param>
    /// <exception cref="AudioDeviceException">
    /// SDL audio initialization, device creation, or stream setup failed.
    /// </exception>
    public static AudioSystem Create(AudioSystemOptions? options = null)
    {
        options ??= new AudioSystemOptions();

        if (!TryCreate(options, out AudioSystem? audio, out string? error))
        {
            throw new AudioDeviceException(error ?? "The audio device could not be created.");
        }

        return audio!;
    }

    /// <summary>
    /// Tries to create an audio system with the default options.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the system was created; otherwise
    /// <see langword="false"/> with a non-empty diagnostic in
    /// <paramref name="error"/>.
    /// </returns>
    public static bool TryCreate(out AudioSystem? audio, out string? error) =>
        TryCreate(new AudioSystemOptions(), out audio, out error);

    /// <summary>
    /// Tries to create an audio system without throwing for device failures.
    /// </summary>
    /// <param name="options">The system configuration. It must not be null.</param>
    /// <param name="audio">The created system, or null on failure.</param>
    /// <param name="error">A diagnostic on failure, or null on success.</param>
    /// <returns><see langword="true"/> when the system was created.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public static bool TryCreate(
        AudioSystemOptions options,
        out AudioSystem? audio,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);

        audio = null;
        error = null;
        bool initializedAudio = false;
        bool succeeded = false;
        IntPtr stream = IntPtr.Zero;
        GCHandle handle = default;

        try
        {
            if (!SDL.IsMainThread())
            {
                error =
                    "The audio system must be created on the process main thread. " +
                    "Call AudioSystem.Create from Main, not from a worker thread.";
                return false;
            }

            if (!SDL.InitSubSystem(SDL.InitFlags.Audio))
            {
                error = $"SDL audio initialization failed: {SDL.GetError()}";
                return false;
            }

            initializedAudio = true;

            var system = new AudioSystem(options);
            handle = GCHandle.Alloc(system);

            var spec = new SDL.AudioSpec
            {
                Format = SDL.AudioFormat.AudioF32LE,
                Channels = Channels,
                Freq = options.SampleRate,
            };

            stream = SDL.OpenAudioDeviceStream(
                SDL.AudioDeviceDefaultPlayback,
                spec,
                StreamCallback,
                GCHandle.ToIntPtr(handle));

            if (stream == IntPtr.Zero)
            {
                error = $"SDL could not open the default playback audio device: {SDL.GetError()}";
                return false;
            }

            if (!SDL.GetAudioStreamFormat(stream, out SDL.AudioSpec source, out _))
            {
                error = $"SDL could not query the audio stream format: {SDL.GetError()}";
                return false;
            }

            if (source.Format != SDL.AudioFormat.AudioF32LE ||
                source.Channels != Channels ||
                source.Freq != options.SampleRate)
            {
                error =
                    $"The audio stream opened as {source.Freq} Hz, " +
                    $"{source.Channels} channel(s), {source.Format}, not the requested " +
                    $"{options.SampleRate} Hz stereo float32 format.";
                return false;
            }

            if (!SDL.ResumeAudioStreamDevice(stream))
            {
                error = $"SDL could not start audio playback: {SDL.GetError()}";
                return false;
            }

            system._stream = stream;
            system._selfHandle = handle;
            succeeded = true;
            audio = system;
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            error = $"SDL audio initialization failed: {exception.Message}";
            return false;
        }
        finally
        {
            if (!succeeded)
            {
                if (stream != IntPtr.Zero)
                {
                    SDL.DestroyAudioStream(stream);
                }

                if (handle.IsAllocated)
                {
                    handle.Free();
                }

                if (initializedAudio)
                {
                    SDL.QuitSubSystem(SDL.InitFlags.Audio);
                }
            }
        }
    }

    /// <summary>
    /// Loads an uncompressed PCM or float WAV file and resamples it to the
    /// system's mixing rate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The path is opened for reading and is not resolved relative to any asset
    /// directory. Missing files and ordinary I/O failures propagate as their usual
    /// exceptions. A malformed or unsupported WAV throws
    /// <see cref="WavFormatException"/>.
    /// </para>
    /// <para>
    /// The returned clip owns managed sample memory only. Dispose it when no voice
    /// references it.
    /// </para>
    /// </remarks>
    /// <param name="path">The WAV file to open.</param>
    /// <exception cref="ObjectDisposedException">The system has been disposed.</exception>
    /// <exception cref="ArgumentException">The path is empty or whitespace.</exception>
    /// <exception cref="WavFormatException">The file is malformed or unsupported.</exception>
    /// <exception cref="InvalidOperationException">
    /// Resampling to the system sample rate would exceed
    /// <see cref="PcmAudioData.MaxSampleCount"/>.
    /// </exception>
    public AudioClip LoadWav(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        return LoadWavCore(stream, path);
    }

    /// <summary>
    /// Loads an uncompressed PCM or float WAV stream and resamples it to the
    /// system's mixing rate.
    /// </summary>
    /// <remarks>
    /// The caller owns and disposes <paramref name="stream"/>; this method leaves
    /// it open.
    /// </remarks>
    /// <param name="stream">A readable stream positioned at the start of the WAV data.</param>
    /// <exception cref="ObjectDisposedException">The system has been disposed.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="WavFormatException">The content is malformed or unsupported.</exception>
    /// <exception cref="InvalidOperationException">
    /// Resampling to the system sample rate would exceed
    /// <see cref="PcmAudioData.MaxSampleCount"/>.
    /// </exception>
    public AudioClip LoadWav(Stream stream)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(stream);

        return LoadWavCore(stream, sourceName: null);
    }

    /// <summary>
    /// Starts a non-spatial voice, suitable for music and UI sounds.
    /// </summary>
    /// <param name="clip">The shared clip to play.</param>
    /// <param name="loop">Whether playback restarts after the last frame.</param>
    /// <exception cref="ObjectDisposedException">
    /// The system or clip has been disposed.
    /// </exception>
    /// <exception cref="AudioDeviceException">The playback device was lost.</exception>
    /// <exception cref="InvalidOperationException">
    /// Every voice slot is in use.
    /// </exception>
    public AudioVoice Play(AudioClip clip, bool loop = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfDeviceLost();
        ArgumentNullException.ThrowIfNull(clip);

        return _mixer.Play(clip, loop);
    }

    /// <summary>
    /// Starts a spatial voice with distance attenuation and left/right panning.
    /// </summary>
    /// <remarks>
    /// This is basic spatialization: linear distance attenuation and equal-power
    /// stereo panning against the listener. It is not HRTF, occlusion, reverb, or
    /// surround sound. The voice's position can be updated every frame without
    /// creating a new device or decoding the clip again.
    /// </remarks>
    /// <param name="clip">The shared clip to play.</param>
    /// <param name="position">The initial world-space source position.</param>
    /// <param name="minDistance">The full-volume radius, in world units.</param>
    /// <param name="maxDistance">The silence radius, in world units.</param>
    /// <param name="loop">Whether playback restarts after the last frame.</param>
    /// <exception cref="ObjectDisposedException">
    /// The system or clip has been disposed.
    /// </exception>
    /// <exception cref="AudioDeviceException">The playback device was lost.</exception>
    /// <exception cref="ArgumentException">
    /// The position is not finite, or the distances are invalid.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Every voice slot is in use.
    /// </exception>
    public AudioVoice PlaySpatial(
        AudioClip clip,
        Vector3 position,
        float minDistance = 1.0f,
        float maxDistance = 25.0f,
        bool loop = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfDeviceLost();
        ArgumentNullException.ThrowIfNull(clip);

        return _mixer.PlaySpatial(clip, position, minDistance, maxDistance, loop);
    }

    /// <summary>
    /// Performs per-frame housekeeping: observes device loss and releases voices
    /// that finished a non-looping clip.
    /// </summary>
    /// <remarks>
    /// Mixing is paced by the audio device callback, not by this method. Elapsed
    /// time is validated for a consistent call contract but does not change
    /// playback timing.
    /// </remarks>
    /// <param name="deltaSeconds">Elapsed seconds since the previous update.</param>
    /// <exception cref="ObjectDisposedException">The system has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The elapsed time is non-finite or negative.
    /// </exception>
    public void Update(float deltaSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deltaSeconds),
                deltaSeconds,
                "The elapsed time must be finite and nonnegative.");
        }

        if (_deviceLost)
        {
            _mixer.StopAllVoices();
            return;
        }

        if (SDL.GetAudioStreamDevice(_stream) == 0)
        {
            MarkDeviceLost();
            return;
        }

        _mixer.ReapFinished();
    }

    /// <summary>
    /// Stops and disposes all voices, destroys the device stream, and shuts down
    /// the audio subsystem. Disposing more than once is a no-op.
    /// </summary>
    /// <remarks>
    /// Dispose the audio system before the window, because destroying the window
    /// shuts SDL down.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mixer.Close();

        if (_stream != IntPtr.Zero)
        {
            SDL.DestroyAudioStream(_stream);
            _stream = IntPtr.Zero;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        SDL.QuitSubSystem(SDL.InitFlags.Audio);
    }

    private AudioClip LoadWavCore(Stream stream, string? sourceName)
    {
        var decoder = new WavAudioDecoder(sourceName);
        PcmAudioData decoded = decoder.Decode(stream);
        PcmAudioData resampled = decoded.Resample(_options.SampleRate);

        return _mixer.CreateClip(resampled);
    }

    private void ThrowIfDeviceLost()
    {
        if (_deviceLost)
        {
            throw new AudioDeviceException(
                "The audio playback device is no longer available. " +
                "Dispose the audio system and create a new one.");
        }
    }

    private void MarkDeviceLost()
    {
        if (_deviceLost)
        {
            return;
        }

        _deviceLost = true;
        _mixer.StopAllVoices();
    }

    private void MixFromAudioThread(IntPtr stream, int additionalBytes)
    {
        int frameCount = additionalBytes / BytesPerFrame;

        while (frameCount > 0 && !_disposed)
        {
            int blockFrames = Math.Min(frameCount, ScratchFrameCount);

            _mixer.Mix(_scratch, blockFrames);

            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(
                _scratch.AsSpan(0, blockFrames * Channels));

            if (!SDL.PutAudioStreamData(stream, bytes, bytes.Length))
            {
                _deviceLost = true;
                return;
            }

            frameCount -= blockFrames;
        }
    }

    private static void OnAudioStreamCallback(
        IntPtr userdata,
        IntPtr stream,
        int additionalBytes,
        int totalBytes)
    {
        if (userdata == IntPtr.Zero || additionalBytes <= 0)
        {
            return;
        }

        AudioSystem? system = null;

        try
        {
            if (GCHandle.FromIntPtr(userdata).Target is AudioSystem target)
            {
                system = target;
                target.MixFromAudioThread(stream, additionalBytes);
            }
        }
        catch
        {
            // Never let a managed exception cross into SDL's audio thread.
            // A callback failure is treated as device loss and handled on the
            // next Update call.
            if (system is not null)
            {
                system._deviceLost = true;
            }
        }
    }
}
