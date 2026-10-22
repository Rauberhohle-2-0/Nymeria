# Nymeria Architecture

This document is the authoritative description of Nymeria's purpose, repository
layout, and design boundaries. It separates what exists today from what is
planned. Architecture changes require the maintainer's approval and a
corresponding update to this document.

## Purpose

Nymeria is a small, understandable, cross-platform 3D engine written in C#. The
project values clarity over breadth: it should be possible to read the codebase
and understand how it works. The engine is intended to run on Windows, macOS,
and Linux, and may later be extended for retro-game compatibility. That
extension is a direction, not a current feature.

## Current status

### Implemented now

- A single Git monorepo with the documented directory layout.
- [Architecture documentation](ARCHITECTURE.md) and
  [Git guidelines](GIT_GUIDELINES.md).
- Root `.editorconfig`, `.gitignore`, and `Directory.Build.props` files.
- .NET SDK 10.0.402 pinned in `global.json`, targeting `net10.0`.
- `Nymeria.slnx`, containing the C# projects that exist today.
- `Nymeria.Platform`, a class library exposing a minimal native window API.
- `Nymeria.Core`, a dependency-free class library containing validated,
  format-independent model data, the `IModelParser` contract, minimal scene data
  (`Scene`, `SceneObject`, and `Transform`), optional explicit mesh
  level-of-detail configurations, optional ambient and directional lighting
  data, conservative bounds and frustum-culling helpers, a validated CPU
  collision layer with box and sphere overlap queries, bounded frame-timing
  diagnostics, format-independent material and texture data with RGBA and
  block-compressed mip chains, and headless transform animation tracks, clips,
  and playback.
- `Nymeria.Formats.Obj`, a dependency-free Wavefront OBJ parser implementing
  `IModelParser` for a documented polygonal subset.
- `Nymeria.Formats.O3d`, a dependency-free OMSI O3D parser implementing
  `IModelParser` for a documented unscrambled static-mesh subset.
- `Nymeria.Formats.DirectX`, a dependency-free text DirectX `.x` parser
  implementing `IModelParser` for a documented static-mesh subset with baked
  frame transforms, normals, texture coordinates, and material-name subsets.
- `Nymeria.Formats.Png`, a dependency-free PNG decoder producing validated RGBA
  texture data for a documented, bounded subset.
- `Nymeria.Formats.Tga`, a dependency-free TGA decoder for documented
  uncompressed and RLE true-color subsets.
- `Nymeria.Formats.Jpeg`, a JPEG decoder built on the pure-managed
  `StbImageSharp` package, with Nymeria-owned input bounds, structural validation,
  and dimension checks.
- `Nymeria.Formats.Dds`, a dependency-free DDS header and payload decoder for
  documented BC1–BC5/BC7 and uncompressed subsets, returning compressed or RGBA
  texture data without decompressing blocks.
- `Nymeria.Formats.Bmp`, a dependency-free BMP decoder for documented
  uncompressed 24-bit, 32-bit, 8-bit indexed, and standard `BI_BITFIELDS`
  subsets, returning validated top-down RGBA texture data.
- `Nymeria.Formats.Textures`, a signature-first `TextureLoader` that dispatches
  to the PNG, JPEG, TGA, DDS, and BMP decoders without touching the GPU.
- `Nymeria.Formats.Wav`, a dependency-free, bounded RIFF/WAVE decoder for
  documented mono/stereo integer PCM and 32-bit float subsets. It returns
  validated `PcmAudioData` and never opens a file or touches a device.
- `Nymeria.Core.Audio`, validated interleaved PCM clip data with bounded linear
  resampling, a listener whose axes match the camera, and pure distance
  attenuation, stereo panning, configurable positional stereo strength, and
  equal-power gain math. It is headless and contains no SDL3, GPU, window, or
  format dependency.
- `Nymeria.Audio`, an SDL3-backed playback system with a bounded managed stereo
  mixer, shared clips, independent 2D and spatial voices, pause/resume/stop,
  looping, per-source and master gain, a runtime-adjustable positional stereo
  effect strength, and explicit device and thread ownership.
- `Nymeria.Core.Animation`, immutable keyframe tracks for translation, rotation,
  and scale, a validated transform clip with deterministic sampling, and a
  per-target playback controller. It is headless, writes ordinary scene
  transforms, and contains no SDL3, GPU, window, format, or scene-membership
  dependency.
- `Nymeria.Graphics`, a class library that owns the SDL3 GPU device and window
  presentation claim, provides reusable MSL shader, buffer, texture, and sampler
  creation, and contains a reusable, SDL-independent free-fly `Camera`.
- `examples/HelloTriangle`, a console executable that opens the window and draws
  the triangle with example-owned shaders and geometry.
- `examples/HelloCube`, an interactive console executable that draws multiple
  fixed-position cubes with a free-fly camera, perspective projection, depth
  testing, example-owned shaders and geometry from a format-independent model,
  and an example-owned depth target.
- `examples/HelloOBJ`, an interactive model viewer that parses a checked-in OBJ
  fixture or a user-supplied path and renders it through the existing GPU path.
- `examples/HelloO3D`, the same interactive viewer for an O3D fixture or a
  user-supplied path, using the shared example renderer.
- `examples/HelloX`, the same interactive viewer for a text DirectX `.x`
  fixture or a user-supplied path, using the shared example renderer.
- `examples/HelloScene`, an interactive example that draws multiple transformed
  instances of one shared mesh through the scene layer, with runtime add/remove.
- `examples/HelloMaterials`, an interactive example that draws shared, tinted,
  and untextured materials with a generated checkerboard or a user-supplied PNG,
  JPEG, TGA, DDS, or BMP texture. It remains unlit.
- `examples/HelloLighting`, an interactive example that draws an imported OBJ
  mesh with a textured and a solid material under configurable ambient and
  directional Lambert lighting.
- `examples/HelloCulling`, an interactive example that draws many instances of
  one shared mesh with optional conservative camera-frustum culling, a runtime
  toggle, and per-frame visibility statistics.
- `examples/HelloCollision`, an interactive example that moves and animates box
  and sphere colliders, highlights current overlapping pairs, and prints active
  collider, tested-pair, overlapping-pair, and query-time diagnostics.
- `examples/HelloLOD`, an interactive example that switches each instance among
  three explicit sphere-detail meshes at configurable camera distances, with
  optional distance culling, a free camera, and runtime LOD, distance, and
  frustum toggles.
- `examples/HelloStreaming`, an interactive five-by-five chunk world that loads
  and unloads deterministic chunks around the camera, reuses shared meshes and
  materials, prunes unused GPU caches after unload, and prints streaming, GPU
  cache, and renderer diagnostics. It includes teleport, streaming, and culling
  controls and a frozen-streaming baseline option.
- `examples/HelloBenchmark`, an auto-exiting Release benchmark with a fixed
  camera, deterministic 512/2048/8192-instance scenes, culling, LOD, and pacing
  modes, bounded CPU phase timings, and allocation/GC counters.
- `examples/HelloPicking`, an interactive streamed world with cursor and
  center-of-screen picking, a material-based selection highlight, a non-pickable
  ghost box toggle, and click-driven console and window-title diagnostics.
- `examples/HelloAudio`, an interactive example with a generated looping 2D
  ambient sound and two generated spatial sources. The camera is the listener,
  source markers and a ground plane show the spatial layout, and the console
  reports device state, distance, attenuation, pan, positional stereo strength,
  and gains. A stereo-strength control with 0/50/100% presets and an optional
  constant-distance left-to-right source sweep demonstrate the setting. It keeps
  running with an explicit status when no audio device is available.
- `examples/HelloAnimation`, an interactive example with three independent
  transform clips: a door rotating around its authored hinge, a wiper sweeping
  around its base, and a cube that moves and scales. It demonstrates playback
  speed, looping, pause/resume, restart, pivot placement, and per-player
  diagnostics.
- `MeshData.Bounds` plus `Nymeria.Core.Culling` bounds, frustum, and per-frame
  visibility helpers, `Nymeria.Core.Lod` explicit mesh variants, validated
  activation distances, conservative union bounds, and hysteresis-aware
  selection, `Nymeria.Core.Streaming` validated scene-chunk definitions,
  bounded synchronous load/unload updates, failure retry, and diagnostics, and
  `Nymeria.Core.Raycasting` validated world-space rays, AABB intersection,
  nearest-hit scene queries, viewport unprojection, and pick eligibility, and
  `Nymeria.Core.Collision` validated box and sphere primitives, inclusive
  overlap tests, world-space conversion, an optional scene-object collider, and
  deterministic O(n²) pair queries, and `Nymeria.Core.Rendering.BlendOrder`
  deterministic back-to-front ordering for blended instances. They are headless,
  use `System.Numerics` only, and contain no SDL3, GPU, window, or format
  dependency.
- `Nymeria.Platform.Tests`, automated tests for platform-independent options.
- `Nymeria.Graphics.Tests`, GPU-independent tests for camera state validation,
  view/projection conventions, and matrix diagnostics.
- `Nymeria.Core.Tests`, GPU-independent tests for model-data validation,
  parser-contract error behavior, transform composition, scene lifecycle,
  lighting data and math, materials, texture-data validation, mesh bounds,
  frustum-plane intersection, visibility counters, LOD validation, distance and
  hysteresis selection, conservative LOD bounds, streaming selection and
  hysteresis, load budgets, teleports, failure retry, content rollback, shared
  references, chunk identifiers, counters, ray and AABB boundary behavior,
  scene nearest-hit selection and ties, transformed bounds, pick eligibility,
  viewport camera rays, picking independence from frustum culling and LOD,
  collision shape validation, inclusive box and sphere overlap, conservative
  collision transforms, scene collider filtering and lifecycle, deterministic
  pair ordering, bounded frame-timing aggregation with warm-up exclusion and
  invalid samples, audio distance attenuation at and beyond the range with
  invalid values, stereo panning for rotated listeners and degenerate bases,
  positional stereo strength interpolation, symmetry, constant power, and
  invalid input, listener validation, PCM data validation and resampling,
  transform animation track sampling and validation, quaternion shortest-path
  interpolation, clip boundaries and limits, playback lifecycle, looping, delta
  partitioning, and deterministic multi-player conflicts.
