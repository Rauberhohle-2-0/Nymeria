namespace Nymeria.Core.Streaming;

/// <summary>
/// Cumulative streaming counters and the current chunk and object totals.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="KnownChunks"/> equals <see cref="LoadedChunks"/> plus
/// <see cref="FailedChunks"/> plus <see cref="UnloadedChunks"/>.
/// </para>
/// <para>
/// <see cref="ActiveSceneObjects"/> counts objects owned by loaded chunks. It
/// does not include objects added to the scene outside the streamer. The
/// cumulative counters are monotonic for the streamer's lifetime and use
/// <see cref="long"/>, so a long-running session cannot wrap a 32-bit count.
/// </para>
/// </remarks>
public readonly record struct SceneStreamingStatistics(
    int KnownChunks,
    int LoadedChunks,
    int FailedChunks,
    int UnloadedChunks,
    int ActiveSceneObjects,
    long LoadAttempts,
    long SuccessfulLoads,
    long Unloads,
    long LoadFailures,
    long ReleaseFailures);
