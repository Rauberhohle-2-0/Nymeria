using System.Numerics;
using Nymeria.Core.Lod;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;

namespace Nymeria.Core.Tests;

public sealed class MeshLodTests
{
    [Fact]
    public void Constructor_StoresLevelsBoundsAndDefaults()
    {
        MeshData low = CreatePointMesh(new Vector3(10.0f, 2.0f, 3.0f));
        MeshData medium = CreatePointMesh(new Vector3(2.0f, -4.0f, 8.0f));
        MeshData high = CreatePointMesh(Vector3.Zero);
        var lod = new MeshLod(
            [
                new MeshLodLevel(high, 0.0f),
                new MeshLodLevel(medium, 10.0f),
                new MeshLodLevel(low, 30.0f),
            ]);

        Assert.Equal(3, lod.Levels.Count);
        Assert.Same(high, lod.Levels[0].Mesh);
        Assert.Same(medium, lod.Levels[1].Mesh);
        Assert.Same(low, lod.Levels[2].Mesh);
        Assert.Equal(new Vector3(0.0f, -4.0f, 0.0f), lod.Bounds.Min);
        Assert.Equal(new Vector3(10.0f, 2.0f, 8.0f), lod.Bounds.Max);
        Assert.Null(lod.MaximumRenderDistance);
        Assert.Equal(MeshLod.DefaultHysteresis, lod.Hysteresis);
    }

    [Fact]
    public void Constructor_RejectsInvalidLevelLists()
    {
        MeshData mesh = CreatePointMesh(Vector3.Zero);
        var validLevel = new MeshLodLevel(mesh, 0.0f);

        Assert.Throws<ArgumentNullException>(() => new MeshLod(null!));
        Assert.Throws<ArgumentException>(() => new MeshLod([]));
        Assert.Throws<ArgumentException>(() => new MeshLod([validLevel, null!]));

        MeshLodLevel[] tooMany = Enumerable
            .Range(0, MeshLod.MaxLevels + 1)
            .Select(index => new MeshLodLevel(mesh, index))
            .ToArray();
        Assert.Throws<ArgumentException>(() => new MeshLod(tooMany));
    }

    [Fact]
    public void Constructor_RequiresZeroFirstAndStrictlyIncreasingDistances()
    {
        MeshData mesh = CreatePointMesh(Vector3.Zero);

        Assert.Throws<ArgumentException>(() => new MeshLod(
            [new MeshLodLevel(mesh, 1.0f)]));
        Assert.Throws<ArgumentException>(() => new MeshLod(
            [
                new MeshLodLevel(mesh, 0.0f),
                new MeshLodLevel(mesh, 0.0f),
            ]));
        Assert.Throws<ArgumentException>(() => new MeshLod(
            [
                new MeshLodLevel(mesh, 0.0f),
                new MeshLodLevel(mesh, 20.0f),
                new MeshLodLevel(mesh, 10.0f),
            ]));
    }

