using System.Numerics;

namespace Nymeria.Audio;

/// <summary>
/// One independent playback instance of an <see cref="AudioClip"/>.
/// </summary>
/// <remarks>
/// <para>
/// Several voices may play the same clip at once; each has its own frame
/// position, gain, looping flag, and spatial position. A voice is created by
/// <see cref="AudioSystem.Play"/> (2D) or <see cref="AudioSystem.PlaySpatial"/>
/// (3D) and must be disposed when no longer needed.
/// </para>
/// <para>
/// <see cref="Stop"/> and <see cref="Dispose"/> release the voice's clip
/// reference and the voice becomes terminal. Pausing holds both the frame
/// position and the clip reference, so a paused voice can be resumed.
/// Disposing the owning <see cref="AudioSystem"/> stops and disposes every voice.
/// </para>
/// <para>
/// A non-spatial voice ignores <see cref="Position"/>, <see cref="MinDistance"/>,
/// and <see cref="MaxDistance"/>; the properties remain available so generic
/// update code can set them on every voice. Gain is a linear amplitude
/// multiplier from 0 through 1, not decibels.
/// </para>
/// </remarks>
public sealed class AudioVoice : IDisposable
{
    internal AudioVoice(
        AudioMixer mixer,
        AudioClip clip,
        int slot,
        bool isSpatial,
        bool isLooping,
        Vector3 position,
        float minDistance,
        float maxDistance)
    {
        _mixer = mixer;
        ClipValue = clip;
        Slot = slot;
        SpatialValue = isSpatial;
        LoopValue = isLooping;
        PositionValue = position;
        MinDistanceValue = minDistance;
        MaxDistanceValue = maxDistance;
        StateValue = AudioVoiceState.Playing;
    }

    private readonly AudioMixer _mixer;

    internal AudioMixer Mixer => _mixer;

    internal AudioClip? ClipValue { get; set; }

    internal int Slot { get; set; }

    internal int Frame { get; set; }

    internal float GainValue { get; set; } = 1.0f;

    internal bool LoopValue { get; set; }

    internal Vector3 PositionValue { get; set; }

    internal float MinDistanceValue { get; set; }

    internal float MaxDistanceValue { get; set; }

    internal bool SpatialValue { get; }

    internal AudioVoiceState StateValue { get; set; }

    /// <summary>
    /// Gets or sets whether a non-looping voice reached the end of its clip
    /// during mixing. The update thread converts this to <see cref="AudioVoiceState.Ended"/>.
    /// </summary>
    internal bool Finished { get; set; }

    internal bool DisposedValue { get; set; }

    /// <summary>
    /// Gets the current lifecycle state. A disposed voice reports
    /// <see cref="AudioVoiceState.Disposed"/>; other members throw
    /// <see cref="ObjectDisposedException"/>.
    /// </summary>
    public AudioVoiceState State => _mixer.GetState(this);

    /// <summary>Gets whether the voice is currently mixing.</summary>
    public bool IsPlaying => State == AudioVoiceState.Playing;

    /// <summary>Gets whether the voice is paused.</summary>
    public bool IsPaused => State == AudioVoiceState.Paused;

    /// <summary>
    /// Gets whether this voice is spatialized against the listener.
    /// </summary>
    public bool IsSpatial => SpatialValue;

    /// <summary>
    /// Gets or sets whether playback wraps to the start after the last frame.
    /// </summary>
    /// <remarks>
    /// The flag can change while the voice is playing; the mixer reads it at the
    /// end of each clip pass. The default is the value supplied at playback time.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The voice has been disposed.</exception>
    public bool IsLooping
    {
        get => _mixer.GetLooping(this);
        set => _mixer.SetLooping(this, value);
    }

    /// <summary>Gets or sets the linear source gain, from 0 through 1.</summary>
    /// <exception cref="ObjectDisposedException">The voice has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is not finite or is outside 0 through 1.
    /// </exception>
    public float Gain
    {
        get => _mixer.GetGain(this);
        set => _mixer.SetGain(this, value);
    }

    /// <summary>Gets or sets the source position in world space.</summary>
    /// <remarks>
    /// The value is ignored by non-spatial voices. It is read by the mixer for
    /// spatial voices each time it mixes frames, so an application can move a
    /// source between updates without recreating the voice.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The voice has been disposed.</exception>
    /// <exception cref="ArgumentException">The value is not finite.</exception>
    public Vector3 Position
    {
        get => _mixer.GetPosition(this);
        set => _mixer.SetPosition(this, value);
    }

    /// <summary>
    /// Gets or sets the radius within which a spatial source plays at full gain.
    /// </summary>
    /// <remarks>
    /// The value must be finite and nonnegative, and smaller than
    /// <see cref="MaxDistance"/>. Set the maximum distance first when moving an
    /// existing voice into a range that would otherwise be invalid.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The voice has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is non-finite, negative, or not smaller than the maximum
    /// distance.
    /// </exception>
    public float MinDistance
    {
        get => _mixer.GetMinDistance(this);
        set => _mixer.SetMinDistance(this, value);
    }

    /// <summary>
    /// Gets or sets the distance at which a spatial source becomes silent.
    /// </summary>
    /// <remarks>
    /// The value must be finite and greater than <see cref="MinDistance"/>.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The voice has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is non-finite or not greater than the minimum distance.
    /// </exception>
    public float MaxDistance
    {
        get => _mixer.GetMaxDistance(this);
        set => _mixer.SetMaxDistance(this, value);
    }

    /// <summary>
    /// Pauses a playing voice. The frame position is held and the clip reference
    /// is retained.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The voice has been disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The voice is not playing; only playing voices can be paused.
    /// </exception>
    public void Pause() => _mixer.PauseVoice(this);

    /// <summary>
    /// Resumes a paused voice from its held frame position.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The voice has been disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The voice is not paused; only paused voices can be resumed.
    /// </exception>
    public void Resume() => _mixer.ResumeVoice(this);

    /// <summary>
    /// Stops the voice and releases its clip reference. Stopping is terminal and
    /// idempotent; a stopped or naturally ended voice is left unchanged. Create a
    /// new voice to play again.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The voice has been disposed.</exception>
    public void Stop() => _mixer.StopVoice(this);

    /// <summary>
    /// Stops the voice if needed, releases its clip reference, and marks it
    /// disposed. Disposing more than once is a no-op.
    /// </summary>
    public void Dispose() => _mixer.DisposeVoice(this);
}
