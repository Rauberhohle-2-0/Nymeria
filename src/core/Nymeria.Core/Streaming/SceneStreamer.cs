using System.Collections.ObjectModel;
using System.Numerics;
using Nymeria.Core.Scenes;

namespace Nymeria.Core.Streaming;

/// <summary>
/// Synchronously loads and unloads application-defined scene chunks around a
/// streaming focus.
/// </summary>
/// <remarks>
/// <para>
/// This is the correct, single-threaded baseline: no worker threads, file
/// streaming, or GPU work run here. <see cref="Update"/> removes loaded chunks
/// beyond the configured unload distance, then loads demanded chunks nearest
/// first, up to <see cref="SceneStreamingConfiguration.MaxLoadsPerUpdate"/>.
/// Every load is completed before the update returns, so a large teleport can
/// still cost one or more synchronous load callbacks. The bounded load count
/// limits that spike to a configured number of chunks per update.
/// </para>
/// <para>
/// A chunk is demanded when its bounds are within the inclusive load distance
/// of the focus. Once loaded, it remains resident until its bounds are strictly
/// beyond the unload distance. The gap is the hysteresis band, so a focus that
/// oscillates at the load boundary cannot repeatedly evict and reload a chunk.
/// Distances are point-to-world-bounds distances, not point-to-origin
/// distances, so a large chunk remains resident while the focus is inside it.
/// </para>
/// <para>
/// Scene mutation is not thread-safe and must not overlap rendering or culling.
/// This type adds and removes streamed objects only. It does not modify meshes,
/// materials, transforms, or the renderer's GPU caches. CPU asset ownership
/// remains with the application and GPU ownership remains with the renderer;
/// the optional release callback is the application's chance to release
/// chunk-exclusive resources after its objects leave the scene. Call
/// <c>MaterialRenderer.PruneUnusedResources</c> in the example if unused GPU
/// uploads should be reclaimed.
/// </para>
/// <para>
/// Failed loads are isolated to the failing chunk. Partially added objects are
/// removed, the chunk becomes <see cref="SceneChunkState.Failed"/>, and it is not
/// retried until the configured delay has elapsed from the failure. Once that
/// delay has passed, it is eligible the next time it is demanded. Error text is
/// bounded. The original exception is not rethrown, so a missing or malformed
/// asset cannot take down the frame loop.
/// </para>
/// <para>
/// The streamer does not open files, resolve paths, load assemblies, or inspect
/// content types. A <see cref="SceneChunkDefinition"/> loader is application
/// code and owns those security decisions. See <c>docs/STREAMING.md</c>.
/// </para>
/// </remarks>
public sealed class SceneStreamer : IDisposable
{
    /// <summary>
    /// The largest number of chunk definitions one streamer accepts.
    /// </summary>
    /// <remarks>
    /// This bounds known-chunk metadata and the per-update demand scan. The
    /// scene's own object limit independently bounds loaded content.
    /// </remarks>
    public const int MaxChunkCount = 10_000;

    private const int MaxDiagnosticLength = 512;

    private static readonly Comparison<LoadCandidate> s_loadOrder =
        static (left, right) =>
        {
            int distanceOrder = left.Distance.CompareTo(right.Distance);

            return distanceOrder != 0
                ? distanceOrder
                : string.CompareOrdinal(
                    left.Chunk.Definition.Id,
                    right.Chunk.Definition.Id);
        };

    private readonly Scene _scene;
    private readonly SceneStreamingConfiguration _configuration;
    private readonly List<ChunkRuntime> _chunks;
    private readonly Dictionary<string, ChunkRuntime> _chunksById;
    private readonly ReadOnlyCollection<SceneChunkDefinition> _definitions;
    private readonly List<LoadCandidate> _loadCandidates = new();

    private double _elapsedSeconds;
    private long _loadAttempts;
    private long _successfulLoads;
    private long _unloads;
    private long _loadFailures;
    private long _releaseFailures;
    private bool _disposed;

