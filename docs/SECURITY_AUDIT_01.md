# Security Audit 01

- **Date:** 2026-10-21
- **Scope:** The whole tracked Nymeria repository at commit `978e86c`, with
  emphasis on the OBJ and O3D parsers, model-to-GPU conversion, SDL3 native
  ownership, repository automation, and dependencies.
- **Status:** Initial findings register with remediation and verification results.
  This document does not claim that Nymeria is fully audited or secure.

## Method

The audit read `AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/GIT_GUIDELINES.md`,
`docs/MODEL_DATA.md`, `docs/OBJ.md`, `docs/O3D.md`, the project files, the
workflows, and all C# sources. It traced untrusted bytes from the parser entry
points through `ModelData` and the example GPU path. It also ran:

```bash
dotnet list Nymeria.slnx package --vulnerable --include-transitive
dotnet list Nymeria.slnx package --outdated
```

Dependency advisory data came from `https://api.nuget.org/v3/index.json` with
SDK `10.0.402`. No known vulnerable direct or transitive package was reported.
The audit did not run a license scan, SBOM generation, or a third-party CVE
database other than NuGet's advisory feed.

## Severity definitions

| Severity | Meaning |
| --- | --- |
| Critical | Remote code execution or memory corruption with little user interaction. |
| High | Practical denial of service, native memory corruption, or a clear trust-boundary escape. |
| Medium | Resource amplification or lifetime defect with realistic impact but a narrower trigger. |
| Low | Hardening defect, malformed-input acceptance, or a stale-handle hazard. |
| Informational | Reviewed area with no confirmed defect, or a documented residual risk. |

## Findings register

| ID | Severity | Area | Finding | Status |
| --- | --- | --- | --- | --- |
| NYM-SEC-01 | High | OBJ parsing | No input, vertex, triangle, subset, or line-length bounds. | Fixed in `659a713` |
| NYM-SEC-02 | Medium | OBJ/O3D parsing | One subset per triangle can amplify a small file into millions of subsets. | Fixed in `659a713` |
| NYM-SEC-03 | Medium | GPU ownership | Renderer failure cleanup could release uploaded buffers while GPU work was pending. | Fixed in `114f50e` |
| NYM-SEC-04 | Medium | GPU lifetime | A managed failure after swapchain acquisition could leave an acquired command buffer unsubmitted. `HelloTriangle` also cancelled after acquisition for a zero-sized drawable. | Fixed in `114f50e` |
| NYM-SEC-05 | Medium | Example GPU path | Model-to-GPU conversion had no upload budget, so a model within parser limits could request hundreds of megabytes. | Fixed in `114f50e` |
| NYM-SEC-06 | Low | OBJ text decoding | Invalid UTF-8 was silently replaced with `U+FFFD`. | Fixed in `659a713` |
| NYM-SEC-07 | Low | Native handles | `Window.Handle` and `GpuDevice.Handle` returned stale native pointers after disposal. | Fixed in `114f50e` |
| NYM-SEC-08 | Informational | O3D memory use | A 64 MiB input can produce roughly 176 MiB of parser-owned arrays plus subset overhead. | Accepted residual risk |
| NYM-SEC-09 | Informational | Dependencies and CI | No vulnerable package was reported; actions are SHA-pinned; workflows use least-privilege permissions; no secrets were found. | Reviewed; `NuGetAudit` enabled |

## Finding details

### NYM-SEC-01 - Unbounded OBJ input and counts (High)

**Affected code:** `src/formats/Nymeria.Formats.Obj/ObjModelParser.cs`, primarily
`Parse`, `ParseLine`, `ParsePosition`, `ParseTextureCoordinate`, `ParseNormal`,
`ParseFace`, and `Build`.

**Evidence:** The parser used `StreamReader.ReadLine` and grew every collection
without a limit. No code compared the input size or the number of positions,
texture coordinates, normals, face corners, triangles, or subsets against a
bound. Unlike the O3D parser, it had no `MaxInputBytes`, `MaxVertexCount`, or
`MaxTriangleCount` equivalent, and it did not cap a single line.

