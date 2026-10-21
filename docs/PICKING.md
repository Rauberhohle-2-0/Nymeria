# Raycasting and object picking

`Nymeria.Core.Raycasting` is a small, CPU-only query layer that answers which
scene object lies along a world-space ray. It also includes a matrix-based
viewport unprojection helper for mouse or center-of-screen picking. The layer is
headless: it does not reference SDL3, a GPU device, a window, an example, or a
file format.

This document is the authoritative contract for the query types, coordinate
spaces, distance and tie semantics, hit eligibility, camera-ray conversion, the
`HelloPicking` example, automated tests, and limitations. Related contracts are
[SCENE.md](SCENE.md), [CULLING.md](CULLING.md), [LOD.md](LOD.md),
[STREAMING.md](STREAMING.md), [MODEL_DATA.md](MODEL_DATA.md), and
[ARCHITECTURE.md](ARCHITECTURE.md).

## Raycasting, bounding-volume picking, and triangle intersection

These are different levels of precision:

| Term | Meaning here |
| --- | --- |
| Raycasting | Testing a world-space ray against geometry and reporting the nearest hit. |
| Bounding-volume picking | Raycasting against each object's transformed conservative box. Fast and approximate. |
| Triangle intersection | Testing the ray against individual mesh triangles for an exact surface point. Not implemented. |

Nymeria implements the first two. A `RaycastHit.Point` therefore lies on the
object's transformed world-space bounds, not necessarily on its rendered
surface. The bounds are always conservative, so a bounds hit can be slightly
outside the visible geometry, and a thin gap between triangles inside the box
still counts as a hit. That approximation is intentional and sufficient for
selecting whole objects. It is not suitable for precise decals, muzzle impacts,
or per-triangle editing.

## Public API

The types live in `src/core/Nymeria.Core/Raycasting/` under the
`Nymeria.Core.Raycasting` namespace.

### `Ray`

```csharp
var ray = new Ray(
    origin: camera.Position,
    direction: camera.Forward); // normalized by the constructor

Vector3 point = ray.GetPoint(12.5f);
```

- `Origin` is a finite world-space point.
- `Direction` is normalized to unit length by the constructor, so distance
  arguments are world units.
- A supplied direction does not have to be unit length. Zero-length,
  non-finite, and non-normalizable directions throw `ArgumentException`;
  `Ray.TryCreate` performs the same validation without throwing.
- Negative direction components and origins inside geometry are valid.

### `RaycastHit`

`RaycastHit` carries:

- `SceneObject`: the object that was present in the scene when the query ran;
- `Point`: the world-space hit point on the object's transformed bounds;
- `Distance`: the world-space distance from the ray origin;
- `Normal`: the normalized outward normal of the entry face.

The public constructor validates finite point and normal values, a nonnegative
finite distance, and a non-zero normal, and normalizes the normal. When the ray
origin is inside the bounds, the hit is at distance zero, the point is the
origin, and the normal is opposite the ray direction.

`SceneObject` is an ordinary managed reference, not a handle. It does not keep a
streamed object in the scene. After a streaming unload, validate a stored hit or
selection with `scene.Contains(hit.SceneObject)` before using it.

### `RayIntersections`

```csharp
if (RayIntersections.TryIntersectBox(
        ray,
        worldBounds,
        maxDistance,
        out float distance,
        out Vector3 normal))
{
    // distance <= maxDistance and distance >= 0
}
```

`TryIntersectBox` reuses the existing `Nymeria.Core.Models.BoundingBox`; there is
no second bounding-volume type. It reports the nearest entry point and rejects a
non-finite or inverted box as a safe miss. A NaN or negative maximum distance is
also a safe miss, while positive infinity means no limit.

### `SceneRaycaster`

```csharp
if (SceneRaycaster.TryRaycast(scene, ray, maxDistance: 200.0f, out RaycastHit hit))
{
    Console.WriteLine($"{hit.SceneObject} at {hit.Distance:F2} units");
}

RaycastHit? maybeHit = SceneRaycaster.Raycast(scene, ray, 200.0f);
```

`TryRaycast` and `Raycast` return the closest valid hit. `maxDistance` is
inclusive and must not be NaN or negative; positive infinity is accepted. The
query scans the scene in insertion order and allocates nothing. There is no
`RaycastAll`: reporting every overlapping hit has no current application need,
and nearest-hit selection is the contract.

## Distance and tie semantics

