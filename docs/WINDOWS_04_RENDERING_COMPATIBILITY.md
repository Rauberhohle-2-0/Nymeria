# Windows W04 — Direct3D 12 rendering compatibility

This document records Windows task W04: porting the existing Cube, Model, and
Material shader families to HLSL/DXIL so the established rendering features reach
direct parity with the macOS Metal path, and exactly what was and was not
verified.

The implementation is complete and the macOS path is re-verified. **The Windows
runtime path is not verified**: this task ran on a macOS arm64 host with no
Windows machine, no Direct3D 12 device, and no runnable DirectX Shader Compiler,
so no HLSL was compiled to DXIL, no DXIL was submitted to SDL, and no example was
looked at on Windows. W04 is therefore **partially verified**, not complete. The
evidence and the remaining acceptance steps are listed below.

**Update (W06).** The pinned DirectX Shader Compiler now compiles this HLSL on the
`windows-latest` CI job, which builds and tests the whole solution on every push to
this branch. That closes the "HLSL has never been compiled" gap below, but it does
not render anything: shader acceptance by a driver and every visual check still
need Windows hardware. See
[WINDOWS_06_CI_STABILIZATION.md](WINDOWS_06_CI_STABILIZATION.md).

## Baseline

| Item | Value |
| --- | --- |
| Branch | `Windows_DirectX` (the task text says `Windows`; the checkout is `Windows_DirectX`, which carries W01–W03) |
| Baseline commit | `ed8a00d` — `build: provision the pinned DirectX Shader Compiler automatically` |
| Host | macOS 27.0.0 arm64, .NET SDK 10.0.402 |
| Pinned SDL3 / binding | `SDL3-CS` 3.4.18 with `SDL3-CS.MacOS` and `SDL3-CS.Windows` 3.4.18 |
| Shader toolchain | DirectX Shader Compiler 1.9.2609, provisioned by `examples/Shared/DxilShaders.targets` |

W01–W03 were inspected before editing: native SDL3 deployment
([WINDOWS_01_NATIVE_SDL3.md](WINDOWS_01_NATIVE_SDL3.md)), backend selection
([WINDOWS_02_D3D12_BACKEND.md](WINDOWS_02_D3D12_BACKEND.md)), the `HelloTriangle`
HLSL/DXIL path and the format-to-asset mapping
([WINDOWS_03_HLSL_DXIL_SHADERS.md](WINDOWS_03_HLSL_DXIL_SHADERS.md)), and DXC
provisioning ([WINDOWS_03_1_DXC_PROVISIONING.md](WINDOWS_03_1_DXC_PROVISIONING.md)).

W03 documents its own Windows run as unverified, and this host cannot verify it
either. W03's implementation is present and its macOS half re-ran here, so W04
proceeded on that basis rather than treating "unverified on Windows" as the
incomplete-implementation blocker the task describes.

## Shader family inventory

There are four shader families in the repository. W03 ported the first; W04 ports
the other three, and nothing else needed a shader.

| Family | MSL source (unchanged) | HLSL source (new) | Used by |
| --- | --- | --- | --- |
| Triangle | `examples/HelloTriangle/Shaders/Triangle.metal` | `examples/HelloTriangle/Shaders/Triangle.hlsl` (W03) | `HelloTriangle` |
| Cube | `examples/HelloCube/Shaders/Cube.metal` | `examples/HelloCube/Shaders/Cube.hlsl` | `HelloCube` |
| Model | `examples/Shared/Shaders/Model.metal` | `examples/Shared/Shaders/Model.hlsl` | `HelloOBJ`, `HelloO3D`, `HelloX`, `HelloScene` |
| Material | `examples/Shared/Shaders/Material.metal` | `examples/Shared/Shaders/Material.hlsl` | `HelloMaterials`, `HelloLighting`, `HelloCulling`, `HelloLOD`, `HelloStreaming`, `HelloPicking`, `HelloAnimation`, `HelloAudio`, `HelloBenchmark`, `HelloCollision` |

Rendering features covered by those families, and therefore by this port: cube and
scene transforms and camera projection; OBJ, O3D, and text `.x` model
presentation through their unchanged loaders; textured and untextured materials
with UVs, normals, and sampled RGBA; sampler state (mipped and single-level);
ambient and directional Lambert lighting with the CPU-built inverse-transpose
normal matrix; depth testing; disabled back-face culling; winding order;
alpha-mask discard; straight source-over blending with back-to-front ordering; and
the shared multi-object draw paths. No lighting model, post-processing, PBR,
shadow, format parser, scene rule, or CPU math changed.

