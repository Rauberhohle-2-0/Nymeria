# Bounds and Frustum Culling

This document describes Nymeria's CPU-side mesh bounds and conservative camera
frustum culling. It covers the geometry contract, the runtime integration, the
statistics, the `HelloCulling` example, and the limits of the current
implementation. Related contracts are [SCENE.md](SCENE.md),
[MODEL_DATA.md](MODEL_DATA.md), [ARCHITECTURE.md](ARCHITECTURE.md),
[PERFORMANCE.md](PERFORMANCE.md), and [examples/README.md](../examples/README.md).

## Scope

Culling answers one question per scene instance: can the object be proven to be
fully outside the camera view? If yes, the renderer may skip its draw submission.
If the answer is no, or the data needed to answer it is invalid, the instance is
submitted. Culling does not remove scene objects, change transforms, unload
meshes or materials, or alter simulation state.

The implementation is a whole-instance axis-aligned-bounds test against six
frustum planes. It is not an ECS, spatial index, BVH, octree, or occlusion query,
and it does not use GPU timing or GPU-side culling. Distance culling and explicit
mesh LOD selection are separate, optional parts of the same per-frame test; see
[LOD.md](LOD.md).

## Coordinate and matrix conventions

The culling math follows the existing rendering conventions:

- world and model spaces are right-handed with `+Y` up and `-Z` forward;
- `System.Numerics` row vectors compose as `scale * rotation * translation`, and
  the renderer combines matrices as `model * view * projection`;