- `Ray.Direction` is normalized, so all reported distances are in world units.
- The maximum distance is inclusive: a hit exactly at `maxDistance` is returned.
- A hit exactly on a box edge, corner, or face is accepted.
- A direction component of exactly zero is treated as parallel to that axis's
  pair of planes. There is no artificial epsilon; comparisons use double
  precision.
- The nearest hit is selected with a strict comparison. When two objects are at
  exactly the same distance, the object earlier in scene insertion order wins.
  This makes ties deterministic without a second sorting key.
- An origin inside a box produces a zero-distance hit, which can be the nearest
  result.

## World transforms and LOD

The query transforms each object's model-space bounds with the object's current
model matrix, composed by `Transform.CreateModelMatrix()` as
`scale * rotation * translation`. Translation, rotation, non-uniform scale, and
negative scale are handled by `BoundingBox.Transform`, which uses all eight
corners.

When `SceneObject.Lod` is configured, the query uses `MeshLod.Bounds`, the
conservative union of all variant bounds. It never uses local-space bounds
directly and never uses the currently selected LOD mesh as a pickability proxy.
A simplified mesh displayed at distance therefore cannot make an object
unpickable, and switching levels cannot change the hit point or distance.

## Eligibility and streaming

The query is deliberately independent of rendering:

| State | Picked? |
| --- | --- |
| `SceneObject.IsPickable == true` (default) | Yes |
| `SceneObject.IsPickable == false` | No |
| Outside the camera frustum | Yes; frustum culling is not consulted |
| Beyond a configured LOD maximum render distance | Yes; distance culling is not consulted |
| Currently drawn at a simplified LOD level | Yes; the LOD union bounds are used |
| Present in the scene but not rendered this frame | Yes |
| Removed by a streaming unload | No; it is no longer in the scene |
| Chunk load failed | No; its objects were never added or were rolled back |

`IsPickable` is a logical interaction flag, not a visibility flag. Setting it to
false leaves rendering, culling, LOD selection, and material state unchanged.

Streaming needs no special picking state. `SceneStreamer` removes unloaded
objects from the scene, so a query cannot see them. A hit or selection obtained
before an unload still references the managed object; it is safe to inspect but
must not be treated as current scene content. The `HelloPicking` example checks
`scene.Contains(selected)` every frame and clears its selection when the chunk
unloads.

## Building a camera ray

`ViewportRay` converts a viewport pixel to a world ray using the project's actual
matrix conventions:

```csharp
Matrix4x4 view = camera.ViewMatrix;
Matrix4x4 projection = camera.CreateProjectionMatrix(
    (float)drawableWidth / drawableHeight);

if (ViewportRay.TryCreate(
        pixel,
        drawableWidth,
        drawableHeight,
        view,
        projection,
        out Ray ray))
{
    // ray is in world space
}
```

`ViewportRay.TryCreateCenter(width, height, view, projection, out ray)` is the
same operation for the viewport center, for first-person interaction.

### Coordinate and depth conventions

- Pixel coordinates have a top-left origin, +X right, and +Y down, matching SDL
  window coordinates and the examples. `(0, 0)` is the top-left pixel and
  `(width, height)` is the bottom-right corner.
- The pixel is converted to normalized device coordinates with
  `x = 2 * pixelX / width - 1` and `y = 1 - 2 * pixelY / height`. This is the Y
  flip: no additional flip is applied to the projection.
- View and projection use Nymeria's right-handed world, +Y up, -Z forward
  convention and `System.Numerics` row vectors composed as
  `view * projection`.
- Clip-space depth is `[0, 1]`, matching SDL3 GPU and Metal and the existing
  `Camera.CreateProjectionMatrix`. The helper unprojects at depth 0 and 1.
- The ray origin is the view-space origin, which is the camera position for the
  project's perspective camera. Distance is measured from there, not from the
  near plane.
- No transpose or handedness conversion is needed because the helper consumes
  the same matrices the renderer uploads. A perspective projection is assumed.

### HiDPI and window-versus-framebuffer coordinates

The helper uses whatever pixel space the caller supplies, but the position and
dimensions must use the same units. The renderer projects with the drawable
(physical) size. SDL mouse coordinates are reported in logical window pixels, so
the application must convert them once, before the query:

```csharp
SDL.GetWindowSizeInPixels(window, out int drawableWidth, out int drawableHeight);
SDL.GetWindowSize(window, out int logicalWidth, out int logicalHeight);
SDL.GetMouseState(out float mouseX, out float mouseY);

float scaleX = (float)drawableWidth / logicalWidth;
float scaleY = (float)drawableHeight / logicalHeight;
var pixel = new Vector2(mouseX * scaleX, mouseY * scaleY);
```

