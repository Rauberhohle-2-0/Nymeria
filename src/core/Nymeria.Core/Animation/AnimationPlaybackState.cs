namespace Nymeria.Core.Animation;

/// <summary>
/// The lifecycle state of an <see cref="AnimationPlayer"/>.
/// </summary>
public enum AnimationPlaybackState
{
    /// <summary>
    /// No time is advancing. <see cref="AnimationPlayer.Stop"/> leaves the target
    /// at the clip's start pose.
    /// </summary>
    Stopped,

    /// <summary>Time advances on each update and the target transform is written.</summary>
    Playing,

    /// <summary>The current pose is held; time and the target are unchanged.</summary>
    Paused,

    /// <summary>
    /// A non-looping clip reached its duration. The final pose is held and cannot
    /// advance further until the player is restarted.
    /// </summary>
    Completed,
}
