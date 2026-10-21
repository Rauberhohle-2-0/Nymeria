namespace Nymeria.Core.Streaming;

/// <summary>
/// Work performed by one streaming update.
/// </summary>
/// <remarks>
/// The counts describe chunk transitions, not object or GPU-resource counts.
/// <see cref="FailedLoads"/> counts failed attempts, including retries.
/// <see cref="ReleaseFailures"/> counts exceptions from chunk release callbacks;
/// those chunks were still removed from the scene. <see cref="FirstError"/> is a
/// bounded diagnostic message for the first failure in this update, or null when
/// no failure occurred.
/// </remarks>
public readonly record struct SceneStreamingUpdateResult(
    int LoadedChunks,
    int UnloadedChunks,
    int FailedLoads,
    int ReleaseFailures,
    string? FirstError)
{
    /// <summary>
    /// Gets whether this update changed the scene or encountered an error.
    /// </summary>
    public bool HasChanges =>
        LoadedChunks != 0 ||
        UnloadedChunks != 0 ||
        FailedLoads != 0 ||
        ReleaseFailures != 0;
}
