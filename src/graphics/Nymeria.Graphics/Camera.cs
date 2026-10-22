using System.Numerics;

namespace Nymeria.Graphics;

/// <summary>
/// A first-person camera with position, yaw/pitch orientation, a right-handed
/// view matrix, and a perspective projection.
/// </summary>
/// <remarks>
/// The camera uses <see cref="System.Numerics"/> only. It does not depend on
/// SDL3, the GPU backend, or an example. World and eye spaces are right-handed
/// with +Y up, and the camera looks down -Z when yaw and pitch are zero.
/// </remarks>
public sealed class Camera
{
    /// <summary>
    /// The pitch limit in radians. A small margin below a right angle keeps the
    /// view direction from becoming parallel to the up vector, which would make
    /// the view matrix degenerate.
    /// </summary>
    public const float PitchLimitRadians = (MathF.PI / 2.0f) - 0.01f;

    private float _yawRadians;
    private float _pitchRadians;

    /// <summary>
    /// Initializes a camera at the given position, looking down -Z.
    /// </summary>
    public Camera(Vector3 position)
    {
        Position = position;
    }

    /// <summary>
    /// Gets or sets the camera position in world space.
    /// </summary>
    public Vector3 Position { get; set; }

    /// <summary>
    /// Gets or sets the horizontal rotation in radians. Positive yaw turns to
    /// the right. The value is wrapped to the range [-π, π].
    /// </summary>
    public float YawRadians
    {
        get => _yawRadians;
        set => _yawRadians = WrapRadians(value);
    }

    /// <summary>
    /// Gets or sets the vertical rotation in radians. Positive pitch looks up.
    /// The value is clamped to <see cref="PitchLimitRadians"/>.
    /// </summary>
    public float PitchRadians
    {
        get => _pitchRadians;
        set => _pitchRadians = float.IsFinite(value)
            ? Math.Clamp(value, -PitchLimitRadians, PitchLimitRadians)
            : 0.0f;
    }

    /// <summary>
    /// Gets or sets the vertical field of view in radians.
    /// </summary>
    public float FieldOfViewRadians { get; set; } = MathF.PI / 4.0f;

    /// <summary>
    /// Gets or sets the near clipping plane distance.
    /// </summary>
    public float NearPlane { get; set; } = 0.1f;

    /// <summary>
    /// Gets or sets the far clipping plane distance.
    /// </summary>
    public float FarPlane { get; set; } = 100.0f;

    /// <summary>
    /// Gets or sets the movement speed in world units per second.
    /// </summary>
    public float MoveSpeed { get; set; } = 4.0f;

    /// <summary>
    /// Gets or sets the mouse sensitivity in radians per pixel.
    /// </summary>
    public float MouseSensitivity { get; set; } = 0.0025f;

    /// <summary>
    /// Gets or sets the multiplier applied to the movement speed while the
    /// sprint key is held.
    /// </summary>
    public float SprintMultiplier { get; set; } = 3.0f;

    /// <summary>
    /// Gets the normalized viewing direction, including pitch.
    /// </summary>
    public Vector3 Forward
    {
        get
        {
            float cosPitch = MathF.Cos(_pitchRadians);
            return new Vector3(
                MathF.Sin(_yawRadians) * cosPitch,
                MathF.Sin(_pitchRadians),
                -MathF.Cos(_yawRadians) * cosPitch);
        }
    }

    /// <summary>
    /// Gets the normalized horizontal viewing direction. This is the direction
    /// used by forward and backward movement, so looking up or down does not
    /// change the movement plane.
    /// </summary>
    public Vector3 HorizontalForward => new(
        MathF.Sin(_yawRadians),
        0.0f,
        -MathF.Cos(_yawRadians));

    /// <summary>
    /// Gets the normalized horizontal right direction.
    /// </summary>
    public Vector3 Right => new(
        MathF.Cos(_yawRadians),
        0.0f,
        MathF.Sin(_yawRadians));

    /// <summary>
    /// Gets the right-handed view matrix.
    /// </summary>
    /// <remarks>
    /// The camera state is validated at this boundary. A non-finite position, a
    /// position so large that the look target coincides with it, or a degenerate
    /// view basis throws <see cref="InvalidOperationException"/> with the
    /// offending property, instead of returning a matrix containing NaNs or
    /// infinities to a renderer.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <see cref="Position"/> is not finite, the position magnitude cannot
    /// express a look target, or the view basis is degenerate.
    /// </exception>
    public Matrix4x4 ViewMatrix
    {
        get
        {
            RequireFinitePosition();

            Vector3 forward = Forward;
            Vector3 right = Vector3.Cross(forward, Vector3.UnitY);

            if (!IsFinite(forward) ||
                !IsFinite(right) ||
                right.LengthSquared() <= 1e-12f)
            {
                throw new InvalidOperationException(
                    "The camera forward direction is degenerate with the up " +
                    "vector, so a view basis cannot be built. Keep the pitch " +
                    "away from a right angle.");
            }

            // Compose the target in double precision and reject a target that
            // rounds back onto the position. That happens only for an extremely
            // large finite position, where the look direction would collapse.
            double targetX = (double)Position.X + forward.X;
            double targetY = (double)Position.Y + forward.Y;
            double targetZ = (double)Position.Z + forward.Z;
            var target = new Vector3((float)targetX, (float)targetY, (float)targetZ);

            if (target == Position)
            {
                throw new InvalidOperationException(
                    $"The camera position magnitude ({Position.Length():0.###e+0}) " +
                    "cannot express a distinct look target in single precision, " +
                    "so the view matrix would be undefined. Move the camera closer " +
                    "to the world origin.");
            }

            Matrix4x4 view = Matrix4x4.CreateLookAt(
                Position,
                target,
                Vector3.UnitY);

            if (!IsFinite(view))
            {
                throw new InvalidOperationException(
                    "The camera position and orientation produce a non-finite " +
                    "view matrix. Check Position, YawRadians, and PitchRadians.");
            }

            return view;
        }
    }

