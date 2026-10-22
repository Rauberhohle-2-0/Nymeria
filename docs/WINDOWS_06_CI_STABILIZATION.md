# Windows W06 — CI, packaging, and stabilization

This document records the continuous-integration extension for the Windows
Direct3D 12 branch, what the Windows job actually verifies, the packaging and
native-dependency audit, and the evidence and open items to hand to the
independent audit.

**Scope of the verified result:** CI now builds and tests the solution on
`windows-latest`, provisions the pinned DirectX Shader Compiler without any manual
install, and compiles all four HLSL shader families to DXIL. What CI still cannot
do is render: hosted runners have no Direct3D 12 device and no interactive desktop.
Visual rendering, audio playback, input, and the W05 feature matrix remain manual
checks on real hardware.

## Workflow change

`.github/workflows/repository-checks.yml` previously ran only on pushes to `main`
and on pull requests targeting `main`, so the Windows job never built this branch.
That job already existed and already ran restore, build, and test on
`windows-latest`; the only change is the push branch list:

```yaml
on:
  push:
    branches:
      - main
      - Windows_DirectX
      - Windows
```

Nothing else changed: same jobs, same runner matrix, same pinned action SHAs, same
`permissions: contents: read`, same timeouts, same concurrency group, and no
repository setting, secret, or branch protection was touched. Both Windows branch
names are listed because the task refers to `Windows` while this checkout is
`Windows_DirectX`; an unused name is inert.

This is the one workflow-policy change W06 makes, and it is exactly the extension
the task requests. Cross-run caching, releases, signing, installers, and
deployment remain out of scope and unconfigured.

## The run

