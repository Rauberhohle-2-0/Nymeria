# Security Audit 02

- **Date:** 2026-10-21
- **Scope:** The tracked Nymeria repository at `42820c8`, including the OBJ, O3D,
  text DirectX `.x`, PNG, JPEG, TGA, BMP, and DDS input paths, texture and model
  data, the SDL3 GPU upload path, scene/lighting/camera math, tests, dependencies,
  and GitHub Actions.
- **Status:** Second repository-wide review. Two verified defects were fixed and
  regression-tested; remaining low-risk items are tracked below. This document
  does not claim that Nymeria is fully audited or secure.

## Executive summary

No Critical issue was found. One High data-integrity defect affected every
standard RLE-compressed TGA: the decoder interpreted the RLE packet header bit
backwards, and the test builder and checked-in fixture encoded the same mistake,
so the test suite passed while ordinary TGA tools would fail. It is fixed, and
the tests now include a literal, spec-shaped packet stream.

One Medium native-lifetime defect was found in the shared material renderer. A
singular model transform threw after the frame's render pass and swapchain had
been acquired, leaving the command buffer unsubmitted. This is the same class of
hazard as the previously fixed NYM-SEC-04, in a newer draw loop. It is fixed.

The remaining findings are Low or Informational: extreme but finite model
coordinates can still make the viewer's framing math degenerate, camera setters
are not fully validated, PNG `tRNS` transparency is ignored, and example GPU
caches retain removed resources until disposal. None is a confirmed memory-safety
or remote-code-execution issue. The prior audit's fixed findings were rechecked
and no regression was found.

## Severity definitions

| Severity | Meaning |
| --- | --- |
| Critical | Remote code execution or memory corruption with little user interaction. |
| High | Silent corruption or misinterpretation of supported untrusted input, a practical denial of service, native memory corruption, or a clear trust-boundary escape. |
| Medium | Resource amplification, a lifetime defect, or a realistic impact with a narrower trigger. |
| Low | Hardening defect, malformed-input acceptance, or a stale-handle/edge-case hazard. |
| Informational | Reviewed area with no confirmed defect, or a documented residual risk. |

## Trust boundaries and data flow

| Boundary | Data | Validation owner |
| --- | --- | --- |
| Untrusted model files and streams | OBJ, O3D, text `.x` | `Nymeria.Formats.Obj`, `.O3d`, `.DirectX`; `MeshData`/`ModelData` invariants |
| Untrusted image files and streams | PNG, JPEG, TGA, BMP, DDS | `Nymeria.Formats.Png`, `.Jpeg`, `.Tga`, `.Bmp`, `.Dds`; `TextureData`/`CompressedTextureData` invariants |
| Format selection | Stream content and optional source name | `TextureLoader` (signature first; TGA by `.tga`/`.targa` name plus header) |
| App-owned model/scene data | `MeshData`, `Material`, `Texture`, `Scene`, `Transform`, lights | Core constructors and setters |
| GPU boundary | Interleaved vertices/indices, uniforms, texture regions | Example builders and `GpuDevice` limits/capability checks |
| Native boundary | SDL3 window, device, buffers, textures, samplers, command buffers | `Nymeria.Platform.Window` and `Nymeria.Graphics.GpuDevice` |
| Build and CI | NuGet restore, actions metadata, Dependabot PRs | Package audit, SHA pins, read-only workflow permissions, gated auto-merge |

There is no custom P/Invoke and no `unsafe` block. SDL3 through the pinned
`SDL3-CS` binding is the only native boundary. Parsers and decoders do not open
files, resolve paths, or touch SDL3/GPU state. Examples accept only explicit
command-line paths and never resolve a texture or material path relative to a
model.

The external JPEG decoder is the only third-party parsing library. `StbImageSharp`
is pure managed; Nymeria still owns the encoded-size bound, structural marker
walk, dimension pre-check, decoded-size check, and opaque alpha normalization.

## Findings register

