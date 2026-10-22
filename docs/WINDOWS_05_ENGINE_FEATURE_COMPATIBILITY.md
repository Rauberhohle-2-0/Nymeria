# Windows W05 — Engine feature compatibility

This document maps Nymeria's existing features to their implementation, example,
and automated tests, and records the verification evidence for each on the two
supported backends. It is the W05 deliverable.

**Windows results are not yet recorded.** This task ran on a macOS arm64 host, the
maintainer runs the Windows checks separately, and no Windows machine or
Direct3D 12 device was available here. Every Windows cell below therefore says
**NOT RUN**, and the document gives the exact procedure and expected observations
so those cells can be filled with evidence instead of assumptions. Nothing in this
document claims Windows parity.

No source code changed in W05: the audit and the headless suite found no
demonstrable defect, and inventing platform-specific fixes from a macOS host would
have been speculation. The only changes are this document and the Windows
procedure added to [SMOKE_TEST.md](SMOKE_TEST.md).

## Baseline

| Item | Value |
| --- | --- |
| Branch | `Windows_DirectX` (task text says `Windows`; this checkout carries W01–W04) |
| Commit at audit | `b952cdc` — `docs: record Direct3D 12 rendering compatibility for W04` |
| Working tree | clean, in sync with `origin/Windows_DirectX` |
| Host | macOS 27.0.0 arm64, .NET SDK 10.0.402 |
| SDL3 | `SDL3-CS` 3.4.18 with `SDL3-CS.MacOS` / `SDL3-CS.Windows` 3.4.18 |
| Shader toolchain | DirectX Shader Compiler 1.9.2609, provisioned by `examples/Shared/DxilShaders.targets` (unchanged by W05) |
| W04 state | Code complete for Cube, Model, and Material DXIL; Windows runtime rendering unverified ([WINDOWS_04_RENDERING_COMPATIBILITY.md](WINDOWS_04_RENDERING_COMPATIBILITY.md)) |

**Prerequisite note.** W04's Windows rendering rows are still open. W05's Windows
interactive checks depend on them: if an example fails at shader or pipeline
creation on Windows, that is a W04 item, not a W05 feature defect. Run the W04
Windows checks first, then the W05 checks below.

## Method and evidence classes

| Evidence | What it proves |
| --- | --- |
| Headless test suite (macOS, `dotnet test Nymeria.slnx`) | Platform-independent logic: transforms, animation, raycasting, collision, LOD, culling, streaming, audio math, format decoding. These tests contain no SDL3 or GPU device dependency, so their result is expected to be identical on Windows, but that is an expectation until the suite runs there. |
| Code audit for OS-specific dependencies | Only `GpuBackendSelection` branches on the operating system; the rest of the engine and every example go through SDL3 or pure managed code. |
| macOS interactive runs | That the examples create a device, build pipelines, submit frames, and update their feature diagnostics. Not a Windows result. |
| Windows procedure | The commands and acceptance observations the maintainer can execute to replace every **NOT RUN** cell. |

## Feature matrix

Statuses: **PASS** / **FAIL** / **NOT RUN** / **N/A**. "macOS" is a result
observed here; "Windows" is a result the maintainer still has to observe.

