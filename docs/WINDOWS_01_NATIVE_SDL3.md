# Windows W01 — native SDL3 runtime foundation

This document records the native-package deployment and the window-only smoke
test added by Windows task W01, together with exactly which checks were executed
and which were not. It is a task record, not a claim of Windows rendering
support.

## Scope

W01 makes Nymeria build and open a basic SDL3 window on Windows by deploying the
native SDL3 runtime. It does not implement Direct3D 12, backend selection, or
shaders, and it does not change the macOS Metal path.

## Baseline

The branch was `Windows_DirectX` at `4fcc426`, the same commit as `main`, with a
clean working tree and no prior Windows work. W01 was therefore implemented from
scratch rather than by extending an existing Windows branch. Two recorded
baseline facts before the change:

| Check | Result |
| --- | --- |
| `dotnet build Nymeria.slnx -c Release` | 0 warnings, 0 errors |
| `dotnet test Nymeria.slnx -c Release --no-build` | 1082 passed, 2 skipped, 0 failed |
| Native assets in a `main` build output | `osx-arm64` and `osx-x64` only; no `SDL3.dll` |

That last row is the failure mode: `SDL3-CS` is the managed binding and ships no
native binary, so a Windows process had nothing to load and failed at the first
P/Invoke with `Unable to load DLL 'SDL3'` (`0x8007007E`).

## What changed

| File | Change |
| --- | --- |
| `src/platform/Nymeria.Platform/Nymeria.Platform.csproj` | Adds `SDL3-CS.Windows` 3.4.18 beside the existing `SDL3-CS` and `SDL3-CS.MacOS` references, and replaces the "macOS only" comment with the actual ownership rule. |
| `examples/HelloWindow/HelloWindow.csproj` | New window-only example. References `Nymeria.Platform` only, so it cannot touch the GPU layer. |
| `examples/HelloWindow/Program.cs` | New entry point: creates the window, prints reported sizes, pumps events, exits. `--smoke` runs a fixed 30 cycles with no user input. |
| `Nymeria.slnx` | Adds the new example project. |
| `README.md`, `examples/README.md`, `docs/ARCHITECTURE.md`, `docs/SMOKE_TEST.md` | Document Windows prerequisites, the native package set, the smoke command, and the remaining GPU limitation. |

`Nymeria.Platform` keeps sole ownership of the SDL3 native packages, exactly as
before. No project was created per operating system, no `SDL3.dll` is checked in
or copied by hand, and no custom native loader, P/Invoke, or `unsafe` code was
added.

## Package and asset resolution

`SDL3-CS.Windows` 3.4.18 exists on nuget.org and matches the pinned managed
`SDL3-CS` 3.4.18, so the binding and native runtime cannot skew. Restore resolved
it directly, which is stronger evidence than the package listing alone:

```text
src/platform/Nymeria.Platform/obj/project.assets.json
  SDL3-CS 3.4.18
  SDL3-CS.MacOS 3.4.18
  SDL3-CS.Windows 3.4.18
```

A portable Release build registers the Windows binaries as RID-scoped native
assets alongside the unchanged macOS ones. From
`examples/HelloWindow/bin/Release/net10.0/HelloWindow.deps.json`:

```text
runtimes/osx-arm64/native/libSDL3.dylib
runtimes/osx-arm64/native/libSDL3.0.dylib
runtimes/osx-x64/native/libSDL3.dylib
runtimes/osx-x64/native/libSDL3.0.dylib
runtimes/win-arm64/native/SDL3.dll
runtimes/win-x64/native/SDL3.dll
runtimes/win-x86/native/SDL3.dll
```

A RID-specific build flattens the matching binary beside the executable, so a
Windows run needs no manual copy. From
`dotnet build examples/HelloWindow/HelloWindow.csproj -c Release -r win-x64`:

```text
examples/HelloWindow/bin/Release/net10.0/win-x64/
  HelloWindow.exe
  SDL3-CS.dll
  SDL3.dll          PE32+ executable (DLL) x86-64, for MS Windows
```