| ID | Severity | Affected path(s) | Finding | Status |
| --- | --- | --- | --- | --- |
| NYM-SEC2-01 | High | `src/formats/Nymeria.Formats.Tga/TgaImageDecoder.cs`; TGA tests and fixture | RLE packet header bit 7 was interpreted backwards, and the tests/fixture encoded the same inversion. | Fixed |
| NYM-SEC2-02 | Medium | `examples/Shared/MaterialRenderer.cs` | A singular model transform threw after swapchain/render-pass acquisition without ending the pass or submitting the command buffer. | Fixed |
| NYM-SEC2-03 | Low | `examples/Shared/ModelViewerApp.cs` | Imported positions are finite but unbounded; extreme coordinates can make the framing camera and view matrix degenerate. | Open, tracked |
| NYM-SEC2-04 | Low | `src/graphics/Nymeria.Graphics/Camera.cs` | Public camera properties accept non-finite or degenerate values; projection validation covers only the aspect ratio. | Open, tracked |
| NYM-SEC2-05 | Low | `src/formats/Nymeria.Formats.Png/PngImageDecoder.cs` | `tRNS` transparency is ignored, so indexed/grayscale/RGB transparency becomes opaque. | Open, tracked |
| NYM-SEC2-06 | Low | `examples/HelloScene/SceneRenderer.cs`, `examples/Shared/MaterialRenderer.cs` | GPU caches retain every distinct removed mesh/texture until `Dispose`; churn grows GPU memory. | Informational, accepted |
| NYM-SEC2-07 | Informational | Five image decoders and two text/binary parsers | Bounded-read/limit logic is duplicated; a shared test oracle mistake can hide a format bug. | Maintainability note |
| NYM-SEC2-08 | Informational | Packages, workflows, Dependabot | No vulnerable package or clear workflow defect was found; audit coverage is feed-based. | Reviewed |

## Finding details

### NYM-SEC2-01 - TGA RLE packet headers inverted (High)

**Affected code:** [`TgaImageDecoder.DecodeRle`](../src/formats/Nymeria.Formats.Tga/TgaImageDecoder.cs),
[`TgaTestFileBuilder`](../tests/Nymeria.Formats.Tga.Tests/TgaTestFileBuilder.cs),
[`TgaImageDecoderTests`](../tests/Nymeria.Formats.Tga.Tests/TgaImageDecoderTests.cs),
and the checked-in `examples/HelloMaterials/Assets/checker-rle.tga`.

**Evidence:** TGA packet headers use bit 7 as the packet type: set means a
run-length packet (one pixel repeated `count` times), clear means a raw packet
(`count` distinct pixels). The decoder had the branches exchanged. A standard
two-pixel run header (`0x81`) was treated as a raw packet and the decoder tried
to read two pixels, while a raw packet (`0x00`) was treated as a run and repeated
one pixel.

The test builder had the same inversion: `RleRun` emitted `count - 1` with bit 7
clear, and `RleRaw` emitted `0x80 | (count - 1)`. The fixture was generated with
that convention: every one of its 128 packets began with `0x7F` (bit 7 clear)
and carried one 24-bit pixel, which only decodes as a 128-pixel run under the
inverted reader. The existing 26 TGA tests therefore validated the implementation
against its own mistake rather than against the format.

**Impact:** Every conforming RLE TGA from another tool would either fail with a
truncation/trailing-data error or decode incorrectly. The defect is
data-integrity/correctness, not memory corruption: the packet bounds checks still
prevented out-of-bounds writes. A viewer could display corrupted textures, and an
asset pipeline could silently accept the project's nonstandard fixture.

**Reproduction:** A 3 x 1 32-bit RLE image with packets
`81 00 00 FF FF` (two red pixels) and `00 00 FF 00 FF` (one green pixel) is
valid TGA. Before the fix it was read as a raw packet followed by misaligned
bytes. The checked-in fixture, parsed with the standard bit rule, contains 128
run packets of 128 pixels and 16,384 pixels total.

**Fix:** The decoder now uses bit 7 as the format specifies. The test builder
emits standard headers, the fixture's 128 packet headers were changed from `0x7F`
to `0xFF`, and a literal packet-stream test plus a truncated-run test were added.
`docs/MATERIALS.md`, `docs/SECURITY.md`, and `docs/SECURITY_CHECKLIST.md` now
state the standard bit rule.

**Verification:** `Nymeria.Formats.Tga.Tests` passes 28 tests. A temporary
headless check decoded the fixture as 128 x 128, 65,536 RGBA bytes, fully opaque.
`HelloMaterials` started with the fixture and reported:

