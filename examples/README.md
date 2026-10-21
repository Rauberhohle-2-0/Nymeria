# Nymeria examples

The examples are small, runnable programs that demonstrate the engine on macOS.
They are intentionally separate from the reusable projects: they own their
pipelines, input mapping, and frame loop. `Nymeria.Graphics` provides the
GPU device, the window presentation claim, shader and buffer creation, the
swapchain format, and the reusable camera math. `Nymeria.Core` provides the
format-independent model data used by `HelloCube`, `Nymeria.Formats.Obj`
provides the OBJ parser used by `HelloOBJ`, and `Nymeria.Formats.O3d` provides
the O3D parser used by `HelloO3D`. `HelloOBJ` and `HelloO3D` share the renderer,
geometry builder, shader, model-viewer loop, and input mapping in
`examples/Shared`. `HelloScene` uses the scene layer in `Nymeria.Core`, the
shared geometry builder, the shared model shader, and the shared camera controls.
`HelloMaterials` uses the material and texture data in `Nymeria.Core`, the
PNG/JPEG/TGA/DDS loader in `Nymeria.Formats.Textures`, and the shared camera
controls.

All examples use SDL3's GPU API with the Metal backend and Metal Shading
Language (MSL) source. SDL compiles the embedded MSL source when the shaders are
created, so there is no Xcode build step and no generated shader artifact.

From the repository root:

```bash
dotnet restore Nymeria.slnx
dotnet build Nymeria.slnx
dotnet run --project examples/HelloTriangle/HelloTriangle.csproj
dotnet run --project examples/HelloCube/HelloCube.csproj
dotnet run --project examples/HelloOBJ/HelloOBJ.csproj
dotnet run --project examples/HelloO3D/HelloO3D.csproj
dotnet run --project examples/HelloScene/HelloScene.csproj
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj
```

## HelloTriangle

`HelloTriangle` is the minimal regression example. It draws one static triangle
with red, green, and blue corners into a 960 × 540 window and waits up to 100 ms
for events between frames, so idle redraws stay near ten per second.

- Geometry: three interleaved position/color vertices in one vertex buffer.
- Shader: `HelloTriangle/Shaders/Triangle.metal`, embedded as
  `HelloTriangle.Shaders.Triangle.metal`.
- Coordinates: vertices are already in clip space. There is no matrix, camera,
  or depth buffer.
- Viewport: a centered square derived from the swapchain's physical pixels, so
  the triangle keeps its proportions on resize and on high-DPI displays.
- Culling: disabled; the pipeline draws a triangle list with one color target.

## HelloCube

`HelloCube` is the interactive example: seven cubes at fixed positions, a
reusable free-fly camera, perspective projection, and a depth buffer. One central
cube continues to rotate around its Y axis. It draws into a 960 × 540 window,
renders continuously, and targets about 16 ms per frame.

- Geometry: `Nymeria.Core.Models.ModelData` with 24 positions (four per face),
  face normals, texture coordinates, 36 zero-based triangle indices, and six face
  subsets with material names. The example converts the model to 24 interleaved
  position/color vertices and 36 16-bit indices. Corners are duplicated so each of
  the six faces has a solid color instead of colors blending across shared
  vertices. Every cube reuses the same vertex and index buffers and gets its own
  model matrix. The model contract and conventions are in
  [../docs/MODEL_DATA.md](../docs/MODEL_DATA.md).
- Shader: `HelloCube/Shaders/Cube.metal`, embedded as
  `HelloCube.Shaders.Cube.metal`.
- Animation: only the central cube rotates. Its angle is advanced by elapsed
  time, not by frame count, and each frame's delta is capped at 0.1 s so a long
  stall cannot make it jump. Cube positions never change.
- Pacing: each iteration processes pending events without blocking, updates
  input, renders, then delays the remainder of the 16 ms budget. There is no
  busy-wait.
- Uniform data: the vertex shader declares one uniform buffer. `GpuDevice`
  passes that count to SDL, and each cube pushes one 64-byte
  model-view-projection matrix to slot 0 before its draw call.

### Controls

`HelloCube`, `HelloOBJ`, `HelloO3D`, `HelloScene`, and `HelloMaterials` share
`examples/Shared/FlyCameraControls.cs`, so these controls apply to all five
examples.

| Input | Action |
| --- | --- |
| `W` / `S` | Move forward / backward along the horizontal view direction |
| `A` / `D` | Move left / right |
| `Space` / `Left Control` | Move up / down |
| Hold right mouse button | Look around; motion is relative and the cursor is hidden |
| Hold `Shift` | Move faster |

