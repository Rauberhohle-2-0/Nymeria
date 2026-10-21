# Level of Detail and Distance Culling

This document is the authoritative description of Nymeria's explicit mesh
level-of-detail (LOD) selection and optional per-object distance culling. It
covers the public data types, distance semantics, hysteresis, renderer
integration, statistics, the `HelloLOD` example, benchmark method, and limits.
Related contracts are [SCENE.md](SCENE.md), [CULLING.md](CULLING.md),
[PERFORMANCE.md](PERFORMANCE.md), [ARCHITECTURE.md](ARCHITECTURE.md), and
[examples/README.md](../examples/README.md).

## Scope

LOD lets an application supply several explicit meshes for one object and choose
exactly one of them per frame based on the camera distance. The engine selects a
variant; it never creates, simplifies, morphs, cross-fades, streams, or
automatically unloads one. There is no new renderer, pipeline, shader, assembly,
or rendering pass, and no GPU-driven or compute-based selection.

Distance culling is an optional maximum distance on the same configuration. An
instance strictly beyond it skips draw submission. Camera near/far clipping is
unchanged and independent: the projection still clips primitives, while distance
culling avoids submitting a whole instance. A maximum distance beyond the far
plane therefore has no visible effect; one inside the far plane removes the
instance before the frustum test. Objects without a configuration are never
distance-culled.

## Public API

The types live in `src/core/Nymeria.Core/Lod/` under `Nymeria.Core.Lod`.

```csharp
using Nymeria.Core.Lod;
using Nymeria.Core.Scenes;

MeshData high = LoadHighDetailMesh();
MeshData medium = LoadMediumDetailMesh();
MeshData low = LoadLowDetailMesh();

var lod = new MeshLod(
    [
        new MeshLodLevel(high, activationDistance: 0.0f),
        new MeshLodLevel(medium, activationDistance: 12.0f),
        new MeshLodLevel(low, activationDistance: 35.0f),
    ],
    maximumRenderDistance: 120.0f,
    hysteresis: 0.75f);

var sceneObject = new SceneObject(high, transform, material, lod);

// Later, to change the configuration at runtime:
sceneObject.Lod = anotherLod;
```

- `MeshLodLevel` references a shared `MeshData` and a finite, nonnegative
  activation distance.
- `MeshLod` is immutable. It validates the level list, copies it into a
  read-only view, and references the meshes without copying or owning them.
- `MeshLod.Bounds` is the model-space union of every level's
  `MeshData.Bounds`, computed once at construction.
- `SceneObject.Mesh` remains the object's primary mesh. The highest-detail LOD
  level must be the same `MeshData` reference; assigning a configuration whose
  first mesh differs throws `ArgumentException`.
- `SceneObject.Lod` defaults to null. Assigning null disables LOD. Assigning a
  configuration, including the same instance again, resets that object's current
  level.
- Several objects and configurations may reference the same meshes. The renderer
  caches one GPU upload per distinct `MeshData` reference, so sharing a variant
  does not duplicate buffers.
- `LodMath.DistanceToBounds(point, bounds)` implements the documented distance.

`MeshLod.MaxLevels` is eight. The bound keeps the per-frame usage counters
fixed-size and allocation-free; it is not an engine-wide asset limit.

## Level ordering and activation

`Levels` is ordered from highest to lowest detail. Validation requires:

- at least one level and at most `MeshLod.MaxLevels`;
- no null levels or meshes;
- the first activation distance exactly zero;
- every activation distance finite and nonnegative;
- every later activation distance strictly greater than the previous one.

The first level must activate at zero so an object can never disappear because
no level is active. A level with activation distance `T` applies while the
measured distance is at least `T` and below the next level's activation
distance. The exact boundary is inclusive: at distance `T`, the level that
activates at `T` is selected. The last level applies at every greater distance.

Distance is measured from the camera position to the closest point of the
conservative world-space union bound. A camera inside the bound is at distance
zero. This is bounding-volume aware: a large object's nearest surface is closer
than its origin, so it keeps a higher detail level than a small object whose
origin is at the same distance. Invalid or non-finite camera positions and
invalid bounds fail safe to distance zero, which selects the highest detail and
never distance-culls.