```text
Loaded base-color texture 'examples/HelloMaterials/Assets/checker-rle.tga':
128 x 128, 1 mip level(s), 65536 RGBA bytes.
```

`Status: Fixed.`

### NYM-SEC2-02 - Material frame left unsubmitted on an invalid transform (Medium)

**Affected code:** [`MaterialRenderer.Render`](../examples/Shared/MaterialRenderer.cs),
inside the per-object loop after `SDL_BeginGPURenderPass`.

**Evidence:** `Transform` deliberately permits zero scale (`Scale = 0` collapses
the object). The material renderer computes an inverse-transpose normal matrix
for every object and throws a diagnostic when the transform is singular. That
throw occurred after the swapchain texture and render pass had been acquired.
Once a swapchain texture is acquired, SDL does not permit
`SDL_CancelGPUCommandBuffer`; the command buffer must be submitted. The prior
audit fixed this pattern in the other renderers (NYM-SEC-04), but this newer loop
reintroduced it.

**Impact:** A singular or non-finite transform, including a zero-scale object
added by application code, can leave an acquired native command buffer and open
render pass on an error path. The examples catch the exception and exit, but the
native state is not deterministically released. It is not a parser or
memory-corruption path.

**Fix:** Before propagating the transform error, the renderer now ends the
render pass and submits the command buffer. If submission also fails, the
resulting exception includes both diagnostics. The normal frame path is
unchanged.

**Verification:** Release build with zero warnings; all examples launched and
remained running for several seconds. The singular-transform branch itself
cannot be exercised by the headless tests because it requires an SDL3 GPU device;
that limitation is recorded below.

`Status: Fixed, with runtime GPU path verified only by successful normal-path
startup.`

### NYM-SEC2-03 - Extreme finite model coordinates can break viewer framing (Low)

**Affected code:** [`ModelViewerApp.GetBounds`](../examples/Shared/ModelViewerApp.cs)
and the camera construction in `Run`.

**Evidence:** `MeshData` intentionally rejects non-finite values but does not
limit their magnitude. `GetBounds` clamps the computed radius to 1,000,000 but
does not clamp the center. At a coordinate near `1e20`, adding the 2.5-unit camera
offset is lost in `float` precision, so the camera position and look-at target
can become identical. `Matrix4x4.CreateLookAt` then builds a view from a
zero-length direction, producing non-finite values.

**Impact:** A syntactically valid OBJ/O3D/`.x` model with extreme coordinates can
make the example viewer render undefined geometry or stop with a GPU/driver
error. It does not cause an out-of-bounds read or a parser allocation issue. The
bounded parser limits still apply.

**Recommended fix:** Clamp or reject framing coordinates with a documented
viewer limit and a clear diagnostic, or compute the bounds/framing in double
precision and clamp the resulting camera transform. This is example policy, not
a `MeshData` contract change; changing the model contract would need maintainer
approval.

`Status: Open, tracked; not fixed in this pass because it is Low and does not
change the parser or GPU safety boundary.`

### NYM-SEC2-04 - Camera accepts non-finite or degenerate projection values (Low)

**Affected code:** [`Camera`](../src/graphics/Nymeria.Graphics/Camera.cs).

**Evidence:** `Position`, `FieldOfViewRadians`, `NearPlane`, `FarPlane`,
`MoveSpeed`, and `MouseSensitivity` are public automatic properties without
validation. `CreateProjectionMatrix` validates only that the aspect ratio is
positive and finite. A NaN field of view or near/far plane can pass the
comparisons inside `Matrix4x4.CreatePerspectiveFieldOfView` and produce a
non-finite projection; a NaN `Position` propagates through `ViewMatrix`.

**Impact:** Programmatic camera misuse can upload NaN matrices to the GPU. The
examples currently pass valid values, and imported geometry does not set these
properties directly, so the practical trigger is application code rather than
untrusted file content. It remains a hardening gap in a reusable public type.

**Recommended fix:** Validate position and projection parameters with the same
finite/range discipline used by `Transform` and `LightingMath`, or validate at
the projection factory boundary. This changes public setter behavior, so it
needs maintainer approval before implementation.

