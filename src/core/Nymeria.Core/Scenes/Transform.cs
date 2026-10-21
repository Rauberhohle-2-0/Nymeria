using System.Numerics;

namespace Nymeria.Core.Scenes;

/// <summary>
/// Position, rotation, and scale for one scene object.
/// </summary>
/// <remarks>
/// <para>
/// World and model spaces are right-handed with +Y up and -Z forward, matching
/// the rest of Nymeria. <see cref="CreateModelMatrix"/> composes
/// <c>scale * rotation * translation</c> because <see cref="System.Numerics"/>
/// uses row vectors. A point is therefore scaled in object space first, then
/// rotated, then translated. Combine the result as
/// <c>model * view * projection</c>.
/// </para>
/// <para>
/// Rotation is stored as a normalized quaternion. Non-finite position, scale,
/// or rotation values are rejected when they are assigned, and a zero-length
/// quaternion is invalid. Zero or negative scale is accepted, but it is
/// degenerate or mirrors the object.
/// </para>
/// </remarks>
public sealed class Transform
{
    private const double MinimumQuaternionLengthSquared = 1e-24;

    private Vector3 _position;
    private Quaternion _rotation = Quaternion.Identity;
    private Vector3 _scale = Vector3.One;

    /// <summary>
    /// Creates an identity transform: zero position, identity rotation, and
    /// unit scale.
    /// </summary>
    public Transform()
    {
    }

    /// <summary>
    /// Creates a transform from the supplied components.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A component is not finite, or the rotation has zero length.
    /// </exception>
    public Transform(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }

    /// <summary>
    /// Gets or sets the world-space translation.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A component is not finite.
    /// </exception>
    public Vector3 Position
    {
        get => _position;
        set
        {
            ValidateFinite(value, nameof(Position));
            _position = value;
        }
    }

    /// <summary>
    /// Gets or sets the rotation. The assigned quaternion is normalized.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A component is not finite, or the quaternion has zero length.
    /// </exception>
    public Quaternion Rotation
    {
        get => _rotation;
        set => _rotation = NormalizeRotation(value);
    }

    /// <summary>
    /// Gets or sets the object-space scale. Non-uniform and negative values are
    /// allowed; zero collapses the object.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A component is not finite.
    /// </exception>
    public Vector3 Scale
    {
        get => _scale;
        set
        {
            ValidateFinite(value, nameof(Scale));
            _scale = value;
        }
    }

    /// <summary>
    /// Creates the row-vector model matrix as
    /// <c>scale * rotation * translation</c>.
    /// </summary>
    public Matrix4x4 CreateModelMatrix()
    {
        return Matrix4x4.CreateScale(_scale) *
            Matrix4x4.CreateFromQuaternion(_rotation) *
            Matrix4x4.CreateTranslation(_position);
    }

    private static void ValidateFinite(Vector3 value, string paramName)
    {
        if (!float.IsFinite(value.X) ||
            !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                "Transform components must be finite.");
        }
    }

    private static Quaternion NormalizeRotation(Quaternion value)
    {
        if (!float.IsFinite(value.X) ||
            !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z) ||
            !float.IsFinite(value.W))
        {
            throw new ArgumentOutOfRangeException(
                nameof(Rotation),
                value,
                "The rotation quaternion must contain finite values.");
        }

        // Use double precision so a finite float quaternion near the maximum
        // magnitude cannot overflow while its length is being computed.
        double lengthSquared =
            ((double)value.X * value.X) +
            ((double)value.Y * value.Y) +
            ((double)value.Z * value.Z) +
            ((double)value.W * value.W);

        if (!double.IsFinite(lengthSquared) ||
            lengthSquared <= MinimumQuaternionLengthSquared)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Rotation),
                value,
                "The rotation quaternion must have a non-zero finite length.");
        }

        double inverseLength = 1.0 / Math.Sqrt(lengthSquared);

        return new Quaternion(
            (float)(value.X * inverseLength),
            (float)(value.Y * inverseLength),
            (float)(value.Z * inverseLength),
            (float)(value.W * inverseLength));
    }
}
