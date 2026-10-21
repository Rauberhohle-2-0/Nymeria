using System.Numerics;
using Nymeria.Core.Models;

namespace Nymeria.Core.Culling;

/// <summary>
/// Six normalized clip planes derived from a view-projection matrix.
/// </summary>
/// <remarks>
/// <para>
/// The planes use Nymeria's rendering conventions: right-handed world space,
/// row-vector composition as <c>model * view * projection</c>, and a clip-space
/// depth range of <c>[0, 1]</c> as required by SDL3 GPU and Metal. The matrix is
/// therefore the same one supplied to the vertex shader; no depth remap or
/// transpose is involved.
/// </para>
/// <para>
/// <see cref="Intersects(BoundingBox)"/> is conservative. It reports
/// <see langword="false"/> only when every corner of the box is on the outside of
/// the same plane, so a box that intersects the frustum, touches a plane, or
/// merely cannot be proven outside is visible. Whole-instance culling therefore
/// never hides geometry that might rasterize; the cost is that some boxes that
/// are outside near a frustum edge are still submitted.
/// </para>
/// <para>
/// <see cref="Create(Matrix4x4)"/> fails open. A matrix containing non-finite
/// values, a singular or non-finite determinant, or a degenerate plane produces
/// an unbounded frustum whose <see cref="Intersects(BoundingBox)"/> always
/// returns <see langword="true"/>. Invalid bounds or a non-finite plane distance
/// are handled the same way. This sacrifices culling for that frame instead of
/// risking the loss of visible geometry.
/// </para>
/// <para>
/// <see cref="Update(Matrix4x4)"/> exists for a renderer that owns one scratch
/// frustum and refreshes it each frame. It does not allocate; <see cref="Create"/>
/// remains the independent-instance factory. A frustum must not be updated while
/// another thread is testing it.
/// </para>
/// </remarks>
public sealed class Frustum
{
    private const int PlaneCount = 6;

    private readonly Plane[] _planes = new Plane[PlaneCount];
    private bool _isUnbounded;

    /// <summary>
    /// Creates an unbounded frustum that can be refreshed with
    /// <see cref="Update(Matrix4x4)"/>.
    /// </summary>
    public Frustum()
    {
        _isUnbounded = true;
    }

    /// <summary>
    /// Gets whether this frustum accepts every bounding box because its source
    /// matrix or one of its planes was degenerate, or because it has not been
    /// updated yet.
    /// </summary>
    public bool IsUnbounded => _isUnbounded;

    /// <summary>
    /// Derives the six clip planes from a view-projection matrix.
    /// </summary>
    /// <param name="viewProjection">
    /// The combined view-projection matrix, composed as
    /// <c>view * projection</c> for <see cref="System.Numerics"/> row vectors.
    /// </param>
    /// <returns>
    /// A frustum, or an unbounded frustum when the matrix cannot produce six
    /// usable planes.
    /// </returns>
    public static Frustum Create(Matrix4x4 viewProjection)
    {
        var frustum = new Frustum();
        frustum.Update(viewProjection);
        return frustum;
    }