| Item | Value |
| --- | --- |
| Workflow | `Repository checks` |
| Run | [39831068582](https://github.com/Rauberhohle-2-0/Nymeria/actions/runs/39831068582) |
| Event / branch | `push` / `Windows_DirectX` |
| Commit | `4eaa75b` — `ci: run repository checks on the Windows Direct3D 12 branch` |
| Conclusion | **success** (all four jobs) |

| Job | Result | Duration |
| --- | --- | --- |
| `Repository checks` | success | 6 s |
| `.NET (windows-latest)` | success | 1 m 42 s |
| `.NET (macos-latest)` | success | 32 s |
| `.NET (ubuntu-latest)` | success | 39 s |

Runner details from the job log: Windows Server 2025, image `windows-2025-vs2026`
(2026-10-07), .NET SDK from `global.json` (10.0.402), `SDL3-CS` 3.4.18 restored
from NuGet.

## What the Windows job verifies

| Check | Evidence from the run |
| --- | --- |
| Clean restore and build | `dotnet restore`, then `dotnet build Nymeria.slnx --configuration Release --no-restore`: **Build succeeded, 0 Warning(s), 0 Error(s)** |
| No manual DXC install or PATH change | the runner has no DXC provisioned by the job; the build reports `Nymeria: downloading the DirectX Shader Compiler 1.9.2609 … This happens once per machine.` |
| Pinned, checksum-verified provisioning | first compiler line: `…AppData\Local\Nymeria\dxc\1.9.2609\<guid>\bin\x64\dxc.exe` (from download; pinned DirectX Shader Compiler 1.9.2609)`. The archive's SHA-256 must match the repository pin before extraction, so a mismatch would have failed the build. |
| Reuse after provisioning | later projects report `(from cache; pinned DirectX Shader Compiler 1.9.2609)` and use the same extraction directory |
| HLSL → DXIL compilation | 16 `Nymeria: compiling DXIL shaders` invocations (one per shader-consuming project) with no compiler diagnostic in the log; the build fails if any `dxc` invocation returns non-zero |
| Windows tests | **1120 passed, 2 skipped, 0 failed** across 14 test assemblies, matching macOS exactly |
| No GPU or desktop requirement | the job only restores, builds, and tests; it launches no window and creates no device |

The two skipped tests are `AudioDeviceIntegrationTests`, skipped by design on every
platform because they need a live playback device and the process main thread.

### HLSL compilation is now proven

W04 could not compile the HLSL: the host had no runnable DirectX Shader Compiler.
This run closes that specific gap. The pinned DXC 1.9.2609 compiled
`Cube.hlsl`, `Model.hlsl`, `Material.hlsl`, and `Triangle.hlsl` for the Windows
build, and the build embedded the generated DXIL under the logical names the
renderers request. **Not proven:** that SDL's DXIL validation and a Direct3D 12
driver accept the bytecode at runtime, which needs real hardware.

### Concurrent provisioning cost (observed, not fixed)

MSBuild builds projects in parallel and the target deliberately avoids a global
lock, so several projects can provision at the same time. This run shows the
designed trade-off in action: 6 distinct extraction directories were created and
the 32 MB archive was downloaded more than once, then later projects reused the
published cache. The job still finished in under two minutes. Serializing this
would change the build or workflow design, so it is documented rather than
"fixed"; cross-run caching would remove the cost entirely but is a workflow-policy
decision this task does not make.

## Native dependencies and packaging

| Check | Result |
| --- | --- |
| `SDL3-CS.Windows` supplies managed binding and native DLL | `SDL3-CS.dll` and `SDL3.dll` are both restored from NuGet; no manual copy exists anywhere in the repository |
| Supported Windows architectures | a portable Release build contains `runtimes/win-x64/native/SDL3.dll`, `runtimes/win-arm64/native/SDL3.dll`, and `runtimes/win-x86/native/SDL3.dll` alongside the macOS binaries |
| RID-specific output | cross-building `HelloWindow` for `win-x64`, `win-arm64`, and `win-x86` places exactly the matching `SDL3.dll` beside `HelloWindow.exe` in each output folder, with 0 warnings and 0 errors |
| Development output versus distribution | the portable tree is development output: it carries every RID so one build runs on the current machine. No publishable distribution, installer, or multi-RID packaging system exists, and W06 introduces none. |
| Generated DXIL availability | embedded as assembly resources under stable logical names (`…vert.dxil` / `…frag.dxil`), so no runtime file lookup or copy step exists; `ExampleShaderAssetTests` pins the names against `ShaderProgram` |
| Repository cleanliness | `git ls-files` contains no `.dxil`, `.dll`, `.dylib`, `.so`, `.exe`, or archive; `.gitignore` covers `*.dxil`, `bin/`, and `obj/` |

The RID-specific builds above were produced by cross-compilation on the macOS
audit host, so they prove asset resolution and layout, not execution. No Windows
binary was run by this task.

## Build integrity and failure behavior

| Check | Result |
| --- | --- |
| Clean build | CI starts from a fresh checkout and succeeds |
| Incremental build | a no-change build skips the DXIL target (0 compiler invocations); touching an HLSL source recompiles both stages (verified in W04) |
| Stale output cannot mask a failure | DXIL output lives in each project's `obj/dxil`, CI starts with none, and a missing output reruns the target |
| Checksum mismatch fails closed | re-verified locally with a deliberately wrong pin: the build stops with `Nymeria rejected the downloaded DirectX Shader Compiler archive…`, the archive is deleted, nothing is extracted, no compiler runs, no `.dxil` is produced, and the repository stays clean |
| Missing compiler fails with guidance | `DxcPath` pointing at a missing file fails naming that path; an unreachable download fails with the download message (W03.1 behavior, unchanged) |
| macOS never provisions | the macOS and Ubuntu jobs contain **0** downloads and **0** compiler lines, so the Metal path keeps its "no shader compiler" property |

## Verification matrix for W06

| Criterion | Status | Evidence |
| --- | --- | --- |
| Clean Windows checkout restores and builds without manual DXC or DLL copying | **PASS** | run 39831068582, `windows-latest`, 0 warnings / 0 errors |
| Windows automated tests pass in CI | **PASS** | 1120 passed, 2 skipped, 0 failed |
| macOS build/test jobs preserved and passing | **PASS** | `macos-latest` job success, 1120 passed |
| macOS rendering parity | **NOT RE-VERIFIED HERE** | W05 recorded macOS example runs; this task changed no rendering code and ran no visual check |
| GPU-independent tests run in CI | **PASS** | all 14 test assemblies execute on all three runners |
| Real Direct3D 12 visual correctness | **NOT RUN** | hosted runners have no GPU or desktop; the maintainer runs the W04/W05 hardware checks |
| Pinned DXC download fails closed, no Git pollution | **PASS** | CI checksum gate plus the local wrong-pin test |
| No generated DXIL, native DLL, cache, or archive committed | **PASS** | `git ls-files` audit |
| README and CI claims distinguish automated, manual, unverified | **PASS** | corrected in this task |
| No architecture changes, per-OS projects, new renderers, or Vulkan | **PASS** | only the workflow trigger and documentation changed |

## Manual Windows checks still required

CI cannot replace these, and none was performed by W06:

1. `dotnet build Nymeria.slnx --configuration Release` on a clean Windows checkout
   (CI proves this, but a local confirmation is cheap).
2. The W04 rendering checks: `HelloTriangle`, `HelloCube`, `HelloMaterials`, then
   the remaining GPU examples.
3. The W05 feature checks: scene, animation, picking, collision, LOD/culling,
   streaming, audio playback and shutdown, input and window lifecycle, benchmark,
   and format loaders.

[SMOKE_TEST.md](SMOKE_TEST.md) has the commands and acceptance observations, and
[WINDOWS_05_ENGINE_FEATURE_COMPATIBILITY.md](WINDOWS_05_ENGINE_FEATURE_COMPATIBILITY.md)
has the fillable matrix.

## Known limitations and deferred work

- **No rendering, audio, or input check exists in CI.** Hosted Windows runners have
  no Direct3D 12 device and no desktop session, and the workflow must not require
  one.
- **No cross-run DXC cache.** Each Windows run provisions the compiler and
  downloads the archive more than once when projects provision concurrently. A
  cache step would need an explicit workflow-policy decision.
- **Bytecode acceptance is unverified.** Compilation succeeds; SDL's DXIL
  validation and the driver's pipeline creation need hardware.
- **Linux remains headless-only.** The Ubuntu job builds and runs headless tests;
  there is no Vulkan backend and no SPIR-V assets, and none was added.
- **No release tooling.** No signing, installer, publishing, or deployment exists
  or was introduced.
- **Branch naming.** The task names the branch `Windows`; the repository branch is
  `Windows_DirectX`, and both names are in the trigger list.

## Audit handoff

| Item | Value |
| --- | --- |
| Branch | `Windows_DirectX` |
| Commits under review | `ed8a00d…4eaa75b` (W03.1 baseline through the CI change), plus the documentation commit that adds this file |
| Verified CI run | [39831068582](https://github.com/Rauberhohle-2-0/Nymeria/actions/runs/39831068582) — success, all four jobs, commit `4eaa75b` |
| Changed areas in W06 | `.github/workflows/repository-checks.yml` (push branch list only), `README.md`, `docs/SMOKE_TEST.md`, this document, and status corrections in the W03, W03.1, W04, and W05 documents |
| Automated evidence | Windows/macOS/Ubuntu restore, build, and 1120 passing tests; Windows DXC provisioning and HLSL compilation |
| Manual evidence | none added by W06; W05 recorded macOS headless and example runs |
| Unverified assumptions | a real Direct3D 12 device accepts the generated DXIL and renders correctly; audio plays and shuts down on a Windows device; input and window lifecycle behave on Windows; the Windows runner is not exercising the native `SDL3.dll` load path because no GUI process starts |
| Risks for the auditor | the workflow trigger is a policy change and should be reviewed as such; concurrent DXC provisioning is intentional but costs bandwidth; no automated window/GPU test exists on any platform |

No engine feature, shader, renderer, dependency, project boundary, or repository
setting was changed by W06.

## Files changed by W06

| File | Change |
| --- | --- |
| `.github/workflows/repository-checks.yml` | Push trigger extended to the Windows development branches; jobs unchanged. |
| `docs/WINDOWS_06_CI_STABILIZATION.md` | This document. |
| `README.md` | CI and Windows status now describe the verified build/test path and the remaining manual checks. |
| `docs/SMOKE_TEST.md` | Windows section no longer claims the HLSL has never been compiled. |
| `docs/WINDOWS_03_HLSL_DXIL_SHADERS.md`, `docs/WINDOWS_03_1_DXC_PROVISIONING.md` | Record the first successful Windows provisioning and compilation run. |
| `docs/WINDOWS_04_RENDERING_COMPATIBILITY.md` | HLSL compilation marked verified in CI; Windows rendering still unverified. |
| `docs/WINDOWS_05_ENGINE_FEATURE_COMPATIBILITY.md` | Windows headless suite marked PASS from CI; interactive rows stay open. |
