namespace Nymeria.Core.Streaming;

/// <summary>
/// Validated distances and work limits for one <see cref="SceneStreamer"/>.
/// </summary>
/// <remarks>
/// A chunk is demanded when its bounds are at or within
/// <see cref="LoadDistance"/> of the streaming focus. A loaded chunk is unloaded
/// only when its bounds are strictly beyond <see cref="UnloadDistance"/>.
/// Requiring <see cref="UnloadDistance"/> to be greater than
/// <see cref="LoadDistance"/> creates a hysteresis band that prevents repeated
/// load/unload transitions at a single boundary.
/// </remarks>
public sealed class SceneStreamingConfiguration
{
    /// <summary>
    /// The default maximum number of chunks loaded by one update.
    /// </summary>
    public const int DefaultMaxLoadsPerUpdate = 4;

    /// <summary>
    /// The default delay before a failed chunk is retried, in seconds.
    /// </summary>
    public const double DefaultRetryDelaySeconds = 5.0;

    /// <summary>
    /// Creates a validated streaming configuration.
    /// </summary>
    /// <param name="loadDistance">
    /// The nonnegative finite distance at which a chunk becomes demanded. The
    /// exact boundary is inclusive.
    /// </param>
    /// <param name="unloadDistance">
    /// The finite distance strictly beyond which a loaded chunk is unloaded. It
    /// must be greater than <paramref name="loadDistance"/>.
    /// </param>
    /// <param name="maxLoadsPerUpdate">
    /// The maximum number of chunk load attempts in one update, at least one.
    /// Failed attempts count against this budget.
    /// </param>
    /// <param name="retryDelaySeconds">
    /// The nonnegative finite delay after a failed load before that chunk may be
    /// attempted again.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A distance, work limit, or retry delay is outside its supported range.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="unloadDistance"/> is not greater than
    /// <paramref name="loadDistance"/>.
    /// </exception>
    public SceneStreamingConfiguration(
        float loadDistance,
        float unloadDistance,
        int maxLoadsPerUpdate = DefaultMaxLoadsPerUpdate,
        double retryDelaySeconds = DefaultRetryDelaySeconds)
    {
        if (!float.IsFinite(loadDistance) || loadDistance < 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(loadDistance),
                loadDistance,
                "The streaming load distance must be finite and nonnegative.");
        }

        if (!float.IsFinite(unloadDistance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(unloadDistance),
                unloadDistance,
                "The streaming unload distance must be finite.");
        }

        if (unloadDistance <= loadDistance)
        {
            throw new ArgumentException(
                "The streaming unload distance must be greater than the load distance.",
                nameof(unloadDistance));
        }

        if (maxLoadsPerUpdate < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxLoadsPerUpdate),
                maxLoadsPerUpdate,
                "At least one chunk load attempt must be allowed per update.");
        }

        if (!double.IsFinite(retryDelaySeconds) || retryDelaySeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryDelaySeconds),
                retryDelaySeconds,
                "The retry delay must be finite and nonnegative.");
        }

        LoadDistance = loadDistance;
        UnloadDistance = unloadDistance;
        MaxLoadsPerUpdate = maxLoadsPerUpdate;
        RetryDelaySeconds = retryDelaySeconds;
    }

    /// <summary>
    /// Gets the inclusive distance at which a chunk becomes demanded.
    /// </summary>
    public float LoadDistance { get; }

    /// <summary>
    /// Gets the distance beyond which a loaded chunk is unloaded.
    /// </summary>
    public float UnloadDistance { get; }

    /// <summary>
    /// Gets the maximum number of chunk load attempts in one update.
    /// </summary>
    public int MaxLoadsPerUpdate { get; }

    /// <summary>
    /// Gets the delay before a failed chunk may be retried.
    /// </summary>
    public double RetryDelaySeconds { get; }
}
