using System.Numerics;
using Nymeria.Core.Culling;
using Nymeria.Core.Lod;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;

namespace Nymeria.Core.Tests;

public sealed class SceneLodCullingTests
{
    [Fact]
    public void Test_ObjectWithoutLod_UsesPrimaryMeshAndIsNeverDistanceCulled()
    {
        MeshData mesh = CreatePointMesh(Vector3.Zero);
        var sceneObject = new SceneObject(
            mesh,
            new Transform(new Vector3(0.0f, 0.0f, -6000.0f), Quaternion.Identity, Vector3.One));
        var counter = new SceneVisibilityCounter();
        counter.Reset();

        bool visible = counter.Test(
            sceneObject,
            frustum: null,
            cameraPosition: Vector3.Zero,
            lodEnabled: true,
            distanceCullingEnabled: true,
            out _,
            out MeshData selectedMesh);

        Assert.True(visible);
        Assert.Same(mesh, selectedMesh);
        Assert.Equal(1, counter.TotalInstances);
        Assert.Equal(1, counter.VisibleInstances);
        Assert.Equal(0, counter.CulledInstances);
        Assert.Equal(0, counter.DistanceCulledInstances);
        Assert.Equal(0, counter.LodDrawnInstances);
        Assert.Equal(0, counter.LodLevelUsage.Total);
    }

    [Fact]
    public void Test_SelectsOneMeshPerObjectFromDistance()
    {
        MeshData high = CreatePointMesh(Vector3.Zero);
        MeshData medium = CreatePointMesh(new Vector3(0.0f, 0.0f, 1.0f));
        MeshData low = CreatePointMesh(new Vector3(0.0f, 0.0f, 2.0f));
        MeshLod lod = CreateThreeLevelLod(high, medium, low, maximumDistance: null, hysteresis: 2.0f);
        var scene = new Scene();
        var near = new SceneObject(high, TransformAt(new Vector3(0.0f, 0.0f, -5.0f)), lod: lod);
        var middle = new SceneObject(high, TransformAt(new Vector3(0.0f, 0.0f, -20.0f)), lod: lod);
        var far = new SceneObject(high, TransformAt(new Vector3(0.0f, 0.0f, -40.0f)), lod: lod);
        scene.Add(near);
        scene.Add(middle);
        scene.Add(far);
        var counter = new SceneVisibilityCounter();
        counter.Reset();

        Assert.True(Test(counter, near, out MeshData nearMesh));
        Assert.True(Test(counter, middle, out MeshData middleMesh));
        Assert.True(Test(counter, far, out MeshData farMesh));

        Assert.Same(high, nearMesh);
        Assert.Same(medium, middleMesh);
        Assert.Same(low, farMesh);
        Assert.Equal(3, counter.TotalInstances);
        Assert.Equal(3, counter.VisibleInstances);
        Assert.Equal(0, counter.CulledInstances);
        Assert.Equal(0, counter.DistanceCulledInstances);
        Assert.Equal(3, counter.LodDrawnInstances);
        Assert.Equal(1, counter.LodLevelUsage[0]);
        Assert.Equal(1, counter.LodLevelUsage[1]);
        Assert.Equal(1, counter.LodLevelUsage[2]);
    }

    [Fact]
    public void Test_ObjectsSharingAConfigurationShareMeshReferences()
    {
        MeshData high = CreatePointMesh(Vector3.Zero);
        MeshData medium = CreatePointMesh(new Vector3(0.0f, 0.0f, 1.0f));
        MeshData low = CreatePointMesh(new Vector3(0.0f, 0.0f, 2.0f));
        MeshLod lod = CreateThreeLevelLod(high, medium, low, maximumDistance: null, hysteresis: 0.0f);
        var first = new SceneObject(high, TransformAt(new Vector3(0.0f, 0.0f, -20.0f)), lod: lod);
        var second = new SceneObject(high, TransformAt(new Vector3(0.0f, 0.0f, -20.0f)), lod: lod);
        var counter = new SceneVisibilityCounter();
        counter.Reset();

        Assert.True(Test(counter, first, out MeshData firstMesh));
        Assert.True(Test(counter, second, out MeshData secondMesh));

        Assert.Same(medium, firstMesh);
        Assert.Same(firstMesh, secondMesh);
        Assert.Same(lod.Levels[1].Mesh, secondMesh);
        Assert.Equal(2, counter.LodLevelUsage[1]);
    }

