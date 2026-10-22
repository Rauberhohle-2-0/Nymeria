using System.Numerics;
using Nymeria.Core.Culling;
using Nymeria.Core.Models;

namespace Nymeria.Core.Tests;

public sealed class FrustumTests
{
    private const float AspectRatio = 1.6f;
    private const float HalfFieldOfView = MathF.PI / 8.0f;
    private const float NearPlane = 0.1f;
    private const float FarPlane = 100.0f;

    // At z = -10 the view is 2 * 10 * tan(22.5 degrees) tall and 1.6x as wide.
    private const float HalfHeightAtTen = 10.0f * 0.41421356f;
    private const float HalfWidthAtTen = HalfHeightAtTen * AspectRatio;

    [Fact]
    public void BoxAtViewCenter_IsVisible()
    {
        Frustum frustum = CreateFrustum(Vector3.Zero, -Vector3.UnitZ);

        Assert.True(frustum.Intersects(Box(new Vector3(0.0f, 0.0f, -10.0f), 1.0f)));
    }

    [Fact]
    public void BoxBehindCamera_IsCulled()
    {
        Frustum frustum = CreateFrustum(Vector3.Zero, -Vector3.UnitZ);

        Assert.False(frustum.Intersects(Box(new Vector3(0.0f, 0.0f, 10.0f), 1.0f)));
    }

    [Fact]
    public void BoxOutsideLeftPlane_IsCulled()
    {
        Frustum frustum = CreateFrustum(Vector3.Zero, -Vector3.UnitZ);

        Assert.False(frustum.Intersects(Box(
            new Vector3(-HalfWidthAtTen - 2.0f, 0.0f, -10.0f),
            1.0f)));
    }

    [Fact]
    public void BoxOutsideRightPlane_IsCulled()
    {
        Frustum frustum = CreateFrustum(Vector3.Zero, -Vector3.UnitZ);

        Assert.False(frustum.Intersects(Box(
            new Vector3(HalfWidthAtTen + 2.0f, 0.0f, -10.0f),
            1.0f)));
    }

    [Fact]
    public void BoxOutsideTopPlane_IsCulled()
    {
        Frustum frustum = CreateFrustum(Vector3.Zero, -Vector3.UnitZ);

        Assert.False(frustum.Intersects(Box(
            new Vector3(0.0f, HalfHeightAtTen + 2.0f, -10.0f),
            1.0f)));
    }

    [Fact]
    public void BoxOutsideBottomPlane_IsCulled()
    {
        Frustum frustum = CreateFrustum(Vector3.Zero, -Vector3.UnitZ);

        Assert.False(frustum.Intersects(Box(
            new Vector3(0.0f, -HalfHeightAtTen - 2.0f, -10.0f),
            1.0f)));
    }

    [Fact]
    public void BoxInFrontOfNearPlane_IsCulled()
    {
        Frustum frustum = CreateFrustum(Vector3.Zero, -Vector3.UnitZ);

        Assert.False(frustum.Intersects(Box(new Vector3(0.0f, 0.0f, -0.05f), 0.01f)));
    }

    [Fact]
    public void BoxBeyondFarPlane_IsCulled()
    {
        Frustum frustum = CreateFrustum(Vector3.Zero, -Vector3.UnitZ);

        Assert.False(frustum.Intersects(Box(new Vector3(0.0f, 0.0f, -120.0f), 1.0f)));
    }

    [Fact]
    public void BoxCrossingNearPlane_IsVisible()
    {
        Frustum frustum = CreateFrustum(Vector3.Zero, -Vector3.UnitZ);

        Assert.True(frustum.Intersects(Box(new Vector3(0.0f, 0.0f, -NearPlane), 0.01f)));
    }

    [Fact]
    public void BoxCrossingFarPlane_IsVisible()
    {
        Frustum frustum = CreateFrustum(Vector3.Zero, -Vector3.UnitZ);

        Assert.True(frustum.Intersects(Box(new Vector3(0.0f, 0.0f, -FarPlane), 1.0f)));
    }

    [Fact]
    public void BoxCrossingRightPlane_IsVisible()
    {
        Frustum frustum = CreateFrustum(Vector3.Zero, -Vector3.UnitZ);

        Assert.True(frustum.Intersects(Box(
            new Vector3(HalfWidthAtTen, 0.0f, -10.0f),
            1.0f)));
    }

    [Fact]
    public void BoxIntersectingFrustumNearCorner_IsVisible()
    {
        Frustum frustum = CreateFrustum(Vector3.Zero, -Vector3.UnitZ);

        // The box's right/top corners are outside two planes, but it still
        // overlaps the frustum corner, so no plane rejects every corner.
        Assert.True(frustum.Intersects(Box(new Vector3(7.0f, 5.0f, -10.0f), 2.0f)));
    }