- `Nymeria.Formats.Obj.Tests`, GPU-independent tests for the OBJ subset,
  conversions, and diagnostics.
- `Nymeria.Formats.O3d.Tests`, GPU-independent tests for the O3D header,
  supported versions and flags, geometry conversion, limits, and failure
  behavior.
- `Nymeria.Formats.Png.Tests`, GPU-independent tests for the PNG subset, malformed
  and truncated input, decompression limits, `tRNS` transparency, and decoded
  texture data.
- `Nymeria.Formats.Tga.Tests`, `Nymeria.Formats.Jpeg.Tests`,
  `Nymeria.Formats.Dds.Tests`, and `Nymeria.Formats.Bmp.Tests`,
  GPU-independent tests for their supported subsets, orientation, channel order,
  mip layouts, malformed input, and limits.
- `Nymeria.Formats.DirectX.Tests`, GPU-independent tests for the text `.x`
  header, frames, mesh elements, transforms, materials, conversions, malformed
  input, and resource limits.
- `Nymeria.Formats.Textures.Tests`, GPU-independent tests for signature-based
  format detection and loader error behavior.
- `Nymeria.Formats.Wav.Tests`, GPU-independent tests for PCM and float decoding,
  truncated and malformed chunks, duplicate and missing chunks, unsupported
  formats, overflow, and input, frame, and payload limits.
- `Nymeria.Audio.Tests`, device-independent tests for the bounded stereo mixer,
  voice lifecycle, looping, overlapping voices, gain and clipping, positional
  stereo strength, shared clip ownership, voice limits, concurrent mixing and
  control, zero allocation per mix block, and options validation. Device checks
  are explicitly skipped with a reason.
- SDL3 windowing, audio playback, and GPU rendering through the pinned
  `SDL3-CS` and `SDL3-CS.MacOS` packages.
- A `Repository checks` GitHub Actions workflow that validates repository
  foundations and restores, builds, and tests the solution on macOS, Windows, and
  Linux.
- Weekly Dependabot updates for GitHub Actions and NuGet, auto-merged only after
  repository checks pass.

### Planned later

The following are future work and are **not** implemented or approved in detail:

- Engine-level input handling and an engine loop.
- Further O3D features beyond the documented unscrambled static-mesh subset,
  such as scrambled vertices, bones, animation, and material rendering.
- Additional graphics backends and a rendering abstraction.

Names and responsibilities in this section are planning boundaries, not
interfaces or commitments to a specific design.

## Why one monorepo

Nymeria is a single engine that targets several platforms, not three separate
products. A monorepo keeps the shared engine code, platform backends, examples,
tests, and documentation in one history. That makes cross-platform changes
reviewable as one logical change, avoids version skew between repositories, and
keeps a single issue tracker and CI configuration.

Platform-specific implementations may later live in separate
directories/projects such as `src/platform`, but they remain part of this
repository. Splitting them into separate Git repositories is out of scope and
would require maintainer approval.

## Directory responsibilities

| Path | Intended responsibility |
| --- | --- |
| `src/audio/` | `Nymeria.Audio`: SDL3 playback device, mixer, clips, and voices. |
| `src/core/` | Platform-independent engine fundamentals in `Nymeria.Core`. |
| `src/formats/` | Format-specific model, image, and audio parsers and decoders in isolated projects. |
| `src/graphics/` | `Nymeria.Graphics`: reusable SDL3 GPU device and resource infrastructure. |
| `src/platform/` | `Nymeria.Platform`: windowing, input, and operating-system integration. |
| `examples/` | Small runnable programs that demonstrate engine usage. |
| `tests/` | Automated unit and integration tests for the engine projects. |
| `docs/` | Authoritative project and architecture decisions. |
| `.github/` | Repository automation: checks and dependency updates. |

`src/audio/` contains `Nymeria.Audio`, `src/platform/` contains
`Nymeria.Platform`, `src/graphics/` contains `Nymeria.Graphics`, `src/core/`
contains `Nymeria.Core`, `src/formats/` contains `Nymeria.Formats.Obj`,
`Nymeria.Formats.O3d`, `Nymeria.Formats.DirectX`, `Nymeria.Formats.Png`,
`Nymeria.Formats.Tga`, `Nymeria.Formats.Jpeg`, `Nymeria.Formats.Dds`,
`Nymeria.Formats.Bmp`, `Nymeria.Formats.Textures`, and
`Nymeria.Formats.Wav`, `examples/` contains `HelloTriangle`, `HelloCube`,
`HelloOBJ`, `HelloO3D`, `HelloX`, `HelloScene`, `HelloMaterials`,
`HelloLighting`, `HelloCulling`, `HelloLOD`, `HelloStreaming`, `HelloPicking`,
`HelloAudio`, `HelloAnimation`, and `HelloBenchmark`, and `tests/` contains
`Nymeria.Core.Tests`,
`Nymeria.Formats.Obj.Tests`, `Nymeria.Formats.O3d.Tests`,
`Nymeria.Formats.DirectX.Tests`, `Nymeria.Formats.Png.Tests`,
`Nymeria.Formats.Tga.Tests`, `Nymeria.Formats.Jpeg.Tests`,
`Nymeria.Formats.Dds.Tests`, `Nymeria.Formats.Bmp.Tests`,
`Nymeria.Formats.Textures.Tests`, `Nymeria.Formats.Wav.Tests`,
`Nymeria.Audio.Tests`, `Nymeria.Platform.Tests`, and
`Nymeria.Graphics.Tests`.

`Nymeria.Core` owns the format-independent model data and parser contract
described in [MODEL_DATA.md](MODEL_DATA.md), the minimal scene data described in
[SCENE.md](SCENE.md), the explicit mesh level-of-detail data described in
[LOD.md](LOD.md), the spatially bounded scene-chunk streaming layer described in
[STREAMING.md](STREAMING.md), the headless raycasting and object-picking layer
described in [PICKING.md](PICKING.md), the material and texture data
described in [MATERIALS.md](MATERIALS.md), the headless audio clip, listener,
and spatial math described in [AUDIO.md](AUDIO.md), and the headless transform
animation tracks, clips, and playback controller described in
[ANIMATION.md](ANIMATION.md). It references
`System.Numerics` and the base class library only. The camera lives in `Nymeria.Graphics` rather than
in `Nymeria.Core`: it is reusable 3D rendering math with no SDL, window, or GPU
dependency, and the model-loading foundation is the first demonstrated need for
the core project. `Nymeria.Formats.Obj` and `Nymeria.Formats.O3d` reference only
`Nymeria.Core` and the base class library; their supported subsets are documented
in [OBJ.md](OBJ.md) and [O3D.md](O3D.md); the `.x` subset is documented in
[DIRECTX_X.md](DIRECTX_X.md). `Nymeria.Formats.Png`,
`Nymeria.Formats.Tga`, `Nymeria.Formats.Dds`, and `Nymeria.Formats.Bmp` also
reference only `Nymeria.Core` and the base class library.
`Nymeria.Formats.Jpeg` references `Nymeria.Core` and the pure-managed
`StbImageSharp` package. `Nymeria.Formats.Textures` references `Nymeria.Core`
and the five image decoders. `Nymeria.Formats.Wav` references `Nymeria.Core` and
the base class library only; [AUDIO.md](AUDIO.md) documents its supported subset
and limits. `Nymeria.Audio` references `Nymeria.Core`, `Nymeria.Formats.Wav`,
and the `SDL3-CS` managed binding; it owns all SDL audio calls and exposes no
SDL type in its public API.
`Nymeria.Core.Lighting` references `System.Numerics` and the base class library
only; [LIGHTING.md](LIGHTING.md) documents the equation, conventions, and limits.
[MATERIALS.md](MATERIALS.md) documents every supported image subset and limit.

## Windowing

`Nymeria.Platform` owns native window creation. Its public surface is deliberately
small:

- `WindowOptions` validates and carries the title and initial client size. The
  defaults are the title `Nymeria` and 960 × 540.
- `Window.Create` initializes SDL video and creates a visible, resizable native
  window with a high-pixel-density drawable.
- `Window.Handle` exposes the native `SDL_Window*` handle so rendering code can
  claim the window without depending on `Nymeria.Platform`.
- `Window.ProcessEvents` waits for at least one event, drains the queue, and
  reports whether the window should stay open. The close button and SDL quit
  events, including the macOS standard quit action, end the loop.
- `Window.Dispose` destroys the window and shuts SDL down deterministically,
  including when creation fails after SDL initialization.

Windowing uses SDL3 through two pinned NuGet packages:

| Package | Version | Purpose | License |
| --- | --- | --- | --- |
| `SDL3-CS` | 3.4.18 | Managed SDL3 binding | zlib |
| `SDL3-CS.MacOS` | 3.4.18 | Native `libSDL3.dylib` for `osx-arm64` and `osx-x64` | zlib |

`Nymeria.Platform.csproj` owns both package references, so examples receive the
binding and native runtime transitively. NuGet restores the dylibs from
nuget.org; no Homebrew installation, manual download, or custom Cocoa interop is
involved. The macOS native runtime and Metal GPU path are validated on macOS. CI
compiles and runs the headless tests on Windows and Linux as well, but does not
launch a GUI there. Windows and Linux native packages are added only when those
platforms are supported.

SDL initialization, window creation, event processing, and disposal must all run
on the process main thread, as macOS requires for its native windowing APIs.
There are no worker threads. `Window.ProcessEvents` blocks in
`SDL_WaitEventTimeout` instead of busy-spinning. One window per process is
supported in this version.

The window is not a renderer. `Nymeria.Graphics` owns the GPU device and claims
the window for presentation through its native handle; the two projects do not
reference each other.

## Model data and parsing