- the projection maps clip-space depth to `[0, 1]`, matching SDL3 GPU and Metal;
- matrices are uploaded without an extra transpose, as described in
  [examples/README.md](../examples/README.md#space-matrices-and-shader-layout).

`Frustum.Create` consumes the combined view-projection matrix directly. It
derives the six planes from the clip-space inequalities `-w <= x <= w`,
`-w <= y <= w`, and `0 <= z <= w`. No Y flip, depth remap, or column-vector
conversion is applied.

## Mesh bounds

`MeshData.Bounds` is a `Nymeria.Core.Models.BoundingBox` in model space. It is
computed once while the mesh validates its positions, so it adds no per-frame
vertex traversal. Every position contributes to the box, including a vertex that
the index list does not reference. That can only make the box larger, which is
safe for conservative visibility.

`BoundingBox` exposes `Min` and `Max`. Its constructor rejects non-finite corners
and inverted axes. A flat box (equal minimum and maximum on an axis) is valid.

`BoundingBox.Transform(Matrix4x4)` computes the axis-aligned box of all eight
transformed corners. It handles translation, rotation, non-uniform scale, and
negative scale without assuming that scale is positive or uniform. Corner
arithmetic uses double precision. If the matrix is non-finite, or a transformed
coordinate cannot be represented as a finite `float`, the result is the maximum
finite box rather than an invalid or empty one. That fallback widens the box and
cannot hide potentially visible geometry.

World bounds are recomputed for each tested instance each frame. `Transform`
has no change notification, and adding a caching/invalidation layer for this
single consumer would be premature. The cost is eight corner transforms per
instance per frame, not a mesh vertex traversal.

## Frustum and intersection test

`Nymeria.Core.Culling.Frustum.Create(viewProjection)` returns an independent
frustum with six normalized planes. A renderer can instead own one scratch
frustum and call `Frustum.Update(viewProjection)` each frame; that refresh does
not allocate a frustum object or a plane array. `Frustum.Intersects(BoundingBox)`
uses the standard positive-vertex test:
for each plane it selects the box corner farthest along the plane normal and
checks that corner against the plane. The box is culled only when that farthest
corner is on the outside of a single plane; otherwise it is visible.

Consequences:

- a box fully outside one plane is culled;
- a box crossing any plane is visible;
- a box just touching a plane is visible;
- a box that is outside near a frustum edge but not fully outside any one plane
  is visible, because that is the conservative answer.

Plane distances are accumulated in double precision so an extreme finite box
cannot overflow an intermediate `float` sum and produce a false cull.

### Degenerate and invalid data

`Frustum` fails open. An unbounded frustum accepts every box, which disables
culling for that frame instead of risking the loss of visible geometry. This
happens when:

- the view-projection matrix contains a non-finite value;
- its determinant is zero or non-finite;
- a derived plane has a zero or non-finite normal after normalization;
- a plane's normalized coefficients cannot be represented as finite floats;
- the supplied bounding box is non-finite or inverted (defensive; `BoundingBox`
  already rejects such values at construction);
- a plane distance becomes non-finite during the test.

A non-finite transform therefore cannot cause `NaN` to reach the renderer's
culling decision. The material renderer separately rejects singular or
non-finite model transforms when it builds a lighting normal matrix.

## Renderer integration

`examples/Shared/MaterialRenderer.cs` owns the optional integration:

- `FrustumCullingEnabled` defaults to `false`, so `HelloMaterials` and
  `HelloLighting` keep their existing behavior. `HelloCulling` enables it.
- For every scene object it calls a `SceneVisibilityCounter.Test` overload. With
  frustum culling disabled, the counter is passed no frustum and treats every
  instance as visible; with it enabled, the object's world bounds are tested.
- When an object has a `MeshLod`, the test uses the conservative union of all of
  its variants' model-space bounds for both distance measurement and the frustum
  test. The union is transformed by the object's current model matrix. Distance
  culling runs before the frustum test, and LOD selection runs only for instances
  that pass both tests. See [LOD.md](LOD.md).
- A visible instance proceeds through the existing material, texture, lighting,
  and geometry path with exactly one selected mesh variant. A culled instance
  only skips draw submission. No scene object is removed, and no CPU or GPU
  resource is released.
- Resources for every referenced mesh and texture, including every LOD variant,
  are still uploaded before the frame command buffer is acquired, so culling
  never triggers an upload between render-pass commands.
- `LastFrameStatistics` reports the most recent frame that reached command
  submission. A minimized window or unusable drawable leaves the value at zero,
  and every `Render` call resets the per-frame counter first.

`SceneVisibilityCounter` lives in `Nymeria.Core.Culling` and is headless. It
computes the object's model matrix once, returns it through an `out` parameter
for reuse by the renderer, and counts total, visible, and culled instances. The
frustum is passed as a nullable reference; culling on and off therefore share the
same call path, and the renderer-owned scratch frustum adds no per-frame
allocation.

### Statistics

`MaterialRenderer.VisibilityStatistics` contains:

| Field | Meaning |
| --- | --- |
| `TotalInstances` | Scene instances tested in the frame |
| `VisibleInstances` | Instances that passed every enabled test and were submitted |
| `CulledInstances` | Instances proven fully outside the frustum, after distance culling |
| `DistanceCulledInstances` | Instances skipped for exceeding their maximum render distance |
| `DrawCalls` | Indexed draw calls actually submitted |
| `LodDrawnInstances` | Submitted instances with an active LOD configuration |
| `LodUsage` | Fixed-size per-level usage snapshot |
| `SubmittedTriangles` | Sum of submitted `indexCount / 3` |

Instance counts are not GPU draw-call counts. This renderer issues one indexed
draw call per visible instance, so `DrawCalls` currently equals
`VisibleInstances`, but that is a property of this renderer rather than of the
culling API. The statistics are not GPU timings, primitive counts, or a claim
about rasterization cost.

With frustum culling disabled, `CulledInstances` is zero and every tested
instance that is not distance-culled is visible. When distance culling is also
disabled or no object has a maximum, `TotalInstances` equals
`VisibleInstances + CulledInstances`. More generally it equals
`VisibleInstances + CulledInstances + DistanceCulledInstances`, so an instance is
never counted in two cull categories. Skipped frames report all zeros.

## `HelloCulling`

`examples/HelloCulling` demonstrates the integration with the existing scene,
camera, material, mesh, renderer, and input infrastructure. For repeatable,
auto-exiting culling comparisons at fixed camera positions and larger scene
sizes, use `examples/HelloBenchmark` as described in
[PERFORMANCE.md](PERFORMANCE.md).

- 551 deterministic instances share one procedural cube `MeshData` and four
  immutable `Material` tints. The shared material renderer uploads the mesh once.
- A grid in front of the camera contains visible instances and instances whose
  bounds cross the left, right, top, or bottom planes. Additional instances sit
  behind the initial camera, beyond its far plane, and far outside its horizontal
  view.
- Some instances use rotation, non-uniform scale, and negative scale, so the
  eight-corner world-bounds path is exercised.
- `C` toggles frustum culling at runtime. `--no-culling` starts with it disabled
  for an apples-to-apples session; `C` still toggles it.
- Once per second the example prints FPS and the four statistics fields. The
  first line is printed before the first frame and therefore contains zeros.
- Camera controls are the shared `FlyCameraControls`; resize, minimize,
  high-DPI, zero-sized-drawable, and shutdown behavior come from the existing
  window and renderer paths.
- Statistics are printed to the console. The repository has no Task 10.1
  diagnostics or Discord RPC integration, so there is nothing to integrate with
  and no RPC dependency is introduced.

Run it with:

```bash
dotnet run --project examples/HelloCulling/HelloCulling.csproj
dotnet run --project examples/HelloCulling/HelloCulling.csproj -- --no-culling
```

## Tests and verification

The culling tests are headless and require no window, GPU, or SDL3 call:

```bash
dotnet restore Nymeria.slnx
dotnet build Nymeria.slnx --configuration Release
dotnet test Nymeria.slnx --configuration Release --no-build
```

`tests/Nymeria.Core.Tests` covers:

- local mesh bounds and rejection of non-finite or inverted boxes;
- translation, rotation, non-uniform scale, negative scale, and the
  scale-rotation-translation order for transformed bounds;
- conservative overflow handling and a visible result for width-overflowed
  bounds;
- boxes inside, outside, crossing, and touching each of the six frustum planes;
- near and far boundaries;
- camera translation and rotation;
- boxes that intersect a frustum corner, and boxes enclosing the frustum;
- uniform matrix scale, identity, zero, singular, and non-finite matrices;
- deterministic per-frame counts with culling enabled and disabled, reset
  behavior, model-matrix reuse, and scene immutability.

Measured comparison on macOS (Release build):

| Property | Value |
| --- | --- |
| Machine | Apple M4, 8-core GPU, macOS 27.0.1, `arm64` |
| Window | 960 × 540 logical client area, high-DPI resizable window |
| Scene | 551 instances, one shared cube mesh, four shared untextured materials |
| Culling enabled | 413 submitted, 138 culled, 413 draw calls |
| Culling disabled | 551 submitted, 0 culled, 551 draw calls |
| FPS, culling enabled | 56.6–57.2 |
| FPS, culling disabled | 56.6–57.3 |

Method: two independent 9-second sessions of the Release executable, one with
`--no-culling`, each printing one FPS/statistics sample per second after the
initial zero sample. The example targets a 16 ms frame budget, so the GPU and
frame pacing dominate at this scene size. The measured FPS difference is within
run-to-run noise; no FPS gain is claimed. The verified effect is that 25% of the
instance draw submissions were skipped.

The full headless suite passed 631 tests in Release configuration. A temporary
native harness (outside the repository) exercised the real
`MaterialRenderer` with two instances, one visible and one behind the camera. It
verified `1 visible / 1 culled / 1 draw call` with culling enabled,
`2 visible / 0 culled / 2 draw calls` with it disabled, the same visible result
after resizing to 1400 × 400 and 400 × 900, zero statistics while minimized, and
recovered counts after restore. This covers resize and minimized-window handling
through the renderer used by `HelloCulling`; it is not a CI test. The interactive
`C` key press, high-DPI inspection, and full manual checklist are still in
[SMOKE_TEST.md](SMOKE_TEST.md).

## Limitations

- One axis-aligned box per mesh instance. Rotated objects use the box of their
  transformed corners, which is conservative but can submit extra work.
- Bounds cover all mesh positions, including unreferenced vertices.
- No per-subset, per-primitive, or occlusion culling.
- No spatial index, scene hierarchy, instanced draw API, or multi-threaded
  culling.
- LOD visibility uses a conservative union of all variant bounds rather than the
  selected variant's bounds. This can submit an instance that the selected lower
  detail mesh alone would have culled, but it cannot hide visible geometry.
- Distance culling is per object and optional; there is no global distance limit.
- No caching or invalidation of world bounds; they are recomputed per frame.
- Culling only skips draw submission. It does not skip texture or mesh uploads,
  which remain renderer-owned and shared, including all LOD variants.
- The renderer reuses one scratch frustum, so the frustum itself is not allocated
  per frame, but the culling path is not allocation-free end to end; the
  benchmark reports residual managed allocation per frame. See
  [PERFORMANCE.md](PERFORMANCE.md).
- Statistics are CPU-side counts. They do not measure GPU time, fragments, or
  actual rasterization savings.
- The fallback for invalid matrices is deliberately to keep every instance, so a
  degenerate projection disables culling for that frame.
