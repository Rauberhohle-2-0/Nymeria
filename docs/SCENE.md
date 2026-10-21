# Scene Data and Instancing

This document is the authoritative description of Nymeria's minimal,
format-independent scene layer. It covers transforms, scene objects, shared mesh
and material references, optional scene lighting, resource ownership, and the
`HelloScene`, `HelloMaterials`, and `HelloLighting` examples. Related contracts
are [MODEL_DATA.md](MODEL_DATA.md), [MATERIALS.md](MATERIALS.md),
[LIGHTING.md](LIGHTING.md), [ARCHITECTURE.md](ARCHITECTURE.md), and
[MODEL_DATA.md's coordinate conventions](MODEL_DATA.md#coordinate-and-index-conventions).

## Purpose and non-goals

The scene layer answers one question: which mesh instances should be drawn, where,
and with which material? It is deliberately smaller than a scene graph.
`Nymeria.Core` owns the data because it is platform-independent,
format-independent, and uses only `System.Numerics` and the base class library.
There is no `Nymeria.Scene` project.

The scene layer contains no ECS, parent-child hierarchy, physics, collision
detection, animation, serialization, editor, culling, material parser, GPU
handle, SDL3, Metal, window, or file-format logic. Materials and lighting are
references to format-independent CPU data, not a rendering pipeline. The scene
layer carries one optional ambient-plus-directional lighting configuration; it
does not calculate shading. [LIGHTING.md](LIGHTING.md) is the authoritative
lighting contract.

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

A scene object is one renderable instance with a mesh reference, its own
transform, and a material reference:

```csharp
MeshData mesh = CreateSharedMesh();
Material material = CreateSharedMaterial();
var first = new SceneObject(mesh, transformA, material);
var second = new SceneObject(mesh, transformB, material);
```

- `Mesh` is a shared reference to validated `MeshData`. The geometry is not
  copied.
- Several objects may reference the same `MeshData` instance.
- `Material` is a shared reference to an immutable `Material`. It is not copied.
  It defaults to the shared `Material.Default`, an untextured white material, when
  none is supplied. Assigning `null` throws `ArgumentNullException`.
- The object does not own or dispose its mesh or material. `MeshData`,
  `Material`, and `Texture` are managed data with no unmanaged resources.
- The constructor copies the supplied transform values into a new `Transform`,
  so objects do not accidentally share mutable transform state.
- Mutate `sceneObject.Transform` to change an instance, replace
  `sceneObject.Material` to change its surface, and replace the mesh only by
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

`Scene.Lighting` is an optional `SceneLighting` reference. It defaults to
`null`, which means unlit. Assigning a lighting object opts the scene into one
ambient light and at most one directional light. The lighting object is immutable
and contains no GPU resources; replace it to change a light at runtime. Renderers
that do not support lighting ignore the property, so earlier examples keep their
behavior.

The object limit is a practical application safeguard, not a format or GPU
contract. It prevents accidental unbounded draw loops and resource requests.

## Ownership and lifetime

- `Scene`, `SceneObject`, and `SceneLighting` own no GPU resources, SDL3 objects,
  files, or unmanaged memory.
- `SceneObject.Mesh` references CPU `MeshData`. The caller that owns the CPU
  arrays remains responsible for not mutating them after `MeshData` construction,
  as described in [MODEL_DATA.md](MODEL_DATA.md#meshdata).
- `SceneObject.Material` references CPU `Material` and its optional `Texture`
  owned by the caller. Sharing one material or texture across objects is intended
  and does not copy pixels. See [MATERIALS.md](MATERIALS.md).
- A renderer owns its GPU device, shaders, pipeline, depth target, and one GPU
  upload per distinct `MeshData` it draws. A material-aware renderer also owns one
  GPU texture per distinct `Texture`. It releases them in reverse order when
  disposed, after waiting for the GPU to become idle.
- Removing a scene object does not dispose a shared mesh, material, or texture or
  invalidate another object that references it.
- Dependencies in this milestone do not need explicit resource disposal. A future
  GPU resource wrapper must keep the same rule: the owner that created a GPU
  resource releases it, and instances only hold references.

The `HelloScene` renderer caches uploads by `MeshData` reference for its own
lifetime. The shared material renderer also caches one GPU texture per `Texture`
reference. Removing objects therefore does not leak per-instance data; each cache
holds at most one upload per distinct reference it has drawn and is cleared when
the renderer is disposed.

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

`HelloScene` draws one opaque color per vertex. `HelloMaterials` and
`HelloLighting` use the same traversal shape with a shared material-aware
renderer: it uploads one mesh per distinct `MeshData` and one texture per
distinct `Texture` before the frame command buffer is acquired, pushes the model
matrix, normal matrix, material tint, and optional lighting uniforms, binds the
material's texture or a white fallback, and draws the cached geometry. A material
without a texture and a mesh without UVs both use the fallback, so the tint is
always well-defined. `Scene.Lighting == null` makes the renderer unlit;
`HelloLighting` supplies ambient and directional light, while `HelloMaterials`
stays unlit.

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

## `HelloMaterials`

`examples/HelloMaterials` demonstrates materials and shared base-color textures
over the same scene layer. It needs no external asset by default.

- Two objects reference one checkerboard `Material` and one procedural UV-cube
  `MeshData`. The renderer uploads both once.
- A third object reuses the same `Texture` through a blue-tinted material.
- A fourth object uses an untextured solid-orange `Material` and the renderer's
  white fallback texture.
- A PNG, JPEG, TGA, or DDS path on the command line is decoded with
  `Nymeria.Formats.Textures`. A missing, unsupported, oversized, or malformed file
  prints a diagnostic and falls back to the generated checkerboard.
- Camera controls are the shared `FlyCameraControls`. Depth, resize, minimize,
  frame-budget, and shutdown behavior follow `HelloScene`.

The generated checkerboard uses four differently colored quadrants, so a flipped
U or V axis is visible on a face. [MATERIALS.md](MATERIALS.md) documents the
material and texture contract.

## `HelloLighting`

`examples/HelloLighting` demonstrates the optional lighting configuration with
an imported OBJ mesh. It links the same shared material renderer and shader as
`HelloMaterials`.

- Three objects reference one imported OBJ cube mesh, so the renderer uploads the
  geometry once. Two use a checkerboard texture and one uses a solid material.
- One object has a strongly non-uniform scale to expose an incorrect normal
  transform.
- Ambient light is a dim blue-white term. One directional light defaults to a
  bright front-right-above direction.
- Arrow keys move the directional light, `L` toggles ambient-only mode, `[` and
  `]` adjust directional intensity, and `R` resets the light. Camera controls are
  the shared `FlyCameraControls`.
- The example requires vertex normals. An OBJ without `vn` data is rejected with
  a diagnostic and status code `1` before rendering.

[LIGHTING.md](LIGHTING.md) documents the equation, coordinate space, normal
matrix, defaults, and controls.

## Tests

`tests/Nymeria.Core.Tests` covers the scene layer without SDL3 or a GPU:

- identity, translation, rotation, non-uniform scale, and the
  scale-rotation-translation matrix order;
- quaternion normalization and rejection of non-finite or zero-length rotation;
- rejection of non-finite position and scale;
- empty scenes, insertion order, removal order, duplicate rejection, clearing,
  re-adding removed objects, and the object limit;
- shared-mesh semantics, shared-material and default-material semantics, material
  replacement and null rejection, and transform copying on object construction;
- ambient and directional light validation, Lambert terms, normal matrices,
  combined lighting, and `Scene.Lighting` default/replacement semantics.

GPU rendering, window behavior, and input are validated manually with
[SMOKE_TEST.md](SMOKE_TEST.md), not by headless CI.

## Intentionally unsupported

The following are out of scope for this milestone: parent-child transforms,
scene graphs, ECS, instanced draw APIs beyond per-object uniform uploads,
serialization, editors, culling, physics, collision, animation, multiple lights,
point or spot lights, shadows, specular or PBR shading, transparency sorting,
texture streaming or hot reload, MTL or OMSI material parsing, and automatic
unloading of a cached GPU mesh or texture when the last referencing object is
removed.
