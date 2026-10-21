using System.Numerics;
using Nymeria.Core.Collision;
using Nymeria.Core.Models;

namespace Nymeria.Core.Tests;

public sealed class CollisionShapeTests
{
    private static readonly BoundingBox LocalBox = new(
        new Vector3(-1.0f, -1.0f, -1.0f),
        new Vector3(1.0f, 1.0f, 1.0f));

    [Fact]
    public void FactoriesStoreExactlyOnePrimitive()
    {
        CollisionShape box = CollisionShape.FromBox(LocalBox);
        CollisionShape sphere = CollisionShape.FromSphere(
            new BoundingSphere(new Vector3(2.0f, 3.0f, 4.0f), 5.0f));

        Assert.Equal(CollisionShapeKind.Box, box.Kind);
        Assert.True(box.TryGetBox(out BoundingBox storedBox));
        Assert.Equal(LocalBox.Min, storedBox.Min);
        Assert.Equal(LocalBox.Max, storedBox.Max);
        Assert.False(box.TryGetSphere(out _));

        Assert.Equal(CollisionShapeKind.Sphere, sphere.Kind);
        Assert.True(sphere.TryGetSphere(out BoundingSphere storedSphere));
        Assert.Equal(new Vector3(2.0f, 3.0f, 4.0f), storedSphere.Center);
        Assert.Equal(5.0f, storedSphere.Radius);
        Assert.False(sphere.TryGetBox(out _));
    }

    [Fact]
    public void DefaultShapeHasNoPrimitiveAndCannotBeTransformed()
    {
        CollisionShape shape = default;

        Assert.Equal(CollisionShapeKind.None, shape.Kind);
        Assert.False(shape.TryGetBox(out _));
        Assert.False(shape.TryGetSphere(out _));
        Assert.False(shape.TryTransform(Matrix4x4.Identity, out CollisionShape world));
        Assert.Equal(default, world);
    }

    [Fact]
    public void TryTransform_BoxTranslatesAndScalesConservatively()
    {
        CollisionShape shape = CollisionShape.FromBox(LocalBox);
        Matrix4x4 transform =
            Matrix4x4.CreateScale(2.0f, 3.0f, 4.0f) *
            Matrix4x4.CreateTranslation(10.0f, -2.0f, 5.0f);

        Assert.True(shape.TryTransform(transform, out CollisionShape world));
        Assert.True(world.TryGetBox(out BoundingBox box));

        Assert.Equal(new Vector3(8.0f, -5.0f, 1.0f), box.Min);
        Assert.Equal(new Vector3(12.0f, 1.0f, 9.0f), box.Max);
    }

    [Fact]
    public void TryTransform_BoxCoversRotatedCorners()
    {
        CollisionShape shape = CollisionShape.FromBox(LocalBox);
        Matrix4x4 transform =
            Matrix4x4.CreateRotationY(MathF.PI / 4.0f) *
            Matrix4x4.CreateTranslation(10.0f, 0.0f, 0.0f);

        Assert.True(shape.TryTransform(transform, out CollisionShape world));
        Assert.True(world.TryGetBox(out BoundingBox box));

        const float halfExtent = 1.414214f;
        Assert.Equal(10.0f - halfExtent, box.Min.X, precision: 5);
        Assert.Equal(10.0f + halfExtent, box.Max.X, precision: 5);
        Assert.Equal(-halfExtent, box.Min.Z, precision: 5);
        Assert.Equal(halfExtent, box.Max.Z, precision: 5);
    }

    [Fact]
    public void TryTransform_SphereUsesLargestScaleAndTransformsCenter()
    {
        CollisionShape shape = CollisionShape.FromSphere(
            new BoundingSphere(new Vector3(1.0f, 0.0f, 0.0f), 2.0f));
        Matrix4x4 transform =
            Matrix4x4.CreateScale(3.0f, 1.0f, 0.5f) *
            Matrix4x4.CreateTranslation(0.0f, 4.0f, 0.0f);

        Assert.True(shape.TryTransform(transform, out CollisionShape world));
        Assert.True(world.TryGetSphere(out BoundingSphere sphere));

        Assert.Equal(new Vector3(3.0f, 4.0f, 0.0f), sphere.Center);
        Assert.Equal(6.0f, sphere.Radius);
    }

    [Fact]
    public void TryTransform_AcceptsSingularAffineTransforms()
    {
        CollisionShape box = CollisionShape.FromBox(LocalBox);
        CollisionShape sphere = CollisionShape.FromSphere(
            new BoundingSphere(new Vector3(3.0f, 4.0f, 5.0f), 2.0f));
        Matrix4x4 collapse = Matrix4x4.CreateScale(0.0f, 1.0f, 1.0f);

        Assert.True(box.TryTransform(collapse, out CollisionShape flat));
        Assert.True(flat.TryGetBox(out BoundingBox flatBox));
        Assert.Equal(0.0f, flatBox.Min.X);
        Assert.Equal(0.0f, flatBox.Max.X);

        Assert.True(sphere.TryTransform(collapse, out CollisionShape flattened));
        Assert.True(flattened.TryGetSphere(out BoundingSphere flattenedSphere));

        // Only X collapses, so the largest remaining axis (Y or Z, both 1)
        // still gives a conservative radius of 2. The center moves to X = 0.
        Assert.Equal(2.0f, flattenedSphere.Radius);
        Assert.Equal(0.0f, flattenedSphere.Center.X);

        Assert.True(sphere.TryTransform(
            Matrix4x4.CreateScale(0.0f),
            out CollisionShape point));
        Assert.True(point.TryGetSphere(out BoundingSphere pointSphere));
        Assert.Equal(0.0f, pointSphere.Radius);
        Assert.Equal(Vector3.Zero, pointSphere.Center);
    }

    [Fact]
    public void TryTransform_RejectsNonFiniteMatrix()
    {
        CollisionShape shape = CollisionShape.FromBox(LocalBox);
        var matrix = new Matrix4x4(
            1.0f, 0.0f, 0.0f, 0.0f,
            0.0f, 1.0f, 0.0f, 0.0f,
            0.0f, 0.0f, 1.0f, 0.0f,
            float.NaN, 0.0f, 0.0f, 1.0f);

        Assert.False(shape.TryTransform(matrix, out CollisionShape world));
        Assert.Equal(default, world);
    }

    [Fact]
    public void TryTransform_RejectsProjectiveMatrix()
    {
        CollisionShape shape = CollisionShape.FromBox(LocalBox);
        var matrix = new Matrix4x4(
            1.0f, 0.0f, 0.0f, 0.25f,
            0.0f, 1.0f, 0.0f, 0.0f,
            0.0f, 0.0f, 1.0f, 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f);

        Assert.False(shape.TryTransform(matrix, out CollisionShape world));
        Assert.Equal(default, world);
    }

    [Fact]
    public void TryTransform_SaturatesExtremeBoxWithoutInvalidGeometry()
    {
        CollisionShape shape = CollisionShape.FromBox(
            new BoundingBox(
                new Vector3(-float.MaxValue),
                new Vector3(float.MaxValue)));

        Assert.True(shape.TryTransform(
            Matrix4x4.CreateScale(2.0f),
            out CollisionShape world));
        Assert.True(world.TryGetBox(out BoundingBox box));
        Assert.True(float.IsFinite(box.Min.X));
        Assert.True(float.IsFinite(box.Max.X));
        Assert.True(box.Min.X < 0.0f);
        Assert.True(box.Max.X > 0.0f);
    }
}
