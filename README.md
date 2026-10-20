# Nymeria

Nymeria is a small, understandable, cross-platform 3D engine written in C#. It is
intended to remain approachable and may later be extended for retro-game
compatibility. The repository currently contains its first runnable applications:
native macOS windows built on SDL3 that draw a GPU-rendered triangle and an
interactive, depth-tested cube scene.

## Current status

- **Implemented now:** .NET 10 solution, the `Nymeria.Platform` window API,
  `Nymeria.Core` with validated format-independent model data and a parser
  contract, `Nymeria.Graphics` with reusable Metal GPU device infrastructure and
  camera math, `examples/HelloTriangle`, the interactive `examples/HelloCube`,
  platform-independent unit tests, CI restore/build/test on macOS, Windows, and
  Linux, repository checks, and Dependabot updates for GitHub Actions and NuGet.
- **Planned later:** engine-level input, the engine loop, Wavefront OBJ and OMSI
  O3D parsers, additional graphics backends, and Windows/Linux rendering. See
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
```

A resizable native window titled `Nymeria` opens at 960 × 540. `HelloTriangle`
draws one red/green/blue triangle. `HelloCube` draws seven cubes at fixed
positions with perspective projection and depth testing, plus a free-fly camera.
Move with `W`/`A`/`S`/`D`, `Space`, and `Left Control`; hold `Shift` to go
faster; hold the right mouse button and move the mouse to look around. Close
either window with the red close button or the standard macOS quit shortcut
(`Cmd+Q`). The command exits with status code `0` on a normal shutdown.

The startup path prints an actionable error and exits with status code `1` if the
native SDL3 library is missing or mismatched, SDL initialization fails, the Metal
GPU backend is unavailable, the window cannot be created or claimed, or a shader
or pipeline cannot be created.

### GPU examples

Both examples use SDL3's GPU API on the Metal backend. There is no direct Metal
interop and no software fallback. `Nymeria.Graphics` provides the reusable GPU
device and the `Camera` math type; `Nymeria.Core` provides the model data used by
`HelloCube`; each example owns its renderer, shader, pipeline, input mapping, and,
for the cube scene, its depth target.

The shaders are `examples/HelloTriangle/Shaders/Triangle.metal` and
`examples/HelloCube/Shaders/Cube.metal`. They are embedded into their examples
and compiled from Metal Shading Language source by SDL at startup. No shader
compiler or Xcode build step is required.

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

Both examples skip rendering while minimized and recover when a drawable is
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

The tests cover `WindowOptions` defaults and validation, plus model-data
validation and parser-contract error semantics in `Nymeria.Core.Tests`. There are
deliberately no fake GUI tests: native window behavior is covered by the manual
smoke test below and is not claimed as a headless CI result.

## Manual smoke test

Follow [docs/SMOKE_TEST.md](docs/SMOKE_TEST.md). GUI behavior is validated
manually; it is not claimed as a headless CI result.

## Repository layout

```text
.github/        Repository checks and Dependabot configuration
docs/           Architecture, Git policy, and manual smoke test
src/core/       Nymeria.Core model data and parser contract
src/graphics/   Nymeria.Graphics reusable GPU infrastructure and camera math
src/platform/   Nymeria.Platform native window project
examples/       HelloTriangle and HelloCube runnable examples
tests/          Nymeria.Core.Tests and Nymeria.Platform.Tests unit tests
```

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Model data and parser contract](docs/MODEL_DATA.md)
- [Examples](examples/README.md)
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
