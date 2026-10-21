using System.Numerics;
using Nymeria.Core.Models;

namespace Nymeria.Core.Tests;

public sealed class BoundingSphereTests
{
    [Fact]
    public void Constructor_AcceptsValidSphereAndZeroRadius()
    {
        var sphere = new BoundingSphere(new Vector3(1.0f, 2.0f, 3.0f), 4.0f);
        var point = new BoundingSphere(new Vector3(1.0f, 2.0f, 3.0f), 0.0f);

        Assert.Equal(new Vector3(1.0f, 2.0f, 3.0f), sphere.Center);
        Assert.Equal(4.0f, sphere.Radius);
        Assert.Equal(0.0f, point.Radius);
    }

    [Fact]
    public void DefaultValue_IsAValidPointSphereAtTheOrigin()
    {
        BoundingSphere sphere = default;

        Assert.Equal(Vector3.Zero, sphere.Center);
        Assert.Equal(0.0f, sphere.Radius);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-1.0f)]
    public void Constructor_RejectsInvalidRadius(float radius)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BoundingSphere(Vector3.Zero, radius));
    }

    [Theory]
    [InlineData(float.NaN, 0.0f, 0.0f)]
    [InlineData(0.0f, float.PositiveInfinity, 0.0f)]
    [InlineData(0.0f, 0.0f, float.NegativeInfinity)]
    public void Constructor_RejectsNonFiniteCenter(float x, float y, float z)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BoundingSphere(new Vector3(x, y, z), 1.0f));
    }

    [Fact]
    public void TryTransform_TranslatesCenterAndKeepsRadius()
    {
        var sphere = new BoundingSphere(new Vector3(1.0f, 2.0f, 3.0f), 2.0f);

        Assert.True(sphere.TryTransform(
            Matrix4x4.CreateTranslation(10.0f, -2.0f, 1.0f),
            out BoundingSphere transformed));

        Assert.Equal(new Vector3(11.0f, 0.0f, 4.0f), transformed.Center);
        Assert.Equal(2.0f, transformed.Radius);
    }

    [Fact]
    public void TryTransform_ScalesUniformly()
    {
        var sphere = new BoundingSphere(new Vector3(1.0f, -2.0f, 0.5f), 2.0f);

        Assert.True(sphere.TryTransform(
            Matrix4x4.CreateScale(2.0f),
            out BoundingSphere transformed));

        Assert.Equal(new Vector3(2.0f, -4.0f, 1.0f), transformed.Center);
        Assert.Equal(4.0f, transformed.Radius);
    }

    [Fact]
    public void TryTransform_UsesLargestScaleForNonUniformScale()
    {
        var sphere = new BoundingSphere(Vector3.Zero, 2.0f);

        Assert.True(sphere.TryTransform(
            Matrix4x4.CreateScale(3.0f, 1.0f, 0.5f),
            out BoundingSphere transformed));

        Assert.Equal(6.0f, transformed.Radius);
    }

    [Fact]
    public void TryTransform_UsesAbsoluteScaleForMirroring()
    {
        var sphere = new BoundingSphere(Vector3.One, 1.5f);

        Assert.True(sphere.TryTransform(
            Matrix4x4.CreateScale(-2.0f, 1.0f, 1.0f),
            out BoundingSphere transformed));

        Assert.Equal(new Vector3(-2.0f, 1.0f, 1.0f), transformed.Center);
        Assert.Equal(3.0f, transformed.Radius);
    }

    [Fact]
    public void TryTransform_DoesNotChangeRadiusForRotation()
    {
        var sphere = new BoundingSphere(Vector3.One, 2.0f);

        Assert.True(sphere.TryTransform(
            Matrix4x4.CreateRotationY(MathF.PI / 2.0f) *
            Matrix4x4.CreateTranslation(0.0f, 5.0f, 0.0f),
            out BoundingSphere transformed));

        Assert.Equal(new Vector3(1.0f, 6.0f, -1.0f), Round(transformed.Center));
        Assert.Equal(2.0f, transformed.Radius);
    }

    [Fact]
    public void TryTransform_AcceptsSingularScaleAsAPointSphere()
    {
        var sphere = new BoundingSphere(new Vector3(2.0f, 3.0f, 4.0f), 5.0f);

        Assert.True(sphere.TryTransform(
            Matrix4x4.CreateScale(0.0f),
            out BoundingSphere transformed));

        Assert.Equal(new Vector3(0.0f, 0.0f, 0.0f), transformed.Center);
        Assert.Equal(0.0f, transformed.Radius);
    }

    [Fact]
    public void TryTransform_RejectsNonFiniteMatrix()
    {
        var sphere = new BoundingSphere(Vector3.Zero, 1.0f);
        var matrix = new Matrix4x4(
            float.NaN, 0.0f, 0.0f, 0.0f,
            0.0f, 1.0f, 0.0f, 0.0f,
            0.0f, 0.0f, 1.0f, 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f);

        Assert.False(sphere.TryTransform(matrix, out BoundingSphere transformed));
        Assert.Equal(default, transformed);
    }

    [Fact]
    public void TryTransform_RejectsProjectiveMatrices()
    {
        var sphere = new BoundingSphere(Vector3.Zero, 1.0f);
        var matrix = new Matrix4x4(
            1.0f, 0.0f, 0.0f, 0.25f,
            0.0f, 1.0f, 0.0f, 0.0f,
            0.0f, 0.0f, 1.0f, 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f);

        Assert.False(sphere.TryTransform(matrix, out _));
    }

    [Fact]
    public void TryTransform_RejectsShearThatWouldUnderReportTheRadius()
    {
        var sphere = new BoundingSphere(Vector3.Zero, 1.0f);
        Matrix4x4 shear = Matrix4x4.Identity;
        shear.M12 = 1.0f;

        Assert.False(sphere.TryTransform(shear, out _));
    }

    [Fact]
    public void TryTransform_SaturatesExtremeResultsToFiniteValues()
    {
        var sphere = new BoundingSphere(new Vector3(float.MaxValue), float.MaxValue);

        Assert.True(sphere.TryTransform(
            Matrix4x4.CreateScale(2.0f),
            out BoundingSphere transformed));

        Assert.True(float.IsFinite(transformed.Center.X));
        Assert.True(float.IsFinite(transformed.Center.Y));
        Assert.True(float.IsFinite(transformed.Center.Z));
        Assert.True(float.IsFinite(transformed.Radius));
        Assert.True(transformed.Radius > 0.0f);
    }

    private static Vector3 Round(Vector3 value)
    {
        return new Vector3(
            MathF.Round(value.X, 4),
            MathF.Round(value.Y, 4),
            MathF.Round(value.Z, 4));
    }
}
