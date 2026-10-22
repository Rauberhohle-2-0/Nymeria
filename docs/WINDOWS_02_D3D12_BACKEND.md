# Windows W02 — Direct3D 12 backend selection

This document records the platform-aware GPU backend policy added by Windows task
W02, the SDL3 calls it uses, what was actually executed, and where W02 ends and
W03 begins.

W02 changes device initialization only. It does **not** port shaders, and it does
not claim that rendering works on Windows.

## Scope

- Replace the hardcoded Metal-only device initialization with a small, testable
  platform policy.
- Windows requests **DXIL** and SDL selects **Direct3D 12**.
- macOS keeps **Metal** with **MSL** source, unchanged.
- Linux/Vulkan stays a documented future target and is rejected explicitly.

## Verified platform mapping

| Platform | Requested shader format | Expected SDL driver | Verified against the created device |
| --- | --- | --- | --- |
| macOS | `SDL_GPUShaderFormat.MSL` | `metal` | driver name and `SDL_GetGPUShaderFormats` |
| Windows | `SDL_GPUShaderFormat.DXIL` | `direct3d12` | driver name and `SDL_GetGPUShaderFormats` |
| Linux | none — no policy | none | rejected before any SDL GPU call |
| Other | none — no policy | none | rejected before any SDL GPU call |

The table is the policy implemented in `GpuBackendSelection.TrySelectPolicy`, and
each row is asserted by unit tests. "Verified against the created device" means
the policy is also enforced at runtime, not only declared.

## SDL3 API calls and why the driver is chosen this way

`GpuDevice.Create` now performs, in order:

1. `SDL_GPUSupportsShaderFormats(formats, NULL)` — probes whether any driver
   compiled into the loaded SDL library accepts the platform's format.
2. `SDL_CreateGPUDevice(formats, debug: false, NULL)` — creates the device.
3. `SDL_GetGPUDeviceDriver(device)` — reads the driver SDL actually selected.
4. `SDL_GetGPUShaderFormats(device)` — reads the bytecode formats that driver
   accepts.
5. `SDL_ClaimWindowForGPUDevice` and `SDL_GetGPUSwapchainTextureFormat` —
   unchanged from before.

**No driver name is hardcoded.** A `NULL` driver name lets SDL select the backend,
and the requested shader format constrains that selection to the driver that can
consume it: only Metal consumes MSL, and only Direct3D 12 consumes DXIL. This
keeps SDL as the abstraction the architecture depends on, avoids pinning a driver
name Nymeria would have to track, and means a wrong outcome is detected rather
than assumed.

The pinned SDL3 3.4.18 binary does not require an explicit driver name for this to
work, so the `"direct3d12"` string is used only as the **expected value** in
diagnostics and verification, never as an input.

Because the request alone is not proof, step 3 and step 4 are enforced: a device
reporting another driver, or lacking the policy format, is destroyed before it
claims the window or builds any resource.

## Device capability versus shader bytecode

These are deliberately separated:

- `GpuDevice.ShaderFormat` is the **device capability** — the format validated
  against `SDL_GetGPUShaderFormats`.
- `GpuDevice.CreateShader(byte[], SDL.GPUShaderFormat, ...)` takes an explicit
  **bytecode format** and rejects it unless it equals `ShaderFormat`.

The pre-existing `CreateShader(byte[], stage, entryPoint, ...)` overload keeps its
signature and behaviour and now documents that it submits MSL source, delegating
to the explicit overload with `SDL.GPUShaderFormat.MSL`.

This means MSL bytes are never relabelled as DXIL. On a Direct3D 12 device the
MSL overload throws a clear error instead of handing MSL source to SDL as DXIL.
That error is the documented W02→W03 boundary.

## What changed

| File | Change |
| --- | --- |
| `src/graphics/Nymeria.Graphics/GpuBackendSelection.cs` | New internal policy component: platform detection, per-platform policy, device-policy verification, shader-code format check, and diagnostic text. Contains no SDL calls, so it is unit-testable. |
| `src/graphics/Nymeria.Graphics/GpuDevice.cs` | Uses the policy instead of hardcoded MSL/`"metal"`; verifies driver and formats after creation; exposes `DriverName` and `ShaderFormat`; adds the explicit-format `CreateShader` overload; adds `DescribeAvailableDrivers` for error paths. |
| `src/graphics/Nymeria.Graphics/Nymeria.Graphics.csproj` | `InternalsVisibleTo` for the test project. |
| `tests/Nymeria.Graphics.Tests/GpuBackendSelectionTests.cs` | 16 GPU-free tests of the policy. |
| `README.md`, `examples/README.md`, `docs/ARCHITECTURE.md`, `docs/SMOKE_TEST.md`, `docs/WINDOWS_01_NATIVE_SDL3.md` | Correct the statements that are no longer true. |

Untouched: `Nymeria.Core`, `Nymeria.Formats.*`, `Nymeria.Audio`,
`Nymeria.Platform`, every `.metal` source, every renderer, and every pipeline.

## Diagnostics

The five failure classes are distinguished by separate messages:

