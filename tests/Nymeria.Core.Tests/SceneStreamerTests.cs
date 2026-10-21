using System.Numerics;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;
using Nymeria.Core.Streaming;

namespace Nymeria.Core.Tests;

public sealed class SceneStreamerTests
{
    [Fact]
    public void Update_LoadsChunkAtInclusiveLoadDistance()
    {
        MeshData mesh = CreateTriangleMesh();
        var scene = new Scene();
        SceneObject content = new(mesh);
        SceneChunkDefinition chunk = PointChunk("near", 10.0f, () => [content]);
        var streamer = new SceneStreamer(
            scene,
            [chunk],
            new SceneStreamingConfiguration(10.0f, 20.0f));

        SceneStreamingUpdateResult result = streamer.Update(Vector3.Zero, 0.0);

        Assert.Equal(1, result.LoadedChunks);
        Assert.Equal(0, result.UnloadedChunks);
        Assert.Single(scene.Objects);
        Assert.Same(content, scene.Objects[0]);
        Assert.Equal(SceneChunkState.Loaded, streamer.GetState("near"));
    }

    [Fact]
    public void Update_DoesNotLoadChunkBeyondInclusiveLoadDistance()
    {
        var scene = new Scene();
        SceneChunkDefinition chunk = PointChunk(
            "far",
            10.001f,
            () => [new SceneObject(CreateTriangleMesh())]);
        var streamer = new SceneStreamer(
            scene,
            [chunk],
            new SceneStreamingConfiguration(10.0f, 20.0f));

        SceneStreamingUpdateResult result = streamer.Update(Vector3.Zero, 0.0);

        Assert.Equal(0, result.LoadedChunks);
        Assert.Empty(scene.Objects);
        Assert.Equal(SceneChunkState.Unloaded, streamer.GetState("far"));
    }

    [Fact]
    public void Update_KeepsChunkLoadedThroughHysteresisBand()
    {
        var scene = new Scene();
        SceneChunkDefinition chunk = PointChunk(
            "band",
            10.0f,
            () => [new SceneObject(CreateTriangleMesh())]);
        var streamer = new SceneStreamer(
            scene,
            [chunk],
            new SceneStreamingConfiguration(10.0f, 15.0f));

        Assert.Equal(1, streamer.Update(Vector3.Zero, 0.0).LoadedChunks);
        Assert.Equal(0, streamer.Update(new Vector3(0.0f, 0.0f, 2.0f), 1.0).UnloadedChunks);
        Assert.Equal(0, streamer.Update(new Vector3(0.0f, 0.0f, 5.0f), 1.0).UnloadedChunks);
        Assert.Equal(
            SceneChunkState.Loaded,
            streamer.GetState("band"));

        SceneStreamingUpdateResult beyond = streamer.Update(
            new Vector3(0.0f, 0.0f, 5.001f),
            1.0);

        Assert.Equal(1, beyond.UnloadedChunks);
        Assert.Empty(scene.Objects);
        Assert.Equal(SceneChunkState.Unloaded, streamer.GetState("band"));
    }

    [Fact]
    public void Update_UsesBoundsRatherThanChunkOrigin()
    {
        var scene = new Scene();
        var bounds = new BoundingBox(
            new Vector3(100.0f, -1.0f, -1.0f),
            new Vector3(101.0f, 1.0f, 1.0f));
        var chunk = new SceneChunkDefinition(
            "large",
            bounds,
            () => [new SceneObject(CreateTriangleMesh())]);
        var streamer = new SceneStreamer(
            scene,
            [chunk],
            new SceneStreamingConfiguration(100.5f, 300.0f));

        SceneStreamingUpdateResult result = streamer.Update(Vector3.Zero, 0.0);

        Assert.Equal(1, result.LoadedChunks);
        Assert.Single(scene.Objects);
        Assert.Equal(100.0f, chunk.DistanceTo(Vector3.Zero), precision: 4);
    }

