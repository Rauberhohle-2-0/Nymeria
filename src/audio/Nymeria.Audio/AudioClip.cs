using Nymeria.Core.Audio;

namespace Nymeria.Audio;

/// <summary>
/// Decoded, resampled audio that can be played by several simultaneous voices.
/// </summary>
/// <remarks>
/// <para>
/// A clip is created by <see cref="AudioSystem.LoadWav(Stream)"/> and owns only
/// managed sample memory. It has no native handle, so it can be shared by any
/// number of voices without duplicating the samples. Voices hold a reference to
/// the clip for as long as they can still play it.
/// </para>
/// <para>
/// <see cref="Dispose"/> is refused with an <see cref="InvalidOperationException"/>
/// while a playing or paused voice still references the clip, because disposing
/// it would invalidate that voice's sample memory. Stopping or disposing those
/// voices releases their references, after which the clip can be disposed.
/// Disposing an already disposed clip is a no-op.
/// </para>
/// <para>
/// Clips and voices belong to one <see cref="AudioSystem"/>; do not share a clip
/// between systems. Control calls are expected on the application thread that
/// owns the system.
/// </para>
/// </remarks>
public sealed class AudioClip : IDisposable
{
    private readonly AudioMixer _mixer;

    internal AudioClip(AudioMixer mixer, PcmAudioData data)
    {
        _mixer = mixer;
        Data = data;
    }

    internal PcmAudioData Data { get; }

    internal AudioMixer Mixer => _mixer;

    /// <summary>
    /// Gets or sets the number of live voices that reference this clip.
    /// </summary>
    /// <remarks>Mutated only while the owning mixer lock is held.</remarks>
    internal int ReferenceCount { get; set; }

    /// <summary>
    /// Gets or sets whether this clip has been disposed. Set under the owning
    /// mixer lock; reads are atomic.
    /// </summary>
    internal bool IsDisposedValue { get; set; }

    /// <summary>Gets the clip sample rate in hertz.</summary>
    /// <exception cref="ObjectDisposedException">The clip has been disposed.</exception>
    public int SampleRate
    {
        get
        {
            ThrowIfDisposed();
            return Data.SampleRate;
        }
    }

    /// <summary>Gets the clip channel count: 1 for mono or 2 for stereo.</summary>
    /// <exception cref="ObjectDisposedException">The clip has been disposed.</exception>
    public int Channels
    {
        get
        {
            ThrowIfDisposed();
            return Data.Channels;
        }
    }

    /// <summary>Gets the clip duration in seconds.</summary>
    /// <exception cref="ObjectDisposedException">The clip has been disposed.</exception>
    public double DurationSeconds
    {
        get
        {
            ThrowIfDisposed();
            return Data.DurationSeconds;
        }
    }

    /// <summary>Gets the number of voices that still reference this clip.</summary>
    public int ActiveVoiceCount => _mixer.GetClipReferenceCount(this);

    /// <summary>
    /// Releases the clip's managed sample memory.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// One or more playing or paused voices still reference the clip.
    /// </exception>
    public void Dispose() => _mixer.DisposeClip(this);

    internal void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(IsDisposedValue, this);
}