## Hysteresis and per-instance state

Each `SceneObject` stores its current level. `MeshLod` itself stores no
selection state, so sharing a configuration does not share transitions.

While a level is selected:

- moving to the next lower-detail level requires
  `distance > activation + hysteresis`;
- moving back to a higher-detail level requires
  `distance < activation - hysteresis`;
- values exactly on either edge hold the current level.

Because the advance threshold of level `n + 1` is above that level's activation
and the retreat threshold of level `n` is below its own activation, the two
conditions cannot overlap even when activation distances are closer together
than twice the hysteresis. A single call therefore cannot advance and then
retreat across the same boundary.

Selection without a previous level, including the first frame and after a
configuration assignment, uses the stateless inclusive rule and ignores
hysteresis. A teleport or very large camera movement can cross several
thresholds in one call; the loops end at the stable level for the new distance.
There is no cross-fade, morph, or temporal blending.

Disabling LOD in the renderer resets each object's stored level while the
primary mesh is drawn. Re-enabling it therefore starts from a fresh selection.
Changing the maximum render distance does not reset a level; only LOD being
disabled, the object's configuration being assigned, or a new object resets it.

## Visibility and draw order

For an object with an active configuration, `SceneVisibilityCounter` performs:

1. count the candidate and compose its model matrix;
2. transform the union bounds and measure the camera distance;
3. skip the instance when its distance is strictly beyond the maximum;
4. skip the instance when its union bounds are fully outside the frustum;
5. select the LOD level with the per-object state;
6. return the selected mesh for one indexed draw.

Distance is checked first because it is cheaper than the six-plane test and can
skip clearly invisible instances. LOD selection happens only for instances that
will be drawn. If the frustum test uses the conservative union bound, the result
does not depend on which level is currently selected, so switching to a
lower-detail mesh cannot cull geometry that the higher-detail bound kept visible.
The selected mesh's own bounds may be smaller; it is not used for visibility.

The existing depth test, material tint, texture, lighting, normal matrix, and
vertex layout are unchanged. The selected mesh supplies its own positions,
normals, texture coordinates, subsets, and index count. The renderer binds the
cached buffers for that variant and draws it once. Every variant is uploaded
once before use, not every frame and not once per instance.

Without a configuration, the original behavior is preserved exactly: the
renderer transforms `SceneObject.Mesh.Bounds`, optionally frustum-tests it, and
never applies a distance limit. The old `SceneVisibilityCounter.Test` overload
also keeps the single-mesh behavior and does not consult `SceneObject.Lod`.

## Statistics

`MaterialRenderer.LastFrameStatistics` contains CPU-side counts for the most
recent submitted frame:

| Field | Meaning |
| --- | --- |
| `TotalInstances` | Scene candidates visited in the frame |
| `VisibleInstances` | Candidates that passed every enabled test and were submitted |
| `CulledInstances` | Candidates fully outside the frustum, after the distance test |
| `DistanceCulledInstances` | Candidates skipped for exceeding their maximum distance |
| `DrawCalls` | Indexed draw calls actually submitted; one per visible instance |
| `LodDrawnInstances` | Submitted instances with an active LOD configuration |
| `LodUsage` | Fixed-size per-level snapshot of submitted LOD instances |
| `SubmittedTriangles` | Sum of `indexCount / 3` for submitted draws |

Invariants:

```text
TotalInstances = VisibleInstances + CulledInstances + DistanceCulledInstances
DrawCalls      = VisibleInstances
LodUsage.Total = LodDrawnInstances
```

An instance skipped by distance culling is not also counted as frustum-culled.
An instance without a configuration contributes to `VisibleInstances` but not to
`LodDrawnInstances` or `LodUsage`. A skipped frame, minimized window, or unusable
drawable reports zeros. These are not GPU draw-call or timing measurements, and
`SubmittedTriangles` is not a fragment or rasterization count.

## `HelloLOD`

`examples/HelloLOD` demonstrates the feature with three original procedural
sphere variants and no external asset:

