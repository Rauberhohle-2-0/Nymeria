# Task 19.1 — Final hardening and graphics runtime validation

This report records the work, verification, and remaining risk of Task 19.1. It
addresses the actionable findings of
[Code Review Task 19](CODE_REVIEW_TASK_19.md): camera state validation
(NYM-CR19-02) and PNG `tRNS` transparency (NYM-CR19-04), and it closes the
runtime verification gap that Task 19 left open.

The historical audits are unchanged. Where this task resolves an earlier
finding, that is stated here; the original reports keep their original
conclusions.

## Implementation summary

### Camera validation

`Nymeria.Graphics.Camera` now validates its state at the two matrix boundaries
instead of inside the render loop:

| Boundary | Rejected state | Exception |
| --- | --- | --- |
| `ViewMatrix` | Non-finite `Position` | `InvalidOperationException` naming `Position` |
| `ViewMatrix` | A finite position whose magnitude cannot express a distinct look target in single precision | `InvalidOperationException` naming the position magnitude |
| `ViewMatrix` | A forward direction that is non-finite or degenerate with the up vector | `InvalidOperationException` describing the view basis |
| `CreateProjectionMatrix` | Aspect ratio that is not positive and finite | `ArgumentOutOfRangeException` naming `aspectRatio` |
| `CreateProjectionMatrix` | `FieldOfViewRadians` not finite and strictly between 0 and π | `InvalidOperationException` naming `FieldOfViewRadians` |
| `CreateProjectionMatrix` | `NearPlane` not finite and positive | `InvalidOperationException` naming `NearPlane` |
| `CreateProjectionMatrix` | `FarPlane` not finite and greater than `NearPlane` | `InvalidOperationException` naming `FarPlane` |
| `CreateProjectionMatrix` | Any parameter set that still yields a non-finite matrix | `InvalidOperationException` describing the projection |

Properties keep their existing getters and setters. No value is silently
replaced and no value is clamped as part of validation; the existing documented
pitch clamp and yaw wrap are unchanged. A camera in a valid state returns exactly
the matrices it returned before this task.

The degenerate view basis cannot be reached through the public API, because
`PitchRadians` clamps below a right angle. The check remains in `ViewMatrix` as a
defence-in-depth guard, and a regression test pins the clamp that prevents it.

Public behavior change: invalid camera configuration now throws a diagnostic
exception instead of returning or uploading a non-finite matrix. This change was
authorized by the task. No other public signature changed.

### PNG `tRNS` transparency

`Nymeria.Formats.Png.PngImageDecoder` now decodes the `tRNS` chunk for the color
types that permit it:

- **Indexed (3):** alpha values are applied to palette entries in order.
  Entries beyond the supplied list stay fully opaque. The chunk must follow
  `PLTE` and carry one to `paletteEntryCount` values.
- **Grayscale (0):** a single 16-bit sample names the transparent gray value.
  Matching pixels get alpha 0, everything else stays opaque.
- **Truecolor (2):** three 16-bit samples name the transparent RGB value.
  Matching pixels get alpha 0, everything else stays opaque.

Samples are compared against the decoded 8-bit samples, before any conversion.
Malformed metadata is rejected with `PngFormatException`:

- a second `tRNS` chunk;
- `tRNS` after `IDAT`;
- indexed `tRNS` before `PLTE`;
- a payload length that does not match the color type, including an empty
  indexed payload;
- more alpha values than palette entries;
- samples outside the 8-bit range (high byte not zero);
- `tRNS` for grayscale-alpha (4) or RGBA (6), where PNG prohibits it;
- truncated chunk payloads, through the existing chunk-length validation.

Resource safeguards are unchanged: the encoded input limit, dimension limit,
decoded payload limit, checked arithmetic, CRC and chunk validation, and bounded
decompression all apply before and during transparency handling. Transparency is
applied in the existing single conversion pass; no intermediate image or second
full-size allocation is introduced. The only additional allocation is one small
copy of the palette alpha list.

The renderer still does not blend alpha. Decoded alpha — including `tRNS` alpha —
is preserved in `TextureData`, but examples submit opaque draws. That limitation
predates this task and is unchanged.

### Example message correction

Runtime validation showed that `HelloMaterials` printed "Esc closes the example",
but no example handles the Escape key; the window close request is the only exit
path. The message now says that closing the window exits.

### Tests added

| Project | Tests | Coverage |
| --- | --- | --- |
| `Nymeria.Graphics.Tests` (new) | 29 | Invalid and non-finite position, unresolvable look target, missing view basis, invalid field of view, near and far planes, aspect ratio, boundary-adjacent valid values, `[0, 1]` depth mapping, view-matrix conventions, movement with valid state |
| `Nymeria.Formats.Png.Tests` | +22 | Indexed partial transparency, single alpha entry, short alpha list, grayscale and truecolor keys, opaque non-matches, missing `tRNS`, wrong lengths, empty indexed payload, too many alpha values, out-of-range samples, prohibited color types, duplicate, misplaced and truncated chunks |

