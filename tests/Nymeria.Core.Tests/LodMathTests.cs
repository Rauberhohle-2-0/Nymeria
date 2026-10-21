using System.Numerics;
using Nymeria.Core.Lod;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;

namespace Nymeria.Core.Tests;

public sealed class LodMathTests
{
    [Fact]
    public void DistanceToBounds_PointInsideIsZero()
    {
        var bounds = new BoundingBox(
            new Vector3(-1.0f, -1.0f, -1.0f),
            new Vector3(1.0f, 1.0f, 1.0f));

        Assert.Equal(0.0f, LodMath.DistanceToBounds(Vector3.Zero, bounds));
        Assert.Equal(0.0f, LodMath.DistanceToBounds(new Vector3(0.5f, -0.5f, 1.0f), bounds));
    }

    [Fact]
    public void DistanceToBounds_UsesTheClosestPoint()
    {
        var bounds = new BoundingBox(
            new Vector3(-1.0f, -1.0f, -1.0f),
            new Vector3(1.0f, 1.0f, 1.0f));

        Assert.Equal(3.0f, LodMath.DistanceToBounds(new Vector3(4.0f, 0.0f, 0.0f), bounds));
        Assert.Equal(2.0f, LodMath.DistanceToBounds(new Vector3(0.0f, 0.0f, -3.0f), bounds));
        Assert.Equal(
            MathF.Sqrt(18.0f),
            LodMath.DistanceToBounds(new Vector3(4.0f, 4.0f, 0.0f), bounds),
            precision: 4);
    }

    [Fact]
    public void DistanceToBounds_UsesTransformedScaledBounds()
    {
        var bounds = new BoundingBox(
            new Vector3(-1.0f, -1.0f, -1.0f),
            new Vector3(1.0f, 1.0f, 1.0f));
        var transform = new Transform(
            new Vector3(0.0f, 0.0f, -30.0f),
            Quaternion.Identity,
            new Vector3(10.0f, 10.0f, 10.0f));

        BoundingBox worldBounds = bounds.Transform(transform.CreateModelMatrix());

        Assert.Equal(20.0f, LodMath.DistanceToBounds(Vector3.Zero, worldBounds));
    }

    [Fact]
    public void DistanceToBounds_NonFinitePointFailsSafeToZero()
    {
        var bounds = new BoundingBox(
            new Vector3(10.0f, 10.0f, 10.0f),
            new Vector3(20.0f, 20.0f, 20.0f));

        Assert.Equal(
            0.0f,
            LodMath.DistanceToBounds(new Vector3(float.NaN, 0.0f, 0.0f), bounds));
        Assert.Equal(
            0.0f,
            LodMath.DistanceToBounds(
                new Vector3(float.PositiveInfinity, 0.0f, 0.0f),
                bounds));
    }

    [Fact]
    public void DistanceToBounds_ExtremeFiniteResultSaturates()
    {
        var bounds = new BoundingBox(
            new Vector3(-float.MaxValue, 0.0f, 0.0f),
            new Vector3(-float.MaxValue, 0.0f, 0.0f));

        float distance = LodMath.DistanceToBounds(
            new Vector3(float.MaxValue, 0.0f, 0.0f),
            bounds);

        Assert.Equal(float.MaxValue, distance);
        Assert.True(float.IsFinite(distance));
    }
}
