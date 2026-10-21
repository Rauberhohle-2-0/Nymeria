using System.Numerics;
using Nymeria.Core.Lod;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;

namespace Nymeria.Core.Culling;

/// <summary>
/// Tests scene instances for distance and frustum visibility, selects their LOD
/// level, and counts the result for one frame.
/// </summary>
/// <remarks>
/// <para>
/// A renderer resets the counter at the start of a frame and calls
/// <see cref="Test(SceneObject, Frustum?, out Matrix4x4)"/> or its LOD-aware
/// overload once per scene object before issuing a draw call. Passing
/// <see langword="null"/> for the frustum disables frustum culling.
/// </para>
/// <para>
/// The LOD-aware test applies this order:
/// <list type="number">
/// <item>count the candidate and compose its model matrix;</item>
/// <item>compute conservative world bounds and the camera-to-bounds distance;</item>
/// <item>skip the instance when it is beyond its maximum render distance;</item>
/// <item>skip the instance when it is fully outside the frustum;</item>
/// <item>select and record its LOD level;</item>
/// <item>return the selected mesh for draw submission.</item>
/// </list>
/// Distance culling is checked before the six-plane test because it is cheaper
/// and avoids testing clearly invisible instances. LOD selection happens only
/// for instances that will be drawn, and never changes the bounds used for
/// visibility. An instance culled by either test is never counted in the
/// per-level usage.
/// </para>
/// <para>
/// An object without a <see cref="SceneObject.Lod"/> configuration follows the
/// original single-mesh path and is never distance-culled. When LOD is disabled
/// for a frame, the object's per-instance selection state is reset so enabling it
/// again starts from a fresh, deterministic selection.
/// </para>
/// <para>
/// This type is a per-renderer scratch buffer and is not thread-safe.
/// </para>
/// </remarks>
public sealed class SceneVisibilityCounter
{
    private readonly int[] _lodLevelCounts = new int[MeshLod.MaxLevels];

    /// <summary>
    /// Gets the number of instances tested in the current frame.
    /// </summary>
    public int TotalInstances { get; private set; }

    /// <summary>
    /// Gets the number of instances that passed every enabled test and may be
    /// submitted in the current frame.
    /// </summary>
    public int VisibleInstances { get; private set; }

    /// <summary>
    /// Gets the number of instances fully outside the frustum in the current
    /// frame.
    /// </summary>
    /// <remarks>
    /// Instances already skipped by distance culling are not counted here, so
    /// <see cref="TotalInstances"/> equals <see cref="VisibleInstances"/> plus
    /// this value plus <see cref="DistanceCulledInstances"/>.
    /// </remarks>
    public int CulledInstances { get; private set; }

    /// <summary>
    /// Gets the number of instances skipped because they were strictly beyond
    /// their configured maximum render distance in the current frame.
    /// </summary>
    public int DistanceCulledInstances { get; private set; }

    /// <summary>
    /// Gets the number of drawn instances whose LOD configuration was active in
    /// the current frame.
    /// </summary>
    /// <remarks>
    /// The sum of <see cref="LodLevelUsage"/> equals this value. Objects without
    /// a configuration are never included.
    /// </remarks>
    public int LodDrawnInstances { get; private set; }

    /// <summary>
    /// Gets a copy of the current frame's drawn-instance count per LOD level.
    /// </summary>
    /// <remarks>
    /// The value is a snapshot and does not allocate. Indexing outside the
    /// supported level range throws.
    /// </remarks>
    public LodLevelUsage LodLevelUsage => new(_lodLevelCounts);

    /// <summary>
    /// Clears the counters before a new frame.
    /// </summary>
    public void Reset()
    {
        TotalInstances = 0;
        VisibleInstances = 0;
        CulledInstances = 0;
        DistanceCulledInstances = 0;
        LodDrawnInstances = 0;
        Array.Clear(_lodLevelCounts);
    }

    /// <summary>
    /// Tests one scene object against an optional frustum without LOD or
    /// distance culling.
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
        return Test(
            sceneObject,
            frustum,
            cameraPosition: Vector3.Zero,
            lodEnabled: false,
            distanceCullingEnabled: false,
            out modelMatrix,
            out _);
    }

    /// <summary>
    /// Tests one scene object for distance culling, frustum visibility, and LOD
    /// selection.
    /// </summary>
    /// <param name="sceneObject">The instance to test.</param>
    /// <param name="frustum">
    /// The current frustum, or <see langword="null"/> to disable frustum
    /// culling.
    /// </param>
    /// <param name="cameraPosition">
    /// The camera position used for distance measurement. A non-finite value
    /// fails safe to the highest-detail level with no distance cull.
    /// </param>
    /// <param name="lodEnabled">
    /// Whether configured LOD and distance culling are active. When false, the
    /// object draws its primary mesh and its stored level is reset.
    /// </param>
    /// <param name="distanceCullingEnabled">
    /// Whether a configured maximum render distance is enforced.
    /// </param>
    /// <param name="modelMatrix">
    /// The object's model matrix, returned so the caller can reuse it instead of
    /// composing it twice.
    /// </param>
    /// <param name="mesh">
    /// The mesh to draw: the selected LOD variant, or the object's primary mesh
    /// when no configuration is active.
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
        Vector3 cameraPosition,
        bool lodEnabled,
        bool distanceCullingEnabled,
        out Matrix4x4 modelMatrix,
        out MeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(sceneObject);

        TotalInstances++;
        modelMatrix = sceneObject.Transform.CreateModelMatrix();
        mesh = sceneObject.Mesh;

        MeshLod? lod = lodEnabled ? sceneObject.Lod : null;

        if (lod is null)
        {
            sceneObject.ResetLodLevel();
            return TestPrimaryMesh(sceneObject, frustum, modelMatrix);
        }

        // One union of every level's bounds keeps the measured distance and the
        // visibility test independent of which level is currently selected.
        BoundingBox worldBounds = lod.Bounds.Transform(modelMatrix);
        float distance = LodMath.DistanceToBounds(cameraPosition, worldBounds);

        if (distanceCullingEnabled && lod.IsBeyondMaximumRenderDistance(distance))
        {
            DistanceCulledInstances++;
            return false;
        }

        if (frustum is not null && !frustum.Intersects(worldBounds))
        {
            CulledInstances++;
            return false;
        }

        int level = lod.SelectLevel(distance, sceneObject.CurrentLodLevel);
        sceneObject.SetCurrentLodLevel(level);
        mesh = lod.Levels[level].Mesh;
        VisibleInstances++;
        LodDrawnInstances++;
        _lodLevelCounts[level]++;
        return true;
    }

    private bool TestPrimaryMesh(
        SceneObject sceneObject,
        Frustum? frustum,
        Matrix4x4 modelMatrix)
    {
        if (frustum is null)
        {
            VisibleInstances++;
            return true;
        }

        if (frustum.Intersects(sceneObject.Mesh.Bounds.Transform(modelMatrix)))
        {
            VisibleInstances++;
            return true;
        }

        CulledInstances++;
        return false;
    }
}