| Class | Produced by |
| --- | --- |
| 1. Native SDL3 runtime cannot be loaded | unchanged `DllNotFoundException` handling in the examples (W01) |
| 2. Backend or DXIL unsupported | `CreateNoBackendMessage` — names the platform, the required format, the expected driver, the drivers compiled into SDL, and the SDL error |
| 3. Device creation fails | `CreateDeviceCreationFailureMessage` — includes the SDL error and the compiled driver list |
| 4. Device reports an unexpected driver or format | `CreateDriverMismatchMessage` and `CreateDeviceFormatMismatchMessage` — name the actual driver, the actual formats, and the expected values |
| 5. Device creation succeeds but shader creation fails | `CreateShaderCodeMismatchMessage` — names the driver, the required format, the supplied format, and that shader assets for this backend are pending |

Diagnostic output captures the selected driver and the shader format in classes 4
and 5. `GpuDevice.DriverName` and `GpuDevice.ShaderFormat` expose them
programmatically; no console backend line was added, because examples delegate
device ownership to their renderers and changing that surface was out of scope.

## Automated verification

Environment: macOS 27.0.1 arm64, .NET SDK 10.0.402.

| Command | Result |
| --- | --- |
| `dotnet build Nymeria.slnx -c Release --no-incremental` | **0 warnings, 0 errors** |
| `dotnet test Nymeria.slnx -c Release --no-build` | **1098 passed, 2 skipped, 0 failed** (1082 before this task, plus 16 new) |
| `dotnet test tests/Nymeria.Graphics.Tests` | 45 passed, 0 skipped, 0 failed |

The 2 skipped tests are the pre-existing device-dependent audio checks.

New tests assert, without a GPU or a native library:

- Windows selects DXIL with the `direct3d12` driver.
- Windows requests neither MSL, metallib, nor SPIR-V.
- macOS selects MSL with the `metal` driver.
- macOS requests neither DXIL, DXBC, nor SPIR-V.
- Linux and unknown platforms select **no** backend and do not fall back.
- `DeviceMatchesPolicy` requires both the expected driver and the policy format,
  accepts an additional legacy format, and rejects a wrong driver, a wrong format,
  and a null driver.
- Neither platform accepts the other platform's device.
- `AcceptsShaderCode` rejects MSL bytes on a DXIL device, DXIL bytes on an MSL
  device, and anything on a device with no format.
- `Contains` does not treat the zero value as present.
- Format names, the stable format list, and every diagnostic message contain the
  expected actionable text.

## macOS manual verification

Run on the same macOS host, Release build:

| Example | Method | Result |
| --- | --- | --- |
| `HelloTriangle` | ran for 5 s, then terminated | process stayed alive, **empty stderr** |
| `HelloMaterials` | ran for 5 s, then terminated | process stayed alive, **empty stderr**, normal startup output printed |

Both examples catch `InvalidOperationException` and exit with status code `1` if
device, shader, or pipeline creation fails, so remaining alive with no stderr is
evidence that Metal was selected, `SDL_GetGPUDeviceDriver` returned `metal`,
`SDL_GetGPUShaderFormats` included MSL (otherwise the new verification would have
thrown), and the MSL shaders and pipelines compiled.

**Not claimed:** no visual inspection was performed, so image correctness, texture
orientation, blending order, and resize behavior were not re-validated by this
task. They are covered by the existing macOS checklist in
[SMOKE_TEST.md](SMOKE_TEST.md).

## Windows manual verification — not performed

**No Windows process was started.** No Windows machine was available, so the
following remain open acceptance items:

- `SDL3.dll` loading and SDL video initialization on Windows.
- `SDL_GPUSupportsShaderFormats(DXIL, NULL)` returning true on real hardware.
- SDL selecting the `direct3d12` driver, and `SDL_GetGPUDeviceFormats` reporting
  DXIL.
- Any GPU example reaching shader creation and failing there with the expected
  Class 5 message.

The expected Windows outcome is **device creation success followed by a shader
creation failure naming `direct3d12`, DXIL, and MSL**. A Metal, driver, or
device-creation failure on Windows is a W02 defect, not an expected limitation.

## Unresolved issues

- Whether SDL selects `direct3d12` when only DXIL is requested with a null driver
  name is unverified on Windows. The post-creation check turns a different choice
  into an explicit error rather than a silent fallback.
- Whether Direct3D 12 accepts unsigned DXIL through SDL is unverified; it belongs
  to W03, where DXIL bytecode first exists.
- No Windows GPU rendering is implemented.

## Boundary between W02 and W03

**W02** owns backend selection, device creation, verification, and the shader-code
format guard. **W03** owns producing DXIL, embedding it, routing shader assets by
device format, and the first visual Windows run.

Consequently, W02 is done when the device is correctly created and verified; a
Windows GPU example stopping at shader creation is the expected outcome, not a
W02 failure.

## No shader pipeline was introduced

W02 added no HLSL, no DXIL binaries, no shader-compilation target, no build-time
toolchain, no shader asset routing, and no Vulkan migration. `Directory.Build.*`
is unchanged, no dependency was added or upgraded, and every `.metal` file is
byte-identical to the previous commit.