    [Fact]
    public void BoxEnclosingFrustum_IsVisible()
    {
        Frustum frustum = CreateFrustum(Vector3.Zero, -Vector3.UnitZ);

        Assert.True(frustum.Intersects(Box(new Vector3(0.0f, 0.0f, -10.0f), 500.0f)));
    }

    [Fact]
    public void CameraTranslation_ChangesVisibility()
    {
        Frustum frustum = CreateFrustum(
            new Vector3(0.0f, 0.0f, -50.0f),
            new Vector3(0.0f, 0.0f, -60.0f));

        Assert.True(frustum.Intersects(Box(new Vector3(0.0f, 0.0f, -60.0f), 1.0f)));
        Assert.False(frustum.Intersects(Box(Vector3.Zero, 1.0f)));
    }

    [Fact]
    public void CameraRotation_ChangesVisibility()
    {
        Frustum lookingRight = CreateFrustum(Vector3.Zero, Vector3.UnitX);

        Assert.True(lookingRight.Intersects(Box(new Vector3(10.0f, 0.0f, 0.0f), 1.0f)));
        Assert.False(lookingRight.Intersects(Box(new Vector3(0.0f, 0.0f, -10.0f), 1.0f)));
    }

    [Fact]
    public void UniformMatrixScale_DoesNotChangeCulling()
    {
        Matrix4x4 viewProjection = CreateViewProjection(Vector3.Zero, -Vector3.UnitZ);
        Frustum normal = Frustum.Create(viewProjection);
        Frustum scaled = Frustum.Create(viewProjection * 2.0f);
        BoundingBox outside = Box(new Vector3(0.0f, 0.0f, 10.0f), 1.0f);
        BoundingBox inside = Box(new Vector3(0.0f, 0.0f, -10.0f), 1.0f);

        Assert.False(normal.Intersects(outside));
        Assert.False(scaled.Intersects(outside));
        Assert.True(normal.Intersects(inside));
        Assert.True(scaled.Intersects(inside));
    }

    [Fact]
    public void IdentityMatrix_UsesClipSpaceBounds()
    {
        Frustum frustum = Frustum.Create(Matrix4x4.Identity);

        Assert.False(frustum.IsUnbounded);
        Assert.True(frustum.Intersects(Box(Vector3.Zero, 0.5f)));
        Assert.False(frustum.Intersects(Box(new Vector3(0.0f, 0.0f, -5.0f), 0.5f)));
    }

    [Fact]
    public void NewFrustum_IsUnbounded()
    {
        // A scratch frustum has no planes until it is updated. It must remain
        // conservative rather than treating every box as outside.
        var frustum = new Frustum();

        Assert.True(frustum.IsUnbounded);
        Assert.True(frustum.Intersects(Box(new Vector3(0.0f, 0.0f, 10.0f), 1.0f)));
    }

    [Fact]
    public void Update_RefreshesReusedPlanes()
    {
        var frustum = new Frustum();
        BoundingBox outside = Box(new Vector3(0.0f, 0.0f, 10.0f), 1.0f);
        BoundingBox inside = Box(new Vector3(0.0f, 0.0f, -10.0f), 1.0f);

        frustum.Update(CreateViewProjection(Vector3.Zero, -Vector3.UnitZ));

        Assert.False(frustum.IsUnbounded);
        Assert.False(frustum.Intersects(outside));
        Assert.True(frustum.Intersects(inside));

        // Refresh the same instance with a view facing the opposite direction.
        frustum.Update(CreateViewProjection(Vector3.Zero, Vector3.UnitZ));

        Assert.False(frustum.IsUnbounded);
        Assert.True(frustum.Intersects(outside));
        Assert.False(frustum.Intersects(inside));
    }

    [Fact]
    public void Update_WithInvalidMatrix_BecomesUnbounded()
    {
        Frustum frustum = CreateFrustum(Vector3.Zero, -Vector3.UnitZ);
        BoundingBox outside = Box(new Vector3(0.0f, 0.0f, 10.0f), 1.0f);

        Assert.False(frustum.Intersects(outside));

        frustum.Update(default);

        Assert.True(frustum.IsUnbounded);
        Assert.True(frustum.Intersects(outside));
    }

    [Fact]
    public void ZeroMatrix_IsUnbounded()
    {
        Frustum frustum = Frustum.Create(new Matrix4x4());

        Assert.True(frustum.IsUnbounded);
        Assert.True(frustum.Intersects(Box(new Vector3(0.0f, 0.0f, 10.0f), 1.0f)));
    }