`Nymeria.Core` contains `ModelData`, `MeshData`, `MeshSubset`,
`ModelFormatException`, and `IModelParser` in the `Nymeria.Core.Models`
namespace. The types hold positions, optional normals and texture coordinates,
zero-based `uint` triangle indices, triangle-aligned subsets, and optional opaque
material names. They contain no GPU resources, SDL3, window state, or
format-specific logic.

Construction is the validation boundary. Empty geometry, non-finite vertex
values, mismatched attribute counts, out-of-range indices, malformed subsets, and
index counts that do not describe whole triangles throw `ModelFormatException`.
The type takes ownership of the geometry arrays without copying them.

`IModelParser` is the small extension point for format parsers:

```csharp
public interface IModelParser
{
    ModelData Parse(Stream stream);
}
```

The caller owns and disposes the stream; a parser reads from its current
position and must not close it. A parser returns a fully validated model or
throws `ModelFormatException` for malformed or unsupported input. There is no
registry, discovery mechanism, or format detection.

`Nymeria.Formats.Obj.ObjModelParser` is the first production implementation. It
reads positions, texture coordinates, normals, and polygon faces; resolves
one-based and negative indices; unifies independent per-corner attribute streams;
fan-triangulates its documented convex polygon subset; preserves `usemtl` labels
as subsets; and reports malformed input with source-line diagnostics. It ignores
`mtllib` and loads no materials. [OBJ.md](OBJ.md) is the authoritative statement
of its supported subset and limitations.

`Nymeria.Formats.O3d.O3dModelParser` is the binary-format implementation. It
reads the O3D signature, version, flags, and unscrambled-key header; imports the
vertex, triangle, and material-name sections; validates and discards the pivot
matrix; converts the Direct3D frame to Nymeria's frame; and rejects scrambled
vertices, bones, unknown tags, and unsupported versions or flags with
offset-based diagnostics. [O3D.md](O3D.md) is the authoritative statement of its
supported subset and limitations.

`Nymeria.Formats.DirectX.XModelParser` is the text `.x` implementation. It
validates the `xof` header, skips top-level template declarations, walks nested
frames, bakes affine `FrameTransformMatrix` transforms into the geometry,
imports positions, polygon faces, normals, texture coordinates, and material
names, fan-triangulates faces, and converts Direct3D's left-handed frame and
winding. It rejects binary, compressed, skinned, animated, and unknown constructs
with line-based diagnostics. Texture filenames and material colors are validated
but not represented. [DIRECTX_X.md](DIRECTX_X.md) is the authoritative statement
of its supported subset and limitations.

The conventions are right-handed with `+Y` up and `-Z` forward, triangle-list
winding counter-clockwise when viewed from outside, zero-based indices, and
texture coordinates with a top-left origin and `+V` down. Parsers convert source
handedness, winding, index bases, and UV origin. [MODEL_DATA.md](MODEL_DATA.md)
documents the complete contract, validation rules, and the extension point for
format parsers. The cube example builds its geometry as model data and converts
it to the existing shader layout; `Nymeria.Core` itself never creates GPU
resources.

`Nymeria.Core.Scenes` adds the smallest layer above model data: a `Scene`
holding deterministic `SceneObject` instances, each with its own `Transform` and
a shared reference to a `MeshData`. `Transform` validates finite position and
scale, normalizes rotation, and composes `scale * rotation * translation` for
`System.Numerics` row vectors. Scenes and objects own no GPU resources. A
renderer owns one upload per distinct mesh and releases it; removing an object
does not dispose or invalidate the shared CPU mesh. [SCENE.md](SCENE.md) is the
authoritative contract. There is no ECS, hierarchy, serialization, editor,
physics simulation, collision response, or animation. Collision detection is a
separate headless overlap-query helper in `Nymeria.Core.Collision`; a scene
object may carry an optional local collision shape, but the scene layer does not
resolve contacts. Culling is a separate headless helper in
`Nymeria.Core.Culling`, not a scene-layer responsibility; it reads scene data and
never mutates it. [CULLING.md](CULLING.md) is the authoritative culling contract.

## GPU rendering

`Nymeria.Graphics` contains reusable graphics infrastructure. `GpuDevice`
creates and owns an SDL3 GPU device, claims a native window for presentation,
reports the swapchain format, and creates MSL shaders, uploaded GPU buffers,
2D RGBA textures, and samplers. It uses SDL3's GPU API through the same `SDL3-CS`
binding; there is no direct Metal interop, native wrapper, second binding, or
software fallback.

`GpuDevice.Create(IntPtr window)`:

- requires the main thread and a non-zero native `SDL_Window*` handle from
  `Window.Handle`;
- checks `SDL_GPUSupportsShaderFormats(MSL, "metal")`;
- creates an SDL GPU device with `SDL_CreateGPUDevice(MSL, debug: false, "metal")`;
- claims the window for presentation and reads its swapchain format.

The demonstrations live in `examples/HelloTriangle`, `examples/HelloCube`,
`examples/HelloOBJ`, `examples/HelloO3D`, `examples/HelloX`,
`examples/HelloScene`, `examples/HelloMaterials`, `examples/HelloLighting`,
`examples/HelloCulling`, `examples/HelloCollision`, `examples/HelloLOD`,
`examples/HelloStreaming`, `examples/HelloPicking`, and
`examples/HelloBenchmark`.
Each has its own vertex data and draw calls, and uses `GpuDevice` for the device, window claim, shader
creation, buffer and texture upload, sampler creation, and swapchain format.
[examples/README.md](../examples/README.md) documents their run commands and
rendering conventions. `HelloOBJ`, `HelloO3D`, and `HelloX` share the renderer,
geometry builder, shader, model-viewer loop, and camera controls in
`examples/Shared`; the only difference is the format parser that produces the
`ModelData`.

`GpuDevice.CreateShader` accepts optional uniform-buffer and sampler counts. SDL
needs the uniform count to bind data pushed with `SDL_PushGPUVertexUniformData`
or `SDL_PushGPUFragmentUniformData`, and the sampler count to bind texture/sampler
pairs. Shaders that declare neither leave both at the default of zero.

`GpuDevice.CreateTexture` accepts tightly packed RGBA pixels and dimensions,
validates its own defensive GPU limits, creates an `R8G8B8A8Unorm` 2D texture with
one mip and sampler usage, and uploads the pixels through a transfer buffer and a
copy pass. A second overload accepts an explicit `SDL_GPUTextureFormat`, one
`TextureUploadRegion` per mip level, and one payload, so block-compressed DDS
data and mip chains upload without CPU decompression. It checks
`SDL_GPUTextureSupportsFormat`, pads each level's transfer offset to 512 bytes,
uploads every level in one copy pass, and exposes `SupportsTextureFormat` for
callers that need to choose a fallback before creating a resource. Neither
overload generates mipmaps, converts color space, or retains the CPU payload. The
caller owns the texture and releases it after pending GPU work has completed.

### Camera

`Nymeria.Graphics` contains `Camera`, a small first-person camera built on
`System.Numerics`. It owns position, yaw, pitch, movement speed, mouse
sensitivity, field of view, and clip planes. It exposes a right-handed view
matrix and a perspective projection factory that takes the drawable aspect
ratio. Pitch is clamped below a right angle, yaw is wrapped, non-finite rotation
deltas are ignored, and diagonal movement is normalized.

Invalid camera state is rejected at the matrix boundary rather than inside the
render loop. `ViewMatrix` throws `InvalidOperationException` when the position is
not finite, when the position magnitude cannot express a distinct look target in
single precision, or when the view basis is degenerate. `CreateProjectionMatrix`
throws `ArgumentOutOfRangeException` for a non-positive or non-finite aspect
ratio, and `InvalidOperationException` when the field of view is not finite and
strictly between 0 and π, the near plane is not finite and positive, or the far
plane is not finite and greater than the near plane. The exception names the
offending property; values are never silently replaced or clamped, and a valid
camera keeps its previous matrices and movement behavior.

The type has no SDL3, window, GPU-device, or example dependency. It is not a
scene graph, controller, input system, or game-loop component. Input mapping,
timing, and demo geometry remain in the example that uses it.

### HelloTriangle

`TriangleRenderer` owns the triangle vertex data, the triangle shader resource,
the vertex buffer, the graphics pipeline, and the draw calls.

The triangle shader asset is
`examples/HelloTriangle/Shaders/Triangle.metal`. It is embedded in the
`HelloTriangle` assembly under `HelloTriangle.Shaders.Triangle.metal` and
compiled from MSL source by SDL when the two shader objects are created. No
shader compiler, Xcode build step, or generated shader artifact is required. The
vertex shader uses `[[stage_in]]` with `[[attribute]]` inputs; the fragment
shader interpolates per-vertex colors.

Geometry is three interleaved position/color vertices in one vertex buffer. The
buffer is uploaded once at startup through a transfer buffer and a copy pass.
The graphics pipeline uses a triangle list, no culling, one color target with the
window's swapchain format, and one sample.

Each frame acquires a command buffer and, without blocking, a swapchain texture,
clears the target, binds the pipeline and vertex buffer, draws three vertices,
and submits. If the window is minimized, or SDL returns no drawable texture, the
command buffer is cancelled and the frame is skipped. The render pass uses a
centered square viewport and scissor sized from the swapchain texture's physical
pixels, so resizing and high-DPI drawables do not stretch the triangle and no
stale window dimensions are used.

`HelloTriangle` draws one frame immediately and then calls
`Window.ProcessEvents(100)` before each subsequent frame. This caps idle redraws
at about ten per second, while resize and other events wake the loop immediately.
The loop does not spin.

### HelloCube

`CubeRenderer` adds perspective projection, a uniform matrix, and a depth buffer.
Its cube is a `Nymeria.Core.Models.ModelData` with 24 positions, face normals,
texture coordinates, 36 zero-based `uint` triangle indices, and six face subsets
with opaque material names. The example converts that model to the existing
interleaved position/color vertex layout and 36 16-bit indices, so each face keeps
a solid color. It owns the cube shaders, both buffers, the pipeline, and the
depth texture. The example places seven cubes at fixed positions and reuses that
geometry for each draw with its own model matrix. The central cube keeps the
previous milestone's rotation; the other cubes stay fixed so camera movement and
perspective are easy to judge.

