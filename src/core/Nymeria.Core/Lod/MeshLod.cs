using System.Collections.ObjectModel;
using System.Numerics;
using Nymeria.Core.Models;

namespace Nymeria.Core.Lod;

/// <summary>
/// An immutable, validated level-of-detail configuration that an application
/// may attach to one or more scene objects.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Levels"/> is ordered from highest to lowest detail. The first
/// level must activate at distance zero, so an object is never hidden by the
/// absence of an active level. Each later activation distance must be finite,
/// nonnegative, and strictly greater than the previous one.
/// </para>
/// <para>
/// Distances are measured from the camera position to the closest point of the
/// conservative world-space bounding volume of <see cref="Bounds"/>. The volume
/// is the union of every level's model-space <see cref="MeshData.Bounds"/>,
/// transformed by the object's current model matrix. Using one union means a
/// change of level cannot change the measured distance or the conservative
/// visibility bounds. The camera being inside the volume yields a distance of
/// zero.
/// </para>
/// <para>
/// Hysteresis is an absolute distance band in world units. While a level is
/// selected, switching to the next lower-detail level requires the distance to
/// exceed that level's activation distance by more than
/// <see cref="Hysteresis"/>. Switching back to a higher-detail level requires
/// the distance to fall below the current level's activation distance by more
/// than <see cref="Hysteresis"/>. The band is therefore stable even when two
/// activation distances are closer together than twice the hysteresis.
/// </para>
/// <para>
/// The configuration references CPU meshes and owns no GPU resources. Sharing
/// one instance across objects shares its levels, bounds, maximum distance, and
/// hysteresis, but selection state is kept by each <c>SceneObject</c>, not here.
/// Nymeria does not generate, simplify, morph, or stream meshes.
/// </para>
/// </remarks>
public sealed class MeshLod
{
    /// <summary>
    /// The largest number of levels a configuration accepts.
    /// </summary>
    /// <remarks>
    /// The bound keeps the per-frame LOD usage counters fixed-size and
    /// allocation-free. Applications that need more levels can group objects or
    /// keep separate configurations.
    /// </remarks>
    public const int MaxLevels = 8;

    /// <summary>
    /// The default hysteresis band in world units.
    /// </summary>
    public const float DefaultHysteresis = 0.25f;

    private readonly ReadOnlyCollection<MeshLodLevel> _levels;