    [Fact]
    public void Test_DistanceCullingUsesAnInclusiveMaximum()
    {
        MeshData mesh = CreatePointMesh(Vector3.Zero);
        MeshLod lod = CreateSingleLevelLod(mesh, maximumDistance: 10.0f);
        var atMaximum = new SceneObject(mesh, TransformAt(new Vector3(0.0f, 0.0f, -10.0f)), lod: lod);
        var beyond = new SceneObject(mesh, TransformAt(new Vector3(0.0f, 0.0f, -10.001f)), lod: lod);
        var counter = new SceneVisibilityCounter();
        counter.Reset();

        Assert.True(Test(counter, atMaximum, out _));
        Assert.False(Test(counter, beyond, out _));

        Assert.Equal(2, counter.TotalInstances);
        Assert.Equal(1, counter.VisibleInstances);
        Assert.Equal(1, counter.DistanceCulledInstances);
        Assert.Equal(0, counter.CulledInstances);
        Assert.Equal(1, counter.LodDrawnInstances);
        Assert.Equal(1, counter.LodLevelUsage.Total);
    }

    [Fact]
    public void Test_DisablingDistanceCulling_DrawsBeyondMaximum()
    {
        MeshData mesh = CreatePointMesh(Vector3.Zero);
        MeshLod lod = CreateSingleLevelLod(mesh, maximumDistance: 10.0f);
        var sceneObject = new SceneObject(
            mesh,
            TransformAt(new Vector3(0.0f, 0.0f, -100.0f)),
            lod: lod);
        var counter = new SceneVisibilityCounter();
        counter.Reset();

        bool visible = counter.Test(
            sceneObject,
            frustum: null,
            cameraPosition: Vector3.Zero,
            lodEnabled: true,
            distanceCullingEnabled: false,
            out _,
            out MeshData selectedMesh);

        Assert.True(visible);
        Assert.Same(mesh, selectedMesh);
        Assert.Equal(0, counter.DistanceCulledInstances);
        Assert.Equal(1, counter.VisibleInstances);
        Assert.Equal(1, counter.LodDrawnInstances);
    }

    [Fact]
    public void Test_CountersPartitionCandidatesWithoutDoubleCounting()
    {
        MeshData mesh = CreatePointMesh(Vector3.Zero);
        MeshLod lod = CreateSingleLevelLod(mesh, maximumDistance: 5000.0f);
        var scene = new Scene();
        var near = new SceneObject(mesh, TransformAt(new Vector3(0.0f, 0.0f, -5.0f)), lod: lod);
        var outside = new SceneObject(mesh, TransformAt(new Vector3(1000.0f, 0.0f, -10.0f)), lod: lod);
        var beyond = new SceneObject(mesh, TransformAt(new Vector3(0.0f, 0.0f, -6000.0f)), lod: lod);
        scene.Add(near);
        scene.Add(outside);
        scene.Add(beyond);
        Frustum frustum = CreateFrustum();
        var counter = new SceneVisibilityCounter();
        counter.Reset();

        Assert.True(Test(counter, near, frustum, out _));
        Assert.False(Test(counter, outside, frustum, out _));
        Assert.False(Test(counter, beyond, frustum, out _));

        Assert.Equal(3, counter.TotalInstances);
        Assert.Equal(1, counter.VisibleInstances);
        Assert.Equal(1, counter.CulledInstances);
        Assert.Equal(1, counter.DistanceCulledInstances);
        Assert.Equal(
            counter.TotalInstances,
            counter.VisibleInstances +
                counter.CulledInstances +
                counter.DistanceCulledInstances);
        Assert.Equal(1, counter.LodDrawnInstances);
        Assert.Equal(counter.LodDrawnInstances, counter.LodLevelUsage.Total);
    }

