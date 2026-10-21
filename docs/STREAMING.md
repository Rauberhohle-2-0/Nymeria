# Scene chunk streaming

`Nymeria.Core.Streaming` is a small, synchronous layer for loading and unloading
application-defined scene chunks around a focus point. It answers one question:
which bounded groups of scene objects should be in the scene near the camera?
It deliberately does not become an asset manager, world editor, terrain system,
or background streaming service.

This document is the authoritative contract for chunk selection, lifecycle,
ownership, failure handling, diagnostics, and limits. Related contracts are
[SCENE.md](SCENE.md), [CULLING.md](CULLING.md), [LOD.md](LOD.md),
[PERFORMANCE.md](PERFORMANCE.md), and [ARCHITECTURE.md](ARCHITECTURE.md).

## Public API

The types live in `src/core/Nymeria.Core/Streaming/` under the
`Nymeria.Core.Streaming` namespace.

```csharp
var scene = new Scene();
var configuration = new SceneStreamingConfiguration(
    loadDistance: 55.0f,
    unloadDistance: 85.0f,
    maxLoadsPerUpdate: 4,
    retryDelaySeconds: 5.0);

var chunks = new[]
{
    new SceneChunkDefinition(
        "market",
        new BoundingBox(
            new Vector3(-40.0f, 0.0f, -40.0f),
            new Vector3(40.0f, 20.0f, 40.0f)),
        loadContent: () => BuildMarketObjects(),
        releaseContent: content => ReleaseMarketOnlyAssets(content)),
};

using var streamer = new SceneStreamer(scene, chunks, configuration);

// Once per frame, before rendering. Update mutates the scene on this thread.
SceneStreamingUpdateResult update = streamer.Update(camera.Position, deltaSeconds);

if (update.UnloadedChunks != 0)
{
    // The streamer has removed the objects; the renderer still owns GPU caches.
    renderer.PruneUnusedResources(scene);
}

if (update.FirstError is not null)
{
    Console.WriteLine(update.FirstError);
}
```

`SceneChunkDefinition` contains:

- `Id`: a unique ordinal identifier of at most 128 characters. Empty,
  whitespace-padded, and control-character identifiers are rejected.
- `Bounds`: conservative world-space bounds for streaming selection. They do not
  replace per-object culling or LOD bounds.
- `LoadContent`: an application callback that returns the chunk's scene objects.
  It runs synchronously on the update thread. It must not return null or null
  entries, must not return an object already in the scene or another loaded
  chunk, and must not mutate the returned list after returning it.
- `ReleaseContent`: an optional callback that runs after the chunk's objects have
  been removed. Use it for chunk-exclusive application resources. It must not
  dispose a mesh, material, or texture still referenced by another object.

`SceneStreamer` accepts at most 10,000 known chunk definitions, which bounds chunk
metadata and the per-update demand scan; the scene's object limit independently
bounds loaded content. It owns only the loaded/unloaded membership of objects in
one `Scene`; do not mutate those objects' membership outside it.
`SceneStreamingStatistics` reports current state totals and cumulative work.
`SceneStreamingUpdateResult` reports the transitions and bounded first error from
one update.

## Distance and selection semantics

A chunk's distance is the shortest distance from the streaming focus to its
world-space bounds, reusing `LodMath.DistanceToBounds`. A focus inside the bounds
is at distance zero.

- **Load:** a chunk is demanded when `distance <= LoadDistance`. The boundary is
  inclusive.
- **Unload:** a loaded chunk is unloaded only when
  `distance > UnloadDistance`. The boundary is inclusive for keeping the chunk.
- **Hysteresis:** `UnloadDistance` must be greater than `LoadDistance`. The gap
  prevents a focus oscillating at the load boundary from repeatedly loading and
  unloading the same chunk.
- **Large chunks:** because distance is measured to bounds, a large chunk stays
  resident while the focus is inside it; a point-to-origin distance would evict
  it prematurely.

Chunks may overlap. Each definition has independent state and loads when
demanded. An empty content list is valid: the chunk becomes loaded with zero
objects. Invalid focus positions, elapsed times, bounds, distances, and
configurations are rejected rather than repaired.

## Update order and lifecycle

Every `Update(focus, elapsedSeconds)` follows this order:

