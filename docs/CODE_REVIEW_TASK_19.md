# Code Review Task 19

- **Date:** 2026-10-22
- **Scope:** the tracked repository at `77a0823`, covering `Nymeria.Core`,
  `Nymeria.Graphics`, `Nymeria.Platform`, `Nymeria.Audio`, every
  `Nymeria.Formats.*` project, the examples, the tests, the CI workflows, and the
  documentation through Task 18 (keyframe animation, audio playback, scene
  streaming, collision, picking, LOD and culling).
- **Status:** third repository-wide review. No Critical, High or Medium defect was
  confirmed in the Task 15–18 code. One documentation inaccuracy was corrected
  and one property-based regression test was added. The previously known Low and
  Informational items remain open and are restated below with their current
  status. This document does not claim that Nymeria is fully audited or secure.

## Executive summary

The audit followed the project rule "review first, prove the problem". Every
finding below is either backed by a reproducible measurement in this document or
is explicitly marked as an observation rather than a defect.

- **No confirmed memory-safety, lifetime, overflow or unbounded-work defect was
  found** in the new modules. All externally supplied asset data still passes
  through parsers that use checked arithmetic, explicit limits, exact payload
  accounting and strict trailing-data rejection, and the four data types
  (`MeshData`, `ModelData`, `TextureData`/`CompressedTextureData`, `PcmAudioData`)
  re-validate their invariants at construction.
- **59,000 mutated asset inputs** across ten valid seeds produced only the
  documented format exceptions; no `IndexOutOfRangeException`,
  `NullReferenceException`, `OverflowException`, `DivideByZeroException`, hang or
  unbounded allocation occurred.
- **4,000,000 sampled points** were used to cross-check frustum conservatism
  against raw clip-space testing with zero false culls. A seeded subset is now a
  unit test.
- **Independent property probes** (brute-force distance, a separately written ray
  slab test, dense box/sphere sampling, equal-power gain normalization, LOD
  fixed-point selection, visibility-counter accounting, streaming failure
  recovery and animation channel preservation) all held.
- **No vulnerable or outdated package** was reported by `dotnet list`.
- The one *defect-class* item still open is the camera's unvalidated projection
  state. It predates Task 18, is rated Low, and cannot be fixed without either
  changing a public contract or adding a Graphics test surface, both of which
  need maintainer approval.

## Baseline (Phase A)

Environment: macOS `osx-arm64`, .NET SDK `10.0.402` from `global.json`,
configuration `Release`.

| Check | Command | Result |
| --- | --- | --- |
| Restore | `dotnet restore Nymeria.slnx` | Up to date |
| Build | `dotnet build Nymeria.slnx --configuration Release --no-restore` | 0 warnings, 0 errors |
| Tests | `dotnet test Nymeria.slnx --configuration Release --no-build --no-restore` | 1002 passed, 2 skipped, 0 failed (1004 total) |
| Vulnerable packages | `dotnet list Nymeria.slnx package --vulnerable --include-transitive` | None reported |
| Outdated packages | `dotnet list Nymeria.slnx package --outdated` | None reported |

The two skipped tests are `AudioDeviceIntegrationTests`; they require an audio
device and are skipped by their own guard. No warning was suppressed to obtain
the clean build, and no test was skipped, weakened or removed by this task.

Test distribution before the change:

```text
Nymeria.Core.Tests              485
Nymeria.Formats.O3d.Tests        86
Nymeria.Formats.DirectX.Tests    75
Nymeria.Formats.Obj.Tests        59
Nymeria.Formats.Bmp.Tests        54
Nymeria.Formats.Dds.Tests        52
Nymeria.Audio.Tests              48 (2 skipped)
Nymeria.Formats.Png.Tests        42
Nymeria.Formats.Wav.Tests        38
Nymeria.Formats.Tga.Tests        28
Nymeria.Formats.Textures.Tests   16
Nymeria.Formats.Jpeg.Tests       12
Nymeria.Platform.Tests            9
```

## Findings register