    [Fact]
    public void Update_TeleportUnloadsDistantChunksAndLoadsNewOnes()
    {
        MeshData mesh = CreateTriangleMesh();
        var scene = new Scene();
        SceneObject first = new(mesh);
        SceneObject second = new(mesh);
        SceneChunkDefinition near = new(
            "near",
            PointBounds(0.0f),
            () => [first]);
        SceneChunkDefinition far = new(
            "far",
            PointBounds(100.0f),
            () => [second]);
        var streamer = new SceneStreamer(
            scene,
            [near, far],
            new SceneStreamingConfiguration(5.0f, 20.0f));

        streamer.Update(Vector3.Zero, 0.0);
        Assert.Same(first, Assert.Single(scene.Objects));

        SceneStreamingUpdateResult result = streamer.Update(
            new Vector3(0.0f, 0.0f, -100.0f),
            0.1);

        Assert.Equal(1, result.UnloadedChunks);
        Assert.Equal(1, result.LoadedChunks);
        Assert.Same(second, Assert.Single(scene.Objects));
        Assert.Equal(SceneChunkState.Unloaded, streamer.GetState("near"));
        Assert.Equal(SceneChunkState.Loaded, streamer.GetState("far"));
    }

    [Fact]
    public void Update_RespectsLoadBudgetAndPrefersNearestChunks()
    {
        MeshData mesh = CreateTriangleMesh();
        var scene = new Scene();
        SceneObject closest = new(mesh);
        SceneObject middle = new(mesh);
        SceneObject farthest = new(mesh);
        SceneChunkDefinition a = new("a", PointBounds(5.0f), () => [closest]);
        SceneChunkDefinition b = new("b", PointBounds(10.0f), () => [middle]);
        SceneChunkDefinition c = new("c", PointBounds(15.0f), () => [farthest]);
        var streamer = new SceneStreamer(
            scene,
            [c, b, a],
            new SceneStreamingConfiguration(
                loadDistance: 20.0f,
                unloadDistance: 40.0f,
                maxLoadsPerUpdate: 2));

        SceneStreamingUpdateResult first = streamer.Update(Vector3.Zero, 0.0);

        Assert.Equal(2, first.LoadedChunks);
        Assert.Equal([closest, middle], scene.Objects);
        Assert.Equal(SceneChunkState.Unloaded, streamer.GetState("c"));

        SceneStreamingUpdateResult second = streamer.Update(Vector3.Zero, 1.0);

        Assert.Equal(1, second.LoadedChunks);
        Assert.Equal([closest, middle, farthest], scene.Objects);
        Assert.Equal(3, streamer.Statistics.LoadedChunks);
    }

    [Fact]
    public void Update_EqualDistancesLoadInIdentifierOrder()
    {
        var scene = new Scene();
        var events = new List<string>();
        SceneChunkDefinition later = new(
            "z-last",
            PointBounds(10.0f),
            () =>
            {
                events.Add("z-last");
                return [new SceneObject(CreateTriangleMesh())];
            });
        SceneChunkDefinition earlier = new(
            "a-first",
            PointBounds(10.0f),
            () =>
            {
                events.Add("a-first");
                return [new SceneObject(CreateTriangleMesh())];
            });
        var streamer = new SceneStreamer(
            scene,
            [later, earlier],
            new SceneStreamingConfiguration(
                loadDistance: 20.0f,
                unloadDistance: 40.0f,
                maxLoadsPerUpdate: 1));

        streamer.Update(Vector3.Zero, 0.0);

        Assert.Equal(["a-first"], events);
    }