    [Fact]
    public void Test_UnionBoundsPreventAFalseFrustumCull()
    {
        // The camera looks down +X. The low-detail mesh sits far to the side and
        // is fully outside the frustum on its own. The high-detail mesh extends
        // back across the view, so the conservative union intersects and the
        // instance must remain visible.
        MeshData high = CreateSpanningMesh(
            new Vector3(-1.0f, -1.0f, -100.0f),
            new Vector3(1.0f, 1.0f, 100.0f));
        MeshData low = CreateSpanningMesh(
            new Vector3(-1.0f, -1.0f, 59.0f),
            new Vector3(1.0f, 1.0f, 61.0f));
        MeshLod lod = CreateLod(
            high,
            low,
            secondActivationDistance: 50.0f,
            maximumDistance: null,
            hysteresis: 0.0f);
        var model = new Transform(
            new Vector3(100.0f, 0.0f, 0.0f),
            Quaternion.Identity,
            Vector3.One);
        var sceneObject = new SceneObject(high, model, lod: lod);
        Frustum frustum = CreateFrustum(
            Vector3.Zero,
            Vector3.UnitX,
            budgetFarPlane: 500.0f);
        Matrix4x4 modelMatrix = model.CreateModelMatrix();

        Assert.False(frustum.Intersects(low.Bounds.Transform(modelMatrix)));
        Assert.True(frustum.Intersects(lod.Bounds.Transform(modelMatrix)));

        var counter = new SceneVisibilityCounter();
        counter.Reset();
        Assert.True(Test(counter, sceneObject, frustum, out MeshData selectedMesh));
        Assert.Same(low, selectedMesh);
        Assert.Equal(1, counter.VisibleInstances);
        Assert.Equal(1, counter.LodLevelUsage[1]);
    }

    [Fact]
    public void Test_TransformedScaledBoundsChangeTheSelectedLevel()
    {
        MeshData high = CreateBoxMesh(1.0f);
        MeshData low = CreatePointMesh(Vector3.Zero);
        MeshLod lod = CreateLod(
            high,
            low,
            secondActivationDistance: 25.0f,
            maximumDistance: null,
            hysteresis: 0.0f);
        var unscaled = new SceneObject(
            high,
            new Transform(new Vector3(0.0f, 0.0f, -30.0f), Quaternion.Identity, Vector3.One),
            lod: lod);
        var scaled = new SceneObject(
            high,
            new Transform(
                new Vector3(0.0f, 0.0f, -30.0f),
                Quaternion.Identity,
                new Vector3(10.0f, 10.0f, 10.0f)),
            lod: lod);
        var counter = new SceneVisibilityCounter();
        counter.Reset();

        // The bounded distance is 29 for the unit object and 20 for the scaled
        // object, so only the unit object crosses the 25-unit threshold.
        Assert.Equal(29.0f, LodMath.DistanceToBounds(
            Vector3.Zero,
            lod.Bounds.Transform(unscaled.Transform.CreateModelMatrix())));
        Assert.Equal(20.0f, LodMath.DistanceToBounds(
            Vector3.Zero,
            lod.Bounds.Transform(scaled.Transform.CreateModelMatrix())));

        Assert.True(Test(counter, unscaled, out MeshData unscaledMesh));
        Assert.True(Test(counter, scaled, out MeshData scaledMesh));

        Assert.Same(low, unscaledMesh);
        Assert.Same(high, scaledMesh);
        Assert.Equal(1, counter.LodLevelUsage[0]);
        Assert.Equal(1, counter.LodLevelUsage[1]);
    }

    [Fact]
    public void Test_SharedConfigurationKeepsPerObjectState()
    {
        MeshData high = CreatePointMesh(Vector3.Zero);
        MeshData medium = CreatePointMesh(Vector3.Zero);
        MeshData low = CreatePointMesh(Vector3.Zero);
        MeshLod lod = CreateThreeLevelLod(high, medium, low, maximumDistance: null, hysteresis: 2.0f);
        var wasFar = new SceneObject(high, TransformAt(new Vector3(0.0f, 0.0f, -100.0f)), lod: lod);
        var wasNear = new SceneObject(high, TransformAt(new Vector3(0.0f, 0.0f, -5.0f)), lod: lod);
        var counter = new SceneVisibilityCounter();

        counter.Reset();
        Assert.True(Test(counter, wasFar, out _));
        Assert.True(Test(counter, wasNear, out _));
        Assert.Equal(1, counter.LodLevelUsage[0]);
        Assert.Equal(1, counter.LodLevelUsage[2]);

        // Same configuration and the same new distance, but different history:
        // one holds the low-detail level inside the band, the other advances
        // only one level. State is therefore per scene object.
        wasFar.Transform.Position = new Vector3(0.0f, 0.0f, -29.9f);
        wasNear.Transform.Position = new Vector3(0.0f, 0.0f, -29.9f);
        counter.Reset();
        Assert.True(Test(counter, wasFar, out MeshData farMesh));
        Assert.True(Test(counter, wasNear, out MeshData nearMesh));

        Assert.Same(low, farMesh);
        Assert.Same(medium, nearMesh);
        Assert.Equal(0, counter.LodLevelUsage[0]);
        Assert.Equal(1, counter.LodLevelUsage[1]);
        Assert.Equal(1, counter.LodLevelUsage[2]);
    }