**Impact:** A user-supplied OBJ file, or a non-terminating stream, could force
unbounded memory growth and CPU time. For the interactive `HelloOBJ` viewer this
is a local denial of service; for any future service that parses untrusted OBJ
content it is a straightforward resource-exhaustion issue.

**Minimal remediation:** Add documented default limits for input bytes, each
attribute stream, triangles, subsets, and line length. Enforce the input and
line limits while reading, before large allocations, and reject counts as soon
as a limit is crossed. Keep the limits internal and configurable for tests; the
public parser constructor keeps the documented defaults.

**Verification:** Headless negative tests for oversized input, oversized lines,
too many positions, too many triangles, and malformed UTF-8. Existing valid OBJ
tests must keep passing.

### NYM-SEC-02 - Unbounded subset count (Medium)

**Affected code:** `src/formats/Nymeria.Formats.O3d/O3dModelParser.cs`
(`BuildSubsets`) and `src/formats/Nymeria.Formats.Obj/ObjModelParser.cs`
(`BeginSubset`, `CloseSubset`).

**Evidence:** O3D creates a new `MeshSubset` whenever the material index changes
between consecutive triangles. OBJ creates one whenever `usemtl` changes.
Neither capped the number of subsets. Alternating two material labels across a
file with millions of triangles creates millions of subsets, and the example
then allocates a fresh dictionary per subset in
`examples/Shared/ModelGpuGeometry.cs`.

**Impact:** A small file inside the triangle limit can cause disproportionate
managed allocation and garbage-collection work, even though the geometry itself
is bounded.

**Minimal remediation:** Set a documented `MaxSubsetCount` of 65,536 in both
parsers and fail with `ModelFormatException` when a new subset would exceed it.
OMSI and ordinary OBJ content use far fewer material groups; the cap is well
above practical models.

**Verification:** O3D test with alternating material indices that crosses the
cap; OBJ test using a small internal limit. Existing subset ordering tests must
keep passing.

### NYM-SEC-03 - GPU resource release without an idle wait (Medium)

**Affected code:** `examples/Shared/ModelRenderer.cs`,
`examples/HelloCube/CubeRenderer.cs`, and
`examples/HelloTriangle/TriangleRenderer.cs`, in each `Create` failure path.

**Evidence:** Each renderer uploads buffers through a submitted copy command
buffer. If a later shader or pipeline creation failed, the `catch` block released
the buffers and then called `GpuDevice.Dispose`, which waits for GPU idle only at
the end. The release therefore happened before the wait. SDL requires that a
released buffer is no longer referenced by pending work.

**Impact:** A startup failure after an upload, such as a shader compilation or
pipeline creation failure, could release a buffer while the upload was still
pending. This is a native lifetime hazard rather than a confirmed crash on the
tested Metal driver.

**Minimal remediation:** Wait for GPU idle before releasing any GPU resource in
the failure path. Keep the existing reverse-order cleanup.

**Verification:** Code review of all three failure paths plus a successful
Release build. A headless regression test cannot create an SDL3 GPU device; the
limitation is recorded in the report.

### NYM-SEC-04 - Command buffer lifetime after swapchain acquisition (Medium)

**Affected code:** `examples/Shared/ModelRenderer.cs`,
`examples/HelloCube/CubeRenderer.cs`, and
`examples/HelloTriangle/TriangleRenderer.cs`, in each `Render` method.

**Evidence:** Once `SDL_AcquireGPUSwapchainTexture` returns a texture, SDL does
not permit the command buffer to be cancelled. `ModelRenderer.Render` and
`CubeRenderer.Render` call `EnsureDepthTexture` and build a projection after that
point. A depth-texture creation/wait failure or an invalid camera projection
threw without submitting the command buffer. `HelloTriangle` also called
`SDL_CancelGPUCommandBuffer` when a non-null swapchain texture had a zero-sized
drawable.

**Impact:** An acquired command buffer left unsubmitted can leak native driver
state or trigger backend validation failures. The zero-sized path is rare but
reachable during window state changes.