`Status: Open, tracked; no public-API behavior change made without approval.`

### NYM-SEC2-05 - PNG `tRNS` transparency is ignored (Low)

**Affected code:** [`PngImageDecoder`](../src/formats/Nymeria.Formats.Png/PngImageDecoder.cs).

**Evidence:** The chunk loop explicitly ignores ancillary chunks such as `tRNS`.
`ConvertToRgba` always writes alpha `255` for indexed, grayscale, and RGB color
types. A palette PNG with transparent entries therefore becomes fully opaque.
The class comment says alpha is preserved, which is true for color types 4 and 6
but not for `tRNS`-based alpha.

**Impact:** Standard transparent PNGs are silently misrepresented. The current
material shader does not blend alpha, so the visible impact is limited today, but
the CPU `TextureData` no longer matches the source. This is an unsupported-feature
handling gap, not a memory-safety issue.

**Recommended fix:** Either apply `tRNS` for color types 0, 2, and 3 while
decoding, or reject `tRNS` explicitly with a `PngFormatException` so the subset
boundary is honest. Add indexed/grayscale transparency tests and document the
decision.

`Status: Open, tracked; not fixed in this pass to avoid changing PNG support
semantics without an explicit decision.`

### NYM-SEC2-06 - Example GPU caches retain removed resources (Low/Informational)

**Affected code:** [`SceneRenderer`](../examples/HelloScene/SceneRenderer.cs),
[`MaterialRenderer`](../examples/Shared/MaterialRenderer.cs).

**Evidence:** Both renderers cache GPU buffers or textures in dictionaries keyed
by `MeshData`/`Texture` reference. `Scene.Remove` and `Scene.Clear` remove only
scene objects; the renderer never evicts a cache entry until `Dispose`. The
documented ownership rule is one upload per distinct reference for the renderer's
lifetime, so this is intentional for a fixed demo scene.

**Impact:** A long-running application that adds a new distinct mesh or texture
every frame can grow GPU memory without bound even though the scene is small.
The input is application-owned scene data, not a direct untrusted-file path; the
parser and texture limits still bound each individual upload.

**Recommended fix:** If examples become long-running viewers, add reference-aware
eviction or an explicit cache-reset API, and document when it is safe to evict.
For the current static demos, retaining until `Dispose` is acceptable.

`Status: Accepted residual risk for the examples.`

### NYM-SEC2-07 - Duplicated bounded-read and limit logic (Informational)

Five image decoders and the OBJ and `.x` parsers each implement their own
bounded input stream, 8 KiB read loop, checked size arithmetic, and format-specific
limit object. This keeps format projects isolated and avoids a speculative shared
framework, but it also means a defect can be fixed in one decoder and missed in
another. NYM-SEC2-01 is an example at the semantic level rather than the read-loop
level: the test builder mirrored the decoder instead of the format.

The new literal TGA packet test is a small mitigation: at least one binary-format
test now states the wire format independently of a helper builder. A shared
internal bounded-read helper could be considered only if a second concrete defect
demonstrates the duplication is costly; no such refactor was performed here.

## Input validation review

### Model parsers

- **OBJ:** Bounds seekable and non-seekable input, source line length, each
  position/texture-coordinate/normal stream, deduplicated vertices, triangles,
  and subsets. Uses strict UTF-8 decoding with `DecoderFallbackException` mapped
  to `ModelFormatException`. Negative indices are resolved against the current
  count and checked. No MTL or texture path is opened.
- **O3D:** Validates signature, versions 1-7, flag bits, unscrambled key, section
  tags, duplicate sections, declared counts, section byte lengths, material and
  triangle indices, and finite vertex/normal/UV/matrix values before constructing
  `MeshData`. Rejects scrambling, bones, unknown tags, and unsupported versions.
- **Text `.x`:** Probes the `xof ... txt ...` header before creating the UTF-8
  reader; rejects binary and compressed forms. Bounds token count, token length,
  nesting depth, frame count, vertex/triangle/subset/face limits, and face index
  count. Validates transform finiteness and affine form, material structure,
  face/corner consistency, indices, and post-transform finite values. Unknown
  objects, animation, skinning, and unsupported mesh elements fail explicitly.