    [Fact]
    public void MeshLodLevel_RejectsNullMeshAndInvalidDistances()
    {
        MeshData mesh = CreatePointMesh(Vector3.Zero);

        Assert.Throws<ArgumentNullException>(() => new MeshLodLevel(null!, 0.0f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MeshLodLevel(mesh, -1.0f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MeshLodLevel(mesh, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MeshLodLevel(mesh, float.PositiveInfinity));
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(-1.0f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Constructor_RejectsInvalidMaximumRenderDistance(float maximum)
    {
        MeshData mesh = CreatePointMesh(Vector3.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() => new MeshLod(
            [new MeshLodLevel(mesh, 0.0f)],
            maximumRenderDistance: maximum));
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Constructor_RejectsInvalidHysteresis(float hysteresis)
    {
        MeshData mesh = CreatePointMesh(Vector3.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() => new MeshLod(
            [new MeshLodLevel(mesh, 0.0f)],
            hysteresis: hysteresis));
    }

    [Fact]
    public void Constructor_AcceptsZeroHysteresisAndIncludesMaximumBoundary()
    {
        MeshData mesh = CreatePointMesh(Vector3.Zero);
        var lod = new MeshLod(
            [new MeshLodLevel(mesh, 0.0f)],
            maximumRenderDistance: 12.0f,
            hysteresis: 0.0f);

        Assert.Equal(0.0f, lod.Hysteresis);
        Assert.Equal(12.0f, lod.MaximumRenderDistance);
        Assert.False(lod.IsBeyondMaximumRenderDistance(11.999f));
        Assert.False(lod.IsBeyondMaximumRenderDistance(12.0f));
        Assert.True(lod.IsBeyondMaximumRenderDistance(12.001f));
    }

    [Fact]
    public void IsBeyondMaximumRenderDistance_WithoutMaximum_IsAlwaysFalse()
    {
        MeshLod lod = CreateThreeLevelLod();

        Assert.False(lod.IsBeyondMaximumRenderDistance(0.0f));
        Assert.False(lod.IsBeyondMaximumRenderDistance(float.MaxValue));
    }

    [Fact]
    public void SelectLevel_InitialBoundariesAreInclusive()
    {
        MeshLod lod = CreateThreeLevelLod(hysteresis: 2.0f);

        Assert.Equal(0, lod.SelectLevel(0.0f));
        Assert.Equal(0, lod.SelectLevel(9.999f));
        Assert.Equal(1, lod.SelectLevel(10.0f));
        Assert.Equal(1, lod.SelectLevel(29.999f));
        Assert.Equal(2, lod.SelectLevel(30.0f));
        Assert.Equal(2, lod.SelectLevel(float.MaxValue));
    }

    [Fact]
    public void SelectLevel_WithHysteresis_HoldsInsideTheBand()
    {
        MeshLod lod = CreateThreeLevelLod(hysteresis: 2.0f);

        Assert.Equal(0, lod.SelectLevel(12.0f, currentLevel: 0));
        Assert.Equal(1, lod.SelectLevel(12.001f, currentLevel: 0));
        Assert.Equal(1, lod.SelectLevel(8.0f, currentLevel: 1));
        Assert.Equal(0, lod.SelectLevel(7.999f, currentLevel: 1));
        Assert.Equal(2, lod.SelectLevel(28.0f, currentLevel: 2));
        Assert.Equal(1, lod.SelectLevel(27.999f, currentLevel: 2));
    }

    [Fact]
    public void SelectLevel_TeleportsAcrossMultipleLevelsDeterministically()
    {
        MeshLod lod = CreateThreeLevelLod(hysteresis: 2.0f);

        Assert.Equal(2, lod.SelectLevel(500.0f, currentLevel: 0));
        Assert.Equal(0, lod.SelectLevel(0.0f, currentLevel: 2));
        Assert.Equal(1, lod.SelectLevel(20.0f, currentLevel: 2));
    }

    [Fact]
    public void SelectLevel_CloseThresholdsDoNotOscillate()
    {
        MeshData mesh = CreatePointMesh(Vector3.Zero);
        var lod = new MeshLod(
            [
                new MeshLodLevel(mesh, 0.0f),
                new MeshLodLevel(mesh, 1.0f),
                new MeshLodLevel(mesh, 2.0f),
            ],
            hysteresis: 5.0f);

        // The dead band is wider than the threshold spacing, so a single call
        // never advances and then retreats across the same boundary.
        Assert.Equal(0, lod.SelectLevel(3.0f, currentLevel: 0));
        Assert.Equal(2, lod.SelectLevel(3.0f, currentLevel: 2));
        Assert.Equal(1, lod.SelectLevel(3.0f, currentLevel: 1));
    }

    [Fact]
    public void SelectLevel_InvalidDistanceOrStateFailsSafe()
    {
        MeshLod lod = CreateThreeLevelLod();

        Assert.Equal(0, lod.SelectLevel(float.NaN));
        Assert.Equal(0, lod.SelectLevel(-5.0f));
        Assert.Equal(0, lod.SelectLevel(float.NaN, currentLevel: 2));
        Assert.Equal(2, lod.SelectLevel(100.0f, currentLevel: -1));
        Assert.Equal(2, lod.SelectLevel(100.0f, currentLevel: 99));
    }

    [Fact]
    public void LodLevelUsage_IndexesCountsAndRejectsOutOfRangeLevels()
    {
        var usage = new LodLevelUsage([3, 0, 4]);

        Assert.Equal(3, usage[0]);
        Assert.Equal(0, usage[1]);
        Assert.Equal(4, usage[2]);
        Assert.Equal(7, usage.Total);
        Assert.Equal(0, default(LodLevelUsage).Total);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = usage[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = usage[MeshLod.MaxLevels]);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LodLevelUsage(new int[MeshLod.MaxLevels + 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LodLevelUsage(new[] { 1, -1 }));
    }

    [Fact]
    public void SceneObject_DefaultsToNoLod()
    {
        MeshData mesh = CreatePointMesh(Vector3.Zero);
        var sceneObject = new SceneObject(mesh);

        Assert.Null(sceneObject.Lod);
    }

    [Fact]
    public void SceneObject_RequiresHighestDetailLevelToBeItsPrimaryMesh()
    {
        MeshData primary = CreatePointMesh(Vector3.Zero);
        MeshData other = CreatePointMesh(new Vector3(1.0f, 0.0f, 0.0f));
        var lod = new MeshLod(
            [
                new MeshLodLevel(other, 0.0f),
                new MeshLodLevel(primary, 10.0f),
            ]);

        Assert.Throws<ArgumentException>(() => new SceneObject(primary, lod: lod));

        var sceneObject = new SceneObject(primary);
        Assert.Throws<ArgumentException>(() => sceneObject.Lod = lod);
        Assert.Null(sceneObject.Lod);
    }

    [Fact]
    public void SceneObject_SharingConfigurationDoesNotCopyMeshes()
    {
        MeshData high = CreatePointMesh(Vector3.Zero);
        MeshData low = CreatePointMesh(Vector3.Zero);
        MeshLod lod = CreateLod(high, low);
        var first = new SceneObject(high, lod: lod);
        var second = new SceneObject(high, lod: lod);

        Assert.Same(lod, first.Lod);
        Assert.Same(lod, second.Lod);
        Assert.Same(first.Mesh, second.Mesh);
        Assert.Same(high, lod.Levels[0].Mesh);
        Assert.Same(low, lod.Levels[1].Mesh);
    }

    private static MeshLod CreateThreeLevelLod(float hysteresis = 0.25f)
    {
        MeshData mesh = CreatePointMesh(Vector3.Zero);

        return new MeshLod(
            [
                new MeshLodLevel(mesh, 0.0f),
                new MeshLodLevel(mesh, 10.0f),
                new MeshLodLevel(mesh, 30.0f),
            ],
            hysteresis: hysteresis);
    }

    private static MeshLod CreateLod(MeshData high, MeshData low)
    {
        return new MeshLod(
            [
                new MeshLodLevel(high, 0.0f),
                new MeshLodLevel(low, 10.0f),
            ]);
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
}