| # | Feature | Implementation | Example | Automated evidence | macOS | Windows |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Scene and transforms (flat list; no hierarchy) | `src/core/Nymeria.Core/Scenes/Scene.cs`, `SceneObject.cs`, `Transform.cs` | `HelloScene` | `SceneTests`, `TransformTests` | **PASS** | **NOT RUN** |
| 1a | Scene hierarchy / parent-child transforms | Not implemented by design ([ARCHITECTURE.md](ARCHITECTURE.md) states there is no hierarchy) | none | none | **N/A** | **N/A** |
| 2 | Animation (tracks, clips, playback, transform writes) | `src/core/Nymeria.Core/Animation/` (`TransformAnimationClip`, `AnimationPlayer`, keyframe tracks) | `HelloAnimation` | `AnimationTrackTests`, `TransformAnimationClipTests`, `AnimationPlayerTests` | **PASS** | **NOT RUN** |
| 3 | Raycasting and picking | `src/core/Nymeria.Core/Raycasting/` (`Ray`, `RayIntersections`, `SceneRaycaster`, `ViewportRay`) | `HelloPicking` | `RayTests`, `RayIntersectionsTests`, `SceneRaycasterTests`, `ViewportRayTests` | **PASS** | **NOT RUN** |
| 4 | Collision detection (headless overlap queries) | `src/core/Nymeria.Core/Collision/` (`CollisionShape`, `CollisionIntersections`, `SceneCollision`) | `HelloCollision` | `CollisionShapeTests`, `CollisionIntersectionsTests`, `SceneCollisionTests`, `BoundingBoxTests`, `BoundingSphereTests` | **PASS** | **NOT RUN** |
| 5 | LOD, distance culling, frustum culling | `src/core/Nymeria.Core/Lod/`, `Culling/`; renderer paths in `examples/Shared/MaterialRenderer.cs` | `HelloLOD`, `HelloCulling` | `LodMathTests`, `MeshLodTests`, `FrustumTests`, `SceneLodCullingTests`, `SceneVisibilityCounterTests` | **PASS** | **NOT RUN** |
| 6 | World streaming and scene chunks | `src/core/Nymeria.Core/Streaming/` (`SceneStreamer`, configuration, statistics) | `HelloStreaming` | `SceneStreamerTests` | **PASS** | **NOT RUN** |
| 7 | Audio: spatial math and mixing | `src/core/Nymeria.Core/Audio/` (`AudioListener`, `AudioMath`, `PcmAudioData`), `src/audio/Nymeria.Audio/` (`AudioMixer`, `AudioSystem`, `AudioClip`, `AudioVoice`) | `HelloAudio` | `AudioMathTests`, `AudioListenerTests`, `PcmAudioDataTests`, `AudioMixerTests`, `AudioSystemOptionsTests`; device tests skipped by design | **PASS** (math, mixing, resampling) | **NOT RUN** |
| 7a | Audio device init, playback, shutdown | `AudioSystem.Create` / `Dispose` | `HelloAudio` | `AudioDeviceIntegrationTests` (2 tests, skipped: require a live device and the process main thread) | **PASS** (device opened and mixed during the run) | **NOT RUN** |
| 8 | Input and window lifecycle | `src/platform/Nymeria.Platform/Window.cs`, `examples/Shared/FlyCameraControls.cs` | `HelloWindow --smoke`, all GPU examples | `WindowOptionsTests` (options only; no automated window creation) | **PASS** (interactive runs) | **NOT RUN** |
| 9 | Performance diagnostics | `src/core/Nymeria.Core/Diagnostics/FrameTimingWindow.cs`, renderer counters | `HelloBenchmark`, plus per-second diagnostics in `HelloCulling`, `HelloLOD`, `HelloStreaming`, `HelloCollision`, `HelloAnimation` | `FrameTimingWindowTests` | **PASS** (counters update) | **NOT RUN** |
| 10 | Format loaders | `src/formats/Nymeria.Formats.Obj`, `.O3d`, `.DirectX`, `.Png`, `.Tga`, `.Jpeg`, `.Dds`, `.Bmp`, `.Textures`, `.Wav` | `HelloOBJ`, `HelloO3D`, `HelloX`, `HelloMaterials` | `ObjModelParserTests`, `O3dModelParserTests`, `XModelParserTests`, `PngImageDecoderTests`, `TgaImageDecoderTests`, `JpegImageDecoderTests`, `DdsTextureDecoderTests`, `BmpImageDecoderTests`, `TextureLoaderTests`, `WavAudioDecoderTests` | **PASS** | **NOT RUN** |
| 11 | Rendering (W04 baseline) | `examples/Shared/*Renderer.cs`, `HelloCube/CubeRenderer.cs`, `Nymeria.Graphics` | `HelloTriangle`, `HelloCube`, `HelloScene`, `HelloMaterials` | `ShaderProgramTests`, `ExampleShaderAssetTests`, `CameraTests`, `GpuBackendSelectionTests` | **PASS** | **NOT RUN** |

## Per-feature notes

### 1. Scene and transforms

`Scene` holds a deterministic list of `SceneObject`s; each object owns a
`Transform` and references shared `MeshData`, an optional `Material`, and optional
collision/LOD configuration. `Transform` composes `scale * rotation * translation`
for `System.Numerics` row vectors and validates finite values. There is **no
hierarchy**, so "world transform" is the object transform, and traversal is a list
walk. `HelloScene` renders four instances of one mesh with different positions,
quaternions, and scales, and adds/removes/resets objects at runtime.

