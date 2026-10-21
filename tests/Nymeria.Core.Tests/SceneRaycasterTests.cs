using System.Numerics;
using Nymeria.Core.Culling;
using Nymeria.Core.Lod;
using Nymeria.Core.Models;
using Nymeria.Core.Raycasting;
using Nymeria.Core.Scenes;
using Nymeria.Core.Streaming;

namespace Nymeria.Core.Tests;

public sealed class SceneRaycasterTests
{
    private static readonly Ray ForwardRay = new(
        new Vector3(0.0f, 0.0f, 10.0f),
        -Vector3.UnitZ);

    [Fact]
    public void TryRaycast_EmptySceneMisses()
    {
        var scene = new Scene();

        Assert.False(SceneRaycaster.TryRaycast(scene, ForwardRay, 100.0f, out RaycastHit hit));
        Assert.Equal(default, hit);
        Assert.Null(SceneRaycaster.Raycast(scene, ForwardRay, 100.0f));
    }

    [Fact]
    public void TryRaycast_ReturnsNearestOfSeveralObjects()
    {
        MeshData mesh = RaycastTestMeshes.CreateBoxMesh(1.0f);
        var scene = new Scene();
        var far = new SceneObject(
            mesh,
            new Transform(new Vector3(0.0f, 0.0f, -10.0f), Quaternion.Identity, Vector3.One));
        var near = new SceneObject(
            mesh,
            new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One));
        scene.Add(far);
        scene.Add(near);

        Assert.True(SceneRaycaster.TryRaycast(scene, ForwardRay, 100.0f, out RaycastHit hit));