    [Fact]
    public void NonFiniteMatrix_IsUnbounded()
    {
        Matrix4x4 viewProjection = CreateViewProjection(Vector3.Zero, -Vector3.UnitZ);
        viewProjection.M22 = float.NaN;

        Frustum frustum = Frustum.Create(viewProjection);

        Assert.True(frustum.IsUnbounded);
        Assert.True(frustum.Intersects(Box(new Vector3(0.0f, 0.0f, 10.0f), 1.0f)));
    }

    [Fact]
    public void SingularMatrix_IsUnbounded()
    {
        // A projection that collapses Y is singular. The conservative fallback
        // keeps every instance instead of risking a false cull.
        Matrix4x4 projection = Matrix4x4.CreateScale(1.0f, 0.0f, 1.0f);
        Frustum frustum = Frustum.Create(projection);

        Assert.True(frustum.IsUnbounded);
        Assert.True(frustum.Intersects(Box(new Vector3(0.0f, 0.0f, 10.0f), 1.0f)));
    }

    [Fact]
    public void DefaultBounds_AreAcceptedConservatively()
    {
        // A default BoundingBox is the zero-sized box at the origin, which is
        // on the near plane of the identity clip volume.
        Frustum frustum = Frustum.Create(Matrix4x4.Identity);

        Assert.True(frustum.Intersects(default));
    }

    [Fact]
    public void RandomBoxesContainingVisiblePoints_AreNeverCulled()
    {
        // Hand-picked boxes can miss a convention or sign error that a
        // cross-check against clip space exposes. For a seeded set of random
        // cameras, a box that encloses a point inside the clip volume must be
        // reported visible: the cull is conservative by contract.
        var random = new Random(1902);

        for (int cameraIndex = 0; cameraIndex < 24; cameraIndex++)
        {
            Vector3 eye = RandomVector(random, 10.0f);
            Vector3 target = eye + RandomVector(random, 1.0f);

            if (target == eye)
            {
                target = eye + Vector3.UnitZ;
            }

            Matrix4x4 viewProjection = CreateViewProjection(eye, target);
            Frustum frustum = Frustum.Create(viewProjection);

            for (int boxIndex = 0; boxIndex < 16; boxIndex++)
            {
                Vector3 center = RandomVector(random, 20.0f);
                Vector3 extent = new(
                    (float)random.NextDouble(),
                    (float)random.NextDouble(),
                    (float)random.NextDouble());
                var box = new BoundingBox(center - extent, center + extent);

                if (frustum.Intersects(box))
                {
                    continue;
                }

                for (int sample = 0; sample < 200; sample++)
                {
                    Vector3 point = center + new Vector3(
                        ((float)random.NextDouble() * 2.0f - 1.0f) * extent.X,
                        ((float)random.NextDouble() * 2.0f - 1.0f) * extent.Y,
                        ((float)random.NextDouble() * 2.0f - 1.0f) * extent.Z);
                    Vector4 clip = Vector4.Transform(new Vector4(point, 1.0f), viewProjection);

                    if (clip.W > 0.0f &&
                        MathF.Abs(clip.X) <= clip.W &&
                        MathF.Abs(clip.Y) <= clip.W &&
                        clip.Z >= 0.0f &&
                        clip.Z <= clip.W)
                    {
                        Assert.Fail(
                            $"The frustum culled a box around in-frustum point {point} " +
                            $"(clip {clip}) for camera {eye}.");
                    }
                }
            }
        }
    }

    private static Vector3 RandomVector(Random random, float scale)
    {
        return new Vector3(
            (float)((random.NextDouble() * 2.0 - 1.0) * scale),
            (float)((random.NextDouble() * 2.0 - 1.0) * scale),
            (float)((random.NextDouble() * 2.0 - 1.0) * scale));
    }

    private static Frustum CreateFrustum(Vector3 position, Vector3 target)
    {
        return Frustum.Create(CreateViewProjection(position, target));
    }

    private static Matrix4x4 CreateViewProjection(Vector3 position, Vector3 target)
    {
        Matrix4x4 view = Matrix4x4.CreateLookAt(position, target, Vector3.UnitY);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            HalfFieldOfView * 2.0f,
            AspectRatio,
            NearPlane,
            FarPlane);

        return view * projection;
    }

    private static BoundingBox Box(Vector3 center, float halfExtent)
    {
        return new BoundingBox(
            center - new Vector3(halfExtent),
            center + new Vector3(halfExtent));
    }
}