The cube shader asset is `examples/HelloCube/Shaders/Cube.metal`, embedded under
`HelloCube.Shaders.Cube.metal`. The vertex shader declares one uniform buffer at
slot 0; the example passes `uniformBufferCount: 1` to `CreateShader` and pushes
one 64-byte model-view-projection matrix before each cube's draw call. The
fragment shader passes the interpolated face color through.

The camera comes from `Nymeria.Graphics.Camera`. Its position, yaw, and pitch
define a right-handed view with +Y up and -Z forward at zero rotation.
`CreateLookAt` and `CreatePerspectiveFieldOfView` use the same convention, and
the projection maps depth to `[0, 1]`, which matches SDL3 GPU and Metal clip
space. The example composes `model * view * projection` with `System.Numerics`
row vectors and uploads the matrix unchanged. MSL is column-major with column
vectors, so it reads those bytes as the transpose, making `mvp * position`
equivalent to `position * mvp` in C#. There is no y-flip or depth remap.
`examples/README.md` records the full conventions.

Input mapping lives in `examples/Shared/FlyCameraControls.cs`, not in an engine
project, and is linked into `HelloCube`, `HelloOBJ`, and `HelloO3D`. It reads SDL's
cached keyboard and mouse state after `Window.ProcessEvents` pumps the queue.
Holding the right mouse button enables window-relative mouse mode; releasing it,
losing input focus, minimizing, or disposing disables relative mode and restores
the cursor. The examples ignore input while the window lacks input focus. Close
handling and the single event loop remain in `Nymeria.Platform`.

The example picks the first supported depth format among `D32Float`,
`D24Unorm`, and `D16Unorm`, and uses it for both the depth texture and the
pipeline's depth-stencil state. Depth testing and depth writes are enabled with
the `Less` comparison; culling is disabled, so the depth test resolves occlusion.
The depth target is cleared to `1.0` each frame and not stored. When the
swapchain's physical size changes, the depth texture is recreated and the GPU is
idle before the old texture is released. Projection uses that physical drawable
size, so resizing and high-DPI displays keep the correct aspect ratio.

`HelloCube` renders continuously: it processes events without blocking, updates
the camera, renders, then delays the rest of a 16 ms frame budget. Movement and
the central cube's rotation are advanced by measured elapsed time with a 0.1 s
per-frame cap, so a stalled or throttled frame cannot make either jump. Diagonal
movement is normalized by the camera, and holding Shift applies a speed
multiplier. A minimized window skips rendering and input changes. If a
zero-sized drawable is acquired, the command buffer is submitted empty instead
of cancelled, because SDL only permits cancellation before swapchain
acquisition; the projection is never built from a zero height.

### HelloOBJ and HelloO3D

`HelloOBJ` and `HelloO3D` are interactive model viewers for the two format
parsers. Each loads a checked-in fixture by default or a path supplied on the
command line, and reports a clear error and status code `1` for a missing,
unreadable, or malformed file. The OBJ fixture is original project content: a
six-quad cube with positions, bottom-left-origin UVs, face normals, and six
`usemtl` labels. No MTL file is provided or loaded. The O3D fixture is a
synthetic, independently generated equivalent of the same cube; it is not an
OMSI game asset. [O3D.md](O3D.md) records its provenance and layout.

`ModelRenderer`, `ModelGpuGeometryBuilder`, `ModelViewerApp`, and the model shader
live in `examples/Shared`. `ModelRenderer` owns the model shader, interleaved
vertex buffer, 32-bit index buffer, pipeline, and depth target.
`ModelGpuGeometryBuilder` converts the parsed model to the example's
position/color vertex layout. It duplicates vertices per subset because the
shader carries one color per vertex; the six fixture labels map to six solid
colors and every other label uses a neutral color. That mapping is example
presentation only, not a material system. The shader is
`examples/Shared/Shaders/Model.metal`, embedded under
`Nymeria.Examples.Shaders.Model.metal`, with the same matrix convention and one
uniform buffer as the cube shader. `ModelViewerApp` owns the window, camera,
frame loop, and startup error handling. The format-specific `Program.cs` files
only handle arguments, parse the file, and report load errors.

The examples use the existing `Camera` and the shared `FlyCameraControls`. They
frame the camera from the parsed model's bounds so a user-supplied model of any
origin is visible, render continuously with the same 16 ms budget, and apply the
same depth, resize, minimize, zero-sized-drawable, and shutdown behavior as
`HelloCube`. They have no textures, lighting, animation, material properties, or
object hierarchy.

### HelloScene

`HelloScene` renders a `Nymeria.Core.Scenes.Scene` through the existing
position/color model shader and depth-tested pipeline. Its `SceneRenderer` owns
the device, shaders, pipeline, depth target, and a cache of one GPU mesh upload
per distinct `MeshData` reference. Mesh data is uploaded before the frame command
buffer is acquired, and each object supplies only its model matrix; repeated
instances therefore reuse the same vertex and index buffers.

The example builds a deterministic six-color cube as `MeshData` in code, so it
requires no OBJ, O3D, or OMSI asset. Four initial objects reference that one mesh
with different positions, quaternions, and non-uniform scales. `E` adds another
instance, `Q` removes the last object, and `R` restores the initial scene.
Removing every object leaves an empty scene, which still clears both targets.
Camera input remains the shared `FlyCameraControls`. The loop, frame budget,
minimize, resize, zero-sized-drawable, and shutdown paths follow the other
interactive examples. [SCENE.md](SCENE.md) documents the data contract and
ownership rules.

### Materials and textures

`Nymeria.Core.Materials` adds an immutable `Material` with a validated base-color
tint and an optional `Texture` reference. `Texture` is the shared abstract base
for validated CPU texture data with a mip chain: `TextureData` holds top-down
tightly packed RGBA pixels, and `CompressedTextureData` holds BC1–BC5/BC7 blocks
with an sRGB flag and no decompression. Materials and textures are managed CPU
data; they contain no GPU handle, SDL3, window, or format dependency.
`SceneObject` references a material without copying it and defaults to the shared,
untextured `Material.Default`. [MATERIALS.md](MATERIALS.md) is the authoritative
contract.

`Nymeria.Formats.Png.PngImageDecoder` is a dependency-free decoder for a bounded
PNG subset. It verifies the signature, chunk order, every chunk CRC, declared
lengths, dimensions, palette indices, scanline filters, and decompressed size;
rejects unknown critical chunks, 16-bit, interlaced, and malformed images; and
returns `TextureData` or a `PngFormatException`. It reads a caller-owned stream,
never opens a file, and enforces a 32 MiB encoded-input limit, a 4096-pixel
dimension limit, and a 64 MiB decoded-payload limit. Its headers and palette are
validated before allocation, and decompression is bounded to the exact scanline
payload implied by the validated header.

`Nymeria.Formats.Tga.TgaImageDecoder` handles uncompressed and RLE true-color
TGA with 24-bit BGR or 32-bit BGRA pixels, both origin flags, and explicit
rejection of color maps, interlacing, unsupported depths, and malformed RLE
packets. `Nymeria.Formats.Bmp.BmpImageDecoder` handles the documented
uncompressed Windows BMP subset: 24-bit BGR and 32-bit BGRA/BGRX, 8-bit indexed
palettes, standard 32-bit `BI_BITFIELDS` masks, both row orientations, and the
channel-mask rule for alpha. It validates the file and DIB headers, strides,
masks, palette, offsets, and exact payload before decoding.
`Nymeria.Formats.Dds.DdsTextureDecoder` validates DDS headers and the DX10
extension, then returns
`CompressedTextureData` for BC1–BC5/BC7 (including sRGB variants) or RGBA
`TextureData` for supported uncompressed layouts. None of these decoders
implements CPU block decompression or mipmap generation.

`Nymeria.Formats.Jpeg.JpegImageDecoder` uses the pure-managed `StbImageSharp`
package (Unlicense OR MIT, no native code, no transitive packages) for JPEG
decoding. Nymeria owns the safety boundary: it bounds the encoded bytes, walks the
JPEG segment chain to reject truncation before decoding, reads dimensions with
`ImageInfo` before allocating pixels, and forces opaque alpha because JPEG has no
alpha channel. EXIF orientation is not applied.

`Nymeria.Formats.Textures.TextureLoader` identifies PNG, JPEG, DDS, and BMP by
their signatures and TGA by a `.tga`/`.targa` source name plus header validation.
It never opens a file, resolves a path, caches data, or makes a GPU call.

`HelloMaterials`, `HelloLighting`, `HelloCulling`, `HelloLOD`,
`HelloStreaming`, and `HelloPicking` share
`examples/Shared/MaterialRenderer.cs` and
`examples/Shared/Shaders/Material.metal`. The renderer caches one GPU upload per
distinct `MeshData` and one GPU texture per distinct `Texture`, both by reference,
uploads mip chains once, selects a mipped sampler for chains, and binds a
generated white texture for materials without a texture or meshes without UVs.
It also caches each distinct LOD variant once and may draw any one of them per
instance. On an explicit unload path, `PruneUnusedResources` waits for the GPU to
become idle and releases cached meshes and textures that the current scene no
longer references, keeping anything shared with a remaining object. The shared
vertex layout is position, normal, and UV; the fragment shader multiplies sampled
RGB by the material tint and an optional lighting term, preserves the sampled
alpha, and applies the material transparency mode. Opaque and masked instances
share one depth-writing pipeline and are drawn in scene order, with a shader
`discard` for the mask cutoff. Blended instances use a second pipeline that keeps
the depth test but disables depth writes and enables straight source-over
blending; they are collected into a reused list, ordered back to front by
`Nymeria.Core.Rendering.BlendOrder`, and drawn after the opaque phase. Sorting is
per object by bounds center, so intersecting or surrounding instances cannot be
ordered per pixel. A steady frame allocates nothing new for this path.