    /// <summary>
    /// Creates a streamer over an existing scene.
    /// </summary>
    /// <param name="scene">
    /// The scene that receives streamed objects. Objects added outside this
    /// streamer are left untouched.
    /// </param>
    /// <param name="chunks">
    /// The chunk definitions. Identifiers must be unique using ordinal
    /// comparison. Equal distances are ordered by identifier, not by the order
    /// in which definitions are supplied.
    /// </param>
    /// <param name="configuration">The validated streaming distances and limits.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="scene"/>, <paramref name="chunks"/>, or
    /// <paramref name="configuration"/> is null, or the sequence contains a null
    /// definition.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Two definitions use the same identifier, or the sequence exceeds
    /// <see cref="MaxChunkCount"/> entries.
    /// </exception>
    public SceneStreamer(
        Scene scene,
        IEnumerable<SceneChunkDefinition> chunks,
        SceneStreamingConfiguration configuration)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        ArgumentNullException.ThrowIfNull(chunks);
        _configuration = configuration ??
            throw new ArgumentNullException(nameof(configuration));

        _chunks = new List<ChunkRuntime>();
        _chunksById = new Dictionary<string, ChunkRuntime>(StringComparer.Ordinal);

        foreach (SceneChunkDefinition definition in chunks)
        {
            if (definition is null)
            {
                throw new ArgumentNullException(
                    nameof(chunks),
                    "The chunk sequence contains a null definition.");
            }

            if (_chunks.Count >= MaxChunkCount)
            {
                throw new ArgumentException(
                    $"A scene streamer cannot contain more than {MaxChunkCount} chunks.",
                    nameof(chunks));
            }

            if (_chunksById.ContainsKey(definition.Id))
            {
                throw new ArgumentException(
                    $"The chunk identifier '{definition.Id}' is duplicated.",
                    nameof(chunks));
            }

            var runtime = new ChunkRuntime(definition);
            _chunks.Add(runtime);
            _chunksById.Add(definition.Id, runtime);
        }

