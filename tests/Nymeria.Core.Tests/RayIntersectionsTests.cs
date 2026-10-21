using System.Numerics;
using Nymeria.Core.Models;
using Nymeria.Core.Raycasting;

namespace Nymeria.Core.Tests;

public sealed class RayIntersectionsTests
{
    private static readonly BoundingBox UnitBox = new(
        new Vector3(-1.0f),
        new Vector3(1.0f));

    [Fact]
    public void TryIntersectBox_ReportsEntryFaceDistanceAndNormal()
    {
        var ray = new Ray(new Vector3(0.0f, 0.0f, 5.0f), -Vector3.UnitZ);

        Assert.True(RayIntersections.TryIntersectBox(
            ray,
            UnitBox,
            float.PositiveInfinity,
            out float distance,
            out Vector3 normal));

        Assert.Equal(4.0f, distance, precision: 5);
        Assert.Equal(Vector3.UnitZ, normal);
        Assert.Equal(new Vector3(0.0f, 0.0f, 1.0f), ray.GetPoint(distance));
    }

    [Fact]
    public void TryIntersectBox_UsesNegativeDirectionEntryFace()
    {
        var ray = new Ray(new Vector3(0.0f, 0.0f, -5.0f), Vector3.UnitZ);

        Assert.True(RayIntersections.TryIntersectBox(
            ray,
            UnitBox,
            100.0f,
            out float distance,
            out Vector3 normal));

        Assert.Equal(4.0f, distance, precision: 5);
        Assert.Equal(-Vector3.UnitZ, normal);
    }

    [Fact]
    public void TryIntersectBox_MissesBoxBesideTheRay()
    {
        var ray = new Ray(new Vector3(2.0f, 0.0f, 5.0f), -Vector3.UnitZ);

        Assert.False(RayIntersections.TryIntersectBox(
            ray,
            UnitBox,
            float.PositiveInfinity,
            out _,
            out _));
    }

    [Fact]
    public void TryIntersectBox_MissesBoxBehindTheRay()
    {
        var ray = new Ray(new Vector3(0.0f, 0.0f, 5.0f), Vector3.UnitZ);

        Assert.False(RayIntersections.TryIntersectBox(
            ray,
            UnitBox,
            float.PositiveInfinity,
            out _,
            out _));
    }

    [Fact]
    public void TryIntersectBox_ParallelDirectionMissesOutsideSlab()
    {
        var ray = new Ray(new Vector3(2.0f, 0.0f, 0.0f), Vector3.UnitY);

        Assert.False(RayIntersections.TryIntersectBox(
            ray,
            UnitBox,
            float.PositiveInfinity,
            out _,
            out _));
    }

    [Fact]
    public void TryIntersectBox_ParallelDirectionOnFaceIsAGrazingHit()
    {
        // The ray travels along the +Y axis while lying in the X = 1 and Z = 0
        // planes. X is parallel and exactly on the face boundary, which is
        // inside; the ray enters through the Y = -1 face at that edge.
        var ray = new Ray(new Vector3(1.0f, -2.0f, 0.0f), Vector3.UnitY);

        Assert.True(RayIntersections.TryIntersectBox(
            ray,
            UnitBox,
            100.0f,
            out float distance,
            out Vector3 normal));

        Assert.Equal(1.0f, distance, precision: 5);
        Assert.Equal(-Vector3.UnitY, normal);
    }

    [Fact]
    public void TryIntersectBox_OriginInsideReturnsZeroDistanceAndFacingNormal()
    {
        var ray = new Ray(Vector3.Zero, -Vector3.UnitZ);

        Assert.True(RayIntersections.TryIntersectBox(
            ray,
            UnitBox,
            100.0f,
            out float distance,
            out Vector3 normal));

        Assert.Equal(0.0f, distance);
        Assert.Equal(Vector3.UnitZ, normal);
    }

    [Fact]
    public void TryIntersectBox_OriginInsideRespectsZeroMaximumDistance()
    {
        var ray = new Ray(Vector3.Zero, Vector3.UnitX);

        Assert.True(RayIntersections.TryIntersectBox(
            ray,
            UnitBox,
            0.0f,
            out float distance,
            out _));
        Assert.Equal(0.0f, distance);
    }