| ID | Severity | Area | Status |
| --- | --- | --- | --- |
| NYM-CR19-01 | Informational | `examples/Shared/MaterialRenderer.cs` comment claimed an allocation-free per-frame traversal that is not allocation-free | **Fixed** (comment corrected; residual measured and already documented) |
| NYM-CR19-02 | Low | `Camera` accepts non-finite position and projection state and then emits non-finite matrices without a diagnostic (re-verified from NYM-SEC2-04) | Open — maintainer decision |
| NYM-CR19-03 | Low | Example model framing still leaves the view centre unbounded for extreme finite coordinates (re-verified from NYM-SEC2-03) | Open — maintainer decision |
| NYM-CR19-04 | Informational | PNG `tRNS` transparency is still ignored (re-verified from NYM-SEC2-05) | Open, documented |
| NYM-CR19-05 | Informational | One boxed enumerator (40 bytes) per pass over `Scene.Objects` in the renderer's per-frame traversal | Open, documented in `CULLING.md`/`LOD.md`; no change |
| NYM-CR19-06 | Informational | `Scene.Remove` is a linear list scan, so unloading *k* objects from *n* is O(*n*·*k*) | Open, bounded by application chunk sizes |
| NYM-CR19-07 | Informational | The audio callback drops a partial trailing frame if SDL ever reports a byte count that is not a multiple of the 8-byte float32 stereo frame | Open, not reproducible with SDL3 |
| NYM-CR19-08 | Informational | Bounded-read and limit logic is duplicated across decoders (re-verified from NYM-SEC2-07) | Open, maintainer decision |

No finding is rated Critical, High or Medium.

## Finding details

### NYM-CR19-01 — Per-frame traversal comment overstated allocation freedom (Informational, fixed)

- **Files:** `examples/Shared/MaterialRenderer.cs` (around line 301).
- **Description:** the comment before `EnsureResourcesUploaded` stated that "a
  scene traversal that only reads object references does not allocate per frame".
- **Evidence:** `Scene.Objects` is a `ReadOnlyCollection<SceneObject>`.
  `foreach` over it resolves to the interface `GetEnumerator()`, which boxes
  `List<T>.Enumerator`. Measured with `GC.GetAllocatedBytesForCurrentThread()`
  over 1,000 traversals of a ten-object scene: **40,000 bytes (40 bytes per
  traversal)**; the indexed equivalent allocated **0 bytes**. The same 40 bytes
  per frame is what `docs/CULLING.md` and `docs/LOD.md` already report from the
  benchmark, so the implementation is not wrong — the comment was.
- **Actual vs expected:** the code and docs agree; the inline comment promised
  more than the code does.
- **Correction:** the comment now names the boxed enumerator and points at the
  documented residual instead of claiming zero allocation. No behaviour change.
- **Regression strategy:** the measurement is recorded here and in the existing
  documentation; a unit test cannot observe the examples' traversal.
- **Status:** resolved.

### NYM-CR19-02 — Camera can silently produce non-finite matrices (Low, open)

- **File:** `src/graphics/Nymeria.Graphics/Camera.cs`. `Position` (line 37),
  `FieldOfViewRadians` (64), `NearPlane` (69) and `FarPlane` (74) are plain
  settable properties. `ViewMatrix` (128) and `CreateProjectionMatrix` (138)
  validate only the aspect ratio.
- **Trigger:** assign a NaN position, a zero or NaN near plane, or a field of view
  outside `(0, π)`. `Matrix4x4.CreateLookAt`/`CreatePerspectiveFieldOfView`
  either return a matrix containing NaNs or throw an unrelated
  `ArgumentOutOfRangeException` from `System.Numerics`.
- **Impact:** a NaN view-projection reaches `MaterialRenderer`/`ModelRenderer`,
  which upload it unchanged; the frame draws nothing while reporting success.
  `Frustum.Update` fails open, so culling silently stops, and
  `ViewportRay.TryCreate` returns false. Picking and culling already fail safe;
  the rendering path does not.
- **Actual vs expected:** documented as a low-risk hardening gap since
  `SECURITY_AUDIT_02.md`. Invalid camera state should be reported once, not turned
  into invisible geometry.
- **Proposed correction (needs approval):** validate the projection scalar
  properties inside `CreateProjectionMatrix` and the position/forward vectors
  inside `ViewMatrix`, throwing `InvalidOperationException` with the property
  name. This changes the public contract for invalid configuration only.
- **Regression strategy:** only possible with a test project that references
  `Nymeria.Graphics`. No existing test project does, and adding that reference is
  a dependency change the repository rules reserve for the maintainer.
- **Status:** open, blocked on a maintainer decision (validation location and test
  surface).

### NYM-CR19-03 — Extreme finite model coordinates still break viewer framing (Low, open)

