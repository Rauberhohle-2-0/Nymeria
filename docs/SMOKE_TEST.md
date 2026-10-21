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
dotnet run --project examples/HelloOBJ/HelloOBJ.csproj
dotnet run --project examples/HelloO3D/HelloO3D.csproj
dotnet run --project examples/HelloScene/HelloScene.csproj
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj
```

The shaders are `examples/HelloTriangle/Shaders/Triangle.metal`,
`examples/HelloCube/Shaders/Cube.metal`, the shared
`examples/Shared/Shaders/Model.metal`, and
`examples/HelloMaterials/Shaders/Material.metal`. The model shader is embedded in
`HelloOBJ`, `HelloO3D`, and `HelloScene` as
`Nymeria.Examples.Shaders.Model.metal`; the material shader is embedded as
`HelloMaterials.Shaders.Material.metal`. Shaders are embedded in their examples
and compiled by SDL's Metal backend at startup, so there is no shader compiler
step and no external shader file to deploy.

Run each example checklist separately. The failure-path checks at the end apply
to all six.

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

## HelloOBJ checks

1. **Window appears.** With no arguments, a native window opens without a
   console error. The checked-in `Assets/cube.obj` fixture is copied beside the
   built executable, so this works from any current directory.
2. **Title is correct.** The title bar reads `Nymeria`.
3. **Model renders.** A cube is visible against the dark background with six
   distinct solid face colors and clearly perspective-projected edges. The output
   is not blank or black.
4. **Depth occlusion is correct.** Near faces hide the faces behind them. The
   cube reads as a solid object rather than a flat set of triangles.
5. **Camera framing is stable.** Without input, the camera does not drift and the
   model stays visible.
6. **Movement and mouse look work.** The shared controls behave as in
   `HelloCube`: `W`/`A`/`S`/`D`, `Space`, `Left Control`, `Shift`, and held right
   mouse button for look. Releasing the button restores the cursor.
7. **Resize works.** Drag a corner or edge, including to a very wide and a very
   tall window. Proportions stay correct, the scene is not stretched, and the
   window remains responsive.
8. **High-DPI works.** On a Retina display, cube edges are drawn at the display's
   native pixel density rather than an upscaled low-resolution image.
9. **Minimize and restore work.** Minimize the window for a few seconds, then
   restore it. The scene reappears with correct depth and proportions, and the
   process does not crash or freeze while minimized.
10. **User-supplied path works.** Run the example with the fixture path passed
    explicitly, for example
    `dotnet run --project examples/HelloOBJ/HelloOBJ.csproj -- examples/HelloOBJ/Assets/cube.obj`.
    The same cube renders.
11. **Close button exits.** Click the red close button. The process ends, the
    terminal prompt returns, and `echo $?` reports `0`. The cursor is visible.
12. **Standard quit exits.** Launch again, focus the window, and press `Cmd+Q`.
    The process ends with status code `0`.
13. **No busy-spin.** The loop renders continuously at about 60 frames per
    second, so CPU use is higher than the idle triangle, but it must stay well
    below one full core. Activity Monitor must not show a pegged core, and memory
    use must stay stable over several minutes.
14. **No unhandled exceptions.** No exception text appears in the terminal during
    normal launch, movement, mouse look, resize, minimize, restore, or shutdown.

## HelloO3D checks

1. **Window appears.** With no arguments, a native window opens without a
   console error. The checked-in synthetic `Assets/cube.o3d` fixture is copied
   beside the built executable, so this works from any current directory.
2. **Title is correct.** The title bar reads `Nymeria`.
3. **Model renders.** A cube is visible against the dark background with six
   distinct solid face colors and clearly perspective-projected edges. The output
   is not blank, black, or stretched.
4. **Depth occlusion is correct.** Near faces hide the faces behind them. The
   cube reads as a solid object rather than a flat set of triangles.
5. **Camera framing is stable.** Without input, the camera does not drift and the
   model stays visible.
6. **Movement and mouse look work.** The shared controls behave as in
   `HelloCube`: `W`/`A`/`S`/`D`, `Space`, `Left Control`, `Shift`, and held right
   mouse button for look. Releasing the button restores the cursor.
7. **Resize works.** Drag a corner or edge, including to a very wide and a very
   tall window. Proportions stay correct, the scene is not stretched, and the
   window remains responsive.
8. **High-DPI works.** On a Retina display, cube edges are drawn at the display's
   native pixel density rather than an upscaled low-resolution image.
9. **Minimize and restore work.** Minimize the window for a few seconds, then
   restore it. The scene reappears with correct depth and proportions, and the
   process does not crash or freeze while minimized.
10. **User-supplied path works.** Run the example with the fixture path passed
    explicitly, for example
    `dotnet run --project examples/HelloO3D/HelloO3D.csproj -- examples/HelloO3D/Assets/cube.o3d`.
    The same cube renders. If you also have a lawfully obtained unscrambled
    static OMSI model, record what it shows, including any diagnostic; a failure
    there is useful evidence, not a checklist pass.
11. **Close button exits.** Click the red close button. The process ends, the
    terminal prompt returns, and `echo $?` reports `0`. The cursor is visible.
12. **Standard quit exits.** Launch again, focus the window, and press `Cmd+Q`.
    The process ends with status code `0`.
13. **No busy-spin.** The loop renders continuously at about 60 frames per
    second, so CPU use is higher than the idle triangle, but it must stay well
    below one full core. Activity Monitor must not show a pegged core, and memory
    use must stay stable over several minutes.
14. **No unhandled exceptions.** No exception text appears in the terminal during
    normal launch, movement, mouse look, resize, minimize, restore, or shutdown.

## HelloScene checks

1. **Window appears.** A native window opens without a console error. The
   terminal prints the `E`/`Q`/`R` control summary.
2. **Title is correct.** The title bar reads `Nymeria`.
3. **Objects render.** Four cubes are visible against the dark background with
   six distinct solid face colors and perspective-projected edges. They have
   different positions, orientations, and scales, including at least one visibly
   non-uniform scale. The output is not blank or black.
4. **Shared mesh is visually consistent.** All four objects use the same cube
   mesh and color mapping; only their transforms differ.
5. **Depth occlusion is correct.** Near faces hide the faces behind them, and
   nearer cubes hide farther cubes where they overlap. The scene reads as solid
   objects.
6. **Camera framing is stable.** Without input, the camera does not drift and the
   initial objects stay visible.
7. **Movement and mouse look work.** The shared controls behave as in
   `HelloCube`: `W`/`A`/`S`/`D`, `Space`, `Left Control`, `Shift`, and held right
   mouse button for look. Releasing the button restores the cursor.
8. **Add works.** Press `E` several times. One new cube appears per press at a
   deterministic position and scale. Holding `E` must not add a continuous
   stream of objects; release and press again to add another.
9. **Remove works.** Press `Q` several times. The last object is removed each
   time while the remaining objects stay in place. Press `Q` until the scene is
   empty: the window must keep clearing to the dark background without crashing.
10. **Reset works.** With the initial objects removed or extra objects added,
    press `R`. The scene returns to the original four objects and the add/remove
    sequence restarts.
11. **Resize works.** Drag a corner or edge, including to a very wide and a very
    tall window. Proportions stay correct, the scene is not stretched, and the
    window remains responsive.
12. **High-DPI works.** On a Retina display, cube edges are drawn at the display's
    native pixel density rather than an upscaled low-resolution image.
13. **Minimize and restore work.** Minimize the window for a few seconds, then
    restore it. The scene reappears with correct depth and proportions, and the
    process does not crash or freeze while minimized.
14. **Close button exits.** Click the red close button. The process ends, the
    terminal prompt returns, and `echo $?` reports `0`. The cursor is visible.
15. **Standard quit exits.** Launch again, focus the window, and press `Cmd+Q`.
    The process ends with status code `0`.
16. **No busy-spin.** The loop renders continuously at about 60 frames per
    second, so CPU use is higher than the idle triangle, but it must stay well
    below one full core. Memory use stays stable while adding and removing the
    same shared mesh repeatedly; it must not grow per instance or per frame.
17. **No unhandled exceptions.** No exception text appears in the terminal during
    normal launch, movement, mouse look, add, remove, reset, resize, minimize,
    restore, or shutdown.

## HelloMaterials checks

1. **Window appears.** With no arguments, a native window opens without a console
   error. The terminal reports that the generated checkerboard texture is in use.
2. **Title is correct.** The title bar reads `Nymeria`.
3. **Materials render.** Four cubes are visible against the dark background. Two
   show a four-quadrant checkerboard, one shows the same pattern with a blue tint,
   and one is solid orange. The output is not blank, black, stretched, or
   untextured.
4. **Texture orientation is correct.** On each checkerboard face seen from
   outside, the top-left quadrant is red, the top-right is green, the bottom-left
   is blue, and the bottom-right is yellow. Rotate the camera around the cubes and
   confirm the pattern is not mirrored vertically or horizontally.
5. **Shared material and mesh are consistent.** The two plain checkerboard cubes
   use the same pattern, scale, and colors; only their transforms differ. The
   renderer uploads the mesh and texture once, not once per object.
6. **Tint combines with the texture.** The blue-tinted cube shows the same
   checkerboard pattern darker and blue-shifted, not a flat color. Its bright
   cells must still be distinguishable.
7. **Solid material uses the fallback.** The orange cube is uniformly orange with
   no checker pattern, showing that an untextured material samples the white
   fallback and displays its base color directly.
8. **Depth occlusion is correct.** Near faces hide faces behind them, and nearer
   cubes hide farther cubes where they overlap. The scene reads as solid objects.
9. **Movement and mouse look work.** The shared controls behave as in `HelloCube`:
   `W`/`A`/`S`/`D`, `Space`, `Left Control`, `Shift`, and held right mouse button
   for look. Releasing the button restores the cursor.
10. **Camera framing is stable.** Without input, the camera does not drift and all
    four objects stay visible.
11. **Resize works.** Drag a corner or edge, including to a very wide and a very
    tall window. Proportions stay correct, textures keep their orientation, and
    the window remains responsive.
12. **High-DPI works.** On a Retina display, cube and texture edges are drawn at
    the display's native pixel density rather than an upscaled low-resolution
    image.
13. **Minimize and restore work.** Minimize the window for a few seconds, then
    restore it. The textured scene reappears correctly and the process does not
    crash or freeze while minimized.
14. **User-supplied PNG works.** Run the example with a valid PNG path, for
    example a 64 × 64 or 256 × 256 image with an obvious asymmetric pattern. The
    terminal reports the decoded dimensions and byte count, and both checkerboard
    materials show that image. The blue tint still multiplies it.
15. **Invalid texture falls back.** Run the example with a missing path and then a
    malformed or oversized PNG. The terminal prints a clear diagnostic naming the
    path and reason, uses the generated checkerboard, and the window still opens.
    Close it normally; the process exits with status code `0`.
16. **Close button exits.** Click the red close button. The process ends, the
    terminal prompt returns, and `echo $?` reports `0`. The cursor is visible.
17. **Standard quit exits.** Launch again, focus the window, and press `Cmd+Q`.
    The process ends with status code `0`.
18. **Repeated cycles work.** Repeat launch and close at least three times, with
    and without a PNG path. Each launch opens one window; each close leaves no
    orphan process.
19. **No busy-spin or per-frame uploads.** CPU use stays well below one full core
    and memory use stays stable for several minutes. The texture and mesh are
    uploaded once at first use, not every frame.
20. **No unhandled exceptions.** No exception text appears in the terminal during
    normal launch, movement, mouse look, resize, minimize, restore, PNG load,
    fallback, or shutdown.

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
- **Missing OBJ file:** run `HelloOBJ` with a path that does not exist. It must
  print a clear "not found" message and exit with status code `1` before a window
  is created.
- **Malformed OBJ file:** create a temporary file outside tracked paths with, for
  example, a face that references an undefined position. `HelloOBJ` must print
  the source line and offending reference, then exit with status code `1`
  without an unhandled exception.
- **Missing O3D file:** run `HelloO3D` with a path that does not exist. It must
  print a clear "not found" message and exit with status code `1` before a window
  is created.
- **Malformed O3D file:** create a temporary file outside tracked paths with, for
  example, the same header as the fixture but a truncated vertex section.
  `HelloO3D` must print the byte offset and the reason, then exit with status
  code `1` without an unhandled exception or partial geometry.
- **Unsupported O3D feature:** if you have a scrambled, bone, or animation O3D
  file, `HelloO3D` must reject it with a message that names the unsupported
  feature, then exit with status code `1`. Do not treat a guessed or partially
  drawn result as a pass.

## Recording results

Report the macOS version, architecture (`arm64` or `x86_64`), GPU model, date, and
pass/fail for each check, separately for `HelloTriangle`, `HelloCube`,
`HelloOBJ`, `HelloO3D`, and `HelloScene`. Do not describe this manual checklist
as a CI result.
State separately that the checked-in `HelloO3D` fixture is synthetic and that
real OMSI content was not available for validation.
