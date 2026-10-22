using System.Numerics;
using Nymeria.Graphics;

namespace Nymeria.Graphics.Tests;

public sealed class CameraTests
{
    [Fact]
    public void ValidCamera_ProducesFiniteViewAndProjection()
    {
        var camera = new Camera(new Vector3(1.0f, 2.0f, 3.0f))
        {
            YawRadians = 0.4f,
            PitchRadians = -0.3f,
            FieldOfViewRadians = MathF.PI / 3.0f,
            NearPlane = 0.05f,
            FarPlane = 500.0f,
        };

        AssertFinite(camera.ViewMatrix);
        AssertFinite(camera.CreateProjectionMatrix(16.0f / 9.0f));
    }

    [Fact]
    public void ViewMatrix_KeepsTheDocumentedConventions()
    {
        var camera = new Camera(new Vector3(0.0f, 0.0f, 4.0f));

        Matrix4x4 view = camera.ViewMatrix;

        // The camera position maps to the view-space origin and a point ten
        // units in front of it (down -Z) keeps its distance on -Z.
        Assert.True(
            Vector3.Distance(Vector3.Transform(camera.Position, view), Vector3.Zero) < 1e-5f);
        Assert.True(
            MathF.Abs(Vector3.Transform(new Vector3(0.0f, 0.0f, -6.0f), view).Z + 10.0f) < 1e-4f);
    }

    [Theory]
    [InlineData(float.NaN, 0.0f, 0.0f)]
    [InlineData(0.0f, float.PositiveInfinity, 0.0f)]
    [InlineData(0.0f, 0.0f, float.NegativeInfinity)]
    public void ViewMatrix_RejectsNonFinitePosition(float x, float y, float z)
    {
        var camera = new Camera(new Vector3(x, y, z));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => camera.ViewMatrix);

        Assert.Contains("Position", exception.Message);
    }

    [Fact]
    public void ViewMatrix_RejectsAPositionThatCannotExpressALookTarget()
    {
        // float.MaxValue plus a unit direction rounds back onto the position, so
        // the look direction would collapse and the matrix would be undefined.
        var camera = new Camera(new Vector3(float.MaxValue));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => camera.ViewMatrix);

        Assert.Contains("position magnitude", exception.Message);
    }

    [Fact]
    public void DegenerateViewBasis_IsPreventedByThePitchClamp()
    {
        // The public API clamps pitch below a right angle, so forward can never
        // become parallel to the up vector. Assert the clamp and that the view
        // matrix stays finite at the limit.
        var camera = new Camera(Vector3.Zero);

        camera.PitchRadians = MathF.PI / 2.0f;

        Assert.Equal(Camera.PitchLimitRadians, camera.PitchRadians);

        Matrix4x4 view = camera.ViewMatrix;

        AssertFinite(view);
        Assert.True(
            MathF.Abs(Vector3.Dot(
                Vector3.Normalize(camera.Forward),
                Vector3.UnitY)) < 1.0f);
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(-0.1f)]
    [InlineData(MathF.PI)]
    [InlineData(MathF.PI + 0.1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Projection_RejectsInvalidFieldOfView(float fieldOfView)
    {
        var camera = new Camera(Vector3.Zero) { FieldOfViewRadians = fieldOfView };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => camera.CreateProjectionMatrix(1.6f));

        Assert.Contains("FieldOfViewRadians", exception.Message);
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(-1.0f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Projection_RejectsInvalidNearPlane(float nearPlane)
    {
        var camera = new Camera(Vector3.Zero) { NearPlane = nearPlane };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => camera.CreateProjectionMatrix(1.6f));

        Assert.Contains("NearPlane", exception.Message);
    }

    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.0f)]
    [InlineData(-5.0f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Projection_RejectsInvalidFarPlane(float farPlane)
    {
        // 0.1 is exactly the default near plane, so equality must be rejected.
        var camera = new Camera(Vector3.Zero) { FarPlane = farPlane };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => camera.CreateProjectionMatrix(1.6f));

        Assert.Contains("FarPlane", exception.Message);
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(-2.0f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Projection_RejectsInvalidAspectRatio(float aspectRatio)
    {
        var camera = new Camera(Vector3.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.CreateProjectionMatrix(aspectRatio));
    }

    [Fact]
    public void Projection_AcceptsBoundaryAdjacentValues()
    {
        var camera = new Camera(Vector3.Zero)
        {
            FieldOfViewRadians = MathF.PI - 0.001f,
            NearPlane = 1e-4f,
            FarPlane = 1e5f,
        };

        Matrix4x4 projection = camera.CreateProjectionMatrix(1000.0f);

        AssertFinite(projection);

        // The near plane maps to depth 0 and the far plane to depth 1 for the
        // documented [0, 1] clip range.
        Vector4 near = Vector4.Transform(
            new Vector4(0.0f, 0.0f, -camera.NearPlane, 1.0f),
            projection);
        Vector4 far = Vector4.Transform(
            new Vector4(0.0f, 0.0f, -camera.FarPlane, 1.0f),
            projection);

        Assert.True(near.W > 0.0f && far.W > 0.0f);
        Assert.InRange(near.Z / near.W, -1e-4f, 1e-4f);
        Assert.InRange(far.Z / far.W, 1.0f - 1e-3f, 1.0f + 1e-3f);
    }

    [Fact]
    public void Projection_MapsDepthsIntoTheZeroToOneRange()
    {
        var camera = new Camera(Vector3.Zero);

        Matrix4x4 projection = camera.CreateProjectionMatrix(1.6f);

        Vector4 near = Vector4.Transform(
            new Vector4(0.0f, 0.0f, -camera.NearPlane, 1.0f),
            projection);
        Vector4 middle = Vector4.Transform(new Vector4(0.0f, 0.0f, -50.0f, 1.0f), projection);
        Vector4 far = Vector4.Transform(
            new Vector4(0.0f, 0.0f, -camera.FarPlane, 1.0f),
            projection);

        Assert.InRange(near.Z / near.W, -1e-5f, 1e-5f);
        Assert.InRange(middle.Z / middle.W, 0.0f, 1.0f);
        Assert.InRange(far.Z / far.W, 1.0f - 1e-4f, 1.0f + 1e-4f);
    }

    [Fact]
    public void Move_WithValidState_KeepsMatricesFinite()
    {
        var camera = new Camera(Vector3.Zero)
        {
            MoveSpeed = 4.0f,
            SprintMultiplier = 3.0f,
        };

        camera.Move(new Vector3(1.0f, 0.0f, 1.0f), 0.016f, sprint: true);

        AssertFinite(camera.ViewMatrix);
        AssertFinite(camera.CreateProjectionMatrix(1.6f));
    }

    private static void AssertFinite(Matrix4x4 value)
    {
        Assert.True(float.IsFinite(value.M11) && float.IsFinite(value.M12));
        Assert.True(float.IsFinite(value.M13) && float.IsFinite(value.M14));
        Assert.True(float.IsFinite(value.M21) && float.IsFinite(value.M22));
        Assert.True(float.IsFinite(value.M23) && float.IsFinite(value.M24));
        Assert.True(float.IsFinite(value.M31) && float.IsFinite(value.M32));
        Assert.True(float.IsFinite(value.M33) && float.IsFinite(value.M34));
        Assert.True(float.IsFinite(value.M41) && float.IsFinite(value.M42));
        Assert.True(float.IsFinite(value.M43) && float.IsFinite(value.M44));
    }
}
