# Nymeria

Nymeria is a small, understandable, cross-platform 3D engine written in C#. It is
intended to remain approachable and may later be extended for retro-game
compatibility. The repository currently contains its first runnable application:
a native macOS window built on SDL3 that draws one GPU-rendered triangle.

## Current status

- **Implemented now:** .NET 10 solution, the `Nymeria.Platform` window API,
  `Nymeria.Graphics` with a Metal triangle renderer, `examples/HelloWindow`,
  platform-independent unit tests, CI restore/build/test on macOS, Windows, and
  Linux, repository checks, and Dependabot updates for GitHub Actions and NuGet.
- **Planned later:** `Nymeria.Core`, input, the engine loop, additional graphics
  backends, and Windows/Linux rendering. See
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
dotnet run --project examples/HelloWindow/HelloWindow.csproj
```

A resizable native window titled `Nymeria` opens at 960 × 540 and draws one
red/green/blue triangle on a dark background. Close the window with the red close
button or the standard macOS quit shortcut (`Cmd+Q`). The command exits with
status code `0` on a normal shutdown.

The startup path prints an actionable error and exits with status code `1` if the
native SDL3 library is missing or mismatched, SDL initialization fails, the Metal
GPU backend is unavailable, the window cannot be created or claimed, or a shader
or pipeline cannot be created.

### GPU triangle example

The triangle is rendered with SDL3's GPU API on the Metal backend. There is no
direct Metal interop and no software fallback. The shader lives at
`src/graphics/Nymeria.Graphics/Shaders/Triangle.metal`, is embedded into
`Nymeria.Graphics`, and is compiled from Metal Shading Language source by SDL at
startup. No shader compiler or Xcode build step is required.

The example draws a frame, then waits up to 100 ms for window events before
drawing again. Resizes and other events wake it immediately; idle redraws are
limited to about ten per second. The triangle is drawn in a centered square
viewport derived from the swapchain's physical pixels, so it keeps its
proportions when the window is resized and on high-DPI displays. Minimizing the
window skips rendering until a drawable is available again.

This path is macOS-only for now. Windows and Linux need their own native SDL3
packages and shader formats; see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
GPU output is validated manually with [docs/SMOKE_TEST.md](docs/SMOKE_TEST.md),
not in CI.

## Tests

Run the platform-independent unit tests:

```bash
dotnet test Nymeria.slnx
```

The tests cover `WindowOptions` defaults and validation. There are deliberately
no fake GUI tests: native window behavior is covered by the manual smoke test
below and is not claimed as a headless CI result.

## Manual smoke test

Follow [docs/SMOKE_TEST.md](docs/SMOKE_TEST.md). GUI behavior is validated
manually; it is not claimed as a headless CI result.

## Repository layout

```text
.github/        Repository checks and Dependabot configuration
docs/           Architecture, Git policy, and manual smoke test
src/core/       Future Nymeria.Core (placeholder)
src/graphics/   Nymeria.Graphics Metal triangle renderer
src/platform/   Nymeria.Platform native window project
examples/       HelloWindow runnable example
tests/          Nymeria.Platform.Tests unit tests
```

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Git guidelines](docs/GIT_GUIDELINES.md)
- [macOS smoke test](docs/SMOKE_TEST.md)
- [Agent instructions](AGENTS.md)

## Repository automation

The `Repository checks` GitHub Actions workflow runs on pushes and pull requests
to `main`. One job validates the repository foundation, including Markdown links,
tracked build output, and Dependabot configuration. A second job restores, builds,
and runs the headless unit tests on macOS, Windows, and Linux. The GUI and GPU
output are not launched or validated in CI; window and triangle behavior is
covered by the manual smoke test.

Dependabot checks GitHub Actions and NuGet weekly and opens update pull requests.
The existing `Dependabot auto-merge` workflow squash-merges a Dependabot pull
request only after `Repository checks` succeeds on that pull request's current
head commit. It does not deploy. The organization currently prevents GitHub
Actions from approving pull requests, so a Dependabot merge is recorded without
an automated approval review. Human pull requests remain manual.

## License

Nymeria is licensed under the BSD 3-Clause License. See [LICENSE](LICENSE).