`HelloScene` adds scene-editing keys: `E` adds an instance of the shared mesh,
`Q` removes the last object, and `R` restores the initial objects. The scene keys
are edge-triggered, so holding a key adds or removes only one object per press.

Mouse look rotates only while the right button is held. Releasing the button,
losing input focus, minimizing the window, or exiting disables relative mouse
mode so the cursor is restored. Movement uses measured elapsed time, normalizes
diagonal input, and caps one frame's contribution at 0.1 s. Moving forward while
looking up or down stays in the horizontal plane.

There is no global input system. The examples keep their SDL3 keyboard and mouse
mapping in the shared `FlyCameraControls`, read SDL's cached state after
`Window.ProcessEvents` pumps the queue, and leave close handling and the single
event loop in `Nymeria.Platform` unchanged. SDL reports the movement keys by
physical scancode, so their positions work across keyboard layouts. On a
trackpad, use a two-finger or configured secondary click for mouse look; a
Control-click also presses the key that moves the camera down.

### Space, matrices, and shader layout

The world and eye spaces are right-handed with +Y up, and the camera looks down
-Z when yaw and pitch are zero. The camera lives in
`src/graphics/Nymeria.Graphics/Camera.cs`; it uses `System.Numerics` only and
does not depend on SDL3, the GPU backend, or an example. It stores position,
yaw, and pitch, and exposes the view matrix and a perspective projection factory
that takes the drawable aspect ratio. Pitch is clamped just below a right angle
so the view can never become parallel to the up vector, and yaw is wrapped to
keep long sessions numerically stable.

Each frame:

- the view matrix is `Matrix4x4.CreateLookAt` from the camera position toward the
  camera's pitched forward direction;
- the projection is `Matrix4x4.CreatePerspectiveFieldOfView` with a 45° field of
  view, near plane 0.1, far plane 100, and the swapchain texture's physical
  width divided by its height;
- each cube's model matrix is scale, Y rotation, X tilt, then translation;
- matrices compose as `model * view * projection` because `System.Numerics` uses
  row vectors.

The 64 bytes are uploaded unchanged. `System.Numerics` stores matrices
row-major, while MSL stores `float4x4` column-major. Uploading the row-major
bytes and multiplying `mvp * position` in MSL is therefore correct: MSL reads
the bytes as the transpose, which matches `position * mvp` in C#. Do not
transpose the matrix again in C#.

SDL3 GPU clip space has lower-left `(-1, -1)`, upper-right `(1, 1)`, and depth
`[0, 1]` with the near plane at 0. `CreatePerspectiveFieldOfView` already
produces that `0..1` depth range, which matches Metal, so no y-flip or depth
remap is applied.

### Depth and visibility

The example owns its depth target; it is not part of `Nymeria.Graphics`. At
startup it picks the first supported 2D depth format in this order:
`D32Float`, `D24Unorm`, `D16Unorm`. The pipeline enables depth testing and depth
writes with the `Less` comparison, and culling stays disabled. The depth test,
not triangle winding, resolves which cube faces and which cubes are visible.

The color and depth targets are cleared each frame. The depth target is cleared
to `1.0` and not stored after the render pass. When the swapchain's physical
drawable size changes, the depth texture is recreated at the new size; the GPU is
idle before the old texture is released because earlier command buffers may still
reference it.

Resizing, zero-sized drawables, minimizing, and restoring are handled without
blocking. A minimized window skips rendering and input changes. If a swapchain
texture is acquired but the drawable has zero size, the command buffer is
submitted empty, because SDL only allows cancellation before the swapchain
texture is acquired. The projection aspect ratio is derived only after the
drawable size is known to be non-zero.

## HelloOBJ

`HelloOBJ` loads a Wavefront OBJ file and renders it with the same GPU device,
camera, depth test, input mapping, resize handling, and shutdown conventions as
`HelloCube`. It is the rendering proof for the parser described in
[../docs/OBJ.md](../docs/OBJ.md). `HelloOBJ` and `HelloO3D` share the model
renderer, geometry builder, shader, and frame loop in `examples/Shared`.

```bash
dotnet run --project examples/HelloOBJ/HelloOBJ.csproj
dotnet run --project examples/HelloOBJ/HelloOBJ.csproj -- path/to/model.obj
```

- Fixture: [HelloOBJ/Assets/cube.obj](HelloOBJ/Assets/cube.obj), an original
  project asset. It is a 2 × 2 × 2 cube with six convex quads, positions,
  bottom-left-origin UVs, face normals, and six `usemtl` labels. No MTL file is
  provided or loaded.