- **File:** `examples/Shared/ModelViewerApp.cs`, `GetBounds`.
- **Description:** the radius is clamped, but the centre is not. Coordinates near
  `float.MaxValue` produce a huge finite centre and camera position; the view
  matrix stays finite but loses all depth precision, so the model is invisible.
  A centre whose sum overflows is already replaced with the origin.
- **Status:** unchanged from NYM-SEC2-03; examples are viewer-scale tools and the
  fix changes framing policy, so it is left to the maintainer.

### NYM-CR19-04 — PNG `tRNS` transparency is ignored (Informational, open)

- **File:** `src/formats/Nymeria.Formats.Png/PngImageDecoder.cs`.
- **Description:** `tRNS` is an ancillary chunk and is skipped; palette entries
  therefore decode as opaque. Documented in `SECURITY_AUDIT_02.md` and
  `MATERIALS.md`. Implementing it is a feature decision, not a defect fix.
- **Status:** open.

### NYM-CR19-05 — 40 bytes per frame from the scene traversal (Informational, open)

Measured as in NYM-CR19-01. The cost is negligible (about 2.4 KB/s at 60 frames
per second) and is already described in `CULLING.md` and `LOD.md`. Changing the
traversal would invalidate the recorded benchmark baseline, which cannot be
re-measured without a GPU run, so the measurement was kept.

### NYM-CR19-06 — Linear scene removal during streaming unload (Informational, open)

- **File:** `src/core/Nymeria.Core/Scenes/Scene.cs` (`Remove`, line 129).
- **Description:** `List<T>.Remove` scans from the front to preserve insertion
  order, so removing *k* streamed objects from a scene of *n* costs O(*n*·*k*).
  Typical chunks hold tens to hundreds of objects, where this is not measurable;
  a pathological single chunk holding the whole 100,000-object limit would be
  slow. Fixing it implies an index structure that still preserves order and is
  therefore an internal data-structure change, not a targeted bug fix.
- **Status:** open, informational.

### NYM-CR19-07 — Partial audio frame dropped if SDL reports a non-frame byte count (Informational, open)

- **File:** `src/audio/Nymeria.Audio/AudioSystem.cs` (line 542).
- **Description:** `frameCount = additionalBytes / BytesPerFrame` discards a
  remainder. SDL3's stream callback works in units of the stream's frame size, so
  the remainder is always zero in practice; no reproduction was found. A guard
  would add a branch to the audio thread for a condition that cannot occur.
- **Status:** open, informational.

### NYM-CR19-08 — Duplicated bounded-read logic (Informational, open)

- **Files:** `Nymeria.Formats.Textures`, `.Png`, `.Jpeg`, `.Bmp`, `.Tga`, `.Dds`,
  `.Wav`, `.O3d`, `.Obj` each contain a near-identical seekable-length check plus
  a bounded `MemoryStream` copy loop.
- **Description:** duplicated but individually correct and deliberately local to
  each format project (the format projects do not reference each other). Moving
  it into a shared helper would add a new project or a new cross-format
  dependency; the duplication is the cheaper option here.
- **Status:** open, informational.

## Security review (Phase C)

All nine format projects plus `TextureLoader` were reviewed for integer
overflow, signed/unsigned conversion, invalid offsets, truncation, malformed
headers, element counts, allocation amplification, unbounded loops, text
decoding, end-of-stream handling, path handling and non-finite numbers.

Confirmed properties (spot-verified in this pass, covered by the existing suites):

| Area | Property |
| --- | --- |
| Texture dimensions | `Texture.MaxDimension`, per-decoder limit and `MaxPixelBytes` are checked with `long`/`checked` before allocation |
| Payload sizing | PNG scanline bytes, DDS mip-chain bytes, BMP stride, TGA RLE expansion and WAV frame counts are computed with `long` or `checked`, and the exact encoded length is asserted |
| Trailing data | PNG, BMP, TGA, DDS, WAV and JPEG reject bytes outside the declared structure |
| Chunk/element counts | Every count is compared with a constant before it becomes an array length; O3D/X section counts are bounded |
| Text | OBJ uses invariant-culture parsing and rejects non-finite numbers; identifiers reject control characters; diagnostics are bounded to 512 single-line characters |
| Decompression | PNG reads exactly the validated scanline size and rejects extra decompressed output; no nested container format is expanded |
| Structure | JPEG walks its segment chain before decoding and requires an end-of-image marker, because the third-party decoder is forgiving of truncation |
| GPU boundary | Vertex/index/upload counts are bounded in the shared builders; texture regions are validated against format, block size, alignment and payload |

