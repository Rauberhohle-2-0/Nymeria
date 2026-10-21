using System.Numerics;
using Nymeria.Core.Collision;
using Nymeria.Core.Models;

namespace Nymeria.Core.Tests;

public sealed class CollisionIntersectionsTests
{
    private static readonly BoundingBox UnitBox = new(Vector3.Zero, Vector3.One);

    [Fact]
    public void BoxesOverlap_Separated()
    {
        var other = new BoundingBox(
            new Vector3(1.001f, 0.0f, 0.0f),
            new Vector3(2.0f, 1.0f, 1.0f));

        Assert.False(CollisionIntersections.Overlaps(UnitBox, other));
    }

    [Fact]
    public void BoxesOverlap_Overlapping()
    {
        var other = new BoundingBox(
            new Vector3(0.5f, -0.5f, 0.25f),
            new Vector3(1.5f, 0.5f, 1.25f));

        Assert.True(CollisionIntersections.Overlaps(UnitBox, other));
        Assert.True(CollisionIntersections.Overlaps(other, UnitBox));
    }

    [Fact]
    public void BoxesOverlap_TouchingFaceEdgeAndCorner()
    {
        var face = new BoundingBox(
            new Vector3(1.0f, 0.0f, 0.0f),
            new Vector3(2.0f, 1.0f, 1.0f));
        var edge = new BoundingBox(
            new Vector3(1.0f, 1.0f, 0.0f),
            new Vector3(2.0f, 2.0f, 1.0f));
        var corner = new BoundingBox(
            new Vector3(1.0f, 1.0f, 1.0f),
            new Vector3(2.0f, 2.0f, 2.0f));

        Assert.True(CollisionIntersections.Overlaps(UnitBox, face));
        Assert.True(CollisionIntersections.Overlaps(UnitBox, edge));
        Assert.True(CollisionIntersections.Overlaps(UnitBox, corner));
    }

    [Fact]
    public void BoxesOverlap_ContainedAndIdentical()
    {
        var contained = new BoundingBox(
            new Vector3(0.25f, 0.25f, 0.25f),
            new Vector3(0.75f, 0.75f, 0.75f));

        Assert.True(CollisionIntersections.Overlaps(UnitBox, contained));
        Assert.True(CollisionIntersections.Overlaps(contained, UnitBox));
        Assert.True(CollisionIntersections.Overlaps(UnitBox, UnitBox));
    }

    [Fact]
    public void BoxesOverlap_DegenerateBoxesAreStillTested()
    {
        var flat = new BoundingBox(Vector3.Zero, new Vector3(1.0f, 0.0f, 1.0f));
        var point = new BoundingBox(
            new Vector3(0.5f, 0.0f, 0.5f),
            new Vector3(0.5f, 0.0f, 0.5f));
        var outside = new BoundingBox(
            new Vector3(2.0f, 0.0f, 0.5f),
            new Vector3(3.0f, 0.0f, 1.5f));

        Assert.True(CollisionIntersections.Overlaps(flat, point));
        Assert.True(CollisionIntersections.Overlaps(point, flat));
        Assert.False(CollisionIntersections.Overlaps(flat, outside));
    }

    [Fact]
    public void SpheresOverlap_SeparatedTouchingAndContained()
    {
        var center = new BoundingSphere(Vector3.Zero, 1.0f);

        Assert.False(CollisionIntersections.Overlaps(
            center,
            new BoundingSphere(new Vector3(2.001f, 0.0f, 0.0f), 1.0f)));
        Assert.True(CollisionIntersections.Overlaps(
            center,
            new BoundingSphere(new Vector3(2.0f, 0.0f, 0.0f), 1.0f)));
        Assert.True(CollisionIntersections.Overlaps(
            center,
            new BoundingSphere(new Vector3(0.1f, 0.0f, 0.0f), 0.2f)));
    }

