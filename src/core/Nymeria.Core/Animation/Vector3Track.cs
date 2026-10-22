using System.Collections.ObjectModel;
using System.Numerics;

namespace Nymeria.Core.Animation;

/// <summary>
/// An immutable, time-ordered track of three-component keyframes with linear
/// interpolation.
/// </summary>
/// <remarks>
/// <para>
/// The track owns a copy of its keyframes. Times must be finite, nonnegative,
/// strictly increasing, and no greater than <see cref="MaxTimeSeconds"/>; values
/// must contain only finite components. Duplicate timestamps are rejected rather
/// than resolved implicitly.
/// </para>
/// <para>
/// Evaluation clamps before the first keyframe and after the last one. The
/// result is deterministic and independent of frame rate. The track has no
/// SDL3, GPU, window, or scene dependency, and it is safe for concurrent reads
/// once constructed.
/// </para>
/// </remarks>
public sealed class Vector3Track
{
    /// <summary>The largest accepted number of keyframes on one track.</summary>
    public const int MaxKeyframeCount = 100_000;

    /// <summary>The largest accepted keyframe time, in seconds (24 hours).</summary>
    public const float MaxTimeSeconds = 86_400.0f;

    private readonly Vector3Keyframe[] _keyframes;
    private readonly ReadOnlyCollection<Vector3Keyframe> _readOnlyKeyframes;

    /// <summary>
    /// Creates a track from time-ordered keyframes.
    /// </summary>
    /// <param name="keyframes">
    /// At least one keyframe. The sequence is copied and enumerated once.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="keyframes"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The sequence is empty, exceeds <see cref="MaxKeyframeCount"/>, contains a
    /// non-finite or negative time, exceeds <see cref="MaxTimeSeconds"/>, is not
    /// strictly increasing, or contains a non-finite value.
    /// </exception>
    public Vector3Track(IEnumerable<Vector3Keyframe> keyframes)
    {
        ArgumentNullException.ThrowIfNull(keyframes);

        var validated = new List<Vector3Keyframe>();
        float previousTime = 0.0f;

        foreach (Vector3Keyframe keyframe in keyframes)
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

            ValidateValue(keyframe.Value, index, nameof(keyframes));
            previousTime = keyframe.Time;
            validated.Add(keyframe);
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

    /// <summary>Gets the keyframes in time order.</summary>
    public IReadOnlyList<Vector3Keyframe> Keyframes => _readOnlyKeyframes;

    /// <summary>
    /// Evaluates the track at the supplied time.
    /// </summary>
    /// <remarks>
    /// A time at or before the first keyframe returns the first value; a time at
    /// or after the last keyframe returns the last value. Between keyframes the
    /// result is a linear interpolation.
    /// </remarks>
    /// <param name="time">The evaluation time in seconds; finite and nonnegative.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="time"/> is negative or not finite.
    /// </exception>
    public Vector3 Evaluate(float time)
    {
        if (!float.IsFinite(time) || time < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(time),
                time,
                "The evaluation time must be finite and nonnegative.");
        }

        Vector3Keyframe first = _keyframes[0];

        if (time <= first.Time)
        {
            return first.Value;
        }

        Vector3Keyframe last = _keyframes[^1];

        if (time >= last.Time)
        {
            return last.Value;
        }

        int index = FindSegment(time);
        Vector3Keyframe start = _keyframes[index];
        Vector3Keyframe end = _keyframes[index + 1];
        float fraction = (time - start.Time) / (end.Time - start.Time);

        return Vector3.Lerp(start.Value, end.Value, fraction);
    }

    /// <summary>Gets the last keyframe time, which is the track's natural length.</summary>
    public float EndTime => _keyframes[^1].Time;

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

    private static void ValidateValue(Vector3 value, int index, string parameterName)
    {
        if (!float.IsFinite(value.X) ||
            !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z))
        {
            throw new ArgumentException(
                $"The keyframe value at index {index} must contain only finite components.",
                parameterName);
        }
    }
}