macOS evidence: the example started, printed its control banner, and stayed alive
for six seconds with no error output. It prints no frame statistics, so this shows
the scene, shaders, and pipeline were created without a failure exit; it is not a
visual inspection. Headless tests cover transform validation, composition order,
quaternion normalization, scene add/remove/reset, and shared-mesh ownership.

Windows: the same list-walk and matrix conventions are used, and no scene code
touches an OS API. Verify with `HelloScene`: four cubes, distinct transforms,
correct depth occlusion, and `E`/`Q`/`R` behavior.

### 2. Animation

`TransformAnimationClip` samples translation, rotation, and scale tracks;
`AnimationPlayer` writes sampled poses into a `SceneObject` or a bare `Transform`,
so "propagation" means writing the target transform before the renderer composes
model matrices. Timing uses a caller-supplied delta that is clamped by the
examples, not a wall clock inside the core.

macOS evidence: `HelloAnimation` printed advancing playheads and angles, for
example `door play 3,23/4,00s 59,7 deg | wiper play 0,86/1,60s 32,0 deg | mover
play 1,04/3,00s scale 1,25 | fps 57,7 | visible 8`. Headless tests cover track
sampling, shortest-path quaternion interpolation, clip boundaries, looping, delta
partitioning, pause/resume/stop, and deterministic multi-player conflict handling.

Windows: pure managed code. Verify with `HelloAnimation`: three independent
clips, `P` pause/resume, `L` looping, `-`/`=` speed, `R` restart, and the same
statistics shape.

### 3. Raycasting and picking

`Ray` validates direction and origin; `RayIntersections` tests AABBs;
`SceneRaycaster` returns the nearest hit over eligible objects;
`ViewportRay` unprojects cursor or viewport-center coordinates using the same
right-handed, row-vector, `[0,1]`-depth conventions as the camera and shaders.
Transformed world bounds are used, so rotation and non-uniform scale are handled.

macOS evidence: `HelloPicking` started, printed its picking and chunk summary,
and stayed alive with no error output; its per-click log only appears on input,
which this unattended run did not produce. Headless tests cover ray validation,
boundary intersections, nearest-hit selection and ties, transformed bounds,
unprojection, and independence from frustum culling and LOD.

Windows: no OS dependency. Manual checks are required because picking is
input-driven: click a box, click empty space, `V` center picking, `G` ghost
eligibility, and verify one log line per click rather than one per frame.

### 4. Collision detection

`CollisionShape` is a validated local box or sphere; `CollisionIntersections`
provides inclusive overlap tests; `SceneCollision` converts shapes to world space
from scene objects and returns deterministic O(n²) pairs. Detection only: nothing
resolves contacts.

macOS evidence: `HelloCollision` printed live diagnostics, for example
`colliders 6 | tested pairs 15 | overlapping pairs 3 | player contact | animation
on | query 0,017 ms | fps 57,9 | rendered 6`. Headless tests cover shape
validation, inclusive boundaries, conservative transforms, filtering, pair
ordering, and query cost bounds.

Windows: pure managed code. Verify with `HelloCollision`: arrow keys move the
player, highlight follows overlapping pairs, `P` pauses animation, `R` resets.

### 5. LOD and culling

`Frustum` derives six normalized planes from the view-projection matrix;
`SceneVisibilityCounter` performs the per-frame distance/frustum/LOD test and
counts; `LodMath` applies thresholds with hysteresis; `MaterialRenderer` caches
one GPU upload per distinct mesh variant and draws the selected one.

macOS evidence: `HelloLOD` reported `candidates 48 | frustum culled 0 | distance
culled 6 | drawn 42 | draw calls 42 | triangles 3598 | LOD 3/10/29`, and
`HelloCulling` reported `instances 551 | submitted 413 | culled 138 | draw calls
413`. Those counts are aspect-ratio dependent for culling and are a startup
configuration for LOD; expect the same shape on Windows, not necessarily the
identical split. Headless tests cover plane extraction, positive-vertex
conservatism, visibility counters, threshold hysteresis, conservative LOD bounds,
and distance rules.

Windows: the logic is platform-independent, but the draw counts depend on the
actual drawable aspect ratio. Verify toggles (`L`/`X`/`F` in `HelloLOD`, `C` in
`HelloCulling`) and that submitted + culled equals tested.