    [Fact]
    public void SpheresOverlap_ZeroRadiusIsAPoint()
    {
        var boxSphere = new BoundingSphere(Vector3.Zero, 1.0f);

        Assert.True(CollisionIntersections.Overlaps(
            boxSphere,
            new BoundingSphere(Vector3.Zero, 0.0f)));
        Assert.True(CollisionIntersections.Overlaps(
            boxSphere,
            new BoundingSphere(Vector3.UnitX, 0.0f)));
        Assert.False(CollisionIntersections.Overlaps(
            boxSphere,
            new BoundingSphere(new Vector3(1.001f, 0.0f, 0.0f), 0.0f)));
        Assert.True(CollisionIntersections.Overlaps(
            new BoundingSphere(new Vector3(5.0f, 5.0f, 5.0f), 0.0f),
            new BoundingSphere(new Vector3(5.0f, 5.0f, 5.0f), 0.0f)));
    }

    [Fact]
    public void SpheresOverlap_DoesNotOverflowAtExtremeCoordinates()
    {
        var first = new BoundingSphere(
            new Vector3(float.MaxValue, 0.0f, 0.0f),
            float.MaxValue);
        var second = new BoundingSphere(
            new Vector3(-float.MaxValue, 0.0f, 0.0f),
            float.MaxValue);

        Assert.True(CollisionIntersections.Overlaps(first, second));
    }

    [Fact]
    public void BoxSphereOverlap_SeparatedTouchingAndContained()
    {
        var touching = new BoundingSphere(new Vector3(2.0f, 0.5f, 0.5f), 1.0f);
        var separated = new BoundingSphere(new Vector3(2.001f, 0.5f, 0.5f), 1.0f);
        var inside = new BoundingSphere(new Vector3(0.5f, 0.5f, 0.5f), 0.1f);

        Assert.True(CollisionIntersections.Overlaps(UnitBox, touching));
        Assert.True(CollisionIntersections.Overlaps(touching, UnitBox));
        Assert.False(CollisionIntersections.Overlaps(UnitBox, separated));
        Assert.True(CollisionIntersections.Overlaps(UnitBox, inside));
    }

    [Fact]
    public void BoxSphereOverlap_SphereCanContainTheBox()
    {
        var sphere = new BoundingSphere(new Vector3(0.5f, 0.5f, 0.5f), 10.0f);

        Assert.True(CollisionIntersections.Overlaps(UnitBox, sphere));
        Assert.True(CollisionIntersections.Overlaps(sphere, UnitBox));
    }

    [Fact]
    public void BoxSphereOverlap_ZeroRadiusUsesTheClosestPoint()
    {
        Assert.True(CollisionIntersections.Overlaps(
            UnitBox,
            new BoundingSphere(new Vector3(0.5f, 0.5f, 0.5f), 0.0f)));
        Assert.True(CollisionIntersections.Overlaps(
            UnitBox,
            new BoundingSphere(Vector3.One, 0.0f)));
        Assert.False(CollisionIntersections.Overlaps(
            UnitBox,
            new BoundingSphere(new Vector3(1.001f, 0.5f, 0.5f), 0.0f)));
    }

    [Fact]
    public void ShapeOverlap_DispatchesAndRejectsEmptyShapes()
    {
        CollisionShape box = CollisionShape.FromBox(UnitBox);
        CollisionShape sphere = CollisionShape.FromSphere(
            new BoundingSphere(new Vector3(2.0f, 0.5f, 0.5f), 1.0f));

        Assert.True(CollisionIntersections.Overlaps(box, box));
        Assert.True(CollisionIntersections.Overlaps(box, sphere));
        Assert.True(CollisionIntersections.Overlaps(sphere, box));
        Assert.True(CollisionIntersections.Overlaps(sphere, sphere));
        Assert.False(CollisionIntersections.Overlaps(default(CollisionShape), box));
        Assert.False(CollisionIntersections.Overlaps(box, default(CollisionShape)));
        Assert.False(CollisionIntersections.Overlaps(
            default(CollisionShape),
            default(CollisionShape)));
    }
}