**Minimal remediation:** After a swapchain texture is acquired, either submit the
command buffer or submit an empty command buffer before propagating a managed
failure. Use submit, not cancel, for a zero-sized drawable. Keep cancellation for
the path where SDL returned no texture.

**Verification:** Code review and a successful Release build. GPU behavior is
covered by the manual macOS smoke test, not by CI.

### NYM-SEC-05 - Unbounded model-to-GPU conversion (Medium)

**Affected code:** `examples/Shared/ModelGpuGeometry.cs`.

**Evidence:** The builder duplicates source vertices per subset so each subset can
carry one color. It grew two unbounded lists and never checked the resulting
vertex or index count. A valid parsed model with many unique corners per subset
can produce far more GPU vertices than source vertices; the parser limits alone
still allow a multi-hundred-megabyte upload.

**Impact:** Opening a crafted OBJ/O3D file can cause a large managed allocation
and a large GPU buffer request, potentially failing the viewer or exhausting
memory.

**Minimal remediation:** Add an explicit example-level budget of 2,000,000 GPU
vertices and 6,000,000 indices (about 48 MiB of vertex data plus 24 MiB of index
data), checked before growth, and fail with a clear `InvalidOperationException`.
This is a viewer resource policy, not a change to the format-independent model
contract.

**Verification:** Release build and a manual review of the growth paths. The
headless parser tests verify the source limits that feed the builder.

### NYM-SEC-06 - Silent acceptance of malformed UTF-8 (Low)

**Affected code:** `src/formats/Nymeria.Formats.Obj/ObjModelParser.cs`.

**Evidence:** `StreamReader` was constructed with the default UTF-8 encoding,
whose replacement fallback turns invalid bytes into `U+FFFD`. A malformed byte in
a material or object name was therefore accepted and silently changed.

**Impact:** Corrupted text can be misrepresented, and malformed input is not
reported predictably. The geometry path is unaffected.

**Minimal remediation:** Use strict UTF-8 decoding and convert
`DecoderFallbackException` into `ModelFormatException`. Valid UTF-8, including a
UTF-8 BOM, keeps working.

**Verification:** Headless test with an invalid UTF-8 byte sequence.

### NYM-SEC-07 - Stale native handles after disposal (Low)

**Affected code:** `src/platform/Nymeria.Platform/Window.cs` (`Handle`) and
`src/graphics/Nymeria.Graphics/GpuDevice.cs` (`Handle`).

**Evidence:** Both properties returned their stored `IntPtr` even after
`Dispose`, when the native window or GPU device had been destroyed. The
documentation warned against use, but the API did not enforce it.

**Impact:** A caller mistake can pass a dangling `SDL_Window*` or
`SDL_GPUDevice*` into native code. The current examples dispose in the correct
order and do not trigger it.

**Minimal remediation:** Throw `ObjectDisposedException` from the handle getters
after disposal. Valid callers see no change.

**Verification:** Release build and source review. The existing headless tests do
not create native windows or GPU devices, so no automated native test is added.

### NYM-SEC-08 - O3D memory amplification (Informational)

The O3D parser buffers the input (up to 64 MiB) and then allocates positions,
normals, and texture coordinates for up to 2,000,000 vertices, plus up to
12,000,000 triangle indices. The worst case is roughly 176 MiB of live arrays
plus subset and material-name overhead. This is bounded and accepted for a
desktop viewer. A future service that accepts untrusted models should lower the
limits or isolate parsing in a memory-limited process.

## Reviewed areas with no confirmed defect

- **O3D structure validation.** Signatures, versions 1-7, flags, unscrambled
  keys, section tags, duplicate sections, declared counts, remaining bytes,
  material indices, triangle indices, and non-finite values are validated before
  or during use. Declared counts are checked before allocation.
- **Path handling.** OBJ `mtllib`, `maplib`, and `usemap` are ignored; no MTL or
  texture path is opened or resolved. O3D material names are opaque and no
  texture is loaded. There is no model-relative path resolver to traverse.
