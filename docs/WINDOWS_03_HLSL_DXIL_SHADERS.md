# Windows W03 — HLSL/DXIL shaders for HelloTriangle

This document records the first shader path for SDL3's Direct3D 12 backend: HLSL
sources for `HelloTriangle`, build-time compilation to DXIL, and shader asset
selection by the device's shader format. It also records exactly what was and was
not verified.

W03 ports **one** example. Every other GPU example still ships MSL only and still
stops at shader creation on Windows; that is the W04 boundary, described at the
end.

## Platform, backend, and shader representation

| Host | SDL3 GPU backend | Requested shader format | Assets shipped | Entry points |
| --- | --- | --- | --- | --- |
| macOS | Metal (`metal`) | `SDL_GPUShaderFormat.MSL` | `Triangle.metal` source, compiled by SDL | `vertex_main`, `fragment_main` |
| Windows | Direct3D 12 (`direct3d12`) | `SDL_GPUShaderFormat.DXIL` | `Triangle.vert.dxil`, `Triangle.frag.dxil` | `VSMain`, `PSMain` |
| Linux | none | none | none | none |

The backend policy is unchanged from
[WINDOWS_02_D3D12_BACKEND.md](WINDOWS_02_D3D12_BACKEND.md). W03 consumes
`GpuDevice.ShaderFormat` – the format verified against the created device – and
supplies bytecode in that format. It does not re-decide the backend, and there is
still no direct Direct3D 12 interop, second binding, or fallback.

## HLSL source

`examples/HelloTriangle/Shaders/Triangle.hlsl` contains both stages. It mirrors
`Shaders/Triangle.metal`, which is unchanged byte for byte:

| MSL | HLSL |
| --- | --- |
| `vertex VertexOut vertex_main(VertexIn in [[stage_in]])` | `VertexOutput VSMain(VertexInput input)` |
| `fragment float4 fragment_main(VertexOut in [[stage_in]])` | `float4 PSMain(VertexOutput input) : SV_Target` |
| `float3 position [[attribute(0)]]` | `float3 position : TEXCOORD0` |
| `float3 color [[attribute(1)]]` | `float3 color : TEXCOORD1` |
| `float4 position [[position]]` | `float4 position : SV_Position` |

Compiled with `vs_6_0` and `ps_6_0`. The interleaved position/color vertex buffer,
the pipeline's two `Float3` attributes at locations 0 and 1, the triangle list, the
disabled culling, and the clear color are all unchanged, so the CPU-side contract
does not move between backends.

Entry point names are explicit and format-specific. `vertex_main`/`fragment_main`
are MSL function names and mean nothing to DXIL; `VSMain`/`PSMain` are the HLSL
names. Both are recorded in `ShaderProgram`, so no caller guesses.

### SDL3 D3D12 conventions consulted

The rules below come from the XML documentation of the pinned `SDL3-CS` 3.4.18
package, which carries the `SDL_gpu.h` comments for the exact native library the
repository restores. No convention was invented:

- **Shader model.** `SDL_GPUShaderFormat.DXIL` is documented as "DXIL SM6_0
  shaders for D3D12", so the profiles are `vs_6_0` and `ps_6_0`. `SDL_GPUShaderFormat.DXBC`
  (SM 5.1) exists but Nymeria does not use it.
- **Vertex input semantics.** "Shader semantics other than system-value semantics
  do not matter in D3D12 and for ease of use the SDL implementation assumes that
  non system-value semantics will all be `TEXCOORD`. If you are using HLSL as the
  shader source language, your vertex semantics should start at `TEXCOORD0` and
  increment like so: `TEXCOORD1`, `TEXCOORD2`, etc." The pipeline's attribute
  locations 0 and 1 therefore map to `TEXCOORD0` and `TEXCOORD1`.
- **Resource binding order.** "For DXBC and DXIL shaders, use the following
  register order. For vertex shaders: `(t[n], space0)` sampled textures, followed
  by storage textures, followed by storage buffers; `(s[n], space0)` samplers;
  `(b[n], space1)` uniform buffers. For pixel shaders: `(t[n], space2)` …
  `(b[n], space3)` uniform buffers." `HelloTriangle` declares no textures,
  samplers, or uniform buffers, so it declares no registers at all; the constants
  are recorded here so W04's ports follow the documented order instead of a
  guessed one.