    /// <summary>
    /// Recomputes this frustum from a view-projection matrix without allocating.
    /// </summary>
    /// <param name="viewProjection">
    /// The combined view-projection matrix, composed as
    /// <c>view * projection</c> for <see cref="System.Numerics"/> row vectors.
    /// </param>
    /// <remarks>
    /// Use this on a frustum owned by one renderer or scene traversal. A failed
    /// update leaves the frustum unbounded, which keeps every instance visible.
    /// Do not update a frustum while another thread is calling
    /// <see cref="Intersects(BoundingBox)"/>.
    /// </remarks>
    public void Update(Matrix4x4 viewProjection)
    {
        _isUnbounded = true;

        if (!IsFinite(viewProjection))
        {
            return;
        }

        float determinant = viewProjection.GetDeterminant();

        if (!float.IsFinite(determinant) || determinant == 0.0f)
        {
            return;
        }

        // Clip-space inequalities for a [0, 1] depth range:
        //   -w <= x <= w, -w <= y <= w, 0 <= z <= w
        // Each inequality becomes a row/column combination of the matrix. For
        // row-vector composition, clip x is the first column dotted with
        // (x, y, z, 1), which is why the row indices below are the coefficients.
        if (!TryCreatePlane(
                viewProjection.M11 + viewProjection.M14,
                viewProjection.M21 + viewProjection.M24,
                viewProjection.M31 + viewProjection.M34,
                viewProjection.M41 + viewProjection.M44,
                out _planes[0]) ||
            !TryCreatePlane(
                viewProjection.M14 - viewProjection.M11,
                viewProjection.M24 - viewProjection.M21,
                viewProjection.M34 - viewProjection.M31,
                viewProjection.M44 - viewProjection.M41,
                out _planes[1]) ||
            !TryCreatePlane(
                viewProjection.M12 + viewProjection.M14,
                viewProjection.M22 + viewProjection.M24,
                viewProjection.M32 + viewProjection.M34,
                viewProjection.M42 + viewProjection.M44,
                out _planes[2]) ||
            !TryCreatePlane(
                viewProjection.M14 - viewProjection.M12,
                viewProjection.M24 - viewProjection.M22,
                viewProjection.M34 - viewProjection.M32,
                viewProjection.M44 - viewProjection.M42,
                out _planes[3]) ||
            !TryCreatePlane(
                viewProjection.M13,
                viewProjection.M23,
                viewProjection.M33,
                viewProjection.M43,
                out _planes[4]) ||
            !TryCreatePlane(
                viewProjection.M14 - viewProjection.M13,
                viewProjection.M24 - viewProjection.M23,
                viewProjection.M34 - viewProjection.M33,
                viewProjection.M44 - viewProjection.M43,
                out _planes[5]))
        {
            return;
        }

        _isUnbounded = false;
    }

    /// <summary>
    /// Tests whether a box can intersect the frustum.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> only when the box is fully outside one plane.
    /// </returns>
    public bool Intersects(BoundingBox bounds)
    {
        if (IsUnbounded)
        {
            return true;
        }

        if (!IsValid(bounds))
        {
            return true;
        }

        foreach (Plane plane in _planes)
        {
            // The corner farthest along the plane normal is the box's positive
            // vertex. If even that corner is outside, the whole box is outside.
            double x = plane.Normal.X >= 0.0f ? bounds.Max.X : bounds.Min.X;
            double y = plane.Normal.Y >= 0.0f ? bounds.Max.Y : bounds.Min.Y;
            double z = plane.Normal.Z >= 0.0f ? bounds.Max.Z : bounds.Min.Z;
            double distance =
                ((double)plane.Normal.X * x) +
                ((double)plane.Normal.Y * y) +
                ((double)plane.Normal.Z * z) +
                plane.D;

            if (!double.IsFinite(distance))
            {
                // Never turn an arithmetic problem into a cull decision.
                return true;
            }

            if (distance < 0.0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryCreatePlane(
        double x,
        double y,
        double z,
        double d,
        out Plane plane)
    {
        double lengthSquared = (x * x) + (y * y) + (z * z);

        if (!double.IsFinite(lengthSquared) || lengthSquared <= 0.0)
        {
            plane = default;
            return false;
        }

        double inverseLength = 1.0 / Math.Sqrt(lengthSquared);

        var normal = new Vector3(
            (float)(x * inverseLength),
            (float)(y * inverseLength),
            (float)(z * inverseLength));

        float distance = (float)(d * inverseLength);

        if (!float.IsFinite(normal.X) ||
            !float.IsFinite(normal.Y) ||
            !float.IsFinite(normal.Z) ||
            !float.IsFinite(distance))
        {
            plane = default;
            return false;
        }

        plane = new Plane(normal, distance);
        return true;
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

    private static bool IsFinite(Matrix4x4 value)
    {
        return
            float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
            float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
            float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
            float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
            float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
            float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
            float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
            float.IsFinite(value.M43) && float.IsFinite(value.M44);
    }
}
