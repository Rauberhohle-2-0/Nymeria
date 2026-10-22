using System.Numerics;

namespace Nymeria.Core.Audio;

/// <summary>
/// Headless distance-attenuation, stereo-panning, and positional stereo
/// strength math for basic spatial audio.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately basic spatialization: a linear distance rolloff and a
/// left/right equal-power pan derived from the listener basis. It is not HRTF or
/// binaural rendering, does not model occlusion or environment, and does not
/// produce surround channels. See <c>docs/AUDIO.md</c>.
/// </para>
/// <para>
/// Configuration errors (an invalid minimum or maximum distance) throw. Unsafe
/// runtime inputs such as a non-finite position or distance fail to silence or a
/// centered pan instead, so a bad frame cannot crash the audio thread.
/// </para>
/// </remarks>
public static class AudioMath
{
    /// <summary>
    /// Computes linear distance attenuation.
    /// </summary>
    /// <remarks>
    /// The result is <c>1</c> at or inside <paramref name="minDistance"/>, falls
    /// linearly to <c>0</c> at <paramref name="maxDistance"/>, and stays <c>0</c>
    /// beyond it. A negative distance is treated as coincident. A non-finite
    /// distance yields <c>0</c>.
    /// </remarks>
    /// <param name="distance">The listener-to-source distance in world units.</param>
    /// <param name="minDistance">The full-volume radius; zero or greater.</param>
    /// <param name="maxDistance">The silence radius; greater than the minimum.</param>
    /// <returns>The attenuation in <c>[0, 1]</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The distances are non-finite, the minimum is negative, or the maximum is
    /// not greater than the minimum.
    /// </exception>
    public static float DistanceAttenuation(
        float distance,
        float minDistance,
        float maxDistance)
    {
        ValidateDistanceRange(minDistance, maxDistance);

        if (!float.IsFinite(distance) || distance >= maxDistance)
        {
            return 0.0f;
        }

        if (distance <= minDistance)
        {
            return 1.0f;
        }

        return (maxDistance - distance) / (maxDistance - minDistance);
    }

    /// <summary>
    /// Computes linear distance attenuation between two world-space positions.
    /// </summary>
    /// <remarks>
    /// Coincident positions have zero distance and therefore full volume. A
    /// non-finite position fails to silence.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The distances are non-finite, the minimum is negative, or the maximum is
    /// not greater than the minimum.
    /// </exception>
    public static float DistanceAttenuation(
        Vector3 listenerPosition,
        Vector3 sourcePosition,
        float minDistance,
        float maxDistance)
    {
        ValidateDistanceRange(minDistance, maxDistance);

        if (!IsFinite(listenerPosition) || !IsFinite(sourcePosition))
        {
            return 0.0f;
        }

        float distance = Vector3.Distance(listenerPosition, sourcePosition);

        return DistanceAttenuation(distance, minDistance, maxDistance);
    }

    /// <summary>
    /// Computes a left/right pan from listener and source positions.
    /// </summary>
    /// <remarks>
    /// The result is the normalized horizontal component of the
    /// listener-to-source direction along the listener's right axis, clamped to
    /// <c>[-1, 1]</c>. A source on the right is positive. A coincident source, a
    /// degenerate right axis, or a non-finite input yields a centered pan of
    /// <c>0</c>.
    /// </remarks>
    /// <param name="listenerPosition">The listener position in world space.</param>
    /// <param name="listenerRight">
    /// The listener's normalized right axis, normally
    /// <see cref="AudioListener.Right"/>.
    /// </param>
    /// <param name="sourcePosition">The source position in world space.</param>
    /// <returns>The pan in <c>[-1, 1]</c>, where <c>-1</c> is fully left.</returns>
    public static float StereoPan(
        Vector3 listenerPosition,
        Vector3 listenerRight,
        Vector3 sourcePosition)
    {
        if (!IsFinite(listenerPosition) ||
            !IsFinite(listenerRight) ||
            !IsFinite(sourcePosition))
        {
            return 0.0f;
        }

        float rightLengthSquared = listenerRight.LengthSquared();

        if (!float.IsFinite(rightLengthSquared) || rightLengthSquared <= 1e-12f)
        {
            return 0.0f;
        }

        Vector3 toSource = sourcePosition - listenerPosition;
        float distanceSquared = toSource.LengthSquared();

        if (!float.IsFinite(distanceSquared) || distanceSquared <= 1e-12f)
        {
            return 0.0f;
        }

        float projection =
            Vector3.Dot(toSource, listenerRight) /
            (MathF.Sqrt(distanceSquared) * MathF.Sqrt(rightLengthSquared));

        return Math.Clamp(projection, -1.0f, 1.0f);
    }

    /// <summary>
    /// Converts a pan value to equal-power left and right gains, scaled by a
    /// positional stereo effect strength.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A strength of <c>1</c> produces exactly
    /// <see cref="EqualPowerGains(float)"/> for <paramref name="pan"/>: the full
    /// positional separation. A strength of <c>0</c> produces the centered gains
    /// for either channel, so a positional source has no left/right separation.
    /// Intermediate values scale the pan toward the center, which keeps the
    /// equal-power law and therefore constant power at every strength.
    /// </para>
    /// <para>
    /// This is a positional-stereo control only. It does not alter a stereo
    /// clip's channel content, distance attenuation, source gain, or listener
    /// orientation. A non-finite pan is centered; a non-finite strength yields
    /// the centered gains. Out-of-range values are clamped.
    /// </para>
    /// </remarks>
    /// <param name="pan">The pan in <c>[-1, 1]</c>, where <c>-1</c> is fully left.</param>
    /// <param name="stereoEffectStrength">
    /// The positional stereo effect strength in <c>[0, 1]</c>, where <c>1</c>
    /// preserves the full pan.
    /// </param>
    /// <returns>The left and right gains in <c>[0, 1]</c>.</returns>
    public static (float Left, float Right) SpatialGains(
        float pan,
        float stereoEffectStrength)
    {
        if (!float.IsFinite(stereoEffectStrength))
        {
            return EqualPowerGains(0.0f);
        }

        float strength = Math.Clamp(stereoEffectStrength, 0.0f, 1.0f);

        return EqualPowerGains(pan * strength);
    }

    /// <summary>
    /// Converts a pan value to equal-power left and right gains.
    /// </summary>
    /// <remarks>
    /// <c>-1</c> is fully left, <c>0</c> is centered, and <c>1</c> is fully
    /// right. A centered source uses <c>1/√2</c> on both channels, so it is not
    /// louder than a fully panned source. Non-finite values are treated as
    /// centered and out-of-range values are clamped.
    /// </remarks>
    public static (float Left, float Right) EqualPowerGains(float pan)
    {
        if (!float.IsFinite(pan))
        {
            pan = 0.0f;
        }

        float angle = (Math.Clamp(pan, -1.0f, 1.0f) + 1.0f) * (MathF.PI / 4.0f);

        return (MathF.Cos(angle), MathF.Sin(angle));
    }

    private static void ValidateDistanceRange(float minDistance, float maxDistance)
    {
        if (!float.IsFinite(minDistance) || minDistance < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minDistance),
                minDistance,
                "The minimum distance must be finite and nonnegative.");
        }

        if (!float.IsFinite(maxDistance) || maxDistance <= minDistance)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDistance),
                maxDistance,
                "The maximum distance must be finite and greater than the minimum distance.");
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);
}
