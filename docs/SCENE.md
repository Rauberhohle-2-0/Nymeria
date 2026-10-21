# Scene Data and Instancing

This document is the authoritative description of Nymeria's minimal,
format-independent scene layer. It covers transforms, scene objects, shared mesh
references, resource ownership, and the `HelloScene` example. Related contracts
are [MODEL_DATA.md](MODEL_DATA.md), [ARCHITECTURE.md](ARCHITECTURE.md), and
[MODEL_DATA.md's coordinate conventions](MODEL_DATA.md#coordinate-and-index-conventions).

## Purpose and non-goals

The scene layer answers one question: which mesh instances should be drawn, and
where? It is deliberately smaller than a scene graph. `Nymeria.Core` owns the
data because it is platform-independent, format-independent, and uses only
`System.Numerics` and the base class library. There is no `Nymeria.Scene`
project.

The scene layer contains no ECS, parent-child hierarchy, physics, collision
detection, animation, serialization, editor, culling, lighting, material system,
GPU handle, SDL3, Metal, window, or file-format logic. Those features are outside
this milestone.

## Public types

The public types live in `src/core/Nymeria.Core/Scenes/` under the
`Nymeria.Core.Scenes` namespace.

### `Transform`

A transform stores position, rotation, and scale. Rotation is a normalized
`Quaternion`; position and scale are `Vector3` values.

```csharp
var transform = new Transform(
    position: new Vector3(0.0f, 1.0f, -3.0f),
    rotation: Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll),
    scale: new Vector3(1.5f, 0.6f, 0.9f));
```

`CreateModelMatrix()` returns a row-vector matrix composed as:

```text
scale * rotation * translation
```

A point is therefore scaled in object space, then rotated, then translated. This
matches `System.Numerics` and the existing examples, which compose
`model * view * projection`. World and model spaces are right-handed with `+Y`
up and `-Z` forward. See
[MODEL_DATA.md](MODEL_DATA.md#coordinate-and-index-conventions) and
[examples/README.md](../examples/README.md#space-matrices-and-shader-layout).

The matrix bytes are uploaded unchanged by the example renderer. `System.Numerics`
stores matrices row-major while MSL stores `float4x4` column-major, so MSL reads
the uploaded bytes as the transpose and `mvp * position` matches
`position * mvp` in C#. Do not transpose the matrix again.

Validation happens at the property boundary:

- position and scale must contain only finite components;
- rotation must contain only finite components and have non-zero length;
- the rotation is normalized when assigned.

Zero or negative scale is accepted because it is finite, but zero scale is
degenerate and negative scale mirrors the object. The layer does not reject
non-uniform scale.

### `SceneObject`

A scene object is one renderable instance with a mesh reference and its own
transform:

```csharp
MeshData mesh = CreateSharedMesh();
var first = new SceneObject(mesh, transformA);
var second = new SceneObject(mesh, transformB);
```

- `Mesh` is a shared reference to validated `MeshData`. The geometry is not
  copied.
- Several objects may reference the same `MeshData` instance.
- The object does not own or dispose its mesh. `MeshData` is managed data and
  has no unmanaged resources.
- The constructor copies the supplied transform values into a new `Transform`,
  so objects do not accidentally share mutable transform state.
- Mutate `sceneObject.Transform` to change an instance; replace the mesh only by
  creating a new `SceneObject`.

### `Scene`

A scene is a deterministic collection of scene objects:

```csharp
var scene = new Scene();
scene.Add(first);
scene.Add(second);
bool removed = scene.Remove(first);
```

- `Objects` is a live, read-only view in insertion order.
- `Count` reports the current number of objects.
- Removing an object preserves the relative order of the remaining objects.
- Adding the same object reference twice is rejected with
  `InvalidOperationException`.
- `Remove` returns `false` when the object is not present.
- `Clear` removes every object.
- The default object limit is `Scene.MaxObjectCount` (100,000). The
  `Scene(int maxObjectCount)` constructor accepts a lower limit from 1 through
  that maximum for applications with a tighter budget. Adding beyond the limit
  throws `InvalidOperationException` before the list grows.
- A scene is not thread-safe.

The limit is a practical application safeguard, not a format or GPU contract. It
prevents accidental unbounded draw loops and resource requests.

## Ownership and lifetime

- `Scene` and `SceneObject` own no GPU resources, SDL3 objects, files, or
  unmanaged memory.
- `SceneObject.Mesh` references CPU `MeshData`. The caller that owns the CPU
  arrays remains responsible for not mutating them after `MeshData` construction,
  as described in [MODEL_DATA.md](MODEL_DATA.md#meshdata).
- A renderer owns its GPU device, shaders, pipeline, depth target, and one GPU
  upload per distinct `MeshData` it draws. It releases them in reverse order when
  disposed, after waiting for the GPU to become idle.
- Removing a scene object does not dispose a shared mesh or invalidate another
  object that references it.
- Dependencies in this milestone do not need explicit mesh disposal. A future
  GPU resource wrapper must keep the same rule: the owner that created a GPU
  resource releases it, and instances only hold references.

The `HelloScene` renderer caches uploads by `MeshData` reference for its own
lifetime. Removing objects therefore does not leak per-instance data; the cache
holds at most one upload per distinct mesh the renderer has drawn. It is cleared
when the renderer is disposed.

## Rendering integration

The scene layer does not render. `HelloScene` owns a `SceneRenderer` that:

1. traverses the scene before acquiring the frame command buffer and uploads any
   mesh it has not seen before;
2. acquires the swapchain texture and ensures the depth target exists;
3. begins one depth-tested render pass and binds the existing model shader and
   pipeline;
4. traverses the scene again and, for each object, pushes
   `object.Transform.CreateModelMatrix() * view * projection` to uniform slot 0,
   binds the cached vertex/index buffers for `object.Mesh`, and draws its indices;
5. ends and submits the command buffer.

An empty scene still clears the color and depth targets. Removed objects are
simply absent from the traversal. Minimized windows skip the frame, zero-sized
drawables submit an empty command buffer, and resize recreates the depth texture
using the same lifetime rules as the other examples. Mesh buffers are uploaded
once per distinct mesh, not once per instance or frame.

The scene traversal and GPU command submission both live in the example
renderer. The scene itself contains no render loop and no second shader pipeline.

## `HelloScene`

`examples/HelloScene` demonstrates the layer with a deterministic, original
procedural cube mesh. It needs no OBJ or O3D file and no OMSI asset.

- Four initial objects reference the same `MeshData` at different positions,
  rotations, and non-uniform scales. At least two, in fact all four, share one
  GPU upload.
- `E` adds another instance of the shared mesh at a deterministic position and
  scale.
- `Q` removes the most recently added or last initial object. Removing every
  object is supported and leaves an empty scene that still clears the window.
- `R` clears the scene and restores the initial four objects.
- Camera controls are the shared `FlyCameraControls`: `W`/`A`/`S`/`D`,
  `Space`, `Left Control`, `Shift`, and held right mouse button for look.

The example prints this control summary once at startup. It uses the same
16 ms frame budget and elapsed-time cap as the other interactive examples.

## Tests

`tests/Nymeria.Core.Tests` covers the scene layer without SDL3 or a GPU:

- identity, translation, rotation, non-uniform scale, and the
  scale-rotation-translation matrix order;
- quaternion normalization and rejection of non-finite or zero-length rotation;
- rejection of non-finite position and scale;
- empty scenes, insertion order, removal order, duplicate rejection, clearing,
  re-adding removed objects, and the object limit;
- shared-mesh semantics and transform copying on object construction.

GPU rendering, window behavior, and input are validated manually with
[SMOKE_TEST.md](SMOKE_TEST.md), not by headless CI.

## Intentionally unsupported

The following are out of scope for this milestone: parent-child transforms,
scene graphs, ECS, instanced draw APIs beyond per-object uniform uploads,
serialization, editors, culling, physics, collision, animation, lighting,
shadows, materials, textures, multithreaded rendering, and automatic unloading
of a cached GPU mesh when the last referencing object is removed.