- **Core validation.** `MeshData` and `ModelData` reject empty geometry,
  mismatched attribute counts, non-finite values, out-of-range indices, and
  malformed subsets. Subset range arithmetic uses `long`.
- **Native interop.** There are no `unsafe` blocks and no custom P/Invoke. The
  SDL3 binding is the only native boundary. Command buffers, swapchain textures,
  and transfer buffers follow the documented acquire/release paths, subject to
  NYM-SEC-03 and NYM-SEC-04.
- **Window lifecycle.** SDL initialization, window creation, event processing,
  and shutdown run on the main thread. Failure paths call `SDL_Quit` and reset
  the single-window guard.
- **Dependencies.** Direct packages are `SDL3-CS` 3.4.18, `SDL3-CS.MacOS`
  3.4.18, `xunit` 2.9.3, `xunit.runner.visualstudio` 4.0.1, and
  `Microsoft.NET.Test.Sdk` 18.11.0. No vulnerable package was reported.
- **GitHub Actions.** Workflows use read-only permissions except the
  Dependabot merge workflow, which has only `contents: write`,
  `pull-requests: write`, and `actions: read`. Checkout and setup actions are
  pinned to full commit SHAs and `persist-credentials` is false. The merge path
  verifies the author, repository, base branch, state, and head SHA before
  merging. No untrusted event value is interpolated directly into a shell
  command.
- **Repository hygiene.** No credentials, private keys, tokens, `.env` files, or
  build output are tracked. The tracked `cube.o3d` is an intentional synthetic
  fixture. `.DS_Store` is ignored.
- **Shader loading.** Shaders are embedded resources from the example assembly,
  not files loaded from the model or the current directory. SDL compiles MSL
  source at startup. This is trusted project content.

## Residual risks and out of scope

- No general fuzzing campaign, address sanitizer run, or third-party binary
  analysis. The audit adds bounded mutation/truncation tests only.
- GPU and window behavior is macOS-only and is not exercised by CI. The manual
  smoke test remains the verification for rendering.
- O3D compatibility, scrambling, bones, and material rendering remain
  unsupported; rejected features are not decoded.
- Parser CPU time is bounded by input and count limits but is still linear in
  the accepted input. A determined local attacker can consume the documented
  maximum.
- Subset coverage is not required to be complete or disjoint; the model
  contract permits it. Renderers draw only the declared subsets.
- Dependency advisory checks depend on the NuGet feed. They do not prove the
  absence of unknown vulnerabilities.
- The build does not sandbox the SDL3 native library or the Metal driver.

## Initial verification baseline

At the start of the audit, the repository built in Release with zero warnings and
all 171 headless tests passed on macOS `osx-arm64` with SDK `10.0.402`:

```text
Nymeria.Core.Tests            29 passed
Nymeria.Formats.Obj.Tests     49 passed
Nymeria.Formats.O3d.Tests     84 passed
Nymeria.Platform.Tests         9 passed
```

## Remediation and verification

### Parser hardening (`659a713`)

- `ObjModelParser` now bounds seekable and non-seekable input, each attribute
  stream, deduplicated vertices, triangles, subsets, and source line length. The
  limits are internal and configurable for tests; the public constructor uses the
  documented defaults.
- OBJ text is decoded as strict UTF-8 and invalid byte sequences become
  `ModelFormatException`.
- `O3dModelParser` caps subsets at 65,536, matching the new OBJ cap.
- New tests cover oversized input, oversized lines, attribute and triangle
  limits, subset limits, malformed UTF-8, truncation, and bounded structure
  mutations.
- `docs/OBJ.md` and `docs/O3D.md` document the limits and strict decoding.

### Native and GPU hardening (`114f50e`)

- All renderer `Create` failure paths call `SDL_WaitForGPUIdle` before releasing
  uploaded buffers.
- `ModelRenderer`, `CubeRenderer`, and `TriangleRenderer` submit an acquired
  command buffer before propagating a managed failure. `TriangleRenderer` submits
  for a zero-sized drawable and for a render-pass failure instead of cancelling.
- The example model viewer enforces a 2,000,000-vertex and 6,000,000-index GPU
  budget before growth.