    [Fact]
    public void FailedLoad_IsIsolatedAndRetriedAfterDelay()
    {
        var scene = new Scene();
        var goodObject = new SceneObject(CreateTriangleMesh());
        int attempts = 0;
        SceneChunkDefinition chunk = new(
            "retry",
            PointBounds(10.0f),
            () =>
            {
                attempts++;

                if (attempts == 1)
                {
                    throw new InvalidOperationException("missing asset");
                }

                return [goodObject];
            });
        var streamer = new SceneStreamer(
            scene,
            [chunk],
            new SceneStreamingConfiguration(
                loadDistance: 20.0f,
                unloadDistance: 40.0f,
                retryDelaySeconds: 5.0));

        SceneStreamingUpdateResult failed = streamer.Update(Vector3.Zero, 0.0);

        Assert.Equal(1, failed.FailedLoads);
        Assert.Equal(SceneChunkState.Failed, streamer.GetState("retry"));
        Assert.Contains("missing asset", streamer.GetLastError("retry"));
        Assert.Empty(scene.Objects);
        Assert.Equal(1, streamer.Statistics.LoadFailures);

        SceneStreamingUpdateResult cooldown = streamer.Update(
            new Vector3(0.0f, 0.0f, 0.0f),
            4.9);

        Assert.Equal(0, cooldown.FailedLoads);
        Assert.Equal(1, attempts);
        Assert.Equal(SceneChunkState.Failed, streamer.GetState("retry"));

        SceneStreamingUpdateResult retried = streamer.Update(Vector3.Zero, 0.1);

        Assert.Equal(1, retried.LoadedChunks);
        Assert.Equal(2, attempts);
        Assert.Same(goodObject, Assert.Single(scene.Objects));
        Assert.Equal(SceneChunkState.Loaded, streamer.GetState("retry"));
        Assert.Null(streamer.GetLastError("retry"));
    }

    [Fact]
    public void FailedPartialLoad_RollsBackAndReleasesContent()
    {
        MeshData mesh = CreateTriangleMesh();
        var scene = new Scene(maxObjectCount: 1);
        SceneObject first = new(mesh);
        SceneObject second = new(mesh);
        IReadOnlyList<SceneObject>? released = null;
        SceneChunkDefinition chunk = new(
            "too-large",
            PointBounds(0.0f),
            () => [first, second],
            content => released = content);
        var streamer = new SceneStreamer(
            scene,
            [chunk],
            new SceneStreamingConfiguration(5.0f, 20.0f));

        SceneStreamingUpdateResult result = streamer.Update(Vector3.Zero, 0.0);

        Assert.Equal(1, result.FailedLoads);
        Assert.Empty(scene.Objects);
        Assert.Equal(SceneChunkState.Failed, streamer.GetState("too-large"));
        Assert.NotNull(released);
        Assert.Equal([first, second], released);
    }

    [Fact]
    public void DuplicateObjectAcrossChunks_IsRejectedWithoutDuplicatingSceneEntry()
    {
        var scene = new Scene();
        SceneObject sharedObject = new(CreateTriangleMesh());
        var released = new List<IReadOnlyList<SceneObject>>();
        SceneChunkDefinition first = new(
            "a",
            PointBounds(5.0f),
            () => [sharedObject]);
        SceneChunkDefinition second = new(
            "b",
            PointBounds(10.0f),
            () => [sharedObject],
            released.Add);
        var streamer = new SceneStreamer(
            scene,
            [first, second],
            new SceneStreamingConfiguration(
                loadDistance: 20.0f,
                unloadDistance: 40.0f,
                maxLoadsPerUpdate: 2));

        SceneStreamingUpdateResult result = streamer.Update(Vector3.Zero, 0.0);

        Assert.Equal(1, result.LoadedChunks);
        Assert.Equal(1, result.FailedLoads);
        Assert.Same(sharedObject, Assert.Single(scene.Objects));
        Assert.Equal(SceneChunkState.Loaded, streamer.GetState("a"));
        Assert.Equal(SceneChunkState.Failed, streamer.GetState("b"));
        Assert.Single(released);
        Assert.Same(sharedObject, released[0][0]);
    }

