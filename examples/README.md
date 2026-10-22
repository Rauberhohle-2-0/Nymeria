# Nymeria examples

The examples are small, runnable programs that demonstrate the engine on macOS.
They are intentionally separate from the reusable projects: they own their
pipelines, input mapping, and frame loop. `Nymeria.Graphics` provides the
GPU device, the window presentation claim, shader and buffer creation, the
swapchain format, and the reusable camera math. `Nymeria.Core` provides the
format-independent model data used by `HelloCube`, `Nymeria.Formats.Obj`
provides the OBJ parser used by `HelloOBJ`, `Nymeria.Formats.O3d` provides
the O3D parser used by `HelloO3D`, and `Nymeria.Formats.DirectX` provides the
text `.x` parser used by `HelloX`. `HelloOBJ`, `HelloO3D`, and `HelloX` share the
renderer, geometry builder, shader, model-viewer loop, and input mapping in
`examples/Shared`. `HelloScene` uses the scene layer in `Nymeria.Core`, the
shared geometry builder, the shared model shader, and the shared camera controls.
`HelloMaterials` uses the material and texture data in `Nymeria.Core`, the
PNG/JPEG/TGA/DDS/BMP loader in `Nymeria.Formats.Textures`, and the shared camera
controls. `HelloMaterials`, `HelloLighting`, `HelloCulling`, `HelloCollision`,
`HelloLOD`, `HelloStreaming`, and `HelloPicking` share the material renderer and
shader in `examples/Shared`;
`HelloLighting` adds the lighting data in `Nymeria.Core`, the OBJ parser in
`Nymeria.Formats.Obj`, and the same texture loader, while `HelloCulling` adds the
headless bounds and frustum helpers in `Nymeria.Core.Culling`, `HelloCollision`
adds the box, sphere, and scene collision queries in `Nymeria.Core.Collision`,
`HelloLOD` adds the explicit mesh-variant and distance-selection data in
`Nymeria.Core.Lod`, `HelloStreaming` adds the spatial chunk layer in
`Nymeria.Core.Streaming`, and `HelloPicking` adds the raycasting and
viewport-picking layer in `Nymeria.Core.Raycasting`.
`HelloBenchmark` reuses the shared material renderer with a fixed camera and the
bounded timing helpers in `Nymeria.Core.Diagnostics`. `HelloAudio` uses
`Nymeria.Audio` for 2D and spatial playback and `Nymeria.Core.Audio` for its
listener and spatial status math.

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
dotnet run --project examples/HelloX/HelloX.csproj
dotnet run --project examples/HelloScene/HelloScene.csproj
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj
dotnet run --project examples/HelloLighting/HelloLighting.csproj
dotnet run --project examples/HelloCulling/HelloCulling.csproj
dotnet run --project examples/HelloCollision/HelloCollision.csproj
dotnet run --project examples/HelloAudio/HelloAudio.csproj
dotnet run --project examples/HelloLOD/HelloLOD.csproj
dotnet run --project examples/HelloStreaming/HelloStreaming.csproj
dotnet run --project examples/HelloStreaming/HelloStreaming.csproj -- --no-streaming
dotnet run --project examples/HelloPicking/HelloPicking.csproj
dotnet run --project examples/HelloBenchmark/HelloBenchmark.csproj -- --instances 2048
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

`HelloCube`, `HelloOBJ`, `HelloO3D`, `HelloX`, `HelloScene`, `HelloMaterials`,
`HelloLighting`, `HelloCulling`, `HelloCollision`, `HelloAudio`, `HelloLOD`,
`HelloStreaming`, and `HelloPicking` share
`examples/Shared/FlyCameraControls.cs`, so these controls apply to all thirteen
interactive examples.

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

`HelloLighting` adds light keys: `L` toggles ambient-only mode, the arrow keys
move the directional light, `[` and `]` lower and raise its intensity, and `R`
resets the light. These are edge-triggered except for the arrow keys, which
rotate continuously.

`HelloCulling` adds `C`, which toggles frustum culling. The example also accepts
`--no-culling` to start with culling disabled for a comparison session.

`HelloLOD` adds `L` to toggle LOD, `X` to toggle distance culling, `F` to toggle
frustum culling, `[` and `]` to change both thresholds and the maximum distance,
and `R` to reset them. It also accepts `--no-lod`, `--no-distance-culling`, and
`--no-culling` for comparison sessions.

