using System.Numerics;
using Nymeria.Core.Lighting;

namespace Nymeria.Core.Tests;

public sealed class LightingMathTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void Lambert_FrontFacingNormalIsFullyLit()
    {
        float diffuse = LightingMath.ComputeLambert(Vector3.UnitZ, Vector3.UnitZ);

        Assert.Equal(1.0f, diffuse, 5);
    }

    [Fact]
    public void Lambert_PerpendicularNormalIsUnlit()
    {
        float diffuse = LightingMath.ComputeLambert(Vector3.UnitZ, Vector3.UnitX);

        Assert.Equal(0.0f, diffuse, 5);
    }

    [Fact]
    public void Lambert_BackFacingNormalIsClampedToZero()
    {
        float diffuse = LightingMath.ComputeLambert(Vector3.UnitZ, -Vector3.UnitZ);

        Assert.Equal(0.0f, diffuse, 5);
    }

    [Fact]
    public void Lambert_NormalizesNonUnitInputs()
    {
        float diffuse = LightingMath.ComputeLambert(
            new Vector3(0.0f, 0.0f, 8.0f),
            new Vector3(0.0f, 0.0f, 3.0f));

        Assert.Equal(1.0f, diffuse, 5);
    }

    [Theory]
    [InlineData(0.0f, 0.0f, 0.0f)]
    [InlineData(float.NaN, 0.0f, 0.0f)]
    public void Lambert_ReturnsZeroForInvalidNormal(float x, float y, float z)
    {
        Assert.Equal(
            0.0f,
            LightingMath.ComputeLambert(new Vector3(x, y, z), Vector3.UnitZ));
    }

    [Fact]
    public void Lambert_ReturnsZeroForNonFiniteLightDirection()
    {
        Assert.Equal(
            0.0f,
            LightingMath.ComputeLambert(
                Vector3.UnitZ,
                new Vector3(float.PositiveInfinity, 0.0f, 0.0f)));
    }

    [Fact]
    public void AmbientAndDirectional_CombineOnAFrontFacingSurface()
    {
        var ambient = new AmbientLight(new Vector3(0.2f, 0.4f, 0.6f), 0.5f);
        var directional = new DirectionalLight(
            new Vector3(0.0f, 0.0f, -1.0f),
            new Vector3(1.0f, 0.5f, 0.25f),
            2.0f);

        float diffuse = LightingMath.ComputeLambert(
            Vector3.UnitZ,
            directional.DirectionToLight);
        Vector3 combined =
            ambient.EffectiveColor +
            (directional.EffectiveColor * diffuse);

        Assert.Equal(1.0f, diffuse, 5);
        Assert.Equal(new Vector3(2.1f, 1.2f, 0.8f), combined);
    }

    [Fact]
    public void NormalMatrix_ForIdentityIsIdentity()
    {
        bool success = LightingMath.TryCreateNormalMatrix(
            Matrix4x4.Identity,
            out Matrix4x4 normalMatrix);

        Assert.True(success);
        Assert.Equal(Matrix4x4.Identity, normalMatrix);
    }

    [Fact]
    public void NormalMatrix_RotatesNormalsWithoutScaling()
    {
        Matrix4x4 model = Matrix4x4.CreateScale(2.0f, 1.0f, 0.5f) *
                          Matrix4x4.CreateRotationY(MathF.PI / 2.0f);

        Assert.True(LightingMath.TryCreateNormalMatrix(model, out Matrix4x4 normalMatrix));

        Vector3 transformed = Vector3.TransformNormal(Vector3.UnitX, normalMatrix);
        transformed = Vector3.Normalize(transformed);

        // Row-vector +Y rotation maps +X to -Z, and the non-uniform scale must
        // not leak into the normal.
        AssertVectorNear(new Vector3(0.0f, 0.0f, -1.0f), transformed);
    }

    [Fact]
    public void NormalMatrix_UsesInverseTransposeForNonUniformScale()
    {
        Matrix4x4 model = Matrix4x4.CreateScale(2.0f, 1.0f, 0.5f);

        Assert.True(LightingMath.TryCreateNormalMatrix(model, out Matrix4x4 normalMatrix));

        Vector3 transformed = Vector3.TransformNormal(
            Vector3.Normalize(new Vector3(1.0f, 1.0f, 0.0f)),
            normalMatrix);
        transformed = Vector3.Normalize(transformed);

        AssertVectorNear(
            Vector3.Normalize(new Vector3(0.5f, 1.0f, 0.0f)),
            transformed);
    }

    [Fact]
    public void NormalMatrix_IgnoresTranslation()
    {
        var model = Matrix4x4.CreateTranslation(10.0f, -4.0f, 3.0f);

        Assert.True(LightingMath.TryCreateNormalMatrix(model, out Matrix4x4 normalMatrix));

        Vector3 transformed = Vector3.TransformNormal(Vector3.UnitY, normalMatrix);

        AssertVectorNear(Vector3.UnitY, transformed);
    }

    [Fact]
    public void NormalMatrix_RejectsSingularTransform()
    {
        Matrix4x4 model = Matrix4x4.CreateScale(Vector3.Zero);

        bool success = LightingMath.TryCreateNormalMatrix(
            model,
            out Matrix4x4 normalMatrix);

        Assert.False(success);
        Assert.Equal(default, normalMatrix);
    }

    [Fact]
    public void NormalMatrix_RejectsNonFiniteTransform()
    {
        Matrix4x4 model = Matrix4x4.Identity;
        model.M11 = float.NaN;

        bool success = LightingMath.TryCreateNormalMatrix(
            model,
            out Matrix4x4 normalMatrix);

        Assert.False(success);
        Assert.Equal(default, normalMatrix);
    }

    private static void AssertVectorNear(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0.0f, Tolerance);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0.0f, Tolerance);
        Assert.InRange(MathF.Abs(expected.Z - actual.Z), 0.0f, Tolerance);
    }
}
