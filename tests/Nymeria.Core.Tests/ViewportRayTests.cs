using System.Numerics;
using Nymeria.Core.Raycasting;
using Nymeria.Core.Scenes;

namespace Nymeria.Core.Tests;

public sealed class ViewportRayTests
{
    private const float Width = 1920.0f;
    private const float Height = 1080.0f;

    [Fact]
    public void CenterRay_StartsAtCameraAndLooksForward()
    {
        var position = new Vector3(0.0f, 5.0f, 10.0f);
        Vector3 forward = -Vector3.UnitZ;
        Matrix4x4 view = CreateView(position, forward);
        Matrix4x4 projection = CreateProjection(Width / Height);

        Assert.True(ViewportRay.TryCreateCenter(
            Width,
            Height,
            view,
            projection,
            out Ray ray));

        AssertVectorEqual(position, ray.Origin);
        AssertVectorEqual(forward, ray.Direction);
    }

    [Fact]
    public void CenterRay_UsesPitchedForwardDirection()
    {
        var position = new Vector3(3.0f, 2.0f, -4.0f);
        const float yaw = 0.7f;
        const float pitch = -0.2f;
        float cosPitch = MathF.Cos(pitch);
        var forward = new Vector3(
            MathF.Sin(yaw) * cosPitch,
            MathF.Sin(pitch),
            -MathF.Cos(yaw) * cosPitch);
        Matrix4x4 view = CreateView(position, forward);
        Matrix4x4 projection = CreateProjection(Width / Height);

        Assert.True(ViewportRay.TryCreateCenter(
            Width,
            Height,
            view,
            projection,
            out Ray ray));

        AssertVectorEqual(position, ray.Origin);
        AssertVectorEqual(forward, ray.Direction);
    }

    [Fact]
    public void PixelRay_PassesThroughProjectedWorldPoint()
    {
        var position = new Vector3(0.0f, 5.0f, 10.0f);
        Vector3 forward = -Vector3.UnitZ;
        Matrix4x4 view = CreateView(position, forward);
        Matrix4x4 projection = CreateProjection(Width / Height);
        var worldPoint = new Vector3(2.0f, 6.0f, 0.0f);
        Vector2 pixel = ProjectToPixel(worldPoint, view, projection);

        Assert.True(ViewportRay.TryCreate(
            pixel,
            Width,
            Height,
            view,
            projection,
            out Ray ray));

        Vector3 toPoint = worldPoint - ray.Origin;
        Assert.True(Vector3.Dot(toPoint, ray.Direction) > 0.0f);
        Assert.True(Vector3.Cross(ray.Direction, toPoint).Length() < 1e-3f);
    }

    [Fact]
    public void TopLeftPixelPointsUpAndLeft()
    {
        var position = Vector3.Zero;
        Vector3 forward = -Vector3.UnitZ;
        Matrix4x4 view = CreateView(position, forward);
        Matrix4x4 projection = CreateProjection(Width / Height);

        Assert.True(ViewportRay.TryCreate(
            Vector2.Zero,
            Width,
            Height,
            view,
            projection,
            out Ray upperLeft));
        Assert.True(ViewportRay.TryCreate(
            new Vector2(Width, Height),
            Width,
            Height,
            view,
            projection,
            out Ray lowerRight));

        Assert.True(upperLeft.Direction.X < 0.0f);
        Assert.True(upperLeft.Direction.Y > 0.0f);
        Assert.True(lowerRight.Direction.X > 0.0f);
        Assert.True(lowerRight.Direction.Y < 0.0f);
    }

    [Fact]
    public void CenterRay_HitsObjectDirectlyAhead()
    {
        var position = new Vector3(0.0f, 5.0f, 10.0f);
        Matrix4x4 view = CreateView(position, -Vector3.UnitZ);
        Matrix4x4 projection = CreateProjection(Width / Height);
        var scene = new Scene();
        var sceneObject = new SceneObject(
            RaycastTestMeshes.CreateBoxMesh(1.0f),
            new Transform(new Vector3(0.0f, 5.0f, 0.0f), Quaternion.Identity, Vector3.One));
        scene.Add(sceneObject);

        Assert.True(ViewportRay.TryCreateCenter(
            Width,
            Height,
            view,
            projection,
            out Ray ray));
        Assert.True(SceneRaycaster.TryRaycast(scene, ray, 100.0f, out RaycastHit hit));

        Assert.Same(sceneObject, hit.SceneObject);
        Assert.Equal(9.0f, hit.Distance, precision: 4);
    }

