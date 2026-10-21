using System.Numerics;
using Nymeria.Core.Raycasting;

namespace Nymeria.Core.Tests;

public sealed class RayTests
{
    [Fact]
    public void Constructor_NormalizesDirectionAndKeepsOrigin()
    {
        var ray = new Ray(
            new Vector3(1.0f, 2.0f, 3.0f),
            new Vector3(0.0f, 0.0f, -5.0f));

        Assert.Equal(new Vector3(1.0f, 2.0f, 3.0f), ray.Origin);
        Assert.Equal(new Vector3(0.0f, 0.0f, -1.0f), ray.Direction);
        Assert.Equal(1.0f, ray.Direction.Length(), precision: 5);
    }

    [Fact]
    public void Constructor_NormalizesDiagonalDirection()
    {
        var ray = new Ray(Vector3.Zero, new Vector3(2.0f, 0.0f, 2.0f));

        Assert.Equal(1.0f, ray.Direction.Length(), precision: 5);
        Assert.Equal(1.0f / MathF.Sqrt(2.0f), ray.Direction.X, precision: 5);
        Assert.Equal(1.0f / MathF.Sqrt(2.0f), ray.Direction.Z, precision: 5);
    }

    [Fact]
    public void Constructor_AllowsNegativeDirectionComponents()
    {
        var ray = new Ray(Vector3.Zero, new Vector3(-1.0f, -2.0f, -3.0f));

        Assert.True(ray.Direction.X < 0.0f);
        Assert.True(ray.Direction.Y < 0.0f);
        Assert.True(ray.Direction.Z < 0.0f);
        Assert.Equal(1.0f, ray.Direction.Length(), precision: 5);
    }

    [Fact]
    public void GetPoint_UsesWorldUnitDistance()
    {
        var ray = new Ray(new Vector3(0.0f, 1.0f, 0.0f), new Vector3(0.0f, 0.0f, -2.0f));

        Assert.Equal(new Vector3(0.0f, 1.0f, -5.0f), ray.GetPoint(5.0f));
        Assert.Equal(ray.Origin, ray.GetPoint(0.0f));
        Assert.Equal(new Vector3(0.0f, 1.0f, 2.5f), ray.GetPoint(-2.5f));
    }

    [Fact]
    public void TryCreate_RejectsZeroLengthDirection()
    {
        Assert.False(Ray.TryCreate(Vector3.Zero, Vector3.Zero, out Ray ray));
        Assert.Equal(default, ray);
    }

    [Theory]
    [InlineData(float.NaN, 0.0f, 0.0f)]
    [InlineData(0.0f, float.PositiveInfinity, 0.0f)]
    [InlineData(0.0f, float.NegativeInfinity, 0.0f)]
    [InlineData(0.0f, 0.0f, float.NaN)]
    public void Constructor_RejectsNonFiniteDirection(float x, float y, float z)
    {
        Assert.Throws<ArgumentException>(() =>
            new Ray(Vector3.Zero, new Vector3(x, y, z)));
    }

    [Theory]
    [InlineData(float.NaN, 0.0f, 0.0f)]
    [InlineData(0.0f, float.PositiveInfinity, 0.0f)]
    [InlineData(0.0f, 0.0f, float.NegativeInfinity)]
    public void Constructor_RejectsNonFiniteOrigin(float x, float y, float z)
    {
        Assert.Throws<ArgumentException>(() =>
            new Ray(new Vector3(x, y, z), Vector3.UnitZ));
    }

    [Fact]
    public void TryCreate_AcceptsVeryLargeAndVerySmallFiniteDirections()
    {
        Assert.True(Ray.TryCreate(
            Vector3.Zero,
            new Vector3(float.MaxValue, 0.0f, 0.0f),
            out Ray large));
        Assert.True(Ray.TryCreate(
            Vector3.Zero,
            new Vector3(1e-30f, 0.0f, 0.0f),
            out Ray small));

        Assert.Equal(Vector3.UnitX, large.Direction);
        Assert.Equal(Vector3.UnitX, small.Direction);
    }
}