1. Validate the focus and elapsed time.
2. Unload every loaded chunk strictly beyond the unload distance.
3. Collect demanded chunks that are unloaded, or failed and past their retry
   delay.
4. Sort candidates by distance, then identifier; load at most
   `MaxLoadsPerUpdate` of them.
5. Return the transition counts and the first bounded error.

The observable states are:

| State | Meaning |
| --- | --- |
| `Unloaded` | No content is in the scene. |
| `Loaded` | The content is active in the scene. |
| `Failed` | The last load attempt failed; no content is in the scene. |

Loading and unloading are synchronous. There is no observable `Loading` or
`Unloading` state in this version. A load either becomes `Loaded` or `Failed` in
the update that attempts it; an unload becomes `Unloaded` in the update that
removes it.

A fast camera move or teleport unloads all chunks beyond the larger unload
distance, then fills the new demand set nearest first over one or more updates.
`MaxLoadsPerUpdate` bounds the number of callbacks, not the time each callback
takes. A slow content callback can still stall that update; this is the explicit
synchronous-baseline limitation.

## Failure handling and retry

Loads are isolated to one chunk. If a content callback throws, returns null,
returns a null object, repeats an object reference, returns an object already in
the scene, or exceeds the scene object limit, the streamer:

- removes any objects it added for that attempt;
- marks the chunk `Failed`;
- records a bounded diagnostic;
- invokes the release callback if content had been returned;
- does not retry until `RetryDelaySeconds` has elapsed since the failure.

Once that delay passes, the chunk is eligible the next time it is demanded. The
retry clock is advanced by the `elapsedSeconds` argument, including while the
chunk is outside the load distance, so a paused or streaming-disabled loop does
not retry. `RetryDelaySeconds = 0` permits a retry on the next update. A failed
chunk does not stop other chunks from loading.
Release-callback exceptions are counted and reported but do not retain the
scene objects or turn a completed unload back into a loaded chunk.

## Resource ownership

Active chunks are not the same thing as reclaimed memory.

| Resource | Owner | What unload does |
| --- | --- | --- |
| Streamed `SceneObject` membership | `SceneStreamer` | Removes the objects and drops its content references. |
| CPU `MeshData`, `Material`, `Texture` | Application | Nothing is disposed. Shared references remain valid; unreferenced managed data is reclaimed by the GC. |
| Chunk-exclusive disposable data | Application, via `ReleaseContent` | Callback runs after object removal. |
| GPU buffers, textures, samplers | Renderer | Nothing changes automatically. `MaterialRenderer.PruneUnusedResources` releases caches no longer referenced by the scene, after `SDL_WaitForGPUIdle`. |

`PruneUnusedResources` considers every current object's primary mesh, every LOD
variant, and every material base-color texture. Shared entries stay cached while
any current object references them. A later object that references a released
entry uploads it again. The renderer never disposes managed CPU data.

Unloaded objects also disappear from scene raycast queries, because
`SceneRaycaster` scans the current scene and never the streamer's retained
metadata. A `RaycastHit` obtained before an unload still holds the managed
`SceneObject` reference; it is safe to inspect but is no longer scene content.
Validate a stored selection with `Scene.Contains` after streaming updates, as
`HelloPicking` does. [PICKING.md](PICKING.md) documents the picking contract.

Do not call pruning while another scene depends on the renderer's cache. Pruning
is an occasional unload-path operation, not a per-frame operation.

## Threading

`SceneStreamer` is not thread-safe. It mutates a `Scene` and must run on the same
thread and outside any renderer or culler traversal. Do not add, remove, or clear
streamer-owned objects outside the streamer; it tracks their membership from the
load and unload transitions. This version has no worker threads, asynchronous
loads, background uploads, or lock-free queues. Content
callbacks are expected to follow the same rule; GPU upload and destruction remain
on the renderer's existing main-thread path.

## Diagnostics

`SceneStreamingStatistics` exposes:

- known, loaded, failed, and unloaded chunk counts;
- active streamed scene-object count;
- cumulative load attempts, successful loads, unloads, load failures, and
  release failures.

The chunk counts satisfy
`KnownChunks == LoadedChunks + FailedChunks + UnloadedChunks`. A load failure
counts as an attempt and a failure but not as a successful load. An unload counts
once per chunk transition, not once per object. Release failures are separate
from load failures.

