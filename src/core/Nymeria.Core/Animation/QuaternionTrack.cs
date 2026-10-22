using System.Collections.ObjectModel;
using System.Numerics;

namespace Nymeria.Core.Animation;

/// <summary>
/// An immutable, time-ordered track of quaternion keyframes with shortest-path
/// spherical interpolation.
/// </summary>
/// <remarks>
/// <para>
/// Each keyframe quaternion must be finite and have nonzero length. The track
/// normalizes every key on construction and returns normalized rotations.
/// Times follow the same rules as <see cref="Vector3Track"/>: finite,
/// nonnegative, no greater than <see cref="MaxTimeSeconds"/>, and strictly
/// increasing, so duplicate timestamps are rejected.
/// </para>
/// <para>
/// Interpolation follows the shortest rotation path by negating one endpoint
/// when the quaternion dot product is negative. Nearly identical orientations
/// fall back to a normalized linear blend to avoid a division by a vanishing
/// sine. Evaluation clamps before the first keyframe and after the last one and
/// is independent of frame rate.
/// </para>
/// </remarks>
public sealed class QuaternionTrack
{
    /// <summary>The largest accepted number of keyframes on one track.</summary>
    public const int MaxKeyframeCount = 100_000;

    /// <summary>The largest accepted keyframe time, in seconds (24 hours).</summary>
    public const float MaxTimeSeconds = 86_400.0f;

    private const float MinimumLengthSquared = 1e-24f;
    private const float LinearBlendThreshold = 0.9995f;

    private readonly QuaternionKeyframe[] _keyframes;
    private readonly ReadOnlyCollection<QuaternionKeyframe> _readOnlyKeyframes;

    /// <summary>
    /// Creates a rotation track from time-ordered keyframes.
    /// </summary>
    /// <param name="keyframes">
    /// At least one keyframe. The sequence is copied and enumerated once.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="keyframes"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The sequence is empty, exceeds <see cref="MaxKeyframeCount"/>, contains an
    /// invalid time, is not strictly increasing, or contains a non-finite or
    /// zero-length quaternion.
    /// </exception>
    public QuaternionTrack(IEnumerable<QuaternionKeyframe> keyframes)
    {
        ArgumentNullException.ThrowIfNull(keyframes);

        var validated = new List<QuaternionKeyframe>();
        float previousTime = 0.0f;

        foreach (QuaternionKeyframe keyframe in keyframes)
        {
            int index = validated.Count;

            if (index >= MaxKeyframeCount)
            {
                throw new ArgumentException(
                    $"A track cannot contain more than {MaxKeyframeCount} keyframes.",
                    nameof(keyframes));
            }

            ValidateTime(keyframe.Time, index, nameof(keyframes));

            if (index > 0 && keyframe.Time <= previousTime)
            {
                throw new ArgumentException(
                    $"Keyframe times must be strictly increasing; the time at " +
                    $"index {index} is not later than the previous keyframe.",
                    nameof(keyframes));
            }

            Quaternion normalized = Normalize(keyframe.Value, index, nameof(keyframes));
            previousTime = keyframe.Time;
            validated.Add(new QuaternionKeyframe(keyframe.Time, normalized));
        }

        if (validated.Count == 0)
        {
            throw new ArgumentException(
                "A track must contain at least one keyframe.",
                nameof(keyframes));
        }

        _keyframes = validated.ToArray();
        _readOnlyKeyframes = Array.AsReadOnly(_keyframes);
    }

    /// <summary>Gets the number of keyframes.</summary>
    public int Count => _keyframes.Length;

    /// <summary>Gets the normalized keyframes in time order.</summary>
    public IReadOnlyList<QuaternionKeyframe> Keyframes => _readOnlyKeyframes;

    /// <summary>Gets the last keyframe time, which is the track's natural length.</summary>
    public float EndTime => _keyframes[^1].Time;

    /// <summary>
    /// Evaluates the track at the supplied time.
    /// </summary>
    /// <remarks>
    /// A time at or before the first keyframe returns the first rotation; a time
    /// at or after the last keyframe returns the last rotation. Between keyframes
    /// the result is a normalized shortest-path spherical interpolation.
    /// </remarks>
    /// <param name="time">The evaluation time in seconds; finite and nonnegative.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="time"/> is negative or not finite.
    /// </exception>
    public Quaternion Evaluate(float time)
    {
        if (!float.IsFinite(time) || time < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                time,
                "The evaluation time must be finite and nonnegative.");
        }