`HelloStreaming` adds `S` to toggle automatic streaming, `T` to cycle teleport
positions, `C` to toggle frustum culling, and `R` to reset the camera. It also
accepts `--no-streaming`, `--no-culling`, `--load-distance`, and
`--unload-distance` for baseline sessions.

`HelloPicking` adds left-click picking with the cursor, `V` to switch between
cursor and center-of-screen rays, `G` to toggle the ghost box's pickability, and
`R` to reset the camera. It has no command-line options other than `--help`.

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

## HelloX

`HelloX` loads a text DirectX `.x` file and renders it through the same shared
renderer, camera, depth test, input mapping, resize handling, and shutdown path as
`HelloOBJ` and `HelloO3D`. It is the rendering proof for the importer described in
[../docs/DIRECTX_X.md](../docs/DIRECTX_X.md).

```bash
dotnet run --project examples/HelloX/HelloX.csproj
dotnet run --project examples/HelloX/HelloX.csproj -- path/to/model.x
```

- Fixture: [HelloX/Assets/cube.x](HelloX/Assets/cube.x), an original project
  asset. It is the `HelloOBJ` cube expressed in DirectX's left-handed frame with
  clockwise source faces, 24 corners, six quads, normals, texture coordinates,
  and six material labels. The parser converts it back, so `HelloX` shows the
  same cube as `HelloOBJ`. Its provenance and layout are documented in
  [HelloX/Assets/README.md](HelloX/Assets/README.md).
- Default path: the fixture is copied next to the built executable, so the
  no-argument command works from any directory. A supplied path is used as given
  and may be relative to the current directory.
- Errors: a missing, unreadable, or malformed file prints a message that includes
  the parser's source-line diagnostic, then exits with status code `1` before a
  window is created. Binary, compressed, skinned, animated, and unknown
  constructs fail the same way instead of being guessed.
- Conversions: the importer bakes frame transforms, converts the left-handed
  frame and winding, transforms normals with the inverse transpose, and preserves
  Direct3D's top-left texture coordinates. Fan triangulation keeps source order.
- Subset colors: the six fixture labels `front`, `back`, `right`, `left`, `top`,
  and `bottom` map to six solid colors. Unknown labels use one neutral color.
  This is example presentation, not a material system; material colors and
  texture filenames are not imported.
- Scope: no binary or compressed `.x`, animation, skinning, skeleton, effects,
  textures, lighting, hierarchy in the model, or generic asset registry.

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
- Scope: no hierarchy, ECS, serialization, editor, physics, animation,
  occlusion or distance culling, textures, lighting, or material system.
  `HelloCulling` demonstrates whole-instance frustum culling separately.

## HelloMaterials

`HelloMaterials` demonstrates the format-independent material data and the base
color texture path. It needs no external asset by default and uses the same
scene layer, camera, depth test, and input mapping as `HelloScene`.