    [Fact]
    public void UnloadAll_RemovesOnlyStreamedObjectsAndReleasesEachChunkOnce()
    {
        MeshData mesh = CreateTriangleMesh();
        var scene = new Scene();
        var external = new SceneObject(mesh);
        scene.Add(external);
        int releases = 0;
        SceneChunkDefinition a = new(
            "a",
            PointBounds(1.0f),
            () => [new SceneObject(mesh)],
            _ => releases++);
        SceneChunkDefinition b = new(
            "b",
            PointBounds(2.0f),
            () => [new SceneObject(mesh)],
            _ => releases++);
        var streamer = new SceneStreamer(
            scene,
            [a, b],
            new SceneStreamingConfiguration(5.0f, 20.0f));

        streamer.Update(Vector3.Zero, 0.0);
        Assert.Equal(3, scene.Count);

        int unloaded = streamer.UnloadAll();
        int unloadedAgain = streamer.UnloadAll();

        Assert.Equal(2, unloaded);
        Assert.Equal(0, unloadedAgain);
        Assert.Same(external, Assert.Single(scene.Objects));
        Assert.Equal(2, releases);
        Assert.Equal(2, streamer.Statistics.Unloads);
        Assert.Equal(SceneChunkState.Unloaded, streamer.GetState("a"));
        Assert.Equal(SceneChunkState.Unloaded, streamer.GetState("b"));
    }

    [Fact]
    public void RepeatedLoadUnloadCycles_DoNotDuplicateObjectsOrReleaseTwice()
    {
        var scene = new Scene();
        int loadedObjects = 0;
        int releases = 0;
        SceneChunkDefinition chunk = new(
            "cycle",
            PointBounds(0.0f),
            () =>
            {
                loadedObjects++;
                return [new SceneObject(CreateTriangleMesh())];
            },
            _ => releases++);
        var streamer = new SceneStreamer(
            scene,
            [chunk],
            new SceneStreamingConfiguration(5.0f, 20.0f));

        for (int cycle = 0; cycle < 3; cycle++)
        {
            Assert.Equal(1, streamer.Update(Vector3.Zero, 0.0).LoadedChunks);
            Assert.Single(scene.Objects);
            Assert.Equal(
                1,
                streamer.Update(new Vector3(0.0f, 0.0f, 50.0f), 0.0).UnloadedChunks);
            Assert.Empty(scene.Objects);
        }

        Assert.Equal(3, loadedObjects);
        Assert.Equal(3, releases);
        Assert.Empty(scene.Objects);
        Assert.Equal(3, streamer.Statistics.SuccessfulLoads);
        Assert.Equal(3, streamer.Statistics.Unloads);
    }

    [Fact]
    public void Chunks_CanShareMeshReferencesWithoutCopyingThem()
    {
        MeshData sharedMesh = CreateTriangleMesh();
        var scene = new Scene();
        SceneChunkDefinition a = new(
            "a",
            PointBounds(1.0f),
            () => [new SceneObject(sharedMesh)]);
        SceneChunkDefinition b = new(
            "b",
            PointBounds(2.0f),
            () => [new SceneObject(sharedMesh)]);
        var streamer = new SceneStreamer(
            scene,
            [a, b],
            new SceneStreamingConfiguration(5.0f, 20.0f));

        streamer.Update(Vector3.Zero, 0.0);

        Assert.Equal(2, scene.Count);
        Assert.Same(sharedMesh, scene.Objects[0].Mesh);
        Assert.Same(sharedMesh, scene.Objects[1].Mesh);
        Assert.Same(scene.Objects[0].Mesh, scene.Objects[1].Mesh);
    }

    [Fact]
    public void FailedLoadDiagnostic_IsSingleLineAndBounded()
    {
        var scene = new Scene();
        SceneChunkDefinition chunk = new(
            "noisy",
            PointBounds(0.0f),
            static () => throw new InvalidOperationException(
                new string('x', 2000) + "\nnext line"));
        var streamer = new SceneStreamer(
            scene,
            [chunk],
            new SceneStreamingConfiguration(5.0f, 20.0f));

        SceneStreamingUpdateResult result = streamer.Update(Vector3.Zero, 0.0);

        string? error = streamer.GetLastError("noisy");
        Assert.NotNull(error);
        Assert.Equal(1, result.FailedLoads);
        Assert.DoesNotContain('\n', error);
        Assert.True(error.Length <= 512);
    }