There is no second renderer: every renderer still uses `GpuDevice`,
`ShaderProgram`, and `ShaderAsset`, and none of them names an operating system or
a shader file extension.

### Asset and entry-point mapping

`ShaderProgram.Select(resourcePrefix, device.ShaderFormat)` owns the mapping, so
each renderer passes only its family prefix:

| Family prefix | MSL resource | MSL entry points | DXIL resources | DXIL entry points | Vertex/fragment uniform blocks | Fragment samplers |
| --- | --- | --- | --- | --- | --- | --- |
| `HelloTriangle.Shaders.Triangle` | `Triangle.metal` | `vertex_main` / `fragment_main` | `Triangle.vert.dxil`, `Triangle.frag.dxil` | `VSMain` / `PSMain` | 0 / 0 | 0 |
| `HelloCube.Shaders.Cube` | `Cube.metal` | `vertex_main` / `fragment_main` | `Cube.vert.dxil`, `Cube.frag.dxil` | `VSMain` / `PSMain` | 1 / 0 | 0 |
| `Nymeria.Examples.Shaders.Model` | `Model.metal` | `vertex_main` / `fragment_main` | `Model.vert.dxil`, `Model.frag.dxil` | `VSMain` / `PSMain` | 1 / 0 | 0 |
| `Nymeria.Examples.Shaders.Material` | `Material.metal` | `vertex_main` / `fragment_main` | `Material.vert.dxil`, `Material.frag.dxil` | `VSMain` / `PSMain` | 1 / 1 | 1 |

Those counts are the `uniformBufferCount` and `samplerCount` values the renderers
already passed to `GpuDevice.CreateShader`, so they are unchanged and now apply to
DXIL as well. The HLSL files are compiled as `vs_6_0` and `ps_6_0`, the profiles
W02/W03 selected for `SDL_GPUShaderFormat.DXIL` ("DXIL SM6_0 shaders for D3D12").

### Vertex layouts and pipeline state (unchanged on both backends)

| Family | Vertex stride | Attributes | Primitive / culling | Depth |
| --- | --- | --- | --- | --- |
| Cube | 24 bytes | `Float3` position at 0, `Float3` color at 1 | triangle list, cull none, front face counter-clockwise | tested and written, `Less`, cleared to 1.0 |
| Model | 24 bytes | `Float3` position at 0, `Float3` color at 1 | triangle list, cull none | tested and written, `Less` |
| Material | 32 bytes | `Float3` position at 0, `Float3` normal at 1, `Float2` uv at 2 | triangle list, cull none | opaque/mask pipeline tested and written; blended pipeline tested, not written |

The HLSL sources declare `TEXCOORD0`, `TEXCOORD1`, and `TEXCOORD2` in that order,
which is the only mapping SDL3 supports for D3D12 vertex attributes. No pipeline
state changed in this task.

## SDL3 Direct3D 12 binding contract

