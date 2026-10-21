using System.Numerics;
using Nymeria.Core.Collision;
using Nymeria.Core.Culling;
using Nymeria.Core.Lod;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;
using Nymeria.Core.Streaming;

namespace Nymeria.Core.Tests;

public sealed class SceneCollisionTests
{
    private static readonly MeshData Mesh = CreateTriangleMesh();

    private static readonly BoundingBox LocalBox = new(
        new Vector3(-1.0f, -1.0f, -1.0f),
        new Vector3(1.0f, 1.0f, 1.0f));

    private static readonly BoundingBox SmallLocalBox = new(
        new Vector3(-0.5f, -0.5f, -0.5f),
        new Vector3(0.5f, 0.5f, 0.5f));

    [Fact]
    public void EmptySceneAndObjectsWithoutCollidersProduceNoPairs()
    {
        var scene = new Scene();
        var box = new SceneObject(Mesh);
        scene.Add(box);

        var pairs = new List<CollisionPair>();

        Assert.Equal(0, SceneCollision.AppendOverlapPairs(scene, pairs));
        Assert.Empty(pairs);
        Assert.False(SceneCollision.TryGetFirstOverlap(scene, box, out _));
        Assert.False(SceneCollision.Overlaps(scene, box));
        Assert.False(SceneCollision.TryGetWorldShape(box, out _));
    }

    [Fact]
    public void AppendOverlapPairsReportsEachPairOnceInInsertionOrder()
    {
        var scene = new Scene();
        SceneObject first = AddBox(scene, Vector3.Zero);
        SceneObject second = AddBox(scene, new Vector3(0.75f, 0.0f, 0.0f));
        SceneObject third = AddBox(scene, new Vector3(0.0f, 0.75f, 0.0f));
        var pairs = new List<CollisionPair>();

        Assert.Equal(3, SceneCollision.AppendOverlapPairs(scene, pairs));

        Assert.Collection(
            pairs,
            pair =>
            {
                Assert.Same(first, pair.First);
                Assert.Same(second, pair.Second);
            },
            pair =>
            {
                Assert.Same(first, pair.First);
                Assert.Same(third, pair.Second);
            },
            pair =>
            {
                Assert.Same(second, pair.First);
                Assert.Same(third, pair.Second);
            });
    }

    [Fact]
    public void AppendOverlapPairsSeparatesObjectsThatDoNotOverlap()
    {
        var scene = new Scene();
        AddBox(scene, Vector3.Zero);
        AddBox(scene, new Vector3(3.0f, 0.0f, 0.0f));
        var pairs = new List<CollisionPair>();

        Assert.Equal(0, SceneCollision.AppendOverlapPairs(scene, pairs));
        Assert.Empty(pairs);
    }

    [Fact]
    public void TryGetFirstOverlapSkipsTheTargetAndFollowsInsertionOrder()
    {
        var scene = new Scene();
        SceneObject first = AddBox(scene, Vector3.Zero);
        SceneObject target = AddBox(scene, new Vector3(0.5f, 0.0f, 0.0f));
        SceneObject third = AddBox(scene, new Vector3(1.0f, 0.0f, 0.0f));

        Assert.True(SceneCollision.TryGetFirstOverlap(scene, target, out SceneObject? other));
        Assert.Same(first, other);
        Assert.NotSame(target, other);

        scene.Remove(first);

        Assert.True(SceneCollision.TryGetFirstOverlap(scene, target, out other));
        Assert.Same(third, other);
    }

    [Fact]
    public void AppendOverlapsExcludesTargetAndPreservesDestinationContents()
    {
        var scene = new Scene();
        SceneObject first = AddBox(scene, Vector3.Zero);
        SceneObject target = AddBox(scene, new Vector3(0.5f, 0.0f, 0.0f));
        SceneObject third = AddBox(scene, new Vector3(1.0f, 0.0f, 0.0f));
        var destination = new List<SceneObject> { first };

        int appended = SceneCollision.AppendOverlaps(scene, target, destination);

        Assert.Equal(2, appended);
        Assert.Equal(new[] { first, first, third }, destination);
    }

    [Fact]
    public void ColliderIsOptInAndCanBeRemovedOrReplaced()
    {
        var scene = new Scene();
        SceneObject first = AddBox(scene, Vector3.Zero, withCollider: false);
        SceneObject second = AddBox(scene, Vector3.Zero, withCollider: false);
        var pairs = new List<CollisionPair>();

        Assert.Equal(0, SceneCollision.AppendOverlapPairs(scene, pairs));

        first.Collider = CollisionShape.FromBox(LocalBox);

        Assert.Equal(0, SceneCollision.AppendOverlapPairs(scene, pairs));

        second.Collider = CollisionShape.FromBox(LocalBox);

        Assert.Equal(1, SceneCollision.AppendOverlapPairs(scene, pairs));

        second.Collider = null;

        Assert.Equal(0, SceneCollision.AppendOverlapPairs(scene, pairs));

        second.Collider = default;

        Assert.Equal(0, SceneCollision.AppendOverlapPairs(scene, pairs));
    }