    [Fact]
    public void NullContent_FailsChunkWithoutAddingObjects()
    {
        var scene = new Scene();
        SceneChunkDefinition chunk = new(
            "null-content",
            PointBounds(0.0f),
            static () => null!);
        var streamer = new SceneStreamer(
            scene,
            [chunk],
            new SceneStreamingConfiguration(5.0f, 20.0f));

        SceneStreamingUpdateResult result = streamer.Update(Vector3.Zero, 0.0);

        Assert.Equal(1, result.FailedLoads);
        Assert.Equal(SceneChunkState.Failed, streamer.GetState("null-content"));
        Assert.Contains("returned null", streamer.GetLastError("null-content"));
        Assert.Empty(scene.Objects);
    }

    [Fact]
    public void NullObjectInContent_RollsBackAndReleasesContent()
    {
        var scene = new Scene();
        SceneObject first = new(CreateTriangleMesh());
        int releases = 0;
        SceneChunkDefinition chunk = new(
            "null-object",
            PointBounds(0.0f),
            () => [first, null!],
            _ => releases++);
        var streamer = new SceneStreamer(
            scene,
            [chunk],
            new SceneStreamingConfiguration(5.0f, 20.0f));

        SceneStreamingUpdateResult result = streamer.Update(Vector3.Zero, 0.0);

        Assert.Equal(1, result.FailedLoads);
        Assert.Equal(SceneChunkState.Failed, streamer.GetState("null-object"));
        Assert.Empty(scene.Objects);
        Assert.Equal(1, releases);
    }

    [Fact]
    public void EmptyChunk_IsLoadedAndCounted()
    {
        var scene = new Scene();
        SceneChunkDefinition chunk = new(
            "empty",
            PointBounds(1.0f),
            static () => []);
        var streamer = new SceneStreamer(
            scene,
            [chunk],
            new SceneStreamingConfiguration(5.0f, 20.0f));

        SceneStreamingUpdateResult result = streamer.Update(Vector3.Zero, 0.0);

        Assert.Equal(1, result.LoadedChunks);
        Assert.Equal(SceneChunkState.Loaded, streamer.GetState("empty"));
        Assert.Empty(scene.Objects);
        Assert.Equal(0, streamer.Statistics.ActiveSceneObjects);
        Assert.Equal(1, streamer.Statistics.LoadedChunks);
    }

    [Fact]
    public void OverlappingChunkBounds_LoadIndependently()
    {
        var scene = new Scene();
        SceneChunkDefinition a = new(
            "a",
            PointBounds(0.0f),
            () => [new SceneObject(CreateTriangleMesh())]);
        SceneChunkDefinition b = new(
            "b",
            PointBounds(0.0f),
            () => [new SceneObject(CreateTriangleMesh())]);
        var streamer = new SceneStreamer(
            scene,
            [a, b],
            new SceneStreamingConfiguration(5.0f, 20.0f));

        SceneStreamingUpdateResult result = streamer.Update(Vector3.Zero, 0.0);

        Assert.Equal(2, result.LoadedChunks);
        Assert.Equal(2, scene.Count);
        Assert.Equal(SceneChunkState.Loaded, streamer.GetState("a"));
        Assert.Equal(SceneChunkState.Loaded, streamer.GetState("b"));
    }

    [Fact]
    public void ReleaseFailure_IsReportedAndDoesNotRetainSceneObjects()
    {
        var scene = new Scene();
        SceneChunkDefinition chunk = new(
            "release-fails",
            PointBounds(0.0f),
            () => [new SceneObject(CreateTriangleMesh())],
            _ => throw new InvalidOperationException("release failed"));
        var streamer = new SceneStreamer(
            scene,
            [chunk],
            new SceneStreamingConfiguration(5.0f, 20.0f));

        streamer.Update(Vector3.Zero, 0.0);
        SceneStreamingUpdateResult result = streamer.Update(
            new Vector3(0.0f, 0.0f, 50.0f),
            0.0);

        Assert.Equal(1, result.UnloadedChunks);
        Assert.Equal(1, result.ReleaseFailures);
        Assert.Contains("release failed", result.FirstError);
        Assert.Empty(scene.Objects);
        Assert.Equal(SceneChunkState.Unloaded, streamer.GetState("release-fails"));
        Assert.Equal(1, streamer.Statistics.ReleaseFailures);
    }