    [Fact]
    public void Test_TogglingLodOffResetsStateDeterministically()
    {
        MeshData high = CreatePointMesh(Vector3.Zero);
        MeshData medium = CreatePointMesh(new Vector3(0.0f, 0.0f, 1.0f));
        MeshData low = CreatePointMesh(new Vector3(0.0f, 0.0f, 2.0f));
        MeshLod lod = CreateThreeLevelLod(high, medium, low, maximumDistance: null, hysteresis: 2.0f);
        var sceneObject = new SceneObject(high, TransformAt(new Vector3(0.0f, 0.0f, -100.0f)), lod: lod);
        var counter = new SceneVisibilityCounter();

        counter.Reset();
        Assert.True(Test(counter, sceneObject, out _));
        Assert.Equal(1, counter.LodLevelUsage[2]);

        // Disabling LOD draws the primary mesh and clears the stored level.
        counter.Reset();
        bool visible = counter.Test(
            sceneObject,
            frustum: null,
            cameraPosition: Vector3.Zero,
            lodEnabled: false,
            distanceCullingEnabled: true,
            out _,
            out MeshData primaryMesh);
        Assert.True(visible);
        Assert.Same(high, primaryMesh);
        Assert.Equal(0, counter.LodDrawnInstances);

        // Re-enabling selects freshly at the near-boundary distance rather than
        // holding the previous low-detail level.
        sceneObject.Transform.Position = new Vector3(0.0f, 0.0f, -29.9f);
        counter.Reset();
        Assert.True(Test(counter, sceneObject, out MeshData selectedMesh));
        Assert.Same(medium, selectedMesh);
        Assert.Equal(1, counter.LodLevelUsage[1]);
    }

    [Fact]
    public void Test_ReassigningAConfigurationResetsState()
    {
        MeshData high = CreatePointMesh(Vector3.Zero);
        MeshData medium = CreatePointMesh(new Vector3(0.0f, 0.0f, 1.0f));
        MeshData low = CreatePointMesh(new Vector3(0.0f, 0.0f, 2.0f));
        MeshLod lod = CreateThreeLevelLod(high, medium, low, maximumDistance: null, hysteresis: 2.0f);
        var sceneObject = new SceneObject(high, TransformAt(new Vector3(0.0f, 0.0f, -100.0f)), lod: lod);
        var counter = new SceneVisibilityCounter();

        counter.Reset();
        Assert.True(Test(counter, sceneObject, out _));
        Assert.Equal(1, counter.LodLevelUsage[2]);

        sceneObject.Lod = lod;
        sceneObject.Transform.Position = new Vector3(0.0f, 0.0f, -29.9f);
        counter.Reset();
        Assert.True(Test(counter, sceneObject, out MeshData selectedMesh));

        Assert.Same(medium, selectedMesh);
        Assert.Equal(1, counter.LodLevelUsage[1]);
    }

    [Fact]
    public void Test_OldOverloadKeepsTheSingleMeshBehavior()
    {
        MeshData high = CreatePointMesh(Vector3.Zero);
        MeshData low = CreatePointMesh(new Vector3(0.0f, 0.0f, 2.0f));
        MeshLod lod = CreateLod(
            high,
            low,
            secondActivationDistance: 10.0f,
            maximumDistance: 20.0f,
            hysteresis: 0.0f);
        var sceneObject = new SceneObject(high, TransformAt(new Vector3(0.0f, 0.0f, -100.0f)), lod: lod);
        var counter = new SceneVisibilityCounter();
        counter.Reset();

        Assert.True(counter.Test(sceneObject, frustum: null, out _));

        Assert.Equal(1, counter.TotalInstances);
        Assert.Equal(1, counter.VisibleInstances);
        Assert.Equal(0, counter.DistanceCulledInstances);
        Assert.Equal(0, counter.LodDrawnInstances);
        Assert.Equal(0, counter.LodLevelUsage.Total);
    }