    [Fact]
    public void QueriesUseTheCurrentTransform()
    {
        var scene = new Scene();
        SceneObject first = AddBox(scene, Vector3.Zero);
        SceneObject second = AddBox(scene, new Vector3(1.5f, 0.0f, 0.0f));
        var pairs = new List<CollisionPair>();

        Assert.Equal(1, SceneCollision.AppendOverlapPairs(scene, pairs));

        second.Transform.Position = new Vector3(3.0f, 0.0f, 0.0f);

        Assert.Equal(0, SceneCollision.AppendOverlapPairs(scene, pairs));

        second.Transform.Position = new Vector3(0.75f, 0.0f, 0.0f);

        Assert.Equal(1, SceneCollision.AppendOverlapPairs(scene, pairs));
    }

    [Fact]
    public void RotatedBoxUsesConservativeWorldBounds()
    {
        var scene = new Scene();
        SceneObject first = AddBox(
            scene,
            Vector3.Zero,
            rotation: Quaternion.CreateFromYawPitchRoll(MathF.PI / 4.0f, 0.0f, 0.0f),
            collider: LocalBox);
        AddBox(
            scene,
            new Vector3(1.7f, 0.0f, 0.0f),
            collider: SmallLocalBox);
        var pairs = new List<CollisionPair>();

        // The rotated unit box reaches X = sqrt(2) = 1.414. The small box
        // begins at X = 1.2, so only the world-space rotation can overlap.
        Assert.Equal(1, SceneCollision.AppendOverlapPairs(scene, pairs));

        first.Transform.Rotation = Quaternion.Identity;

        Assert.Equal(0, SceneCollision.AppendOverlapPairs(scene, pairs));
    }

    [Fact]
    public void NonUniformBoxScaleExpandsWorldBoundsConservatively()
    {
        var scene = new Scene();
        SceneObject first = AddBox(scene, Vector3.Zero, collider: LocalBox);
        AddBox(
            scene,
            new Vector3(3.25f, 0.0f, 0.0f),
            collider: SmallLocalBox);
        var pairs = new List<CollisionPair>();

        Assert.Equal(0, SceneCollision.AppendOverlapPairs(scene, pairs));

        first.Transform.Scale = new Vector3(3.0f, 1.0f, 1.0f);

        Assert.Equal(1, SceneCollision.AppendOverlapPairs(scene, pairs));
    }

    [Fact]
    public void SphereColliderUsesLargestScaleAsConservativeRadius()
    {
        CollisionShape sphere = CollisionShape.FromSphere(
            new BoundingSphere(Vector3.Zero, 1.0f));
        var scene = new Scene();
        var first = new SceneObject(Mesh)
        {
            Collider = sphere,
        };
        var second = new SceneObject(
            Mesh,
            new Transform(
                new Vector3(3.5f, 0.0f, 0.0f),
                Quaternion.Identity,
                Vector3.One))
        {
            Collider = CollisionShape.FromSphere(
                new BoundingSphere(Vector3.Zero, 0.6f)),
        };
        scene.Add(first);
        scene.Add(second);
        var pairs = new List<CollisionPair>();

        // Scale (3, 1, 1) gives a conservative radius of 3, not the smallest
        // axis scale of 1. The centers are 3.5 apart and the radii sum to 3.6.
        first.Transform.Scale = new Vector3(3.0f, 1.0f, 1.0f);

        Assert.Equal(1, SceneCollision.AppendOverlapPairs(scene, pairs));
    }

    [Fact]
    public void CollisionIgnoresLodDistanceCullingAndPickability()
    {
        MeshData low = CreateTriangleMesh();
        var lod = new MeshLod(
            [
                new MeshLodLevel(Mesh, 0.0f),
                new MeshLodLevel(low, 1.0f),
            ],
            maximumRenderDistance: 0.5f);
        var scene = new Scene();
        var first = new SceneObject(
            Mesh,
            null,
            null,
            lod)
        {
            Collider = CollisionShape.FromBox(LocalBox),
            IsPickable = false,
        };
        var second = new SceneObject(Mesh)
        {
            Collider = CollisionShape.FromBox(LocalBox),
        };
        scene.Add(first);
        scene.Add(second);
        var pairs = new List<CollisionPair>();

        Assert.Equal(1, SceneCollision.AppendOverlapPairs(scene, pairs));
        Assert.True(SceneCollision.Overlaps(scene, first));
        Assert.True(SceneCollision.Overlaps(scene, second));
    }

    [Fact]
    public void CollisionDoesNotDependOnFrustumVisibility()
    {
        var scene = new Scene();
        SceneObject first = AddBox(scene, new Vector3(100.0f, 0.0f, 0.0f));
        AddBox(scene, new Vector3(100.5f, 0.0f, 0.0f));
        Matrix4x4 view = Matrix4x4.CreateLookAt(
            Vector3.Zero,
            -Vector3.UnitZ,
            Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4.0f,
            1.0f,
            0.1f,
            50.0f);
        Frustum frustum = Frustum.Create(view * projection);
        BoundingBox worldBounds = LocalBox.Transform(
            first.Transform.CreateModelMatrix());

        Assert.False(frustum.Intersects(worldBounds));
        Assert.Equal(1, SceneCollision.AppendOverlapPairs(
            scene,
            new List<CollisionPair>()));
    }

