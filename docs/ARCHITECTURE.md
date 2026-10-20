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
- SDL3 windowing through the pinned `SDL3-CS` and `SDL3-CS.MacOS` packages.
- A `Repository checks` GitHub Actions workflow for repository foundations.
- Weekly Dependabot updates for GitHub Actions, auto-merged only after
  repository checks pass.
- Organizational directories kept in Git with `.gitkeep` files where empty.

### Planned later

The following are future work and are **not** implemented or approved in detail:

- `Nymeria.Core` and `Nymeria.Graphics`.
- A runnable `HelloWindow` example and automated tests.
- Build and test steps in CI once the example and tests exist.
- Input handling and an engine loop.
- A rendering abstraction and at least one graphics backend.

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
| `src/graphics/` | Rendering abstractions and graphics backends, eventually `Nymeria.Graphics`. |
| `src/platform/` | `Nymeria.Platform`: windowing, input, and operating-system integration. |
| `examples/` | Small runnable programs that demonstrate engine usage. |
| `tests/` | Automated unit and integration tests for the engine projects. |
| `docs/` | Authoritative project and architecture decisions. |
| `.github/` | Repository automation: checks and dependency updates. |

`src/platform/` contains the `Nymeria.Platform` project. `src/core/`,
`src/graphics/`, `examples/`, and `tests/` still contain `.gitkeep`
placeholders because they have no source code yet. Git does not track empty
directories, so these placeholders keep the intended layout visible in a fresh
clone without introducing speculative scaffolding.

## Windowing

`Nymeria.Platform` owns native window creation. Its public surface is deliberately
small:

- `WindowOptions` validates and carries the title and initial client size. The
  defaults are the title `Nymeria` and 960 × 540.
- `Window.Create` initializes SDL video and creates a visible, resizable native
  window.
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
involved. Task 02 validates only the macOS native runtime. The binding compiles
on other operating systems, but Windows and Linux native packages are added only
when those platforms are supported.

SDL initialization, window creation, event processing, and disposal must all run
on the process main thread, as macOS requires for its native windowing APIs.
There are no worker threads. `Window.ProcessEvents` blocks in
`SDL_WaitEventTimeout` instead of busy-spinning. One window per process is
supported in this version.

The window is not a renderer. Task 02 creates no SDL renderer, GPU device,
swapchain, or render loop, and the client area remains blank. A future
`Nymeria.Graphics` will own rendering against the window; neither project depends
on the other yet.

## Dependency direction

The following principle guides project references. Only the platform project
exists today.

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

`Nymeria.Platform` has no `Nymeria.Core` reference because `Nymeria.Core` does
not exist and the platform code has no demonstrated need for it. No interfaces or
abstractions are created to enforce the future direction yet. Adding them
speculatively would violate YAGNI.

## Technical choices

The following choices are implemented:

- **.NET SDK and target framework:** .NET 10, targeting `net10.0`. `global.json`
  pins SDK 10.0.402 with `latestPatch` roll-forward and no prerelease SDKs.
- **Solution format:** `Nymeria.slnx`, built with the `dotnet` CLI.
- **Windowing:** SDL3 3.4.18 through `SDL3-CS` 3.4.18 and the macOS native
  package `SDL3-CS.MacOS` 3.4.18.
- **External packages:** only the SDL3 binding and its macOS native runtime are
  approved. Automated test packages are added when the test project exists.

The following choices remain open:

- **Graphics API:** no API or backend has been selected. Vulkan, Metal, Direct3D,
  and OpenGL are possible directions only, not decisions.
- **`Nymeria.Core` and `Nymeria.Graphics`:** neither project exists yet, and
  neither is justified by Task 02.

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

`.github/workflows/repository-checks.yml` runs the `Repository checks` job on
pushes and pull requests to `main`. It verifies required files and directories,
resolves relative Markdown links, and performs basic Dependabot configuration
integrity checks. It uses read-only repository permissions. C# restore, build,
and test steps are added once the example and test projects exist.

Results appear on commits and pull requests, and in the repository's **Actions**
tab. The workflow can also be inspected with `gh run list` and
`gh run view <run-id>`.

Branch protection or GitHub rulesets are not enabled. Enabling them may conflict
with the policy of pushing small verified commits immediately, so it requires
maintainer approval. See [GIT_GUIDELINES.md](GIT_GUIDELINES.md).

### Dependabot

`.github/dependabot.yml` checks the `github-actions` ecosystem at `/` weekly.
Dependabot opens pull requests for available updates. It does not deploy
anything.

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
`Repository checks` run. Automatic merging trusts Dependabot's proposed action
updates; the checks validate repository foundations, not action security.

Branch protection or GitHub rulesets are not required for this auto-merge path
and are still not enabled. Enabling them is a separate workflow-policy decision
that requires maintainer approval. See [GIT_GUIDELINES.md](GIT_GUIDELINES.md).

NuGet updates are not configured yet. They are added once the .NET project set
is in place.

## Governance

The human maintainer owns architecture and scope. Changes to architecture,
dependencies, repository structure, or workflow policy require explicit
maintainer approval. Approved changes must update this document in the same
change. When implementation and documentation disagree, the documentation is
the baseline to discuss, not silently override.