- **Core:** `MeshData` rejects empty geometry, non-finite positions/normals/UVs,
  mismatched attribute counts, non-triangle-aligned indices, out-of-range indices,
  and malformed subsets. `ModelData` rejects empty/null mesh lists.

### Image decoders

- **PNG:** Signature, chunk type, CRC, ordering, `IHDR` fields, palette size,
  contiguous `IDAT`, empty `IEND`, no trailing bytes, exact zlib output, filter
  types, and palette indices. Bounds input, dimensions, decoded RGBA, and
  scanline allocation. Unknown critical chunks and unsupported formats fail.
- **JPEG:** Bounds input, verifies SOI, walks the segment chain to EOI, rejects
  truncation, reads dimensions before decoding, checks decoded dimensions and
  payload length, and forces opaque alpha. EXIF orientation is not applied.
- **TGA:** Header, image types 2/10, 24/32-bit pixels, color-map absence,
  interleaving, alpha-bit declaration, dimensions, exact uncompressed size, RLE
  packet count/payload, standard packet bit, trailer/footer allowance, and origin
  flags. All packet bounds are checked before writes.
- **BMP:** Signature, optional declared file size, DIB header sizes, dimensions
  (including `int.MinValue` height), planes, depth, compression, channel masks,
  alpha mask, palette count/placement, stride, `biSizeImage`, pixel offset, exact
  payload, and no trailing bytes. Rejects RLE, embedded JPEG/PNG, OS/2 core
  headers, 1/4/16-bit pixels, and non-standard masks.
- **DDS:** Signature, header/pixel-format sizes, required flags, caps/caps2,
  depth, dimensions, mip count/flag consistency, DX10 resource dimension, array
  size, cubemap flag, per-level block/row sizes with checked arithmetic, and exact
  payload. Rejects BC6H, arrays, volumes, cubemaps, unsupported masks, and padded
  rows. Never decompresses blocks.
- **Loader:** Signature selects PNG/JPEG/DDS/BMP; TGA requires a `.tga`/`.targa`
  name plus full header validation. It does not open files or resolve paths.

### GPU-facing validation

`GpuDevice` validates nonzero dimensions, its own 16,384-pixel and 256 MiB
bounds, exact RGBA payload lengths, texture format capability, mip dimensions,
block alignment, region lengths/offsets, and aligned transfer-buffer size. It
creates buffers/textures with checked sizes, waits for idle before release in
normal disposal and failure paths, and throws disposed handles. The example
builders additionally cap converted vertices/indices and normalize or reject
invalid normals. The material renderer checks the sampled format and uses a
single-level sampler when only one level exists.

### Scene, lighting, and camera math

`Transform` rejects non-finite position/scale/rotation, normalizes the quaternion,
and documents zero scale as degenerate. `LightingMath` rejects non-finite colors,
intensities, directions, and models; returns 0 for zero/non-finite Lambert inputs;
and reports singular/non-finite normal matrices. The material renderer rejects
missing normals and singular transforms instead of uploading them. Camera pitch
and yaw inputs are sanitized, but the broader camera validation gap is
NYM-SEC2-04.

## Prior audit recheck

| Prior finding | Recheck |
| --- | --- |
| NYM-SEC-01 OBJ bounds | Still enforced; tests cover input, line, attribute, triangle, and subset limits. |
| NYM-SEC-02 subset caps | Still enforced in OBJ and O3D; parser tests cover the limits. |
| NYM-SEC-03 renderer failure cleanup | Still present in the other renderers; the new material draw-loop path needed NYM-SEC2-02. |
| NYM-SEC-04 acquired command buffer | Still fixed in `ModelRenderer`, `CubeRenderer`, and `TriangleRenderer`; material renderer now follows it. |
| NYM-SEC-05 GPU conversion budget | Still enforced in `ModelGpuGeometryBuilder` and `MaterialGpuGeometryBuilder`. |
| NYM-SEC-06 strict UTF-8 | Still rejects invalid UTF-8. `StreamReader` BOM detection still accepts UTF-16 BOMs, so the documentation's "strict UTF-8" statement is narrower than the implementation; this is a compatibility note, not a new security defect. |
| NYM-SEC-07 stale handles | `Window.Handle` and `GpuDevice.Handle` still throw after disposal. |
| NYM-SEC-08 O3D memory use | Accepted residual risk remains; no new unbounded allocation was found. |
| NYM-SEC-09 dependencies/CI | Rechecked; no vulnerable package, SHA pins and least-privilege workflow remain. |

