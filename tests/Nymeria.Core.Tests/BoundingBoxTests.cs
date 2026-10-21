using System.Numerics;
using Nymeria.Core.Culling;
using Nymeria.Core.Models;

namespace Nymeria.Core.Tests;

public sealed class BoundingBoxTests
{
    [Fact]
    public void MeshBounds_CoverEveryPosition()
    {
        var mesh = new MeshData(
            new[]
            {
                new Vector3(2.0f, -1.0f, 0.5f),
                new Vector3(-3.0f, 4.0f, -2.0f),
                new Vector3(0.0f, 1.0f, 7.0f),
            },
            ReadOnlyMemory<Vector3>.Empty,
            ReadOnlyMemory<Vector2>.Empty,
            new uint[] { 0, 1, 2 });

        Assert.Equal(new Vector3(-3.0f, -1.0f, -2.0f), mesh.Bounds.Min);
        Assert.Equal(new Vector3(2.0f, 4.0f, 7.0f), mesh.Bounds.Max);
    }

    [Fact]
    public void MeshBounds_AreComputedFromAllVertices()
    {
        var mesh = new MeshData(
            new[]
            {
                new Vector3(5.0f, 5.0f, 5.0f),
                new Vector3(-5.0f, 5.0f, 5.0f),
                new Vector3(0.0f, -5.0f, 0.0f),
            },
            ReadOnlyMemory<Vector3>.Empty,
            ReadOnlyMemory<Vector2>.Empty,
            new uint[] { 0, 1, 2 });

        Assert.Equal(new Vector3(-5.0f, -5.0f, 0.0f), mesh.Bounds.Min);
        Assert.Equal(new Vector3(5.0f, 5.0f, 5.0f), mesh.Bounds.Max);
    }