```bash
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker.jpg
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker.tga
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker-rle.tga
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker.bmp
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker-32.bmp
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
- Shader: `Shared/Shaders/Material.metal`, embedded as
  `Nymeria.Examples.Shaders.Material.metal`, with one vertex and one fragment
  uniform buffer plus one texture/sampler pair. It uses the same row-major matrix
  upload convention as the other examples. The shader supports lighting, but the
  scene sets no `Scene.Lighting`, so this example stays unlit: the renderer
  substitutes a white ambient term and no directional term.
- Formats: a supplied path is decoded by `Nymeria.Formats.Textures`. PNG, JPEG,
  DDS, and BMP are identified by signature; TGA uses a `.tga`/`.targa` name plus
  header validation. The checked-in `Assets` fixtures are original project
  content, and their layout is documented in `Assets/README.md`.
- BMP: uncompressed 24-bit BGR, 32-bit BGRA/BGRX, 8-bit indexed, and standard
  32-bit `BI_BITFIELDS` images, bottom-up or top-down. The 24-bit fixture is
  bottom-up; the 32-bit fixture is top-down and opaque. RLE, 1/4/16-bit, OS/2
  headers, embedded JPEG/PNG streams, and non-standard masks are rejected.
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
- Scope: no MTL or OMSI material parsing, PBR, transparency sorting, texture
  streaming, mipmap generation, CPU block decompression, hot reload, or material
  graphs. Lighting is demonstrated by `HelloLighting` instead.

## HelloLighting

`HelloLighting` demonstrates the minimal lighting model on the existing material
path. It imports its cube from `HelloOBJ/Assets/cube.obj`, loads
`HelloMaterials/Assets/checker.png`, and reuses the shared material renderer,
shader, and camera controls. Both assets are existing original project content,
so the example bundles nothing new.

```bash
dotnet run --project examples/HelloLighting/HelloLighting.csproj
dotnet run --project examples/HelloLighting/HelloLighting.csproj -- path/to/model.obj
```

- Lighting data: `SceneLighting` holds one `AmbientLight` and at most one
  `DirectionalLight`. `Scene.Lighting` is `null` by default, which means unlit;
  `HelloLighting` assigns a new immutable `SceneLighting` only when a light key
  changes the values. See [../docs/LIGHTING.md](../docs/LIGHTING.md).
- Ambient: a dim blue-white ambient term (color `(0.55, 0.65, 0.90)`, intensity
  `0.18`) lifts surfaces that face away from the directional light. Ambient
  depends on neither object position nor camera orientation.
- Directional: one white light with an initial intensity of `1.1`. The light's
  stored direction is the direction it travels; the shader uses the negated
  direction toward the light. The constructor rejects zero-length and non-finite
  directions and normalizes valid ones.
- Objects: three instances of the imported mesh. Two share one checkerboard
  material and one uses a solid orange material. The middle object uses a
  strongly non-uniform scale and a roll, so a wrong normal transform is visible.
- Shading: world-space Lambert diffuse,
  `max(0, dot(normalize(N), normalize(directionToLight)))`, added to the ambient
  term and multiplied by `texture.rgb * tint`. Alpha behavior is unchanged: the
  fragment is opaque.
- Normals: the vertex shader transforms normals with the inverse transpose of the
  model matrix and normalizes them after interpolation. `LightingMath` computes
  the CPU-side matrix and refuses a singular or non-finite transform instead of
  uploading it; the renderer reports that as a startup-time error.
- Control: `L` toggles ambient-only mode (which removes the directional light),
  the arrow keys move the light's yaw and elevation, `[` and `]` change its
  intensity, and `R` resets it. The intensity is clamped to `0..4` for the
  example, within the model's own limit.
- Errors: a missing, unreadable, or malformed OBJ or texture prints a diagnostic
  and exits with status code `1` before a window is created. An OBJ without `vn`
  normals is rejected with a message that asks for normals rather than inventing
  them.
- Scope: no shadows, point or spot lights, specular highlights, PBR, deferred
  rendering, global illumination, gamma-management redesign, or shader graphs.

## HelloCulling

`HelloCulling` demonstrates conservative CPU-side camera-frustum culling through
the existing scene and shared material renderer. It needs no asset file.

```bash
dotnet run --project examples/HelloCulling/HelloCulling.csproj
dotnet run --project examples/HelloCulling/HelloCulling.csproj -- --no-culling
```

- Scene: 551 deterministic instances reference one procedural cube mesh and four
  shared untextured materials, so the renderer uploads the geometry once. Some
  instances use rotation, non-uniform scale, and negative scale.
- Arrangement: a dense grid in front of the camera, instances crossing its side
  planes, instances behind the initial camera, instances beyond the far plane,
  and instances far outside the horizontal view.
- Bounds: `MeshData.Bounds` is computed once from the local positions;
  `BoundingBox.Transform` produces world bounds from all eight corners. The test
  is conservative, so intersecting or uncertain instances are still submitted.
- Frustum: `Frustum.Create` derives and normalizes six planes from
  `view * projection`. Degenerate or non-finite matrices produce an unbounded
  frustum that accepts everything instead of risking a false cull. See
  [../docs/CULLING.md](../docs/CULLING.md).
- Toggle: `C` switches culling at runtime; `--no-culling` starts it disabled.
- Diagnostics: once per second the example prints FPS, instances tested,
  submitted instances, culled instances, and actual indexed draw calls. Instance
  counts are not GPU draw-call counts, and the statistics are CPU-side counts,
  not GPU timings.
- Scope: whole-instance frustum culling only. There is no occlusion culling,
  distance culling, LOD, spatial index, hierarchy, GPU-driven culling, or cached
  world-bounds invalidation. Existing meshes, materials, textures, lighting,
  scene objects, and transforms are never changed by a cull decision. Explicit
  LOD and distance culling are demonstrated by `HelloLOD`.

## HelloLOD

`HelloLOD` demonstrates explicit mesh variants and optional per-object distance
culling through the existing scene and shared material renderer. It needs no
asset file.

```bash
dotnet run --project examples/HelloLOD/HelloLOD.csproj
dotnet run --project examples/HelloLOD/HelloLOD.csproj -- --no-lod
```

- Variants: three original procedural spheres with 576, 100, and 30 triangles.
  The medium and low levels are visibly faceted. All instances share one high
  mesh, one `MeshLod`, and four untextured materials. Each variant is uploaded
  once and reused by reference.
- Distances: thresholds scale with `[` and `]` from 50% to 300% of the base
  14/38 units, and the optional maximum distance scales with them from the base
  120 units. Hysteresis is 0.75 world units around each threshold.
- Scene: forty-five instances in three receding columns plus three larger
  objects. The larger objects show that distance is measured to the conservative
  bounding volume, not the object origin.
- Toggles: `L` disables LOD and distance culling and draws the high mesh; `X`
  disables distance culling while keeping LOD; `F` toggles frustum culling.
  `R` resets the threshold scale.
- Diagnostics: once per second the example prints FPS, render-time average and
  p95, candidates, frustum- and distance-culled instances, drawn instances, draw
  calls, submitted triangles, and per-level usage. The first line is a zero
  startup sample.
- Comparison: run with `--no-lod`, `--no-distance-culling`, or `--no-culling` and
  compare the same camera path and statistics interval. Do not interpret an FPS
  difference under the 16 ms frame budget as a proven GPU improvement.
- Scope: explicit application-supplied variants only. Nymeria does not
  simplify, generate, stream, morph, or cross-fade meshes, and there is no
  per-subset, occlusion, spatial-index, or GPU-driven LOD. See
  [../docs/LOD.md](../docs/LOD.md).

## HelloStreaming

`HelloStreaming` demonstrates synchronous scene-chunk streaming through the
shared material renderer and the same scene, culling, and LOD paths as the other
examples.

```bash
dotnet run --project examples/HelloStreaming/HelloStreaming.csproj
dotnet run --project examples/HelloStreaming/HelloStreaming.csproj -- --no-streaming
```

- World: a deterministic five-by-five grid of 40-unit chunks with conservative
  bounds. The starting focus loads 9 chunks instead of all 25.
- Sharing: every chunk shares one cube mesh and six materials. The marker mesh is
  unique per load, so unloading one chunk and calling
  `PruneUnusedResources` releases only that chunk's GPU buffers while shared
  geometry stays cached.
- Selection: the load distance is inclusive, the unload distance is strictly
  greater and creates hysteresis, and distances are measured to chunk bounds.
  Four synchronous loads per update bound the work; nearest chunks load first.
- Diagnostics: known/loaded/unloaded/failed chunks, active streamed objects,
  cumulative and last-update loads/unloads/failures, streaming update time, GPU
  entries pruned, FPS, and renderer draw counts.
- Controls: `S` toggles streaming, `T` cycles teleport positions, `C` toggles
  frustum culling, and `R` resets the camera. `--no-streaming` loads the initial
  demand set and then freezes it; `--load-distance` and `--unload-distance` change
  the thresholds.

See [../docs/STREAMING.md](../docs/STREAMING.md) for lifecycle, ownership,
retry semantics, measured observations, and limits.

## HelloPicking

`HelloPicking` demonstrates CPU raycasting and whole-object picking through the
existing scene, streaming, material renderer, camera, and input paths. It needs
no asset file.

```bash
dotnet run --project examples/HelloPicking/HelloPicking.csproj
```

- World: nine deterministic 26-unit chunks stream around the camera. Each chunk
  creates three boxes at different positions, depths, rotations, and non-uniform
  scales, and every chunk shares one cube mesh and six materials.
- Ray source: a left click builds a world ray through the mouse pixel with
  `ViewportRay.TryCreate`; `V` switches to the center-of-screen overload. The
  example scales logical SDL mouse coordinates to the drawable size before
  unprojecting, and the renderer's existing perspective projection supplies the
  matrices. No crosshair is drawn because the examples have no overlay path; the
  window title shows the active mode.
- Selection: `SceneRaycaster.TryRaycast` returns the nearest pickable object.
  Each object is tested with its transformed world bounds, or the conservative
  union of its LOD variants when configured. The selected object receives a
  shared bright highlight material, which is the existing material path rather
  than a new rendering feature.
- Eligibility: the static gray ghost box starts with
  `SceneObject.IsPickable = false`, so a center ray passes through it. `G`
  toggles the flag and demonstrates the difference. Frustum and distance culling
  never make an object unpickable.
- Diagnostics: each click prints one console line, including misses, and the
  window title shows the selected label and distance. There is no per-frame log.
- Streaming: chunk updates run before picking. If the selected object's chunk is
  unloaded, the example validates `Scene.Contains` on the next frame, clears the
  selection, and does not retain a stale current selection.
- Scope: bounds-only picking, no triangle-accurate hits, spatial index, physics,
  collision response, interaction events, or GPU color-ID picking. See
  [../docs/PICKING.md](../docs/PICKING.md).

## HelloCollision

`HelloCollision` demonstrates basic CPU collision detection through the existing
scene, material renderer, camera, and input paths. It needs no asset file. It is
not a physics demo: overlap is detected and highlighted, but nothing is pushed,
stopped, or resolved.

```bash
dotnet run --project examples/HelloCollision/HelloCollision.csproj
```

- Scene: six bodies share a procedural cube or sphere mesh and six base
  materials. The cyan player box, a large center box, a static sphere, an
  orbiting box, an orbiting sphere, and a bobbing box all have explicit local
  colliders. The orbit and bob paths cross the static obstacles without input.
- Shapes: boxes use a local `BoundingBox` matching the cube mesh. Spheres use a
  local `BoundingSphere` of radius one. The collider is separate from the render
  mesh and LOD data; no triangles are tested.
- World space: `SceneCollision` applies each object's current
  `Transform.CreateModelMatrix()`. Boxes use the conservative transformed
  eight-corner AABB. Spheres use the largest absolute axis scale as a conservative
  radius, so rotation changes only the center and non-uniform scale does not
  under-report the extent.
- Feedback: before each frame's query, every base material is restored. Every
  object in a reported overlapping pair receives the shared bright-yellow
  material. This is presentation only; it does not change the transform or
  movement path.
- Controls: arrow keys move the player box in the XZ plane, `P` pauses or resumes
  animation, and `R` resets the player and animation. Shared camera controls are
  `W`/`A`/`S`/`D`, `Space`/`Left Control`, `Shift`, and held right mouse button.
- Diagnostics: once per second the console prints active colliders, tested
  unordered pairs, overlapping pairs, player contact, animation state, measured
  collision-query time, FPS, and drawn instances. The window title shows the pair
  count, player contact, and animation state.
- Scope: box and sphere overlap queries against current scene membership. There
  is no triangle-mesh collision, capsule, compound shape, contact point,
  penetration depth, response, rigid body, gravity, trigger, spatial index, or
  continuous collision detection. The scene pair query is O(n²). See
  [../docs/COLLISION.md](../docs/COLLISION.md).

## HelloAudio

`HelloAudio` demonstrates the audio foundation with no external sound asset. It
generates a seamless four-second stereo ambient loop and two two-second mono
tone loops, writes them as in-memory PCM WAV data, and loads them through
`AudioSystem.LoadWav`. It draws a ground plane and a colored marker for each of
two spatial sources; the camera is the listener.

- Playback: the ambient clip is a non-spatial looping voice at low gain. Sources
  A and B are spatial looping voices with independent positions, minimum and
  maximum distances, and gains.
- Spatial model: linear distance attenuation and equal-power left/right panning
  against the listener position and forward direction. It is not HRTF, occlusion,
  reverb, Doppler, or surround sound. See [../docs/AUDIO.md](../docs/AUDIO.md).
- Listener: the camera position and forward direction are copied into
  `AudioSystem.Listener` every frame before `AudioSystem.Update`.
- Controls: shared camera movement and mouse look; `P` pauses/resumes the ambient
  loop, `O` pauses/resumes source A, `X` stops source A and starts a new voice on
  the same clip, `[`/`]` change master gain, `-`/`=` change source A gain, and
  `R` resets the listener.
- Diagnostics: once per second the console prints device state, voice count,
  master gain, and each voice's state, position, distance, attenuation, pan, and
  gain. With no audio device it prints the failure and keeps rendering with an
  `audio unavailable` status.
- Ownership: voices release their clip references before the system is disposed,
  and clips are disposed last. The example demonstrates the explicit policy for
  stopping a voice instead of destroying a shared clip.

## HelloBenchmark

`HelloBenchmark` is a deterministic, auto-exiting CPU benchmark for the scene and
shared material renderer. It opens the same window and GPU path, measures a
warm-up-separated interval, prints one report per culling mode, then exits
without user input. It is not an interactive example.

```bash
dotnet build Nymeria.slnx --configuration Release
dotnet run --project examples/HelloBenchmark/HelloBenchmark.csproj \
  --configuration Release -- --instances 8192 --warmup 120 --frames 600