- `Window.Handle` and `GpuDevice.Handle` throw `ObjectDisposedException` after
  disposal.

### Dependency enforcement

`Directory.Build.props` enables `NuGetAudit` with `NuGetAuditMode=all`. Because
`TreatWarningsAsErrors` is already enabled, a NuGet advisory for a direct or
transitive package fails restore and the repository checks. A manual
`dotnet list Nymeria.slnx package --vulnerable --include-transitive` scan reported
no known vulnerable package during this audit.

### Verification results

- Release build: zero warnings, zero errors, SDK `10.0.402`, macOS `osx-arm64`.
- Headless tests: 183 passed, 0 failed, 0 skipped.
  - `Nymeria.Formats.Obj.Tests`: 59
  - `Nymeria.Formats.O3d.Tests`: 86
  - `Nymeria.Core.Tests`: 29
  - `Nymeria.Platform.Tests`: 9
- GitHub Actions `Repository checks`:
  - run `39570073949` passed for `659a713` on macOS, Windows, and Linux;
  - run `39570282602` passed for `114f50e` on macOS, Windows, and Linux.
- The GPU and window paths were code-reviewed and compiled but were not launched
  during this audit. The manual [smoke test](SMOKE_TEST.md) remains required
  before treating the GPU lifetime fixes as runtime-verified.

### Post-audit material and PNG review

The material and texture milestone was added after this audit's initial scope. It
was reviewed against the same trust model and baseline rather than treated as a
pass:

- `Material` and `TextureData` are managed CPU data with tint, dimension, and
  payload validation. They contain no native or GPU handle.
- `PngImageDecoder` treats streams as untrusted. It validates the signature,
  chunk order, lengths, CRCs, dimensions, palette indices, filters, and exact
  decompressed size; rejects unknown critical chunks and unsupported formats; and
  enforces 32 MiB encoded, 4096-pixel, and 64 MiB decoded limits before use.
- `GpuDevice.CreateTexture` validates its own GPU limits and payload length,
  uploads through a transfer buffer, and releases the texture on failure.
- `HelloMaterials` accepts only an explicit PNG path, never resolves a texture
  relative to a model, and prints a diagnostic and uses generated content for a
  missing or invalid file.
- Tests cover the tint, texture payload, scene material references, malformed and
  truncated PNG structures, boundary limits, and decompression behavior.

No new security finding was identified in this review. The residual risks remain:
the decoder is not fuzzed, a valid image at the limits still allocates the
documented payload, and texture upload and sampling are not exercised by CI.

A later follow-up added JPEG, TGA, and DDS decoding on the same trust model:

- `StbImageSharp` 2.30.16 was approved as the JPEG decoder. It is a pure-managed
  port of `stb_image`, licensed Unlicense OR MIT, with no native code and no
  transitive package dependencies. NuGet audit mode is `all`, and restoration
  reports no known advisory for the pinned version. The package is not fuzzed or
  independently audited here.
- The JPEG wrapper, not the library, owns the encoded-input bound, the
  start-of-image check, segment-length and end-of-image validation, the
  pre-decode dimension check, the decoded-size check, and opaque alpha. This
  rejects truncated files that `stb_image` would otherwise decode with
  synthesized pixels.
- The TGA decoder is dependency-free and validates its header, origin flags,
  alpha declaration, RLE packet sizes, and trailing data before use.
- The DDS decoder is dependency-free and validates the header, the DX10
  extension, caps, depth, dimensions, mip count, per-level sizes with checked
  arithmetic, and the exact payload. It uploads blocks only after a GPU format
  capability check and never decompresses them.
- `TextureLoader` selects by signature for PNG, JPEG, and DDS. TGA, which has no
  signature, requires a `.tga` name and is still header-validated.

No new security finding was identified. Residual risk now also includes the
third-party JPEG decoder and the fact that compressed texture upload and sampling
remain manual macOS checks.

The findings above are addressed at the source level. This audit does not claim
that Nymeria is secure or fully audited; the residual risks listed earlier still
apply.
