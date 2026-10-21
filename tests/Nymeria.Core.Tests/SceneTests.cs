using System.Numerics;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;

namespace Nymeria.Core.Tests;

public sealed class SceneTests
{
    private static MeshData CreateTriangleMesh()
    {
        return new MeshData(
            new[]
            {
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(1.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 1.0f, 0.0f),
            },
            ReadOnlyMemory<Vector3>.Empty,
            ReadOnlyMemory<Vector2>.Empty,
            new uint[] { 0, 1, 2 });
    }

    [Fact]
    public void EmptyScene_HasNoObjects()
    {
        var scene = new Scene();

        Assert.Equal(0, scene.Count);
        Assert.Empty(scene.Objects);
    }

    [Fact]
    public void Add_PreservesInsertionOrder()
    {
        MeshData mesh = CreateTriangleMesh();
        var first = new SceneObject(mesh);
        var second = new SceneObject(mesh);
        var scene = new Scene();

        scene.Add(first);
        scene.Add(second);

        Assert.Equal(2, scene.Count);
        Assert.Equal(new[] { first, second }, scene.Objects);
        Assert.True(scene.Contains(first));
        Assert.True(scene.Contains(second));
    }

    [Fact]
    public void Remove_PreservesRemainingOrder()
    {
        MeshData mesh = CreateTriangleMesh();
        var first = new SceneObject(mesh);
        var middle = new SceneObject(mesh);
        var last = new SceneObject(mesh);
        var scene = new Scene();
        scene.Add(first);
        scene.Add(middle);
        scene.Add(last);

        Assert.True(scene.Remove(middle));

        Assert.Equal(new[] { first, last }, scene.Objects);
        Assert.False(scene.Contains(middle));
    }

    [Fact]
    public void Remove_ReturnsFalseForMissingObject()
    {
        MeshData mesh = CreateTriangleMesh();
        var scene = new Scene();
        var objectInScene = new SceneObject(mesh);
        var objectNotInScene = new SceneObject(mesh);
        scene.Add(objectInScene);

        Assert.False(scene.Remove(objectNotInScene));
        Assert.Equal(new[] { objectInScene }, scene.Objects);
    }

    [Fact]
    public void Add_RejectsDuplicateObject()
    {
        var scene = new Scene();
        var sceneObject = new SceneObject(CreateTriangleMesh());
        scene.Add(sceneObject);

        Assert.Throws<InvalidOperationException>(() => scene.Add(sceneObject));
        Assert.Single(scene.Objects);
    }

    [Fact]
    public void Add_RejectsNull()
    {
        var scene = new Scene();

        Assert.Throws<ArgumentNullException>(() => scene.Add(null!));
    }

    [Fact]
    public void Remove_RejectsNull()
    {
        var scene = new Scene();

        Assert.Throws<ArgumentNullException>(() => scene.Remove(null!));
    }

    [Fact]
    public void Clear_RemovesAllObjects()
    {
        MeshData mesh = CreateTriangleMesh();
        var scene = new Scene();
        scene.Add(new SceneObject(mesh));
        scene.Add(new SceneObject(mesh));

        scene.Clear();

        Assert.Equal(0, scene.Count);
        Assert.Empty(scene.Objects);
    }

    [Fact]
    public void RemovedObject_CanBeAddedAgain()
    {
        MeshData mesh = CreateTriangleMesh();
        var scene = new Scene();
        var sceneObject = new SceneObject(mesh);
        scene.Add(sceneObject);
        scene.Remove(sceneObject);

        scene.Add(sceneObject);

        Assert.Same(sceneObject, Assert.Single(scene.Objects));
    }

    [Fact]
    public void ObjectLimit_IsEnforcedBeforeGrowth()
    {
        MeshData mesh = CreateTriangleMesh();
        var scene = new Scene(maxObjectCount: 2);
        scene.Add(new SceneObject(mesh));
        scene.Add(new SceneObject(mesh));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => scene.Add(new SceneObject(mesh)));

        Assert.Contains("2", exception.Message);
        Assert.Equal(2, scene.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(Scene.MaxObjectCount + 1)]
    public void Constructor_RejectsUnsupportedObjectLimit(int maxObjectCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Scene(maxObjectCount));
    }

    [Fact]
    public void MultipleObjects_CanShareOneMesh()
    {
        MeshData mesh = CreateTriangleMesh();
        var first = new SceneObject(mesh);
        var second = new SceneObject(mesh);
        var scene = new Scene();
        scene.Add(first);
        scene.Add(second);

        Assert.Same(mesh, first.Mesh);
        Assert.Same(mesh, second.Mesh);
        Assert.Same(first.Mesh, second.Mesh);

        scene.Remove(first);

        Assert.Same(mesh, second.Mesh);
        Assert.Equal(3, mesh.Positions.Length);
    }

    [Fact]
    public void SceneObject_RejectsNullMesh()
    {
        Assert.Throws<ArgumentNullException>(() => new SceneObject(null!));
    }

    [Fact]
    public void SceneObject_CopiesInitialTransform()
    {
        MeshData mesh = CreateTriangleMesh();
        var source = new Transform(
            new Vector3(1.0f, 2.0f, 3.0f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f),
            new Vector3(2.0f, 2.0f, 2.0f));

        var sceneObject = new SceneObject(mesh, source);

        source.Position = new Vector3(9.0f, 9.0f, 9.0f);
        source.Scale = Vector3.One;

        Assert.Equal(new Vector3(1.0f, 2.0f, 3.0f), sceneObject.Transform.Position);
        Assert.Equal(new Vector3(2.0f, 2.0f, 2.0f), sceneObject.Transform.Scale);
        Assert.NotSame(source, sceneObject.Transform);
    }

    [Fact]
    public void SceneObject_UsesIdentityTransformWhenNoneIsSupplied()
    {
        var sceneObject = new SceneObject(CreateTriangleMesh());

        Assert.Equal(Vector3.Zero, sceneObject.Transform.Position);
        Assert.Equal(Quaternion.Identity, sceneObject.Transform.Rotation);
        Assert.Equal(Vector3.One, sceneObject.Transform.Scale);
    }

    [Fact]
    public void SceneObject_UsesDefaultMaterialWhenNoneIsSupplied()
    {
        var sceneObject = new SceneObject(CreateTriangleMesh());

        Assert.Same(Material.Default, sceneObject.Material);
    }

    [Fact]
    public void SceneObject_ReferencesSharedMaterialWithoutCopying()
    {
        var texture = new TextureData(1, 1, new byte[] { 1, 2, 3, 255 });
        var material = new Material(new Vector3(0.2f, 0.4f, 0.6f), texture, "shared");
        MeshData mesh = CreateTriangleMesh();
        var first = new SceneObject(mesh, material: material);
        var second = new SceneObject(mesh, material: material);

        Assert.Same(material, first.Material);
        Assert.Same(material, second.Material);
        Assert.Same(texture, first.Material.BaseColorTexture);
    }

    [Fact]
    public void SceneObject_CanReplaceMaterialAndRejectsNull()
    {
        var sceneObject = new SceneObject(CreateTriangleMesh());
        var material = new Material(new Vector3(0.1f, 0.2f, 0.3f));

        sceneObject.Material = material;

        Assert.Same(material, sceneObject.Material);
        Assert.Throws<ArgumentNullException>(() => sceneObject.Material = null!);
        Assert.Same(material, sceneObject.Material);
    }
}