        Assert.Same(near, hit.SceneObject);
        Assert.Equal(9.0f, hit.Distance, precision: 4);
        Assert.Equal(new Vector3(0.0f, 0.0f, 1.0f), hit.Normal);
        Assert.Equal(new Vector3(0.0f, 0.0f, 1.0f), hit.Point);
    }

    [Fact]
    public void TryRaycast_SelectsNearestWhenBoundsOverlapTheSameRay()
    {
        MeshData mesh = RaycastTestMeshes.CreateBoxMesh(1.0f);
        var scene = new Scene();
        var largerBehind = new SceneObject(
            mesh,
            new Transform(
                new Vector3(0.0f, 0.0f, -2.0f),
                Quaternion.Identity,
                new Vector3(3.0f, 3.0f, 1.0f)));
        var smallerInFront = new SceneObject(
            mesh,
            new Transform(
                new Vector3(0.0f, 0.0f, 2.0f),
                Quaternion.Identity,
                new Vector3(0.3f, 0.3f, 0.3f)));
        scene.Add(largerBehind);
        scene.Add(smallerInFront);

        Assert.True(SceneRaycaster.TryRaycast(scene, ForwardRay, 100.0f, out RaycastHit hit));

        Assert.Same(smallerInFront, hit.SceneObject);
        Assert.Equal(7.7f, hit.Distance, precision: 4);
    }

    [Fact]
    public void TryRaycast_HonorsInclusiveMaximumDistance()
    {
        var scene = new Scene();
        var sceneObject = new SceneObject(RaycastTestMeshes.CreateBoxMesh(1.0f));
        scene.Add(sceneObject);

        Assert.True(SceneRaycaster.TryRaycast(scene, ForwardRay, 9.0f, out RaycastHit hit));
        Assert.Equal(9.0f, hit.Distance, precision: 4);
        Assert.False(SceneRaycaster.TryRaycast(scene, ForwardRay, 8.999f, out _));
    }

    [Fact]
    public void TryRaycast_EqualDistancesResolveByInsertionOrder()
    {
        MeshData mesh = RaycastTestMeshes.CreateBoxMesh(1.0f);
        var first = new SceneObject(mesh);
        var second = new SceneObject(mesh);
        var scene = new Scene();
        scene.Add(first);
        scene.Add(second);

        Assert.True(SceneRaycaster.TryRaycast(scene, ForwardRay, 100.0f, out RaycastHit hit));

        Assert.Same(first, hit.SceneObject);

        scene.Remove(first);

        Assert.True(SceneRaycaster.TryRaycast(scene, ForwardRay, 100.0f, out RaycastHit afterRemoval));
        Assert.Same(second, afterRemoval.SceneObject);
    }

    [Fact]
    public void TryRaycast_UsesWorldTranslationInsteadOfLocalBounds()
    {
        var scene = new Scene();
        var sceneObject = new SceneObject(
            RaycastTestMeshes.CreateBoxMesh(1.0f),
            new Transform(new Vector3(10.0f, 0.0f, 0.0f), Quaternion.Identity, Vector3.One));
        scene.Add(sceneObject);

        Assert.False(SceneRaycaster.TryRaycast(scene, ForwardRay, 100.0f, out _));

        var shiftedRay = new Ray(new Vector3(10.0f, 0.0f, 10.0f), -Vector3.UnitZ);

        Assert.True(SceneRaycaster.TryRaycast(scene, shiftedRay, 100.0f, out RaycastHit hit));
        Assert.Same(sceneObject, hit.SceneObject);
        Assert.Equal(new Vector3(10.0f, 0.0f, 1.0f), hit.Point);
    }

    [Fact]
    public void TryRaycast_UsesWorldBoundsForRotationAndNonUniformScale()
    {
        var scene = new Scene();
        var sceneObject = new SceneObject(
            RaycastTestMeshes.CreateBoxMesh(1.0f),
            new Transform(
                new Vector3(10.0f, 0.0f, 0.0f),
                Quaternion.CreateFromYawPitchRoll(MathF.PI / 4.0f, 0.0f, 0.0f),
                new Vector3(2.0f, 1.0f, 1.0f)));
        scene.Add(sceneObject);

        // Scaled X extent 2 and Z extent 1, rotated 45 degrees about Y, produce
        // a world half extent of 2 * cos(45) + 1 * sin(45) on both axes.
        const float halfExtent = 2.12132f;

        Assert.True(SceneRaycaster.TryRaycast(
            scene,
            new Ray(new Vector3(12.0f, 0.0f, 10.0f), -Vector3.UnitZ),
            100.0f,
            out RaycastHit hit));
        Assert.Equal(10.0f - halfExtent, hit.Distance, precision: 3);
        Assert.Equal(12.0f, hit.Point.X, precision: 3);

        Assert.False(SceneRaycaster.TryRaycast(
            scene,
            new Ray(new Vector3(13.0f, 0.0f, 10.0f), -Vector3.UnitZ),
            100.0f,
            out _));
    }

    [Fact]
    public void TryRaycast_UsesNonUniformScaleOfLocalBounds()
    {
        var scene = new Scene();
        var sceneObject = new SceneObject(
            RaycastTestMeshes.CreateBoxMesh(1.0f),
            new Transform(Vector3.Zero, Quaternion.Identity, new Vector3(4.0f, 1.0f, 1.0f)));
        scene.Add(sceneObject);

        // The local box ends at X = 1; only the transformed box reaches X = 3.
        Assert.True(SceneRaycaster.TryRaycast(
            scene,
            new Ray(new Vector3(3.0f, 0.0f, 10.0f), -Vector3.UnitZ),
            100.0f,
            out RaycastHit hit));
        Assert.Same(sceneObject, hit.SceneObject);
    }

    [Fact]
    public void TryRaycast_SkipsNonPickableObjects()
    {
        MeshData mesh = RaycastTestMeshes.CreateBoxMesh(1.0f);
        var scene = new Scene();
        var ghost = new SceneObject(
            mesh,
            new Transform(new Vector3(0.0f, 0.0f, 5.0f), Quaternion.Identity, Vector3.One))
        {
            IsPickable = false,
        };
        var behind = new SceneObject(mesh);
        scene.Add(ghost);
        scene.Add(behind);

        Assert.True(SceneRaycaster.TryRaycast(scene, ForwardRay, 100.0f, out RaycastHit hit));
        Assert.Same(behind, hit.SceneObject);
        Assert.Equal(9.0f, hit.Distance, precision: 4);

        ghost.IsPickable = true;

        Assert.True(SceneRaycaster.TryRaycast(scene, ForwardRay, 100.0f, out RaycastHit enabled));
        Assert.Same(ghost, enabled.SceneObject);
        Assert.Equal(4.0f, enabled.Distance, precision: 4);
    }

    [Fact]
    public void TryRaycast_DoesNotUseFrustumVisibilityAsPickability()
    {
        var scene = new Scene();
        var sceneObject = new SceneObject(
            RaycastTestMeshes.CreateBoxMesh(1.0f),
            new Transform(new Vector3(100.0f, 0.0f, 0.0f), Quaternion.Identity, Vector3.One));
        scene.Add(sceneObject);

        var cameraPosition = new Vector3(0.0f, 0.0f, 10.0f);
        Matrix4x4 view = Matrix4x4.CreateLookAt(
            cameraPosition,
            cameraPosition - Vector3.UnitZ,
            Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4.0f,
            1.0f,
            0.1f,
            100.0f);
        Frustum frustum = Frustum.Create(view * projection);
        var counter = new SceneVisibilityCounter();

        Assert.False(counter.Test(sceneObject, frustum, out _));
        Assert.Equal(1, counter.CulledInstances);

        Vector3 direction = Vector3.Normalize(new Vector3(100.0f, 0.0f, 0.0f) - cameraPosition);
        var ray = new Ray(cameraPosition, direction);

        Assert.True(SceneRaycaster.TryRaycast(scene, ray, 200.0f, out RaycastHit hit));
        Assert.Same(sceneObject, hit.SceneObject);
    }

    [Fact]
    public void TryRaycast_UsesLodUnionBoundsRegardlessOfSelectedLevel()
    {
        MeshData high = RaycastTestMeshes.CreateBoxMesh(1.0f);
        MeshData low = RaycastTestMeshes.CreateBoxMesh(3.0f);
        var lod = new MeshLod(
            [
                new MeshLodLevel(high, 0.0f),
                new MeshLodLevel(low, 10.0f),
            ]);
        var scene = new Scene();
        var sceneObject = new SceneObject(high, null, null, lod);
        scene.Add(sceneObject);

        // X = 2.5 lies outside the high-detail mesh but inside the low-detail
        // variant, so the conservative union must be used for logical picking.
        var ray = new Ray(new Vector3(2.5f, 0.0f, 10.0f), -Vector3.UnitZ);
        var counter = new SceneVisibilityCounter();

        Assert.True(counter.Test(
            sceneObject,
            frustum: null,
            cameraPosition: new Vector3(0.0f, 0.0f, 5.0f),
            lodEnabled: true,
            distanceCullingEnabled: true,
            out _,
            out MeshData nearMesh));
        Assert.Same(high, nearMesh);

        Assert.True(SceneRaycaster.TryRaycast(scene, ray, 100.0f, out RaycastHit nearSelectionHit));
        Assert.Equal(7.0f, nearSelectionHit.Distance, precision: 4);

        Assert.True(counter.Test(
            sceneObject,
            frustum: null,
            cameraPosition: new Vector3(0.0f, 0.0f, 100.0f),
            lodEnabled: true,
            distanceCullingEnabled: true,
            out _,
            out MeshData farMesh));
        Assert.Same(low, farMesh);

        Assert.True(SceneRaycaster.TryRaycast(scene, ray, 100.0f, out RaycastHit farSelectionHit));
        Assert.Equal(nearSelectionHit.Distance, farSelectionHit.Distance, precision: 4);
        Assert.Same(sceneObject, farSelectionHit.SceneObject);
    }

    [Fact]
    public void TryRaycast_UnloadedChunkObjectCannotProduceAHit()
    {
        MeshData mesh = RaycastTestMeshes.CreateBoxMesh(1.0f);
        var scene = new Scene();
        var streamedObject = new SceneObject(
            mesh,
            new Transform(new Vector3(0.0f, 0.0f, -5.0f), Quaternion.Identity, Vector3.One));
        var chunk = new SceneChunkDefinition(
            "picked",
            new BoundingBox(
                new Vector3(-2.0f, -2.0f, -7.0f),
                new Vector3(2.0f, 2.0f, -3.0f)),
            () => [streamedObject]);
        using var streamer = new SceneStreamer(
            scene,
            [chunk],
            new SceneStreamingConfiguration(10.0f, 20.0f));

        streamer.Update(Vector3.Zero, 0.0);

        Assert.True(SceneRaycaster.TryRaycast(scene, ForwardRay, 100.0f, out RaycastHit hit));
        Assert.Same(streamedObject, hit.SceneObject);
        Assert.Equal(14.0f, hit.Distance, precision: 4);

        SceneStreamingUpdateResult update = streamer.Update(
            new Vector3(0.0f, 0.0f, -100.0f),
            1.0);

        Assert.Equal(1, update.UnloadedChunks);
        Assert.Empty(scene.Objects);
        Assert.False(scene.Contains(hit.SceneObject));
        Assert.False(SceneRaycaster.TryRaycast(scene, ForwardRay, 100.0f, out _));

        // The returned reference is a plain managed object, not a native handle.
        // It remains safe to inspect, but it no longer represents scene content.
        Assert.Same(mesh, hit.SceneObject.Mesh);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(-1.0f)]
    public void TryRaycast_RejectsInvalidMaximumDistance(float maxDistance)
    {
        var scene = new Scene();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SceneRaycaster.TryRaycast(scene, ForwardRay, maxDistance, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SceneRaycaster.Raycast(scene, ForwardRay, maxDistance));
    }

    [Fact]
    public void TryRaycast_AllowsInfiniteMaximumDistance()
    {
        var scene = new Scene();
        scene.Add(new SceneObject(RaycastTestMeshes.CreateBoxMesh(1.0f)));

        Assert.True(SceneRaycaster.TryRaycast(
            scene,
            ForwardRay,
            float.PositiveInfinity,
            out _));
    }

    [Fact]
    public void TryRaycast_RejectsNullScene()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SceneRaycaster.TryRaycast(null!, ForwardRay, 1.0f, out _));
        Assert.Throws<ArgumentNullException>(() =>
            SceneRaycaster.Raycast(null!, ForwardRay, 1.0f));
    }

    [Fact]
    public void RaycastHit_ValidatesAndNormalizesItsValues()
    {
        var sceneObject = new SceneObject(RaycastTestMeshes.CreateBoxMesh(1.0f));

        var hit = new RaycastHit(
            sceneObject,
            new Vector3(1.0f, 2.0f, 3.0f),
            2.0f,
            new Vector3(0.0f, 0.0f, 4.0f));

        Assert.Same(sceneObject, hit.SceneObject);
        Assert.Equal(Vector3.UnitZ, hit.Normal);
        Assert.Throws<ArgumentNullException>(() =>
            new RaycastHit(null!, Vector3.Zero, 0.0f, Vector3.UnitZ));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RaycastHit(sceneObject, Vector3.Zero, -1.0f, Vector3.UnitZ));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RaycastHit(sceneObject, Vector3.Zero, float.NaN, Vector3.UnitZ));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RaycastHit(sceneObject, new Vector3(float.NaN), 0.0f, Vector3.UnitZ));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RaycastHit(sceneObject, Vector3.Zero, 0.0f, Vector3.Zero));
    }
}
