namespace Nymeria.Audio;

/// <summary>
/// The lifecycle state of an <see cref="AudioVoice"/>.
/// </summary>
public enum AudioVoiceState
{
    /// <summary>The voice is mixing and its frame position advances.</summary>
    Playing,

    /// <summary>
    /// The voice is silent and its frame position is held. It can be resumed or
    /// stopped.
    /// </summary>
    Paused,

    /// <summary>
    /// The voice was stopped explicitly. Stopping is terminal; create a new voice
    /// to play the clip again.
    /// </summary>
    Stopped,

    /// <summary>
    /// A non-looping voice reached the end of its clip and cannot be resumed.
    /// Its slot and clip reference are released by the next update or playback.
    /// </summary>
    Ended,

    /// <summary>
    /// The voice or its audio system was disposed. It holds no resources.
    /// </summary>
    Disposed,
}