Mutation fuzzing (temporary harness, not committed) ran 3,000 mutated inputs per
seed across ten valid assets (`png`, `bmp`, `tga` raw and RLE, `dds`, `jpg`, `x`,
`o3d`, `obj`, `wav`) after two earlier runs of 2,500 and 400, for **59,000
inputs**. Mutations covered byte flips, truncation, structured-zero/extreme
overwrites, random splices and header-integer replacement with
`0`, `1`, `-1`, `int.MaxValue`, `int.MinValue`, `0x7FFF` and `0xFFFF`.

Result: only the documented per-format exceptions (`*FormatException`,
`ModelFormatException`, `InvalidDataException`, `EndOfStreamException`,
`InvalidOperationException`, `IOException`, `NotSupportedException`) were
observed. No unexpected exception type, no `OutOfMemoryException`, no hang.

## Graphics and rendering review (Phase D)

- **Command-buffer lifetime:** every renderer ends the render pass before
  submitting on an error path, and never cancels a command buffer after acquiring
  a swapchain texture. The defect fixed in `SECURITY_AUDIT_02` remains fixed in
  `MaterialRenderer`, `ModelRenderer`, `SceneRenderer`, `CubeRenderer` and
  `TriangleRenderer`.
- **Upload lifetime:** `GpuDevice` releases a buffer or texture on a creation
  failure only after that resource's upload was never accepted, so no drain is
  required there; the renderers do drain because they can fail after a *previous*
  upload was submitted. This asymmetry was checked and is correct.
- **Depth textures:** resizes drain the GPU before releasing the previous texture
  and release a newly created texture if the drain fails.
- **Resource ownership:** `MaterialRenderer` caches one upload per distinct
  `MeshData`/`Texture` reference, prunes only on an unload path, and releases the
  fallback texture, samplers, pipeline, shaders and device exactly once.
- **Render state:** one pipeline, one sampler pair (single-level textures use
  `MaxLod = 0`), explicit viewport/scissor per frame, and an inverse-transpose
  normal matrix that is validated before upload.
- **Per-frame allocation:** only the 40-byte enumerator described in
  NYM-CR19-05; uniform data uses `stackalloc`, the frustum is reused, and
  statistics are value types.

## Animation review (Phase E)

Reviewed `AnimationPlayer`, `AnimationPlaybackState`, `TransformAnimationClip`,
`TransformPose`, `Vector3Track`, `QuaternionTrack`, both keyframe types, the
`HelloAnimation` fixture and all animation tests.

- Play/pause/resume/stop/restart/completed transitions match the documented
  contract, including the deliberate `Play`-on-paused exception and idempotent
  stop.
- Delta validation rejects negative and non-finite values; zero is accepted and
  re-applies the pose. Looping wraps with one double-precision modulo, so a large
  delta costs constant work; non-looping clamps to the duration exactly.
- Track construction rejects empty, oversized, unsorted, duplicate, non-finite
  and out-of-range keys; single-key tracks clamp; quaternions are normalized on
  construction and interpolated along the shortest arc with a linear fallback
  below the `0.9995` dot threshold.
- Sampling is deterministic, does not read a clock, and writes only animated
  channels; a probe confirmed that a scale channel is untouched by a
  translation/rotation clip and that a keyframe time samples exactly.
- Time is a `float` advanced from a `double` sum. For the documented maximum clip
  length (24 hours) the accumulated error is far below one frame; no evidence
  justified changing the representation.
- No shared mutable state between players or clips; the only cross-player effect
  is the documented last-writer-wins order on a shared transform.

## Streaming review (Phase F)

Reviewed `SceneStreamer`, `SceneChunkDefinition`, `SceneStreamingConfiguration`,
`SceneStreamingStatistics` and the tests.

Verified by reading and by probe: eviction precedes loading; demand is inclusive
at `LoadDistance`; unload requires strictly exceeding `UnloadDistance`; loads are
limited by `MaxLoadsPerUpdate` and ordered by distance then ordinal identifier;
partial `Scene.Add` failure rolls back; duplicate/foreign objects are rejected
before any mutation; a failed load is isolated, bounded in diagnostic length, and
retried only after its delay; release failures are counted and reported without
retaining objects; `UnloadAll` and `Dispose` remove exactly the streamed objects
and reject further updates. The probe loaded a chunk that throws on its first
attempt, confirmed no retry before the delay, confirmed recovery after it, and
confirmed `UnloadAll` empties the scene.

## Platform review (Phase G)