`GetState(id)`, `TryGetState(id, out state)`, and `GetLastError(id)` expose
per-chunk state and the most recent bounded diagnostic without allocating a
per-frame collection.

## `HelloStreaming`

`examples/HelloStreaming` builds a deterministic five-by-five world of 40-unit
chunks. Every chunk shares one cube mesh and six materials. Each chunk also builds
a unique tapered marker mesh on load, so unloading and pruning can release
chunk-exclusive GPU buffers while the shared cube remains cached.

```bash
dotnet run --project examples/HelloStreaming/HelloStreaming.csproj
dotnet run --project examples/HelloStreaming/HelloStreaming.csproj -- --no-streaming
dotnet run --project examples/HelloStreaming/HelloStreaming.csproj -- \
  --load-distance 45 --unload-distance 75
```

Controls:

- `S` toggles automatic streaming. Disabling it freezes the current active set;
  no loads, unloads, retries, or pruning occur until it is enabled again.
- `T` cycles teleport positions at the start, the opposite corner, and the other
  opposite corner. This exercises fast movement, unload/reload, and resource
  pruning.
- `C` toggles frustum culling.
- `R` returns to the starting camera.
- Camera movement and look use the shared `FlyCameraControls`.

`--no-streaming` loads the initial demand set, then freezes it. This is a
baseline for comparing draw counts and render time while moving, not a separate
renderer or a claim that streaming always improves FPS. The example prints:

- known/loaded/unloaded/failed chunks and active streamed objects;
- cumulative load attempts, successes, unloads, load failures, and release
  failures;
- loads and unloads in the last update;
- streaming update average, p95, and maximum time;
- pruned GPU entries, current and cumulative;
- FPS and renderer average/p95 time;
- candidates, frustum-culled instances, and actual draw calls.

On this workstation (Apple M4, macOS, Release build, 960 × 540 logical window),
the demo loaded 9 of 25 chunks and 99 objects at its starting position, submitted
47 draw calls after frustum culling, and reported roughly 0.01–0.05 ms per
streaming update with no load or unload in steady state. Render time was roughly
0.29–0.42 ms across observed runs. These are single-session observations, not a
benchmark or an FPS claim.
A separate Metal runtime harness exercised four teleport round trips, pruned one
unique marker mesh per unload (eight total), rendered after each prune, resized
the window, minimized it, restored it, and completed 54 counter/lifecycle checks
without a use-after-dispose failure. Pruning that tiny two-object scene waited
for GPU idle and took roughly 0.5–5.5 ms per unload, with occasional higher
first-call warm-up. That cost is why pruning is an unload-path operation rather
than a per-frame one; a larger scene can produce a visible teleport spike.

## Tests

`tests/Nymeria.Core.Tests/SceneStreamerTests.cs` covers the layer without SDL3
or a GPU:

- inclusive load and strict unload boundaries, point-to-bounds distance, and
  hysteresis;
- nearest-first ordering, identifier tie-breaking, load budgets, and teleport
  unload/reload behavior;
- per-object state, empty chunks, overlapping bounds, and counter invariants;
- null, malformed, duplicate, and oversized content; partial-add rollback;
  failed-load isolation and retry delay; and bounded single-line diagnostics;
- repeated load/unload cycles, shared mesh references, release callbacks,
  release failures, external objects, and disposal.

## Limitations

- Synchronous content callbacks; a slow callback stalls the update.
- No background streaming, asynchronous IO, or upload queue.
- No automatic asset dependency discovery, file-format streaming, or path
  resolution. The exit criteria are application-defined; a loader that reads
  files must apply the repository's path, size, and format validation rules.
- No terrain, LOD generation, occlusion culling, spatial index, networking, or
  world editor.
- Unload removes scene objects but does not automatically free CPU assets.
- GPU caches are released only by an explicit renderer prune or disposal.
  Pruning waits for GPU idle and is intentionally not per-frame.
- The load budget bounds callback count, not callback duration or bytes read.
- Unloading removes each owned object through `Scene.Remove`, which preserves
  scene order. A very large chunk can therefore make removal work grow with the
  current scene size; keep chunks reasonably bounded and measure large worlds.
- There is no automatic GPU-timing or memory reporting.
- GPU output and interactivity are checked manually with
  [SMOKE_TEST.md](SMOKE_TEST.md), not in headless CI.