    [Fact]
    public void DrawableScaledPixels_ProduceTheSameWorldRay()
    {
        var position = new Vector3(1.5f, 3.0f, 8.0f);
        Matrix4x4 view = CreateView(position, -Vector3.UnitZ);
        Matrix4x4 projection = CreateProjection(Width / Height);
        var pixel = new Vector2(700.0f, 420.0f);

        Assert.True(ViewportRay.TryCreate(
            pixel,
            Width,
            Height,
            view,
            projection,
            out Ray logicalRay));
        Assert.True(ViewportRay.TryCreate(
            pixel * 2.0f,
            Width * 2.0f,
            Height * 2.0f,
            view,
            projection,
            out Ray drawableRay));

        AssertVectorEqual(logicalRay.Direction, drawableRay.Direction);
        AssertVectorEqual(logicalRay.Origin, drawableRay.Origin);
    }

    [Theory]
    [InlineData(0.0f, 100.0f)]
    [InlineData(-1.0f, 100.0f)]
    [InlineData(float.NaN, 100.0f)]
    [InlineData(100.0f, 0.0f)]
    [InlineData(100.0f, -1.0f)]
    [InlineData(100.0f, float.PositiveInfinity)]
    public void TryCreate_RejectsInvalidViewport(float width, float height)
    {
        Matrix4x4 view = CreateView(Vector3.Zero, -Vector3.UnitZ);
        Matrix4x4 projection = CreateProjection(1.0f);

        Assert.False(ViewportRay.TryCreate(
            Vector2.Zero,
            width,
            height,
            view,
            projection,
            out Ray ray));
        Assert.Equal(default, ray);
    }

    [Theory]
    [InlineData(float.NaN, 0.0f)]
    [InlineData(0.0f, float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity, 0.0f)]
    public void TryCreate_RejectsNonFinitePixel(float x, float y)
    {
        Matrix4x4 view = CreateView(Vector3.Zero, -Vector3.UnitZ);
        Matrix4x4 projection = CreateProjection(1.0f);

        Assert.False(ViewportRay.TryCreate(
            new Vector2(x, y),
            Width,
            Height,
            view,
            projection,
            out _));
    }

    [Fact]
    public void TryCreate_RejectsSingularMatrices()
    {
        Matrix4x4 view = CreateView(Vector3.Zero, -Vector3.UnitZ);
        Matrix4x4 projection = CreateProjection(1.0f);

        Assert.False(ViewportRay.TryCreate(
            Vector2.Zero,
            Width,
            Height,
            new Matrix4x4(),
            projection,
            out _));
        Assert.False(ViewportRay.TryCreate(
            Vector2.Zero,
            Width,
            Height,
            view,
            new Matrix4x4(),
            out _));
    }

    [Fact]
    public void TryCreate_RejectsNonFiniteMatrices()
    {
        Matrix4x4 view = CreateView(Vector3.Zero, -Vector3.UnitZ);
        Matrix4x4 projection = CreateProjection(1.0f);
        Matrix4x4 nonFinite = projection;
        nonFinite.M11 = float.NaN;

        Assert.False(ViewportRay.TryCreate(
            Vector2.Zero,
            Width,
            Height,
            view,
            nonFinite,
            out _));
    }

    private static Matrix4x4 CreateView(Vector3 position, Vector3 forward)
    {
        return Matrix4x4.CreateLookAt(
            position,
            position + forward,
            Vector3.UnitY);
    }

    private static Matrix4x4 CreateProjection(float aspectRatio)
    {
        // Same projection factory and conventions as Nymeria.Graphics.Camera.
        return Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 4.0f,
            aspectRatio,
            0.1f,
            100.0f);
    }

    private static Vector2 ProjectToPixel(
        Vector3 point,
        Matrix4x4 view,
        Matrix4x4 projection)
    {
        Vector4 clip = Vector4.Transform(
            new Vector4(point, 1.0f),
            view * projection);
        var normalized = new Vector3(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);

        return new Vector2(
            (normalized.X + 1.0f) * 0.5f * Width,
            (1.0f - normalized.Y) * 0.5f * Height);
    }

    private static void AssertVectorEqual(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, precision: 3);
        Assert.Equal(expected.Y, actual.Y, precision: 3);
        Assert.Equal(expected.Z, actual.Z, precision: 3);
    }
}
