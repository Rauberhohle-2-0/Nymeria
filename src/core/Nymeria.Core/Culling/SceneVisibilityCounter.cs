using System.Numerics;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;

namespace Nymeria.Core.Culling;

/// <summary>
/// Tests scene instances against an optional frustum and counts the result for
/// one frame.
/// </summary>
/// <remarks>
/// <para>
/// A renderer resets the counter at the start of a frame and calls
/// <see cref="Test"/> once per scene object before issuing a draw call. Passing
/// <see langword="null"/> for the frustum disables culling: every tested instance
/// is visible and no instance is culled, which keeps statistics comparable
/// between the two modes.
/// </para>
/// <para>
/// World bounds are derived from <see cref="MeshData.Bounds"/> and the object's
/// current model matrix. The scene layer has no change notification, so the box
/// is intentionally recomputed per frame rather than cached. Culling only reads
/// scene data: it never removes objects, unloads meshes, or changes transforms.
/// </para>
/// <para>
/// This type is a per-renderer scratch buffer and is not thread-safe.
/// </para>
/// </remarks>
public sealed class SceneVisibilityCounter
{
    /// <summary>
    /// Gets the number of instances tested in the current frame.
    /// </summary>
    public int TotalInstances { get; private set; }

    /// <summary>
    /// Gets the number of instances that may be submitted in the current frame.
    /// </summary>
    public int VisibleInstances { get; private set; }

    /// <summary>
    /// Gets the number of instances fully outside the frustum in the current
    /// frame.
    /// </summary>
    public int CulledInstances { get; private set; }

    /// <summary>
    /// Clears the counters before a new frame.
    /// </summary>
    public void Reset()
    {
        TotalInstances = 0;
        VisibleInstances = 0;
        CulledInstances = 0;
    }

    /// <summary>
    /// Tests one scene object and updates the current frame's counters.
    /// </summary>
    /// <param name="sceneObject">The instance to test.</param>
    /// <param name="frustum">
    /// The current frustum, or <see langword="null"/> to disable culling.
    /// </param>
    /// <param name="modelMatrix">
    /// The object's model matrix, returned so the caller can reuse it for the
    /// draw submission instead of composing it twice.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the caller should submit the instance.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="sceneObject"/> is null.
    /// </exception>
    public bool Test(
        SceneObject sceneObject,
        Frustum? frustum,
        out Matrix4x4 modelMatrix)
    {
        ArgumentNullException.ThrowIfNull(sceneObject);

        TotalInstances++;
        modelMatrix = sceneObject.Transform.CreateModelMatrix();

        if (frustum is null)
        {
            VisibleInstances++;
            return true;
        }

        BoundingBox worldBounds = sceneObject.Mesh.Bounds.Transform(modelMatrix);

        if (frustum.Intersects(worldBounds))
        {
            VisibleInstances++;
            return true;
        }

        CulledInstances++;
        return false;
    }
}
