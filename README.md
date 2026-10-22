# Nymeria

Nymeria is a small, understandable, cross-platform 3D engine written in C#. It is
intended to remain approachable and may later be extended for retro-game
compatibility. The repository currently contains its first runnable applications:
native macOS windows built on SDL3 that draw a GPU-rendered triangle and an
interactive, depth-tested cube scene.

## Current status

- **Implemented now:** .NET 10 solution, the `Nymeria.Platform` window API,
  `Nymeria.Core` with validated format-independent model data, a parser
  contract, a minimal scene layer, explicit mesh level-of-detail data, mesh
  bounds and conservative frustum helpers, and base-color materials with
  validated texture data,
  `Nymeria.Formats.Obj` with a
  dependency-free Wavefront OBJ parser, `Nymeria.Formats.O3d` with a
  dependency-free OMSI O3D static-mesh parser, `Nymeria.Formats.DirectX` with a
  bounded text DirectX `.x` static-mesh parser, `Nymeria.Formats.Png` with a
  bounded PNG decoder, `Nymeria.Formats.Tga`, `Nymeria.Formats.Jpeg`,
  `Nymeria.Formats.Dds`, and `Nymeria.Formats.Bmp` with bounded texture decoders,
  `Nymeria.Formats.Textures` with a signature-first texture loader,
  `Nymeria.Formats.Wav` with a bounded PCM/float decoder, `Nymeria.Audio` with
  validated clips, overlapping 2D and spatial voices, gain, looping, and an SDL3
  playback device, a headless audio listener and distance/pan math layer,
  immutable translation/rotation/scale animation tracks with deterministic
  sampling and a per-object playback controller,
  ambient and single-directional lighting data with Lambert diffuse shading,
  `Nymeria.Graphics` with reusable
  Metal GPU device infrastructure and camera math, `examples/HelloTriangle`, the
  interactive `examples/HelloCube`, the OBJ-rendering `examples/HelloOBJ`, the
  O3D-rendering `examples/HelloO3D`, the text-`.x`-rendering `examples/HelloX`,
  the scene-instancing `examples/HelloScene`, the textured and solid-material
  `examples/HelloMaterials`, the lit `examples/HelloLighting`, the conservative
  frustum-culling `examples/HelloCulling`, the explicit-LOD and distance-culling
  `examples/HelloLOD`, the chunk-streaming `examples/HelloStreaming`, the
  raycasting and picking `examples/HelloPicking`, the collision-detection
  `examples/HelloCollision`, the audio and spatial-sound `examples/HelloAudio`,
  the transform-animation `examples/HelloAnimation`,
  the CPU-profiling `examples/HelloBenchmark`,
  the window-only `examples/HelloWindow`,
  bounded frame-timing diagnostics, headless mesh-bounds, visibility, LOD,
  streaming, raycasting, collision, audio, and animation helpers,
  platform-independent unit tests,
  CI restore/build/test on macOS, Windows, and Linux, repository checks, an
  audited dependency restore, a documented security baseline, and Dependabot
  updates for GitHub Actions and NuGet.
