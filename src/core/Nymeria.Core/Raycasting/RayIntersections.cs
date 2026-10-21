using System.Numerics;
using Nymeria.Core.Models;

namespace Nymeria.Core.Raycasting;

/// <summary>
/// CPU-only ray intersection helpers for Nymeria's existing bounding volumes.
/// </summary>
/// <remarks>
/// <para>
/// The only bounding volume in Nymeria is the axis-aligned
/// <see cref="BoundingBox"/> used for culling and picking. These tests reuse it
/// rather than introducing a second geometry representation.
/// </para>
/// <para>
/// All comparisons use double precision. Boundary behavior is inclusive: a ray
/// that only grazes an edge, corner, or face counts as a hit, and a hit exactly
/// at the maximum distance is accepted. A direction component of exactly zero
/// is treated as parallel to that pair of planes, with no epsilon. Negative
/// direction components are ordinary input. Origins inside the box produce a
/// zero-distance hit.
/// </para>
/// </remarks>
public static class RayIntersections
{
    /// <summary>
    /// Tests a ray against an axis-aligned bounding box and reports the nearest
    /// entry point.
    /// </summary>
    /// <param name="ray">The world-space ray.</param>
    /// <param name="bounds">The box in the same space as the ray.</param>
    /// <param name="maxDistance">
    /// The inclusive maximum distance in world units. Positive infinity is
    /// allowed. A negative or NaN value is treated as a safe miss.
    /// </param>
    /// <param name="distance">
    /// The nonnegative distance from the ray origin to the entry point, or zero
    /// when the origin is inside the box.
    /// </param>
    /// <param name="normal">
    /// The normalized outward normal of the entry face. When the origin is inside
    /// the box, this is opposite the ray direction.
    /// </param>
    /// <returns>True when the ray intersects the box within the limit.</returns>
    /// <remarks>
    /// A non-finite or inverted box is treated as a miss. The method does not
    /// allocate and does not throw for invalid geometry, so an invalid query
    /// cannot produce a hit with a NaN point or distance.
    /// </remarks>
    public static bool TryIntersectBox(
        Ray ray,
        BoundingBox bounds,
        float maxDistance,
        out float distance,
        out Vector3 normal)
    {
        distance = 0.0f;
        normal = Vector3.Zero;

        if (float.IsNaN(maxDistance) || maxDistance < 0.0f)
        {
            return false;
        }

        Vector3 min = bounds.Min;
        Vector3 max = bounds.Max;
        Vector3 origin = ray.Origin;
        Vector3 direction = ray.Direction;

        if (!IsFinite(min) ||
            !IsFinite(max) ||
            min.X > max.X ||
            min.Y > max.Y ||
            min.Z > max.Z ||
            !IsFinite(origin) ||
            !IsFinite(direction))
        {
            return false;
        }

        double near = double.NegativeInfinity;
        double far = double.PositiveInfinity;
        int nearAxis = -1;
        double nearNormalSign = 0.0;

        for (int axis = 0; axis < 3; axis++)
        {
            double axisOrigin = Component(origin, axis);
            double axisDirection = Component(direction, axis);
            double axisMin = Component(min, axis);
            double axisMax = Component(max, axis);

            if (axisDirection == 0.0)
            {
                // Parallel to this pair of planes: only a miss when the origin
                // is outside the slab. The comparison is inclusive, so an origin
                // exactly on a face is still inside.
                if (axisOrigin < axisMin || axisOrigin > axisMax)
                {
                    return false;
                }

                continue;
            }

            double first = (axisMin - axisOrigin) / axisDirection;
            double second = (axisMax - axisOrigin) / axisDirection;

            // The entry face follows from the direction alone. Deriving it this
            // way also works for a flat slab, where both intersections are equal
            // and cannot be ordered.
            double normalSign = axisDirection > 0.0 ? -1.0 : 1.0;

            if (first > second)
            {
                (first, second) = (second, first);
            }

            if (first > near)
            {
                near = first;
                nearAxis = axis;
                nearNormalSign = normalSign;
            }

            if (second < far)
            {
                far = second;
            }

            if (near > far)
            {
                return false;
            }
        }

        // The whole box is behind the origin.
        if (far < 0.0)
        {
            return false;
        }

        double hitDistance = near > 0.0 ? near : 0.0;

        // The reported distance must remain a finite float. A finite double
        // distance beyond float.MaxValue is unrepresentable, so it is a miss
        // rather than an infinity that RaycastHit would have to reject.
        if (!double.IsFinite(hitDistance) ||
            hitDistance > maxDistance ||
            hitDistance > float.MaxValue)
        {
            return false;
        }

        bool originInside =
            origin.X >= min.X && origin.X <= max.X &&
            origin.Y >= min.Y && origin.Y <= max.Y &&
            origin.Z >= min.Z && origin.Z <= max.Z;

        if (originInside)
        {
            normal = -direction;
        }
        else if (nearAxis >= 0)
        {
            normal = nearAxis switch
            {
                0 => new Vector3((float)nearNormalSign, 0.0f, 0.0f),
                1 => new Vector3(0.0f, (float)nearNormalSign, 0.0f),
                _ => new Vector3(0.0f, 0.0f, (float)nearNormalSign),
            };
        }
        else
        {
            // A valid box always constrains at least one axis unless the ray is
            // exactly parallel to every pair of planes, which would mean a zero
            // direction. Defend against that without inventing a NaN normal.
            return false;
        }

        distance = (float)hitDistance;
        return true;
    }

    private static double Component(Vector3 value, int axis)
    {
        return axis switch
        {
            0 => value.X,
            1 => value.Y,
            _ => value.Z,
        };
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) &&
            float.IsFinite(value.Y) &&
            float.IsFinite(value.Z);
    }
}
