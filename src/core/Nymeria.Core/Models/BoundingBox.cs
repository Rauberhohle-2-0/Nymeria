using System.Numerics;

namespace Nymeria.Core.Models;

/// <summary>
/// An axis-aligned bounding box in model or world space.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Min"/> and <see cref="Max"/> are finite and ordered component by
/// component. A box with equal minimum and maximum on an axis is flat on that
/// axis and still valid.
/// </para>
/// <para>
/// <see cref="Transform"/> treats its matrix as an affine transform and returns
/// the axis-aligned box of the eight transformed corners, so rotation and
/// negative or non-uniform scale are handled without transforming the box's
/// center and extent. Corner arithmetic uses double precision. A result outside
/// the finite <see cref="float"/> range is saturated rather than turned into
/// infinity, which widens the box and can never hide geometry that a conservative
/// visibility test should keep.
/// </para>
/// </remarks>
public readonly struct BoundingBox
{
    private static readonly Vector3 s_maxValue = new(float.MaxValue);

    private readonly Vector3 _min;
    private readonly Vector3 _max;

    /// <summary>
    /// Creates a box from finite, ordered corner values.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A corner contains a non-finite component.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Any minimum component is greater than the matching maximum component.
    /// </exception>
    public BoundingBox(Vector3 min, Vector3 max)
    {
        if (!IsFinite(min) || !IsFinite(max))
        {
            throw new ArgumentOutOfRangeException(
                nameof(min),
                "Bounding-box corners must contain finite components.");
        }

        if (min.X > max.X || min.Y > max.Y || min.Z > max.Z)
        {
            throw new ArgumentException(
                "Each bounding-box minimum component must be less than or equal " +
                "to the matching maximum component.",
                nameof(min));
        }

        _min = min;
        _max = max;
    }

    /// <summary>
    /// Gets the corner with the smallest component on each axis.
    /// </summary>
    public Vector3 Min => _min;

    /// <summary>
    /// Gets the corner with the largest component on each axis.
    /// </summary>
    public Vector3 Max => _max;

    /// <summary>
    /// Returns the axis-aligned box containing the eight transformed corners.
    /// </summary>
    /// <remarks>
    /// The matrix is treated as affine. A non-finite matrix, or a transformed
    /// coordinate that cannot be represented as a finite <see cref="float"/>,
    /// produces the maximum finite box instead of an invalid or empty result.
    /// That fallback is deliberately conservative for visibility tests.
    /// </remarks>
    public BoundingBox Transform(Matrix4x4 matrix)
    {
        if (!IsFinite(matrix))
        {
            return CreateMaximum();
        }

        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        double maxZ = double.NegativeInfinity;

        for (int corner = 0; corner < 8; corner++)
        {
            float x = (corner & 1) == 0 ? _min.X : _max.X;
            float y = (corner & 2) == 0 ? _min.Y : _max.Y;
            float z = (corner & 4) == 0 ? _min.Z : _max.Z;

            // System.Numerics uses row vectors. These are the first three
            // components of (x, y, z, 1) * matrix, computed in double to avoid
            // overflowing an intermediate float sum.
            double transformedX =
                ((double)x * matrix.M11) +
                ((double)y * matrix.M21) +
                ((double)z * matrix.M31) +
                matrix.M41;
            double transformedY =
                ((double)x * matrix.M12) +
                ((double)y * matrix.M22) +
                ((double)z * matrix.M32) +
                matrix.M42;
            double transformedZ =
                ((double)x * matrix.M13) +
                ((double)y * matrix.M23) +
                ((double)z * matrix.M33) +
                matrix.M43;

            if (!double.IsFinite(transformedX) ||
                !double.IsFinite(transformedY) ||
                !double.IsFinite(transformedZ))
            {
                return CreateMaximum();
            }

            minX = Math.Min(minX, transformedX);
            minY = Math.Min(minY, transformedY);
            minZ = Math.Min(minZ, transformedZ);
            maxX = Math.Max(maxX, transformedX);
            maxY = Math.Max(maxY, transformedY);
            maxZ = Math.Max(maxZ, transformedZ);
        }

        return new BoundingBox(
            new Vector3(Saturate(minX), Saturate(minY), Saturate(minZ)),
            new Vector3(Saturate(maxX), Saturate(maxY), Saturate(maxZ)));
    }

    private static BoundingBox CreateMaximum()
    {
        return new BoundingBox(-s_maxValue, s_maxValue);
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
}
