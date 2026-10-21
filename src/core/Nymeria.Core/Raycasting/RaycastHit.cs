using System.Numerics;
using Nymeria.Core.Scenes;

namespace Nymeria.Core.Raycasting;

/// <summary>
/// One nearest hit returned by <see cref="SceneRaycaster"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SceneObject"/> is the reference that was present in the scene when
/// the query ran. The hit is not a handle and does not keep the object alive:
/// if a streaming chunk is unloaded later, the object leaves the scene and this
/// reference must not be treated as a current selection. Validate it with
/// <see cref="Scene.Contains(SceneObject)"/> before using it.
/// </para>
/// <para>
/// <see cref="Distance"/> is measured in world units from the ray origin, and
/// <see cref="Point"/> is the corresponding world-space position. Picking uses
/// transformed per-object bounds, so <see cref="Point"/> lies on a world-space
/// axis-aligned box rather than necessarily on the mesh surface, and
/// <see cref="Normal"/> is the outward normal of the box face the ray entered.
/// </para>
/// </remarks>
public readonly struct RaycastHit
{
    /// <summary>
    /// Creates a hit with validated finite world-space values.
    /// </summary>
    /// <param name="sceneObject">The scene object that was hit.</param>
    /// <param name="point">The world-space hit point.</param>
    /// <param name="distance">The nonnegative world-space distance from the ray origin.</param>
    /// <param name="normal">The unit-length world-space surface normal.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sceneObject"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The point, distance, or normal is non-finite, the distance is negative, or
    /// the normal is zero length.
    /// </exception>
    public RaycastHit(
        SceneObject sceneObject,
        Vector3 point,
        float distance,
        Vector3 normal)
    {
        ArgumentNullException.ThrowIfNull(sceneObject);

        if (!IsFinite(point))
        {
            throw new ArgumentOutOfRangeException(
                nameof(point),
                point,
                "A raycast hit point must contain finite components.");
        }

        if (!float.IsFinite(distance) || distance < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(distance),
                distance,
                "A raycast hit distance must be finite and nonnegative.");
        }

        if (!float.IsFinite(normal.X) ||
            !float.IsFinite(normal.Y) ||
            !float.IsFinite(normal.Z))
        {
            throw new ArgumentOutOfRangeException(
                nameof(normal),
                normal,
                "A raycast hit normal must be finite and non-zero.");
        }

        // Normalize in double so a very large finite normal cannot overflow its
        // length and collapse to a zero vector.
        double lengthSquared =
            ((double)normal.X * normal.X) +
            ((double)normal.Y * normal.Y) +
            ((double)normal.Z * normal.Z);

        if (!double.IsFinite(lengthSquared) || lengthSquared <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(normal),
                normal,
                "A raycast hit normal must be finite and non-zero.");
        }

        double inverseLength = 1.0 / Math.Sqrt(lengthSquared);
        var normalized = new Vector3(
            (float)(normal.X * inverseLength),
            (float)(normal.Y * inverseLength),
            (float)(normal.Z * inverseLength));

        if (!IsFinite(normalized))
        {
            throw new ArgumentOutOfRangeException(
                nameof(normal),
                normal,
                "A raycast hit normal must normalize to a finite unit vector.");
        }

        SceneObject = sceneObject;
        Point = point;
        Distance = distance;
        Normal = normalized;
    }

    /// <summary>
    /// Gets the scene object that was present in the scene at query time.
    /// </summary>
    public SceneObject SceneObject { get; }

    /// <summary>
    /// Gets the world-space hit point on the object's transformed bounds.
    /// </summary>
    public Vector3 Point { get; }

    /// <summary>
    /// Gets the world-space distance from the ray origin.
    /// </summary>
    public float Distance { get; }

    /// <summary>
    /// Gets the normalized outward world-space normal of the entry face.
    /// </summary>
    /// <remarks>
    /// When the ray origin starts inside the object's bounds, the hit is at
    /// distance zero and the normal is opposite the ray direction.
    /// </remarks>
    public Vector3 Normal { get; }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) &&
            float.IsFinite(value.Y) &&
            float.IsFinite(value.Z);
    }
}