This conversion belongs to the platform/application layer. `Nymeria.Core` never
queries SDL and does not assume a display scale.

### Failure behavior

`ViewportRay.TryCreate` returns false, rather than producing a NaN ray, when:

- the viewport width or height is zero, negative, or non-finite;
- the pixel position is non-finite;
- the view or projection contains a non-finite value;
- the combined matrix or the view matrix is not invertible.

Positions outside the viewport are not clamped. They are valid finite inputs and
produce rays outside the camera frustum.

## `HelloPicking`

`examples/HelloPicking` demonstrates the layer with the existing scene, material
renderer, camera, and input mapping. It needs no asset file.

- Nine deterministic 26-unit chunks are streamed around the camera. Each chunk
  creates three boxes at different positions, depths, rotations, and non-uniform
  scales. All chunks share one cube mesh and six materials.
- A separate static gray ghost box sits in front of the starting camera. It
  starts with `IsPickable = false`; `G` toggles the flag so a pick either passes
  through it or selects it.
- A left click picks with the mouse cursor. `V` switches to center-of-screen
  picking. No crosshair is drawn, because the examples have no overlay
  rendering; the window title shows the active mode.
- The selected object is highlighted by assigning a shared bright material. This
  is the existing material path, not a new rendering architecture or gizmo.
- The window title shows the selected label and distance. Each click prints one
  console line, including a miss; there is no per-frame logging.
- Objects at different depths overlap on screen, so a click demonstrates
  nearest-hit selection. Clicking empty space demonstrates a miss.
- Chunks load and unload around the camera. If the selected object's chunk is
  unloaded, the example clears the selection on the next frame instead of
  retaining a stale reference.

```bash
dotnet run --project examples/HelloPicking/HelloPicking.csproj
```

Controls: left click picks, `V` toggles cursor/center picking, `G` toggles the
ghost box's pickability, `R` resets the camera. Movement and look use the shared
`FlyCameraControls` (`W`/`A`/`S`/`D`, `Space`/`Left Control`, `Shift`, and held
right mouse button).

## Tests

`tests/Nymeria.Core.Tests` covers the layer without SDL3, a window, or a GPU:

- ray normalization, negative components, degenerate and non-finite inputs, and
  distance parameterization;
- box hit, miss, nearest entry, parallel ray, origin inside, grazing face and
  corner, flat and point boxes, inclusive maximum distance, and invalid maximum
  distances;
- empty scenes, nearest-hit ordering, deterministic insertion-order ties, and
  invalid or infinite query distances;
- translation, rotation, and non-uniform scale, including a hit that only the
  transformed world bounds can produce;
- `IsPickable` exclusion and re-enabling;
- frustum-culled objects remaining pickable;
- LOD union bounds producing the same hit before and after a level switch;
- streamed object removal, no hit after unload, and stale-reference validation;
- center and off-center viewport rays, projected world points, top-left versus
  bottom-right direction, drawable-scaled pixels, invalid viewports, non-finite
  inputs, and singular matrices.

## Limitations

- Bounds-only picking; there is no triangle-accurate hit, triangle index, or
  per-subset hit.
- Axis-aligned world bounds are conservative for rotated and non-uniformly
  scaled objects, so the reported point can be outside the mesh surface.
- No sphere or capsule bounds, BVH, octree, spatial index, or cached world
  bounds. Each query is one linear scene scan, bounded by
  `Scene.MaxObjectCount` (100,000).
- No `RaycastAll`, multi-hit ordering, layer masks, distance filtering per
  object, or interaction events.
- No physics, collision response, rigid bodies, character controllers,
  navigation meshes, skeletal hitboxes, GPU color-ID picking, editor gizmos, or
  scene-graph traversal.
- Camera unprojection assumes a perspective projection and the project's
  right-handed, row-vector, `[0, 1]` depth conventions. An orthographic camera
  would need a near-plane ray origin instead of the view-space origin.
- The query is single-threaded and must not run concurrently with scene mutation
  such as a streaming update.

## Security notes

- Every query is managed CPU code with no native interop, file access, network
  access, GPU call, or telemetry.
- `Ray`, `RaycastHit`, and the viewport helper validate finite values and reject
  degenerate or non-invertible input before producing a result.
- Invalid geometry fails safe to a miss rather than returning a NaN point or
  distance.
- Work is bounded by the scene object limit. One click cannot allocate or
  traverse unbounded mesh data because no mesh triangles are read.
- A hit reference is managed and cannot dangle as a native pointer, but it can
  become stale after a streaming unload. Applications must validate stored
  selections with `Scene.Contains`.