- **Entry point.** `SDL_GPUShaderCreateInfo.Entrypoint` is passed to
  `SDL_CreateGPUShader` for every format. DXIL bakes the entry point into the
  container, so the name passed for DXIL repeats the compiled entry point rather
  than selecting one at runtime.

The binding's own surface confirms the native library supports this path: the
pinned Windows `SDL3.dll` contains the `direct3d12` driver name, the `DXIL`
format, and the SDL error strings "The provided shader code is not valid DXIL!"
and "D3D12: DXIL is not supported and DXBC is not being provided".

## Build integration

`examples/HelloTriangle/HelloTriangle.csproj` declares one `NymeriaHlslShader` item
per stage, supplies the resource wiring, and imports
`examples/Shared/DxilShaders.targets`, which provides the
`CompileHlslShadersToDxil` target and the compiler it runs:

```xml
<NymeriaHlslShader Include="Shaders/Triangle.hlsl" Profile="vs_6_0" EntryPoint="VSMain" OutputName="Triangle.vert.dxil" />
<NymeriaHlslShader Include="Shaders/Triangle.hlsl" Profile="ps_6_0" EntryPoint="PSMain" OutputName="Triangle.frag.dxil" />
```

which expands to:

```
dxc -T vs_6_0 -E VSMain -Fo obj/dxil/Triangle.vert.dxil Shaders/Triangle.hlsl
dxc -T ps_6_0 -E PSMain -Fo obj/dxil/Triangle.frag.dxil Shaders/Triangle.hlsl
```

- **Output location.** `obj/dxil/`, derived from
  `$(BaseIntermediateOutputPath)`, so bytecode is a build artifact and is never
  committed. `.gitignore` also ignores `*.dxil` so an accidental copy cannot be
  tracked; `.hlsl` sources are tracked normally.
- **Embedding.** Two conditional `EmbeddedResource` items add the generated files
  under `HelloTriangle.Shaders.Triangle.vert.dxil` and
  `HelloTriangle.Shaders.Triangle.frag.dxil`. They reuse the existing embedded
  resource convention rather than adding a file lookup at runtime.
- **Ordering.** The target runs `BeforeTargets="PrepareResourceNames"`, which is
  after `PrepareForBuild` and before the manifest resource names and the compiler
  invocation, so the bytes exist when the assembly is built. The resource items
  are declared statically rather than inside the target, so an up-to-date
  incremental build still embeds them.
- **Incrementality.** `Inputs`/`Outputs` name the HLSL source and both DXIL
  outputs. A no-change build skips the target; touching the source or deleting
  either output reruns both stages. A clean or rebuilt tree regenerates them with
  no manual copy step.
- **Compiler discovery.** `DxcPath` overrides everything and is validated: a path
  that does not exist fails the build with a message naming it. Otherwise the
  build uses a cached compiler, then one already on `PATH`, and finally
  provisions a pinned DXC release; see
  [WINDOWS_03_1_DXC_PROVISIONING.md](WINDOWS_03_1_DXC_PROVISIONING.md).
- **Platform gating.** `DxilShadersEnabled` is true only when `$(OS)` is
  `Windows_NT` and the build is not a design-time build. macOS builds neither run
  dxc nor reference a DXIL resource, so the MSL workflow and its "no shader
  compiler" property are preserved. Cross-building a Windows binary from macOS or
  Linux does not produce DXIL and is not supported.
- **No shader compiler dependency in the repository.** No toolchain is vendored
  into version control, no `.dxil` or compiler binary is tracked, and the compiler
  itself is provisioned and checksum-verified as described in
  [WINDOWS_03_1_DXC_PROVISIONING.md](WINDOWS_03_1_DXC_PROVISIONING.md).

### DXC version and acquisition

W03 originally required a manually installed compiler. W03.1 replaced that with
provisioning: the build downloads one pinned release
(**DXC 1.9.2609**, `dxc_2026_09_29.zip`, SHA-256
`ad31b1fc8443175d204f77a611fdb3ef2ec42759bdc2f1167368de24a4a7e7f1`), verifies it
before extracting, caches it under `%LOCALAPPDATA%\Nymeria\dxc`, and reuses it.
`DxcPath` and a `dxc` already on `PATH` still take precedence, so the manual
install documented here originally remains a supported path. Requirements: any
DXC release that supports shader model 6.0. The pin and the reproducible
acquisition procedure are recorded in
[WINDOWS_03_1_DXC_PROVISIONING.md](WINDOWS_03_1_DXC_PROVISIONING.md).

