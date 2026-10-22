using System.Numerics;
using Nymeria.Core.Scenes;

namespace Nymeria.Core.Animation;

/// <summary>
/// An immutable transform animation clip with optional translation, rotation,
/// and scale tracks.
/// </summary>
/// <remarks>
/// <para>
/// A clip animates any subset of the three transform channels. A missing track
/// leaves that channel untouched when the clip is applied to a
/// <see cref="Transform"/>. Tracks are sampled independently and each clamps at
/// its own first and last keyframe.
/// </para>
/// <para>
/// The clip duration must be finite, nonnegative, and no greater than
/// <see cref="MaxDurationSeconds"/>. Every track's last keyframe must fall at or
/// before the duration, so a clip never holds timestamps outside its own range.
/// Zero-duration clips are allowed only with keyframes at time zero; they sample
/// a constant pose. At least one track is required.
/// </para>
/// <para>
/// Sampling is deterministic, frame-rate independent, and free of SDL3, GPU,
/// window, and scene-membership state. Clips are immutable after construction
/// and safe for concurrent reads.
/// </para>
/// </remarks>
public sealed class TransformAnimationClip
{
    /// <summary>The largest accepted clip duration, in seconds (24 hours).</summary>
    public const float MaxDurationSeconds = 86_400.0f;

    /// <summary>
    /// Creates a transform animation clip.
    /// </summary>
    /// <param name="durationSeconds">
    /// The clip length in seconds, from zero through <see cref="MaxDurationSeconds"/>.
    /// </param>
    /// <param name="translation">The optional position track.</param>
    /// <param name="rotation">The optional rotation track.</param>
    /// <param name="scale">The optional scale track.</param>
    /// <param name="name">An optional diagnostic name; it does not bind a target.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="durationSeconds"/> is negative, non-finite, or too large.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// No track is supplied, or a track ends after the clip duration.
    /// </exception>
    public TransformAnimationClip(
        float durationSeconds,
        Vector3Track? translation = null,
        QuaternionTrack? rotation = null,
        Vector3Track? scale = null,
        string? name = null)
    {
        if (!float.IsFinite(durationSeconds) ||
            durationSeconds < 0.0f ||
            durationSeconds > MaxDurationSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationSeconds),
                durationSeconds,
                $"The clip duration must be finite, nonnegative, and no greater " +
                $"than {MaxDurationSeconds} seconds.");
        }

        if (translation is null && rotation is null && scale is null)
        {
            throw new ArgumentException(
                "A clip must animate at least one transform channel.",
                nameof(translation));
        }

        ValidateTrackEnd(translation, durationSeconds, nameof(translation));
        ValidateTrackEnd(rotation, durationSeconds, nameof(rotation));
        ValidateTrackEnd(scale, durationSeconds, nameof(scale));

        DurationSeconds = durationSeconds;
        Translation = translation;
        Rotation = rotation;
        Scale = scale;
        Name = string.IsNullOrWhiteSpace(name) ? null : name;
    }

    /// <summary>Gets the optional diagnostic name.</summary>
    public string? Name { get; }

    /// <summary>Gets the clip duration in seconds.</summary>
    public float DurationSeconds { get; }

    /// <summary>Gets the optional position track.</summary>
    public Vector3Track? Translation { get; }

    /// <summary>Gets the optional rotation track.</summary>
    public QuaternionTrack? Rotation { get; }

    /// <summary>Gets the optional scale track.</summary>
    public Vector3Track? Scale { get; }

    /// <summary>Gets whether the clip animates position.</summary>
    public bool HasTranslation => Translation is not null;

    /// <summary>Gets whether the clip animates rotation.</summary>
    public bool HasRotation => Rotation is not null;

    /// <summary>Gets whether the clip animates scale.</summary>
    public bool HasScale => Scale is not null;

    /// <summary>
    /// Samples the clip at the supplied local time.
    /// </summary>
    /// <remarks>
    /// The time must lie within <c>[0, DurationSeconds]</c>. Sampling exactly at
    /// a keyframe returns that keyframe's value; sampling before a track's first
    /// keyframe or after its last keyframe clamps to the nearest keyframe.
    /// </remarks>
    /// <param name="time">The clip-local time in seconds.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="time"/> is negative, non-finite, or later than the clip
    /// duration.
    /// </exception>
    public TransformPose Sample(float time)
    {
        if (!float.IsFinite(time) ||
            time < 0.0f ||
            time > DurationSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                time,
                $"The clip time must be finite and between 0 and " +
                $"{DurationSeconds} seconds.");
        }

        return new TransformPose(
            Translation?.Evaluate(time),
            Rotation?.Evaluate(time),
            Scale?.Evaluate(time));
    }

    /// <summary>
    /// Samples the clip and applies its channels to a transform.
    /// </summary>
    /// <remarks>
    /// Only animated channels are written. A position track changes
    /// <see cref="Transform.Position"/>, a rotation track changes
    /// <see cref="Transform.Rotation"/>, and a scale track changes
    /// <see cref="Transform.Scale"/>; the other values are preserved.
    /// </remarks>
    /// <param name="time">The clip-local time in seconds.</param>
    /// <param name="target">The transform to update. It is not owned or retained.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="time"/> is outside the clip range.
    /// </exception>
    public void Apply(float time, Transform target)
    {
        ArgumentNullException.ThrowIfNull(target);

        TransformPose pose = Sample(time);

        if (pose.Translation is { } translation)
        {
            target.Position = translation;
        }

        if (pose.Rotation is { } rotation)
        {
            target.Rotation = rotation;
        }

        if (pose.Scale is { } scale)
        {
            target.Scale = scale;
        }
    }

    private static void ValidateTrackEnd(
        Vector3Track? track,
        float durationSeconds,
        string parameterName)
    {
        if (track is not null && track.EndTime > durationSeconds)
        {
            throw new ArgumentException(
                $"The track ends at {track.EndTime} seconds, which is later than " +
                $"the clip duration of {durationSeconds} seconds.",
                parameterName);
        }
    }

    private static void ValidateTrackEnd(
        QuaternionTrack? track,
        float durationSeconds,
        string parameterName)
    {
        if (track is not null && track.EndTime > durationSeconds)
        {
            throw new ArgumentException(
                $"The track ends at {track.EndTime} seconds, which is later than " +
                $"the clip duration of {durationSeconds} seconds.",
                parameterName);
        }
    }
}
