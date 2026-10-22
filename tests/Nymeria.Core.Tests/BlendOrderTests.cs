using System.Numerics;
using Nymeria.Core.Rendering;

namespace Nymeria.Core.Tests;

public sealed class BlendOrderTests
{
    private static readonly Vector3 Camera = new(0.0f, 0.0f, 10.0f);

    [Fact]
    public void SortBackToFront_LeavesAnEmptySpanEmpty()
    {
        BlendOrder.SortBackToFront(Span<BlendOrderEntry>.Empty, Camera);
    }

    [Fact]
    public void SortBackToFront_LeavesASingleEntryUnchanged()
    {
        Span<BlendOrderEntry> entries = [new BlendOrderEntry(7, new Vector3(1.0f, 2.0f, 3.0f))];

        BlendOrder.SortBackToFront(entries, Camera);

        Assert.Equal(7, entries[0].Index);
    }

    [Fact]
    public void SortBackToFront_PlacesFartherEntriesFirst()
    {
        Span<BlendOrderEntry> entries =
        [
            new BlendOrderEntry(0, new Vector3(0.0f, 0.0f, 9.0f)),
            new BlendOrderEntry(1, new Vector3(0.0f, 0.0f, -8.0f)),
            new BlendOrderEntry(2, new Vector3(0.0f, 0.0f, 3.0f)),
        ];

        BlendOrder.SortBackToFront(entries, Camera);

        Assert.Equal(new[] { 1, 2, 0 }, Indices(entries));
    }

    [Fact]
    public void SortBackToFront_BreaksTiesByAscendingIndex()
    {
        Span<BlendOrderEntry> entries =
        [
            new BlendOrderEntry(9, new Vector3(2.0f, 0.0f, 4.0f)),
            new BlendOrderEntry(3, new Vector3(-2.0f, 0.0f, 4.0f)),
            new BlendOrderEntry(5, new Vector3(0.0f, 2.0f, 4.0f)),
        ];

        BlendOrder.SortBackToFront(entries, Camera);

        Assert.Equal(new[] { 3, 5, 9 }, Indices(entries));
    }

    [Fact]
    public void SortBackToFront_IsIndependentOfTheIncomingOrder()
    {
        BlendOrderEntry[] source =
        [
            new(4, new Vector3(0.0f, 0.0f, 8.0f)),
            new(1, new Vector3(0.0f, 0.0f, -6.0f)),
            new(3, new Vector3(3.0f, 0.0f, 2.0f)),
            new(2, new Vector3(-3.0f, 0.0f, 2.0f)),
            new(0, new Vector3(0.0f, 0.0f, 9.5f)),
        ];

        int[] expected = Sort(source);
        int[] reversed = Sort([.. source.Reverse()]);
        int[] rotated = Sort([.. source[2..], .. source[..2]]);

        Assert.Equal(expected, reversed);
        Assert.Equal(expected, rotated);
        Assert.Equal(new[] { 1, 2, 3, 4, 0 }, expected);
    }

    [Fact]
    public void SortBackToFront_OrdersNonFiniteDistancesDeterministically()
    {
        Span<BlendOrderEntry> entries =
        [
            new BlendOrderEntry(0, new Vector3(float.NaN, 0.0f, 0.0f)),
            new BlendOrderEntry(1, new Vector3(0.0f, 0.0f, 0.0f)),
            new BlendOrderEntry(2, new Vector3(0.0f, float.PositiveInfinity, 0.0f)),
        ];

        BlendOrder.SortBackToFront(entries, Camera);

        // Non-finite distances are drawn before finite ones, and two non-finite
        // distances tie and fall back to the entry index.
        Assert.Equal(new[] { 0, 2, 1 }, Indices(entries));

        BlendOrderEntry[] again = entries.ToArray();
        BlendOrder.SortBackToFront(again, Camera);
        Assert.Equal(new[] { 0, 2, 1 }, Indices(again));
    }

    [Fact]
    public void SortBackToFront_RejectsANonFiniteCameraPosition()
    {
        BlendOrderEntry[] entries = [new BlendOrderEntry(0, Vector3.Zero)];

        Assert.Throws<ArgumentOutOfRangeException>(() => BlendOrder.SortBackToFront(
            entries,
            new Vector3(float.NaN, 0.0f, 0.0f)));
    }

    [Fact]
    public void Compare_OrdersByDrawSequence()
    {
        var near = new BlendOrderEntry(0, new Vector3(0.0f, 0.0f, 9.0f));
        var far = new BlendOrderEntry(1, new Vector3(0.0f, 0.0f, 0.0f));

        Assert.True(BlendOrder.Compare(far, near, Camera) < 0);
        Assert.True(BlendOrder.Compare(near, far, Camera) > 0);
        Assert.Equal(0, BlendOrder.Compare(near, near, Camera));
    }

    private static int[] Sort(BlendOrderEntry[] entries)
    {
        BlendOrder.SortBackToFront(entries, Camera);
        return Indices(entries);
    }

    private static int[] Indices(Span<BlendOrderEntry> entries)
    {
        var indices = new int[entries.Length];

        for (int i = 0; i < entries.Length; i++)
        {
            indices[i] = entries[i].Index;
        }

        return indices;
    }
}
