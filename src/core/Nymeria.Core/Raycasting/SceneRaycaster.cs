using System.Numerics;
using Nymeria.Core.Lod;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;

namespace Nymeria.Core.Raycasting;

/// <summary>
/// Answers the nearest scene-object hit for a world-space ray.
/// </summary>
/// <remarks>
/// <para>
/// Each pickable object is tested with its conservative world-space bounds: the
/// object's <see cref="MeshData.Bounds"/> transformed by its current model
/// matrix, or the union of all LOD variants when
/// <see cref="SceneObject.Lod"/> is set. Translation, rotation, non-uniform
/// scale, and negative scale are therefore handled by
/// <see cref="BoundingBox.Transform"/>. The test is a bounding-volume pick, not
/// a triangle-accurate hit: a reported point may lie on the box around a mesh
/// rather than on its surface.
/// </para>
/// <para>
/// Logical pickability is deliberately independent of rendering. Frustum
/// culling, distance culling, the currently selected LOD level, and whether the
/// object would be drawn this frame do not affect the query. An object with
/// <see cref="SceneObject.IsPickable"/> set to false is skipped.
/// </para>
/// <para>
/// Unloaded streamed objects are simply absent from the scene, so they cannot
/// produce a hit. A returned <see cref="RaycastHit"/> holds an ordinary object
/// reference; validate it with <see cref="Scene.Contains(SceneObject)"/> before
/// using a stored selection after a streaming update.
/// </para>
/// <para>
/// The query is single-threaded and allocation-free. It performs one linear scan
/// in scene insertion order, bounded by <see cref="Scene.MaxObjectCount"/>, and
/// does not build a spatial index or cache world bounds. When two objects are at
/// exactly the same distance, the earlier object in insertion order wins, so the
/// result is deterministic.
/// </para>
/// </remarks>
public static class SceneRaycaster
{
    /// <summary>
    /// Finds the closest pickable scene object along a ray.
    /// </summary>
    /// <param name="scene">The scene to query.</param>
    /// <param name="ray">The world-space ray.</param>
    /// <param name="maxDistance">
    /// The inclusive maximum world-space distance. It must be nonnegative and
    /// not NaN; <see cref="float.PositiveInfinity"/> means no limit.
    /// </param>
    /// <param name="hit">
    /// The nearest valid hit, or the default value when there is none.
    /// </param>
    /// <returns>True when at least one pickable object was hit.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxDistance"/> is NaN or negative. Positive infinity is
    /// allowed and means no distance limit.
    /// </exception>
    public static bool TryRaycast(
        Scene scene,
        Ray ray,
        float maxDistance,
        out RaycastHit hit)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (float.IsNaN(maxDistance) || maxDistance < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDistance),
                maxDistance,
                "The maximum raycast distance must be nonnegative and not NaN. " +
                "Positive infinity is allowed.");
        }

        hit = default;
        bool found = false;
        float bestDistance = float.MaxValue;

        for (int index = 0; index < scene.Objects.Count; index++)
        {
            SceneObject sceneObject = scene.Objects[index];

            if (!sceneObject.IsPickable)
            {
                continue;
            }

            Matrix4x4 modelMatrix = sceneObject.Transform.CreateModelMatrix();
            BoundingBox localBounds = sceneObject.Lod is { } lod
                ? lod.Bounds
                : sceneObject.Mesh.Bounds;
            BoundingBox worldBounds = localBounds.Transform(modelMatrix);

            if (!RayIntersections.TryIntersectBox(
                    ray,
                    worldBounds,
                    maxDistance,
                    out float distance,
                    out Vector3 normal))
            {
                continue;
            }

            // Strictly closer keeps the earliest insertion order on an exact tie.
            if (found && distance >= bestDistance)
            {
                continue;
            }

            found = true;
            bestDistance = distance;
            hit = new RaycastHit(
                sceneObject,
                ray.GetPoint(distance),
                distance,
                normal);
        }

        return found;
    }

    /// <summary>
    /// Finds the closest pickable scene object along a ray.
    /// </summary>
    /// <param name="scene">The scene to query.</param>
    /// <param name="ray">The world-space ray.</param>
    /// <param name="maxDistance">The inclusive maximum world-space distance.</param>
    /// <returns>The nearest valid hit, or null when there is none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxDistance"/> is NaN or negative.
    /// </exception>
    public static RaycastHit? Raycast(Scene scene, Ray ray, float maxDistance)
    {
        return TryRaycast(scene, ray, maxDistance, out RaycastHit hit)
            ? hit
            : null;
    }
}