### 6. World streaming and scene chunks

`SceneStreamer` owns validated chunk definitions, bounded synchronous
load/unload updates, add order, load-distance/unload-distance hysteresis, a
per-update load budget, failure isolation with retry, content rollback on partial
failure, and statistics. `HelloStreaming` shares meshes and materials across
chunks and prunes unreferenced GPU entries on unload.

macOS evidence: `HelloStreaming` reported `known 25 | active 9 | unloaded 16 |
failed 0 | objects 99 | loads 9/9 | unloads 0 | failures 0/0 | update 0,00 ms |
pruned 0/0 | fps 57,7 | render 0,33 ms | candidates 99 | frustum culled 52 |
draws 47`. Headless tests cover boundaries, hysteresis, load budgets, teleports,
failure retry, rollback, shared references, identifiers, and counters.

Windows: pure managed code plus the shared renderer. Verify with `HelloStreaming`:
`T` teleports unload and load without duplicate objects, `S` freezes streaming,
`C` changes culled counts only, and pruned entries appear after an unload.

### 7. Audio and source sample-rate handling

This was audited specifically rather than assumed.

- `WavAudioDecoder` returns `PcmAudioData` at the **file's own** sample rate and
  channel count; it does not rewrite the header rate.
- `AudioSystem.LoadWav` always calls `decoded.Resample(_options.SampleRate)`
  before creating the clip (`src/audio/Nymeria.Audio/AudioSystem.cs`), so a
  44.1 kHz or 22.05 kHz source plays at the correct pitch and duration on a
  48 kHz mixer.
- `AudioMixer.CreateClip` rejects clip data whose rate differs from the mixer's,
  so a future caller cannot bypass the conversion by accident.
- The playback stream is opened as F32 stereo at `AudioSystemOptions.SampleRate`
  (default 48 kHz, 44.1 kHz supported) and the opened stream format is verified
  after creation; SDL converts to the hardware rate, so a device that does not
  natively run at the requested rate is still correct.
- Resampling is bounded linear interpolation in `PcmAudioData.Resample`, with
  output-size limits and rate validation.

Evidence: `PcmAudioDataTests` covers same-rate short-circuit, upsampling,
downsampling with duration preservation, stereo preservation, output-size limits,
and unsupported rates; `AudioMixerTests` covers the rate guard; and
`AudioSystemOptionsTests` covers the default and an explicit 44.1 kHz option.

macOS evidence: `HelloAudio` opened a device (`Audio ready: 48000 Hz stereo,
float32 mixing, 8 voice limit`), played the 2D loop and two spatial sources, and
reported live spatial state, for example `source B (3D loop): playing, pos (5,0,
1,0, -4,0), distance 13,0, attenuation 0,63, pan right 0,38 (L 0,47 / R 0,89),
gain 70%`. The harness terminated it after the observation window, so a graceful
dispose and device shutdown on Windows still need the manual check.

Windows: **NOT RUN**. Device initialization, playback, spatial panning, the
`, .` and `T` stereo-strength controls, and shutdown must be observed on a real
Windows playback device; a successful build is not playback validation. The
current `HelloAudio` generates its WAVs at 48 kHz, so device playback of a
non-48 kHz source is covered by unit tests and code inspection only; there is no
example that loads an arbitrary WAV.

### 8. Input and window lifecycle

`Nymeria.Platform.Window` owns SDL initialization, a resizable high-DPI window,
event pumping with `SDL_WaitEventTimeout`, close and quit handling, and
deterministic shutdown. `FlyCameraControls` reads SDL's cached keyboard and mouse
state, uses physical scancodes (layout-independent), and owns relative-mouse
capture and its recovery. Every GPU example skips rendering while minimized,
handles a zero-sized drawable, recreates its depth texture on resize, and exits
with status `0` on a normal close.

macOS evidence: `HelloWindow` and all sixteen GPU examples started and stayed
alive without error output until the harness terminated them. Resize,
minimize/restore, and key/mouse behavior were not driven by this unattended run,
and a harness termination is not a graceful-close test. Headless tests cover
`WindowOptions` validation only; the window itself has no automated test on either
platform.

