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
  format-independent model data and the `IModelParser` contract.
- `Nymeria.Formats.Obj`, a dependency-free Wavefront OBJ parser implementing
  `IModelParser` for a documented polygonal subset.
- `Nymeria.Formats.O3d`, a dependency-free OMSI O3D parser implementing
  `IModelParser` for a documented unscrambled static-mesh subset.
- `Nymeria.Graphics`, a class library that owns the SDL3 GPU device and window
  presentation claim, provides reusable MSL shader and buffer creation, and
  contains a reusable, SDL-independent free-fly `Camera`.
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
- `Nymeria.Platform.Tests`, automated tests for platform-independent options.
- `Nymeria.Core.Tests`, GPU-independent tests for model-data validation and
  parser-contract error behavior.
- `Nymeria.Formats.Obj.Tests`, GPU-independent tests for the OBJ subset,
  conversions, and diagnostics.
- `Nymeria.Formats.O3d.Tests`, GPU-independent tests for the O3D header,
  supported versions and flags, geometry conversion, limits, and failure
  behavior.
- SDL3 windowing and GPU rendering through the pinned `SDL3-CS` and
  `SDL3-CS.MacOS` packages.
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
| `src/core/` | Platform-independent engine fundamentals in `Nymeria.Core`. |
| `src/formats/` | Format-specific model parsers in isolated projects such as `Nymeria.Formats.Obj`. |
| `src/graphics/` | `Nymeria.Graphics`: reusable SDL3 GPU device and resource infrastructure. |
| `src/platform/` | `Nymeria.Platform`: windowing, input, and operating-system integration. |
| `examples/` | Small runnable programs that demonstrate engine usage. |
| `tests/` | Automated unit and integration tests for the engine projects. |
| `docs/` | Authoritative project and architecture decisions. |
| `.github/` | Repository automation: checks and dependency updates. |

`src/platform/` contains the `Nymeria.Platform` project, `src/graphics/`
contains `Nymeria.Graphics`, `src/core/` contains `Nymeria.Core`, `src/formats/`
contains `Nymeria.Formats.Obj` and `Nymeria.Formats.O3d`, `examples/` contains
`HelloTriangle`, `HelloCube`, `HelloOBJ`, and `HelloO3D`, and `tests/` contains
`Nymeria.Core.Tests`, `Nymeria.Formats.Obj.Tests`,
`Nymeria.Formats.O3d.Tests`, and `Nymeria.Platform.Tests`.

`Nymeria.Core` owns the format-independent model data and parser contract
described in [MODEL_DATA.md](MODEL_DATA.md). It references `System.Numerics` and
the base class library only. The camera lives in `Nymeria.Graphics` rather than
in `Nymeria.Core`: it is reusable 3D rendering math with no SDL, window, or GPU
dependency, and the model-loading foundation is the first demonstrated need for
the core project. `Nymeria.Formats.Obj` and `Nymeria.Formats.O3d` reference only
`Nymeria.Core` and the base class library; their supported subsets are documented
in [OBJ.md](OBJ.md) and [O3D.md](O3D.md).

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

The conventions are right-handed with `+Y` up and `-Z` forward, triangle-list
winding counter-clockwise when viewed from outside, zero-based indices, and
texture coordinates with a top-left origin and `+V` down. Parsers convert source
handedness, winding, index bases, and UV origin. [MODEL_DATA.md](MODEL_DATA.md)
documents the complete contract, validation rules, and the extension point for
format parsers. The cube example builds its geometry as model data and converts
it to the existing shader layout; `Nymeria.Core` itself never creates GPU
resources.

## GPU rendering

`Nymeria.Graphics` contains reusable graphics infrastructure. `GpuDevice`
creates and owns an SDL3 GPU device, claims a native window for presentation,
reports the swapchain format, and creates MSL shaders and uploaded GPU buffers.
It uses SDL3's GPU API through the same `SDL3-CS` binding; there is no direct
Metal interop, native wrapper, second binding, or software fallback.

`GpuDevice.Create(IntPtr window)`:

- requires the main thread and a non-zero native `SDL_Window*` handle from
  `Window.Handle`;
- checks `SDL_GPUSupportsShaderFormats(MSL, "metal")`;
- creates an SDL GPU device with `SDL_CreateGPUDevice(MSL, debug: false, "metal")`;
- claims the window for presentation and reads its swapchain format.

The demonstrations live in `examples/HelloTriangle`, `examples/HelloCube`,
`examples/HelloOBJ`, and `examples/HelloO3D`. Each has its own vertex data and
draw calls, and uses `GpuDevice` for the device, window claim, shader creation,
buffer upload, and swapchain format.
[examples/README.md](../examples/README.md) documents their run commands and
rendering conventions. `HelloOBJ` and `HelloO3D` share the renderer, geometry
builder, shader, model-viewer loop, and camera controls in `examples/Shared`; the
only difference is the format parser that produces the `ModelData`.

`GpuDevice.CreateShader` accepts an optional uniform-buffer count. SDL needs the
count to bind data pushed with `SDL_PushGPUVertexUniformData`; shaders that
declare no uniform buffer leave it at the default of zero.

### Camera

`Nymeria.Graphics` contains `Camera`, a small first-person camera built on
`System.Numerics`. It owns position, yaw, pitch, movement speed, mouse
sensitivity, field of view, and clip planes. It exposes a right-handed view
matrix and a perspective projection factory that takes the drawable aspect
ratio. Pitch is clamped below a right angle, yaw is wrapped, non-finite rotation
deltas are ignored, and diagonal movement is normalized.

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
scene graph.

### Ownership and lifetime