    [Fact]
    public void StreamingUnloadRemovesCollidersFromQueries()
    {
        var scene = new Scene();
        SceneObject fixedObject = AddBox(scene, new Vector3(5.0f, 0.0f, 0.0f));
        var streamedObject = new SceneObject(
            Mesh,
            new Transform(new Vector3(5.0f, 0.0f, 0.0f), Quaternion.Identity, Vector3.One))
        {
            Collider = CollisionShape.FromBox(LocalBox),
        };
        var chunk = new SceneChunkDefinition(
            "collision",
            new BoundingBox(
                new Vector3(4.0f, -2.0f, 4.0f),
                new Vector3(6.0f, 2.0f, 6.0f)),
            () => [streamedObject]);
        using var streamer = new SceneStreamer(
            scene,
            [chunk],
            new SceneStreamingConfiguration(10.0f, 20.0f));
        var pairs = new List<CollisionPair>();

        streamer.Update(new Vector3(5.0f, 0.0f, 0.0f), 0.0);

        Assert.True(scene.Contains(streamedObject));
        Assert.Equal(1, SceneCollision.AppendOverlapPairs(scene, pairs));
        Assert.True(SceneCollision.Overlaps(scene, fixedObject));

        SceneStreamingUpdateResult update = streamer.Update(
            new Vector3(100.0f, 0.0f, 0.0f),
            1.0);

        Assert.Equal(1, update.UnloadedChunks);
        Assert.False(scene.Contains(streamedObject));
        Assert.Equal(0, SceneCollision.AppendOverlapPairs(scene, pairs));
        Assert.False(SceneCollision.Overlaps(scene, fixedObject));

        // The removed object remains a safe managed reference, but it is not
        // current scene content and cannot be queried as a target.
        Assert.Throws<ArgumentException>(
            () => SceneCollision.Overlaps(scene, streamedObject));
    }

    [Fact]
    public void TryGetWorldShapeTransformsLocalCollider()
    {
        var sceneObject = new SceneObject(
            Mesh,
            new Transform(
                new Vector3(10.0f, -2.0f, 5.0f),
                Quaternion.Identity,
                new Vector3(2.0f, 3.0f, 4.0f)))
        {
            Collider = CollisionShape.FromBox(LocalBox),
        };

        Assert.True(SceneCollision.TryGetWorldShape(
            sceneObject,
            out CollisionShape world));
        Assert.True(world.TryGetBox(out BoundingBox box));

        Assert.Equal(new Vector3(8.0f, -5.0f, 1.0f), box.Min);
        Assert.Equal(new Vector3(12.0f, 1.0f, 9.0f), box.Max);
    }

    [Fact]
    public void QueriesRejectObjectsThatAreNotInTheScene()
    {
        var scene = new Scene();
        var outside = AddBox(scene, Vector3.Zero);
        scene.Remove(outside);
        var pairs = new List<CollisionPair>();

        Assert.Throws<ArgumentException>(
            () => SceneCollision.TryGetFirstOverlap(scene, outside, out _));
        Assert.Throws<ArgumentException>(
            () => SceneCollision.Overlaps(scene, outside));
        Assert.Throws<ArgumentException>(
            () => SceneCollision.AppendOverlaps(scene, outside, new List<SceneObject>()));
    }

    [Fact]
    public void QueriesRejectNullArguments()
    {
        var scene = new Scene();
        SceneObject sceneObject = AddBox(scene, Vector3.Zero);

        Assert.Throws<ArgumentNullException>(
            () => SceneCollision.TryGetWorldShape(null!, out _));
        Assert.Throws<ArgumentNullException>(
            () => SceneCollision.TryGetFirstOverlap(null!, sceneObject, out _));
        Assert.Throws<ArgumentNullException>(
            () => SceneCollision.TryGetFirstOverlap(scene, null!, out _));
        Assert.Throws<ArgumentNullException>(
            () => SceneCollision.Overlaps(null!, sceneObject));
        Assert.Throws<ArgumentNullException>(
            () => SceneCollision.AppendOverlaps(scene, sceneObject, null!));
        Assert.Throws<ArgumentNullException>(
            () => SceneCollision.AppendOverlapPairs(null!, new List<CollisionPair>()));
        Assert.Throws<ArgumentNullException>(
            () => SceneCollision.AppendOverlapPairs(scene, null!));
    }

    private static SceneObject AddBox(
        Scene scene,
        Vector3 position,
        Vector3? scale = null,
        Quaternion? rotation = null,
        BoundingBox? collider = null,
        bool withCollider = true)
    {
        var sceneObject = new SceneObject(
            Mesh,
            new Transform(
                position,
                rotation ?? Quaternion.Identity,
                scale ?? Vector3.One));

        if (withCollider)
        {
            sceneObject.Collider = CollisionShape.FromBox(collider ?? LocalBox);
        }

        scene.Add(sceneObject);
        return sceneObject;
    }

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
}