Windows: **NOT RUN**. `HelloWindow --smoke` is the window-only check; the
per-example manual checks (`Alt+F4`, minimize/restore, resize, right-mouse capture
and release, `Shift` sprint, diagonal movement) are in
[SMOKE_TEST.md](SMOKE_TEST.md).

### 9. Performance diagnostics

`FrameTimingWindow` aggregates bounded frame samples with warm-up exclusion and
invalid-sample rejection; renderers expose per-frame instance, draw-call, triangle,
LOD, streaming, and pruning counters; `HelloBenchmark` runs deterministic
512/2048/8192-instance scenes and prints CPU phase timings plus GC counters.

macOS observation (this hardware only, not a comparison): `HelloBenchmark
--instances 128 --warmup 10 --frames 60` exited `0` with `total frame avg
17.77 ms`, `render avg 0.30 ms`, 120 draw calls, 92,160 triangles, and
`managed 40 bytes/frame | GC +0/+0/+0`.

Windows: **NOT RUN**, and no performance parity is claimed. Record the same
command's output and the reported OS/architecture line; compare only trends on
the same machine, never against these numbers.

### 10. Format loaders

All decoders are pure managed code with no OS or SDL dependency and are covered by
per-format test projects. `HelloOBJ`, `HelloO3D`, and `HelloX` exercise the model
parsers through the shared renderer; `HelloMaterials` exercises PNG, JPEG, TGA,
DDS (including block-compressed), and BMP through `TextureLoader`, and falls back
to a generated checkerboard with a diagnostic when a file is missing or malformed.

macOS evidence: the four examples started and rendered from their checked-in
fixtures; the format test projects passed (see the table below). Windows should
behave identically because the decoders never branch on the platform.

## Commands and results

Run from the repository root on the macOS audit host.

| Command | Result |
| --- | --- |
| `dotnet build Nymeria.slnx -c Release` | **0 warnings, 0 errors** |
| `dotnet test Nymeria.slnx -c Release --no-build` | **1120 passed, 2 skipped, 0 failed** |

Per project (macOS):

| Test project | Passed | Skipped |
| --- | --- | --- |
| `Nymeria.Core.Tests` | 514 | 0 |
| `Nymeria.Graphics.Tests` | 67 | 0 |
| `Nymeria.Formats.O3d.Tests` | 86 | 0 |
| `Nymeria.Formats.DirectX.Tests` | 75 | 0 |
| `Nymeria.Formats.Png.Tests` | 64 | 0 |
| `Nymeria.Formats.Obj.Tests` | 59 | 0 |
| `Nymeria.Formats.Bmp.Tests` | 54 | 0 |
| `Nymeria.Formats.Dds.Tests` | 52 | 0 |
| `Nymeria.Audio.Tests` | 46 | 2 |
| `Nymeria.Formats.Wav.Tests` | 38 | 0 |
| `Nymeria.Formats.Tga.Tests` | 28 | 0 |
| `Nymeria.Formats.Textures.Tests` | 16 | 0 |
| `Nymeria.Formats.Jpeg.Tests` | 12 | 0 |
| `Nymeria.Platform.Tests` | 9 | 0 |

The 2 skipped tests are `AudioDeviceIntegrationTests`, which require a live
playback device and the process main thread and are skipped by design on every
platform.

Interactive macOS runs (six seconds each, then terminated):

| Example | Observed |
| --- | --- |
| `HelloScene` | banner printed, rendered, no error output |
| `HelloAnimation` | `fps 57,7`, three clips advancing, `visible 8` |
| `HelloCollision` | `colliders 6`, `tested pairs 15`, live overlap and query time |
| `HelloStreaming` | `known 25`, `active 9`, `objects 99`, `failed 0`, pruning counters live |
| `HelloPicking` | banner printed, rendered, no error output |
| `HelloAudio` | 48 kHz stereo device, spatial source pan and attenuation reported |
| `HelloLOD` | `candidates 48`, `distance culled 6`, `drawn 42`, `LOD 3/10/29` |
| `HelloCulling` | `instances 551`, `submitted 413`, `culled 138`, `draw calls 413` |

Rendering examples were exercised for W04 and re-confirmed by the build and suite
here; their Windows status is unchanged from
[WINDOWS_04_RENDERING_COMPATIBILITY.md](WINDOWS_04_RENDERING_COMPATIBILITY.md).

## Windows procedure