`HelloMaterials` builds a UV-mapped cube and a generated four-quadrant
checkerboard in code, shares one checkerboard material and one mesh between two
objects, adds a blue-tinted variant of the same texture, and adds an untextured
solid material. It also demonstrates transparency: one masked pane uses a
generated checkerboard cutout texture, and three overlapping blended panes use
the loaded texture with cool and warm tints at different distances so a wrong
order or depth rule is visible. It leaves `Scene.Lighting` null, so the shared
renderer uses a white ambient term and no directional term: the output is the
same unlit base color as before lighting existed. A supplied PNG, JPEG, TGA, DDS, or BMP path is
decoded through the loader; a missing, unreadable, malformed, oversized, or
unsupported file prints a diagnostic and falls back to the generated checkerboard
while the example keeps running. A compressed DDS format the GPU cannot sample is
reported and also falls back, using `GpuDevice.SupportsTextureFormat` before a
frame is recorded.

The example uses the existing position/normal/UV layout, MSL shader embedding,
depth target, resize, minimize, frame budget, and shutdown rules. It is not a
material pipeline: it does not parse MTL, map O3D material names, stream or
reload textures, generate mipmaps, decompress BC blocks, or sort transparent
objects.

### Lighting

`Nymeria.Core.Lighting` adds immutable `AmbientLight`, `DirectionalLight`, and
`SceneLighting` data plus `LightingMath`. A scene optionally references one
`SceneLighting`; `null` means unlit. Light colors are finite values between 0
and 1, intensity is finite from 0 through 100, and directional directions are
rejected when zero or non-finite and normalized otherwise. [LIGHTING.md](LIGHTING.md)
is the authoritative contract.

The shared material shader evaluates ambient and Lambert diffuse terms in world
space:

```text
lit = baseColor * (ambientColor + directionalColor * max(0, dot(N, L)))
```

`L` is `DirectionalLight.DirectionToLight`, and `N` is the interpolated normal
transformed by the inverse transpose of the model matrix. The renderer computes
that matrix on the CPU, rejects singular or non-finite transforms with an object
diagnostic, and requires finite non-zero normals for every lit mesh. Normals are
not invented when a mesh lacks them. `HelloLighting` loads the checked-in OBJ
cube fixture, sets scene lighting, and allows the direction, intensity, and
ambient-only state to change at runtime. `HelloMaterials` leaves lighting unset
and remains unlit, demonstrating the compatibility path.

There is no specular term, PBR, shadow, point or spot light, multiple-light
collection, normal map, gamma-management change, deferred renderer, or shader
graph. The scene still owns no GPU resources.

### Culling

`MeshData.Bounds` is a model-space `BoundingBox` computed once during mesh
validation. `BoundingBox.Transform` produces world bounds from all eight
transformed corners, so rotation and non-uniform or negative scale are handled
correctly. `Frustum.Create` derives six normalized planes from the combined
view-projection matrix using the same right-handed, row-vector, `[0, 1]` depth
conventions as the shaders. The positive-vertex plane test culls an instance only
when it is fully outside one plane; intersecting, touching, and uncertain cases
remain visible. Degenerate or non-finite matrices produce an unbounded frustum,
and invalid bounds are treated as visible, rather than risking a false cull.

`SceneVisibilityCounter` performs the per-frame distance, frustum, and LOD test
and counts total, visible, frustum-culled, distance-culled, drawn, and per-level
instances. The shared material renderer exposes `FrustumCullingEnabled` (default
`false`), `LodEnabled` and `DistanceCullingEnabled` (default `true`), and
`LastFrameStatistics` (instance counts, draw calls, submitted triangles, and
per-level usage). Culling and LOD selection only skip draw submission or choose
one referenced variant: scene objects, shared meshes, materials, textures, and
their GPU uploads are unchanged.

The renderer checks distance culling before the six-plane frustum test, then
selects a LOD level only for instances that will be drawn. A configured
`MeshLod` supplies an ordered set of explicit meshes and uses the conservative
union of their model-space bounds for both distance measurement and visibility.
That union cannot change with the selected level, so a level switch never culls
previously visible geometry. Hysteresis state belongs to each `SceneObject`, not
to a shared configuration. There is no occlusion culling, spatial index,
hierarchy, GPU-driven culling, automatic mesh simplification, or cached
world-bounds invalidation. [CULLING.md](CULLING.md) and [LOD.md](LOD.md) document
the contracts, fallbacks, statistics, and limits.

### Streaming

`Nymeria.Core.Streaming` adds a small, headless synchronous chunk layer. A
`SceneChunkDefinition` has an ordinal-unique identifier, conservative
world-space bounds, a content callback that returns scene objects, and an
optional release callback for application-owned chunk-exclusive resources.
`SceneStreamer` owns the loaded/unloaded membership of those objects in one
`Scene`; it never owns the meshes, materials, textures, or GPU uploads.

`SceneStreamingConfiguration` requires a nonnegative inclusive load distance and
a greater finite unload distance. The gap is hysteresis: a loaded chunk is
unloaded only when its bounds are strictly beyond the unload distance. Both
distances are measured to chunk bounds, so a large chunk remains resident while
the focus is inside it. `Update` evicts first, then loads demanded chunks nearest
first, sorting equal distances by identifier. It completes at most
`MaxLoadsPerUpdate` synchronous load callbacks per update, so a teleport is
bounded in work but can still take several updates to fill the destination.

Chunk states are `Unloaded`, `Loaded`, and `Failed`; loading and unloading are
synchronous and have no observable intermediate state. A failed load removes any
partially added objects, records a bounded diagnostic, and is not retried until
the configured delay has elapsed. Once eligible, it retries when next demanded.
Release failures are recorded without retaining scene objects. Duplicate object references, null content,
content above the scene limit, duplicate chunk identifiers, non-finite focus or
bounds, negative distances, and invalid configurations are rejected.

`SceneStreamer` mutates the scene on the calling thread and must not run
concurrently with rendering, culling, or another scene mutation. Streamed
objects are ordinary `SceneObject` instances, so the existing frustum-culling and
LOD renderer paths continue to apply unchanged. Unloading removes objects but
does not reclaim managed CPU assets or GPU caches by itself. CPU assets remain
application-owned; the release callback handles chunk-exclusive disposal. GPU
caches are renderer-owned, and `MaterialRenderer.PruneUnusedResources` releases
unreferenced uploads after waiting for the GPU to become idle. [STREAMING.md](STREAMING.md)
is the authoritative contract, including the active-set versus reclaimed-memory
distinction, retry policy, diagnostics, measurements, and limits.

### Raycasting and picking

`Nymeria.Core.Raycasting` is a small headless query layer. A `Ray` stores a
finite world-space origin and a normalized direction, so its distance arguments
are world units. `RayIntersections.TryIntersectBox` tests the existing
`BoundingBox` with double-precision slab arithmetic; boundaries are inclusive,
exactly parallel axes are handled without an epsilon, and an origin inside the
box yields a zero-distance hit. `SceneRaycaster` scans the scene once and returns
the nearest `RaycastHit`, which carries the object reference, world-space point,
distance, and entry-face normal. Equal distances resolve to the earlier object in
insertion order. There is no `RaycastAll`, BVH, octree, or generalized spatial
query framework.

Picking uses each object's current world transform and the union of its LOD
variant bounds when a `MeshLod` is configured. It never uses local bounds alone,
the selected LOD mesh, the frustum, or distance-culling state as a pickability
proxy. `SceneObject.IsPickable` is a logical interaction flag that defaults to
true and does not affect rendering. Unloaded streamed objects are absent from
the scene, so they cannot produce hits; a stored `RaycastHit` reference must be
validated with `Scene.Contains` before use.

`ViewportRay.TryCreate` in the same namespace converts a viewport pixel to a
world ray through an existing view and projection matrix. It consumes the
project's actual `System.Numerics` row-vector, right-handed, `[0, 1]` depth
conventions rather than an OpenGL unprojection formula, uses the top-left pixel
space the examples read from SDL, and returns false for zero-sized viewports or
non-invertible matrices. Input mapping and logical-to-drawable pixel scaling
remain in the application. [PICKING.md](PICKING.md) is the authoritative
contract.

### Collision detection

`Nymeria.Core.Collision` is a small headless overlap-query layer, not a physics
engine. `BoundingSphere` joins the existing validated `BoundingBox`, and
`CollisionShape` holds exactly one box or sphere. `CollisionIntersections`
implements inclusive AABB–AABB, sphere–sphere, and AABB–sphere tests in double
precision. Contact counts as overlap, and a default empty shape never overlaps.

A scene object opts in with an optional local-space `Collider`. Queries transform
it with the object's current model matrix: boxes use the eight-corner world AABB,
and spheres use the largest axis scale as a conservative radius. Non-finite and
projective matrices are rejected; sphere conversion also rejects shear rather
than under-reporting extent. `SceneCollision` answers overlap against one object
or appends all unordered overlapping pairs. It reads current scene membership in
insertion order, so unloaded chunks cannot be retained, self-pairs and duplicate
pairs are impossible, and results are deterministic for fixed scene state.
Collision ignores frustum culling, distance culling, LOD selection, and
`IsPickable`; off-screen active objects still collide. The pair query is the
deliberate O(n²) baseline, with no BVH, octree, spatial hash, cached world bounds,
parallel work, or collision response. [COLLISION.md](COLLISION.md) is the
authoritative contract.

### Ownership and lifetime

- `GpuDevice` owns the GPU device and window claim. Each renderer owns its
  shaders, geometry buffers, textures, samplers, pipeline, and depth texture. A
  renderer must be created after the window and disposed before it.
- `Scene` and `SceneObject` own no GPU resources. `SceneObject.Mesh` is a shared
  reference to managed `MeshData`, `SceneObject.Lod` is an optional shared
  reference to immutable LOD data, and `SceneObject.Material` is a shared
  reference to managed `Material` and its `Texture`. `Scene.Lighting` is an
  optional reference to immutable CPU lighting data. The scene renderer owns one
  upload per distinct mesh; the shared material renderer owns one upload per
  distinct mesh and texture, including every distinct LOD variant. Each cached
  upload is released exactly once. Removing an object never disposes a mesh,
  material, or texture that other objects may still reference.
