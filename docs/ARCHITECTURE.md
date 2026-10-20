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
- `Nymeria.Graphics`, a class library that owns the SDL3 GPU device and window
  presentation claim, provides reusable MSL shader and buffer creation, and
  contains a reusable, SDL-independent free-fly `Camera`.
- `examples/HelloTriangle`, a console executable that opens the window and draws
  the triangle with example-owned shaders and geometry.
- `examples/HelloCube`, an interactive console executable that draws multiple
  fixed-position cubes with a free-fly camera, perspective projection, depth
  testing, example-owned shaders and geometry, and an example-owned depth target.
- `Nymeria.Platform.Tests`, automated tests for platform-independent options.
- SDL3 windowing and GPU rendering through the pinned `SDL3-CS` and
  `SDL3-CS.MacOS` packages.
- A `Repository checks` GitHub Actions workflow that validates repository
  foundations and restores, builds, and tests the solution on macOS, Windows, and
  Linux.
- Weekly Dependabot updates for GitHub Actions and NuGet, auto-merged only after
  repository checks pass.
- Organizational directories kept in Git with `.gitkeep` files while empty.

### Planned later

The following are future work and are **not** implemented or approved in detail:

- `Nymeria.Core`.
- Engine-level input handling and an engine loop.
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
| `src/core/` | Platform-independent engine fundamentals, eventually `Nymeria.Core`. |
| `src/graphics/` | `Nymeria.Graphics`: reusable SDL3 GPU device and resource infrastructure. |
| `src/platform/` | `Nymeria.Platform`: windowing, input, and operating-system integration. |
| `examples/` | Small runnable programs that demonstrate engine usage. |
| `tests/` | Automated unit and integration tests for the engine projects. |
| `docs/` | Authoritative project and architecture decisions. |
| `.github/` | Repository automation: checks and dependency updates. |

`src/platform/` contains the `Nymeria.Platform` project, `src/graphics/`
contains `Nymeria.Graphics`, `examples/` contains `HelloTriangle` and
`HelloCube`, and `tests/` contains `Nymeria.Platform.Tests`. `src/core/` still
contains a `.gitkeep` placeholder because it has no source code yet. The camera
lives in `Nymeria.Graphics` rather than in a new project: it is reusable 3D
rendering math with no SDL, window, or GPU dependency, and creating
`Nymeria.Core` solely for it would add a project before a second concrete need.
Git does not track empty directories, so the placeholder keeps the intended
layout visible in a fresh clone without introducing speculative scaffolding.

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

The demonstrations live in `examples/HelloTriangle` and `examples/HelloCube`.
Each owns its vertex data, shader resource, buffers, pipeline, and draw calls,
and uses `GpuDevice` for the device, window claim, shader creation, buffer
upload, and swapchain format. [examples/README.md](../examples/README.md)
documents their run commands and rendering conventions.

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
It owns 24 vertices (four per face, duplicated so every face has a solid color),
36 16-bit indices, the cube shaders, both buffers, the pipeline, and the depth
texture. The example places seven cubes at fixed positions and reuses that
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

Input mapping lives in `examples/HelloCube/FlyCameraControls.cs`, not in an
engine project. It reads SDL's cached keyboard and mouse state after
`Window.ProcessEvents` pumps the queue. Holding the right mouse button enables
window-relative mouse mode; releasing it, losing input focus, minimizing, or
disposing disables relative mode and restores the cursor. The example ignores
input while the window lacks input focus. Close handling and the single event
loop remain in `Nymeria.Platform`.

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

### Ownership and lifetime

- `GpuDevice` owns the GPU device and window claim. Each renderer owns its
  shaders, geometry buffers, pipeline, and, for the cube scene, the depth
  texture. A renderer must be created after the window and disposed before it.
- Each `Dispose` waits for the GPU to become idle, releases its resources, and
  then disposes the `GpuDevice`, which releases the window claim and destroys the
  device. Initialization failures release whatever was created in reverse order
  and then rethrow.