The rules below are quoted from SDL3's own documentation for
`SDL_CreateGPUShader`, which is the contract of the pinned 3.4.18 native library
the repository restores (`SDL_gpu.h` in SDL `release-3.4.18`, also published at
<https://wiki.libsdl.org/SDL3/SDL_CreateGPUShader>). No register or space was
guessed.

For DXBC/DXIL:

- Vertex shaders: `(t[n], space0)` sampled textures, storage textures, and storage
  buffers; `(s[n], space0)` samplers; `(b[n], space1)` uniform data.
- Fragment ("pixel") shaders: `(t[n], space2)` sampled textures, storage textures,
  and storage buffers; `(s[n], space2)` samplers; `(b[n], space3)` uniform data.
- The first resource in a register set is index 0, indices are consecutive with no
  gaps, sampled textures come first in `t` in SDL sampler-bind order, and sampler
  objects follow the same order.
- "Shader semantics other than system-value semantics do not matter in D3D12 and
  for ease of use the SDL implementation assumes that non system-value semantics
  will all be `TEXCOORD`. If you are using HLSL as the shader source language, your
  vertex semantics should start at `TEXCOORD0` and increment."

Copyright (C) 1997-2026 Sam Lantinga; zlib license. Quoted as the technical
contract, not as project documentation.

Applied per family:

| Family | Vertex shader registers | Pixel shader registers |
| --- | --- | --- |
| Cube | `cbuffer Uniforms : register(b0, space1)` | none |
| Model | `cbuffer Uniforms : register(b0, space1)` | none |
| Material | `cbuffer VertexUniforms : register(b0, space1)` | `cbuffer FragmentUniforms : register(b0, space3)`, `Texture2D baseColorTexture : register(t0, space2)`, `SamplerState baseColorSampler : register(s0, space2)` |

Each family pushes exactly one vertex uniform block at slot 0 and (Material only)
one fragment uniform block at slot 0, matching `SDL_PushGPUVertexUniformData` and
`SDL_PushGPUFragmentUniformData` slot 0, and binds one fragment sampler at slot 0,
matching `SDL_BindGPUFragmentSamplers(renderPass, 0, …)`. The MSL side keeps
`[[buffer(0)]]`, `[[texture(0)]]`, and `[[sampler(0)]]`, so both backends read the
same slots.

### Matrix convention

The CPU still composes `model * view * projection` with `System.Numerics` row
vectors and uploads the 64 bytes unchanged, and the normal matrix is still the
CPU-built inverse transpose. `System.Numerics` stores rows contiguously, so those
bytes are a row-major matrix.

An HLSL cbuffer matrix is column-major by default, which would read those same
bytes as the transpose; `mul(m, v)` with column vectors then equals `v * m` in C#
— exactly the rule the MSL shaders rely on. Each HLSL matrix is therefore declared
`column_major` **explicitly**, so the correspondence does not depend on a compiler
default or on `-Zpc`/`-Zpr`:

```hlsl
cbuffer Uniforms : register(b0, space1)
{
    column_major float4x4 mvp;
};
...
output.position = mul(mvp, float4(input.position, 1.0));
```

No transpose, y-flip, depth remap, or coordinate-system change was introduced. The
projection already maps depth to `[0, 1]`, which both Metal and Direct3D 12 use,
and both treat texture coordinates with a top-left origin, so the same UVs,
sampling, and depth comparison state apply unchanged. Culling is disabled in every
pipeline, so winding cannot differ between the backends.

## Platform-specific differences

None were needed. The HLSL is a direct transcription of the MSL: same inputs,
same uniform layout and ordering, same lighting equation, same alpha rules, same
sampling. No `.metal` file changed, no CPU-side rendering code changed beyond
selecting assets through `ShaderProgram`, and no pipeline, buffer, texture,
sampler, depth, blend, or ordering behavior changed.

The only backend-visible differences are the ones the formats require:

| Concern | MSL | DXIL | Consequence |
| --- | --- | --- | --- |
| Stage code | one source resource, both stages | one bytecode blob per stage | `ShaderProgram` already returns one or two resources |
| Entry point names | `vertex_main` / `fragment_main` | `VSMain` / `PSMain` | carried by `ShaderAsset.EntryPoint` |
| Vertex semantics | `[[attribute(n)]]` | `TEXCOORDn` | attribute locations 0–2 map one to one |
| Uniform binding | `[[buffer(0)]]` | `register(b0, space1/space3)` | both read push slot 0 |
| Texture binding | `[[texture(0)]]`, `[[sampler(0)]]` | `t0/s0` in space2 | both read fragment sampler slot 0 |
| Mask discard | `discard_fragment()` | `discard` | same depth-writing pipeline |
| Matrix storage | column-major, read as transpose | `column_major`, read as transpose | same bytes, same result |

## Compiler and build integration

HLSL is compiled **at build time** to DXIL with the toolchain W03 established; no
shader is compiled at runtime and no `.dxil` file is committed. Invocation per
stage, unchanged from W03:

```text
dxc -T vs_6_0 -E VSMain   -Fo obj/dxil/<Family>.vert.dxil <source>.hlsl
dxc -T ps_6_0 -E PSMain   -Fo obj/dxil/<Family>.frag.dxil <source>.hlsl
```

What W04 changed is that the wiring now serves more than one project:

| File | Role |
| --- | --- |
| `examples/Shared/ModelShaders.targets` | New: declares the Model family's two DXIL embedded resources and two compile items, then imports `DxilShaders.targets`. Imported by `HelloOBJ`, `HelloO3D`, `HelloX`, and `HelloScene`. |
| `examples/Shared/MaterialShaders.targets` | New: the same for the Material family. Imported by the ten material examples. |
| `examples/HelloCube/HelloCube.csproj` | Declares its own Cube family the same way `HelloTriangle.csproj` declares Triangle. |
| `examples/Shared/DxilShaders.targets` | Unchanged: the pinned toolchain, provisioning, and the compile target. |

Both shared files gate on the same `DxilShadersEnabled` and
`DxilShaderOutputDirectory` properties W03 defined, so:

- **Windows builds** compile the family once per consuming project into that
  project's own `obj/dxil/`, and embed it under the family's logical name.
- **macOS (and any non-Windows) build** declares no DXIL resource and runs no
  compiler, so Metal keeps its "no shader compiler" property.
- **Incrementality** is the W03 target's: a build with nothing to do runs no
  compiler, and editing an HLSL source or deleting an output recompiles both
  stages of that family in that project.
- **Failure diagnostics** are W03's: a missing `DxcPath`, a failed or
  checksum-mismatched download, or a rejected archive stops the build with the
  message naming the cause. `ShaderAsset.ReadBytes` still reports a missing
  embedded resource by format, resource name, and assembly, and
  `ShaderProgram.Select` still refuses a format Nymeria ships no assets for.
- **Same DXIL contract as W03:** `CreateShader` receives the asset's own format
  and still rejects a format that differs from the device's, so MSL bytes cannot
  reach Direct3D 12 and DXIL bytes cannot reach Metal.

## Automated verification

Environment: macOS 27.0.0 arm64, .NET SDK 10.0.402, Release configuration, after
the change.

| Command | Result |
| --- | --- |
| `dotnet build Nymeria.slnx -c Release --no-incremental` | **0 warnings, 0 errors** |
| `dotnet test Nymeria.slnx -c Release --no-build` | **1120 passed, 2 skipped, 0 failed** (1115 before this task, plus 5) |
| `dotnet test tests/Nymeria.Graphics.Tests -c Release` | 67 passed, 0 skipped, 0 failed (62 before, plus 5) |

The 2 skipped tests are the pre-existing device-dependent audio checks.

New GPU-free tests in `tests/Nymeria.Graphics.Tests/ExampleShaderAssetTests.cs`
assert, for Cube, Model, and Material:

- each declaration names exactly one `vs_6_0`/`VSMain` and one
  `ps_6_0`/`PSMain` stage, with the entry points `ShaderProgram` resolves;
- the compiled output of each stage is embedded under the exact logical name
  `ShaderProgram` looks up, so a renderer cannot receive Metal-only assets;
- the `Include` of each stage resolves to the family's HLSL source;
- each HLSL source defines both entry points, writes `SV_Position` and
  `SV_Target`, and declares the register spaces listed above;
- the Cube project still embeds its MSL source;
- every example that embeds `Shaders/Model.metal` imports the model targets, and
  every example that embeds `Shaders/Material.metal` imports the material targets
  (with the expected counts, so a rename cannot silently turn the check into a
  no-op).

### Build-integration checks

DXC cannot run on this host: the pinned release ships Windows and Linux x86-64
binaries, and this machine is macOS arm64. The MSBuild wiring was therefore
exercised with a stand-in executable that parses `-T`, `-E`, `-Fo` and writes a
marker file. This verifies the wiring, **not** the HLSL:

| Check | Observed |
| --- | --- |
| Forced build (`-p:DxilShadersEnabled=true -p:DxcPath=…`) for `HelloCube` | ran `vs_6_0 VSMain` and `ps_6_0 PSMain` on `Shaders/Cube.hlsl`, wrote `obj/dxil/Cube.vert.dxil` and `Cube.frag.dxil` |
| Forced build for `HelloOBJ` and `HelloScene` | both compiled `Shared/Shaders/Model.hlsl`, each embedding `Nymeria.Examples.Shaders.Model.vert.dxil` / `.frag.dxil` |
| Forced build for `HelloMaterials` | compiled `Shared/Shaders/Material.hlsl`, embedding `Nymeria.Examples.Shaders.Material.vert.dxil` / `.frag.dxil` |
| Embedded names | `strings` on each produced assembly shows exactly the logical names the renderers request |
| Rebuild with no changes | target skipped, **0** compiler invocations |
| After touching `Material.hlsl` | **2** invocations, both stages regenerated |
| Normal macOS rebuild | target absent, `obj/dxil` not regenerated, assembly embeds only `Nymeria.Examples.Shaders.Material.metal` |
| `git ls-files "*.dxil"` | **0** tracked files |

**Not verified here:** that these three HLSL sources compile, that the pinned dxc
produces valid DXIL for them, and that SDL and Direct3D 12 accept that bytecode.
This host has no usable HLSL compiler: DXC publishes no macOS binary for 1.9.2609,
and the HLSL front-end in this host's clang cannot parse the sources without DXC's
standard-library headers, which the release archive does not contain.

**Superseded by W06 for compilation only:** the CI run 39831068582 downloaded the
pinned DXC and compiled all four families on `windows-latest` with no compiler
diagnostic, so the HLSL is known to compile and produce DXIL. SDL's acceptance of
that bytecode and Direct3D 12 pipeline creation remain unverified.

## Windows manual smoke test — NOT RUN

No Windows machine and no Direct3D 12 device were available, and no HLSL was
compiled here. The user runs Windows checks separately. **None of the rows below
were executed by this task**, and a successful build is not evidence for any of
them.

| Example | Purpose | Result |
| --- | --- | --- |
| `HelloTriangle` | W03 baseline regression | **Not run** |
| `HelloCube` | Cube geometry, transforms, depth | **Not run** |
| `HelloOBJ` | OBJ model presentation | **Not run** |
| `HelloO3D` | O3D model presentation | **Not run** |
| `HelloX` | Text `.x` model presentation | **Not run** |
| `HelloMaterials` | Textures, UVs, tint, mask, blend order | **Not run** |
| `HelloLighting` | Ambient and directional lighting, normals | **Not run** |
| `HelloScene` | Shared scene renderer, shared mesh cache | **Not run** |
| `HelloCulling` | Large shared-mesh draw count, culling | **Not run** |
| `HelloLOD` | Mesh variants and distance culling | **Not run** |
| `HelloStreaming` | Chunk load/unload and GPU cache pruning | **Not run** |
| `HelloPicking` | Picking with the material highlight | **Not run** |
| `HelloCollision` | Collision example through the material renderer | **Not run** |
| `HelloAnimation` | Animated transforms through the material renderer | **Not run** |
| `HelloAudio` | Audio example's window and markers | **Not run** |
| `HelloBenchmark` | Steady-state draw path | **Not run** |

Transparency and render ordering have no dedicated example; they are covered by
`HelloMaterials` (masked pane and three ordered blended panes) and its
`Transparency checks` T1–T7 in [SMOKE_TEST.md](SMOKE_TEST.md). Those are also
**Not run** on Windows.

Expected Windows sequence, in order: the build compiles each family's HLSL to
DXIL; `GpuDevice` creates a `direct3d12` device that reports DXIL; each renderer
selects its DXIL assets and `SDL_CreateGPUShader` accepts both stages with the
documented register spaces; the pipeline builds; the example draws. If the
bytecode is rejected, the things to check, in order, are the profile
(`vs_6_0`/`ps_6_0`), the entry points actually compiled, the `TEXCOORDn`
semantics against the pipeline's attribute locations, and the register
spaces/spaces above. A failure before shader creation is a W02 regression, not a
W04 shader defect.

## macOS regression verification

Run on the same macOS host, Release build, after the change. Each example was
started from its output directory, left running for five seconds, and terminated;
`HelloBenchmark` was allowed to exit on its own.

| Example | Observed | Status |
| --- | --- | --- |
| `HelloCube` | process alive for 5 s, **empty stdout and stderr** | Pass |
| `HelloOBJ` | process alive, **empty output** | Pass |
| `HelloO3D` | process alive, **empty output** | Pass |
| `HelloX` | process alive, **empty output** | Pass |
| `HelloScene` | startup banner printed, process alive | Pass |
| `HelloMaterials` | startup banner and generated-checkerboard notice, process alive | Pass |
| `HelloLighting` | startup banner printed, process alive | Pass |
| `HelloCulling` | steady statistics: `fps 57.5`, 551 instances, 413 submitted, 138 culled, 413 draw calls | Pass |
| `HelloLOD` | steady statistics: `fps 58.4`, 48 candidates, 42 drawn, LOD usage 3/10/29 | Pass |
| `HelloStreaming` | steady statistics: `fps 57.8`, chunks loading, no failures | Pass |
| `HelloPicking` | startup banner printed, process alive | Pass |
| `HelloAnimation` | steady statistics: `fps ~58`, clips advancing | Pass |
| `HelloAudio` | audio device opened at 48 kHz stereo, window alive | Pass |
| `HelloCollision` | steady statistics: `fps 57.8`, 6 colliders, 6 rendered | Pass |
| `HelloBenchmark` | exit status `0`, `fps 56.3`, `render avg 0.30 ms`, 120 draw calls | Pass |

The examples create their device, select shader assets, create both shaders, and
build their pipelines during startup, and exit with status code `1` if any of that
fails. A steady `fps` from the render loop is therefore evidence that Metal was
selected, the MSL resource was found and created for both stages, the pipelines
compiled, and frames were submitted and presented.

**Not claimed:** no visual inspection was performed by this task, so pixel-level
correctness, texture orientation, lighting direction, blending order, and resize
behavior rely on the existing checklist in [SMOKE_TEST.md](SMOKE_TEST.md) rather
than on W04. macOS runtime rendering is unchanged by argument and by these runs,
not by a new visual comparison.

## Limitations and deferred work

- **Windows rendering is unverified.** Every manual Windows row above is open.
- **Direct3D 12 still has not rendered anything.** W06's CI run proves the HLSL
  compiles and the DXIL is generated with the pinned compiler; SDL's acceptance of
  the bytecode and Direct3D 12 pipeline creation remain unverified, as does every
  visual check. The HLSL mirrors the MSL line by line, but that is a review
  argument, not a measurement.
- **The pinned DXC download path is unchanged and still unverified on Windows.**
  W03.1's provisioning was not exercised against a real download here.
- **DXIL generation stays Windows-only.** A Windows binary cross-built on macOS or
  Linux embeds no DXIL.
- **CI was extended later, by W06.** At the time of W04 the workflow ran only on
  `main`, so this branch produced no run. W06 added the Windows development branch
  to the push trigger; the resulting job compiles this HLSL with the real compiler
  and runs the headless tests, but it does not prove rendering.
- **Linux/Vulkan/SPIR-V** remains unsupported and out of scope, as does any new
  graphics abstraction, renderer, or platform project.
- **Next step:** run the Windows matrix above on a Direct3D 12 machine. A clean
  Windows build is the first real test of the HLSL; the `HelloCube` run then
  validates transforms and depth, and `HelloMaterials` validates textures,
  lighting, and transparency ordering.

## Files changed by W04

| File | Change |
| --- | --- |
| `examples/HelloCube/Shaders/Cube.hlsl` | New: HLSL equivalent of `Cube.metal`. |
| `examples/Shared/Shaders/Model.hlsl` | New: HLSL equivalent of `Model.metal`. |
| `examples/Shared/Shaders/Material.hlsl` | New: HLSL equivalent of `Material.metal`. |
| `examples/Shared/ModelShaders.targets` | New: Model DXIL resources, compile items, and import for consuming examples. |
| `examples/Shared/MaterialShaders.targets` | New: Material DXIL resources, compile items, and import for consuming examples. |
| `examples/HelloCube/CubeRenderer.cs` | Selects assets through `ShaderProgram` instead of reading `Cube.metal`. |
| `examples/Shared/ModelRenderer.cs` | Same, for the shared model shader. |
| `examples/Shared/MaterialRenderer.cs` | Same, for the shared material shader; uniform and sampler counts unchanged. |
| `examples/HelloScene/SceneRenderer.cs` | Same, for the shared model shader. |
| `examples/HelloCube/HelloCube.csproj` | Declares and embeds the Cube DXIL assets for Windows builds. |
| `examples/HelloOBJ/HelloOBJ.csproj`, `HelloO3D`, `HelloX`, `HelloScene` | Import `ModelShaders.targets`. |
| `examples/HelloMaterials/HelloMaterials.csproj`, `HelloLighting`, `HelloCulling`, `HelloLOD`, `HelloStreaming`, `HelloPicking`, `HelloAnimation`, `HelloAudio`, `HelloBenchmark`, `HelloCollision` | Import `MaterialShaders.targets`. |
| `tests/Nymeria.Graphics.Tests/ExampleShaderAssetTests.cs` | New: the GPU-free wiring and contract tests above. |
| `README.md`, `examples/README.md`, `docs/ARCHITECTURE.md`, `docs/SMOKE_TEST.md` | Statements that only `HelloTriangle` shipped DXIL were corrected. |
| `docs/WINDOWS_04_RENDERING_COMPATIBILITY.md` | This document. |

No `.metal` source, no parser, no scene, culling, LOD, streaming, picking,
collision, animation, audio, or math code changed. No dependency was added,
upgraded, or removed, and no workflow, repository setting, or `main` branch was
touched.