- A texture reference is not ownership. The code that constructs `TextureData`
  or `CompressedTextureData` owns the memory and must not mutate it. The renderer
  that calls `GpuDevice.CreateTexture` owns the GPU texture and releases it after
  `SDL_WaitForGPUIdle`. It releases only the GPU handle, never the managed
  texture data.
- Each `Dispose` waits for the GPU to become idle, releases its resources, and
  then disposes the `GpuDevice`, which releases the window claim and destroys the
  device. Initialization failures release whatever was created in reverse order
  and then rethrow.
- `Window` keeps ownership of the native window and calls `SDL_Quit` after the
  renderer has released its claim.
- `HelloCube`, `HelloOBJ`, `HelloO3D`, `HelloX`, `HelloScene`, `HelloMaterials`,
  `HelloLighting`, `HelloCulling`, `HelloCollision`, `HelloLOD`,
  `HelloStreaming`, `HelloPicking`, `HelloAudio`, and `HelloAnimation` own their
  `Camera` instances and share the `FlyCameraControls` implementation.
  `HelloBenchmark` owns a fixed `Camera` and does not use input controls. The
  camera has no unmanaged resources. The input mapping disables relative mouse
  mode and discards pending mouse motion during disposal, before the window is
  destroyed.
- A `SceneStreamer` owns only the scene membership of loaded chunks. It removes
  those objects on unload and drops its content references; it never disposes a
  shared mesh, material, or texture. The optional release callback runs after
  removal for chunk-exclusive application resources. GPU cache entries are
  released by the renderer, either on disposal or through
  `PruneUnusedResources`, which waits for GPU idle and keeps any resource still
  referenced by the scene.
- `AudioSystem` owns exactly one SDL audio stream and closes the device when
  the stream is destroyed. The managed mixer owns the fixed voice slots and the
  per-clip voice reference counts. A clip owns managed sample memory only; a
  voice holds a reference to its clip while it can still play. `AudioClip.Dispose`
  is refused while a playing or paused voice references it, and disposing the
  system stops and disposes every voice before the stream is destroyed. Voices,
  the system, and clips are released in that order; the system must be disposed
  before the window because window disposal shuts SDL down.
- Audio is not part of the scene or streaming ownership graph. A `SceneStreamer`
  never disposes or stops audio. An application's chunk release callback stops
  and disposes the voices it owns and disposes clips that no other chunk shares;
  a still-shared clip is protected by the reference count.
- `TransformAnimationClip` and its tracks own immutable managed keyframe arrays.
  An `AnimationPlayer` owns only its playback state and a reference to one target
  transform; it has no native handle and no `Dispose`. The player writes ordinary
  `Transform` properties and never releases or mutates meshes, materials,
  textures, or GPU resources. Removing a scene object does not invalidate the
  player; the application stops and drops players when it unloads their objects.
- No renderer creates per-frame GPU resources. The scene and material renderers
  upload newly referenced meshes and textures once and reuse them; command
  buffers and swapchain textures are owned and recycled by SDL. Pruning is an
  occasional unload-path operation, not part of the per-frame draw loop.

Limitations in this version:

- GPU rendering is macOS-only. It requires SDL's Metal driver and MSL shader
  source; Windows and Linux will need their own shader formats and native
  packages, which are not implemented.
- Apple Silicon and Intel Macs both use Metal. The macOS native package contains
  both architectures.
- Audio playback is verified on macOS through CoreAudio. Windows and Linux audio
  is not verified because the repository ships no native SDL3 package for those
  platforms. The headless audio projects still build and their tests run on all
  three CI platforms.
- Spatial audio is basic stereo: linear distance attenuation, left/right
  equal-power panning, and a global positional stereo strength (default full).
  There is no HRTF, binaural filter, reverb, occlusion, Doppler, propagation
  delay, surround or height output, physics-driven acoustics, per-voice stereo
  override, mono mixing mode, or stereo-file width control.
- Audio supports uncompressed mono/stereo WAV only, decoded fully into memory and
  resampled linearly to the mixer rate. There is no compressed format, sample
  streaming, audio capture, microphone input, effects chain, or audio graph.
  Device loss stops all voices and requires explicit recreation; there is no
  automatic device reopen.
- Transform animation is keyframe-based and per-object. There is no skeletal
  animation, bone hierarchy, blending, additive layer, state machine, animation
  graph, reverse playback, animation event, morph target, or animation import.
  Multiple players on one transform resolve by update order, and pivots must come
  from the authored mesh origin because the scene has no hierarchy.
- There is no scene hierarchy, ECS, serialization, editor, shadow, physics
  simulation, or collision response, and no engine-level input system. The scene
  layer holds flat objects only. Collision is limited to box and sphere overlap
  queries. `Camera` is reusable math, but input mapping and timing stay in the
  examples; the triangle remains static clip-space geometry.
- Culling is one conservative axis-aligned distance test plus one six-plane
  frustum test per instance. LOD chooses one explicit application-supplied mesh
  per object from a conservative union of its variants. There is no per-subset
  or occlusion culling, no spatial index, no cached world-bounds invalidation,
  and no GPU timing. A degenerate projection disables frustum culling for that
  frame instead of risking a false cull. Nymeria does not generate, simplify, or
  cross-fade meshes; distance culling is optional and applies only to objects
  with a configured maximum.
- Picking is a bounds-only, single linear scene scan. There is no triangle or
  per-subset hit, sphere or capsule bound, spatial index, cached world bounds,
  `RaycastAll`, interaction layer, or GPU color-ID picking. Rotated and
  non-uniformly scaled objects use conservative world AABBs, so a hit point can
  lie outside the mesh surface. Camera unprojection assumes a perspective
  projection. A stored hit does not keep a streamed object resident.
- Collision is a bounding-volume overlap query, not physics. There is no
  triangle-mesh collider, capsule, compound shape, contact manifold, penetration
  depth, response, rigid body, force, gravity, trigger, event, spatial index, or
  continuous collision detection. Sphere colliders under non-uniform scale use a
  conservative largest-axis radius rather than an ellipsoid. The scene pair
  query is O(n²), with no broad phase or cached world bounds.
- Streaming is a synchronous, single-threaded chunk membership baseline. Chunk
  content callbacks run on the update thread, so a large or slow loader can stall
  that frame; `MaxLoadsPerUpdate` bounds the number of callbacks but not their
  individual cost. There is no background loading, asynchronous upload, file
  watcher, streaming service, terrain system, or automatic asset dependency
  discovery. Active chunks are not proof that CPU or GPU memory was reclaimed:
  CPU assets are application-owned and GPU caches are released only by an
  explicit renderer prune or renderer disposal. The streamer does not resolve
  paths or parse formats; its content callback is application code and owns those
  trust boundaries.
- Lighting is one ambient term plus at most one directional Lambert term. There
  is no specular or PBR shading, multiple-light collection, point or spot light,
  shadow, normal mapping, deferred rendering, global illumination, or shader
  graph. Meshes without normals are rejected by the lit material renderer rather
  than receiving invented normals.
- Materials are a base-color tint plus an optional base-color texture and a
  transparency mode. There is no MTL or OMSI material parsing, texture streaming
  or reload, mipmap generation, CPU block decompression, or animation, and no
  order-independent or per-triangle transparency: blending is object-level,
  back-to-front, by bounds center. `HelloMaterials` accepts one explicit texture
  path and does not search for textures relative to a model.
- Texture formats are PNG, JPEG, TGA, BMP, and a documented DDS subset. DDS
  support is not universal: BC6H, ASTC, ETC, cubemaps, volumes, texture arrays,
  3D textures, padded row pitches, and uncompressed sRGB DDS layouts are rejected
  explicitly. BMP support is a documented subset: RLE, 1/4/16-bit, OS/2 headers,
  embedded JPEG/PNG streams, and non-standard masks are rejected. Compressed
  upload requires the backend to support the format, and only a block-aligned
  prefix of a compressed mip chain is uploaded.
- The Wavefront OBJ parser handles a documented polygonal subset only. It does
  not load MTL materials or textures, tessellate concave or non-planar polygons,
  or import lines, points, or free-form geometry. `HelloCube` still constructs
  its cube as model data in code.
- The O3D parser handles unscrambled static meshes of versions 1 through 7 only.
  It rejects scrambled vertex keys, bones, unknown sections, and unknown flag
  bits. It preserves material names but does not render materials or textures,
  and it does not apply the pivot matrix. Its tests and fixture are synthetic;
  ordinary stock OMSI content has not been validated. [O3D.md](O3D.md) lists the
  exact matrix and uncertainties.
- The text DirectX `.x` parser handles a documented static-mesh subset only. It
  rejects binary and compressed files, skinning, animation, material effects,
  and unknown objects; it bakes frame transforms, preserves
  only material names, and fan-triangulates polygons without concavity tests.
  Its fixture and tests are synthetic, so broad exporter compatibility is not
  claimed. [DIRECTX_X.md](DIRECTX_X.md) lists the exact matrix and limitations.
- GPU output and interactive input are not validated by CI.
  [SMOKE_TEST.md](SMOKE_TEST.md) covers them.

## Dependency direction

The following principle guides project references. The audio, core, formats,
graphics, and platform projects and the examples exist today.

```text
examples  -----------------------> engine projects
   |
   v
Nymeria.Graphics   Nymeria.Platform   Nymeria.Formats.* (Obj, O3d, DirectX, Png, Tga, Jpeg, Dds, Bmp, Wav)
        \                /                                |
         v              v                                 v
                            Nymeria.Core

Nymeria.Formats.Textures  ->  Nymeria.Core + Png + Jpeg + Tga + Dds + Bmp
Nymeria.Audio             ->  Nymeria.Core + Nymeria.Formats.Wav + SDL3-CS
```

- `Nymeria.Core` must not depend on graphics, platform, or format code.
- Every `Nymeria.Formats.*` decoder depends on `Nymeria.Core` and must not depend
  on graphics or platform code. `Nymeria.Formats.Textures` additionally depends
  on the five image decoders.
