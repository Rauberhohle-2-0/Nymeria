namespace Nymeria.Core.Streaming;

/// <summary>
/// Observable lifecycle state of one scene chunk.
/// </summary>
/// <remarks>
/// Loading and unloading are synchronous in this implementation, so they have no
/// observable intermediate state. A load either completes and becomes
/// <see cref="Loaded"/> or fails and becomes <see cref="Failed"/> within the
/// update that attempted it. An unload completes and becomes
/// <see cref="Unloaded"/> within the same update.
/// </remarks>
public enum SceneChunkState
{
    /// <summary>
    /// The chunk has no objects in the scene and is not being retried.
    /// </summary>
    Unloaded,

    /// <summary>
    /// The chunk's objects are active in the scene.
    /// </summary>
    Loaded,

    /// <summary>
    /// The most recent load attempt failed. The chunk has no objects in the
    /// scene and may be retried after the configured delay.
    /// </summary>
    Failed,
}
