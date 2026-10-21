using System.Numerics;
using Nymeria.Core.Models;

namespace Nymeria.Core.Collision;

/// <summary>
/// CPU-only narrow-phase overlap tests for Nymeria's collision primitives.
/// </summary>
/// <remarks>
/// <para>
/// The tests reuse <see cref="BoundingBox"/> and <see cref="BoundingSphere"/>
/// directly; there is no second box or sphere representation. All tests are
/// inclusive at contact: boxes that share a face, edge, or corner overlap, and
/// spheres whose surfaces touch overlap. Containment and zero-size shapes are
/// ordinary cases. A zero-radius sphere is a point and can overlap only when it
/// lies inside or exactly on the other shape.
/// </para>
/// <para>
/// Comparisons use double precision. The input types are validated at
/// construction, so these methods do not throw for geometry and do not allocate.
/// A default <see cref="CollisionShape"/> has no primitive and never overlaps.
/// </para>
/// </remarks>
public static class CollisionIntersections
{
    /// <summary>
    /// Tests two axis-aligned boxes for overlap.
    /// </summary>
    /// <remarks>
    /// Separation is strict on every axis, so touching faces, edges, and corners
    /// count as overlapping. A flat or point box is valid.
    /// </remarks>
    public static bool Overlaps(BoundingBox first, BoundingBox second)
    {
        return
            (double)first.Min.X <= second.Max.X &&
            (double)first.Max.X >= second.Min.X &&
            (double)first.Min.Y <= second.Max.Y &&
            (double)first.Max.Y >= second.Min.Y &&
            (double)first.Min.Z <= second.Max.Z &&
            (double)first.Max.Z >= second.Min.Z;
    }

    /// <summary>
    /// Tests two spheres for overlap.
    /// </summary>
    /// <remarks>
    /// Surface contact counts as overlap. A zero-radius sphere is a point.
    /// </remarks>
    public static bool Overlaps(BoundingSphere first, BoundingSphere second)
    {
        double deltaX = (double)first.Center.X - second.Center.X;
        double deltaY = (double)first.Center.Y - second.Center.Y;
        double deltaZ = (double)first.Center.Z - second.Center.Z;
        double radiusSum = (double)first.Radius + second.Radius;

        return
            (deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ) <=
            radiusSum * radiusSum;
    }

    /// <summary>
    /// Tests a box and a sphere for overlap.
    /// </summary>
    /// <remarks>
    /// The closest point on the box to the sphere center is compared with the
    /// radius. Surface contact and containment count as overlap.
    /// </remarks>
    public static bool Overlaps(BoundingBox box, BoundingSphere sphere)
    {
        double closestX = Math.Clamp(sphere.Center.X, box.Min.X, box.Max.X);
        double closestY = Math.Clamp(sphere.Center.Y, box.Min.Y, box.Max.Y);
        double closestZ = Math.Clamp(sphere.Center.Z, box.Min.Z, box.Max.Z);
        double deltaX = (double)sphere.Center.X - closestX;
        double deltaY = (double)sphere.Center.Y - closestY;
        double deltaZ = (double)sphere.Center.Z - closestZ;

        return
            (deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ) <=
            (double)sphere.Radius * sphere.Radius;
    }

    /// <summary>
    /// Tests a sphere and a box for overlap.
    /// </summary>
    public static bool Overlaps(BoundingSphere sphere, BoundingBox box)
    {
        return Overlaps(box, sphere);
    }

    /// <summary>
    /// Tests two collision shapes for overlap, dispatching on their primitive
    /// kinds.
    /// </summary>
    /// <returns>
    /// False when either shape has no primitive or the two primitives do not
    /// overlap.
    /// </returns>
    public static bool Overlaps(CollisionShape first, CollisionShape second)
    {
        if (first.Kind == CollisionShapeKind.None ||
            second.Kind == CollisionShapeKind.None)
        {
            return false;
        }

        if (first.Kind == CollisionShapeKind.Box)
        {
            first.TryGetBox(out BoundingBox firstBox);

            if (second.Kind == CollisionShapeKind.Box)
            {
                second.TryGetBox(out BoundingBox secondBox);
                return Overlaps(firstBox, secondBox);
            }

            second.TryGetSphere(out BoundingSphere secondSphere);
            return Overlaps(firstBox, secondSphere);
        }

        first.TryGetSphere(out BoundingSphere firstSphere);

        if (second.Kind == CollisionShapeKind.Box)
        {
            second.TryGetBox(out BoundingBox secondBox);
            return Overlaps(firstSphere, secondBox);
        }

        second.TryGetSphere(out BoundingSphere otherSphere);
        return Overlaps(firstSphere, otherSphere);
    }
}