- `Window` keeps ownership of the native window and calls `SDL_Quit` after the
  renderer has released its claim.
- `HelloCube` owns its `Camera` instance and its example input mapping. The
  camera has no unmanaged resources. The input mapping disables relative mouse
  mode and discards pending mouse motion during disposal, before the window is
  destroyed.
- Neither renderer creates per-frame GPU resources. Command buffers and swapchain
  textures are owned and recycled by SDL.

Limitations in this version:

- GPU rendering is macOS-only. It requires SDL's Metal driver and MSL shader
  source; Windows and Linux will need their own shader formats and native
  packages, which are not implemented.
- Apple Silicon and Intel Macs both use Metal. The macOS native package contains
  both architectures.
- There is no scene graph, texture, lighting, material system, collision system,
  or engine-level input system. `Camera` is reusable math, but input mapping and
  timing stay in `HelloCube`; the triangle remains static clip-space geometry.
- GPU output and interactive input are not validated by CI.
  [SMOKE_TEST.md](SMOKE_TEST.md) covers them.

## Dependency direction

The following principle guides project references. The graphics project, platform
project, and examples exist today.

```text
examples  -----------------------> engine projects
   |
   v
Nymeria.Graphics     Nymeria.Platform
        \                 /
         v               v
             Nymeria.Core
```

- `Nymeria.Core` must not depend on graphics or platform code.
- `Nymeria.Graphics` and `Nymeria.Platform` may depend on `Nymeria.Core`.
- `Nymeria.Graphics` and `Nymeria.Platform` must not depend on each other
  directly unless the maintainer approves and this document records the reason.
- `examples/` may depend on all engine projects.
- `tests/` may reference the project under test.

`Nymeria.Graphics` and `Nymeria.Platform` have no `Nymeria.Core` reference
because `Nymeria.Core` does not exist and neither project has a demonstrated need
for it. They do not reference each other: each example creates the window and
passes `Window.Handle` to its renderer, which creates a `GpuDevice` in
`Nymeria.Graphics`; the dependency remains in the example. `Camera` sits in
`Nymeria.Graphics` and references only `System.Numerics`, so it adds no project
reference or platform dependency. No engine project references an example. Both
examples reference both engine projects, and `Nymeria.Platform.Tests` references
the project under test. No interfaces or abstractions are created to enforce the
future direction yet. Adding them speculatively would violate YAGNI.

## Technical choices

The following choices are implemented:

- **.NET SDK and target framework:** .NET 10, targeting `net10.0`. `global.json`
  pins SDK 10.0.402 with `latestPatch` roll-forward and no prerelease SDKs.
- **Solution format:** `Nymeria.slnx`, built with the `dotnet` CLI.
- **Windowing:** SDL3 3.4.18 through `SDL3-CS` 3.4.18 and the macOS native
  package `SDL3-CS.MacOS` 3.4.18.
- **GPU rendering:** SDL3's GPU API with the Metal backend and MSL shader source,
  reached through `SDL3-CS`. `Nymeria.Graphics` provides the reusable
  `GpuDevice`; the examples embed their own `.metal` files and SDL compiles them
  at runtime. No direct Metal interop or shader compiler is used.
- **Math:** `System.Numerics` vectors and matrices, with no custom math library.
  `Camera` uses it for yaw/pitch view and projection math. `HelloCube` composes
  one model-view-projection matrix per cube, uploads row-major bytes, and lets
  MSL interpret them as column-major.
- **External packages:** the SDL3 binding and macOS native runtime, plus the
  xUnit test packages `xunit` 2.9.3, `xunit.runner.visualstudio` 4.0.1, and
  `Microsoft.NET.Test.Sdk` 18.11.0. The tests cover only platform-independent
  `WindowOptions` behavior; they do not launch a GUI or create a GPU device.

The following choices remain open:

- **Additional platforms and backends:** Windows and Linux rendering, their
  native SDL3 packages, and the shader formats those backends require are not
  implemented.
- **`Nymeria.Core`:** the project does not exist yet and is not justified by the
  examples.

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
