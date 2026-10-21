using Nymeria.Core.Models;

namespace Nymeria.Core.Lod;

/// <summary>
/// One explicit mesh variant in an ordered level-of-detail configuration.
/// </summary>
/// <remarks>
/// The level list is ordered from highest to lowest detail. A level becomes
/// active when the camera's distance to the configured bounding volume reaches
/// or exceeds <see cref="ActivationDistance"/>. The mesh is referenced, not
/// copied or owned, so several objects and configurations can share one GPU
/// upload.
/// </remarks>
public sealed class MeshLodLevel
{
    /// <summary>
    /// Creates a mesh LOD level.
    /// </summary>
    /// <param name="mesh">
    /// The shared CPU mesh for this level. It is not copied or owned.
    /// </param>
    /// <param name="activationDistance">
    /// The nonnegative, finite camera distance at which this level becomes
    /// active. Distance is measured to the configuration's conservative world
    /// bounding volume, not to the object origin.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="mesh"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="activationDistance"/> is negative or not finite.
    /// </exception>
    public MeshLodLevel(MeshData mesh, float activationDistance)
    {
        Mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));

        if (!float.IsFinite(activationDistance) || activationDistance < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activationDistance),
                activationDistance,
                "A LOD activation distance must be finite and nonnegative.");
        }

        ActivationDistance = activationDistance;
    }

    /// <summary>
    /// Gets the shared CPU mesh for this level.
    /// </summary>
    public MeshData Mesh { get; }

    /// <summary>
    /// Gets the distance at which this level becomes active.
    /// </summary>
    public float ActivationDistance { get; }
}