The TGA defect was introduced with the later TGA support and was missed by the
post-audit TGA review recorded in Audit 01. This audit corrects that record; no
earlier fixed finding regressed.

## Architecture and maintainability

### Dependency direction

Verified from every `.csproj` and `Nymeria.slnx`:

- `Nymeria.Core` has no project references.
- Every `Nymeria.Formats.*` project references only `Nymeria.Core`; JPEG adds
  `StbImageSharp`; `Nymeria.Formats.Textures` references Core, PNG, JPEG, TGA,
  BMP, and DDS.
- `Nymeria.Graphics` and `Nymeria.Platform` do not reference each other and do
  not acquire Core or format dependencies. Examples wire them together.
- No format reader references Graphics or Platform; no cycle was introduced.

### Duplication and coupling

The format-isolated structure is sound and was preserved. The main duplication
is bounded-input reading and limit arithmetic; see NYM-SEC2-07. No speculative
shared framework, plugin loader, reflection discovery, or format registry exists.
The examples share renderers and geometry builders rather than duplicating GPU
code, and the Core scene/material contracts remain renderer-agnostic.

### Test coverage

Headless coverage is broad: 579 passing tests across 11 projects, including
malformed/truncated inputs, limits, conversions, orientation, alpha, mip layout,
and loader dispatch. Gaps remain:

- No headless tests for `Nymeria.Graphics` (`Camera`, `GpuDevice`) or the example
  renderers; GPU and window behavior needs a native device.
- No fuzzing, property-based mutation campaign, coverage report, or sanitizer run.
- The TGA case shows that builder-generated fixtures can share an implementation
  mistake. Binary formats should include at least one literal, independently
  derived packet/chunk test (added for TGA).
- O3D and `.x` fixtures are synthetic; broad exporter compatibility is not
  covered.

### Hot paths

The TGA fix changes branch selection only and adds no per-byte work; no timing
claim is made. The material renderer cleanup executes only on the error path, so
normal-frame allocations and GPU calls are unchanged. Existing per-frame paths
continue to avoid per-frame buffer/texture creation. One-time uploads still copy
spans via `ToArray()` before `Marshal.Copy`; this is a one-time allocation, not a
hot-path regression.

### Public API

No public type, method signature, or dependency was added or changed. The TGA
behavior now matches the format; previously accepted inverted RLE files (including
the old fixture encoding) are rejected or misread and should be re-exported with
a standard TGA writer. No architectural refactor was performed.

## False positives, limitations, and residual risks

- **No fuzzing, ASan, SBOM, or independent license scan.** NuGet's advisory feed
  reported no known vulnerable package for the pinned graph; that is not proof
  of absence.
- **GPU behavior is not exercised by CI.** The macOS examples were launched
  successfully, but visual correctness, resize/minimize, and the singular
  transform error path were not inspected automatically.
- **`dotnet list` output is feed-dependent.** Audit mode is `all`, warnings are
  errors, and direct versions are pinned, but transitive resolution still depends
  on the NuGet graph.
- **PNG `tRNS`, camera validation, and extreme-coordinate framing remain open**
  as described above.
- **TGA compatibility:** nonstandard inverted RLE files produced by the old
  Nymeria fixture are no longer accepted. They fail with a controlled format
  error; re-export them with a conforming TGA writer.
- **O3D and `.x` subset limits** are practical viewer limits, not general format
  compatibility. Scrambled O3D, animation, skinning, binary `.x`, and unknown
  objects remain unsupported and rejected.
- **No concurrency contract:** scenes, renderers, and SDL objects are not
  thread-safe; creation/use/disposal must stay on the process main thread.
- **Resource amplification:** a valid image at the documented limits can still
  request 64 MiB of pixels or encoded blocks plus GPU memory; O3D and `.x` inputs
  can produce bounded but large arrays. A future service should lower limits or
  sandbox parsing.

## Remediation plan

### Completed in this audit