**CI.** A `windows-latest` job now provisions DXC on its first shader build, so
it no longer depends on `dxc` being on `PATH`. That path is unverified, and
[.github/workflows/repository-checks.yml](../.github/workflows/repository-checks.yml)
was not changed by W03 or W03.1.

## Runtime shader selection

Two small public types in `Nymeria.Graphics` own the mapping, so no renderer
branches on the operating system or backend:

- `ShaderProgram.Select(resourcePrefix, deviceFormat)` returns the vertex and
  fragment `ShaderAsset`s for a format. MSL resolves to one `.metal` resource with
  the MSL entry points; DXIL resolves to one `.vert.dxil` and one `.frag.dxil`
  with `VSMain`/`PSMain`. Any other format – SPIR-V, metallib, DXBC, none – throws
  an `InvalidOperationException` naming the format, the family, and what Nymeria
  does ship, instead of falling back.
- `ShaderAsset` carries the resource name, its bytecode format, and its entry
  point, and reads its bytes from the owning assembly. A missing resource throws
  an `InvalidOperationException` naming the format, the resource, and the
  assembly, and pointing at the build step that produces it.

`TriangleRenderer` selects the program once from `device.ShaderFormat`, reads the
code, and passes the asset's own format to
`GpuDevice.CreateShader(byte[], SDL.GPUShaderFormat, …)`, which still rejects a
format that does not equal the device's. MSL bytes therefore cannot be submitted
to a Direct3D 12 device, and DXIL bytes cannot be submitted to a Metal device. The
Metal source resource is read once and shared by both stages, as before.

Because `ShaderProgram` and `ShaderAsset` hold no SDL state, both are unit-tested
without a device, a GPU, or a native library.

## Automated verification

Environment: macOS 27.0.1 arm64, .NET SDK 10.0.402, Release configuration.

| Command | Result |
| --- | --- |
| `dotnet build Nymeria.slnx -c Release --no-incremental` | **0 warnings, 0 errors** |
| `dotnet test Nymeria.slnx -c Release --no-build` | **1115 passed, 2 skipped, 0 failed** (1098 before this task, plus 17 new) |
| `dotnet test tests/Nymeria.Graphics.Tests` | 62 passed, 0 skipped, 0 failed (45 before, plus 17) |

The 2 skipped tests are the pre-existing device-dependent audio checks.

New tests assert, without a GPU:

- DXIL selects two stage resources, `.vert.dxil` and `.frag.dxil`, with `VSMain`
  and `PSMain`, all reporting `SDL_GPUShaderFormat.DXIL`.
- MSL selects the single `.metal` resource for both stages with `vertex_main` and
  `fragment_main`.
- Neither format's program references the other's resource names or entry points.
- The two stages use distinct resources for DXIL and a shared resource for MSL.
- SPIR-V, metallib, DXBC, and "no format" are rejected with a message naming the
  format, the family, and the supported formats.
- A missing embedded resource is reported with its format, resource name, and
  assembly, and `ReadBytes` rejects a null assembly.

### Build integration checks

DXC is not installed on the macOS host, so the target was exercised with a
stand-in executable that parses `-T`, `-E`, and `-Fo` and writes a marker file.
This verifies the MSBuild wiring, not the HLSL:

| Check | Observed |
| --- | --- |
| Forced DXIL build (`-p:DxilShadersEnabled=true -p:DxcPath=…`) | target ran, wrote `obj/dxil/Triangle.vert.dxil` and `Triangle.frag.dxil` with the profiles `vs_6_0`/`ps_6_0` and entry points `VSMain`/`PSMain` |
| Compiler line | `csc` received `/resource:obj/dxil/Triangle.vert.dxil,HelloTriangle.Shaders.Triangle.vert.dxil` and the fragment equivalent |
| Rebuild with no changes | target skipped, **0** compiler invocations |
| After touching `Triangle.hlsl` | **2** invocations, both stages regenerated |
| After deleting one output | **2** invocations, both regenerated |
| `DxcPath` pointing at a missing file | build failed with `error : DxcPath is set to '/nope/dxc', but no file exists there…`, exit status 1 |
| Normal macOS build | target and both DXIL resources absent; the assembly embeds only `HelloTriangle.Shaders.Triangle.metal` |

**Not verified:** that this HLSL compiles, that dxc emits valid DXIL for it, and
that SDL and Direct3D 12 accept that bytecode. Those need DXC and a Direct3D 12
device.

## macOS regression verification

