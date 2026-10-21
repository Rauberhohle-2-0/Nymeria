using System.Numerics;

namespace Nymeria.Core.Models;

/// <summary>
/// A validated sphere in model or world space.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Center"/> must be finite and <see cref="Radius"/> must be finite and
/// nonnegative. A zero-radius sphere is a valid point. The type is immutable and
/// uses only <see cref="System.Numerics"/>.
/// </para>
/// <para>
/// <see cref="TryTransform"/> produces a conservative world-space sphere. The
/// center is transformed as a point, and the radius is multiplied by the largest
/// magnitude among the three transformed basis rows. For the
/// scale-rotation-translation matrices produced by
/// <see cref="Scenes.Transform.CreateModelMatrix"/>, that value is the largest
/// absolute axis scale, so rotation never changes the radius and nonuniform scale
/// uses the largest stretch. This is intentionally conservative: it can report a
/// larger sphere than an exact transformed ellipsoid would require, but it never
/// under-reports the extent of the transformed sphere.
/// </para>
/// </remarks>
public readonly struct BoundingSphere
{
    /// <summary>
    /// Creates a validated sphere.
    /// </summary>
    /// <param name="center">The finite sphere center.</param>
    /// <param name="radius">The finite, nonnegative radius.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The center or radius is non-finite, or the radius is negative.
    /// </exception>
    public BoundingSphere(Vector3 center, float radius)
    {
        if (!IsFinite(center))
        {
            throw new ArgumentOutOfRangeException(
                nameof(center),
                center,
                "A bounding-sphere center must contain finite components.");
        }

        if (!float.IsFinite(radius) || radius < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                radius,
                "A bounding-sphere radius must be finite and nonnegative.");
        }

        Center = center;
        Radius = radius;
    }

    /// <summary>
    /// Gets the sphere center.
    /// </summary>
    public Vector3 Center { get; }

    /// <summary>
    /// Gets the nonnegative radius.
    /// </summary>
    public float Radius { get; }

    /// <summary>
    /// Computes a conservative world-space sphere for an affine transform.
    /// </summary>
    /// <param name="matrix">The model or world transform.</param>
    /// <param name="sphere">
    /// The transformed sphere, or the default value when the matrix is unusable.
    /// </param>
    /// <returns>
    /// True when the matrix is finite and the result can be represented as a
    /// finite sphere.
    /// </returns>
    /// <remarks>
    /// The method does not require an invertible matrix; collision overlap tests
    /// need only a supported affine transform. The matrix must be finite and
    /// affine, and its 3x3 basis rows must stay mutually orthogonal. That is the
    /// case for the scale-rotation-translation matrices produced by
    /// <see cref="Scenes.Transform.CreateModelMatrix"/>. A projective matrix or
    /// an arbitrary shear returns false rather than under-reporting the sphere's
    /// transformed extent. A transformed center or radius outside the finite
    /// <see cref="float"/> range is saturated to the finite extreme, which widens
    /// the sphere and keeps collision tests conservative instead of producing
    /// infinity.
    /// </remarks>
    public bool TryTransform(Matrix4x4 matrix, out BoundingSphere sphere)
    {
        sphere = default;

        if (!IsFinite(matrix) ||
            !IsAffine(matrix) ||
            !HasOrthogonalBasis(matrix))
        {
            return false;
        }

        double centerX =
            ((double)Center.X * matrix.M11) +
            ((double)Center.Y * matrix.M21) +
            ((double)Center.Z * matrix.M31) +
            matrix.M41;
        double centerY =
            ((double)Center.X * matrix.M12) +
            ((double)Center.Y * matrix.M22) +
            ((double)Center.Z * matrix.M32) +
            matrix.M42;
        double centerZ =
            ((double)Center.X * matrix.M13) +
            ((double)Center.Y * matrix.M23) +
            ((double)Center.Z * matrix.M33) +
            matrix.M43;

        if (!double.IsFinite(centerX) ||
            !double.IsFinite(centerY) ||
            !double.IsFinite(centerZ))
        {
            return false;
        }

        // The three rows of the 3x3 basis are the transformed local axes when
        // using row vectors. The longest row bounds how far the transformed
        // sphere can extend for a scale-rotation-translation transform.
        double scaleX =
            ((double)matrix.M11 * matrix.M11) +
            ((double)matrix.M12 * matrix.M12) +
            ((double)matrix.M13 * matrix.M13);
        double scaleY =
            ((double)matrix.M21 * matrix.M21) +
            ((double)matrix.M22 * matrix.M22) +
            ((double)matrix.M23 * matrix.M23);
        double scaleZ =
            ((double)matrix.M31 * matrix.M31) +
            ((double)matrix.M32 * matrix.M32) +
            ((double)matrix.M33 * matrix.M33);

        if (!double.IsFinite(scaleX) ||
            !double.IsFinite(scaleY) ||
            !double.IsFinite(scaleZ))
        {
            return false;
        }

        double maximumScale = Math.Sqrt(Math.Max(scaleX, Math.Max(scaleY, scaleZ)));
        double radius = (double)Radius * maximumScale;

        if (!double.IsFinite(radius) || !double.IsFinite(maximumScale))
        {
            return false;
        }

        sphere = new BoundingSphere(
            new Vector3(
                Saturate(centerX),
                Saturate(centerY),
                Saturate(centerZ)),
            (float)Math.Min(radius, float.MaxValue));
        return true;
    }

    private static float Saturate(double value)
    {
        return (float)Math.Clamp(value, -float.MaxValue, float.MaxValue);
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

    private static bool IsAffine(Matrix4x4 value)
    {
        return
            value.M14 == 0.0f &&
            value.M24 == 0.0f &&
            value.M34 == 0.0f &&
            value.M44 == 1.0f;
    }

    private static bool HasOrthogonalBasis(Matrix4x4 value)
    {
        double rowX =
            ((double)value.M11 * value.M11) +
            ((double)value.M12 * value.M12) +
            ((double)value.M13 * value.M13);
        double rowY =
            ((double)value.M21 * value.M21) +
            ((double)value.M22 * value.M22) +
            ((double)value.M23 * value.M23);
        double rowZ =
            ((double)value.M31 * value.M31) +
            ((double)value.M32 * value.M32) +
            ((double)value.M33 * value.M33);
        double dotXY =
            ((double)value.M11 * value.M21) +
            ((double)value.M12 * value.M22) +
            ((double)value.M13 * value.M23);
        double dotXZ =
            ((double)value.M11 * value.M31) +
            ((double)value.M12 * value.M32) +
            ((double)value.M13 * value.M33);
        double dotYZ =
            ((double)value.M21 * value.M31) +
            ((double)value.M22 * value.M32) +
            ((double)value.M23 * value.M33);

        // The 3x3 basis of scale * rotation is orthogonal. A shear would keep
        // the row lengths but change those dot products, so it is outside this
        // conservative radius rule. The tolerance covers float rotation noise.
        const double tolerance = 1e-5;

        return IsOrthogonal(dotXY, rowX, rowY, tolerance) &&
            IsOrthogonal(dotXZ, rowX, rowZ, tolerance) &&
            IsOrthogonal(dotYZ, rowY, rowZ, tolerance);
    }

    private static bool IsOrthogonal(
        double dot,
        double firstLengthSquared,
        double secondLengthSquared,
        double tolerance)
    {
        if (firstLengthSquared == 0.0 || secondLengthSquared == 0.0)
        {
            return dot == 0.0;
        }

        double scale = Math.Sqrt(firstLengthSquared * secondLengthSquared);
        return Math.Abs(dot) <= tolerance * scale;
    }
}
