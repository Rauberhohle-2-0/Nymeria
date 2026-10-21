using System.Numerics;

namespace Nymeria.Core.Lighting;

/// <summary>
/// CPU reference math for the lighting model shared by the examples.
/// </summary>
/// <remarks>
/// The renderers use the same equation in MSL, in world space. These helpers
/// make the conventions and edge cases testable without a GPU: normals are
/// normalized before the diffuse dot product, negative Lambert terms are
/// clamped to zero, and a degenerate model transform cannot produce a normal
/// matrix.
/// </remarks>
public static class LightingMath
{
    /// <summary>
    /// Computes the Lambert diffuse term
    /// <c>max(0, dot(normalize(normal), normalize(directionToLight)))</c>.
    /// </summary>
    /// <param name="normal">The world-space surface normal.</param>
    /// <param name="directionToLight">
    /// The world-space direction from the surface toward the light.
    /// </param>
    /// <returns>
    /// A value between 0 and 1, or 0 when either vector is zero or non-finite.
    /// </returns>
    public static float ComputeLambert(Vector3 normal, Vector3 directionToLight)
    {
        if (!IsFinite(normal) || !IsFinite(directionToLight))
        {
            return 0.0f;
        }

        float normalLengthSquared = normal.LengthSquared();
        float lightLengthSquared = directionToLight.LengthSquared();

        if (normalLengthSquared <= 0.0f || lightLengthSquared <= 0.0f)
        {
            return 0.0f;
        }

        Vector3 unitNormal = normal / MathF.Sqrt(normalLengthSquared);
        Vector3 unitDirection = directionToLight / MathF.Sqrt(lightLengthSquared);
        return MathF.Max(0.0f, Vector3.Dot(unitNormal, unitDirection));
    }

    /// <summary>
    /// Creates the normal matrix for a model transform.
    /// </summary>
    /// <remarks>
    /// The result is the inverse transpose of the full model matrix. Because
    /// Nymeria uses row vectors, a normal is transformed as
    /// <c>normal * normalMatrix</c>; with a fourth component of zero, the
    /// translation part is ignored. This transforms normals correctly under
    /// rotation and non-uniform scale. A singular transform, such as zero scale,
    /// or a result containing non-finite values returns false instead of
    /// producing a matrix that would poison the shader.
    /// </remarks>
    /// <param name="model">The row-vector model matrix.</param>
    /// <param name="normalMatrix">The inverse-transpose matrix when successful.</param>
    /// <returns>True when a finite normal matrix exists.</returns>
    public static bool TryCreateNormalMatrix(
        Matrix4x4 model,
        out Matrix4x4 normalMatrix)
    {
        normalMatrix = default;

        if (!IsFinite(model) || !Matrix4x4.Invert(model, out Matrix4x4 inverse))
        {
            return false;
        }

        Matrix4x4 result = Matrix4x4.Transpose(inverse);

        if (!IsFinite(result))
        {
            return false;
        }

        normalMatrix = result;
        return true;
    }

    internal static void ValidateColor(Vector3 color, string paramName)
    {
        if (!IsFinite(color) ||
            color.X < 0.0f || color.X > 1.0f ||
            color.Y < 0.0f || color.Y > 1.0f ||
            color.Z < 0.0f || color.Z > 1.0f)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                color,
                "Light color components must be finite and between 0 and 1.");
        }
    }

    internal static void ValidateIntensity(float intensity, string paramName)
    {
        if (!float.IsFinite(intensity) ||
            intensity < 0.0f ||
            intensity > AmbientLight.MaxIntensity)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                intensity,
                $"Light intensity must be finite and between 0 and {AmbientLight.MaxIntensity}.");
        }
    }

    internal static Vector3 NormalizeDirection(Vector3 direction, string paramName)
    {
        if (!IsFinite(direction))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                direction,
                "The light direction must contain finite values.");
        }

        double x = direction.X;
        double y = direction.Y;
        double z = direction.Z;
        double lengthSquared = (x * x) + (y * y) + (z * z);

        if (!double.IsFinite(lengthSquared) || lengthSquared <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                direction,
                "The light direction must have non-zero length.");
        }

        double inverseLength = 1.0 / Math.Sqrt(lengthSquared);

        return new Vector3(
            (float)(x * inverseLength),
            (float)(y * inverseLength),
            (float)(z * inverseLength));
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