`Nymeria.Graphics.Tests` follows `Nymeria.Core.Tests`: same SDK, target
framework, xunit packages, and project reference. It introduces no third-party
dependency and no abstraction to make `Camera` testable. The camera
implementation was not moved.

## Graphics backend inventory

Nymeria has one implemented graphics path: SDL3 GPU with the **Metal** backend
and MSL shader source. `GpuDevice.Create` explicitly requests the `metal` driver
and rejects a device whose shader formats do not include MSL. `Nymeria.Platform`
ships only the macOS native SDL3 runtime (`SDL3-CS.MacOS`); `Nymeria.Audio` uses
the same SDL3 native library.

| Capability | Windows | macOS | Linux |
| --- | --- | --- | --- |
| Native window creation | Unsupported — no native SDL3 runtime shipped | Supported (SDL3 3.4.18) | Unsupported — no native SDL3 runtime shipped |
| GPU device initialization | Unsupported — no implemented backend | Supported (Metal) | Unsupported — no implemented backend |
| Shader compilation and execution | Unsupported | Supported (MSL compiled by SDL at runtime) | Unsupported |
| Swapchain presentation | Unsupported | Supported | Unsupported |
| Texture upload (RGBA, BC1–BC5, BC7) | Unsupported | Supported | Unsupported |
| Depth buffering | Unsupported | Supported | Unsupported |
| Resize handling | Unsupported | Implemented; not runtime-verified (see below) | Unsupported |
| Resource disposal | Unsupported | Implemented and exercised by `HelloBenchmark` | Unsupported |
| Build verification | CI build passes | CI build passes; local Release build passes | CI build passes |
| Headless test verification | 486 core tests and format tests are GPU-independent | same | same |
| Actual GPU runtime verification | Not available | Performed, see below | Not available |

Windows and Linux compile and pass the GPU-independent tests, but their native
SDL3 runtime and graphics backend are not implemented. This task did not add a
backend, so no support is claimed for them.

Audio playback follows the same rule: only the macOS native SDL3 runtime is
shipped, and the two device-gated audio tests remain skipped without an audio
device.

## Runtime test results

Environment:

- macOS 27.0.1, arm64 (Apple M4, 8-core GPU, Metal 4).
- .NET SDK 10.0.402, Release build of `Nymeria.slnx`.
- Display: SAMSUNG 4608 × 2592 (2304 × 1296 logical, 60 Hz); SDL reported a
  1920 × 1080 physical drawable for the 960 × 540 logical window in
  `HelloBenchmark`.
- Backend: SDL3 GPU, Metal.

| Example | Method | Result | Observed |
| --- | --- | --- | --- |
| `HelloBenchmark` | `--instances 64 --warmup 10 --frames 60`, exits on its own | Passed | Window and Metal device created; all four modes rendered 60 frames each at ~57 fps; 60–64 draw calls and 46 080–49 152 triangles per frame; culling removed 4 of 64 instances; 40 B managed allocation per frame; exit code 0 (clean disposal of renderer, window, and device) |
| `HelloTriangle` | Ran 6 s, no output, then terminated | Passed (window, device, shaders, frame loop) | Window and Metal pipeline initialized; no error output |
| `HelloCube` | Ran 6 s, no output, then terminated | Passed (window, device, depth buffer, uniform updates, frame loop) | No error output |
| `HelloMaterials` | Ran 6 s, then terminated | Passed (window, device, texture upload, frame loop) | Generated checkerboard texture uploaded; no error output |
| `HelloMaterials <tRNS PNG>` | Ran 6 s with a 4 × 4 indexed PNG containing `tRNS`, then terminated | Passed (decode → upload → present) | "Loaded base-color texture ... 4 x 4, 1 mip level(s), 64 RGBA bytes"; no error output |
| `HelloLighting` | Ran 6 s, then terminated | Passed (window, device, lighting path, frame loop) | No error output |
| `HelloLOD` | Ran 6 s, then terminated | Passed | ~57 fps; 48 candidates, 6 distance-culled, 42 draws, 3 598 triangles, LOD usage 3/10/29 per second |
| `HelloAnimation` | Ran 6 s, then terminated | Passed | ~57 fps; clip time advanced, door/wiper/mover poses changed, 8 visible objects |
| `HelloStreaming` | Ran 6 s, then terminated | Passed | ~57 fps; 9 chunks loaded, 0 failed, 99 objects, 52 frustum-culled, 47 draws, 0 pruned GPU entries |
| Invalid camera state | `Nymeria.Graphics.Tests` | Passed | Every documented invalid configuration throws with the property name; valid boundary-adjacent values produce finite matrices |
| PNG `tRNS` decoding | `Nymeria.Formats.Png.Tests` plus an independent encoder probe | Passed | Exact RGBA output, including alpha 0/128/255 and color-key matches, for fixtures produced by an external encoder |