1. Correct TGA RLE packet semantics, the test builder, the checked-in fixture,
   and spec-literal/truncation tests; update format and security documentation.
2. Submit the material renderer's command buffer after ending the render pass
   when a model transform cannot produce a normal matrix.

### Follow-up candidates (need maintainer decisions)

1. Decide whether PNG `tRNS` should be implemented for color types 0/2/3 or
   rejected explicitly; add tests and documentation either way.
2. Add camera setter/projection validation, or validate at the example boundary,
   without changing public behavior until approved.
3. Clamp or reject viewer framing coordinates, or frame in double precision, with
   a diagnostic and a test for extreme finite models.
4. If examples become long-running, add mesh/texture cache eviction and document
   the ownership transition.
5. Add a small headless camera/renderer-helper test surface only if it can be done
   without a GPU-specific test project; do not add speculative projects.
6. Consider a bounded mutation/fuzz harness for each binary decoder and a
   spec-derived fixture set for TGA/PNG/BMP/DDS. Any new CI dependency or project
   needs approval.

## Verification evidence

Environment: macOS `osx-arm64`, .NET SDK `10.0.402` from `global.json`.

| Check | Command | Result |
| --- | --- | --- |
| Release build | `dotnet build Nymeria.slnx --configuration Release` | 0 warnings, 0 errors |
| Full headless suite | `dotnet test Nymeria.slnx --configuration Release --no-build` | 579 passed, 0 failed, 0 skipped |
| TGA suite | `dotnet test tests/Nymeria.Formats.Tga.Tests/... --configuration Release` | 28 passed |
| Vulnerable packages | `dotnet list Nymeria.slnx package --vulnerable --include-transitive` | No vulnerable package reported for any project |
| Outdated packages | `dotnet list Nymeria.slnx package --outdated` | No updates reported |
| TGA fixture | Headless `TgaImageDecoder` check | 128 x 128, 65,536 bytes, fully opaque |
| Material example | `HelloMaterials checker-rle.tga`, 6 s | Started, loaded fixture, no error output |
| Examples | HelloTriangle, HelloCube, HelloScene, HelloOBJ, HelloO3D, HelloX, HelloLighting, 4 s each | All remained running; no startup error output |

Test distribution after the change:

```text
Nymeria.Core.Tests            146
Nymeria.Formats.DirectX.Tests  75
Nymeria.Formats.O3d.Tests      86
Nymeria.Formats.Obj.Tests      59
Nymeria.Formats.Bmp.Tests      54
Nymeria.Formats.Dds.Tests      52
Nymeria.Formats.Png.Tests      42
Nymeria.Formats.Tga.Tests      28
Nymeria.Formats.Textures.Tests 16
Nymeria.Formats.Jpeg.Tests     12
Nymeria.Platform.Tests          9
```

The examples were terminated after the observation window; no screenshots or
visual assertions were captured. A human should still run [SMOKE_TEST.md](SMOKE_TEST.md)
before treating GPU rendering, resize/minimize behavior, or the new error path as
runtime-verified.

## Files changed

- [`src/formats/Nymeria.Formats.Tga/TgaImageDecoder.cs`](../src/formats/Nymeria.Formats.Tga/TgaImageDecoder.cs) - standard RLE packet branches.
- [`tests/Nymeria.Formats.Tga.Tests/TgaTestFileBuilder.cs`](../tests/Nymeria.Formats.Tga.Tests/TgaTestFileBuilder.cs) and [`TgaImageDecoderTests.cs`](../tests/Nymeria.Formats.Tga.Tests/TgaImageDecoderTests.cs) - standard encoding and spec-literal/truncation tests.
- [`examples/HelloMaterials/Assets/checker-rle.tga`](../examples/HelloMaterials/Assets/checker-rle.tga) - conforming 128-packet RLE fixture.
- [`examples/Shared/MaterialRenderer.cs`](../examples/Shared/MaterialRenderer.cs) - end/submit before propagating a singular-transform error.
- [`docs/MATERIALS.md`](MATERIALS.md), [`docs/SECURITY.md`](SECURITY.md), and [`docs/SECURITY_CHECKLIST.md`](SECURITY_CHECKLIST.md) - packet-semantics and fixture requirements.