        _definitions = Array.AsReadOnly(
            _chunks.Select(chunk => chunk.Definition).ToArray());
    }

    /// <summary>
    /// Gets the immutable chunk definitions in the order supplied to the
    /// constructor.
    /// </summary>
    public IReadOnlyList<SceneChunkDefinition> Definitions => _definitions;

    /// <summary>
    /// Gets the streaming configuration.
    /// </summary>
    public SceneStreamingConfiguration Configuration => _configuration;

    /// <summary>
    /// Gets cumulative counters and the current chunk and scene-object totals.
    /// </summary>
    /// <remarks>
    /// The property walks the known chunks to compute state totals. It is
    /// intended for periodic diagnostics, not a per-frame hot path.
    /// </remarks>
    public SceneStreamingStatistics Statistics
    {
        get
        {
            int loaded = 0;
            int failed = 0;
            int activeObjects = 0;

            foreach (ChunkRuntime chunk in _chunks)
            {
                switch (chunk.State)
                {
                    case SceneChunkState.Loaded:
                        loaded++;
                        activeObjects += chunk.Objects?.Length ?? 0;
                        break;
                    case SceneChunkState.Failed:
                        failed++;
                        break;
                }
            }

            return new SceneStreamingStatistics(
                _chunks.Count,
                loaded,
                failed,
                _chunks.Count - loaded - failed,
                activeObjects,
                _loadAttempts,
                _successfulLoads,
                _unloads,
                _loadFailures,
                _releaseFailures);
        }
    }

    /// <summary>
    /// Applies one synchronous streaming update for the supplied focus.
    /// </summary>
    /// <param name="focus">
    /// The streaming focus in world space, normally the active camera position.
    /// It must contain only finite components.
    /// </param>
    /// <param name="elapsedSeconds">
    /// The nonnegative elapsed time since the previous update. It advances the
    /// failed-load retry clock and does not change distances or load order.
    /// </param>
    /// <returns>The chunk transitions and failures performed by this update.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="focus"/> is non-finite, <paramref name="elapsedSeconds"/>
    /// is negative or non-finite, or elapsed time cannot be represented.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The streamer is disposed.</exception>
    public SceneStreamingUpdateResult Update(Vector3 focus, double elapsedSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!float.IsFinite(focus.X) ||
            !float.IsFinite(focus.Y) ||
            !float.IsFinite(focus.Z))
        {
            throw new ArgumentOutOfRangeException(
                nameof(focus),
                focus,
                "The streaming focus must contain finite components.");
        }

        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsedSeconds),
                elapsedSeconds,
                "Elapsed streaming time must be finite and nonnegative.");
        }

        double nextTime = _elapsedSeconds + elapsedSeconds;

        if (!double.IsFinite(nextTime))
        {
            throw new ArgumentOutOfRangeException(
                nameof(elapsedSeconds),
                elapsedSeconds,
                "Elapsed streaming time exceeded the supported range.");
        }

        _elapsedSeconds = nextTime;

        int unloadedChunks = 0;
        int releaseFailures = 0;
        string? firstError = null;

        // Evict first. Distances are measured to the whole chunk bounds, so a
        // large chunk remains resident while the focus is inside it.
        foreach (ChunkRuntime chunk in _chunks)
        {
            if (chunk.State != SceneChunkState.Loaded)
            {
                continue;
            }

            float distance = chunk.Definition.DistanceTo(focus);

            if (distance <= _configuration.UnloadDistance)
            {
                continue;
            }

            if (TryUnload(chunk, ref releaseFailures, ref firstError))
            {
                unloadedChunks++;
            }
        }

        _loadCandidates.Clear();

        foreach (ChunkRuntime chunk in _chunks)
        {
            if (chunk.State == SceneChunkState.Loaded)
            {
                continue;
            }

            float distance = chunk.Definition.DistanceTo(focus);

            if (distance > _configuration.LoadDistance)
            {
                continue;
            }

            if (chunk.State == SceneChunkState.Failed &&
                _elapsedSeconds < chunk.RetryAfterSeconds)
            {
                continue;
            }

            _loadCandidates.Add(new LoadCandidate(chunk, distance));
        }

        // Bound the number of synchronous callbacks and favor the chunks
        // nearest the focus. Identifier order makes equal distances stable.
        _loadCandidates.Sort(s_loadOrder);
        int attemptCount = Math.Min(
            _loadCandidates.Count,
            _configuration.MaxLoadsPerUpdate);
        int loadedChunks = 0;
        int failedLoads = 0;

        for (int index = 0; index < attemptCount; index++)
        {
            ChunkRuntime chunk = _loadCandidates[index].Chunk;

            if (TryLoad(chunk, ref releaseFailures, ref firstError))
            {
                loadedChunks++;
            }
            else
            {
                failedLoads++;
            }
        }

        return new SceneStreamingUpdateResult(
            loadedChunks,
            unloadedChunks,
            failedLoads,
            releaseFailures,
            firstError);
    }

    /// <summary>
    /// Gets the current state of one chunk.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="chunkId"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">No chunk has that identifier.</exception>
    public SceneChunkState GetState(string chunkId)
    {
        return GetChunk(chunkId).State;
    }

    /// <summary>
    /// Tries to get the current state of one chunk.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="chunkId"/> is null.</exception>
    public bool TryGetState(string chunkId, out SceneChunkState state)
    {
        ArgumentNullException.ThrowIfNull(chunkId);

        if (_chunksById.TryGetValue(chunkId, out ChunkRuntime? chunk))
        {
            state = chunk.State;
            return true;
        }

        state = default;
        return false;
    }

    /// <summary>
    /// Gets the most recent load or release error for one chunk, or null when
    /// there is none.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="chunkId"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">No chunk has that identifier.</exception>
    public string? GetLastError(string chunkId)
    {
        return GetChunk(chunkId).LastError;
    }

    /// <summary>
    /// Removes every loaded chunk from the scene, invoking each release callback
    /// once. Failed and unloaded chunks are unchanged.
    /// </summary>
    /// <returns>The number of chunks unloaded.</returns>
    /// <exception cref="ObjectDisposedException">The streamer is disposed.</exception>
    public int UnloadAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int unloadedChunks = 0;
        int releaseFailures = 0;
        string? firstError = null;

        foreach (ChunkRuntime chunk in _chunks)
        {
            if (chunk.State != SceneChunkState.Loaded)
            {
                continue;
            }

            if (TryUnload(chunk, ref releaseFailures, ref firstError))
            {
                unloadedChunks++;
            }
        }

        return unloadedChunks;
    }

    /// <summary>
    /// Unloads every loaded chunk and releases this streamer's scene references.
    /// </summary>
    /// <remarks>
    /// Disposal does not dispose meshes, materials, or textures. The scene keeps
    /// any objects that were added outside the streamer, and the renderer keeps
    /// its caches until it is disposed or asked to prune them.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UnloadAll();
        _disposed = true;
        _loadCandidates.Clear();
    }

    private bool TryLoad(
        ChunkRuntime chunk,
        ref int releaseFailures,
        ref string? firstError)
    {
        _loadAttempts++;
        IReadOnlyList<SceneObject>? returnedContent = null;
        bool releaseReturnedContent = false;

        try
        {
            returnedContent = chunk.Definition.LoadContent();

            if (returnedContent is null)
            {
                throw new InvalidOperationException(
                    "The chunk content callback returned null.");
            }

            releaseReturnedContent = true;

            if (returnedContent.Count > Scene.MaxObjectCount)
            {
                throw new InvalidOperationException(
                    $"The chunk returned {returnedContent.Count} objects, which " +
                    $"exceeds the scene limit of {Scene.MaxObjectCount}.");
            }

            var objects = new SceneObject[returnedContent.Count];
            var uniqueObjects = new HashSet<SceneObject>(
                ReferenceEqualityComparer.Instance);

            for (int index = 0; index < returnedContent.Count; index++)
            {
                SceneObject sceneObject = returnedContent[index] ??
                    throw new InvalidOperationException(
                        $"The chunk content contains null at index {index}.");

                if (!uniqueObjects.Add(sceneObject))
                {
                    throw new InvalidOperationException(
                        $"The chunk content contains the same scene object more " +
                        $"than once at index {index}.");
                }

                if (_scene.Contains(sceneObject))
                {
                    throw new InvalidOperationException(
                        $"Scene object at index {index} already belongs to the " +
                        "scene or another loaded chunk.");
                }

                objects[index] = sceneObject;
            }

            int added = 0;

            try
            {
                for (; added < objects.Length; added++)
                {
                    _scene.Add(objects[added]);
                }
            }
            catch
            {
                // A custom scene limit can fail part-way through. Do not leave a
                // partial chunk behind.
                for (int index = added - 1; index >= 0; index--)
                {
                    _scene.Remove(objects[index]);
                }

                throw;
            }

            chunk.Objects = objects;
            chunk.ReturnedContent = returnedContent;
            chunk.State = SceneChunkState.Loaded;
            chunk.RetryAfterSeconds = 0.0;
            chunk.LastError = null;
            _successfulLoads++;
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            chunk.Objects = null;
            chunk.ReturnedContent = null;
            chunk.State = SceneChunkState.Failed;
            chunk.RetryAfterSeconds = AddRetryDelay(_elapsedSeconds);
            chunk.LastError = FormatError(
                $"Chunk '{chunk.Definition.Id}' failed to load",
                exception);
            _loadFailures++;

            if (firstError is null)
            {
                firstError = chunk.LastError;
            }

            if (releaseReturnedContent && returnedContent is not null)
            {
                ReleaseAfterFailedLoad(
                    chunk,
                    returnedContent,
                    ref releaseFailures,
                    ref firstError);
            }

            return false;
        }
    }

    private bool TryUnload(
        ChunkRuntime chunk,
        ref int releaseFailures,
        ref string? firstError)
    {
        SceneObject[]? objects = chunk.Objects;
        IReadOnlyList<SceneObject>? returnedContent = chunk.ReturnedContent;

        chunk.Objects = null;
        chunk.ReturnedContent = null;
        chunk.State = SceneChunkState.Unloaded;
        chunk.RetryAfterSeconds = 0.0;

        if (objects is not null)
        {
            // Remove in reverse insertion order so a partially external mutation
            // cannot make the remaining order surprising.
            for (int index = objects.Length - 1; index >= 0; index--)
            {
                _scene.Remove(objects[index]);
            }
        }

        _unloads++;

        if (returnedContent is not null)
        {
            try
            {
                chunk.Definition.ReleaseContent(returnedContent);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                string message = FormatError(
                    $"Chunk '{chunk.Definition.Id}' release callback failed",
                    exception);
                chunk.LastError = message;
                _releaseFailures++;
                releaseFailures++;

                if (firstError is null)
                {
                    firstError = message;
                }
            }
        }

        return true;
    }

    private void ReleaseAfterFailedLoad(
        ChunkRuntime chunk,
        IReadOnlyList<SceneObject> returnedContent,
        ref int releaseFailures,
        ref string? firstError)
    {
        try
        {
            chunk.Definition.ReleaseContent(returnedContent);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            string message = FormatError(
                $"Chunk '{chunk.Definition.Id}' release after a failed load failed",
                exception);
            chunk.LastError = AppendDiagnostic(chunk.LastError, message);
            _releaseFailures++;
            releaseFailures++;

            if (firstError is null)
            {
                firstError = message;
            }
        }
    }

    private ChunkRuntime GetChunk(string chunkId)
    {
        ArgumentNullException.ThrowIfNull(chunkId);

        if (_chunksById.TryGetValue(chunkId, out ChunkRuntime? chunk))
        {
            return chunk;
        }

        throw new KeyNotFoundException(
            $"The scene streamer has no chunk with identifier '{chunkId}'.");
    }

    private double AddRetryDelay(double currentTime)
    {
        double retryTime = currentTime + _configuration.RetryDelaySeconds;

        return double.IsFinite(retryTime) ? retryTime : double.MaxValue;
    }

    private static string FormatError(string prefix, Exception exception)
    {
        string message = $"{prefix}: {exception.Message}";
        int length = Math.Min(message.Length, MaxDiagnosticLength);
        var sanitized = new char[length];
        int written = 0;

        foreach (char character in message)
        {
            if (written == length)
            {
                break;
            }

            // Exception text may originate in untrusted content. Keep the
            // diagnostic on one bounded line instead of allowing log injection
            // or an unbounded message.
            sanitized[written++] = char.IsControl(character) ? ' ' : character;
        }

        return new string(sanitized, 0, written);
    }

    private static string AppendDiagnostic(string? existing, string message)
    {
        if (string.IsNullOrEmpty(existing))
        {
            return message;
        }

        string combined = $"{existing} {message}";
        return combined.Length <= MaxDiagnosticLength
            ? combined
            : combined[..MaxDiagnosticLength];
    }

    private sealed class ChunkRuntime
    {
        public ChunkRuntime(SceneChunkDefinition definition)
        {
            Definition = definition;
        }

        public SceneChunkDefinition Definition { get; }

        public SceneChunkState State { get; set; }

        public SceneObject[]? Objects { get; set; }

        public IReadOnlyList<SceneObject>? ReturnedContent { get; set; }

        public double RetryAfterSeconds { get; set; }

        public string? LastError { get; set; }
    }

    private readonly record struct LoadCandidate(ChunkRuntime Chunk, float Distance);
}