- high detail: 24 segments × 12 rings (576 triangles);
- medium detail: 10 segments × 5 rings (100 triangles);
- low detail: 5 segments × 3 rings (30 triangles).

The lower levels are visibly faceted. Forty-five instances form three receding
columns plus three larger instances whose union bounds exercise the
bounding-volume-aware distance. All instances share one high mesh, one `MeshLod`
configuration, and four shared untextured materials. Each GPU variant is uploaded
once.

Controls:

| Input | Action |
| --- | --- |
| `L` | Toggle LOD. Off draws every primary (high) mesh and disables distance culling. |
| `X` | Toggle distance culling while LOD is active. |
| `F` | Toggle frustum culling. |
| `[` / `]` | Lower or raise both thresholds and the maximum distance by 10% of the base values. |
| `R` | Reset the threshold scale to 1.0. |
| Shared camera controls | `WASD`, `Space`, `Left Control`, `Shift`, right mouse look. |

`--no-lod`, `--no-distance-culling`, and `--no-culling` start with each feature
disabled for a comparison session. Once per second the example prints FPS, average
and p95 render times, candidates, frustum- and distance-culled counts, drawn
instances, draw calls, submitted triangles, and per-level usage. The first line is
a zero startup sample.

As the camera moves across a threshold, an object changes silhouette once and
holds through the hysteresis band; it must not flicker while the camera
oscillates near the boundary. Disabling LOD restores the dense high-detail
spheres, disabling distance culling reveals instances beyond the maximum, and
disabling frustum culling reveals instances behind the camera without changing
the distance-culled count.

## Benchmark and measured comparison

`HelloBenchmark` now builds three shared cube variants from the same geometry at
eight, three, and one subdivisions per face: 768, 108, and 12 triangles. Its
fixed camera and deterministic grid are unchanged, and all instances share one
`MeshLod` with thresholds 18 and 45, maximum distance 90, and hysteresis 0.5.
`--lod off|on|both` selects the renderer mode; the default remains `off` so the
previous workload is still available.

A comparison holds the instance count, camera, materials, and culling mode fixed
and changes only `MaterialRenderer.LodEnabled`:

```bash
dotnet build Nymeria.slnx --configuration Release

# Isolate LOD: keep the same draw-call count by disabling distance culling.
dotnet run --project examples/HelloBenchmark/HelloBenchmark.csproj \
  --configuration Release -- \
  --instances 2048 --culling off --lod both --no-distance-culling \
  --warmup 120 --frames 600

# Include the configured maximum distance to show distance-cull draw removal.
dotnet run --project examples/HelloBenchmark/HelloBenchmark.csproj \
  --configuration Release -- \
  --instances 2048 --culling off --lod both --warmup 120 --frames 600
```

Each mode gets its own warm-up. The report includes per-mode average and p95
total, event, render, and pacing durations; instance, draw-call, triangle, and
per-level averages; managed bytes per frame; and GC counts. Run each configuration
at least three times as separate processes and report the range, as described in
[PERFORMANCE.md](PERFORMANCE.md).

### Recorded result

The measurement below was recorded on 2026-10-21 with an Apple M4 (8-core GPU),
macOS 27.0.1, `arm64`, .NET 10.0.13, a 960 × 540 logical / 1920 × 1080 drawable
window, the Metal backend, pacing enabled at a 16 ms target, 120 warm-up frames,
and 600 measured frames per mode. Three separate processes were run for each
mode; the table reports the median run.

| Instances | Mode | Render avg | Render p95 | Draw calls | Triangles/frame | LOD usage 0/1/2 |
| --- | --- | --- | --- | --- | --- | --- |
| 2048 | LOD off | 2.17 ms | 3.29 ms | 2048 | 1,572,864 | 0/0/0 |
| 2048 | LOD on | 2.45 ms | 3.76 ms | 2048 | 96,480 | 64/245/1739 |
| 8192 | LOD off | 4.11 ms | 4.95 ms | 8192 | 6,291,456 | 0/0/0 |
| 8192 | LOD on | 4.30 ms | 5.22 ms | 8192 | 333,792 | 256/437/7499 |

