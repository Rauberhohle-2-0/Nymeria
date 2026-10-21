namespace Nymeria.Core.Lod;

/// <summary>
/// A fixed-size snapshot of how many drawn instances used each LOD level in one
/// frame.
/// </summary>
/// <remarks>
/// <para>
/// The value is a copy, so it remains valid after the renderer starts a new
/// frame. Only instances whose LOD configuration was active and whose draw was
/// submitted are counted. Instances without a configuration are not counted.
/// The sum of every level is the frame's
/// <c>VisibilityStatistics.LodDrawnInstances</c>.
/// </para>
/// <para>
/// The array is not exposed so the snapshot cannot alias mutable renderer
/// state. Indexing outside <c>0 .. MeshLod.MaxLevels - 1</c> throws.
/// </para>
/// </remarks>
public readonly struct LodLevelUsage
{
    private readonly int _level0;
    private readonly int _level1;
    private readonly int _level2;
    private readonly int _level3;
    private readonly int _level4;
    private readonly int _level5;
    private readonly int _level6;
    private readonly int _level7;

    /// <summary>
    /// Creates a snapshot from up to <see cref="MeshLod.MaxLevels"/> counts.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="counts"/> is longer than <see cref="MeshLod.MaxLevels"/>.
    /// </exception>
    public LodLevelUsage(ReadOnlySpan<int> counts)
    {
        if (counts.Length > MeshLod.MaxLevels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(counts),
                counts.Length,
                $"LOD usage accepts at most {MeshLod.MaxLevels} levels.");
        }

        for (int index = 0; index < counts.Length; index++)
        {
            if (counts[index] < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(counts),
                    counts[index],
                    $"LOD usage count {index} must be nonnegative.");
            }
        }

        _level0 = Read(counts, 0);
        _level1 = Read(counts, 1);
        _level2 = Read(counts, 2);
        _level3 = Read(counts, 3);
        _level4 = Read(counts, 4);
        _level5 = Read(counts, 5);
        _level6 = Read(counts, 6);
        _level7 = Read(counts, 7);
    }

    /// <summary>
    /// Gets the number of drawn instances that used the supplied level.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="level"/> is outside the supported range.
    /// </exception>
    public int this[int level] => level switch
    {
        0 => _level0,
        1 => _level1,
        2 => _level2,
        3 => _level3,
        4 => _level4,
        5 => _level5,
        6 => _level6,
        7 => _level7,
        _ => throw new ArgumentOutOfRangeException(
            nameof(level),
            level,
            $"A LOD level index must be between 0 and {MeshLod.MaxLevels - 1}."),
    };

    /// <summary>
    /// Gets the number of counted drawn instances across all levels.
    /// </summary>
    public int Total =>
        _level0 + _level1 + _level2 + _level3 +
        _level4 + _level5 + _level6 + _level7;

    private static int Read(ReadOnlySpan<int> counts, int index)
    {
        return index < counts.Length ? counts[index] : 0;
    }
}