- `Nymeria.Graphics` and `Nymeria.Platform` may depend on `Nymeria.Core`.
- `Nymeria.Audio` depends on `Nymeria.Core`, `Nymeria.Formats.Wav`, and the
  `SDL3-CS` managed binding. It must not depend on `Nymeria.Graphics` or
  `Nymeria.Platform`, so audio does not require a window, GPU, or rendering.
- `Nymeria.Graphics` and `Nymeria.Platform` must not depend on each other
  directly unless the maintainer approves and this document records the reason.
- `examples/` may depend on all engine projects.
- `tests/` may reference the project under test.

`Nymeria.Core` has no project references; it uses `System.Numerics` and the base
class library only. Its scene types reference `MeshData`, `Material`, and
`Texture` but no GPU, SDL3, or format code. `Nymeria.Formats.Obj`,
`Nymeria.Formats.O3d`, `Nymeria.Formats.DirectX`, `Nymeria.Formats.Png`,
`Nymeria.Formats.Tga`, `Nymeria.Formats.Dds`, and `Nymeria.Formats.Bmp`
reference `Nymeria.Core` only. `Nymeria.Formats.Jpeg` references
`Nymeria.Core` and `StbImageSharp`; `Nymeria.Formats.Textures` references
`Nymeria.Core` and the five decoders; `Nymeria.Formats.Wav` references
`Nymeria.Core` only.
`Nymeria.Audio` references `Nymeria.Core`, `Nymeria.Formats.Wav`, and
`SDL3-CS`. The native SDL3 runtime still comes from `Nymeria.Platform` in an
application, so `Nymeria.Audio` adds no second native package and no window or
GPU dependency. `Nymeria.Graphics` and `Nymeria.Platform` have no
`Nymeria.Core` reference; culling math lives in `Nymeria.Core.Culling`, so the
renderer in the example composes the dependency instead of adding a
`Nymeria.Graphics` reference. They do not reference each other: each example
creates the window and passes `Window.Handle` to its renderer, which creates a
`GpuDevice` in `Nymeria.Graphics`; the dependency remains in the example.
`Camera` sits in `Nymeria.Graphics` and references only `System.Numerics`.
`HelloCube` references
`Nymeria.Core` for its cube model data; `HelloOBJ` references `Nymeria.Core`,
`Nymeria.Formats.Obj`, `Nymeria.Graphics`, and `Nymeria.Platform`; `HelloO3D`
references `Nymeria.Core`, `Nymeria.Formats.O3d`, `Nymeria.Graphics`, and
`Nymeria.Platform`; `HelloX` references `Nymeria.Core`,
`Nymeria.Formats.DirectX`, `Nymeria.Graphics`, and `Nymeria.Platform`;
`HelloScene` references `Nymeria.Core` for scene and mesh
data, `Nymeria.Graphics` for the device and camera, and `Nymeria.Platform` for
the window; `HelloMaterials` references `Nymeria.Core` for materials and mesh
data, `Nymeria.Formats.Textures` for texture decoding, `Nymeria.Graphics` for the
device and camera, and `Nymeria.Platform` for the window; `HelloLighting`
references `Nymeria.Core` for scene, lighting, and material data,
`Nymeria.Formats.Obj` for its mesh, `Nymeria.Formats.Textures` for its texture,
`Nymeria.Graphics`, and `Nymeria.Platform`; `HelloCulling` references
`Nymeria.Core` for scene, mesh, material, and culling data,
`Nymeria.Graphics` for the device and camera, and `Nymeria.Platform` for the
window; `HelloLOD` references `Nymeria.Core` for scene, LOD, mesh, and
material data, `Nymeria.Graphics` for the device and camera, and
`Nymeria.Platform` for the window; `HelloStreaming` references `Nymeria.Core`
for scene, streaming, mesh, and material data, `Nymeria.Graphics` for the device
and camera, and `Nymeria.Platform` for the window; `HelloPicking` references `Nymeria.Core`
for scene, streaming, picking, mesh, and material data, `Nymeria.Graphics` for
the device and camera, and `Nymeria.Platform` for the window; `HelloCollision`
references `Nymeria.Core` for scene, collision, mesh, and material data,
`Nymeria.Graphics` for the device and camera, and `Nymeria.Platform` for the
window; `HelloAudio`
references `Nymeria.Audio` for playback, `Nymeria.Core` for scene, camera-math,
and audio-math data, `Nymeria.Graphics` for the device and camera, and
`Nymeria.Platform` for the window; `HelloAnimation` references `Nymeria.Core`
for scene, animation, and material data, `Nymeria.Graphics` for the device and
camera, and `Nymeria.Platform` for the window; `HelloBenchmark`
references `Nymeria.Core` for scene, LOD, mesh, material, and timing data,
`Nymeria.Graphics` for the device and camera, and `Nymeria.Platform` for the
window; `HelloTriangle` does not need the core or formats projects.
No engine project references an example. `Nymeria.Core.Tests`,
`Nymeria.Formats.Obj.Tests`, `Nymeria.Formats.O3d.Tests`,
`Nymeria.Formats.DirectX.Tests`, `Nymeria.Formats.Png.Tests`,
`Nymeria.Formats.Tga.Tests`, `Nymeria.Formats.Jpeg.Tests`,
`Nymeria.Formats.Dds.Tests`, `Nymeria.Formats.Bmp.Tests`,
`Nymeria.Formats.Textures.Tests`, `Nymeria.Formats.Wav.Tests`,
`Nymeria.Audio.Tests`, `Nymeria.Platform.Tests`, and `Nymeria.Graphics.Tests`
reference their project or projects under test; the animation tests live in
`Nymeria.Core.Tests`. No
interfaces or abstractions are created to enforce the future direction. Adding
them speculatively would violate YAGNI.

## Technical choices

The following choices are implemented:

- **.NET SDK and target framework:** .NET 10, targeting `net10.0`. `global.json`
  pins SDK 10.0.402 with `latestPatch` roll-forward and no prerelease SDKs.
- **Solution format:** `Nymeria.slnx`, built with the `dotnet` CLI.
- **Windowing:** SDL3 3.4.18 through `SDL3-CS` 3.4.18 and the macOS native
  package `SDL3-CS.MacOS` 3.4.18.
- **GPU rendering:** SDL3's GPU API with the Metal backend and MSL shader source,
  reached through `SDL3-CS`. `Nymeria.Graphics` provides the reusable
  `GpuDevice`, including buffer, RGBA and block-compressed texture, sampler, and
  shader creation; the
  examples embed their own `.metal` files; `HelloOBJ` and `HelloO3D` share
  `examples/Shared/Shaders/Model.metal`, and `HelloMaterials` and `HelloLighting`
  share `examples/Shared/Shaders/Material.metal`, compiled by SDL at runtime. No
  direct Metal interop or shader compiler is used.
- **Math:** `System.Numerics` vectors, quaternions, and matrices, with no custom
  math library. `Camera` uses it for yaw/pitch view and projection math.
  `Transform` uses it for normalized quaternion rotation and row-vector
  `scale * rotation * translation` composition. `HelloCube` and `HelloScene`
  upload row-major bytes and let MSL interpret them as column-major.
- **Model data:** `Nymeria.Core` contains validated, format-independent
  `ModelData`/`MeshData` types and the `IModelParser` stream contract. It uses
  `System.Numerics` and no external package, SDL3, or GPU dependency.
- **Scene data:** `Nymeria.Core` contains `Scene`, `SceneObject`, and `Transform`
  in `Nymeria.Core.Scenes`. Objects reference shared `MeshData`; the scene owns
  no GPU resources. Finite transform validation, a 100,000-object default limit,
  deterministic ordering, and optional scene lighting are documented in
  [SCENE.md](SCENE.md).
- **Lighting data:** `Nymeria.Core.Lighting` contains validated ambient and
  directional light data plus CPU Lambert and normal-matrix reference math. A
  scene references one optional `SceneLighting`; null means unlit. The lighting
  types depend only on `System.Numerics` and contain no SDL3 or GPU state.
  [LIGHTING.md](LIGHTING.md) is the authoritative contract.
- **Culling:** `MeshData.Bounds` is a validated model-space AABB. `Nymeria.Core.Culling`
  derives six normalized planes from the view-projection matrix, tests them
  conservatively against transformed AABBs, and counts per-frame visibility. The
  shared material renderer opts in with `FrustumCullingEnabled` and exposes
  instance and draw-call statistics. Invalid data fails open rather than
  discarding geometry. [CULLING.md](CULLING.md) is the authoritative contract.
- **Level of detail:** `Nymeria.Core.Lod` validates an optional ordered list of
  explicit mesh variants per `SceneObject`, measures camera distance to the
  conservative union of their world bounds, selects one variant per frame with a
  per-object hysteresis state, and can skip an instance beyond an optional
  maximum distance. Selection state is headless and allocation-free per frame;
  the renderer uploads each distinct variant once and shares it by reference.
  Nymeria does not simplify meshes. [LOD.md](LOD.md) is the authoritative
  contract.
- **Streaming:** `Nymeria.Core.Streaming` validates chunk identifiers and bounds,
  selects demanded chunks by point-to-bounds distance, enforces a greater unload
  distance as hysteresis, bounds loads per update, retries failures after a
  delay, rolls back partial content, and counts state and work. It is
  synchronous and headless; scene mutation stays on the caller's thread. It
  owns scene membership only, not CPU assets or GPU resources.
  [STREAMING.md](STREAMING.md) is the authoritative contract.
- **Picking:** `Nymeria.Core.Raycasting` contains a normalized world-space ray,
  nearest-hit AABB intersection, a nearest-hit scene query, and a
  matrix-based viewport unprojection helper. It is headless and uses
  `System.Numerics` only. World bounds come from the existing model-space
  `BoundingBox`, including LOD union bounds, and logical pickability is
  independent of frustum culling and selected LOD state. [PICKING.md](PICKING.md)
  is the authoritative contract.
