# Windows W03.1 — Automatic DXC toolchain provisioning

This document records how a Windows build obtains Microsoft's DirectX Shader
Compiler (DXC) without the developer installing it, what is pinned and verified,
where the cache lives, how failures are reported, and what was actually tested.

W03.1 changes build-tool provisioning only. It adds no renderer, shader, or
runtime behavior, and it does not change the Metal path.

## What changed

Before this task, `dotnet build` on Windows required `dxc` on `PATH` or an
explicit `-p:DxcPath=…`, and failed the build with guidance when neither was
available. Now the build provisions a pinned, checksum-verified release itself
and reuses it afterwards.

| File | Change |
| --- | --- |
| `examples/Shared/DxilShaders.targets` | New shared file: pinned toolchain, provisioning, and the HLSL→DXIL target. |
| `examples/HelloTriangle/HelloTriangle.csproj` | Declares `NymeriaHlslShader` items and imports the shared target instead of carrying its own dxc resolution. |
| `README.md`, `docs/ARCHITECTURE.md`, `docs/SMOKE_TEST.md`, `examples/README.md`, `docs/WINDOWS_03_HLSL_DXIL_SHADERS.md` | Corrected the manual-install prerequisite. |

No dependency was added to any project, no package version changed, and no file
outside the two shader-build files changed behavior.

## Resolution order

The build resolves a compiler in this order and stops at the first success:

| Order | Source | Notes |
| --- | --- | --- |
| 1 | `DxcPath` property | Always wins. Validated: a path that does not exist fails the build naming it. |
| 2 | Provisioned cache | Reused when `current.txt` points at an extraction that still holds `bin\<arch>\dxc.exe`. |
| 3 | `dxc` on `PATH` | A compiler the developer already installed. Keeps offline machines with the Windows SDK working. |
| 4 | Provisioning | Downloads the pinned release, verifies its SHA-256, then extracts it into the cache. |

Every build that compiles shaders prints the resolved compiler and where it came
from, for example:

```text
Nymeria: compiling DXIL shaders with 'C:\Users\you\AppData\Local\Nymeria\dxc\1.9.2609\6b4d…\bin\x64\dxc.exe' (from cache; pinned DirectX Shader Compiler 1.9.2609).
```

## The pinned toolchain

| Item | Value |
| --- | --- |
| Release | DirectX Shader Compiler `v1.9.2609` (September 2026 release) |
| Asset | `dxc_2026_09_29.zip`, 32,287,592 bytes |
| URL | `https://github.com/microsoft/DirectXShaderCompiler/releases/download/v1.9.2609/dxc_2026_09_29.zip` |
| SHA-256 | `ad31b1fc8443175d204f77a611fdb3ef2ec42759bdc2f1167368de24a4a7e7f1` |

The digest was not taken on trust. The archive was downloaded from that URL, hashed
locally, and compared against the `digest` field GitHub's release API publishes for
the same asset; both values match. The pin lives in
`examples/Shared/DxilShaders.targets` as `NymeriaDxcVersion`, `NymeriaDxcArchiveName`,
`NymeriaDxcDownloadUrl`, and `NymeriaDxcArchiveSha256`.

Because the URL and digest identify one immutable release asset, changing either
one requires downloading and re-verifying the archive. Do not "fix" a hash mismatch
by editing the pinned value; the mismatch is the check working.

The archive contains `bin\{x64,x86,arm64}\dxc.exe` with `dxcompiler.dll` and
`dxil.dll` beside each. The build selects the directory matching
`%PROCESSOR_ARCHITECTURE%` (`AMD64`→`x64`, `ARM64`→`arm64`, `x86`→`x86`) and uses
`dxc.exe` from it, so the compiler and its DLLs stay together.

## Cache location and layout

```text
%LOCALAPPDATA%\Nymeria\dxc\                 (NymeriaDxcCacheRoot)
  1.9.2609\
    current.txt                             verified extraction path
    <run-guid>\
      bin\x64\dxc.exe                       used on x64 hosts
      bin\x64\dxcompiler.dll
      bin\x64\dxil.dll
      bin\arm64\…, bin\x86\…, inc\, lib\
  downloads\                                only while a download is in flight
```

- The cache is outside the repository, per user, and needs no administrator
  rights. It holds about 75 MB per provisioned version.
- `NymeriaDxcCacheRoot` overrides the location, for example
  `-p:NymeriaDxcCacheRoot=D:\tools\nymeria-dxc` or a shared CI cache directory.
- Deleting the version directory, or `current.txt`, forces a fresh, verified
  provision on the next build that compiles shaders. Deleting the whole cache is
  always safe.
- The compiler is not copied into the repository or into `bin/`, so nothing about
  the toolchain can be committed by accident.