Run on the same macOS host, Release build, after the change:

| Check | Result |
| --- | --- |
| `HelloTriangle` launched and left running for 6 seconds | process alive, **empty stdout and stderr** |

`Program.Main` catches `InvalidOperationException` and exits with status code 1
when device creation, shader creation, or pipeline creation fails, so remaining
alive with no output is evidence that Metal was selected, the driver check passed,
MSL was in the device formats, `ShaderProgram` resolved the MSL resource,
`ReadBytes` found it, both MSL shaders were created from the unchanged source, and
the pipeline compiled.

**Not claimed:** no visual inspection was performed by this task, so triangle
color, proportions, resize behavior, and window close are covered by the existing
checklist in [SMOKE_TEST.md](SMOKE_TEST.md) rather than by W03.

## Windows manual verification — NOT VERIFIED

No Windows machine or Direct3D 12 device was available to this task, and the user
elected to run the Windows checks separately. None of the following was executed:

- `dxc` producing DXIL for `Shaders/Triangle.hlsl` on Windows.
- `SDL_GPUSupportsShaderFormats(DXIL, NULL)` and device creation reporting the
  `direct3d12` driver on real hardware.
- `SDL_CreateGPUShader` accepting the generated DXIL for both stages.
- Graphics pipeline creation, the draw call, and a **visibly rendered triangle**.
- Window resize and shutdown with error-free GPU resource disposal.

The expected Windows sequence is: device created with `direct3d12` and DXIL
support, both shaders created from DXIL, pipeline created, colored triangle drawn.
Anything that fails earlier is a W02 regression; a failure at `SDL_CreateGPUShader`
naming DXIL is the first genuinely new W03 integration point. If the bytecode is
rejected, the things to check, in order, are the profile (`vs_6_0`/`ps_6_0`), the
entry point names actually compiled, and whether the pipeline's vertex attribute
locations line up with `TEXCOORD0`/`TEXCOORD1`.

## Known limitations

- Only `HelloTriangle` was ported by W03. The `Cube`, `Model`, and `Material`
  families were still MSL-only at this point, so on Windows those examples created
  their device and then failed at `SDL_CreateGPUShader` with the W02
  format-mismatch message. **W04 has since ported them**; see
  [WINDOWS_04_RENDERING_COMPATIBILITY.md](WINDOWS_04_RENDERING_COMPATIBILITY.md).
- `HelloTriangle` on Windows has no uniform buffers or textures, so the documented
  `(b[n], space1)`/`(b[n], space3)` register mapping is recorded but not exercised.
- DXIL generation is limited to Windows builds. macOS never runs dxc, and a
  Windows binary cross-built on another platform will not embed DXIL.
- The `windows-latest` CI job builds the solution. Since W03.1 the build
  provisions DXC itself when it is not already available, so the job no longer
  needs a manual install; the first run downloads about 32 MB. That CI path is
  unverified and no workflow was changed.

## Open items

- **CI on `windows-latest`.** Provisioning removes the manual-install dependency,
  but the job itself was not run, and caching `NymeriaDxcCacheRoot` between runs
  would be a workflow-policy change that needs maintainer approval.
- **DXIL container acceptance by SDL.** Whether SDL's DXIL validation accepts the
  container produced by the pinned dxc on Windows is unverified.
- **No `dxil.dll` dependency is assumed.** The pinned native library contains no
  reference to `dxcompiler`/`dxil.dll`; DXIL loading is expected to be
  self-contained. Untested on Windows.

## Boundary between W03 and W04

**W03** owns the HLSL sources for `HelloTriangle`, the build-time dxc target, the
DXIL embedding, the format-to-asset selection, and the first Direct3D 12 shader
path. **W04** owns porting the remaining shader families – `Cube`, the shared
`Model` and `Material` shaders, and every example that uses them – including their
uniform buffers and the register spaces above. W04 did that in
[WINDOWS_04_RENDERING_COMPATIBILITY.md](WINDOWS_04_RENDERING_COMPATIBILITY.md),
reusing `DxilShaders.targets` and adding one small `.targets` file per shared
family.

## No shader compiler was introduced for macOS or for shader authoring

No HLSL toolchain runs on macOS, no `.metal` file changed, no MSL compilation step
changed, no DXIL binary is tracked, no DXC binary or archive is vendored, no MSBuild
file outside `examples/HelloTriangle/HelloTriangle.csproj` changed, and no
dependency was added or upgraded.
