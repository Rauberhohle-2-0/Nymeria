using System.Numerics;
using Nymeria.Core.Scenes;

namespace Nymeria.Core.Tests;

public sealed class TransformTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void DefaultTransform_IsIdentity()
    {
        var transform = new Transform();

        Assert.Equal(Vector3.Zero, transform.Position);
        Assert.Equal(Quaternion.Identity, transform.Rotation);
        Assert.Equal(Vector3.One, transform.Scale);
        Assert.Equal(Matrix4x4.Identity, transform.CreateModelMatrix());
    }

    [Fact]
    public void CreateModelMatrix_ComposesScaleThenRotationThenTranslation()
    {
        var position = new Vector3(1.0f, 2.0f, 3.0f);
        Quaternion rotation = Quaternion.CreateFromYawPitchRoll(0.3f, -0.2f, 0.1f);
        var scale = new Vector3(2.0f, 3.0f, 4.0f);
        var transform = new Transform(position, rotation, scale);

        Matrix4x4 expected =
            Matrix4x4.CreateScale(scale) *
            Matrix4x4.CreateFromQuaternion(rotation) *
            Matrix4x4.CreateTranslation(position);

        Assert.Equal(expected, transform.CreateModelMatrix());
    }

    [Fact]
    public void Translation_MovesObjectSpaceOrigin()
    {
        var transform = new Transform(
            new Vector3(4.0f, -2.0f, 7.0f),
            Quaternion.Identity,
            Vector3.One);

        Vector3 result = Vector3.Transform(Vector3.Zero, transform.CreateModelMatrix());

        AssertVectorNear(new Vector3(4.0f, -2.0f, 7.0f), result);
    }

    [Fact]
    public void Rotation_IsAppliedBeforeTranslation()
    {
        Quaternion rotation = Quaternion.CreateFromAxisAngle(
            Vector3.UnitY,
            MathF.PI / 2.0f);
        var transform = new Transform(
            new Vector3(10.0f, 0.0f, 0.0f),
            rotation,
            Vector3.One);

        Vector3 result = Vector3.Transform(
            new Vector3(1.0f, 0.0f, 0.0f),
            transform.CreateModelMatrix());

        // Row vectors and right-handed +Y rotation map +X to -Z.
        AssertVectorNear(new Vector3(10.0f, 0.0f, -1.0f), result);
    }

    [Fact]
    public void NonUniformScale_IsAppliedInObjectSpaceBeforeRotation()
    {
        Quaternion rotation = Quaternion.CreateFromAxisAngle(
            Vector3.UnitZ,
            MathF.PI / 2.0f);
        var transform = new Transform(
            new Vector3(5.0f, 0.0f, 0.0f),
            rotation,
            new Vector3(2.0f, 3.0f, 1.0f));

        // (1, 1, 0) scales to (2, 3, 0), rotates counter-clockwise to
        // (-3, 2, 0), then translates to (2, 2, 0).
        Vector3 result = Vector3.Transform(
            new Vector3(1.0f, 1.0f, 0.0f),
            transform.CreateModelMatrix());

        AssertVectorNear(new Vector3(2.0f, 2.0f, 0.0f), result);
    }

    [Fact]
    public void RotationSetter_NormalizesQuaternion()
    {
        var transform = new Transform
        {
            Rotation = new Quaternion(0.0f, 0.0f, 0.0f, 2.0f),
        };

        Assert.Equal(1.0f, transform.Rotation.Length(), 5);
    }

    [Fact]
    public void ZeroScale_IsAllowed()
    {
        var transform = new Transform(
            Vector3.Zero,
            Quaternion.Identity,
            Vector3.Zero);

        Vector3 result = Vector3.Transform(
            new Vector3(1.0f, 2.0f, 3.0f),
            transform.CreateModelMatrix());

        AssertVectorNear(Vector3.Zero, result);
    }

    [Theory]
    [InlineData(float.NaN, 0.0f, 0.0f)]
    [InlineData(float.PositiveInfinity, 0.0f, 0.0f)]
    [InlineData(0.0f, float.NegativeInfinity, 0.0f)]
    [InlineData(0.0f, 0.0f, float.NaN)]
    public void Position_RejectsNonFiniteValues(float x, float y, float z)
    {
        var transform = new Transform();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            transform.Position = new Vector3(x, y, z));
    }

    [Theory]
    [InlineData(float.NaN, 1.0f, 1.0f)]
    [InlineData(float.PositiveInfinity, 1.0f, 1.0f)]
    [InlineData(1.0f, float.NegativeInfinity, 1.0f)]
    [InlineData(1.0f, 1.0f, float.NaN)]
    public void Scale_RejectsNonFiniteValues(float x, float y, float z)
    {
        var transform = new Transform();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            transform.Scale = new Vector3(x, y, z));
    }

    [Fact]
    public void Rotation_RejectsZeroLengthQuaternion()
    {
        var transform = new Transform();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            transform.Rotation = new Quaternion(0.0f, 0.0f, 0.0f, 0.0f));
    }

    [Fact]
    public void Rotation_RejectsNonFiniteQuaternion()
    {
        var transform = new Transform();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            transform.Rotation = new Quaternion(float.NaN, 0.0f, 0.0f, 1.0f));
    }

    private static void AssertVectorNear(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(MathF.Abs(expected.X - actual.X), 0.0f, Tolerance);
        Assert.InRange(MathF.Abs(expected.Y - actual.Y), 0.0f, Tolerance);
        Assert.InRange(MathF.Abs(expected.Z - actual.Z), 0.0f, Tolerance);
    }
}