- **Planned later:** engine-level input, the engine loop, further O3D features
  (scrambled vertices, bones, animation, and material rendering), additional
  graphics backends, and Windows/Linux rendering. See
  [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the authoritative distinction.

## Prerequisites

- [.NET SDK 10.0.402](https://dotnet.microsoft.com/download/dotnet/10.0).
  `global.json` pins this SDK with patch-level roll-forward.

macOS:

- macOS 12 or newer on Apple Silicon or Intel.

Windows:

- Windows 10 or newer on x64, x86, or arm64, for building the solution and for
  the window-only example. GPU rendering is not implemented on Windows yet.

No Homebrew SDL installation, Xcode installation, or manual native-library
download is required. NuGet restores the pinned SDL3 binding and the native
`libSDL3.dylib` or `SDL3.dll` for the current platform and architecture.

Verify the SDK:

```bash
dotnet --version
```

The output must be `10.0.402` or another `10.0.4xx` patch.

## Build and run

From the repository root:

```bash
dotnet restore Nymeria.slnx
dotnet build Nymeria.slnx
dotnet run --project examples/HelloTriangle/HelloTriangle.csproj
dotnet run --project examples/HelloWindow/HelloWindow.csproj
dotnet run --project examples/HelloWindow/HelloWindow.csproj -- --smoke
dotnet run --project examples/HelloCube/HelloCube.csproj
dotnet run --project examples/HelloOBJ/HelloOBJ.csproj
dotnet run --project examples/HelloO3D/HelloO3D.csproj
dotnet run --project examples/HelloX/HelloX.csproj
dotnet run --project examples/HelloScene/HelloScene.csproj
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker.bmp
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker-32.bmp
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker-bc1.dds
dotnet run --project examples/HelloLighting/HelloLighting.csproj
dotnet run --project examples/HelloCulling/HelloCulling.csproj
dotnet run --project examples/HelloCulling/HelloCulling.csproj -- --no-culling
dotnet run --project examples/HelloCollision/HelloCollision.csproj
dotnet run --project examples/HelloAudio/HelloAudio.csproj
dotnet run --project examples/HelloAnimation/HelloAnimation.csproj
dotnet run --project examples/HelloLOD/HelloLOD.csproj
dotnet run --project examples/HelloStreaming/HelloStreaming.csproj
dotnet run --project examples/HelloStreaming/HelloStreaming.csproj -- --no-streaming
dotnet run --project examples/HelloPicking/HelloPicking.csproj
dotnet run --project examples/HelloBenchmark/HelloBenchmark.csproj --configuration Release -- --instances 2048
```

A resizable native window titled `Nymeria` opens at 960 × 540. `HelloWindow`
opens only the window, prints its logical and drawable sizes, and pumps events
without drawing anything; pass `--smoke` to pump a fixed number of events and
exit without user input. `HelloTriangle`
draws one red/green/blue triangle. `HelloCube` draws seven cubes at fixed
positions with perspective projection and depth testing, plus a free-fly camera.
`HelloOBJ` loads its checked-in cube fixture and renders it through the same
camera and GPU path; pass a path after `--` to load a different OBJ file.
`HelloO3D` uses the same viewer and GPU path with its synthetic O3D fixture or a
path to another O3D file. `HelloX` uses it with its checked-in text `.x` fixture
or a path to another text `.x` file. `HelloScene` draws several transformed
instances of one shared mesh; press `E` to add an instance, `Q` to remove the
last object, and `R` to reset. `HelloMaterials` draws objects that share a
checkerboard texture and a mesh, a blue-tinted variant of that texture, and a
solid-color material; pass a PNG, JPEG, TGA, DDS, or BMP path after `--` to use a
different base-color texture.
`HelloLighting` draws three instances of an imported OBJ cube under ambient and
directional Lambert light; press `L` to toggle ambient-only mode, use the arrow
keys to move the light, `[` and `]` to change its intensity, and `R` to reset it.
`HelloCulling` draws 551 instances of one shared cube mesh and skips instances
proven fully outside the camera frustum; press `C` to toggle culling at runtime,
or start with `--no-culling` for comparison. `HelloLOD` switches each instance
among three explicit sphere-detail meshes at configurable distances and can skip
objects beyond a maximum distance; `L` toggles LOD, `X` toggles distance
culling, `F` toggles frustum culling, `[`/`]` change the thresholds, and `R`
resets them. `HelloStreaming` loads and unloads 25 deterministic chunks around a
free camera, reuses one cube mesh and six materials across chunks, and releases
chunk-exclusive GPU buffers after unload; `S` toggles streaming, `T` teleports,
`C` toggles culling, and `R` resets the camera. `HelloPicking` streams nine
chunks, picks whole objects with cursor or center-of-screen rays, highlights the
selection, and shows its label and distance in the window title; left-click
picks, `V` switches modes, `G` toggles the ghost box's pickability, and `R`
resets the camera. `HelloCollision` animates box and sphere colliders around a
movable player box, highlights every currently overlapping pair, and prints
active-collider, tested-pair, overlapping-pair, and query-time diagnostics; the
arrow keys move the player, `P` pauses animation, and `R` resets. `HelloAudio`
generates its own looping test audio, plays a non-spatial ambient sound and two
spatial tone sources, draws the source positions, and uses the camera as the
listener; `P` and `O` pause/resume the ambient and source A, `X` stops and
restarts source A, `[`/`]` and `-`/`=` change master and source gain, and `R`
resets the listener. `HelloAnimation` plays three independent transform clips: a
door rotating around its authored hinge, a wiper sweeping around its base, and a
cube that moves and scales; `P` pauses or resumes, `R` restarts, `L` toggles
looping, and `-`/`=` change playback speed. `HelloBenchmark` is an
auto-exiting Release CPU benchmark with a
fixed camera, deterministic 512/2048/8192-instance scenes, culling and LOD
modes, and 16 ms pacing control; it prints bounded phase timings, instance,
draw-call, triangle, and per-level counts, and managed allocation, then exits.
See [docs/PERFORMANCE.md](docs/PERFORMANCE.md) for commands and interpretation.
Move with
`W`/`A`/`S`/`D`, `Space`, and
`Left Control`; hold `Shift` to go faster; hold the right mouse button and move
the mouse to look around. Close any window with the red close button or the
standard macOS quit shortcut (`Cmd+Q`). The command exits with status code `0`
on a normal shutdown.

The startup path prints an actionable error and exits with status code `1` if the
native SDL3 library is missing or mismatched, SDL initialization fails, the Metal
GPU backend is unavailable, the window cannot be created or claimed, a shader or
pipeline cannot be created, or the OBJ/O3D/`.x` example cannot read or parse its
file.

### GPU examples

The GPU examples use SDL3's GPU API on the Metal backend. There is no direct
Metal interop and no software fallback. `Nymeria.Graphics` provides the reusable
GPU device and the `Camera` math type; `Nymeria.Core` provides the model data
used by `HelloCube` and the scene data used by `HelloScene`;
`Nymeria.Formats.Obj` parses the file used by `HelloOBJ`,
`Nymeria.Formats.O3d` parses the file used by `HelloO3D`, and
`Nymeria.Formats.DirectX` parses the file used by `HelloX`;
`Nymeria.Formats.Textures` loads the PNG, JPEG, TGA, DDS, or BMP texture used by
`HelloMaterials`, and `Nymeria.Formats.Obj` provides the cube used by
`HelloLighting`. Each
example owns its pipeline and depth target, while `HelloOBJ`, `HelloO3D`, and
`HelloX` share the renderer, shader, model-viewer loop, and camera controls in
`examples/Shared`.
`HelloScene` uses the shared geometry builder, model shader, and camera controls
with its own scene renderer. `HelloMaterials`, `HelloLighting`, `HelloCulling`,
`HelloCollision`, `HelloLOD`, `HelloStreaming`, `HelloPicking`, `HelloAudio`,
and `HelloAnimation` share the material renderer and camera controls in
`examples/Shared`; `HelloAudio` also uses `Nymeria.Audio` and the headless audio
math in `Nymeria.Core.Audio`, and `HelloAnimation` uses the headless transform
animation in `Nymeria.Core.Animation`. `HelloCulling` enables the renderer's
optional conservative frustum culling and prints per-frame instance and
draw-call counts; bounds,
planes, fallbacks, and limitations are documented in
[docs/CULLING.md](docs/CULLING.md). `HelloLOD` enables the renderer's optional
LOD and distance culling for objects with a `MeshLod` configuration and prints
per-level usage; distance semantics, hysteresis, and limits are documented in
[docs/LOD.md](docs/LOD.md). `HelloBenchmark` provides the repeatable culling and
LOD comparison and CPU timing baseline described in
[docs/PERFORMANCE.md](docs/PERFORMANCE.md) and
[docs/PERFORMANCE_BASELINE_01.md](docs/PERFORMANCE_BASELINE_01.md).

The shaders are `examples/HelloTriangle/Shaders/Triangle.metal`,
`examples/HelloCube/Shaders/Cube.metal`, the shared
`examples/Shared/Shaders/Model.metal`, and
`examples/Shared/Shaders/Material.metal`. They are embedded into their
examples and compiled from Metal Shading Language source by SDL at startup. No
shader compiler or Xcode build step is required.

`HelloTriangle` draws a frame, then waits up to 100 ms for window events before
drawing again, so idle redraws stay near ten per second. It renders in a centered
square viewport derived from the swapchain's physical pixels, so it keeps its
proportions on resize and on high-DPI displays.

`HelloCube` renders continuously, targeting about 16 ms per frame. Its cube is a
format-independent `ModelData` with positions, normals, texture coordinates,
triangle indices, and six face subsets. The example converts that model to the
existing interleaved position/color layout; the shader, depth target, and visuals
are unchanged. Its reusable `Camera` in `Nymeria.Graphics` stores position, yaw,
and pitch, clamps pitch, and builds a right-handed view matrix and a perspective
projection from the actual drawable aspect ratio. The example moves the camera
from measured elapsed time with a 0.1 s per-frame cap, normalizes diagonal
movement, and maps SDL3 keyboard and mouse state to the camera. Holding the right
mouse button enables relative mouse mode; releasing it, losing focus, minimizing,
or exiting restores the cursor. Each cube gets its own model matrix, composed and
uploaded with the same row-major convention as before. The central cube's
rotation is also advanced by elapsed time with the same cap. The depth target and
pipeline state live in the example, not in `Nymeria.Graphics`.

`HelloOBJ` loads `examples/HelloOBJ/Assets/cube.obj` by default, or a path passed
on the command line, through `Nymeria.Formats.Obj`. It converts the parsed model
to the shared position/color layout with 32-bit indices and renders it with the
same device, depth test, camera, and input path. The checked-in fixture is
original project content: six convex quads with positions, UVs, normals, and six
`usemtl` labels. No MTL file is loaded. The parser supports a documented
polygonal subset; see [docs/OBJ.md](docs/OBJ.md) for its exact behavior and
limitations.

`HelloO3D` uses the same viewer and GPU path with
`examples/HelloO3D/Assets/cube.o3d`, a synthetic fixture generated from the
documented O3D layout, or a path passed on the command line. The importer supports
unscrambled static meshes of versions 1 through 7 and rejects scrambled vertices,
bones, unknown sections, and unknown flags with offset-based diagnostics. See
[docs/O3D.md](docs/O3D.md) for the exact support matrix, provenance, and known
uncertainties.

`HelloX` uses the same viewer with
`examples/HelloX/Assets/cube.x`, an original text `.x` fixture, or a path passed
on the command line. The importer supports text `xof 0302txt`/`0303txt` files with
static meshes, baked frame transforms, normals, texture coordinates, and material
names. It rejects binary, compressed, skinned, animated, and unknown constructs
with line-based diagnostics. See [docs/DIRECTX_X.md](docs/DIRECTX_X.md) for the
support matrix, conversions, limits, and known limitations.

`HelloScene` renders a `Nymeria.Core.Scenes.Scene` containing objects that share
one procedural cube mesh. Its renderer uploads one GPU mesh per distinct
`MeshData` and draws each object with its own model matrix. Scenes own no GPU
resources; removing objects leaves shared meshes and cached uploads intact for
other instances. `E`, `Q`, and `R` add, remove, and reset objects at runtime.
See [docs/SCENE.md](docs/SCENE.md) for the data and ownership contract.

`HelloMaterials` renders a scene whose objects carry a `Material`: a base-color
tint and an optional base-color `Texture`. It generates a four-quadrant
checkerboard in code, shares one mesh and one material between two objects, tints
the same texture blue for a third, and shows a solid orange material on a
fourth. The renderer caches one GPU mesh per distinct `MeshData` and one GPU
texture per distinct `Texture`, uploads mip chains once, and binds a generated
white fallback for untextured materials and meshes without UVs. The fragment
shader multiplies sampled RGB by the tint and honors a material transparency
mode: opaque, an alpha cutoff that discards fragments, or straight source-over
blending drawn back to front after opaque geometry. A PNG,
JPEG, TGA, DDS, or BMP path supplied on the command line is decoded by
`Nymeria.Formats.Textures`; signatures take precedence over extensions, and TGA
is recognized by its extension plus header validation. BMP support covers
uncompressed 24-bit, 32-bit, 8-bit indexed, and standard `BI_BITFIELDS` images
with both row orientations; RLE and other variants are rejected explicitly. A
missing, unreadable, malformed, oversized, or unsupported file prints a
diagnostic and falls back to the generated checkerboard. Block-compressed DDS is uploaded directly when the
backend supports the format; otherwise the example reports it and falls back. See
[docs/MATERIALS.md](docs/MATERIALS.md) for the data contract, supported DDS
variants, decoder limits, and resource ownership.

`HelloLighting` renders three instances of the `HelloOBJ` cube with the shared
material renderer. A `SceneLighting` with one ambient light and one directional
light is assigned to the scene each time the light changes, and the renderer
adds Lambert diffuse shading on top of the material base color and sampled
texture. Two objects use the shared checkerboard material and one uses a solid
orange material, with rotations and non-uniform scales that expose incorrect
normal transforms. `Scene.Lighting` is `null` by default, so `HelloMaterials`
remains unlit. See [docs/LIGHTING.md](docs/LIGHTING.md) for the light data model,
the equation, the coordinate-space and normal-matrix conventions, and the
deferred features.

`HelloCulling` draws 551 instances of one shared cube mesh through the shared
material renderer with optional conservative frustum culling. It tests each
instance's transformed model-space bounds against six normalized planes derived
from `view * projection`; an instance is skipped only when it is fully outside
one plane. The example includes instances behind the camera, beyond the far
plane, outside the horizontal view, and crossing frustum boundaries, with
rotation and non-uniform or negative scale. `C` toggles culling at runtime and
`--no-culling` starts it disabled; once per second the console reports FPS,
instances tested, submitted, culled, and actual indexed draw calls. See
[docs/CULLING.md](docs/CULLING.md) for bounds semantics, safe fallbacks,
statistics, measurements, and limits.

`HelloCollision` renders six boxes and spheres through the shared material
renderer and gives each one an explicit local `CollisionShape`. `SceneCollision`
transforms them with the current model matrices, tests every unordered pair, and
applies a shared yellow material to current overlaps; the underlying base
materials are restored each frame. The cyan player box moves with the arrow keys,
while orbit and bob paths cross the static obstacles without input. Boxes use
conservative world AABBs; spheres use the largest axis scale as a conservative
radius under non-uniform scale. `P` pauses animation and `R` resets. Once per
second the console reports active colliders, tested pairs, overlapping pairs,
player contact, query time, FPS, and drawn instances. This is detection only;
overlaps do not move, stop, or resolve anything. See
[docs/COLLISION.md](docs/COLLISION.md).

`HelloPicking` renders a streamed nine-chunk world through the shared material
renderer and picks whole objects with `Nymeria.Core.Raycasting`. A ray is built
from the mouse pixel or the viewport center using the camera's actual view and
projection matrices; the nearest hit is chosen from each object's transformed
world bounds, and `IsPickable` excludes the gray ghost box until `G` enables it.
The selected object is highlighted by assigning a shared material, and the
window title shows its label and distance. Picking deliberately ignores frustum
culling, distance culling, and the selected LOD variant. See
[docs/PICKING.md](docs/PICKING.md).

`HelloLOD` supplies three explicit sphere meshes (576, 100, and 30 triangles) for
each instance and selects one per frame from the camera distance to the
conservative union of their world bounds. The configuration also has an optional
maximum distance, so instances beyond it skip draw submission. The medium and low
levels are visibly faceted, and a 0.75-unit hysteresis band keeps a camera
oscillating near a threshold from flickering. The example prints candidate,
frustum-culled, distance-culled, drawn, draw-call, submitted-triangle, and
per-level counts plus render-time average and p95; see
[docs/LOD.md](docs/LOD.md) for the contract, benchmark method, and limits.

All fourteen interactive examples skip rendering while minimized and recover
when a drawable is available again. [examples/README.md](examples/README.md) records the matrix
order, handedness, shader layout, depth convention, and run commands.

The GPU path is macOS-only for now. Windows deploys the native `SDL3.dll` and can
build and run the window-only `HelloWindow` example, but it has no GPU backend
yet: `GpuDevice` still requests Metal, and Direct3D 12 is not implemented.
Linux is unsupported and ships no native package. See
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and
[docs/WINDOWS_01_NATIVE_SDL3.md](docs/WINDOWS_01_NATIVE_SDL3.md).
GPU output is validated manually with [docs/SMOKE_TEST.md](docs/SMOKE_TEST.md),
not in CI.

## Tests

Run the platform-independent unit tests:

```bash
dotnet test Nymeria.slnx
```

The tests cover `WindowOptions` defaults and validation, model-data validation
and parser-contract error semantics, transform composition, scene lifecycle and
mesh sharing, mesh bounds, frustum intersection, visibility counts, bounded
frame-timing aggregation and warm-up exclusion, material and texture-data
validation, the lighting data, Lambert term, and normal-matrix math, ray and
AABB boundary behavior, nearest-hit selection, transformed bounds, viewport
camera rays, pick eligibility, picking independence from culling and LOD,
collision-shape validation, inclusive box and sphere overlap, conservative
collision transforms, scene collision lifecycle and ordering, audio distance and
pan math, listener validation, PCM resampling, animation track sampling and
validation, quaternion interpolation, clip limits, playback lifecycle, looping,
and deterministic multi-player conflicts in
`Nymeria.Core.Tests`, the
OBJ subset and diagnostics in `Nymeria.Formats.Obj.Tests`, the O3D header,
supported versions and flags, conversions, limits, and failure behavior in
`Nymeria.Formats.O3d.Tests`, the text `.x` header, frames, transforms, mesh
elements, conversions, malformed input, and limits in
`Nymeria.Formats.DirectX.Tests`, the PNG subset, malformed input, and decode
limits in `Nymeria.Formats.Png.Tests`, and the JPEG, TGA, DDS, BMP, and loader
paths in `Nymeria.Formats.Jpeg.Tests`, `Nymeria.Formats.Tga.Tests`,
`Nymeria.Formats.Dds.Tests`, `Nymeria.Formats.Bmp.Tests`,
`Nymeria.Formats.Textures.Tests`, and `Nymeria.Formats.Wav.Tests`, and the
mixer, voice lifecycle, clip ownership, concurrency, and allocation checks in
`Nymeria.Audio.Tests`. Device-dependent audio tests are explicitly skipped and
covered by the manual smoke test. There are
deliberately no fake GUI tests: native window behavior is covered by the manual
smoke test below and is not claimed as a headless CI result.

## Manual smoke test

Follow [docs/SMOKE_TEST.md](docs/SMOKE_TEST.md). GUI behavior is validated
manually; it is not claimed as a headless CI result.

## Repository layout

```text
.github/        Repository checks and Dependabot configuration
docs/           Architecture, Git policy, and manual smoke test
src/audio/      Nymeria.Audio SDL3 playback, mixer, clips, and voices
src/core/       Nymeria.Core model data, parser contract, scene, material,
                audio math, and transform animation data
src/formats/    Nymeria.Formats.Obj, O3d, DirectX, Png, Tga, Jpeg, Dds, Bmp,
                Textures, and Wav
src/graphics/   Nymeria.Graphics reusable GPU infrastructure and camera math
src/platform/   Nymeria.Platform native window project
examples/       HelloTriangle, HelloWindow, HelloCube, HelloOBJ, HelloO3D, HelloX,
                HelloScene, HelloMaterials, HelloLighting, HelloCulling,
                HelloCollision, HelloAudio, HelloAnimation, HelloLOD,
                HelloStreaming, HelloPicking, and HelloBenchmark
tests/          Headless unit tests for audio, core, formats, and platform
                projects
```

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Model data and parser contract](docs/MODEL_DATA.md)
- [Scene data and instancing](docs/SCENE.md)
- [Materials and base-color textures](docs/MATERIALS.md)
- [Ambient and directional lighting](docs/LIGHTING.md)
- [Mesh bounds and frustum culling](docs/CULLING.md)
- [Explicit mesh LOD](docs/LOD.md)
- [Scene chunk streaming](docs/STREAMING.md)
- [Raycasting and object picking](docs/PICKING.md)
- [Basic collision detection](docs/COLLISION.md)
- [Audio and basic spatial sound](docs/AUDIO.md)
- [Transform animation](docs/ANIMATION.md)
- [Performance measurement](docs/PERFORMANCE.md)
- [Performance baseline 01](docs/PERFORMANCE_BASELINE_01.md)
- [Wavefront OBJ import](docs/OBJ.md)
- [OMSI O3D import](docs/O3D.md)
- [Text DirectX .x import](docs/DIRECTX_X.md)
- [Examples](examples/README.md)
- [Security baseline](docs/SECURITY.md)
- [Security checklist](docs/SECURITY_CHECKLIST.md)
- [Security audit 01](docs/SECURITY_AUDIT_01.md)
- [Security audit 02](docs/SECURITY_AUDIT_02.md)
- [Windows native SDL3 deployment](docs/WINDOWS_01_NATIVE_SDL3.md)
- [Git guidelines](docs/GIT_GUIDELINES.md)
- [macOS smoke test](docs/SMOKE_TEST.md)
- [Agent instructions](AGENTS.md)

## Repository automation

The `Repository checks` GitHub Actions workflow runs on pushes and pull requests
to `main`. One job validates the repository foundation, including Markdown links,
tracked build output, and Dependabot configuration. A second job restores, builds,
and runs the headless unit tests on macOS, Windows, and Linux. GUI, GPU, and audio
device output are not launched or validated in CI; window, GPU, and audio-device
behavior is covered by the manual smoke test.

Dependabot checks GitHub Actions and NuGet weekly and opens update pull requests.
The existing `Dependabot auto-merge` workflow squash-merges a Dependabot pull
request only after `Repository checks` succeeds on that pull request's current
head commit. It does not deploy. The organization currently prevents GitHub
Actions from approving pull requests, so a Dependabot merge is recorded without
an automated approval review. Human pull requests remain manual.

## License

Nymeria is licensed under the BSD 3-Clause License. See [LICENSE](LICENSE).
