# Basic collision detection

`Nymeria.Core.Collision` is a small, CPU-only overlap-query layer. It answers
whether an axis-aligned box or a sphere overlaps another collision shape in the
current scene. It is not a physics engine: it has no rigid bodies, forces,
gravity, contact manifolds, impulses, collision response, continuous collision
detection, joints, triggers, or simulation loop. The `HelloCollision` example
changes material colors to make overlap visible; that is presentation, not
collision response.

This document is the authoritative contract for collision shapes, coordinate
spaces, contact semantics, world-space conversion, scene queries, lifecycle
behavior, complexity, the example, tests, and limitations. Related contracts are
[SCENE.md](SCENE.md), [PICKING.md](PICKING.md), [STREAMING.md](STREAMING.md),
[CULLING.md](CULLING.md), [LOD.md](LOD.md),
[SECURITY.md](SECURITY.md), and [ARCHITECTURE.md](ARCHITECTURE.md).

## Scope

Nymeria implements three narrow-phase overlap tests:

| Query | Primitives |
| --- | --- |
| AABB–AABB | `BoundingBox` against `BoundingBox` |
| sphere–sphere | `BoundingSphere` against `BoundingSphere` |
| AABB–sphere | either order |

The shapes are bounding volumes, not triangle meshes. A box collision shape does
not follow a mesh's surface, and no mesh triangles are read. There is no
triangle-mesh collider, capsule, convex hull, terrain collider, or pixel-perfect
test. Collision shapes are chosen independently of the render mesh and LOD
variants.

## Public API

The types live in `src/core/Nymeria.Core/Collision/` under the
`Nymeria.Core.Collision` namespace. The sphere primitive lives with the existing
bounds types in `Nymeria.Core.Models`.

### `BoundingSphere`

`BoundingSphere` is an immutable value type with a finite `Vector3 Center` and a
finite, nonnegative `Radius`. A zero-radius sphere is a valid point. NaN,
infinity, and negative radii are rejected by the constructor.

```csharp
var sphere = new BoundingSphere(
    center: new Vector3(2.0f, 1.0f, 0.0f),
    radius: 1.5f);
```

`TryTransform(Matrix4x4, out BoundingSphere)` produces a conservative
world-space sphere. The center is transformed as a point, and the radius is
multiplied by the largest row length of the matrix's 3x3 basis. For the
scale–rotation–translation matrices built by `Transform.CreateModelMatrix()`,
that is the largest absolute axis scale: rotation does not change the radius, and
non-uniform scale uses the largest stretch. A projective matrix or an arbitrary
shear is rejected rather than producing a radius that could under-report the
transformed sphere. A singular zero-scale transform is accepted and can produce
a point sphere.

Extreme finite results are saturated to the finite `float` range. Saturation can
widen the sphere and produce conservative false positives, but it never returns a
NaN or infinite shape.

### `CollisionShape`

`CollisionShape` is an immutable value type holding exactly one primitive. Build
it with `FromBox` or `FromSphere`:

```csharp
CollisionShape box = CollisionShape.FromBox(new BoundingBox(
    new Vector3(-1.0f, -1.0f, -1.0f),
    new Vector3(1.0f, 1.0f, 1.0f)));

CollisionShape sphere = CollisionShape.FromSphere(
    new BoundingSphere(Vector3.Zero, 1.0f));
```

- `Kind` is `Box`, `Sphere`, or `None`.
- `TryGetBox` and `TryGetSphere` return the stored primitive and whether it is
  the active kind.
- A default `CollisionShape` has `Kind == CollisionShapeKind.None`. It never
  overlaps anything. Assigning `default` to a collider therefore disables that
  object without allocating a replacement shape.
- `TryTransform` converts a local shape to world space. Boxes transform all eight
  corners and return a conservative world AABB that covers rotation and
  non-uniform or negative scale. Spheres use `BoundingSphere.TryTransform`.
  Non-finite or projective matrices are rejected. Singular affine matrices are
  accepted because they produce valid flat boxes or point spheres.

### `CollisionIntersections`