```

- Scene: three shared cube variants (768/108/12 triangles), four shared
  untextured materials, a fixed camera, and a deterministic grid with rotation,
  non-uniform scale, and negative scale. Every object carries one shared `MeshLod`
  with thresholds 18/45, maximum distance 90, and hysteresis 0.5. One sixteenth
  of the instances sit behind the camera; larger sizes also extend beyond the
  side and far planes.
- Modes: culling off and on by default, each with its own warm-up. `--culling on`
  or `--culling off` restricts the run. `--lod on`, `--lod off`, or `--lod both`
  selects the LOD comparison; the default is off. `--no-distance-culling`
  disables the configured maximum render distance so LOD can be measured without
  whole-instance distance culling.
- Timing: total frame, event pump, renderer submission, and pacing are separate
  CPU wall-clock intervals. `render` includes culling, command recording,
  submission, and swapchain acquisition, not GPU execution.
- Counters: instances tested, visible/frustum-culled/distance-culled/LOD-drawn
  averages, actual indexed draw calls, submitted triangles, per-level usage,
  managed bytes allocated on the benchmark thread, and process-wide GC counts.
- Options: `--instances`, `--culling`, `--lod`, `--no-distance-culling`,
  `--warmup`, `--frames`, `--no-pacing`, and `--help`. Defaults are 2048
  instances, both culling modes, LOD off, distance culling enabled, 120 warm-up
  frames, and 600 measured frames per mode.
- Scope: CPU profiling only. There is no GPU timestamp query, automatic report
  file, network telemetry, or CI benchmark. See
  [../docs/PERFORMANCE.md](../docs/PERFORMANCE.md) for methodology, metric
  definitions, and reproduction guidance, and
  [../docs/PERFORMANCE_BASELINE_01.md](../docs/PERFORMANCE_BASELINE_01.md) for
  the first recorded results.

## Platform limits

All fifteen examples are macOS-only for now. They require SDL's Metal backend and
MSL source; Windows and Linux need their own native SDL3 packages and shader
formats. `HelloAudio` additionally uses the SDL3 audio device, which is verified
on macOS CoreAudio only. `HelloOBJ`, `HelloO3D`, and `HelloX` use focused format subsets, not
general material or scene importers. `HelloO3D` accepts only unscrambled static meshes
and rejects bones and animation. `HelloCube` still constructs its model data in
code. `HelloScene` adds flat, transformed instances but no hierarchy.
`HelloMaterials` adds a tint and an optional base-color texture from PNG, JPEG,
TGA, BMP, or a documented DDS subset, not a material pipeline. DDS and BMP
support are not universal, and compressed DDS formats depend on backend
capability. `HelloLighting`
adds one ambient term and one directional Lambert term, not a general light
system. `HelloCulling` adds a conservative whole-instance frustum test. `HelloLOD` adds
explicit application-supplied mesh variants, distance culling, and hysteresis;
it does not simplify or generate geometry. `HelloStreaming` adds synchronous
chunk membership with bounded loads per update; it does not stream files in the
background, reclaim CPU assets automatically, or generate terrain. `HelloPicking`
adds conservative bounds picking; it has no triangle-accurate hit, interaction
events, or spatial index. `HelloCollision` adds box and sphere overlap detection
and deterministic pair queries; it has no triangle-mesh collision, contact
manifold, response, rigid body, physics solver, or broad phase. `HelloAudio`
adds decoded PCM playback, 2D and basic stereo spatialization, gain, and
looping; it has no HRTF, occlusion, reverb, Doppler, surround output, compressed
formats, audio capture, or effects graph. No example has
shadows, specular or PBR shading, an object hierarchy, or a physics simulation. GPU output, texture sampling,
compressed-format capability, lighting, frustum culling, LOD selection, distance
culling, collision feedback, audio playback, and input behavior are validated
manually with [../docs/SMOKE_TEST.md](../docs/SMOKE_TEST.md), not in CI.