- Default path: the fixture is copied next to the built executable, so the
  no-argument command works from any directory. A supplied path is used as given
  and may be relative to the current directory.
- Errors: a missing, unreadable, or malformed file prints a message that includes
  the parser's source-line diagnostic when available, then exits with status
  code `1` before a window is created.
- Geometry conversion: the parsed model is converted to the example's
  position/color layout with 32-bit indices, then uploaded through
  `GpuDevice.CreateBuffer`. Vertices are duplicated per subset because the shader
  carries one color per vertex.
- Subset colors: the six fixture labels `front`, `back`, `right`, `left`, `top`,
  and `bottom` map to six solid colors. Unknown labels use one neutral color.
  This is example presentation, not a material system.
- Shader: `examples/Shared/Shaders/Model.metal`, embedded as
  `Nymeria.Examples.Shaders.Model.metal` in both `HelloOBJ` and `HelloO3D`. It
  uses the same row-major matrix upload and one uniform buffer as the cube
  shader.
- Camera: framed from the parsed model's bounds so the fixture and ordinary
  user-supplied models start visible. It uses the shared `FlyCameraControls`.
- Scope: no textures, lighting, material properties, animation, object hierarchy,
  or generic asset registry.

## HelloO3D

`HelloO3D` loads an OMSI `.o3d` file and renders it through the same shared
renderer, camera, depth test, input mapping, resize handling, and shutdown path as
`HelloOBJ`. It is the rendering proof for the importer described in
[../docs/O3D.md](../docs/O3D.md).

```bash
dotnet run --project examples/HelloO3D/HelloO3D.csproj
dotnet run --project examples/HelloO3D/HelloO3D.csproj -- path/to/model.o3d
```

- Fixture: [HelloO3D/Assets/cube.o3d](HelloO3D/Assets/cube.o3d), a synthetic
  project asset. It re-expresses the `HelloOBJ` cube in the O3D frame and winding:
  version 3, 16-bit indices, unscrambled, 24 vertices, 12 triangles, and six
  material subsets. Its provenance is documented in
  [HelloO3D/Assets/README.md](HelloO3D/Assets/README.md). It is not an OMSI game
  asset and is not evidence of real-world compatibility.
- Default path: the fixture is copied next to the built executable, so the
  no-argument command works from any directory. A supplied path is used as given
  and may be relative to the current directory.
- Errors: a missing, unreadable, or malformed file prints a message that includes
  the parser's offset diagnostic when available, then exits with status code `1`
  before a window is created. Scrambled files, bones, unknown sections, and
  unknown versions or flags fail the same way instead of being guessed.
- Geometry conversion: the importer already converts positions and normals to
  Nymeria's right-handed frame and reverses triangle winding. The example builds
  the shared position/color layout with 32-bit indices and uploads it through
  `GpuDevice.CreateBuffer`. Vertices are duplicated per subset because the shader
  carries one color per vertex.
- Subset colors: the six fixture labels `front`, `back`, `right`, `left`, `top`,
  and `bottom` map to six solid colors. Unknown labels use one neutral color.
  This is example presentation, not a material system.
- Scope: no textures, lighting, animation, bones, skinning, scrambling, pivot
  transforms, object hierarchy, or generic asset registry.

## HelloScene

`HelloScene` demonstrates `Nymeria.Core.Scenes`: four initial objects reference
one shared procedural cube mesh at different positions, rotations, and
non-uniform scales. It uses the existing model shader, depth test, shared
geometry builder, and free camera. The scene data has no SDL3, GPU, or file
format dependency.

```bash
dotnet run --project examples/HelloScene/HelloScene.csproj
```

- Mesh: a deterministic six-color cube built as `MeshData` in
  `HelloScene/SceneFixtures.cs`. It is original project content and requires no
  OBJ, O3D, or OMSI asset.
- Sharing: all objects reference the same `MeshData`. `SceneRenderer` caches one
  GPU vertex/index upload per distinct mesh, so adding instances does not
  re-upload geometry.
- Transforms: each object has a `Transform` with a quaternion rotation. The
  model matrix composes `scale * rotation * translation` and then
  `model * view * projection`, using the same row-major upload convention as
  `HelloCube`. See [../docs/SCENE.md](../docs/SCENE.md).
- Runtime editing: `E` adds a deterministic instance, `Q` removes the last
  object, and `R` restores the initial four. Removing every object leaves an
  empty scene that still clears the color and depth targets.