The narrow-phase helpers are static, allocation-free, and use double precision:

```csharp
bool boxesTouch = CollisionIntersections.Overlaps(firstBox, secondBox);
bool spheresTouch = CollisionIntersections.Overlaps(firstSphere, secondSphere);
bool mixed = CollisionIntersections.Overlaps(box, sphere);
```

`Overlaps(CollisionShape, CollisionShape)` dispatches on the kinds and returns
false when either shape is empty. The primitive inputs are already validated, so
these methods do not throw for geometry.

## Local space and world space

A `SceneObject.Collider` is defined in the object's local model space, the same
space as `MeshData.Positions` and `MeshData.Bounds`. Every query transforms the
shape with the object's current `Transform.CreateModelMatrix()`:

```text
scale * rotation * translation
```

Collision does not cache world bounds. Moving or rotating an object's transform
changes the next query result immediately. There is no separate collider
hierarchy, offset transform, or collider-relative position in this version.

| Case | Result |
| --- | --- |
| Translation | Box and sphere centers move by the translation. |
| Rotation | The box uses its transformed corners; the sphere center rotates and its radius is unchanged. |
| Uniform scale | Box extents and sphere radius scale by the factor. |
| Non-uniform scale, box | The transformed eight-corner AABB is conservative. |
| Non-uniform scale, sphere | The radius uses the largest absolute axis scale. |
| Negative scale | Absolute scale magnitude is used; box corners are reordered by the transformed AABB. |
| Zero scale | A valid flat box or point sphere is produced. |
| Non-finite matrix | The shape cannot be produced; an object is skipped by scene queries. |
| Projective matrix | Rejected for sphere conversion; boxes remain corner-transformed. |
| Shear | Box conversion remains conservative; sphere conversion is rejected. |

A sphere is the conservative volume test chosen by the application. Under
non-uniform scale it becomes a larger sphere with the largest axis radius, not an
ellipsoid. That can report overlap outside the exact ellipsoid but cannot hide
overlap inside it. Applications that need a tighter shape should use a box or
several primitives.

## Contact semantics

All comparisons are inclusive at contact:

- Boxes that share a face, edge, or corner overlap.
- Spheres whose surfaces touch overlap.
- A box and sphere that touch at a point overlap.
- Containment, identical shapes, flat boxes, and point spheres are supported.
- There is no epsilon, skin width, or separation tolerance.

Box tests compare all three axes and treat strict separation as a miss. Sphere
tests compare squared center distance with the squared sum of radii. Box–sphere
tests clamp the sphere center to the box and compare the closest-point distance
with the radius. Double precision is used so large finite values do not overflow
an intermediate float sum.

## Associating colliders with scene objects

`SceneObject.Collider` is an optional `CollisionShape?`:

```csharp
var sceneObject = new SceneObject(mesh, transform)
{
    Collider = CollisionShape.FromBox(localBounds),
};

scene.Add(sceneObject);
```

The default is `null`, so existing scenes do not participate. Setting a collider
does not change rendering, picking, frustum culling, distance culling, LOD
selection, materials, or mesh data. Setting it to `null` or `default` opts the
object out.

Collision participation and logical pickability are independent:
`SceneObject.IsPickable` affects `SceneRaycaster`, not collision. An object can be
unpickable and still collide. Conversely, collision does not require a collider
to match the render mesh or the LOD union bounds.

## Scene queries

`SceneCollision` provides static query methods. They read current scene
membership and transforms and do not mutate the scene.

### World-space shape

```csharp
if (SceneCollision.TryGetWorldShape(sceneObject, out CollisionShape worldShape))
{
    // worldShape is the conservative collision primitive for this frame.
}
```

The method returns false for a missing or empty collider, or when the transform
cannot produce a supported shape.

### Overlap against one object

```csharp
bool overlapsAnything = SceneCollision.Overlaps(scene, sceneObject);

if (SceneCollision.TryGetFirstOverlap(
        scene,
        sceneObject,
        out SceneObject? other))
{
    // other is the first overlap in scene insertion order.
}

int count = SceneCollision.AppendOverlaps(scene, sceneObject, destination);
```