    /// <summary>
    /// Creates a validated level-of-detail configuration.
    /// </summary>
    /// <param name="levels">
    /// The ordered mesh variants, from highest to lowest detail. The list is
    /// copied, but the meshes are referenced and not owned. Its first level must
    /// activate at distance zero.
    /// </param>
    /// <param name="maximumRenderDistance">
    /// An optional maximum distance in world units, or null for no distance
    /// culling. When set, it must be finite and greater than zero. An object at
    /// exactly this distance is still drawn; only a greater distance is culled.
    /// </param>
    /// <param name="hysteresis">
    /// The nonnegative, finite hysteresis band in world units. The default is
    /// <see cref="DefaultHysteresis"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="levels"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The list is empty, contains a null level, exceeds <see cref="MaxLevels"/>,
    /// does not start at distance zero, or its activation distances are not
    /// strictly increasing.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The maximum render distance or hysteresis is negative or not finite.
    /// </exception>
    public MeshLod(
        IReadOnlyList<MeshLodLevel> levels,
        float? maximumRenderDistance = null,
        float hysteresis = DefaultHysteresis)
    {
        ArgumentNullException.ThrowIfNull(levels);

        if (levels.Count == 0)
        {
            throw new ArgumentException(
                "A LOD configuration must contain at least one level.",
                nameof(levels));
        }

        if (levels.Count > MaxLevels)
        {
            throw new ArgumentException(
                $"A LOD configuration cannot contain more than {MaxLevels} levels.",
                nameof(levels));
        }

        var copy = new MeshLodLevel[levels.Count];
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(-float.MaxValue);

        for (int index = 0; index < levels.Count; index++)
        {
            MeshLodLevel level = levels[index] ??
                throw new ArgumentException(
                    $"LOD level {index} is null.",
                    nameof(levels));

            if (index == 0 && level.ActivationDistance != 0.0f)
            {
                throw new ArgumentException(
                    "The highest-detail LOD level must activate at distance zero.",
                    nameof(levels));
            }

            if (index > 0 &&
                level.ActivationDistance <= copy[index - 1].ActivationDistance)
            {
                throw new ArgumentException(
                    $"LOD activation distances must be strictly increasing. Level " +
                    $"{index} has {level.ActivationDistance}, which is not greater " +
                    $"than level {index - 1}'s {copy[index - 1].ActivationDistance}.",
                    nameof(levels));
            }

            copy[index] = level;
            min = Vector3.Min(min, level.Mesh.Bounds.Min);
            max = Vector3.Max(max, level.Mesh.Bounds.Max);
        }

        if (maximumRenderDistance is float maximum)
        {
            if (!float.IsFinite(maximum) || maximum <= 0.0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumRenderDistance),
                    maximum,
                    "The maximum render distance must be finite and greater than zero.");
            }
        }

        if (!float.IsFinite(hysteresis) || hysteresis < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hysteresis),
                hysteresis,
                "The LOD hysteresis must be finite and nonnegative.");
        }

        _levels = Array.AsReadOnly(copy);
        Bounds = new BoundingBox(min, max);
        MaximumRenderDistance = maximumRenderDistance;
        Hysteresis = hysteresis;
    }

    /// <summary>
    /// Gets the ordered levels, from highest to lowest detail.
    /// </summary>
    public IReadOnlyList<MeshLodLevel> Levels => _levels;

    /// <summary>
    /// Gets the optional maximum render distance, or null for no distance
    /// culling.
    /// </summary>
    public float? MaximumRenderDistance { get; }

    /// <summary>
    /// Gets the hysteresis band in world units.
    /// </summary>
    public float Hysteresis { get; }

    /// <summary>
    /// Gets the conservative model-space union of every level's mesh bounds.
    /// </summary>
    public BoundingBox Bounds { get; }

    /// <summary>
    /// Reports whether a distance is strictly beyond the configured maximum
    /// render distance.
    /// </summary>
    /// <remarks>
    /// A configuration without a maximum never distance-culls. The exact maximum
    /// distance is inclusive, so an object on the boundary is still drawn.
    /// </remarks>
    public bool IsBeyondMaximumRenderDistance(float distance)
    {
        return MaximumRenderDistance is float maximum && distance > maximum;
    }

    /// <summary>
    /// Selects a level for a distance without considering a previous selection.
    /// </summary>
    /// <remarks>
    /// This is the deterministic initial and teleport-free selection. An
    /// activation distance is inclusive: a distance exactly equal to a level's
    /// activation distance selects that level. Invalid or non-finite distances
    /// fail safe to the highest-detail level rather than hiding the object.
    /// </remarks>
    public int SelectLevel(float distance)
    {
        if (!float.IsFinite(distance) || distance < 0.0f)
        {
            return 0;
        }

        int selected = 0;

        for (int index = 1; index < _levels.Count; index++)
        {
            if (distance < _levels[index].ActivationDistance)
            {
                break;
            }

            selected = index;
        }

        return selected;
    }

    /// <summary>
    /// Selects a level using the object's previous level and the hysteresis
    /// band.
    /// </summary>
    /// <param name="distance">
    /// The camera distance to the conservative world bounding volume.
    /// </param>
    /// <param name="currentLevel">
    /// The object's previous level, or a negative value when it has no previous
    /// selection.
    /// </param>
    /// <remarks>
    /// <para>
    /// A missing or out-of-range previous level selects as if there were no
    /// state. A large teleport can move across several levels in one call; the
    /// selection always ends at the stable level for the supplied distance.
    /// </para>
    /// <para>
    /// Initial selection is inclusive at an activation distance. While a level
    /// is selected, a move to a lower-detail level requires
    /// <c>distance &gt; activation + hysteresis</c> and a move back to a
    /// higher-detail level requires
    /// <c>distance &lt; activation - hysteresis</c>, so values exactly on either
    /// band edge hold the current level.
    /// </para>
    /// </remarks>
    public int SelectLevel(float distance, int currentLevel)
    {
        if (!float.IsFinite(distance) || distance < 0.0f)
        {
            return 0;
        }

        if (currentLevel < 0 || currentLevel >= _levels.Count)
        {
            return SelectLevel(distance);
        }

        int selected = currentLevel;

        // A teleport away from the camera may cross several thresholds.
        while (selected + 1 < _levels.Count &&
               (double)distance >
                   (double)_levels[selected + 1].ActivationDistance + Hysteresis)
        {
            selected++;
        }

        // A teleport toward the camera may cross several thresholds back.
        while (selected > 0 &&
               (double)distance <
                   (double)_levels[selected].ActivationDistance - Hysteresis)
        {
            selected--;
        }

        return selected;
    }
}