    [Fact]
    public void TryIntersectBox_GrazesCorner()
    {
        var direction = Vector3.Normalize(new Vector3(1.0f, 1.0f, 0.0f));
        var ray = new Ray(new Vector3(-3.0f, -3.0f, 1.0f), direction);

        Assert.True(RayIntersections.TryIntersectBox(
            ray,
            UnitBox,
            100.0f,
            out float distance,
            out Vector3 normal));

        Assert.Equal(MathF.Sqrt(8.0f), distance, precision: 4);
        Assert.Equal(1.0f, MathF.Abs(normal.X) + MathF.Abs(normal.Y), precision: 5);
        Assert.Equal(0.0f, normal.Z);
    }

    [Fact]
    public void TryIntersectBox_AcceptsFlatBox()
    {
        var flat = new BoundingBox(Vector3.Zero, new Vector3(2.0f, 0.0f, 2.0f));
        var ray = new Ray(new Vector3(1.0f, 5.0f, 1.0f), -Vector3.UnitY);

        Assert.True(RayIntersections.TryIntersectBox(
            ray,
            flat,
            100.0f,
            out float distance,
            out Vector3 normal));

        Assert.Equal(5.0f, distance, precision: 5);
        Assert.Equal(Vector3.UnitY, normal);
    }

    [Fact]
    public void TryIntersectBox_MaximumDistanceIsInclusive()
    {
        var ray = new Ray(new Vector3(0.0f, 0.0f, 5.0f), -Vector3.UnitZ);

        Assert.True(RayIntersections.TryIntersectBox(
            ray,
            UnitBox,
            4.0f,
            out float distance,
            out _));
        Assert.Equal(4.0f, distance, precision: 5);
        Assert.False(RayIntersections.TryIntersectBox(
            ray,
            UnitBox,
            3.999f,
            out _,
            out _));
    }

    [Fact]
    public void TryIntersectBox_UnrepresentableFiniteDistanceIsASafeMiss()
    {
        // The entry distance is larger than float.MaxValue even though every
        // input coordinate is finite. Reporting it would require an infinity
        // that RaycastHit rejects, so the intersection is a miss.
        var farBox = new BoundingBox(
            new Vector3(float.MaxValue - 1.0f, -1.0f, -1.0f),
            new Vector3(float.MaxValue, 1.0f, 1.0f));
        var ray = new Ray(new Vector3(-float.MaxValue, 0.0f, 0.0f), Vector3.UnitX);

        Assert.False(RayIntersections.TryIntersectBox(
            ray,
            farBox,
            float.PositiveInfinity,
            out float distance,
            out _));
        Assert.Equal(0.0f, distance);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(-1.0f)]
    public void TryIntersectBox_InvalidMaximumDistanceIsASafeMiss(float maxDistance)
    {
        var ray = new Ray(new Vector3(0.0f, 0.0f, 5.0f), -Vector3.UnitZ);

        Assert.False(RayIntersections.TryIntersectBox(
            ray,
            UnitBox,
            maxDistance,
            out float distance,
            out Vector3 normal));

        Assert.Equal(0.0f, distance);
        Assert.Equal(Vector3.Zero, normal);
    }

    [Fact]
    public void TryIntersectBox_DegeneratePointBoxOnlyHitsItsExactPoint()
    {
        var pointBox = new BoundingBox(Vector3.Zero, Vector3.Zero);

        Assert.True(RayIntersections.TryIntersectBox(
            new Ray(Vector3.Zero, -Vector3.UnitZ),
            pointBox,
            0.0f,
            out float distance,
            out _));
        Assert.Equal(0.0f, distance);

        Assert.False(RayIntersections.TryIntersectBox(
            new Ray(new Vector3(1.0f, 0.0f, 0.0f), -Vector3.UnitZ),
            pointBox,
            float.PositiveInfinity,
            out _,
            out _));
    }
}