- Lifetime: the scene owns no GPU resources. The renderer releases its cached
  uploads, depth target, pipeline, shaders, and device on shutdown.
- Scope: no hierarchy, ECS, serialization, editor, physics, animation, culling,
  textures, lighting, or material system.

## HelloMaterials

`HelloMaterials` demonstrates the format-independent material data and the base
color texture path. It needs no external asset by default and uses the same
scene layer, camera, depth test, and input mapping as `HelloScene`.

```bash
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker.jpg
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker.tga
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker-rle.tga
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker-bc1.dds
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker-rgba.dds
```

- Materials: a `Material` carries an optional `TextureData` reference and a
  base-color tint. Materials and textures are CPU data with no GPU handle; see
  [../docs/MATERIALS.md](../docs/MATERIALS.md).
- Generated texture: with no argument, the example builds a 128 × 128
  checkerboard with four differently colored quadrants and an alternating
  brightness pattern. It is original project content generated in code, so no
  image file is checked in.
- Sharing: two objects reference one checkerboard material and one procedural
  UV-cube mesh. `MaterialRenderer` caches one GPU mesh per distinct `MeshData`
  and one GPU texture per distinct `TextureData`, so neither is uploaded twice.
- Tint: a third object reuses the same `TextureData` through a blue-tinted
  material. The fragment shader samples the base-color texture and multiplies its
  RGB value by the tint; alpha is not blended.
- Solid color and fallback: a fourth object uses an untextured orange material.
  The renderer binds a generated 1 × 1 white texture, so the tint is displayed
  unchanged. A mesh without UVs also binds that fallback.
- UV orientation: the model contract uses a top-left origin with `+V` down, and
  the vertex layout passes `(0, 1)` at the bottom-left and `(0, 0)` at the
  top-left of each face as seen from outside. The four quadrants make a flipped
  axis visible.
- Shader: `HelloMaterials/Shaders/Material.metal`, embedded as
  `HelloMaterials.Shaders.Material.metal`, with one vertex and one fragment
  uniform buffer plus one texture/sampler pair. It uses the same row-major matrix
  upload convention as the other examples.
- Formats: a supplied path is decoded by `Nymeria.Formats.Textures`. PNG, JPEG,
  and DDS are identified by signature; TGA uses a `.tga`/`.targa` name plus
  header validation. The checked-in `Assets` fixtures are original project
  content, and their layout is documented in `Assets/README.md`.
- JPEG: decoded through the pure-managed `StbImageSharp` package with Nymeria's
  own input bound, segment-chain validation, and dimension checks. Alpha is
  opaque. EXIF orientation is not applied.
- TGA: uncompressed or RLE true-color, 24-bit BGR or 32-bit BGRA, both origin
  flags. Color maps, interleaving, and other depths are rejected.
- DDS: BC1, BC2, BC3, BC4, BC5, and BC7 block data (legacy fourCC and DX10
  formats, including sRGB where available) and supported uncompressed RGBA8
  layouts. Mip chains upload once. Block-compressed regions must be block
  aligned; levels below 4 × 4 stay on the CPU, and a single-level or mipped
  sampler is chosen to match.
- GPU capability: if the backend cannot sample a compressed DDS format, or a
  compressed texture is not block aligned, the example prints a diagnostic and
  uses the generated checkerboard instead.
- Errors: a missing, unreadable, malformed, oversized, or unsupported file prints
  a diagnostic and falls back; the window still opens. Limits are 32 MiB of
  encoded input, 4096 × 4096 pixels, and 64 MiB of payload.
- Scope: no MTL or OMSI material parsing, lighting, PBR, transparency sorting,
  texture streaming, mipmap generation, CPU block decompression, hot reload, or
  material graphs.

## Platform limits

All six examples are macOS-only for now. They require SDL's Metal backend and
MSL source; Windows and Linux need their own native SDL3 packages and shader
formats. `HelloOBJ` and `HelloO3D` use focused format subsets, not general
material or scene importers. `HelloO3D` accepts only unscrambled static meshes
and rejects bones and animation. `HelloCube` still constructs its model data in
code. `HelloScene` adds flat, transformed instances but no hierarchy.
`HelloMaterials` adds a tint and an optional base-color texture from PNG, JPEG,
TGA, or a documented DDS subset, not a material pipeline. DDS support is not
universal, and compressed formats depend on backend capability. No example has
lighting, an object hierarchy, or collisions. GPU output, texture sampling,
compressed-format capability, and input behavior are validated manually with
[../docs/SMOKE_TEST.md](../docs/SMOKE_TEST.md), not in CI.
