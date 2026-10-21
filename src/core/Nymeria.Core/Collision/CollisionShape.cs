using System.Numerics;
using Nymeria.Core.Models;

namespace Nymeria.Core.Collision;

/// <summary>
/// The collision primitive kind stored by a <see cref="CollisionShape"/>.
/// </summary>
public enum CollisionShapeKind
{
    /// <summary>
    /// No shape. This is the value of a default <see cref="CollisionShape"/>.
    /// </summary>
    None = 0,

    /// <summary>
    /// An axis-aligned box, represented by <see cref="BoundingBox"/>.
    /// </summary>
    Box = 1,

    /// <summary>
    /// A sphere, represented by <see cref="BoundingSphere"/>.
    /// </summary>
    Sphere = 2,
}

/// <summary>
/// One immutable collision primitive in model or world space.
/// </summary>
/// <remarks>
/// <para>
/// A shape holds either a validated <see cref="BoundingBox"/> or a validated
/// <see cref="BoundingSphere"/>; it never holds both. Construct it with
/// <see cref="FromBox"/> or <see cref="FromSphere"/>. The shape is separate from
/// render meshes: collision is a bounding-volume test, not triangle-mesh
/// collision. A default value has <see cref="Kind"/> equal to
/// <see cref="CollisionShapeKind.None"/> and does not participate in collision.
/// </para>
/// <para>
/// <see cref="TryTransform"/> converts a local shape to world space for the
/// affine transforms produced by <see cref="Scenes.Transform.CreateModelMatrix"/>.
/// Boxes use <see cref="BoundingBox.Transform"/>, which transforms all eight
/// corners and therefore conservatively covers rotation and non-uniform or
/// negative scale. Spheres use <see cref="BoundingSphere.TryTransform"/>, which
/// keeps rotation from changing the radius and uses the largest scale magnitude
/// for non-uniform scale. A non-finite or projective matrix is rejected rather
/// than producing invalid geometry, and sphere conversion rejects shear rather
/// than under-reporting its extent. A singular or zero-scale affine matrix is
/// accepted because it produces a valid flat box or point sphere.
/// </para>
/// <para>
/// This is a CPU-only value type. It has no SDL3, GPU, window, or format
/// dependency.
/// </para>
/// </remarks>
public readonly struct CollisionShape
{
    private readonly BoundingBox _box;
    private readonly BoundingSphere _sphere;
    private readonly CollisionShapeKind _kind;

    private CollisionShape(BoundingBox box)
    {
        _box = box;
        _kind = CollisionShapeKind.Box;
    }

    private CollisionShape(BoundingSphere sphere)
    {
        _sphere = sphere;
        _kind = CollisionShapeKind.Sphere;
    }

    /// <summary>
    /// Gets the primitive kind held by this shape.
    /// </summary>
    public CollisionShapeKind Kind => _kind;

    /// <summary>
    /// Creates a collision shape from an existing validated box.
    /// </summary>
    public static CollisionShape FromBox(BoundingBox box)
    {
        return new CollisionShape(box);
    }

    /// <summary>
    /// Creates a collision shape from an existing validated sphere.
    /// </summary>
    public static CollisionShape FromSphere(BoundingSphere sphere)
    {
        return new CollisionShape(sphere);
    }

    /// <summary>
    /// Tries to read the box stored by this shape.
    /// </summary>
    /// <returns>True when this shape is a box.</returns>
    public bool TryGetBox(out BoundingBox box)
    {
        box = _box;
        return _kind == CollisionShapeKind.Box;
    }

    /// <summary>
    /// Tries to read the sphere stored by this shape.
    /// </summary>
    /// <returns>True when this shape is a sphere.</returns>
    public bool TryGetSphere(out BoundingSphere sphere)
    {
        sphere = _sphere;
        return _kind == CollisionShapeKind.Sphere;
    }

    /// <summary>
    /// Converts this shape to world space using an affine model or world matrix.
    /// </summary>
    /// <param name="matrix">The affine transform to apply.</param>
    /// <param name="worldShape">
    /// The conservative world-space shape, or the default value when the shape
    /// is empty or the transform is unusable.
    /// </param>
    /// <returns>
    /// True when this shape contains a primitive and the matrix is finite and
    /// affine with a bottom row of (0, 0, 0, 1).
    /// </returns>
    /// <remarks>
    /// Box conversion can saturate extreme corner coordinates to the finite
    /// <see cref="float"/> range. That widens the box instead of creating a NaN
    /// or infinite shape, so the result can produce conservative false positives
    /// but never hides overlap. Sphere conversion fails when its center or radius
    /// cannot be represented as a finite sphere.
    /// </remarks>
    public bool TryTransform(Matrix4x4 matrix, out CollisionShape worldShape)
    {
        worldShape = default;

        if (_kind == CollisionShapeKind.None ||
            !IsAffineFinite(matrix))
        {
            return false;
        }

        if (_kind == CollisionShapeKind.Box)
        {
            worldShape = FromBox(_box.Transform(matrix));
            return true;
        }

        if (!_sphere.TryTransform(matrix, out BoundingSphere sphere))
        {
            return false;
        }

        worldShape = FromSphere(sphere);
        return true;
    }

    private static bool IsAffineFinite(Matrix4x4 value)
    {
        return
            float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
            float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
            float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
            float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
            float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
            float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
            float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
            float.IsFinite(value.M43) && float.IsFinite(value.M44) &&
            value.M14 == 0.0f &&
            value.M24 == 0.0f &&
            value.M34 == 0.0f &&
            value.M44 == 1.0f;
    }
}