    /// <summary>
    /// Creates a right-handed perspective projection for the given aspect
    /// ratio. The matrix maps depth to [0, 1], matching SDL3 GPU and Metal.
    /// </summary>
    /// <remarks>
    /// The projection state is validated at this boundary. An invalid field of
    /// view, near plane, or far plane throws
    /// <see cref="InvalidOperationException"/> naming the property instead of
    /// returning a matrix containing NaNs or throwing from
    /// <see cref="System.Numerics"/>.
    /// </remarks>
    /// <param name="aspectRatio">Drawable width divided by drawable height.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="aspectRatio"/> is not a positive finite number.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The field of view is not finite and strictly between 0 and π, the near
    /// plane is not finite and positive, the far plane is not finite and greater
    /// than the near plane, or the resulting matrix is not finite.
    /// </exception>
    public Matrix4x4 CreateProjectionMatrix(float aspectRatio)
    {
        if (!float.IsFinite(aspectRatio) || aspectRatio <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(aspectRatio),
                "The projection aspect ratio must be a positive finite number.");
        }

        if (!float.IsFinite(FieldOfViewRadians) ||
            FieldOfViewRadians <= 0.0f ||
            FieldOfViewRadians >= MathF.PI)
        {
            throw new InvalidOperationException(
                $"FieldOfViewRadians is {FieldOfViewRadians} but must be finite and " +
                $"strictly between 0 and π radians ({MathF.PI}).");
        }

        if (!float.IsFinite(NearPlane) || NearPlane <= 0.0f)
        {
            throw new InvalidOperationException(
                $"NearPlane is {NearPlane} but must be finite and greater than zero.");
        }

        if (!float.IsFinite(FarPlane) || FarPlane <= NearPlane)
        {
            throw new InvalidOperationException(
                $"FarPlane is {FarPlane} but must be finite and greater than " +
                $"NearPlane ({NearPlane}).");
        }

        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            FieldOfViewRadians,
            aspectRatio,
            NearPlane,
            FarPlane);

        if (!IsFinite(projection))
        {
            throw new InvalidOperationException(
                "The camera projection parameters produce a non-finite matrix. " +
                "Check FieldOfViewRadians, NearPlane, and FarPlane.");
        }

        return projection;
    }

    /// <summary>
    /// Rotates the camera by the given yaw and pitch deltas in radians.
    /// Non-finite deltas are ignored.
    /// </summary>
    public void Rotate(float yawDeltaRadians, float pitchDeltaRadians)
    {
        if (!float.IsFinite(yawDeltaRadians) || !float.IsFinite(pitchDeltaRadians))
        {
            return;
        }

        YawRadians = _yawRadians + yawDeltaRadians;
        PitchRadians = _pitchRadians + pitchDeltaRadians;
    }

    /// <summary>
    /// Rotates the camera from a mouse movement in pixels.
    /// </summary>
    /// <param name="deltaX">Horizontal mouse movement; positive is right.</param>
    /// <param name="deltaY">Vertical mouse movement; positive is down.</param>
    public void LookByMouse(float deltaX, float deltaY)
    {
        Rotate(
            deltaX * MouseSensitivity,
            -deltaY * MouseSensitivity);
    }

    /// <summary>
    /// Moves the camera relative to its horizontal orientation.
    /// </summary>
    /// <param name="localDirection">
    /// Local movement input: X is right, Y is world up, and Z is horizontal
    /// forward. The vector is normalized when longer than one unit, so diagonal
    /// movement is not faster than movement along one axis.
    /// </param>
    /// <param name="deltaSeconds">Elapsed time in seconds.</param>
    /// <param name="sprint">Whether to apply the sprint multiplier.</param>
    public void Move(Vector3 localDirection, float deltaSeconds, bool sprint = false)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0.0f)
        {
            return;
        }

        float lengthSquared = localDirection.LengthSquared();

        if (!float.IsFinite(lengthSquared) || lengthSquared <= 0.0f)
        {
            return;
        }

        if (lengthSquared > 1.0f)
        {
            localDirection /= MathF.Sqrt(lengthSquared);
        }

        Vector3 worldDirection =
            (Right * localDirection.X) +
            (Vector3.UnitY * localDirection.Y) +
            (HorizontalForward * localDirection.Z);

        float multiplier = sprint ? SprintMultiplier : 1.0f;
        Position += worldDirection * MoveSpeed * multiplier * deltaSeconds;
    }

    private void RequireFinitePosition()
    {
        if (!IsFinite(Position))
        {
            throw new InvalidOperationException(
                $"Position is ({Position.X}, {Position.Y}, {Position.Z}) but must " +
                "contain only finite components.");
        }
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

    private static float WrapRadians(float radians)
    {
        if (!float.IsFinite(radians))
        {
            return 0.0f;
        }

        return MathF.IEEERemainder(radians, MathF.Tau);
    }
}
