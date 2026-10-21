using System.Numerics;

namespace Nymeria.Core.Raycasting;

/// <summary>
/// Builds a world-space ray from a viewport pixel position by unprojecting
/// through an existing view and projection matrix.
/// </summary>
/// <remarks>
/// <para>
/// The pixel convention matches SDL's window coordinates and the examples'
/// renderer: the origin is the top-left corner, +X points right, and +Y points
/// down. <c>(0, 0)</c> is the top-left pixel and <c>(width, height)</c> is the
/// bottom-right corner of the viewport. Positions outside the viewport are not
/// clamped; they produce rays outside the view frustum.
/// </para>
/// <para>
/// The view and projection must follow the project's conventions: right-handed
/// world and eye spaces, +Y up, -Z forward, row vectors composed as
/// <c>view * projection</c>, and clip-space depth in <c>[0, 1]</c>. These are the
/// matrices produced by <c>Nymeria.Graphics.Camera.ViewMatrix</c> and
/// <c>Camera.CreateProjectionMatrix</c>. A perspective projection is assumed;
/// the ray origin is the view-space origin (the camera position) and distance is
/// measured from there. No Y flip or depth remap is applied.
/// </para>
/// <para>
/// The helper works in whatever pixel space the caller supplies, as long as the
/// position and dimensions use the same units. The renderer's projection uses
/// the drawable size, so an application that reads mouse coordinates in logical
/// window units must scale them by drawable size divided by window size before
/// calling this method. This conversion stays in the application layer; the
/// helper performs no SDL query.
/// </para>
/// <para>
/// Zero or negative viewport dimensions, non-finite pixels or matrices, and
/// non-invertible matrices return false rather than producing a NaN ray.
/// </para>
/// </remarks>
public static class ViewportRay
{
    /// <summary>
    /// Tries to create a world-space ray through a viewport pixel.
    /// </summary>
    /// <param name="viewportPosition">
    /// The pixel position in viewport coordinates, with a top-left origin and +Y
    /// down.
    /// </param>
    /// <param name="viewportWidth">The viewport width in pixels. Must be positive.</param>
    /// <param name="viewportHeight">The viewport height in pixels. Must be positive.</param>
    /// <param name="view">The right-handed view matrix.</param>
    /// <param name="projection">The perspective projection matrix.</param>
    /// <param name="ray">The created world-space ray, or the default value.</param>
    /// <returns>True when the inputs describe a usable ray.</returns>
    public static bool TryCreate(
        Vector2 viewportPosition,
        float viewportWidth,
        float viewportHeight,
        Matrix4x4 view,
        Matrix4x4 projection,
        out Ray ray)
    {
        ray = default;

        if (!IsFinite(viewportPosition) ||
            !float.IsFinite(viewportWidth) ||
            !float.IsFinite(viewportHeight) ||
            viewportWidth <= 0.0f ||
            viewportHeight <= 0.0f ||
            !IsFinite(view) ||
            !IsFinite(projection))
        {
            return false;
        }

        Matrix4x4 viewProjection = view * projection;

        if (!Matrix4x4.Invert(viewProjection, out Matrix4x4 inverseViewProjection) ||
            !IsFinite(inverseViewProjection) ||
            !Matrix4x4.Invert(view, out Matrix4x4 inverseView) ||
            !IsFinite(inverseView))
        {
            return false;
        }

        double normalizedX =
            (((double)viewportPosition.X / viewportWidth) * 2.0) - 1.0;
        double normalizedY =
            1.0 - (((double)viewportPosition.Y / viewportHeight) * 2.0);

        if (!TryUnproject(inverseViewProjection, normalizedX, normalizedY, 0.0, out Vector3 nearPoint) ||
            !TryUnproject(inverseViewProjection, normalizedX, normalizedY, 1.0, out Vector3 farPoint))
        {
            return false;
        }

        // For the project's perspective camera this is the camera position. The
        // direction comes from the near/far pair, so the origin does not depend
        // on the near-plane distance.
        Vector3 origin = Vector3.Transform(Vector3.Zero, inverseView);
        Vector3 direction = farPoint - nearPoint;

        if (!IsFinite(origin) || !IsFinite(direction))
        {
            return false;
        }

        return Ray.TryCreate(origin, direction, out ray);
    }

    /// <summary>
    /// Tries to create the center-of-viewport ray used by first-person picking.
    /// </summary>
    /// <param name="viewportWidth">The viewport width in pixels. Must be positive.</param>
    /// <param name="viewportHeight">The viewport height in pixels. Must be positive.</param>
    /// <param name="view">The right-handed view matrix.</param>
    /// <param name="projection">The perspective projection matrix.</param>
    /// <param name="ray">The created world-space ray, or the default value.</param>
    /// <returns>True when the inputs describe a usable ray.</returns>
    public static bool TryCreateCenter(
        float viewportWidth,
        float viewportHeight,
        Matrix4x4 view,
        Matrix4x4 projection,
        out Ray ray)
    {
        return TryCreate(
            new Vector2(viewportWidth * 0.5f, viewportHeight * 0.5f),
            viewportWidth,
            viewportHeight,
            view,
            projection,
            out ray);
    }

    private static bool TryUnproject(
        Matrix4x4 inverseViewProjection,
        double normalizedX,
        double normalizedY,
        double normalizedZ,
        out Vector3 point)
    {
        point = default;

        // Row-vector convention: clip = (ndcX, ndcY, ndcZ, 1) * inverse.
        double w =
            (normalizedX * inverseViewProjection.M14) +
            (normalizedY * inverseViewProjection.M24) +
            (normalizedZ * inverseViewProjection.M34) +
            inverseViewProjection.M44;

        if (!double.IsFinite(w) || Math.Abs(w) <= 1e-12)
        {
            return false;
        }

        double x =
            ((normalizedX * inverseViewProjection.M11) +
             (normalizedY * inverseViewProjection.M21) +
             (normalizedZ * inverseViewProjection.M31) +
             inverseViewProjection.M41) / w;
        double y =
            ((normalizedX * inverseViewProjection.M12) +
             (normalizedY * inverseViewProjection.M22) +
             (normalizedZ * inverseViewProjection.M32) +
             inverseViewProjection.M42) / w;
        double z =
            ((normalizedX * inverseViewProjection.M13) +
             (normalizedY * inverseViewProjection.M23) +
             (normalizedZ * inverseViewProjection.M33) +
             inverseViewProjection.M43) / w;

        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
        {
            return false;
        }

        point = new Vector3((float)x, (float)y, (float)z);
        return IsFinite(point);
    }

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) &&
            float.IsFinite(value.Y) &&
            float.IsFinite(value.Z);
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