    [Fact]
    public void Constructor_RejectsNonFiniteCorners()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BoundingBox(new Vector3(float.NaN, 0.0f, 0.0f), Vector3.One));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BoundingBox(Vector3.Zero, new Vector3(0.0f, float.PositiveInfinity, 0.0f)));
    }

    [Fact]
    public void Constructor_RejectsInvertedCorners()
    {
        Assert.Throws<ArgumentException>(() =>
            new BoundingBox(new Vector3(1.0f, 0.0f, 0.0f), Vector3.Zero));
    }

    [Fact]
    public void Constructor_AllowsFlatBox()
    {
        var box = new BoundingBox(Vector3.Zero, new Vector3(1.0f, 0.0f, 1.0f));

        Assert.Equal(Vector3.Zero, box.Min);
        Assert.Equal(new Vector3(1.0f, 0.0f, 1.0f), box.Max);
    }

    [Fact]
    public void Transform_TranslationMovesBothCorners()
    {
        var box = new BoundingBox(
            new Vector3(-1.0f, -2.0f, -3.0f),
            new Vector3(4.0f, 5.0f, 6.0f));

        BoundingBox moved = box.Transform(Matrix4x4.CreateTranslation(10.0f, -3.0f, 2.0f));

        Assert.Equal(new Vector3(9.0f, -5.0f, -1.0f), moved.Min);
        Assert.Equal(new Vector3(14.0f, 2.0f, 8.0f), moved.Max);
    }

    [Fact]
    public void Transform_RotatesAllEightCorners()
    {
        var box = new BoundingBox(
            new Vector3(-1.0f, -2.0f, -3.0f),
            new Vector3(2.0f, 1.0f, 4.0f));

        // A quarter turn about +Y maps (x, z) to (z, -x).
        BoundingBox rotated = box.Transform(Matrix4x4.CreateRotationY(MathF.PI / 2.0f));

        Assert.Equal(new Vector3(-3.0f, -2.0f, -2.0f), Round(rotated.Min));
        Assert.Equal(new Vector3(4.0f, 1.0f, 1.0f), Round(rotated.Max));
    }

    [Fact]
    public void Transform_HandlesNonUniformAndNegativeScale()
    {
        var box = new BoundingBox(
            new Vector3(-1.0f, 0.0f, -2.0f),
            new Vector3(2.0f, 1.0f, 3.0f));

        Matrix4x4 transform =
            Matrix4x4.CreateScale(-1.0f, 2.0f, 3.0f) *
            Matrix4x4.CreateTranslation(10.0f, 0.0f, 0.0f);

        BoundingBox scaled = box.Transform(transform);

        // X is mirrored, so the old minimum becomes the new maximum.
        Assert.Equal(new Vector3(8.0f, 0.0f, -6.0f), scaled.Min);
        Assert.Equal(new Vector3(11.0f, 2.0f, 9.0f), scaled.Max);
    }

    [Fact]
    public void Transform_SupportsScaleRotationAndTranslationOrder()
    {
        var box = new BoundingBox(
            new Vector3(-1.0f, -1.0f, -1.0f),
            new Vector3(1.0f, 1.0f, 3.0f));

        Matrix4x4 transform =
            Matrix4x4.CreateScale(2.0f, 1.0f, 1.0f) *
            Matrix4x4.CreateRotationY(MathF.PI / 2.0f) *
            Matrix4x4.CreateTranslation(0.0f, 5.0f, -10.0f);

        BoundingBox transformed = box.Transform(transform);

        // Scale first: x in [-2, 2] and z in [-1, 3]. The quarter turn maps
        // those to x in [-1, 3] and z in [-2, 2], then translation shifts Y and Z.
        Assert.Equal(new Vector3(-1.0f, 4.0f, -12.0f), Round(transformed.Min));
        Assert.Equal(new Vector3(3.0f, 6.0f, -8.0f), Round(transformed.Max));
    }

    [Fact]
    public void Transform_NonFiniteMatrixProducesConservativeMaximumBox()
    {
        var box = new BoundingBox(Vector3.Zero, Vector3.One);
        var nonFinite = new Matrix4x4(
            float.NaN, 0.0f, 0.0f, 0.0f,
            0.0f, 1.0f, 0.0f, 0.0f,
            0.0f, 0.0f, 1.0f, 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f);

        BoundingBox transformed = box.Transform(nonFinite);

        Assert.Equal(new Vector3(-float.MaxValue), transformed.Min);
        Assert.Equal(new Vector3(float.MaxValue), transformed.Max);
    }

    [Fact]
    public void Transform_OverflowProducesFiniteMaximumBox()
    {
        var box = new BoundingBox(
            new Vector3(-float.MaxValue),
            new Vector3(float.MaxValue));

        BoundingBox transformed = box.Transform(Matrix4x4.CreateScale(2.0f));

        Assert.All(
            new[] { transformed.Min, transformed.Max },
            corner =>
            {
                Assert.True(float.IsFinite(corner.X));
                Assert.True(float.IsFinite(corner.Y));
                Assert.True(float.IsFinite(corner.Z));
            });
        Assert.True(transformed.Min.X < 0.0f);
        Assert.True(transformed.Max.X > 0.0f);
    }

    [Fact]
    public void OverflowedBounds_AreStillConsideredVisible()
    {
        var box = new BoundingBox(
            new Vector3(-float.MaxValue),
            new Vector3(float.MaxValue));
        BoundingBox transformed = box.Transform(Matrix4x4.CreateScale(4.0f));

        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4.0f,
            1.6f,
            0.1f,
            100.0f);
        Matrix4x4 view = Matrix4x4.CreateLookAt(
            Vector3.Zero,
            -Vector3.UnitZ,
            Vector3.UnitY);
        Frustum frustum = Frustum.Create(view * projection);

        Assert.True(frustum.Intersects(transformed));
    }

    private static Vector3 Round(Vector3 value)
    {
        // Rotation introduces small float rounding; compare at a tolerance
        // suitable for a quarter turn without hiding a wrong axis.
        return new Vector3(
            MathF.Round(value.X, 4),
            MathF.Round(value.Y, 4),
            MathF.Round(value.Z, 4));
    }
}
