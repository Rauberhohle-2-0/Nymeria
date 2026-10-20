# Nymeria

Nymeria is a small, understandable, cross-platform 3D engine written in C#. It is
intended to remain approachable and may later be extended for retro-game
compatibility. The repository currently contains its first runnable application:
a native macOS window built on SDL3. Rendering is deliberately not implemented.

## Current status

- **Implemented now:** .NET 10 solution, the `Nymeria.Platform` window API,
  `examples/HelloWindow`, repository checks, and Dependabot updates for GitHub
  Actions.
- **Planned later:** automated tests, .NET build/test steps in CI,
  `Nymeria.Core`, `Nymeria.Graphics`, input, the engine loop, and rendering. See
  [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the authoritative distinction.

## Prerequisites (macOS)

- macOS 11 or newer on Apple Silicon or Intel.
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

A resizable native window titled `Nymeria` opens at 960 × 540. The client area is
intentionally blank because this milestone has no renderer. Close the window with
the red close button or the standard macOS quit shortcut (`Cmd+Q`). The command
exits with status code `0` on a normal shutdown.

The startup path prints an actionable error and exits with status code `1` if the
native SDL3 library is missing or mismatched, SDL initialization fails, or the
window cannot be created.

## Tests

There is no test project yet. It is added with the platform-independent window
option tests.

## Manual smoke test

Follow [docs/SMOKE_TEST.md](docs/SMOKE_TEST.md). GUI behavior is validated
manually; it is not claimed as a headless CI result.

## Repository layout

```text
.github/        Repository checks and Dependabot configuration
docs/           Architecture, Git policy, and manual smoke test
src/core/       Future Nymeria.Core (placeholder)
src/graphics/   Future Nymeria.Graphics (placeholder)
src/platform/   Nymeria.Platform native window project
examples/       HelloWindow runnable example
tests/          Future automated tests (placeholder)
```

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Git guidelines](docs/GIT_GUIDELINES.md)
- [macOS smoke test](docs/SMOKE_TEST.md)
- [Agent instructions](AGENTS.md)

## Repository automation

The `Repository checks` GitHub Actions workflow runs on pushes and pull requests
to `main` and validates the repository foundation. Build and test steps for the
.NET solution are added once the test project exists.

Dependabot checks GitHub Actions weekly and opens update pull requests. The
`Dependabot auto-merge` workflow squash-merges a Dependabot pull request only
after `Repository checks` succeeds on that pull request's current head commit. It
does not deploy. The organization currently prevents GitHub Actions from
approving pull requests, so a Dependabot merge is recorded without an automated
approval review. Human pull requests remain manual. NuGet updates are added once
the .NET project set is complete.

## License

Nymeria is licensed under the BSD 3-Clause License. See [LICENSE](LICENSE).