- The target must currently belong to the queried scene. A removed or unloaded
  object is rejected with `ArgumentException` instead of being silently treated
  as current content.
- The target is never reported against itself.
- `TryGetFirstOverlap` returns the earliest overlapping object in scene insertion
  order. That makes a fixed scene state deterministic.
- `AppendOverlaps` appends all overlaps in insertion order and preserves existing
  destination contents. The caller owns the destination allocation.

### Overlapping pairs

```csharp
var pairs = new List<CollisionPair>();
int count = SceneCollision.AppendOverlapPairs(scene, pairs);

foreach (CollisionPair pair in pairs)
{
    // pair.First precedes pair.Second in scene insertion order.
}
```

Each unordered pair is tested once. There are no duplicate pairs and no
self-pairs. Pairs are appended in first-object insertion order, then second-object
insertion order. A `CollisionPair` contains ordinary managed references. It does
not keep an unloaded object in the scene; validate a stored pair with
`Scene.Contains` before using it after a mutation.

## Lifecycle, streaming, and rendering independence

Collision traverses `Scene.Objects` at query time, so membership follows the
existing scene lifecycle:

| State | Participates? |
| --- | --- |
| `Collider` set to a box or sphere | Yes |
| `Collider` null or `default` | No |
| Object outside the camera frustum | Yes |
| Object outside a LOD maximum render distance | Yes |
| Object currently drawn at a simplified LOD level | Yes |
| `IsPickable == false` | Yes |
| Object removed with `Scene.Remove` | No; it is absent |
| Streamed chunk unloaded by `SceneStreamer` | No; its objects are absent |
| Streamed chunk failed to load | No; content was never added or was rolled back |
| Streamed object reference retained by the application | Safe to inspect as managed data, but not current scene content |

`SceneStreamer` does not set an "inactive collision" flag and collision does not
retain chunk objects. Unloading removes the objects from the scene, which removes
them from the next query. Moving the streaming focus can change results between
frames because membership changed, not because rendering visibility changed.

Collision must not depend on LOD or frustum visibility. Off-screen active objects
can still collide, and selecting a different LOD mesh never changes the collider.
LOD is a rendering choice; the collision shape belongs to the object and its
transform.

## Complexity and determinism

`AppendOverlapPairs` is the deliberate O(n²) baseline for small active sets. It
evaluates every unordered pair of colliders, so both collision tests and transform
work grow quadratically. There is no BVH, octree, spatial hash, sweep-and-prune,
cached world bounds, parallel query, or background simulation thread. The
existing scene object limit (`Scene.MaxObjectCount`, 100,000) bounds the input,
but a large fully populated scene is not a suitable collision workload. A
broad-phase structure needs a measured use case before it is added.

For a fixed scene state, queries are deterministic: insertion order controls pair
and target ordering, and there is no unstable sort or hash iteration in the
result. The queries allocate only what the caller's destination handles; the
query methods themselves do not create per-pair collections.

Collision is single-threaded. It reads `SceneObject.Transform`, `Collider`, and
scene membership, and must not overlap scene mutation, a streaming update,
rendering, or another collision query that mutates the scene. There is no
concurrency contract beyond that.

## `HelloCollision`

`examples/HelloCollision` demonstrates the layer with the existing shared material
renderer, camera, and input mapping. It needs no asset file.

- Six bodies share a procedural cube or sphere mesh and six base materials.
- The cyan player box is moved with the arrow keys in the XZ plane.
- A large center box, a static sphere, an orbiting box, an orbiting sphere, and a
  bobbing box animate or remain fixed around it. The orbit paths cross each other
  and the static obstacles, so overlap occurs without user input.
- Every body has an explicit local collider. Boxes use a local AABB matching the
  cube; spheres use a unit `BoundingSphere`. The collider is separate from the
  render mesh and is transformed by the object's current transform each frame.
- Overlapping bodies are assigned a shared yellow material for that frame. All
  base materials are restored before the next query, so the feedback is current
  contact state, not a collision response.