With the configured maximum distance enabled, the 2048-instance LOD-on mode
submitted 1,056 draw calls instead of 2,048, culled 992 instances by distance,
and submitted 84,576 triangles. Its median render average was 1.86 ms and p95 was
2.98 ms. This second comparison changes two things at once: LOD selection and
whole-instance distance culling. The table above isolates LOD by disabling
distance culling.

One 8192-instance LOD-off run hit a single 1.0 s host stall and one skipped
frame; its values were excluded from the medians, and the run is retained in the
saved output. Managed allocation stayed at 40 bytes per measured frame in every
mode, the same as the pre-LOD benchmark, and no measured GC collections occurred.
Interpretation:

- LOD reduces the triangle count submitted per frame by selecting lighter
  geometry; it does not reduce the number of draw calls for this renderer, which
  still issues one draw per visible instance.
- The CPU render time includes bounds transformation, distance math, level
  selection, cache lookup, command recording, and submission. It may be slightly
  higher with LOD enabled; any CPU cost must be compared with the triangle work
  removed. The benchmark does not measure GPU execution, so it cannot prove a
  GPU speedup.
- Distance culling removes entire draw submissions, so it affects `DrawCalls`
  as well as triangles. It is disabled in this comparison to isolate LOD.
- No FPS improvement is claimed. With pacing enabled the loop is generally
  frame-cap limited, so a difference in FPS is not evidence about GPU cost.
- Small scene sizes may show no benefit because the per-object distance and
  selection work can cost more than the reduced geometry saves. In the recorded
  runs the LOD-on render average was about 0.13–0.28 ms higher while submitting
  roughly 94–95% fewer triangles. That is a CPU overhead, not a CPU win, and the
  GPU cost is not measured here.

## Limitations and non-goals

- No automatic polygon reduction, mesh generation, LOD discovery from OBJ, O3D,
  or `.x` files, asset streaming, asynchronous loading, or unloading.
- No morphing, cross-fading, temporal blending, dithering, or per-triangle LOD.
- No per-subset LOD, occlusion culling, spatial index, BVH, octree, or GPU-driven
  selection.
- Visibility bounds are a conservative axis-aligned union. A level switch never
  causes a false cull, but the union can keep geometry that the selected level
  alone would have culled.
- World bounds are recomputed per frame. There is no change notification or
  cached-bounds invalidation.
- All variants are uploaded once before use. The renderer does not unload a
  variant when no object references it; cached GPU resources live until renderer
  disposal, matching the existing mesh and texture caches.
- Statistics are CPU counts. There is no GPU timing, primitive throughput, or
  memory accounting.
- The renderer still issues one draw per visible instance. This feature does not
  introduce instancing, indirect draws, or a second pipeline.

## Validation and tests

Configuration is validated at construction and at the `SceneObject.Lod` setter.
Null meshes, empty or oversized lists, null levels, a non-zero first activation
distance, non-increasing or non-finite distances, non-finite or nonpositive
maximum distances, and non-finite or negative hysteresis all fail with an
exception before rendering. Unsupported or malformed configurations are
application errors, not silently repaired data. No untrusted file format is
parsed by this feature.

`tests/Nymeria.Core.Tests` covers, without a window or GPU:

- configuration storage, default hysteresis, union bounds, and every rejection
  path;
- inclusive initial boundaries, exact hysteresis edges, close thresholds,
  multi-level teleports, invalid distances, and invalid previous levels;
- camera-to-bounds distance for inside, face, edge, and corner positions,
  transformed and scaled bounds, non-finite points, and extreme finite values;
- `SceneObject` default and replacement semantics, primary-mesh matching, shared
  configurations, and deterministic state reset;
- selection at each threshold, distance culling at and beyond the maximum,
  disabled distance culling, union-bound conservative frustum behavior,
  transformed and scaled bounds, per-object state under one shared configuration,
  disabled LOD, runtime replacement, and counter invariants;
- reset behavior and the per-level usage snapshot.

GPU output, the runtime toggles, and the visible switches are verified manually
with [SMOKE_TEST.md](SMOKE_TEST.md); they are not CI tests.