- `GpuDevice` owns the GPU device and window claim. Each renderer owns its
  shaders, geometry buffers, pipeline, and, for the cube and model scenes, the
  depth texture. A renderer must be created after the window and disposed before
  it.
- Each `Dispose` waits for the GPU to become idle, releases its resources, and
  then disposes the `GpuDevice`, which releases the window claim and destroys the
  device. Initialization failures release whatever was created in reverse order
  and then rethrow.
- `Window` keeps ownership of the native window and calls `SDL_Quit` after the
  renderer has released its claim.
- `HelloCube`, `HelloOBJ`, and `HelloO3D` own their `Camera` instances and share
  the `FlyCameraControls` implementation. The camera has no unmanaged resources.
  The input mapping disables relative mouse mode and discards pending mouse
  motion during disposal, before the window is destroyed.
- No renderer creates per-frame GPU resources. Command buffers and swapchain
  textures are owned and recycled by SDL.

Limitations in this version:

- GPU rendering is macOS-only. It requires SDL's Metal driver and MSL shader
  source; Windows and Linux will need their own shader formats and native
  packages, which are not implemented.
- Apple Silicon and Intel Macs both use Metal. The macOS native package contains
  both architectures.
- There is no scene graph, texture, lighting, material system, collision system,
  or engine-level input system. `Camera` is reusable math, but input mapping and
  timing stay in the examples; the triangle remains static clip-space geometry.
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
- GPU output and interactive input are not validated by CI.
  [SMOKE_TEST.md](SMOKE_TEST.md) covers them.

## Dependency direction

The following principle guides project references. The core, formats, graphics,
and platform projects and the examples exist today.

```text
examples  -----------------------> engine projects
   |
   v
Nymeria.Graphics   Nymeria.Platform   Nymeria.Formats.Obj   Nymeria.Formats.O3d
        \                /                    |                      |
         v              v                     v                      v
                            Nymeria.Core
```

- `Nymeria.Core` must not depend on graphics, platform, or format code.
- `Nymeria.Formats.Obj` and `Nymeria.Formats.O3d` depend on `Nymeria.Core` and
  must not depend on graphics or platform code.
- `Nymeria.Graphics` and `Nymeria.Platform` may depend on `Nymeria.Core`.
- `Nymeria.Graphics` and `Nymeria.Platform` must not depend on each other
  directly unless the maintainer approves and this document records the reason.
- `examples/` may depend on all engine projects.
- `tests/` may reference the project under test.

`Nymeria.Core` has no project references; it uses `System.Numerics` and the base
class library only. `Nymeria.Formats.Obj` and `Nymeria.Formats.O3d` reference
`Nymeria.Core` only. `Nymeria.Graphics` and `Nymeria.Platform` have no
`Nymeria.Core` reference because neither has a demonstrated need for it yet.
They do not reference each other: each example creates the window and passes
`Window.Handle` to its renderer, which creates a `GpuDevice` in
`Nymeria.Graphics`; the dependency remains in the example. `Camera` sits in
`Nymeria.Graphics` and references only `System.Numerics`. `HelloCube` references
`Nymeria.Core` for its cube model data; `HelloOBJ` references `Nymeria.Core`,
`Nymeria.Formats.Obj`, `Nymeria.Graphics`, and `Nymeria.Platform`; `HelloO3D`
references `Nymeria.Core`, `Nymeria.Formats.O3d`, `Nymeria.Graphics`, and
`Nymeria.Platform`; `HelloTriangle` does not need the core or formats projects.
No engine project references an example. `Nymeria.Core.Tests`,
`Nymeria.Formats.Obj.Tests`, `Nymeria.Formats.O3d.Tests`, and
`Nymeria.Platform.Tests` reference their project or projects under test. No
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
  `GpuDevice`; the examples embed their own `.metal` files, and `HelloOBJ` and
  `HelloO3D` share `examples/Shared/Shaders/Model.metal`, compiled by SDL at
  runtime. No direct Metal interop or shader compiler is used.
- **Math:** `System.Numerics` vectors and matrices, with no custom math library.
  `Camera` uses it for yaw/pitch view and projection math. `HelloCube` composes
  one model-view-projection matrix per cube, uploads row-major bytes, and lets
  MSL interpret them as column-major.
- **Model data:** `Nymeria.Core` contains validated, format-independent
  `ModelData`/`MeshData` types and the `IModelParser` stream contract. It uses
  `System.Numerics` and no external package, SDL3, or GPU dependency.
- **Model parsing:** `Nymeria.Formats.Obj` and `Nymeria.Formats.O3d` are
  dependency-free parsers in isolated format projects. They use the base class
  library and `Nymeria.Core` only, implement `IModelParser`, and document their
  supported subsets and limitations in [OBJ.md](OBJ.md) and [O3D.md](O3D.md). No
  third-party OBJ or O3D loader is used.
- **External packages:** the SDL3 binding and macOS native runtime, plus the
  xUnit test packages `xunit` 2.9.3, `xunit.runner.visualstudio` 4.0.1, and
  `Microsoft.NET.Test.Sdk` 18.11.0. The headless tests cover platform-independent
  `WindowOptions` behavior, model-data validation and parser error semantics, the
  OBJ subset, and the O3D subset; they do not launch a GUI or create a GPU device.

The following choices remain open:

- **Additional platforms and backends:** Windows and Linux rendering, their
  native SDL3 packages, and the shader formats those backends require are not
  implemented.
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
foundation job. macOS window and GPU example behavior is covered by
[SMOKE_TEST.md](SMOKE_TEST.md) instead of a CI claim.

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
