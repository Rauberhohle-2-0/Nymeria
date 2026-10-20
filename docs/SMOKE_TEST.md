# macOS Manual Smoke Test: Window and GPU Examples

This checklist validates behavior that headless CI cannot verify. Run it on a
macOS machine after the solution builds. Record the macOS version, CPU
architecture, GPU model, and date with the result.

## Setup

From the repository root:

```bash
dotnet restore Nymeria.slnx
dotnet build Nymeria.slnx
dotnet run --project examples/HelloTriangle/HelloTriangle.csproj
dotnet run --project examples/HelloCube/HelloCube.csproj
```

The shaders are `examples/HelloTriangle/Shaders/Triangle.metal` and
`examples/HelloCube/Shaders/Cube.metal`. They are embedded in their examples and
compiled by SDL's Metal backend at startup, so there is no shader compiler step
and no external shader file to deploy.

Run the triangle and cube checklists separately. The failure-path checks at the
end apply to both.

## HelloTriangle checks

1. **Window appears.** A native window opens without a console error.
2. **Title is correct.** The title bar reads `Nymeria`.
3. **Triangle renders.** The client area shows one clearly visible triangle with
   red, green, and blue corners on a dark background. The client area is
   approximately 960 × 540 and the triangle is not blank, black, or stretched.
4. **Resize works.** Drag a corner or edge, including to a very wide and a very
   tall window. The triangle stays centered and keeps its proportions inside a
   square area. The window remains responsive and does not crash or hang.
5. **High-DPI works.** On a Retina display, the triangle edges are drawn at the
   display's native pixel density rather than an upscaled low-resolution image.
6. **Minimize and restore work.** Minimize the window, leave it minimized for a
   few seconds, then restore it. The triangle reappears and the process does not
   crash or freeze while minimized.
7. **Close button exits.** Click the red close button. The process ends, the
   terminal prompt returns, and `echo $?` reports `0`.
8. **Standard quit exits.** Launch again, focus the window, and press `Cmd+Q`.
   The process ends with status code `0`.
9. **Repeated cycles work.** Repeat launch and close at least three times. Each
   launch opens one window; each close leaves no orphan process.
10. **No busy-spin.** While the window is idle and focused, CPU use in Activity
    Monitor stays low. The loop waits for events and redraws at most about ten
    times per second; it must not peg a core. Memory use stays stable while idle.
11. **No unhandled exceptions.** No exception text appears in the terminal during
    normal launch, resize, minimize, restore, or shutdown.

## HelloCube checks

1. **Window appears.** A native window opens without a console error.
2. **Title is correct.** The title bar reads `Nymeria`.
3. **Cube renders.** A cube is visible in the center of a dark background. It
   has six distinct solid face colors, clearly perspective-projected edges, and
   no blank or black output. If the initial orientation hides some faces, wait
   for it to rotate.
4. **Depth occlusion is correct.** Near faces hide the faces behind them. No
   back face draws over a front face, and the cube reads as a solid object rather
   than a flat arrangement of triangles or a wireframe.
5. **Rotation is smooth and continuous.** The cube turns steadily around its
   vertical axis with a fixed tilt, completing a full turn in roughly seven
   seconds. It does not stutter, jump, or stop. Motion uses elapsed time, so it
   should look the same on a slower or busier machine.
6. **Resize works.** Drag a corner or edge, including to a very wide and a very
   tall window. The cube keeps its proportions instead of stretching, stays
   centered, and is not clipped. The window stays responsive and does not crash,
   hang, or show a stale aspect ratio.
7. **High-DPI works.** On a Retina display, the cube edges are drawn at the
   display's native pixel density rather than an upscaled low-resolution image.
8. **Minimize and restore work.** Minimize the window for a few seconds, then
   restore it. The cube reappears with correct depth and proportions, and the
   process does not crash or freeze while minimized.
9. **Close button exits.** Click the red close button. The process ends, the
   terminal prompt returns, and `echo $?` reports `0`.
10. **Standard quit exits.** Launch again, focus the window, and press `Cmd+Q`.
    The process ends with status code `0`.
11. **Repeated cycles work.** Repeat launch and close at least three times. Each
    launch opens one window; each close leaves no orphan process or depth
    resource warning.
12. **No busy-spin.** The loop renders continuously at about 60 frames per
    second, so CPU use is higher than the idle triangle, but it must stay well
    below one full core. Activity Monitor must not show a pegged core, and memory
    use must stay stable over several minutes.
13. **No unhandled exceptions.** No exception text appears in the terminal during
    normal launch, rotation, resize, minimize, restore, or shutdown.

Check the exit code after a normal close:

```bash
echo $?
```

Expected: `0`.

## Failure-path checks (optional)

These checks are optional and must not modify tracked files.

- **Missing native library:** make the restored native SDL3 library unavailable
  to the process. Startup must print an actionable message and exit with status
  code `1`, not hang or report success.
- **Missing embedded shader:** only test by temporarily breaking the build
  outside tracked files. Startup must name the missing shader resource and exit
  with status code `1`.
- **Unavailable Metal backend:** run on a Mac without a Metal-capable GPU if one
  is available. Startup must report that the Metal backend is required and exit
  with status code `1`; it must not fall back to software rendering.

## Recording results

Report the macOS version, architecture (`arm64` or `x86_64`), GPU model, date, and
pass/fail for each check, separately for `HelloTriangle` and `HelloCube`. Do not
describe this manual checklist as a CI result.