Run from the repository root in a Developer shell with .NET SDK 10.0.402 and a
Direct3D 12-capable GPU. Record the Windows version, architecture, GPU, driver
(both the OS driver and `GpuDevice.DriverName`), the audio playback device, and
the date with each result.

```powershell
dotnet restore Nymeria.slnx
dotnet build Nymeria.slnx --configuration Release
dotnet test Nymeria.slnx --configuration Release --no-build
```

Then, in order:

1. **W04 prerequisite.** `HelloTriangle`, then `HelloCube` and
   `HelloMaterials`. If shader or pipeline creation fails, stop: that is W04, and
   the W05 interactive checks cannot be interpreted until it is resolved.
2. **Headless features.** The `dotnet test` run above must be 1120 passed, 2
   skipped, 0 failed on Windows too. Any failure is a W05 finding; record the test
   name and message.
3. **Scene and transforms.** `dotnet run --project examples/HelloScene` — four
   cubes with distinct transforms, correct depth, `E`/`Q`/`R` behavior.
4. **Animation.** `dotnet run --project examples/HelloAnimation` — three clips,
   `P`, `L`, `-`/`=`, `R`, and a statistics line that advances.
5. **Picking.** `dotnet run --project examples/HelloPicking` — cursor click, miss,
   `V` center mode, `G` ghost toggle, one log line per click.
6. **Collision.** `dotnet run --project examples/HelloCollision` — 6 colliders,
   15 tested pairs, highlights on overlaps, arrow keys, `P`, `R`.
7. **LOD and culling.** `dotnet run --project examples/HelloLOD` and
   `examples/HelloCulling` — toggles and counters as in their
   [SMOKE_TEST.md](SMOKE_TEST.md) sections; expect the same shape as the macOS
   counts, not necessarily the same split.
8. **Streaming.** `dotnet run --project examples/HelloStreaming` — starting
   demand set near 9 of 25 chunks, `T` teleport, `S` freeze, `C` culling, and
   `pruned` after an unload.
9. **Audio.** `dotnet run --project examples/HelloAudio` — device line, 2D loop,
   spatial pan as the listener moves, `,`/`.`/`T` stereo-strength controls, `P`,
   `O`, `X`, `[`/`]`, and clean shutdown. Confirm the process exits `0` and no
   audio thread remains after close.
10. **Input and window.** `dotnet run --project examples/HelloWindow -- --smoke`,
    then resize, minimize/restore, right-mouse capture and release on a GPU
    example, and `Alt+F4`.
11. **Performance diagnostics.** `dotnet run --project examples/HelloBenchmark --
    --instances 128 --warmup 10 --frames 60`. Record the output; do not compare it
    with the macOS numbers.
12. **Formats.** `HelloOBJ`, `HelloO3D`, `HelloX` with fixtures and with explicit
    paths, and `HelloMaterials` with
    `examples/HelloMaterials/Assets/checker.jpg`, `checker.tga`,
    `checker-rle.tga`, `checker.bmp`, `checker-32.bmp`, `checker-rgba.dds`, and
    `checker-bc1.dds`.

## Known limitations and remaining issues

- **No Windows result exists yet.** Every Windows cell is open; W05 stays
  partially verified until the procedure above is executed.
- **W04 Windows rendering is also open.** W05 depends on it.
- **Device audio is untested automatically.** `AudioDeviceIntegrationTests` are
  skipped by design; playback, spatialization, and shutdown need the manual
  `HelloAudio` run.
- **No example loads a non-48 kHz WAV.** Resampling is covered by unit tests and
  code inspection; an end-to-end device check of a 44.1 kHz source would need a
  new example capability, which W05 must not add.
- **No automated window, GPU, or input test exists.** CI builds and runs headless
  tests only, and this branch triggers no CI run because the workflow is limited to
  `main` and pull requests targeting it.
- **No performance parity claim.** Timings from different hardware are not
  comparable, and no benchmark result is used to assert parity.
- **Linux/Vulkan remains deferred.** No policy, no SPIR-V assets, and no plan is
  changed by W05.

## Files changed by W05

| File | Change |
| --- | --- |
| `docs/WINDOWS_05_ENGINE_FEATURE_COMPATIBILITY.md` | This document. |
| `docs/SMOKE_TEST.md` | Windows engine-feature procedure and the audio sample-rate note. |

No source file, project file, shader, test, dependency, workflow, or repository
setting was modified.
