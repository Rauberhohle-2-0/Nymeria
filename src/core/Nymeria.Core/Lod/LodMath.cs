using System.Numerics;
using Nymeria.Core.Models;

namespace Nymeria.Core.Lod;

/// <summary>
/// Distance helpers used by level-of-detail selection and distance culling.
/// </summary>
public static class LodMath
{
    /// <summary>
    /// Computes the distance from a point to the closest point of an
    /// axis-aligned bounding box.
    /// </summary>
    /// <remarks>
    /// A point inside the box is at distance zero. The arithmetic uses double
    /// precision, and an extreme finite result saturates to
    /// <see cref="float.MaxValue"/> rather than becoming infinity. A non-finite
    /// point or invalid box fails safe to zero, which selects the highest-detail
    /// level and never causes a distance cull.
    /// </remarks>
    public static float DistanceToBounds(Vector3 point, BoundingBox bounds)
    {
        if (!IsFinite(point) || !IsValid(bounds))
        {
            return 0.0f;
        }

        double deltaX = Math.Max(
            Math.Max((double)bounds.Min.X - point.X, (double)point.X - bounds.Max.X),
            0.0);
        double deltaY = Math.Max(
            Math.Max((double)bounds.Min.Y - point.Y, (double)point.Y - bounds.Max.Y),
            0.0);
        double deltaZ = Math.Max(
            Math.Max((double)bounds.Min.Z - point.Z, (double)point.Z - bounds.Max.Z),
            0.0);
        double distance = Math.Sqrt(
            (deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ));

        if (!double.IsFinite(distance))
        {
            return float.MaxValue;
        }

        return (float)Math.Min(distance, float.MaxValue);
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) &&
            float.IsFinite(value.Y) &&
            float.IsFinite(value.Z);
    }

    private static bool IsValid(BoundingBox bounds)
    {
        Vector3 min = bounds.Min;
        Vector3 max = bounds.Max;

        return
            float.IsFinite(min.X) && float.IsFinite(min.Y) && float.IsFinite(min.Z) &&
            float.IsFinite(max.X) && float.IsFinite(max.Y) && float.IsFinite(max.Z) &&
            min.X <= max.X && min.Y <= max.Y && min.Z <= max.Z;
    }
}