- `P` pauses or resumes animation. `R` resets the player and animation. Shared
  camera controls remain `W`/`A`/`S`/`D`, `Space`/`Left Control`, `Shift`, and
  held right mouse button.
- Once per second the console prints active colliders, tested unordered pairs,
  overlapping pairs, whether the player is in contact, animation state, measured
  collision-query time, FPS, and drawn instances. The window title shows the
  current pair count, player contact, and animation state.
- There is no response, resolution, impulse, gravity, trigger event, or physics
  loop. Overlapping objects continue their existing motion.

```bash
dotnet run --project examples/HelloCollision/HelloCollision.csproj
```

## Tests

`tests/Nymeria.Core.Tests` covers the layer without SDL3, a window, or a GPU:

- `BoundingSphere` validation, zero radius, translation, rotation, uniform and
  non-uniform scale, mirroring, singular scale, extreme values, non-finite
  matrices, projective matrices, and shear rejection.
- AABB–AABB separated, overlapping, touching face/edge/corner, contained,
  identical, and flat or point boxes.
- Sphere–sphere and AABB–sphere separated, touching, contained, zero-radius,
  symmetric dispatch, and large finite coordinates.
- `CollisionShape` factories, empty defaults, box rotation and scaling, sphere
  conservative radius, singular affine transforms, non-finite/projective
  rejection, and extreme saturation.
- Scene pair count and ordering, no duplicate or self pairs, opt-in and removal,
  current-transform updates, rotated and non-uniform world bounds, sphere
  non-uniform scale, LOD/pickability independence, streaming unload, invalid
  targets, and null arguments.
- Existing Task 15 ray and AABB tests continue to run against the shared bounds
  types. Collision does not replace or change `RayIntersections` or
  `SceneRaycaster`; ray–sphere intersection remains outside the picking contract.

## Limitations

- Bounding-volume overlap only. There is no triangle-mesh collision, triangle
  index, penetration depth, contact point, contact normal, or separation vector.
- Only boxes and spheres. There is no capsule, convex hull, compound collider,
  height field, or terrain.
- A sphere under non-uniform scale becomes a conservative larger sphere, not an
  ellipsoid.
- There is no broad phase, spatial index, cached world bounds, sweep, continuous
  collision detection, or parallel query.
- There is no collision response, rigid body, mass, force, gravity, friction,
  restitution, joint, trigger, event system, or solver.
- There are no collision layers, masks, categories, filtering callbacks, or
  per-query exclusions beyond choosing which objects have colliders.
- There is no scene hierarchy. Collision is between flat current scene objects.
- There is no collision with the camera or character controller. The camera is
  not a collider.
- There is no ray–sphere query or collision-aware picking. Picking remains the
  Task 15 AABB raycast layer.
- There is no persistence, serialization, editor integration, or collision-shape
  debug rendering. `HelloCollision` uses material color as feedback.
- Single-threaded query only; no concurrent scene mutation is supported.

## Security notes

- All collision code is managed CPU code. It performs no file or network access,
  native interop, GPU call, allocation of native memory, or telemetry.
- `BoundingBox` and `BoundingSphere` validate finite values and ordered bounds at
  construction. `BoundingSphere` rejects negative or non-finite radii.
- `CollisionShape` can only contain a validated primitive. A default empty shape
  is a safe no-op.
- World conversion rejects non-finite and projective matrices, and rejects shear
  for sphere conversion rather than under-reporting extent. Extreme finite box and
  sphere results saturate to finite values; they can create conservative false
  positives but not NaN geometry.
- Query work is bounded by the scene object limit and the caller's active set.
  The pair query is intentionally quadratic; callers should not use a fully
  populated 100,000-object scene as a collision workload without adding a measured
  broad phase.
- Scene queries reject targets that are not currently in the scene. Returned
  object and pair references are managed and cannot dangle as native pointers, but
  they can become stale after removal or streaming unload; validate with
  `Scene.Contains`.
- Collision reads only `Collider`, `Transform`, and scene membership. It does not
  modify render state, resources, or transforms. The example's material changes
  are example presentation and restore their base materials on the next frame.