- `Window` enforces main-thread creation, one window per process, and resets the
  creation guard on failure and dispose. `WindowOptions` validates title and
  dimensions.
- `GpuDevice` requires the main thread and the Metal backend, claims and releases
  the window, and destroys the device on every failure path.
- `AudioSystem` requires the main thread, opens the default device, verifies the
  negotiated format instead of assuming it, and tears down the stream, the
  `GCHandle` and the subsystem on every failure path.
- Paths are never resolved implicitly; the examples take explicit command-line
  paths, and `LoadWav(string)` opens exactly the supplied path.
- **Verified:** macOS arm64, build and headless suites.
- **Covered by tests:** `WindowOptions`, `Platform` tests, audio mixer and
  listener math.
- **Requires manual verification:** window resize/minimize on each OS, GPU
  presentation, audio device loss, and shutdown ordering. CI builds and tests on
  macOS, Windows and Ubuntu but does not create windows or audio devices.

## Tests and CI review (Phase H)

- The suite is behaviour-oriented and already covers the defects fixed in audits
  01 and 02 with literal, spec-shaped fixtures.
- New coverage added by this task: a seeded property test that cross-checks
  frustum culling against raw clip-space testing for random cameras and boxes
  (`FrustumTests.RandomBoxesContainingVisiblePoints_AreNeverCulled`). This guards
  the single most damaging culling failure mode — hiding visible geometry — with
  a check that is independent of the plane derivation used by the implementation.
- `.github/workflows/repository-checks.yml` uses pinned action SHAs, read-only
  `contents` permission, `persist-credentials: false`, a required-file/link
  check, and a three-OS `restore`/`build`/`test` matrix.
  `dependabot-auto-merge.yml` merges only Dependabot PRs from this repository
  targeting the default branch, only after `Repository checks` succeeds for the
  exact head commit. No workflow was modified.

## False positives, limitations and residual risks

- **No GPU execution in this review.** No example was launched, so no visual,
  resize, device-loss or presentation behaviour is claimed as verified. The
  singular-transform error path and depth-texture resize path were reviewed by
  reading only.
- **No sanitizer, SBOM or license scan.** The package audit is feed-dependent.
- **Fuzzing was a local, temporary harness.** It is not part of CI; adding such a
  job would be a workflow and dependency decision.
- **Camera validation and extreme-coordinate framing remain open** as described
  in NYM-CR19-02 and NYM-CR19-03.
- **Streaming, animation, audio and scenes are single-threaded by contract.**
  Nothing in this review changes that, and no new concurrency was introduced.
- **Resource amplification remains** at the documented per-parser limits (for
  example a 64 MiB decoded image plus its GPU copy).

## Verification evidence

| Check | Command | Result |
| --- | --- | --- |
| Release build | `dotnet build Nymeria.slnx --configuration Release --no-restore` | 0 warnings, 0 errors |
| Full headless suite | `dotnet test Nymeria.slnx --configuration Release --no-build --no-restore` | 1003 passed, 2 skipped, 0 failed |
| Parser mutation fuzz | local harness, 59,000 mutated inputs | documented exceptions only |
| Frustum cross-check | local harness, 4,000,000 sampled points | 0 false culls |
| Core invariants | local harness (distance, ray slab, box/sphere, gains, counters, LOD, raycast, streaming, animation) | all held |
| Package audit | `dotnet list Nymeria.slnx package --vulnerable --include-transitive` | none reported |
| CI | `Repository checks` on the pushed commit | see the final summary of the task |

## Files changed

- [`tests/Nymeria.Core.Tests/FrustumTests.cs`](../tests/Nymeria.Core.Tests/FrustumTests.cs) — seeded frustum-conservatism property test.
- [`examples/Shared/MaterialRenderer.cs`](../examples/Shared/MaterialRenderer.cs) — corrected the per-frame allocation comment.
- [`docs/CODE_REVIEW_TASK_19.md`](CODE_REVIEW_TASK_19.md) — this report.

## Items requiring maintainer approval

1. Whether to validate `Camera` position/projection state and where that
   validation belongs (NYM-CR19-02). A test requires a project that references
   `Nymeria.Graphics`.
2. Whether to clamp example framing coordinates (NYM-CR19-03).
3. Whether to implement PNG `tRNS` (NYM-CR19-04).
4. Whether a fuzz or benchmark job belongs in CI (workflow policy).
5. Whether to consolidate the duplicated bounded-read logic (NYM-CR19-08).
