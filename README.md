# Nymeria

Nymeria is a small, understandable, cross-platform 3D engine written in C#. It is
intended to remain approachable and may later be extended for retro-game
compatibility. The repository is currently at its foundation stage: layout,
documentation, Git policy, and minimal GitHub automation exist, but no engine
code or .NET project exists yet.

## Current status

- **Implemented now:** monorepo layout, architecture and Git documentation,
  editor defaults, .NET-oriented ignore rules, repository checks, and Dependabot
  updates for GitHub Actions.
- **Planned later:** C# projects, `Nymeria.slnx`, windowing, rendering, input,
  the runtime loop, examples, and tests. See
  [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the authoritative distinction.

## Repository layout

```text
.github/        Repository checks and Dependabot configuration
docs/           Architecture and Git policy
src/core/       Future Nymeria.Core
src/graphics/   Future Nymeria.Graphics
src/platform/   Future Nymeria.Platform
examples/       Future runnable examples
tests/          Future automated tests
```

`src/*`, `examples/`, and `tests/` contain `.gitkeep` files because Git does not
track empty directories. They are organizational placeholders only; no C#
sources or project files exist yet.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Git guidelines](docs/GIT_GUIDELINES.md)
- [Agent instructions](AGENTS.md)

## Repository automation

The `Repository checks` GitHub Actions workflow runs on pushes and pull requests
to `main` and validates only the repository foundation. Build and test steps are
added once real .NET projects exist.

Dependabot checks GitHub Actions weekly and opens update pull requests. It does
**not** merge or deploy them; a maintainer reviews and merges each pull request.
NuGet updates are deferred until a `.csproj`, `.slnx`, or NuGet configuration
exists.

## License

Nymeria is licensed under the BSD 3-Clause License. See [LICENSE](LICENSE).