- **Collision:** `Nymeria.Core.Collision` contains validated box and sphere
  primitives, inclusive overlap tests, conservative world-space conversion, an
  optional local `SceneObject.Collider`, and deterministic unordered-pair and
  per-object scene queries. It is headless, allocation-free inside the query
  loops, and independent of rendering visibility and LOD. It is detection only:
  there is no response or physics solver. [COLLISION.md](COLLISION.md) is the
  authoritative contract.
- **Audio data and spatial math:** `Nymeria.Core.Audio` contains validated
  interleaved `PcmAudioData`, bounded linear resampling, an `AudioListener` with
  camera-matching axes, and `AudioMath` distance attenuation, stereo panning,
  configurable positional stereo strength, and equal-power gains. It is
  headless, uses `System.Numerics` only, and contains no SDL3, device, or GPU
  state. [AUDIO.md](AUDIO.md) is the authoritative contract.
- **WAV parsing:** `Nymeria.Formats.Wav` is a dependency-free decoder for mono
  or stereo 8/16/24/32-bit PCM and 32-bit float RIFF/WAVE input. It validates
  RIFF and chunk sizes, ordering, padding, format fields, frame alignment, and
  input, frame, and decoded-payload limits before allocating. [AUDIO.md](AUDIO.md)
  documents the subset and limits.
- **Transform animation:** `Nymeria.Core.Animation` stores immutable,
  time-ordered translation, rotation, and scale tracks with validated keyframes.
  Clips sample any subset of channels at an explicit time; vectors interpolate
  linearly and quaternions use shortest-path spherical interpolation. The
  `AnimationPlayer` controller advances from explicit frame deltas, supports
  play/pause/resume/stop/restart, looping, completion, and a nonnegative speed,
  and writes ordinary `SceneObject.Transform` values. It is deterministic,
  headless, and independent of rendering visibility. [ANIMATION.md](ANIMATION.md)
  is the authoritative contract.
- **Audio playback:** `Nymeria.Audio` owns one SDL3 playback stream and a bounded
  managed float32 stereo mixer. It supports shared clips, overlapping 2D and
  spatial voices, pause/resume/stop, looping, per-source and master gain, linear
  distance attenuation, stereo panning, and a runtime-adjustable positional
  stereo effect strength. Mixing runs in SDL's callback with a preallocated
  scratch buffer and one short lock; Nymeria's mixer allocates nothing per block
  after warm-up and performs no I/O. Device creation is explicit and never
  happens merely by loading the assembly. [AUDIO.md](AUDIO.md) is the
  authoritative contract.
- **Performance diagnostics:** `Nymeria.Core.Diagnostics.FrameTimingWindow` is a
  bounded, warm-up-aware rolling window for CPU frame durations. It rejects
  invalid samples, reports average, nearest-rank p95, maximum, and FPS from the
  average, and its `AddSample` path does not allocate; `GetSummary` allocates a
  copy for sorting and is intended for reporting intervals only. It does not
  collect GPU timings or run in the background. `examples/HelloBenchmark` is the
  reproducible workload; `HelloLOD` and `HelloStreaming` also report rolling
  render and streaming-time averages and p95. [PERFORMANCE.md](PERFORMANCE.md)
  defines the phases, method, and limits.
- **Model parsing:** `Nymeria.Formats.Obj`, `Nymeria.Formats.O3d`, and
  `Nymeria.Formats.DirectX` are dependency-free parsers in isolated format
  projects. They use the base class library and `Nymeria.Core` only, implement
  `IModelParser`, and document their supported subsets and limitations in
  [OBJ.md](OBJ.md), [O3D.md](O3D.md), and [DIRECTX_X.md](DIRECTX_X.md). No
  third-party OBJ, O3D, or `.x` loader is used.
- **Material data:** `Nymeria.Core.Materials` contains the shared base-color
  tint, optional `Texture` reference, RGBA texture data with mip chains, and
  block-compressed texture data for BC1–BC5/BC7. Materials, textures, and scene
  objects own no GPU handle or SDL3 state.
- **Texture parsing:** `Nymeria.Formats.Png`, `Nymeria.Formats.Tga`,
  `Nymeria.Formats.Dds`, and `Nymeria.Formats.Bmp` are dependency-free decoders
  that use only the base class library and `Nymeria.Core`;
  `Nymeria.Formats.Jpeg` adds the pure-managed `StbImageSharp` package;
  `Nymeria.Formats.Textures` dispatches by signature. Every decoder enforces the
  input, dimension, mip, and payload limits documented in
  [MATERIALS.md](MATERIALS.md).
- **Texture upload:** `GpuDevice` supports RGBA8 and BC1–BC7 mip chains with a
  capability check, 512-byte level offsets, and one copy pass per texture. The
  shared material renderer owns two samplers so single-level textures never
  request absent levels.
- **Surface shading:** the shared material shader evaluates ambient and Lambert
  diffuse light in world space. The CPU computes each object's inverse-transpose
  normal matrix and rejects degenerate transforms; normals are required and
  normalized before upload. `HelloMaterials` leaves lighting unset and remains
  unlit; `HelloLighting` opts in. See [LIGHTING.md](LIGHTING.md).
- **External packages:** the SDL3 binding and macOS native runtime (also used
  for audio playback), the pure-managed `StbImageSharp` 2.30.16 JPEG decoder
  (Unlicense OR MIT, no transitive packages), plus the xUnit test packages
  `xunit` 2.9.3, `xunit.runner.visualstudio` 4.0.1, and
  `Microsoft.NET.Test.Sdk` 18.11.0. The headless tests cover
  platform-independent `WindowOptions` behavior, model-data validation and
  parser error semantics, transform composition, scene lifecycle and mesh
  sharing, lighting data and math, mesh bounds and frustum culling, visibility
  counts, bounded frame-timing aggregation, material and texture data, the OBJ,
  O3D, and text `.x` subsets, the PNG, JPEG, TGA, BMP, DDS, loader, and WAV
  paths, and the audio math, mixer, voice lifecycle, clip ownership, concurrency,
  and allocation checks; they do not launch a GUI, create a GPU device, or open
  an audio device.

The following choices remain open:

- **Additional platforms and backends:** Windows and Linux rendering, their
  native SDL3 packages, and the shader formats those backends require are not
  implemented. Audio playback uses SDL3's portable API but has only been verified
  on macOS; Windows and Linux audio device testing remains open.
- **Further O3D features:** scrambled vertices, bones, animation, and material
  rendering are not implemented and require a separate decision.

## KISS and YAGNI

Nymeria follows KISS (Keep It Simple) and YAGNI (You Aren't Gonna Need It):

- Prefer the simplest straightforward solution that satisfies a demonstrated
  need.
- Do not add layers, interfaces, configuration, packages, or projects in
  anticipation of a future requirement.
- Explain non-obvious decisions in code comments or documentation, including the
  trade-off that was accepted.
- Add abstractions only when a second concrete use case justifies them.
- Delete or avoid speculative scaffolding rather than carrying it indefinitely.

## Repository automation

### Repository checks

`.github/workflows/repository-checks.yml` runs on pushes and pull requests to
`main` with two jobs:

- **Repository checks** on `ubuntu-latest` verifies required files and
  directories, ensures no `bin/` or `obj/` output is tracked, resolves relative
  Markdown links, and checks that Dependabot covers the tracked GitHub Actions
  and NuGet ecosystems.
- **.NET** runs a `macos-latest`, `windows-latest`, and `ubuntu-latest` matrix.
  Each runner restores `Nymeria.slnx`, builds it in Release configuration, and
  runs the headless unit tests. No GUI is launched in CI.

Both jobs use read-only repository permissions. The workflow is named
`Repository checks`, so Dependabot auto-merge waits for every matrix leg and the
foundation job. macOS window, GPU, and audio-device example behavior is covered
by [SMOKE_TEST.md](SMOKE_TEST.md) instead of a CI claim.

Results appear on commits and pull requests, and in the repository's **Actions**
tab. The workflow can also be inspected with `gh run list` and
`gh run view <run-id>`.

Branch protection or GitHub rulesets are not enabled. Enabling them may conflict
with the policy of pushing small verified commits immediately, so it requires
maintainer approval. See [GIT_GUIDELINES.md](GIT_GUIDELINES.md).

### Dependabot

`.github/dependabot.yml` checks the `github-actions` and `nuget` ecosystems at
`/` weekly. Dependabot opens pull requests for available updates. It does not
deploy anything.

`.github/workflows/dependabot-auto-merge.yml` squash-merges a Dependabot pull
request only when all of the following are true:

- The `Repository checks` workflow completed successfully for a `pull_request`
  event.
- The pull request author is `dependabot[bot]`.
- The pull request is open, unmerged, and targets the repository's default
  branch.
- The pull request comes from this repository.
- The current head commit still matches the commit that passed
  `Repository checks`.

The workflow uses the read/write `GITHUB_TOKEN` only for the merge and pins the
merge to the verified head commit. It uses no repository secrets and does not
deploy.
Human-authored pull requests are not auto-merged.

The organization currently prevents GitHub Actions from creating or approving
pull requests. As a result, the workflow does not submit an approval review; it
merges after the checks pass. Auto-approval would require an organization policy
change or a separate credential, and must not be added without maintainer
approval.

Because the merge uses `GITHUB_TOKEN`, GitHub suppresses the resulting push-event
workflows. The same content was already validated by the pull request's
`Repository checks` run. Automatic merging trusts Dependabot's proposed dependency
updates. For NuGet updates, the checks include the Release build and unit tests;
they do not prove that a native window still behaves correctly.

Branch protection or GitHub rulesets are not required for this auto-merge path
and are still not enabled. Enabling them is a separate workflow-policy decision
that requires maintainer approval. See [GIT_GUIDELINES.md](GIT_GUIDELINES.md).

The maintainer chose to let the existing auto-merge workflow apply to both
Dependabot ecosystems. No additional auto-merge rule or credential exists.

## Governance

The human maintainer owns architecture and scope. Changes to architecture,
dependencies, repository structure, or workflow policy require explicit
maintainer approval. Approved changes must update this document in the same
change. When implementation and documentation disagree, the documentation is
the baseline to discuss, not silently override.
