# Nymeria

Nymeria is a small, understandable, cross-platform 3D engine written in C#. It is
intended to remain approachable and may later be extended for retro-game
compatibility. The repository currently contains its first runnable applications:
native macOS windows built on SDL3 that draw a GPU-rendered triangle and an
interactive, depth-tested cube scene.

## Current status

- **Implemented now:** .NET 10 solution, the `Nymeria.Platform` window API,
  `Nymeria.Core` with validated format-independent model data, a parser
  contract, a minimal scene layer, and base-color materials with validated
  texture data, `Nymeria.Formats.Obj` with a
  dependency-free Wavefront OBJ parser, `Nymeria.Formats.O3d` with a
  dependency-free OMSI O3D static-mesh parser, `Nymeria.Formats.Png` with a
  bounded PNG decoder, `Nymeria.Formats.Tga`, `Nymeria.Formats.Jpeg`, and
  `Nymeria.Formats.Dds` with bounded texture decoders, and
  `Nymeria.Formats.Textures` with a signature-first texture loader,
  ambient and single-directional lighting data with Lambert diffuse shading,
  `Nymeria.Graphics` with reusable
  Metal GPU device infrastructure and camera math, `examples/HelloTriangle`, the
  interactive `examples/HelloCube`, the OBJ-rendering `examples/HelloOBJ`, the
  O3D-rendering `examples/HelloO3D`, the scene-instancing `examples/HelloScene`,
  the textured and solid-material `examples/HelloMaterials`, the lit
  `examples/HelloLighting`,
  platform-independent unit tests, CI restore/build/test on macOS, Windows, and
  Linux, repository checks, an audited dependency restore, a documented security
  baseline, and Dependabot updates for GitHub Actions and NuGet.
