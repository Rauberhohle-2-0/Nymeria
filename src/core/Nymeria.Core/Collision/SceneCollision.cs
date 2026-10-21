using Nymeria.Core.Scenes;

namespace Nymeria.Core.Collision;

/// <summary>
/// Allocation-free, CPU-only collision queries over the current scene contents.
/// </summary>
/// <remarks>
/// <para>
/// Collision participation is opt-in through <see cref="SceneObject.Collider"/>.
/// An object without a collider is skipped. Shapes are defined in the object's
/// local model space and converted with the object's current model matrix, so
/// transforms are read at query time rather than cached. A sphere collider under
/// non-uniform scale uses the largest axis scale as a conservative radius; a box
/// collider uses the conservative transformed box.
/// </para>
/// <para>
/// Queries traverse <see cref="Scene.Objects"/> in insertion order. Only objects
/// currently in the scene participate: a chunk unloaded by
/// <see cref="Streaming.SceneStreamer"/> is absent, so its colliders cannot
/// overlap or retain anything. Frustum culling, distance culling, LOD selection,
/// and <see cref="SceneObject.IsPickable"/> are deliberately ignored. An active
/// off-screen collider still collides.
/// </para>
/// <para>
/// The pair query is the simple O(n²) baseline for small active sets. It tests
/// each unordered pair at most once, never tests an object against itself, and
/// reports pairs in first-object insertion order and then second-object insertion
/// order. There is no broad-phase index, cache, background thread, or collision
/// response. Queries are deterministic for a fixed scene state and must not run
/// concurrently with scene mutation or another streaming update.
/// </para>
/// </remarks>
public static class SceneCollision
{
    /// <summary>
    /// Gets the current world-space collision shape of one scene object.
    /// </summary>
    /// <param name="sceneObject">The object to inspect.</param>
    /// <param name="shape">
    /// The transformed collider, or the default value when the object has no
    /// collider or its transform cannot produce one.
    /// </param>
    /// <returns>True when a world-space shape was produced.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sceneObject"/> is null.</exception>
    public static bool TryGetWorldShape(
        SceneObject sceneObject,
        out CollisionShape shape)
    {
        ArgumentNullException.ThrowIfNull(sceneObject);

        shape = default;
        CollisionShape? collider = sceneObject.Collider;

        if (collider is null)
        {
            return false;
        }

        return collider.Value.TryTransform(
            sceneObject.Transform.CreateModelMatrix(),
            out shape);
    }

    /// <summary>
    /// Gets whether one scene object overlaps any other current collider.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="scene"/> or <paramref name="sceneObject"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="sceneObject"/> is not in <paramref name="scene"/>.
    /// </exception>
    public static bool Overlaps(Scene scene, SceneObject sceneObject)
    {
        return TryGetFirstOverlap(scene, sceneObject, out _);
    }

    /// <summary>
    /// Finds the first object in insertion order whose collider overlaps the
    /// supplied target.
    /// </summary>
    /// <param name="scene">The scene to query.</param>
    /// <param name="sceneObject">
    /// The target object. It must currently belong to <paramref name="scene"/>.
    /// </param>
    /// <param name="other">
    /// The first overlapping object in insertion order, or null when none
    /// overlaps. The target itself is never reported.
    /// </param>
    /// <returns>True when another object's collider overlaps the target.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="scene"/> or <paramref name="sceneObject"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="sceneObject"/> is not in <paramref name="scene"/>.
    /// </exception>
    public static bool TryGetFirstOverlap(
        Scene scene,
        SceneObject sceneObject,
        out SceneObject? other)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(sceneObject);
        ThrowIfNotInScene(scene, sceneObject);

        other = null;

        if (!TryGetWorldShape(sceneObject, out CollisionShape targetShape))
        {
            return false;
        }

        for (int index = 0; index < scene.Objects.Count; index++)
        {
            SceneObject candidate = scene.Objects[index];

            if (ReferenceEquals(candidate, sceneObject) ||
                !TryGetWorldShape(candidate, out CollisionShape candidateShape) ||
                !CollisionIntersections.Overlaps(targetShape, candidateShape))
            {
                continue;
            }

            other = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Appends every current object whose collider overlaps the target.
    /// </summary>
    /// <param name="scene">The scene to query.</param>
    /// <param name="sceneObject">
    /// The target object. It must currently belong to <paramref name="scene"/>.
    /// </param>
    /// <param name="destination">
    /// The collection that receives overlapping objects in insertion order. Its
    /// existing contents are preserved.
    /// </param>
    /// <returns>The number of objects appended.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="scene"/>, <paramref name="sceneObject"/>, or
    /// <paramref name="destination"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="sceneObject"/> is not in <paramref name="scene"/>.
    /// </exception>
    public static int AppendOverlaps(
        Scene scene,
        SceneObject sceneObject,
        ICollection<SceneObject> destination)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(sceneObject);
        ArgumentNullException.ThrowIfNull(destination);
        ThrowIfNotInScene(scene, sceneObject);

        if (!TryGetWorldShape(sceneObject, out CollisionShape targetShape))
        {
            return 0;
        }

        int appended = 0;

        for (int index = 0; index < scene.Objects.Count; index++)
        {
            SceneObject candidate = scene.Objects[index];

            if (ReferenceEquals(candidate, sceneObject) ||
                !TryGetWorldShape(candidate, out CollisionShape candidateShape) ||
                !CollisionIntersections.Overlaps(targetShape, candidateShape))
            {
                continue;
            }

            destination.Add(candidate);
            appended++;
        }

        return appended;
    }

    /// <summary>
    /// Appends every overlapping unordered pair of current colliders.
    /// </summary>
    /// <param name="scene">The scene to query.</param>
    /// <param name="destination">
    /// The collection that receives pairs. Its existing contents are preserved.
    /// Each pair is reported once, with the earlier scene object as
    /// <see cref="CollisionPair.First"/>, and objects are never paired with
    /// themselves.
    /// </param>
    /// <returns>The number of pairs appended.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="scene"/> or <paramref name="destination"/> is null.
    /// </exception>
    /// <remarks>
    /// The query evaluates every unordered pair of colliders, so it is O(n²) in
    /// the number of colliders and O(n²) transform work as well. Callers own the
    /// destination and its allocation. An active set with no overlapping pair
    /// still costs the full pair scan.
    /// </remarks>
    public static int AppendOverlapPairs(
        Scene scene,
        ICollection<CollisionPair> destination)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(destination);

        int appended = 0;

        for (int firstIndex = 0; firstIndex < scene.Objects.Count; firstIndex++)
        {
            SceneObject first = scene.Objects[firstIndex];

            if (!TryGetWorldShape(first, out CollisionShape firstShape))
            {
                continue;
            }

            for (int secondIndex = firstIndex + 1;
                 secondIndex < scene.Objects.Count;
                 secondIndex++)
            {
                SceneObject second = scene.Objects[secondIndex];

                if (!TryGetWorldShape(second, out CollisionShape secondShape) ||
                    !CollisionIntersections.Overlaps(firstShape, secondShape))
                {
                    continue;
                }

                destination.Add(new CollisionPair(first, second));
                appended++;
            }
        }

        return appended;
    }

    private static void ThrowIfNotInScene(Scene scene, SceneObject sceneObject)
    {
        if (!scene.Contains(sceneObject))
        {
            throw new ArgumentException(
                "The scene object is not present in the queried scene.",
                nameof(sceneObject));
        }
    }
}