    [Fact]
    public void Reset_ClearsDistanceAndLodCounters()
    {
        MeshData mesh = CreatePointMesh(Vector3.Zero);
        MeshLod lod = CreateSingleLevelLod(mesh, maximumDistance: 10.0f);
        var visible = new SceneObject(mesh, TransformAt(new Vector3(0.0f, 0.0f, -5.0f)), lod: lod);
        var beyond = new SceneObject(mesh, TransformAt(new Vector3(0.0f, 0.0f, -50.0f)), lod: lod);
        var counter = new SceneVisibilityCounter();
        counter.Reset();

        Assert.True(Test(counter, visible, out _));
        Assert.False(Test(counter, beyond, out _));
        Assert.NotEqual(0, counter.LodLevelUsage.Total);

        counter.Reset();

        Assert.Equal(0, counter.TotalInstances);
        Assert.Equal(0, counter.VisibleInstances);
        Assert.Equal(0, counter.CulledInstances);
        Assert.Equal(0, counter.DistanceCulledInstances);
        Assert.Equal(0, counter.LodDrawnInstances);
        Assert.Equal(0, counter.LodLevelUsage.Total);
    }

    private static bool Test(
        SceneVisibilityCounter counter,
        SceneObject sceneObject,
        out MeshData mesh)
    {
        return Test(counter, sceneObject, frustum: null, out mesh);
    }

    private static bool Test(
        SceneVisibilityCounter counter,
        SceneObject sceneObject,
        Frustum? frustum,
        out MeshData mesh)
    {
        return counter.Test(
            sceneObject,
            frustum,
            cameraPosition: Vector3.Zero,
            lodEnabled: true,
            distanceCullingEnabled: true,
            out _,
            out mesh);
    }

    private static Frustum CreateFrustum(
        Vector3? position = null,
        Vector3? forward = null,
        float budgetFarPlane = 100.0f)
    {
        Matrix4x4 view = Matrix4x4.CreateLookAt(
            position ?? Vector3.Zero,
            (position ?? Vector3.Zero) + (forward ?? -Vector3.UnitZ),
            Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4.0f,
            1.0f,
            0.1f,
            budgetFarPlane);

        return Frustum.Create(view * projection);
    }

    private static Transform TransformAt(Vector3 position)
    {
        return new Transform(position, Quaternion.Identity, Vector3.One);
    }

    private static MeshLod CreateThreeLevelLod(
        MeshData high,
        MeshData medium,
        MeshData low,
        float? maximumDistance,
        float hysteresis)
    {
        return new MeshLod(
            [
                new MeshLodLevel(high, 0.0f),
                new MeshLodLevel(medium, 10.0f),
                new MeshLodLevel(low, 30.0f),
            ],
            maximumDistance,
            hysteresis);
    }

    private static MeshLod CreateSingleLevelLod(MeshData mesh, float maximumDistance)
    {
        return new MeshLod(
            [new MeshLodLevel(mesh, 0.0f)],
            maximumDistance,
            hysteresis: 0.0f);
    }

    private static MeshLod CreateLod(
        MeshData high,
        MeshData low,
        float secondActivationDistance,
        float? maximumDistance,
        float hysteresis)
    {
        return new MeshLod(
            [
                new MeshLodLevel(high, 0.0f),
                new MeshLodLevel(low, secondActivationDistance),
            ],
            maximumDistance,
            hysteresis);
    }

    private static MeshData CreatePointMesh(Vector3 position)
    {
        Vector3[] positions = [position, position, position];

        return new MeshData(
            positions,
            ReadOnlyMemory<Vector3>.Empty,
            ReadOnlyMemory<Vector2>.Empty,
            new uint[] { 0u, 1u, 2u });
    }

    private static MeshData CreateBoxMesh(float halfExtent)
    {
        Vector3[] positions =
        [
            new(halfExtent, halfExtent, halfExtent),
            new(-halfExtent, -halfExtent, halfExtent),
            new(-halfExtent, halfExtent, -halfExtent),
        ];

        return new MeshData(
            positions,
            ReadOnlyMemory<Vector3>.Empty,
            ReadOnlyMemory<Vector2>.Empty,
            new uint[] { 0u, 1u, 2u });
    }

    private static MeshData CreateSpanningMesh(Vector3 min, Vector3 max)
    {
        Vector3 center = (min + max) * 0.5f;
        Vector3[] positions =
        [
            new(min.X, center.Y, min.Z),
            new(max.X, center.Y, max.Z),
            new(center.X, center.Y, min.Z),
        ];

        // Ensure the box's Y range is the requested one even though the
        // triangle is flat.
        positions[0].Y = min.Y;
        positions[1].Y = max.Y;
        positions[2].Y = min.Y;

        return new MeshData(
            positions,
            ReadOnlyMemory<Vector3>.Empty,
            ReadOnlyMemory<Vector2>.Empty,
            new uint[] { 0u, 1u, 2u });
    }
}