- **Planned later:** engine-level input, the engine loop, further O3D features
  (scrambled vertices, bones, animation, and material rendering), additional
  graphics backends, and Windows/Linux rendering. See
  [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the authoritative distinction.

## Prerequisites (macOS)

- macOS 12 or newer on Apple Silicon or Intel.
- [.NET SDK 10.0.402](https://dotnet.microsoft.com/download/dotnet/10.0).
  `global.json` pins this SDK with patch-level roll-forward.

No Homebrew SDL installation, Xcode installation, or manual native-library
download is required. NuGet restores the pinned SDL3 binding and its macOS native
`libSDL3.dylib` for the current architecture.

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
dotnet run --project examples/HelloCube/HelloCube.csproj
dotnet run --project examples/HelloOBJ/HelloOBJ.csproj
dotnet run --project examples/HelloO3D/HelloO3D.csproj
dotnet run --project examples/HelloScene/HelloScene.csproj
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj -- examples/HelloMaterials/Assets/checker-bc1.dds
dotnet run --project examples/HelloLighting/HelloLighting.csproj
```

A resizable native window titled `Nymeria` opens at 960 × 540. `HelloTriangle`
draws one red/green/blue triangle. `HelloCube` draws seven cubes at fixed
positions with perspective projection and depth testing, plus a free-fly camera.
`HelloOBJ` loads its checked-in cube fixture and renders it through the same
camera and GPU path; pass a path after `--` to load a different OBJ file.
`HelloO3D` uses the same viewer and GPU path with its synthetic O3D fixture or a
path to another O3D file. `HelloScene` draws several transformed instances of one
shared mesh; press `E` to add an instance, `Q` to remove the last object, and `R`
to reset. `HelloMaterials` draws objects that share a checkerboard texture and a
mesh, a blue-tinted variant of that texture, and a solid-color material; pass a
PNG, JPEG, TGA, or DDS path after `--` to use a different base-color texture.
`HelloLighting` draws three instances of an imported OBJ cube under ambient and
directional Lambert light; press `L` to toggle ambient-only mode, use the arrow
keys to move the light, `[` and `]` to change its intensity, and `R` to reset it. Move with
`W`/`A`/`S`/`D`, `Space`, and
`Left Control`; hold `Shift` to go faster; hold the right mouse button and move
the mouse to look around. Close any window with the red close button or the
standard macOS quit shortcut (`Cmd+Q`). The command exits with status code `0`
on a normal shutdown.

The startup path prints an actionable error and exits with status code `1` if the
native SDL3 library is missing or mismatched, SDL initialization fails, the Metal
GPU backend is unavailable, the window cannot be created or claimed, a shader or
pipeline cannot be created, or the OBJ/O3D example cannot read or parse its file.

### GPU examples

The GPU examples use SDL3's GPU API on the Metal backend. There is no direct
Metal interop and no software fallback. `Nymeria.Graphics` provides the reusable
GPU device and the `Camera` math type; `Nymeria.Core` provides the model data
used by `HelloCube` and the scene data used by `HelloScene`;
`Nymeria.Formats.Obj` parses the file used by `HelloOBJ`, and
`Nymeria.Formats.O3d` parses the file used by `HelloO3D`;
`Nymeria.Formats.Textures` loads the PNG, JPEG, TGA, or DDS texture used by
`HelloMaterials`, and `Nymeria.Formats.Obj` provides the cube used by
`HelloLighting`. Each
example owns its pipeline and depth target, while `HelloOBJ` and `HelloO3D` share
the renderer, shader, model-viewer loop, and camera controls in `examples/Shared`.
`HelloScene` uses the shared geometry builder, model shader, and camera controls
with its own scene renderer. `HelloMaterials` and `HelloLighting` share the
material renderer and camera controls in `examples/Shared`.

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
shader multiplies sampled RGB by the tint and writes an opaque fragment. A PNG,
JPEG, TGA, or DDS path supplied on the command line is decoded by
`Nymeria.Formats.Textures`; signatures take precedence over extensions, and TGA
is recognized by its extension plus header validation. A missing, unreadable,
malformed, oversized, or unsupported file prints a diagnostic and falls back to
the generated checkerboard. Block-compressed DDS is uploaded directly when the
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

All seven examples skip rendering while minimized and recover when a drawable is
available again. [examples/README.md](examples/README.md) records the matrix
order, handedness, shader layout, depth convention, and run commands.

This path is macOS-only for now. Windows and Linux need their own native SDL3
packages and shader formats; see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
GPU output is validated manually with [docs/SMOKE_TEST.md](docs/SMOKE_TEST.md),
not in CI.

## Tests

Run the platform-independent unit tests:

```bash
dotnet test Nymeria.slnx
```

The tests cover `WindowOptions` defaults and validation, model-data validation
and parser-contract error semantics, transform composition, scene lifecycle and
mesh sharing, material and texture-data validation, and the lighting data, Lambert
term, and normal-matrix math in `Nymeria.Core.Tests`, the
OBJ subset and diagnostics in `Nymeria.Formats.Obj.Tests`, the O3D header,
supported versions and flags, conversions, limits, and failure behavior in
`Nymeria.Formats.O3d.Tests`, and the PNG subset, malformed input, and decode
limits in `Nymeria.Formats.Png.Tests`, and the JPEG, TGA, DDS, and loader paths in
`Nymeria.Formats.Jpeg.Tests`, `Nymeria.Formats.Tga.Tests`,
`Nymeria.Formats.Dds.Tests`, and `Nymeria.Formats.Textures.Tests`. There are
deliberately no fake GUI tests: native window behavior is covered by the manual
smoke test below and is not claimed as a headless CI result.

## Manual smoke test

Follow [docs/SMOKE_TEST.md](docs/SMOKE_TEST.md). GUI behavior is validated
manually; it is not claimed as a headless CI result.

## Repository layout

```text
.github/        Repository checks and Dependabot configuration
docs/           Architecture, Git policy, and manual smoke test
src/core/       Nymeria.Core model data, parser contract, scene, and material data
src/formats/    Nymeria.Formats.Obj, O3d, Png, Tga, Jpeg, Dds, and Textures
src/graphics/   Nymeria.Graphics reusable GPU infrastructure and camera math
src/platform/   Nymeria.Platform native window project
examples/       HelloTriangle, HelloCube, HelloOBJ, HelloO3D, HelloScene,
                HelloMaterials, and HelloLighting
tests/          Headless unit tests for core, formats, and platform projects
```

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Model data and parser contract](docs/MODEL_DATA.md)
- [Scene data and instancing](docs/SCENE.md)
- [Materials and base-color textures](docs/MATERIALS.md)
- [Ambient and directional lighting](docs/LIGHTING.md)
- [Wavefront OBJ import](docs/OBJ.md)
- [OMSI O3D import](docs/O3D.md)
- [Examples](examples/README.md)
- [Security baseline](docs/SECURITY.md)
- [Security checklist](docs/SECURITY_CHECKLIST.md)
- [Security audit 01](docs/SECURITY_AUDIT_01.md)
- [Git guidelines](docs/GIT_GUIDELINES.md)
- [macOS smoke test](docs/SMOKE_TEST.md)
- [Agent instructions](AGENTS.md)

## Repository automation

The `Repository checks` GitHub Actions workflow runs on pushes and pull requests
to `main`. One job validates the repository foundation, including Markdown links,
tracked build output, and Dependabot configuration. A second job restores, builds,
and runs the headless unit tests on macOS, Windows, and Linux. The GUI and GPU
output are not launched or validated in CI; window and example behavior is
covered by the manual smoke test.

Dependabot checks GitHub Actions and NuGet weekly and opens update pull requests.
The existing `Dependabot auto-merge` workflow squash-merges a Dependabot pull
request only after `Repository checks` succeeds on that pull request's current
head commit. It does not deploy. The organization currently prevents GitHub
Actions from approving pull requests, so a Dependabot merge is recorded without
an automated approval review. Human pull requests remain manual.

## License

Nymeria is licensed under the BSD 3-Clause License. See [LICENSE](LICENSE).
