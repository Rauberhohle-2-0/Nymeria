using System.Numerics;

namespace Nymeria.Core.Rendering;

/// <summary>
/// One entry in a blended-object draw order: a caller-defined index and the
/// world-space position used to sort it.
/// </summary>
/// <param name="Index">
/// The caller's entry index. It is returned unchanged and only breaks ties
/// between equal distances, so callers should supply a deterministic value such
/// as the scene index.
/// </param>
/// <param name="Position">
/// The world-space position the distance is measured from, normally the
/// transformed center of the instance bounds.
/// </param>
public readonly record struct BlendOrderEntry(int Index, Vector3 Position);

/// <summary>
/// Orders blended scene instances from farthest to nearest relative to a camera.
/// </summary>
/// <remarks>
/// <para>
/// Alpha blending is not order-independent, so a renderer that draws blended
/// instances without depth writes must submit them back to front. This helper is
/// the deterministic part of that ordering and contains no GPU or scene
/// dependency, so it can be tested headlessly.
/// </para>
/// <para>
/// Entries are sorted by squared distance from the camera, farthest first. Equal
/// distances are broken by ascending <see cref="BlendOrderEntry.Index"/>, which
/// makes the result independent of the incoming order and of the sort algorithm.
/// A non-finite squared distance, which only a non-finite position can produce,
/// is treated as farther than any finite distance; two non-finite distances tie
/// and fall back to the index. A non-finite camera position throws.
/// </para>
/// <para>
/// The sort is an insertion sort over the supplied span. Blended instance counts
/// are small in practice, and this avoids the delegate allocation that
/// <see cref="List{T}.Sort(Comparison{T})"/> performs per frame.
/// </para>
/// <para>
/// Limitation: this is object-level sorting by bounding-box center. Two
/// intersecting instances, or one instance that surrounds another, cannot be
/// ordered correctly per pixel. See <c>docs/MATERIALS.md</c>.
/// </para>
/// </remarks>
public static class BlendOrder
{
    /// <summary>
    /// Sorts the entries in place from farthest to nearest.
    /// </summary>
    /// <param name="entries">The entries to reorder.</param>
    /// <param name="cameraPosition">The camera position in world space.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="cameraPosition"/> contains a non-finite component.
    /// </exception>
    public static void SortBackToFront(Span<BlendOrderEntry> entries, Vector3 cameraPosition)
    {
        if (!IsFinite(cameraPosition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(cameraPosition),
                cameraPosition,
                "The camera position must contain only finite components.");
        }

        for (int i = 1; i < entries.Length; i++)
        {
            BlendOrderEntry entry = entries[i];
            int j = i - 1;

            while (j >= 0 && Compare(entries[j], entry, cameraPosition) > 0)
            {
                entries[j + 1] = entries[j];
                j--;
            }

            entries[j + 1] = entry;
        }
    }

    /// <summary>
    /// Compares two entries in draw order: negative or zero when
    /// <paramref name="left"/> is drawn before <paramref name="right"/>.
    /// </summary>
    /// <param name="left">The first entry.</param>
    /// <param name="right">The second entry.</param>
    /// <param name="cameraPosition">The camera position in world space.</param>
    /// <returns>A value that orders the entries for a back-to-front draw.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="cameraPosition"/> contains a non-finite component.
    /// </exception>
    public static int Compare(
        BlendOrderEntry left,
        BlendOrderEntry right,
        Vector3 cameraPosition)
    {
        if (!IsFinite(cameraPosition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(cameraPosition),
                cameraPosition,
                "The camera position must contain only finite components.");
        }

        // Farther first: compare the negated distances so a positive result means
        // "left is drawn later". NaN distances sort last.
        float leftDistance = DistanceSquared(left.Position, cameraPosition);
        float rightDistance = DistanceSquared(right.Position, cameraPosition);
        int byDistance = CompareDescending(leftDistance, rightDistance);

        return byDistance != 0 ? byDistance : left.Index.CompareTo(right.Index);
    }

    private static int CompareDescending(float left, float right)
    {
        bool leftFinite = float.IsFinite(left);
        bool rightFinite = float.IsFinite(right);

        if (!leftFinite || !rightFinite)
        {
            // A non-finite distance is treated as the farthest position, so it
            // is drawn first. Two non-finite distances tie.
            if (!leftFinite && !rightFinite)
            {
                return 0;
            }

            return leftFinite ? 1 : -1;
        }

        return right.CompareTo(left);
    }

    private static float DistanceSquared(Vector3 position, Vector3 cameraPosition)
    {
        Vector3 delta = position - cameraPosition;
        return Vector3.Dot(delta, delta);
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) &&
            float.IsFinite(value.Y) &&
            float.IsFinite(value.Z);
    }
}
