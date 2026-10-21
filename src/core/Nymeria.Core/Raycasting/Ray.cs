using System.Numerics;

namespace Nymeria.Core.Raycasting;

/// <summary>
/// A world-space ray with a finite origin and a normalized direction.
/// </summary>
/// <remarks>
/// <para>
/// The constructor normalizes <paramref name="direction"/>, so every distance
/// and <see cref="GetPoint"/> argument is measured in world units along the
/// ray. A direction may be supplied at any non-zero finite length; it does not
/// have to be normalized by the caller.
/// </para>
/// <para>
/// World space is right-handed with +Y up and -Z forward, matching the rest of
/// Nymeria. A ray has no length limit of its own; query methods take a maximum
/// distance. Origins inside geometry and negative direction components are
/// valid. NaN, infinities, zero-length directions, and directions that cannot be
/// normalized to a finite unit vector are rejected.
/// </para>
/// <para>
/// This is a CPU-only value type. It contains no SDL3, GPU, window, or
/// format dependency.
/// </para>
/// </remarks>
public readonly struct Ray
{
    private readonly Vector3 _origin;
    private readonly Vector3 _direction;

    /// <summary>
    /// Creates a ray and normalizes its direction.
    /// </summary>
    /// <param name="origin">The finite world-space starting point.</param>
    /// <param name="direction">
    /// A non-zero finite world-space direction. It does not have to be unit
    /// length.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The origin or direction is non-finite, the direction is zero length, or
    /// its normalized value is not finite.
    /// </exception>
    public Ray(Vector3 origin, Vector3 direction)
    {
        if (!TryCreate(origin, direction, out Ray ray))
        {
            throw new ArgumentException(
                "A ray requires a finite origin and a finite, non-zero direction " +
                "that normalizes to a finite unit vector.",
                nameof(direction));
        }

        _origin = ray._origin;
        _direction = ray._direction;
    }

    // Trusted constructor used only after TryCreate has validated and
    // normalized the direction. The unused flag distinguishes it from the
    // public, validating constructor without allocating or re-normalizing.
    private Ray(Vector3 origin, Vector3 normalizedDirection, bool _)
    {
        _origin = origin;
        _direction = normalizedDirection;
    }

    /// <summary>
    /// Gets the finite world-space starting point.
    /// </summary>
    public Vector3 Origin => _origin;

    /// <summary>
    /// Gets the normalized world-space direction.
    /// </summary>
    public Vector3 Direction => _direction;

    /// <summary>
    /// Tries to create a ray without throwing.
    /// </summary>
    /// <param name="origin">The finite world-space starting point.</param>
    /// <param name="direction">A non-zero finite world-space direction.</param>
    /// <param name="ray">
    /// The created ray when this method returns true, or the default value.
    /// </param>
    /// <returns>True when the origin and direction describe a valid ray.</returns>
    public static bool TryCreate(
        Vector3 origin,
        Vector3 direction,
        out Ray ray)
    {
        ray = default;

        if (!IsFinite(origin) ||
            !float.IsFinite(direction.X) ||
            !float.IsFinite(direction.Y) ||
            !float.IsFinite(direction.Z))
        {
            return false;
        }

        // Double precision keeps a very large finite direction from overflowing
        // while its length is computed.
        double lengthSquared =
            ((double)direction.X * direction.X) +
            ((double)direction.Y * direction.Y) +
            ((double)direction.Z * direction.Z);

        if (!double.IsFinite(lengthSquared) || lengthSquared <= 0.0)
        {
            return false;
        }

        double inverseLength = 1.0 / Math.Sqrt(lengthSquared);
        var normalized = new Vector3(
            (float)(direction.X * inverseLength),
            (float)(direction.Y * inverseLength),
            (float)(direction.Z * inverseLength));

        if (!IsFinite(normalized))
        {
            return false;
        }

        ray = new Ray(origin, normalized, true);
        return true;
    }

    /// <summary>
    /// Gets the point at a distance from the origin along the normalized
    /// direction.
    /// </summary>
    /// <param name="distance">
    /// A distance in world units. Negative values return a point behind the
    /// origin; query methods only report nonnegative distances.
    /// </param>
    public Vector3 GetPoint(float distance)
    {
        return _origin + (_direction * distance);
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) &&
            float.IsFinite(value.Y) &&
            float.IsFinite(value.Z);
    }
}
