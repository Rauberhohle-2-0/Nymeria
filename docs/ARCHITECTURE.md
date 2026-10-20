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
- Root `.editorconfig` and `.gitignore` files.
- A `Repository checks` GitHub Actions workflow for repository foundations.
- Weekly Dependabot updates for GitHub Actions.
- Empty organizational directories kept in Git with `.gitkeep` files.

There is no engine implementation. No C# project, solution, package reference,
windowing layer, renderer, or runtime dependency has been added.

### Planned later

The following are future work and are **not** implemented or approved in detail:

- C# projects using the root namespace `Nymeria`: `Nymeria.Core`,
  `Nymeria.Graphics`, and `Nymeria.Platform`.
- The `Nymeria.slnx` solution file.
- A window, input handling, and an engine loop.
- A rendering abstraction and at least one graphics backend.
- Runnable examples and automated tests.
- Build and test steps in CI once projects exist.

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
| `src/platform/` | Windowing, input, and operating-system integration, eventually `Nymeria.Platform`. |
| `examples/` | Small runnable programs that demonstrate engine usage. |
| `tests/` | Automated unit and integration tests for the engine projects. |
| `docs/` | Authoritative project and architecture decisions. |
| `.github/` | Repository automation: checks and dependency updates. |

The source directories currently contain only `.gitkeep` files. Git does not
track empty directories, so these placeholders keep the intended layout visible
in a fresh clone without introducing source code or project files.

## Dependency direction

The following is a future design principle, not implemented code. It guides
project references when the C# projects are created.

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

No interfaces, abstractions, or projects are created to enforce this yet.
Adding them speculatively would violate YAGNI.

## Undecided technical choices

The following choices are deliberately open. Listing them here does not select
them.

- **.NET SDK and target framework:** no SDK version or target framework has been
  chosen.
- **Windowing:** no library or native API has been selected. Any candidate would
  require an architecture and dependency decision first.
- **Graphics API:** no API or backend has been selected. Vulkan, Metal, Direct3D,
  and OpenGL are possible directions only, not decisions.
- **External packages:** none are approved. Dependencies need a documented
  reason and maintainer approval.
- **Solution format:** `Nymeria.slnx` is the intended filename, but the file does
  not exist yet.
- **Build and test tooling:** deferred until actual projects exist.

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
integrity checks. It uses read-only repository permissions and does not build or
test C# code because no projects exist.

Results appear on commits and pull requests, and in the repository's **Actions**
tab. The workflow can also be inspected with `gh run list` and
`gh run view <run-id>`.

Branch protection or GitHub rulesets are not enabled. Enabling them may conflict
with the policy of pushing small verified commits immediately, so it requires
maintainer approval. See [GIT_GUIDELINES.md](GIT_GUIDELINES.md).

### Dependabot

`.github/dependabot.yml` checks the `github-actions` ecosystem at `/` weekly.
Dependabot opens pull requests for available updates; it does **not**
automatically merge, approve, or deploy them. A maintainer reviews and merges
each pull request. Automatic merging is not enabled and requires approval.

NuGet updates are intentionally not configured until a `.csproj`, `.slnx`, or
NuGet dependency configuration exists.

## Governance

The human maintainer owns architecture and scope. Changes to architecture,
dependencies, repository structure, or workflow policy require explicit
maintainer approval. Approved changes must update this document in the same
change. When implementation and documentation disagree, the documentation is
the baseline to discuss, not silently override.