    [Fact]
    public void Update_RejectsInvalidFocusAndNegativeTimeWithoutMutation()
    {
        var scene = new Scene();
        SceneChunkDefinition chunk = PointChunk(
            "stable",
            0.0f,
            () => [new SceneObject(CreateTriangleMesh())]);
        var streamer = new SceneStreamer(
            scene,
            [chunk],
            new SceneStreamingConfiguration(5.0f, 20.0f));
        streamer.Update(Vector3.Zero, 0.0);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            streamer.Update(new Vector3(float.NaN, 0.0f, 0.0f), 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            streamer.Update(Vector3.Zero, -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            streamer.Update(Vector3.Zero, double.PositiveInfinity));

        Assert.Equal(SceneChunkState.Loaded, streamer.GetState("stable"));
        Assert.Single(scene.Objects);
    }

    [Fact]
    public void Statistics_RemainConsistentAcrossStates()
    {
        var scene = new Scene();
        SceneChunkDefinition loaded = new(
            "loaded",
            PointBounds(0.0f),
            () => [new SceneObject(CreateTriangleMesh())]);
        SceneChunkDefinition failed = new(
            "failed",
            PointBounds(1.0f),
            static () => throw new InvalidOperationException("bad"));
        SceneChunkDefinition unloaded = new(
            "unloaded",
            PointBounds(100.0f),
            () => [new SceneObject(CreateTriangleMesh())]);
        var streamer = new SceneStreamer(
            scene,
            [loaded, failed, unloaded],
            new SceneStreamingConfiguration(5.0f, 20.0f));

        streamer.Update(Vector3.Zero, 0.0);
        SceneStreamingStatistics statistics = streamer.Statistics;

        Assert.Equal(3, statistics.KnownChunks);
        Assert.Equal(1, statistics.LoadedChunks);
        Assert.Equal(1, statistics.FailedChunks);
        Assert.Equal(1, statistics.UnloadedChunks);
        Assert.Equal(1, statistics.ActiveSceneObjects);
        Assert.Equal(2, statistics.LoadAttempts);
        Assert.Equal(1, statistics.SuccessfulLoads);
        Assert.Equal(1, statistics.LoadFailures);
        Assert.Equal(0, statistics.Unloads);
        Assert.Equal(
            statistics.KnownChunks,
            statistics.LoadedChunks +
            statistics.FailedChunks +
            statistics.UnloadedChunks);
    }

    [Fact]
    public void Constructor_RejectsDuplicateIdentifiers()
    {
        SceneChunkDefinition first = PointChunk("same", 1.0f, static () => []);
        SceneChunkDefinition second = PointChunk("same", 2.0f, static () => []);

        Assert.Throws<ArgumentException>(() => new SceneStreamer(
            new Scene(),
            [first, second],
            new SceneStreamingConfiguration(1.0f, 2.0f)));
    }

    [Fact]
    public void Constructor_RejectsMoreThanTheMaximumChunkCount()
    {
        IEnumerable<SceneChunkDefinition> Chunks()
        {
            for (int index = 0; index <= SceneStreamer.MaxChunkCount; index++)
            {
                yield return PointChunk($"chunk-{index}", 1.0f, static () => []);
            }
        }

        Assert.Throws<ArgumentException>(() => new SceneStreamer(
            new Scene(),
            Chunks(),
            new SceneStreamingConfiguration(1.0f, 2.0f)));
    }

    [Fact]
    public void Constructor_RejectsNullChunkInSequence()
    {
        Assert.Throws<ArgumentNullException>(() => new SceneStreamer(
            new Scene(),
            [PointChunk("ok", 1.0f, static () => []), null!],
            new SceneStreamingConfiguration(1.0f, 2.0f)));
    }

    [Fact]
    public void Definition_RejectsMalformedIdentifiers()
    {
        Assert.Throws<ArgumentNullException>(() => new SceneChunkDefinition(
            null!,
            PointBounds(0.0f),
            static () => []));
        Assert.Throws<ArgumentException>(() => new SceneChunkDefinition(
            string.Empty,
            PointBounds(0.0f),
            static () => []));
        Assert.Throws<ArgumentException>(() => new SceneChunkDefinition(
            " ",
            PointBounds(0.0f),
            static () => []));
        Assert.Throws<ArgumentException>(() => new SceneChunkDefinition(
            " leading",
            PointBounds(0.0f),
            static () => []));
        Assert.Throws<ArgumentException>(() => new SceneChunkDefinition(
            "trailing ",
            PointBounds(0.0f),
            static () => []));
        Assert.Throws<ArgumentException>(() => new SceneChunkDefinition(
            "control\ncharacter",
            PointBounds(0.0f),
            static () => []));
        Assert.Throws<ArgumentException>(() => new SceneChunkDefinition(
            new string('a', SceneChunkDefinition.MaxIdLength + 1),
            PointBounds(0.0f),
            static () => []));
        Assert.Throws<ArgumentNullException>(() => new SceneChunkDefinition(
            "no-loader",
            PointBounds(0.0f),
            null!));
    }

    [Fact]
    public void Configuration_RejectsInvalidDistancesAndLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SceneStreamingConfiguration(-1.0f, 10.0f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SceneStreamingConfiguration(float.NaN, 10.0f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SceneStreamingConfiguration(5.0f, float.PositiveInfinity));
        Assert.Throws<ArgumentException>(() =>
            new SceneStreamingConfiguration(10.0f, 10.0f));
        Assert.Throws<ArgumentException>(() =>
            new SceneStreamingConfiguration(10.0f, 5.0f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SceneStreamingConfiguration(5.0f, 10.0f, maxLoadsPerUpdate: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SceneStreamingConfiguration(5.0f, 10.0f, retryDelaySeconds: -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SceneStreamingConfiguration(
                5.0f,
                10.0f,
                retryDelaySeconds: double.NaN));
    }

    [Fact]
    public void GetState_RejectsUnknownIdentifier()
    {
        var streamer = new SceneStreamer(
            new Scene(),
            [PointChunk("known", 1.0f, static () => [])],
            new SceneStreamingConfiguration(1.0f, 2.0f));

        Assert.Throws<KeyNotFoundException>(() => streamer.GetState("missing"));
        Assert.False(streamer.TryGetState("missing", out _));
        Assert.True(streamer.TryGetState("known", out SceneChunkState state));
        Assert.Equal(SceneChunkState.Unloaded, state);
    }

    [Fact]
    public void Dispose_UnloadsContentAndRejectsFurtherUpdates()
    {
        var scene = new Scene();
        SceneChunkDefinition chunk = PointChunk(
            "dispose",
            0.0f,
            () => [new SceneObject(CreateTriangleMesh())]);
        var streamer = new SceneStreamer(
            scene,
            [chunk],
            new SceneStreamingConfiguration(5.0f, 20.0f));
        streamer.Update(Vector3.Zero, 0.0);

        streamer.Dispose();
        streamer.Dispose();

        Assert.Empty(scene.Objects);
        Assert.Throws<ObjectDisposedException>(() =>
            streamer.Update(Vector3.Zero, 0.0));
        Assert.Throws<ObjectDisposedException>(() => streamer.UnloadAll());
    }

    private static BoundingBox PointBounds(float z)
    {
        var point = new Vector3(0.0f, 0.0f, -z);
        return new BoundingBox(point, point);
    }

    private static SceneChunkDefinition PointChunk(
        string id,
        float zDistance,
        Func<IReadOnlyList<SceneObject>> load,
        Action<IReadOnlyList<SceneObject>>? release = null)
    {
        return new SceneChunkDefinition(id, PointBounds(zDistance), load, release);
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