### Blocked or not applicable

- **Visual correctness (Passed frames, unverified pixels).** Frame submission,
  draw counts, and per-frame timings are measured; the agent cannot reliably
  observe the window contents. Confirming that the expected image appears is a
  manual step.
- **Window resize.** Not triggered programmatically, because the examples only
  react to user-driven SDL events. Resize handling (viewport from swapchain
  size, depth-target recreation) is implemented and reviewed, but not
  runtime-verified in this environment. Manual confirmation required.
- **Minimize and restore.** Same reason; not runtime-verified.
- **Clean shutdown of interactive examples.** The examples were terminated with
  `SIGTERM` after the observation window, so their disposal path did not run in
  those runs. Disposal is exercised by `HelloBenchmark`, which exits on its own
  with code 0.
- **Device loss, swapchain acquisition failure, allocation failure.** Not forced.
  `GpuDevice` already releases the window claim and destroys the device when a
  later creation step fails, and the examples cancel the command buffer when no
  swapchain texture is available, but these paths have no automated test seam
  that does not require a generalized graphics mock.
- **Failed GPU initialization and missing native library.** Reachable only on a
  machine without Metal or without the native SDL3 runtime; not reproducible on
  this host.
- **Texture transparency in the rendered image.** The renderer does not blend
  alpha, so `tRNS` affects decoded data only, as documented in
  [MATERIALS.md](MATERIALS.md).

## Verification

Final state on macOS 27.0.1 / arm64, .NET SDK 10.0.402:

- `dotnet restore Nymeria.slnx` — succeeded.
- `dotnet build Nymeria.slnx --configuration Release --no-restore` — 0 warnings,
  0 errors.
- `dotnet test Nymeria.slnx --configuration Release --no-build --no-restore` —
  1 054 passed, 2 skipped, 0 failed (1 056 total) across 14 test projects. The two
  skips are the audio device checks that require hardware.

  | Test project | Passed | Skipped |
  | --- | --- | --- |
  | `Nymeria.Core.Tests` | 486 | 0 |
  | `Nymeria.Formats.O3d.Tests` | 86 | 0 |
  | `Nymeria.Formats.DirectX.Tests` | 75 | 0 |
  | `Nymeria.Formats.Png.Tests` | 64 | 0 |
  | `Nymeria.Formats.Obj.Tests` | 59 | 0 |
  | `Nymeria.Formats.Bmp.Tests` | 54 | 0 |
  | `Nymeria.Formats.Dds.Tests` | 52 | 0 |
  | `Nymeria.Audio.Tests` | 46 | 2 |
  | `Nymeria.Formats.Wav.Tests` | 38 | 0 |
  | `Nymeria.Graphics.Tests` | 29 | 0 |
  | `Nymeria.Formats.Tga.Tests` | 28 | 0 |
  | `Nymeria.Formats.Textures.Tests` | 16 | 0 |
  | `Nymeria.Formats.Jpeg.Tests` | 12 | 0 |
  | `Nymeria.Platform.Tests` | 9 | 0 |

- `dotnet list Nymeria.slnx package --vulnerable --include-transitive` — no
  vulnerable packages.
- Baseline before this task: 0 warnings, 0 errors, 1 003 passed, 2 skipped. The
  51 additional passing tests are the 29 camera tests and the 22 PNG
  transparency tests; no existing test changed behavior.

The independent PNG probe and the fixtures it used were temporary and are not
part of the repository.

## Remaining risks

**Confirmed defects fixed:** camera state could produce non-finite matrices
without a diagnostic (NYM-CR19-02), PNG `tRNS` was ignored (NYM-CR19-04), and
`HelloMaterials` advertised an Escape exit that does not exist.

**Confirmed defects still open:** none known in the reviewed code.

**Unsupported functionality, by design:**

- Windows and Linux native windowing and GPU backends.
- Any backend other than SDL3 Metal, and any shading language other than MSL.
- Alpha blending, which limits `tRNS` to decoded data.

**Unverified runtime behavior:**

- Visual appearance of every example.
- Window resize, minimize/restore, and high-DPI transitions at runtime.
- Device loss, swapchain failure, and allocation failure paths.
- Clean shutdown of the interactive examples under a real window close (only
  `HelloBenchmark` exits through its own disposal path).
- The Windows and Linux runtime, which has no native SDL3 runtime to load.

**Optional future improvements, not authorized here:**

- Example model framing for extreme finite coordinates (NYM-CR19-03).
- Benchmarking or fuzzing jobs in CI.
- Consolidating duplicate bounded-read logic across decoders (NYM-CR19-08).
- The documented 40 B per-frame boxed-enumerator residual (NYM-CR19-05).