## Provisioning sequence

The target runs only when a Windows build actually needs to compile shaders, and
it uses MSBuild's own tasks so no shell, PowerShell profile, or admin shell is
involved:

1. `MakeDir` creates the download directory.
2. `DownloadFile` fetches the pinned URL into
   `downloads\dxc_2026_09_29.zip.<run-guid>.zip`, with three retries.
3. `GetFileHash` computes SHA-256 over that exact file; the value is compared with
   the pin. MSBuild string comparison is case-insensitive, and the task reports
   upper-case hex.
4. On a mismatch, `Delete` removes the archive and the build stops. The bytes are
   never extracted and the compiler is never executed.
5. `Unzip` extracts into `1.9.2609\<run-guid>\`, a directory private to this build.
6. The build verifies `<run-guid>\bin\<arch>\dxc.exe` exists. If it does not, the
   archive and the extraction are removed and the build stops with a message
   naming the expected path.
7. Only then is `current.txt` written, publishing the extraction for reuse.
8. The downloaded archive is deleted; the extracted compiler stays cached.

### Concurrent builds

Two builds on the same machine can run this at the same time. Instead of a lock
file, each build uses its own guid-named archive and extraction directory, and
`current.txt` is written only after a verified, layout-checked extraction. So:

- Neither build can see the other's half-written archive or half-extracted tree.
- Both extractions are individually verified; whichever writes `current.txt` last
  wins, and both are valid.
- A build never executes a compiler from another build's in-progress directory.
- A crash mid-provision leaves an unreferenced `<run-guid>` directory and possibly
  a stale archive. Neither is ever used, because only the path in `current.txt` is
  read. Delete the version directory to reclaim the space.

This avoids a lock protocol that could deadlock or leave a stale lock behind, at
the cost of occasionally keeping one extra extraction directory after a race.

## Security and trust boundaries

The build downloads and executes a third-party binary, so the trust boundary is
explicit:

- **One source.** Only the pinned `https://github.com/microsoft/DirectXShaderCompiler/releases/download/<tag>/<asset>` URL is fetched. No feed, mirror, or redirect target is followed to a different host for content, and no version is resolved from the network at build time.
- **Verified before use.** The archive is hashed before extraction and the result is compared with the repository pin. An unverified archive is deleted, never extracted, never executed.
- **Extraction is confined.** `Unzip` writes only under the per-run cache directory. A traversal-style entry name cannot escape it (`ZipFile.ExtractToDirectory` rejects entries that resolve outside the destination), and the archive is checksum-pinned before extraction anyway.
- **No elevated rights.** Everything happens under `%LOCALAPPDATA%`. No installer, service, registry write, system-wide PATH change, or elevation prompt.
- **Nothing generated is tracked.** The cache is outside the repository; `obj/dxil/*.dxil` is already ignored by `obj/` and `*.dxil`.
- **No new runtime surface.** Nothing here loads at application runtime. The compiled DXIL is the only artifact that reaches the application, exactly as in W03.
- **Override stays available.** `DxcPath` bypasses provisioning entirely for users who must supply their own compiler; provisioning never overrides it.

The repository's [security checklist](SECURITY_CHECKLIST.md) "Dependencies and
automation" items apply: no dependency or version was added, `NuGetAudit` is
untouched, no workflow or repository setting changed, and no credential, binary,
or build output is committed.

## Diagnostics and troubleshooting

| Symptom | Meaning | Fix |
| --- | --- | --- |
| `Nymeria could not download …` | No network, DNS, proxy, or TLS failure, and no cache or `dxc` on `PATH`. | Connect and rebuild, install DXC and pass `-p:DxcPath=…`, or pre-populate `NymeriaDxcCacheRoot`. Nothing was executed. |
| `Nymeria rejected the downloaded … archive` | The download did not match the pinned SHA-256. | Usually a proxy, mirror, or security appliance rewriting the download. The archive was deleted. Retry; if it repeats, use `DxcPath`. |
| `Nymeria verified … but it did not contain bin\<arch>\dxc.exe` | The pinned archive layout changed, or extraction did not produce the expected tree. | Report it. The archive and extraction were removed. Build now with `DxcPath`. |
| `DxcPath is set to '…', but no file exists there` | The override points at a missing file. | Correct the path or clear the property. |
| Shaders not recompiled after an HLSL edit | MSBuild considered the outputs current. | Touch the `.hlsl` file, or delete `obj/dxil`. |
| Slow first build | The one-time 32 MB download. | Expected; later builds use the cache. Pre-seed `NymeriaDxcCacheRoot` on CI to avoid it. |

Useful switches:

```powershell
dotnet build -p:DxcPath="C:\tools\dxc\bin\x64\dxc.exe"       # skip provisioning entirely
dotnet build -p:NymeriaDxcCacheRoot="D:\ci\nymeria-dxc"      # relocate the cache
dotnet build -p:NymeriaDxcVersion=1.9.2609                   # only with a matching pin
```

A rebuild that has nothing to compile skips the target completely, so it neither
downloads nor probes for a compiler.

## Verification

Environment: macOS 27.0.1 arm64, .NET SDK 10.0.402. The Windows-only branch of
this logic cannot run here, so the provisioning steps were exercised through the
same MSBuild tasks on another platform, with the host-specific pieces substituted.

| Check | Method | Result |
| --- | --- | --- |
| Pinned digest is real | downloaded `dxc_2026_09_29.zip` from the pinned URL and hashed it locally | `ad31b1fc…e7f1`, identical to the value GitHub's release API reports for that asset |
| Archive layout | listed the archive's entries | `bin\x64\dxc.exe`, `bin\arm64\dxc.exe`, `bin\x86\dxc.exe` plus `dxcompiler.dll`/`dxil.dll`, 33 entries |
| Download + hash tasks | ran the same `DownloadFile`/`GetFileHash` calls in a scratch project | download succeeded, hash matched, and `GetFileHash` reports the digest through its `Items` output |
| Corrupted download | build with a deliberately wrong pin | build failed with the mismatch message, archive deleted, **no extraction directory created**, exit status 1 |
| Cache reuse | pre-populated cache plus `current.txt` | `from cache`, **0 downloads**, both stages compiled |
| Cache published only after validation | build whose extraction failed the layout check | `current.txt` **not written**, extraction and archive removed |
| `DxcPath` override | build with a missing path | build failed with the message naming that path, exit status 1 |
| No work needed | rebuild with unchanged inputs | target skipped, **0 compiler invocations** |
| Source changed | touch the HLSL, rebuild | 2 invocations, both stages regenerated |
| macOS never provisions | normal Release build | no provisioning, no `obj/dxil`, no compiler line, 0 warnings and 0 errors |
| Whole solution | `dotnet build Nymeria.slnx -c Release --no-incremental` | **0 warnings, 0 errors** |
| Tests | `dotnet test Nymeria.slnx -c Release --no-build` | **1115 passed, 2 skipped, 0 failed** |

### Verified later in CI

W06 extended the workflow trigger to the Windows development branch and observed a
successful `windows-latest` run (39831068582) in which the build downloaded
`dxc_2026_09_29.zip`, accepted its SHA-256, extracted it, and used it:

```text
Nymeria: downloading the DirectX Shader Compiler 1.9.2609 ... into C:\Users\runneradmin\AppData\Local\Nymeria\dxc.
Nymeria: compiling DXIL shaders with 'C:\Users\runneradmin\AppData\Local\Nymeria\dxc\1.9.2609\<guid>\bin\x64\dxc.exe' (from download; pinned DirectX Shader Compiler 1.9.2609).
Nymeria: compiling DXIL shaders with '...\<guid>\bin\x64\dxc.exe' (from cache; pinned DirectX Shader Compiler 1.9.2609).
```

That run also closes the extraction-layout question below. Because the build runs
projects in parallel, six extraction directories were created and the archive was
downloaded more than once before later projects reused the cache; the job still
finished in under two minutes. See
[WINDOWS_06_CI_STABILIZATION.md](WINDOWS_06_CI_STABILIZATION.md).

### Not verified here

- **The Windows extraction layout.** The archive stores entry names with
  backslashes (`bin\x64\dxc.exe`). On Windows a backslash is a path separator, so
  `Unzip` produces `bin\x64\dxc.exe` as nested directories; on this host the same
  names stayed literal single files, which is why the layout check above refused
  and removed the extraction. That check is therefore proven to gate on the real
  file. The Windows success path was not observed here, but W06's CI run did
  observe it (above).
- **A real `dxc` invocation.** No Windows machine or DXC binary was executed
  here; the compiler calls were exercised with a stand-in that accepts the same
  arguments.
- **Two builds provisioning at once.** The guid-per-run design was reviewed, not
  raced on real hardware.
- **Windows CI.** `windows-latest` provisions automatically on its first shader
  build. W06 verified this in run 39831068582 after extending the workflow trigger
  to the Windows branch; the original W03.1 push did not run it.

## macOS and Metal

Nothing in this file runs for a macOS build. `DxilShadersEnabled` is true only
when `$(OS)` is `Windows_NT` and the build is not a design-time build, so the
target — and with it every download, hash, and extraction step — is skipped
entirely. macOS still compiles the unchanged MSL source at startup with no shader
compiler, no download, and no new prerequisite.