        QuaternionKeyframe first = _keyframes[0];

        if (time <= first.Time)
        {
            return first.Value;
        }

        QuaternionKeyframe last = _keyframes[^1];

        if (time >= last.Time)
        {
            return last.Value;
        }

        int index = FindSegment(time);
        QuaternionKeyframe start = _keyframes[index];
        QuaternionKeyframe end = _keyframes[index + 1];
        float fraction = (time - start.Time) / (end.Time - start.Time);

        return Interpolate(start.Value, end.Value, fraction);
    }

    private int FindSegment(float time)
    {
        int low = 0;
        int high = _keyframes.Length - 1;

        while (high - low > 1)
        {
            int middle = low + ((high - low) / 2);

            if (_keyframes[middle].Time <= time)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static Quaternion Interpolate(Quaternion start, Quaternion end, float fraction)
    {
        float dot = Quaternion.Dot(start, end);

        if (dot < 0.0f)
        {
            // q and -q are the same rotation. Flip the endpoint so the blend
            // takes the shorter arc instead of spinning the long way around.
            end = new Quaternion(-end.X, -end.Y, -end.Z, -end.W);
            dot = -dot;
        }

        if (dot > LinearBlendThreshold)
        {
            Quaternion blended = new(
                start.X + ((end.X - start.X) * fraction),
                start.Y + ((end.Y - start.Y) * fraction),
                start.Z + ((end.Z - start.Z) * fraction),
                start.W + ((end.W - start.W) * fraction));

            return NormalizeOrFirst(blended, start);
        }

        dot = Math.Clamp(dot, -1.0f, 1.0f);
        float angle = MathF.Acos(dot);
        float sine = MathF.Sin(angle);

        if (!float.IsFinite(sine) || sine <= 1e-6f)
        {
            return start;
        }

        float startWeight = MathF.Sin((1.0f - fraction) * angle) / sine;
        float endWeight = MathF.Sin(fraction * angle) / sine;
        Quaternion result = new(
            (start.X * startWeight) + (end.X * endWeight),
            (start.Y * startWeight) + (end.Y * endWeight),
            (start.Z * startWeight) + (end.Z * endWeight),
            (start.W * startWeight) + (end.W * endWeight));

        return NormalizeOrFirst(result, start);
    }

    private static Quaternion Normalize(
        Quaternion value,
        int index,
        string parameterName)
    {
        if (!float.IsFinite(value.X) ||
            !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z) ||
            !float.IsFinite(value.W))
        {
            throw new ArgumentException(
                $"The rotation keyframe at index {index} must contain only finite components.",
                parameterName);
        }

        double lengthSquared =
            ((double)value.X * value.X) +
            ((double)value.Y * value.Y) +
            ((double)value.Z * value.Z) +
            ((double)value.W * value.W);

        if (!double.IsFinite(lengthSquared) || lengthSquared <= MinimumLengthSquared)
        {
            throw new ArgumentException(
                $"The rotation keyframe at index {index} must have nonzero finite length.",
                parameterName);
        }

        double inverseLength = 1.0 / Math.Sqrt(lengthSquared);

        return new Quaternion(
            (float)(value.X * inverseLength),
            (float)(value.Y * inverseLength),
            (float)(value.Z * inverseLength),
            (float)(value.W * inverseLength));
    }

    private static Quaternion NormalizeOrFirst(Quaternion value, Quaternion fallback)
    {
        double lengthSquared =
            ((double)value.X * value.X) +
            ((double)value.Y * value.Y) +
            ((double)value.Z * value.Z) +
            ((double)value.W * value.W);

        if (!double.IsFinite(lengthSquared) || lengthSquared <= MinimumLengthSquared)
        {
            return fallback;
        }

        double inverseLength = 1.0 / Math.Sqrt(lengthSquared);

        return new Quaternion(
            (float)(value.X * inverseLength),
            (float)(value.Y * inverseLength),
            (float)(value.Z * inverseLength),
            (float)(value.W * inverseLength));
    }

    private static void ValidateTime(float time, int index, string parameterName)
    {
        if (!float.IsFinite(time) || time < 0.0f || time > MaxTimeSeconds)
        {
            throw new ArgumentException(
                $"The keyframe time at index {index} must be finite, nonnegative, " +
                $"and no greater than {MaxTimeSeconds} seconds.",
                parameterName);
        }
    }
}
