using System.Numerics;
using Nymeria.Core.Culling;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;

namespace Nymeria.Core.Tests;

public sealed class SceneVisibilityCounterTests
{
    [Fact]
    public void Test_CountsVisibleAndCulledInstancesForOneFrame()
    {
        (Scene scene, Frustum frustum) = CreateDeterministicScene();
        var counter = new SceneVisibilityCounter();

        counter.Reset();
        var results = new bool[scene.Count];

        for (int i = 0; i < scene.Count; i++)
        {
            results[i] = counter.Test(scene.Objects[i], frustum, out _);
        }

        Assert.Equal(7, counter.TotalInstances);
        Assert.Equal(4, counter.VisibleInstances);
        Assert.Equal(3, counter.CulledInstances);
        Assert.Equal(new[] { true, false, false, false, true, true, true }, results);
    }

    [Fact]
    public void Test_WithNoFrustum_TreatsEveryInstanceAsVisible()
    {
        (Scene scene, _) = CreateDeterministicScene();
        var counter = new SceneVisibilityCounter();
        counter.Reset();

        foreach (SceneObject sceneObject in scene.Objects)
        {
            Assert.True(counter.Test(sceneObject, frustum: null, out _));
        }

        Assert.Equal(7, counter.TotalInstances);
        Assert.Equal(7, counter.VisibleInstances);
        Assert.Equal(0, counter.CulledInstances);
    }

    [Fact]
    public void Test_WithUnboundedFrustum_TreatsEveryInstanceAsVisible()
    {
        (Scene scene, _) = CreateDeterministicScene();
        Frustum unbounded = Frustum.Create(new Matrix4x4());
        var counter = new SceneVisibilityCounter();
        counter.Reset();

        foreach (SceneObject sceneObject in scene.Objects)
        {
            Assert.True(counter.Test(sceneObject, unbounded, out _));
        }

        Assert.Equal(7, counter.TotalInstances);
        Assert.Equal(7, counter.VisibleInstances);
        Assert.Equal(0, counter.CulledInstances);
    }

    [Fact]
    public void Reset_ClearsCountersBetweenFrames()
    {
        (Scene scene, Frustum frustum) = CreateDeterministicScene();
        var counter = new SceneVisibilityCounter();

        counter.Reset();
        foreach (SceneObject sceneObject in scene.Objects)
        {
            counter.Test(sceneObject, frustum, out _);
        }

        counter.Reset();

        Assert.Equal(0, counter.TotalInstances);
        Assert.Equal(0, counter.VisibleInstances);
        Assert.Equal(0, counter.CulledInstances);
    }

    [Fact]
    public void Test_ReturnsTheObjectModelMatrix()
    {
        (Scene scene, Frustum frustum) = CreateDeterministicScene();
        var counter = new SceneVisibilityCounter();
        SceneObject sceneObject = scene.Objects[0];

        counter.Test(sceneObject, frustum, out Matrix4x4 model);

        Assert.Equal(sceneObject.Transform.CreateModelMatrix(), model);
    }

    [Fact]
    public void Test_DoesNotChangeSceneState()
    {
        (Scene scene, Frustum frustum) = CreateDeterministicScene();
        var counter = new SceneVisibilityCounter();
        Matrix4x4[] before = scene.Objects
            .Select(sceneObject => sceneObject.Transform.CreateModelMatrix())
            .ToArray();

        counter.Reset();
        foreach (SceneObject sceneObject in scene.Objects)
        {
            counter.Test(sceneObject, frustum, out _);
        }

        Assert.Equal(7, scene.Count);
        Assert.Equal(
            before,
            scene.Objects
                .Select(sceneObject => sceneObject.Transform.CreateModelMatrix())
                .ToArray());
    }

    [Fact]
    public void Test_RejectsNullObject()
    {
        var counter = new SceneVisibilityCounter();

        Assert.Throws<ArgumentNullException>(() =>
            counter.Test(sceneObject: null!, frustum: null, out _));
    }

    private static (Scene Scene, Frustum Frustum) CreateDeterministicScene()
    {
        MeshData mesh = CreateUnitCubeMesh();
        var scene = new Scene();

        // Visible: centered ahead of the camera.
        scene.Add(new SceneObject(mesh, TransformAt(new Vector3(0.0f, 0.0f, -10.0f))));
        // Culled: behind the camera.
        scene.Add(new SceneObject(mesh, TransformAt(new Vector3(0.0f, 0.0f, 10.0f))));
        // Culled: beyond the far plane.
        scene.Add(new SceneObject(mesh, TransformAt(new Vector3(0.0f, 0.0f, -200.0f))));
        // Culled: far outside the left plane.
        scene.Add(new SceneObject(mesh, TransformAt(new Vector3(-50.0f, 0.0f, -10.0f))));
        // Visible: the box crosses the right frustum plane.
        scene.Add(new SceneObject(mesh, TransformAt(new Vector3(6.6f, 0.0f, -10.0f))));
        // Visible: non-uniform negative scale mirrors the asymmetric bounds.
        scene.Add(new SceneObject(
            mesh,
            new Transform(
                new Vector3(0.0f, 0.0f, -20.0f),
                Quaternion.Identity,
                new Vector3(-2.0f, 1.0f, 1.0f))));
        // Visible: rotated and non-uniformly scaled ahead of the camera.
        scene.Add(new SceneObject(
            mesh,
            new Transform(
                new Vector3(0.0f, 0.0f, -30.0f),
                Quaternion.CreateFromYawPitchRoll(0.4f, 0.2f, 0.1f),
                new Vector3(1.5f, 0.75f, 1.25f))));

        Matrix4x4 view = Matrix4x4.CreateLookAt(
            Vector3.Zero,
            -Vector3.UnitZ,
            Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4.0f,
            1.6f,
            0.1f,
            100.0f);

        return (scene, Frustum.Create(view * projection));
    }

    private static Transform TransformAt(Vector3 position)
    {
        return new Transform(position, Quaternion.Identity, Vector3.One);
    }

    private static MeshData CreateUnitCubeMesh()
    {
        Vector3[] positions =
        {
            new(-1.0f, -1.0f, -1.0f),
            new(1.0f, -1.0f, -1.0f),
            new(1.0f, 1.0f, -1.0f),
            new(-1.0f, 1.0f, -1.0f),
            new(-1.0f, -1.0f, 1.0f),
            new(1.0f, -1.0f, 1.0f),
            new(1.0f, 1.0f, 1.0f),
            new(-1.0f, 1.0f, 1.0f),
        };

        uint[] indices =
        {
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            2, 3, 7, 2, 7, 6,
            1, 2, 6, 1, 6, 5,
            0, 4, 7, 0, 7, 3,
        };

        return new MeshData(
            positions,
            ReadOnlyMemory<Vector3>.Empty,
            ReadOnlyMemory<Vector2>.Empty,
            indices);
    }
}
