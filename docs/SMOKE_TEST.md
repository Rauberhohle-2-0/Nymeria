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
3. **Cubes render.** Seven cubes are visible against the dark background. Each
   has six distinct solid face colors, clearly perspective-projected edges, and
   no blank or black output. The central cube rotates; the other six stay at
   fixed positions.
4. **Depth occlusion is correct.** Near faces hide the faces behind them, and
   nearer cubes hide farther cubes where they overlap. No back face draws over a
   front face, and the scene reads as solid objects rather than flat triangles or
   a wireframe.
5. **Initial framing is stable.** Without input, the camera does not drift, and
   the cubes keep their positions while only the central cube rotates.
6. **Movement works.** Hold `W` and `S` to move toward and away from the scene
   along the horizontal view direction. Hold `A` and `D` to strafe left and
   right. Movement is smooth and follows the current horizontal facing.
7. **Vertical movement works.** Hold `Space` to rise and `Left Control` to
   descend. Moving up or down does not change the direction used by `W`/`S`.
8. **Mouse look works only while held.** Hold the right mouse button and move the
   mouse. The view turns left/right and up/down, the cursor is hidden, and motion
   continues at the window edge. Release the button and move the mouse again: the
   view must not rotate and the cursor must be visible.
9. **Capture recovery works.** Hold the right mouse button, then focus another
   application (for example with `Cmd+Tab`) while still holding it. The cursor
   must be restored. Minimize and restore the window while looking. Shut down
   normally. In every case the cursor is visible and no capture is left behind.
10. **Sprint works.** Hold `Shift` with a movement key. Movement is visibly faster
    than without `Shift`.
11. **Diagonal movement is normalized.** Hold `W`+`D` and compare with `D` alone.
    Diagonal speed must not exceed single-axis speed. Do the same with
    `W`+`Space`.
12. **Timing is stable.** Movement speed looks consistent at the normal frame
    rate. Drag the window or switch focus to stall the process briefly, then
    return: the camera must resume without a large jump.
13. **Pitch is clamped.** Look straight up and straight down. The view stops short
    of vertical and never flips, spins, or becomes degenerate.
14. **Resize works.** Drag a corner or edge, including to a very wide and a very
    tall window. Cube proportions stay correct, the scene is not stretched, and
    the window stays responsive without a stale aspect ratio.
15. **High-DPI works.** On a Retina display, cube edges are drawn at the display's
    native pixel density rather than an upscaled low-resolution image.
16. **Minimize and restore work.** Minimize the window for a few seconds, then
    restore it. The scene reappears with correct depth and proportions, and the
    process does not crash or freeze while minimized.
17. **Close button exits.** Click the red close button. The process ends, the
    terminal prompt returns, and `echo $?` reports `0`. The cursor is visible.
18. **Standard quit exits.** Launch again, focus the window, and press `Cmd+Q`.
    The process ends with status code `0`.
19. **Repeated cycles work.** Repeat launch and close at least three times. Each
    launch opens one window; each close leaves no orphan process or depth
    resource warning.
20. **No busy-spin.** The loop renders continuously at about 60 frames per
    second, so CPU use is higher than the idle triangle, but it must stay well
    below one full core. Activity Monitor must not show a pegged core, and memory
    use must stay stable over several minutes.
21. **No unhandled exceptions.** No exception text appears in the terminal during
    normal launch, movement, mouse look, resize, minimize, restore, or shutdown.

macOS notes:

- The movement keys are read by physical scancode, so `W`/`A`/`S`/`D` occupy the
  same positions on other keyboard layouts.
- Use a two-finger or configured secondary click on a trackpad for mouse look.
  If secondary click is mapped to Control-click, that click also presses the
  key used to descend.
- If the system or a remote desktop session refuses relative mouse mode, mouse
  look still follows relative motion but the cursor may remain visible and
  rotation stops at the screen edge. Record this as a limitation rather than a
  rendering failure.

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