`HelloWindow.deps.json` in that output lists `SDL3-CS.Windows/3.4.18` and
`runtimes/win-x64/native/SDL3.dll` as the resolved native asset, and no
`runtimes/` folder remains.

## Verification actually performed

Host: macOS 27.0.1 arm64, .NET SDK 10.0.402. **No Windows host was available
during this task.**

| # | Check | Result |
| --- | --- | --- |
| 1 | `dotnet build Nymeria.slnx -c Release` (macOS) | 0 warnings, 0 errors |
| 2 | `dotnet test Nymeria.slnx -c Release --no-build` | 1082 passed, 2 skipped, 0 failed — unchanged from baseline |
| 3 | `dotnet build examples/HelloWindow -c Release -r win-x64` | 0 warnings, 0 errors; `SDL3.dll` flattened beside the executable |
| 4 | Portable build registers `win-*` native assets | Yes, all three Windows RIDs in `deps.json` |
| 5 | `dotnet .../HelloWindow.dll --smoke` (macOS) | Passed; see the output below |

The macOS smoke run proves the new entry point is correct and is not
Windows-specific:

```text
host: macOS 27.0.1 (Arm64)
window: 960x540 logical | drawable 1920x1080
event pump: 30 cycles
window smoke test passed
```

## Not verified

- **No Windows process was started.** Native `SDL3.dll` loading, SDL video
  initialization, window creation, resizing, and input on Windows are
  unverified. Those are the Windows checks in
  [SMOKE_TEST.md](SMOKE_TEST.md#windows-window-only-smoke-test).
- **No Windows GPU behavior was verified in W01.** At the time of this task
  `GpuDevice.Create` still requested the Metal backend. W02 replaced that with a
  platform policy, so this statement has been superseded; see
  [WINDOWS_02_D3D12_BACKEND.md](WINDOWS_02_D3D12_BACKEND.md).
- **No CI run.** The `Repository checks` workflow triggers on `main` only, so no
  Actions result exists for this branch. The Windows and Linux CI legs are
  expected to keep compiling, because the new project is ordinary managed code
  that consumes the binding only, but that is an expectation here, not a result.

## Troubleshooting

| Symptom | Cause | Action |
| --- | --- | --- |
| `Unable to load DLL 'SDL3' (0x8007007E)` | `ERROR_MOD_NOT_FOUND`: no `SDL3.dll` in the output. | `dotnet restore` then `dotnet build`. Confirm `runtimes/win-<arch>/native/SDL3.dll` in the output, or use `dotnet build -r win-x64`. |
| `BadImageFormatException` | Architecture mismatch, for example an x64 `SDL3.dll` in an arm64 process. | Build for the running architecture with `-r win-x64` or `-r win-arm64`. |
| Window never appears | A display or session problem, not a missing library. | Check `SDL_GetError` output; the smoke mode still reports the window size when it succeeds. |

## macOS preservation

The macOS native package and its `osx-*` assets are unchanged, and both native
packages coexist because their assets are RID-scoped. `GpuDevice`, all shader
sources, all shader loaders, the renderers, the pipelines, and the audio path
were not modified. The macOS build and the full headless test suite still pass
with identical counts.

## Handoff to W02 — Direct3D 12 backend selection

W02 can now assume:

- `SDL3.dll` for `win-x64`, `win-arm64`, and `win-x86` is deployed by NuGet and
  resolves without manual copying.
- `examples/HelloWindow` is a GPU-free Windows path for separating native-runtime
  failures from GPU failures.
- The GPU backend was hardcoded to Metal in
  `src/graphics/Nymeria.Graphics/GpuDevice.cs` and only MSL shader assets
  existed. Backend selection was W02's scope and is now implemented; DXIL shader
  assets remain W03's scope.

Deliberately left untouched by W01: backend selection, shader formats, `.metal`
sources, `GpuDevice`, and every CI workflow.
