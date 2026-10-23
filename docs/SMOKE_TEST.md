# Manual Smoke Test: Window, GPU, and Audio Examples

This checklist validates behavior that headless CI cannot verify. Run the macOS
sections on a macOS machine and the
[Windows section](#windows-smoke-test) on a Windows machine. Record the macOS
version, CPU architecture, GPU model, audio output device, and date with the
result.

## Setup

From the repository root:

```bash
dotnet restore Nymeria.slnx
dotnet build Nymeria.slnx
dotnet run --project examples/HelloTriangle/HelloTriangle.csproj
dotnet run --project examples/HelloCube/HelloCube.csproj
dotnet run --project examples/HelloOBJ/HelloOBJ.csproj
dotnet run --project examples/HelloO3D/HelloO3D.csproj
dotnet run --project examples/HelloX/HelloX.csproj
dotnet run --project examples/HelloScene/HelloScene.csproj
dotnet run --project examples/HelloMaterials/HelloMaterials.csproj
dotnet run --project examples/HelloLighting/HelloLighting.csproj
dotnet run --project examples/HelloCulling/HelloCulling.csproj
dotnet run --project examples/HelloCollision/HelloCollision.csproj
dotnet run --project examples/HelloAudio/HelloAudio.csproj
dotnet run --project examples/HelloAnimation/HelloAnimation.csproj
dotnet run --project examples/HelloLOD/HelloLOD.csproj
dotnet run --project examples/HelloStreaming/HelloStreaming.csproj
dotnet run --project examples/HelloPicking/HelloPicking.csproj
dotnet run --project examples/HelloBenchmark/HelloBenchmark.csproj \
  -- --instances 128 --warmup 10 --frames 60
```

The shaders are `examples/HelloTriangle/Shaders/Triangle.metal`,
`examples/HelloCube/Shaders/Cube.metal`, the shared
`examples/Shared/Shaders/Model.metal`, and the shared
`examples/Shared/Shaders/Material.metal`. The model shader is embedded in
`HelloOBJ`, `HelloO3D`, `HelloX`, and `HelloScene` as
`Nymeria.Examples.Shaders.Model.metal`; the material shader is embedded in
`HelloMaterials`, `HelloLighting`, `HelloCulling`, `HelloCollision`, `HelloLOD`,
`HelloStreaming`, `HelloPicking`, `HelloAudio`, `HelloAnimation`, and
`HelloBenchmark` as `Nymeria.Examples.Shaders.Material.metal`. On macOS those MSL
sources are embedded in their examples and compiled by SDL's Metal backend at
startup, so there is no shader compiler step and no external shader file to
deploy.

Each family also ships an HLSL equivalent (`Triangle.hlsl`, `Cube.hlsl`,
`Shared/Shaders/Model.hlsl`, and `Shared/Shaders/Material.hlsl`). A **Windows**
build compiles them to DXIL with the provisioned DirectX Shader Compiler and
embeds one `.vert.dxil` and one `.frag.dxil` per family, so every GPU example has
bytecode for Direct3D 12. That compiler runs only on Windows, uses the
checksum-verified compiler the build provisions (or `dxc` on `PATH`, or
`-p:DxcPath=<path to dxc.exe>`), and produces no committed artifact. macOS still
needs no shader compiler. See
[WINDOWS_03_HLSL_DXIL_SHADERS.md](WINDOWS_03_HLSL_DXIL_SHADERS.md) and
[WINDOWS_04_RENDERING_COMPATIBILITY.md](WINDOWS_04_RENDERING_COMPATIBILITY.md).

Run each interactive example checklist separately. The failure-path checks at
the end apply to all fourteen interactive examples; `HelloBenchmark` has its own
automated checks below.

## Windows smoke test

This section validates native SDL3 deployment, window creation, and the Windows
render path. `GpuDevice` selects Direct3D 12 with DXIL, and every shader family
supplies DXIL generated from HLSL at build time, so every GPU example is expected
to draw.

**The Windows build and headless tests are verified in CI; Windows rendering is
not.** The `windows-latest` job restores, builds, and tests the solution, which
provisions the pinned DirectX Shader Compiler and compiles every HLSL shader
family to DXIL. No task has yet run a GPU example on Windows hardware, so shader
acceptance by the driver, visual output, audio playback, and input remain manual
checks. [WINDOWS_04_RENDERING_COMPATIBILITY.md](WINDOWS_04_RENDERING_COMPATIBILITY.md)
and [WINDOWS_06_CI_STABILIZATION.md](WINDOWS_06_CI_STABILIZATION.md) record
exactly what was and was not executed. Treat the rendering checks below as the
expected behavior to confirm, not as a recorded result.

Setup:

1. **Build.** From the repository root run `dotnet build Nymeria.slnx`. It must
   report no warnings and no errors. The build compiles
   `examples/HelloTriangle/Shaders/Triangle.hlsl` to DXIL; on a machine without a
   shader compiler it downloads a pinned, checksum-verified DirectX Shader
   Compiler once (about 32 MB) and prints a `Nymeria:` line naming the compiler it
   used. Later builds reuse the cache. An installed `dxc` on `PATH` or
   `-p:DxcPath=<path to dxc.exe>` takes precedence; see
   [WINDOWS_03_1_DXC_PROVISIONING.md](WINDOWS_03_1_DXC_PROVISIONING.md) if the
   download is blocked.
2. **Native library is deployed.** Confirm that
   `examples/HelloWindow/bin/Debug/net10.0/runtimes/win-x64/native/SDL3.dll`
   exists (use `win-arm64` or `win-x86` on those architectures), or that
   `SDL3.dll` sits beside `HelloWindow.exe` after
   `dotnet build -r win-x64`.
3. **Run the checks below.**

### HelloWindow checks

1. **Smoke run passes.** Run
   `dotnet run --project examples/HelloWindow/HelloWindow.csproj -- --smoke`.
   The console prints one `host:` line, one `window:` line, `event pump: 30
   cycles`, and `window smoke test passed`. No `DllNotFoundException` or
   `BadImageFormatException` appears, and a normal exit reports status code `0`.
2. **Reported sizes are plausible.** The `window:` line shows `960x540 logical`
   and a drawable size of 960 × 540 at 100% display scaling, or a larger value
   at higher scaling.
3. **Interactive run works.** Run the example with no arguments. The window
   appears, resizes, and stays responsive. Closing it prints
   `window closed by the user` and exits with status code `0`.
4. **No native copy is required.** Run the example from a clean build without
   placing any DLL in the output directory by hand.

### HelloTriangle checks on Windows

1. **The device uses Direct3D 12 with DXIL.** Running
   `dotnet run --project examples/HelloTriangle/HelloTriangle.csproj` must not
   report a Metal, driver, format, or device-creation failure.
2. **The triangle renders.** A window opens containing one triangle with a red top
   corner and green and blue bottom corners on the dark clear color. There is no
   shader or pipeline creation error. Resizing keeps the triangle centered and
   proportional, and closing the window exits with status code `0` and no error
   output. A failure naming DXIL at shader creation is the W03 integration point
   to report, not the expected state.
3. **Every other GPU example reaches its render loop.** Run `HelloCube`,
   `HelloOBJ`, `HelloO3D`, `HelloX`, `HelloScene`, `HelloMaterials`,
   `HelloLighting`, `HelloCulling`, `HelloLOD`, `HelloStreaming`, `HelloPicking`,
   `HelloCollision`, `HelloAnimation`, `HelloAudio`, and `HelloBenchmark`. None
   may fail at shader or pipeline creation, and each must show the geometry its
   section of this document describes. A message naming `direct3d12`, DXIL, or a
   rejected shader is the W04 integration point to report.
4. **Run the per-example checklists below on Windows.** They use macOS-specific
   key names in a few places (`Cmd+Q`, Retina, Activity Monitor); use the Windows
   equivalents (`Alt+F4`, display scaling, Task Manager). The rendering checks
   themselves apply unchanged to Direct3D 12.

### Windows engine feature checks

These checks cover the features W05 verifies. They run the existing examples; no
extra tooling or SDK is needed. Record the Windows version, CPU architecture, GPU,
driver (OS driver and `GpuDevice.DriverName`), audio playback device, and date
with each result, and use `PASS` / `FAIL` / `NOT RUN` rather than prose.

1. **Headless suite.** From the repository root run
   `dotnet test Nymeria.slnx --configuration Release`. Expect **1120 passed, 2
   skipped, 0 failed**; the two skips are the device-dependent audio integration
   tests, which are skipped on every platform. A failure here is a W05 finding.
2. **Scene and transforms.** `dotnet run --project examples/HelloScene` — four
   cubes with distinct transforms and correct depth, and working `E`/`Q`/`R`.
3. **Animation.** `dotnet run --project examples/HelloAnimation` — three
   independent clips advance, and `P`, `L`, `-`/`=`, and `R` behave as their
   checklist describes.
4. **Picking.** `dotnet run --project examples/HelloPicking` — cursor click,
   empty-space miss, `V` center mode, and `G` ghost eligibility print exactly one
   line per click, not one per frame.
5. **Collision.** `dotnet run --project examples/HelloCollision` — 6 colliders,
   15 tested pairs, highlights on the current overlaps, and working arrow keys,
   `P`, and `R`.
6. **LOD and culling.** `dotnet run --project examples/HelloLOD` and
   `dotnet run --project examples/HelloCulling` — the toggles change the image and
   the counters together, `submitted + culled = tested`, and draw calls equal the
   drawn instances.
7. **World streaming.** `dotnet run --project examples/HelloStreaming` — the
   starting demand set fills to about 9 of 25 chunks, `T` unloads and reloads
   without duplicate objects, `S` freezes streaming, `C` changes only culled
   counts, and `pruned` rises after an unload.
8. **Audio.** `dotnet run --project examples/HelloAudio` — the console reports a
   live device at `48000 Hz stereo` (or the configured rate), the 2D loop and two
   spatial sources play, panning and attenuation change as the listener moves,
   `,`/`.`/`T` change the stereo strength, and closing the window exits with
   status `0` without a stuck audio thread. **A successful build is not playback
   validation.**
   - Source sample-rate handling is verified by `PcmAudioDataTests`
     (`Resample_*`), the `AudioMixer.CreateClip` rate guard, and code inspection:
     `AudioSystem.LoadWav` resamples every decoded WAV to the mixer rate, so a
     non-48 kHz source is not assumed. The examples generate their WAVs at
     48 kHz, so no example exercises a non-48 kHz file on a real device.
9. **Input and window lifecycle.** `dotnet run --project examples/HelloWindow --
   --smoke`, then resize, minimize/restore, hold and release the right mouse
   button, and close with `Alt+F4`; the process exits `0` and the cursor is
   restored.
10. **Performance diagnostics.** `dotnet run --project
    examples/HelloBenchmark -- --instances 128 --warmup 10 --frames 60` exits `0`
    and prints its timing, draw-call, triangle, and managed-allocation lines.
    Record the numbers; do not compare them with results from other hardware.
11. **Formats.** `HelloOBJ`, `HelloO3D`, and `HelloX` render their fixtures and an
    explicitly supplied path; `HelloMaterials` renders the generated checkerboard
    and the PNG, JPEG, TGA, BMP, RGBA DDS, and BC1 DDS assets under
    `examples/HelloMaterials/Assets/`.

[WINDOWS_05_ENGINE_FEATURE_COMPATIBILITY.md](WINDOWS_05_ENGINE_FEATURE_COMPATIBILITY.md)
records the feature matrix, the macOS evidence, and which Windows cells are still
open.

**Not claimed:** this section does not validate hi-DPI drawable sizing or GPU-side
frame timing against a reference, and it asserts no cross-platform performance
parity.

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
    The same cube renders. If you also have a lawfully obtained static OMSI
    model, plain or scrambled, record what it shows, including any diagnostic; a
    failure there is useful evidence, not a checklist pass.
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

## HelloX checks

1. **Window appears.** With no arguments, a native window opens without a
   console error. The checked-in `Assets/cube.x` fixture is copied beside the
   built executable, so this works from any current directory.
2. **Title is correct.** The title bar reads `Nymeria`.
3. **Model renders.** A cube is visible against the dark background with six
   distinct solid face colors and perspective-projected edges. The output is
   not blank, black, or missing faces.
4. **Depth occlusion is correct.** Near faces hide the faces behind them. The
   cube reads as a solid object rather than a flat set of triangles.
5. **Camera framing is stable.** Without input, the camera does not drift and
   the model stays visible.
6. **Movement and mouse look work.** The shared controls behave as in
   `HelloCube`: `W`/`A`/`S`/`D`, `Space`, `Left Control`, `Shift`, and held right
   mouse button for look. Releasing the button restores the cursor.
7. **Source-face order is preserved.** The fixture's source faces are clockwise
   in Direct3D's left-handed frame. After conversion, no face is inside-out in a
   way that makes the cube look like a set of disconnected quads. Culling is
   disabled, so also inspect the silhouette and the consistency of the six face
   colors while rotating.
8. **Normals and texture coordinates are accepted.** The fixture imports
   normals and UVs; the viewer does not require them for its unlit color shader,
   but loading must not fail. Run the parser-linked tests as the authoritative
   check for values.
9. **Resize works.** Drag a corner or edge, including to a very wide and a very
   tall window. Proportions stay correct and the window remains responsive.
10. **High-DPI works.** On a Retina display, cube edges are drawn at the display's
    native pixel density rather than an upscaled low-resolution image.
11. **Minimize and restore work.** Minimize the window for a few seconds, then
    restore it. The scene reappears with correct depth and proportions.
12. **User-supplied path works.** Run with the fixture path passed explicitly,
    for example
    `dotnet run --project examples/HelloX/HelloX.csproj -- examples/HelloX/Assets/cube.x`.
    The same cube renders.
13. **Close button exits.** Click the red close button. The process ends, the
    terminal prompt returns, and `echo $?` reports `0`.
14. **Standard quit exits.** Launch again, focus the window, and press `Cmd+Q`.
    The process ends with status code `0`.
15. **No busy-spin.** The loop renders continuously at about 60 frames per
    second, so CPU use is higher than the idle triangle, but it must stay well
    below one full core. Memory use stays stable over several minutes.
16. **No unhandled exceptions.** No exception text appears in the terminal during
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
   and one is solid orange. In front of them are one masked pane and three
   overlapping blended panes. The output is not blank, black, stretched, or
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
15. **JPEG works.** Run with `examples/HelloMaterials/Assets/checker.jpg`. The
    terminal reports 128 × 128 and 65536 RGBA bytes, the quadrants keep their
    documented orientation, and every fragment is opaque.
16. **Uncompressed TGA works.** Run with
    `examples/HelloMaterials/Assets/checker.tga`. It is stored bottom-up, so a
    correct decode still shows red top-left, green top-right, blue bottom-left,
    and yellow bottom-right on each face.
17. **RLE TGA works.** Run with
    `examples/HelloMaterials/Assets/checker-rle.tga`. It is 24-bit BGR and
    top-down; the four color bands appear in the same orientation and are fully
    opaque.
18. **Uncompressed 24-bit BMP works.** Run with
    `examples/HelloMaterials/Assets/checker.bmp`. The terminal reports 128 × 128
    and 65536 RGBA bytes, and the quadrants keep their documented orientation.
    The file is stored bottom-up with four-byte row padding, so a wrong stride or
    row order is visible as bands or a vertical flip. Every fragment is opaque.
19. **32-bit top-down BMP works.** Run with
    `examples/HelloMaterials/Assets/checker-32.bmp`. The terminal reports
    128 × 128 and 65536 RGBA bytes. The file uses a negative height, so rows are
    stored top-down; the result matches the PNG orientation and is fully opaque
    because no alpha mask is declared.
20. **Uncompressed DDS works.** Run with
    `examples/HelloMaterials/Assets/checker-rgba.dds`. The terminal reports
    128 × 128 and 65536 RGBA bytes, and the quadrants match the PNG result.
21. **Compressed DDS works or reports capability.** Run with
    `examples/HelloMaterials/Assets/checker-bc1.dds`. The terminal reports BC1 with
    eight mip levels. On a backend that supports BC1, the checker pattern renders
    without a capability warning. If support is missing, the terminal reports the
    format and falls back to the generated checkerboard without crashing. Mip
    levels below 4 × 4 are not uploaded, and the result does not show seams or
    missing mips at a distance.
22. **Invalid texture falls back.** Run the example with a missing path and then a
    malformed or oversized PNG, JPEG, TGA, DDS, and BMP. The terminal prints a clear
    diagnostic naming the path and reason, uses the generated checkerboard, and
    the window still opens. Close it normally; the process exits with status code
    `0`.
23. **Close button exits.** Click the red close button. The process ends, the
    terminal prompt returns, and `echo $?` reports `0`. The cursor is visible.
24. **Standard quit exits.** Launch again, focus the window, and press `Cmd+Q`.
    The process ends with status code `0`.
25. **Repeated cycles work.** Repeat launch and close at least three times, with
    and without each texture format. Each launch opens one window; each close
    leaves no orphan process.
26. **No busy-spin or per-frame uploads.** CPU use stays well below one full core
    and memory use stays stable for several minutes. The texture and mesh are
    uploaded once at first use, not every frame, including mip chains.
27. **No unhandled exceptions.** No exception text appears in the terminal during
    normal launch, movement, mouse look, resize, minimize, restore, any texture
    load, fallback, or shutdown.

### Transparency checks

T1. **Mask discards fragments.** The pane nearest the camera shows a regular
    checkerboard of fully transparent holes. Through each hole the scene behind
    it is visible, and there is no dark or tinted residue where a hole should
    be. Moving the camera along the pane keeps the holes aligned to the texture.
T2. **Mask writes depth.** An opaque object between the camera and the surviving
    part of the masked pane hides it; a hole never hides the geometry behind it.
T3. **Blend composites.** The three glass panes tint the objects behind them
    rather than replacing them. Where two panes overlap, the tint is visibly
    stronger than for either pane alone, and the four-quadrant checkerboard is
    still recognizable through the glass.
T4. **Blend order is back to front.** Orbit so the three glass panes line up. The
    far pane is composited before the near one: a warm pane behind a cool pane
    tints the cool pane, not the reverse. No pane disappears or flickers as the
    camera moves, and rotating away and back reproduces the same result.
T5. **Blend does not write depth.** A blended pane in front of another blended
    pane does not cut a hole in it, and two glass panes seen edge-on do not
    occlude each other through the depth buffer.
T6. **Opaque and masked paths are unchanged.** The four original cubes still
    show the same colors, orientation, tint blending, and depth occlusion as
    before this milestone; the mask pane is the only object that discards
    fragments.
T7. **Resize and minimize keep transparency stable.** After resizing, minimizing,
    and restoring, the mask holes, the blend tints, and the ordering look the
    same, and no depth or blend state leaks between frames.

## HelloLighting checks

1. **Window appears.** With no arguments, a native window opens without a console
   error. The terminal prints the lighting control summary, and the example loads
   the copied `Assets/cube.obj` and `Assets/checker.png` fixtures.
2. **Title is correct.** The title bar reads `Nymeria`.
3. **Lit objects render.** Three cubes are visible against the dark background.
   Two show the checkerboard texture and one is solid orange. Each face has a
   different brightness depending on its orientation, so the cubes read as lit
   volumes rather than flat colors.
4. **Ambient and diffuse are both visible.** Faces pointing toward the light are
   clearly brighter than faces pointing away. No face is pure black, because the
   ambient term lights the unlit sides.
5. **Normal transform is correct under non-uniform scale.** The middle cube is
   visibly squashed and rolled. Its face shading must match the faces of the
   uniformly scaled cubes: no face may appear lit from the wrong side or
   brighter than the light direction allows.
6. **Toggling works.** Press `L`. The directional contribution disappears and the
   scene becomes uniformly dim ambient-only; shading no longer varies with face
   orientation. Press `L` again to restore directional lighting for the remaining
   checks.
7. **Light direction changes.** Hold the left and right arrow keys. Brightness
   moves smoothly between faces of the cubes. Hold the up and down arrow keys to
   move the light vertically; faces rotate out of and into the light without the
   scene flashing or producing black or white artifacts.
8. **Intensity changes are clamped.** Press `[` repeatedly until the cubes are
   nearly unlit, then `]` repeatedly until they are bright. Values stop changing
   at the documented limits, and no NaN-like flicker, black squares, or
   over-bright clipping artifact appears.
9. **Reset works.** Press `R`. The light direction, intensity, and directional
   on/off state return to the startup values.
10. **Textured and solid materials interact correctly.** The checkerboard pattern
    and the blue-free orange tint stay visible under lighting; the texture is not
    double-multiplied and the tint is not applied twice. Output remains opaque.
11. **Movement and mouse look work.** The shared controls behave as in
    `HelloCube`: `W`/`A`/`S`/`D`, `Space`, `Left Control`, `Shift`, and held right
    mouse button for look. Releasing the button restores the cursor. Shading stays
    attached to the surfaces while the camera moves, so it is not view-dependent.
12. **Resize works.** Drag a corner or edge, including to a very wide and a very
    tall window. Proportions stay correct, lighting stays consistent, and the
    window remains responsive.
13. **High-DPI works.** On a Retina display, cube and texture edges are drawn at
    the display's native pixel density rather than an upscaled low-resolution
    image.
14. **Minimize and restore work.** Minimize the window for a few seconds, then
    restore it. The lit scene reappears correctly and the process does not crash
    or freeze while minimized.
15. **Close button exits.** Click the red close button. The process ends, the
    terminal prompt returns, and `echo $?` reports `0`. The cursor is visible.
16. **Standard quit exits.** Launch again, focus the window, and press `Cmd+Q`.
    The process ends with status code `0`.
17. **No busy-spin or per-frame uploads.** CPU use stays well below one full core
    and memory use stays stable for several minutes and across repeated light
    changes. Geometry and textures are uploaded once; only the small lighting and
    transform uniforms change per frame.
18. **No unhandled exceptions.** No exception text appears in the terminal during
    normal launch, light changes, movement, mouse look, resize, minimize, restore,
    or shutdown.
19. **HelloMaterials stays unlit.** Run `HelloMaterials` again. It must look the
    same as before this task, with no directional shading, because it sets no
    `Scene.Lighting`.

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

## HelloCulling checks

1. **Window appears.** With no arguments, a native window opens and the terminal
   prints the control summary. The first statistics line is a startup sample and
   contains zeros; later lines update once per second.
2. **Many instances render.** The scene contains 551 cubes sharing one mesh and
   four tinted materials. Cubes fill the grid in front of the camera, extend
   beyond the far plane, sit behind the initial view, and sit far to the sides.
   Confirm visually that visible cubes appear normally: no missing faces, no
   white or black artifacts, and no duplicated geometry.
3. **Initial counts are consistent.** With culling enabled (the default), the
   statistics show 551 tested instances, 413 submitted, 138 culled, and 413 draw
   calls for the initial camera. The value 413 may vary slightly after a manual
   window resize because the drawable aspect ratio changes the frustum; the key
   invariant is submitted + culled = tested and draw calls = submitted.
4. **Toggle works.** Press `C`. The terminal reports that culling is disabled and
   the next sample shows all 551 instances submitted with 0 culled and 551 draw
   calls. The visible image must look the same apart from any cubes that were
   previously culled but were in fact visible at the screen edge; there must be
   no pop, no object loss, and no error. Press `C` again to restore the initial
   counts.
5. **Disabled start works.** Close the window and run with `--no-culling`. The
   first non-zero sample must show 551 submitted and 0 culled. Press `C` to
   enable culling; the submitted and culled counts must change while the scene
   remains stable.
6. **No false culling at boundaries.** Fly forward and sideways with the shared
   camera controls. Cubes must not disappear while any part of them is still on
   screen. Cubes crossing a side, top, bottom, near, or far plane may flicker
   only as their whole bounds leave the view, never while visible geometry
   remains.
7. **Camera rotation is correct.** Hold the right mouse button and turn a full
   circle. Cubes behind the camera must be culled, cubes ahead must appear, and
   the counts must change smoothly without inverted or stale visibility.
8. **Transformed bounds are correct.** Some cubes use rotation, non-uniform
   scale, and negative scale. They must remain visible when their transformed
   corners are in view, and must not be culled early because a scale is negative.
9. **Resize works.** Drag the window through a very wide and a very tall shape.
   Proportions stay correct, statistics update for the new aspect ratio, and the
   window remains responsive. No crash or stale depth texture occurs.
10. **High-DPI works.** On a Retina display, cube edges are drawn at native pixel
    density rather than an upscaled low-resolution image.
11. **Minimize and restore work.** Minimize for a few seconds, then restore. The
    process does not crash or freeze. While minimized, rendering is skipped and
    statistics samples report zeros; after restore, non-zero counts return.
12. **No unhandled exceptions.** No exception text appears during launch, camera
    movement, culling toggles, resize, minimize, restore, or shutdown.
13. **Close button exits.** Click the red close button. The process ends with
    status code `0` and the cursor is visible.
14. **Standard quit exits.** Launch again, focus the window, and press `Cmd+Q`.
    The process ends with status code `0`.

Check the exit code after a normal close:

```bash
echo $?
```

Expected: `0`.

## HelloLOD checks

1. **Window appears.** With no arguments, a native window opens and the terminal
   prints the control summary. The first statistics line is a startup sample and
   contains zeros; later lines update once per second.
2. **Three detail levels are visible.** Three receding columns of spheres and
   three larger spheres fill the view. Near spheres have smooth silhouettes;
   distant spheres are visibly faceted, with at least two clearly different
   triangle densities. No sphere is missing, black, or drawn as a wireframe.
3. **Initial counts are consistent.** With LOD, distance culling, and frustum
   culling enabled, the counters satisfy `candidates = drawn + frustum culled +
   distance culled` and `drawn = draw calls`. LOD usage level counts sum to the
   LOD-drawn count. The exact split changes with the camera and window aspect.
4. **LOD toggle works.** Press `L`. The terminal reports LOD disabled, every
   sphere becomes the dense high-detail mesh (including distant ones), and the
   LOD usage counts become zero while draw calls still equal drawn instances.
   The distance-culled count also becomes zero. Press `L` again to restore.
5. **Distance toggle works.** Press `X`. The terminal reports distance culling
   disabled and the distance-culled count becomes zero; far instances beyond the
   previous maximum reappear at the low detail level. Press `X` again to restore
   the previous counts. The remaining scene must not change shape or pop.
6. **Frustum toggle works.** Press `F`. Instances behind the camera and off the
   sides return; the image may reveal geometry that was previously outside the
   view. Press `F` again to restore. LOD and distance counts remain consistent.
7. **Threshold changes work.** Press `]` several times. The printed thresholds and
   maximum distance increase. Near spheres keep the high level longer, and the
   number of high-detail draws rises; press `[` to reverse the change. `R`
   restores the base 14/38 thresholds and 120 maximum.
8. **Hysteresis is stable.** Move the camera slowly back and forth across one
   threshold (a sphere at roughly 14 or 38 units). A sphere must change silhouette
   once and hold; it must not flicker between levels while the camera oscillates
   inside the 0.75-unit band.
9. **Bounding-volume distance is used.** Fly toward a large sphere and compare it
   with a small sphere at the same origin distance. The larger object keeps its
   higher detail longer because its nearest surface is closer.
10. **Transforms remain correct.** Rotated and uniformly scaled spheres render
    with correct silhouettes and no inverted normals, stretched faces, or depth
    artifacts. There are no textures or lighting terms, so the material tint is
    flat.
11. **Resize works.** Drag the window through a very wide and a very tall shape.
    Proportions stay correct, counters update for the new aspect ratio, and the
    renderer remains responsive.
12. **High-DPI works.** On a Retina display, sphere edges are drawn at native
    pixel density rather than an upscaled low-resolution image.
13. **Minimize and restore work.** Minimize for a few seconds, then restore. The
    process does not crash or freeze. While minimized, rendering is skipped and
    statistics report zeros; after restore, non-zero counts return.
14. **No per-frame uploads or leaks.** CPU use stays below one full core and
    memory stays stable while moving across thresholds and toggling LOD for
    several minutes. GPU buffers for all three variants are created once during
    startup, not repeatedly on each switch.
15. **No unhandled exceptions.** No exception text appears during launch, camera
    movement, threshold changes, toggles, resize, minimize, restore, or
    shutdown.
16. **Close and standard quit exit.** The close button and `Cmd+Q` end the
    process with status code `0`, and the cursor is visible.
17. **Disabled starts work.** Run with `--no-lod`, `--no-distance-culling`, and
    `--no-culling` separately. The first statistics line and subsequent samples
    must reflect each starting state, and the runtime keys must still toggle it.

## HelloStreaming checks

1. **Window appears.** With no arguments, a native window opens and the terminal
   prints the world description and controls. The first statistics line may show
   only the initial four-chunk budget; subsequent lines fill the starting demand
   set to roughly nine of 25 chunks and report no failures.
2. **Chunks activate.** The starting view shows several raised floor tiles with
   colored cubes and tapered markers; chunks beyond the load distance are absent.
   The printed active count equals the number of visible chunk groups, and
   `known = active + unloaded + failed`.
3. **Boundary hysteresis is stable.** Move slowly across a load distance and back.
   Chunks must not repeatedly load and unload while the camera remains inside the
   gap between load and unload distances. Counts should hold steady apart from
   deliberate crossings.
4. **Loads are bounded.** On startup, the active count rises by at most four per
   update until the demand set is filled. The last-update load count must never
   exceed the configured maximum.
5. **Teleport works.** Press `T` to jump to the opposite corner. The terminal
   reports one or more unloads and loads; the previous distant chunks disappear,
   new nearby chunks appear, and each remaining chunk has exactly one copy of its
   floor, cubes, and marker. No duplicate objects or draw-call doubling is visible.
6. **GPU cache pruning works.** After a teleport unload, the terminal reports
   pruned GPU entries. The image must not flicker, lose shared floor or cube
   geometry, or show released buffers. Press `T` repeatedly for several
   round-trips and confirm no crash or use-after-dispose error.
7. **Shared resources remain valid.** The floor and cube geometry and the six
   material tints continue to render across every chunk. Only the unique marker
   geometry is released on unload; no active chunk loses its shared cube.
8. **Streaming toggle works.** Press `S`; the terminal reports streaming disabled.
   Move or teleport: the active set, object count, and draw count stay frozen, and
   no new load, unload, or prune occurs. Press `S` again and confirm the active
   set updates on the next frame.
9. **Frustum culling still works.** Press `C` to disable and re-enable frustum
   culling. The candidate count remains the active object count; culled instances
   and draw calls change without changing chunk membership.
10. **Invalid configuration is rejected.** Run with a load distance greater than
    or equal to the unload distance, or with a non-finite value. The process prints
    an actionable error and exits with status `1` before opening a window.
11. **Resize, high-DPI, minimize, and restore work.** Drag through wide and tall
    shapes. Retina output stays sharp. Minimize for a few seconds: rendering is
    skipped and statistics report zero renderer counts, while the next update
    still has a finite cost. Restore and confirm drawing resumes with valid
    chunk and draw counts.
12. **No leaks over repeated navigation.** Watch process memory and CPU while
    teleporting across the grid for several minutes. Memory must stabilize rather
    than grow with every unload, and GPU entries pruned should roughly match the
    unique markers from unloaded chunks. Close with the button and `Cmd+Q`; both
    exit with status `0` and restore the cursor.

## HelloPicking checks

1. **Window appears.** The window opens and the terminal prints the chunk,
   control, and bounds-picking summary. The title shows `no selection`, `cursor`,
   `ghost skipped`, and `0 picks`.
2. **Cursor picking works.** Left-click a visible box. It immediately changes to
   the bright highlight material, the terminal prints one `Pick N: ... at ...
   units (cursor, bounds)` line, and the title shows the same label and distance.
   Hovering elsewhere and clicking must not log every frame.
3. **Nearest overlap works.** Click where two boxes overlap on screen. The
   reported object must be the nearer one, and only it is highlighted. Changing
   the camera angle and clicking the same pair again must still select the nearer
   bounds.
4. **Miss works.** Click empty background. The previous highlight must clear, the
   title shows `no selection (miss)`, and exactly one miss line is printed.
5. **Center picking works.** Press `V`; the title mode changes to `center`. Click
   without moving the mouse and confirm the selected object is the one under the
   viewport center. Press `V` again and confirm cursor mode returns.
6. **Pick eligibility works.** With the ghost box between the camera and the
   towers, use center picking. While the title says `ghost skipped`, the ray must
   pass through it and select an object behind. Press `G`; the title says `ghost
   pickable`, and a center click must now select the ghost and highlight it.
   Toggle `G` again to confirm the pass-through returns.
7. **Transformed bounds are used.** Move close to a rotated, non-uniformly scaled
   slab and click near its visual corner and just outside it. The highlight and
   reported point must follow the transformed world bounds, not the unrotated
   local mesh bounds.
8. **Streaming unload clears the selection.** Select an object, then fly or
   strafe until its chunk is beyond the unload distance. The terminal must print
   one `Selection cleared because its chunk was unloaded.` line, the title must
   show `no selection (unloaded)`, and no highlight may remain on an object that
   is no longer drawn.
9. **Screen edges and occlusion do not confuse picking.** Click boxes that are
   partially clipped by a viewport edge and boxes that are partly hidden behind
   another object. Selection must follow the ray and the nearest transformed
   bounds, not the drawn pixels. The example leaves frustum culling and LOD at
   their defaults; picking never consults either system.
10. **Resize, high-DPI, minimize, and restore work.** Resize through wide and
    tall shapes, then click the same on-screen object at a new size. Retina output
    stays sharp and the selection must still match the pointer. Minimize and
    restore; rendering resumes, and picks still work. Close with the button and
    `Cmd+Q`; both exit with status `0` and restore the cursor.

## HelloCollision checks

1. **Window appears.** The window opens and the terminal prints the body,
   collider, shape, and diagnostic summary. The title contains
   `Nymeria - HelloCollision` and initially reports `player clear` and `animation
   on`.
2. **Contact feedback is current.** Within the first second, moving bodies cross
   the static sphere and each other. Their materials change to the shared yellow
   highlight and the title reports a nonzero overlapping-pair count. The bodies
   continue along their paths; overlap must not push, stop, or resolve anything.
3. **Player contact works.** Use the arrow keys to drive the cyan box into the
   large center box or a moving body. Both turn yellow while they overlap. The
   title changes to `player contact`, and the console's next once-per-second line
   reports the same condition. Back away and confirm the highlight clears.
4. **Pause works.** Press `P`. Orbit and bob motion stops, and the contact state
   remains stable while the player can still move. Press `P` again and confirm
   animation resumes from the paused time without a jump.
5. **Reset works.** Move the player away, press `R`, and confirm the cyan box
   returns to its starting position and animation time resets. The camera is not
   reset by `R` in this example.
6. **Diagnostics are bounded.** Once per second the terminal prints exactly one
   collision line with six active colliders, fifteen tested unordered pairs, the
   current overlapping-pair count, player contact, animation state, measured
   query time, FPS, and drawn instances. No line is printed per frame.
7. **Lifecycle remains stable.** Run for several minutes while moving through
   contacts. Memory and CPU must remain stable, the example must not throw, and
   no contact highlight may remain on a body after it separates.
8. **Resize, minimize, and restore work.** Resize through wide and tall shapes
   and confirm the highlight still matches current overlap. Minimize and restore;
   rendering resumes and the next diagnostic line remains accurate. Close with
   the button and `Cmd+Q`; both exit with status `0` and restore the cursor.

## HelloAudio checks

1. **Window and terminal appear.** The window opens, draws a dark ground plane
   and red and blue source markers, and prints the control summary. The status
   starts with `Audio ready:` followed by the sample rate, stereo format, and
   voice limit.
2. **Status is live.** Once per second the console prints the listener position
   and forward direction, `device ok`, a nonzero voice count, and each voice's
   state, position, distance, attenuation, pan, and gain. Values must change as
   the listener moves and turns.
3. **2D ambient playback works.** A quiet looping chord is audible. Turning the
   listener must not pan or attenuate it.
4. **Spatial sources are audible and distinct.** The two pulsing tones are
   audible at different pitches. Face the red source: it should be louder and
   panned left. Face the blue source: it should be panned right. The reported
   `pan left`/`pan right` values must agree with what is heard.
5. **Attenuation works.** Walk toward a source and confirm it gets louder and the
   reported attenuation approaches `1.00`. Walk away and confirm it gets quieter;
   beyond the maximum distance it becomes silent and attenuation is `0.00`.
   Turning the listener changes pan but not attenuation.
6. **Pause and resume work.** Press `P`; the ambient loop stops while the spatial
   tones continue, and the status says `paused`. Press `P` again and it resumes.
   Press `O` to pause and resume source A independently.
7. **Stop and restart share the clip.** Press `X`; source A stops and its status
   says `stopped (no voice)`. Press `X` again; a new voice starts the same clip
   from the beginning without an error or a device change.
8. **Gain controls work.** `[` and `]` change master gain; `-` and `=` change
   source A gain. The audible level and the printed percentages change together.
9. **Positional stereo strength works.** Press `S` to start the sweep and leave
   the listener still; source A circles it at a constant distance, so attenuation
   and gain stay fixed while the pan moves left to right. Press `T` to select 0%,
   50%, and 100%. At 0% the reported left and right pan gains are equal
   (`L 0.71 / R 0.71`) and the tone is centered; at 100% the gains separate fully;
   50% sits between them. The tone must not change level or restart, and the
   stereo gain columns must agree with what is heard. `,` and `.` adjust the
   strength by 10%.
10. **Missing device is graceful.** Quit, then run the example with an invalid
   SDL audio driver:

   ```bash
   SDL_AUDIODRIVER=nymeria-not-a-driver \
     dotnet run --project examples/HelloAudio/HelloAudio.csproj
   ```

   Expected: the terminal prints `Audio unavailable:` with the SDL failure and
   the window still opens and renders with an `audio unavailable` status. The
   process must not crash or exit with status `1`.
11. **Close exits cleanly.** Close with the button and with `Cmd+Q`. Both exit
    with status `0`, release the cursor, and leave no orphan process.

## HelloAnimation checks

1. **Window and terminal appear.** The window opens and shows a dark ground
   plane, a brown door between two frame posts, a dark wiper, a teal hovering
   cube, and two yellow pivot markers. The terminal prints the control summary
   and an initial status line with three players at time `0.00`.
2. **The door swings around the hinge.** The door rotates without drifting away
   from its frame. The reported angle rises from about `0` toward `109` degrees,
   holds briefly, and returns. The yellow marker stays at the hinge.
3. **The wiper sweeps around its base.** The wiper pivots at the yellow marker,
   not around its center. The reported angle moves between roughly `-37` and
   `+32` degrees and reverses smoothly.
4. **The mover translates and scales.** The teal cube rises, grows from `0.70`
   to `1.50` scale, returns, and shrinks. The printed X scale matches the visible
   size.
5. **Clips are independent.** Door, wiper, and mover reach different times and
   loop at different periods. Pausing or restarting must not reset the camera or
   any player's clip speed.
6. **Pause and resume work.** Press `P`; all three player times freeze and the
   status reports `paused`. Press `P` again and all three resume from their held
   times without a jump.
7. **Restart works.** Press `R`; all three players return to time `0.00` and the
   start pose, and playback continues.
8. **Looping and speed controls work.** Press `L`; the status reports `loop off`
   and each player stops at its duration with state `done` (the door first, then
   the wiper, then the mover). Press `L` again and `R` to restart and confirm
   looping resumes. Press `-` and `=`; the reported speed multiplier changes in
   `0.25` steps between `x0.25` and `x3.00`, and the motions visibly slow down or
   speed up together.
9. **Off-screen animation continues.** Turn the camera away from all three
   objects and confirm their times and angles keep changing in the status line.
10. **Close exits cleanly.** Close with the button and with `Cmd+Q`. Both exit
    with status `0`, release the cursor, and leave no orphan process.

## HelloBenchmark checks

`HelloBenchmark` is a CPU benchmark, not a visual or GPU-timing test. It opens a
window, runs the requested fixed modes, prints a report, and exits without user
input.

1. **Run the short smoke workload.** From the repository root:

   ```bash
   dotnet run --project examples/HelloBenchmark/HelloBenchmark.csproj \
     -- --instances 128 --warmup 10 --frames 60
   ```

   Expected: a window titled `Nymeria - HelloBenchmark` opens and closes. The
   terminal prints the runtime, OS, architecture, window sizes, Metal backend,
   scene size, pacing, warm-up, and sample settings before the reports.
2. **Both culling modes run, off first.** The output contains `mode: culling
   off | lod off | distance on` and then `mode: culling on | lod off | distance
   on`, each with `samples 60` and `skipped 0`.
3. **Counts are consistent.** For 128 instances, culling off reports 128 tested,
   128 visible, 0 frustum-culled, and 128 draw calls. Culling on reports 128
   tested, 120 visible, 8 frustum-culled, and 120 draw calls. Submitted plus
   frustum-culled plus distance-culled equals tested in both modes. LOD usage is
   zero when LOD is off.
4. **Pacing is explicit.** Each report shows a total frame average of roughly
   17.5–18.3 ms and FPS around 55–57, with a separate pacing interval. The exact
   paced total can vary with the machine's scheduling and display state. Do not
   interpret that FPS as a GPU throughput limit. Run with `--no-pacing` only if
   investigating presentation behavior; skipped frames are then expected and
   reported.
5. **Allocation is bounded and equal across modes.** Each mode reports
   `40 bytes/frame` managed allocation and `GC +0/+0/+0` for the short run. A
   LOD-on or culling-on value above the baseline indicates a per-frame allocation
   regression; see [PERFORMANCE.md](PERFORMANCE.md).
6. **LOD comparison works.** Run the short workload with `--lod both`. Four
   reports appear: culling off/lod off, culling off/lod on, culling on/lod off,
   and culling on/lod on. With LOD on, `lod drawn` equals the visible count and
   the LOD usage levels sum to it; submitted triangles drop substantially. With
   LOD off, LOD usage is zero. Run with `--lod on` and `--lod off` separately to
   confirm mode selection. Add `--no-distance-culling` to keep draw calls equal
   and isolate the triangle reduction from whole-instance distance culling; the
   report then says `distance off` and `distance culled avg 0.0`.
7. **Clean exit.** The process exits with status code `0` after all reports and
   writes no report file. Check with:

   ```bash
   echo $?
   ```
8. **Option handling.** `--help` prints usage and exits with status code `0`. An
   unknown option or an out-of-range value such as `--frames 0` or `--lod maybe`
   prints an error and exits with status code `1` without opening a measurement
   window.
9. **Scope.** The output must not claim GPU timings, GPU memory, or process-wide
   resident memory. `render` is CPU submission time; `triangles` is submitted
   indexed geometry, not fragments; `managed` is the benchmark thread's managed
   allocation only.

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
- **Missing .x file:** run `HelloX` with a path that does not exist. It must
  print a clear "not found" message and exit with status code `1` before a
  window is created.
- **Malformed .x file:** create a temporary file outside tracked paths with a
  valid header but a face that references a vertex beyond the mesh. `HelloX`
  must print the source line and the invalid index, then exit with status code
  `1` without partial geometry.
- **Unsupported .x variant:** create text files with `xof 0303bin 0032` and
  `xof 0303tzip 0032` headers, and a text file containing `AnimationSet` or
  `SkinWeights`. `HelloX` must reject each with a message naming binary,
  compressed, animation, or skinning support, and exit with status code `1`.
- **Missing O3D file:** run `HelloO3D` with a path that does not exist. It must
  print a clear "not found" message and exit with status code `1` before a window
  is created.
- **Malformed O3D file:** create a temporary file outside tracked paths with, for
  example, the same header as the fixture but a truncated vertex section.
  `HelloO3D` must print the byte offset and the reason, then exit with status
  code `1` without an unhandled exception or partial geometry.
- **Unsupported O3D feature:** if you have a bone or animation O3D file,
  `HelloO3D` must reject it with a message that names the unsupported feature,
  then exit with status code `1`. Do not treat a guessed or partially drawn
  result as a pass.
- **Scrambled O3D file:** a version 4+ file whose key is not `0xFFFFFFFF` must
  load and show its geometry. Check that it is not shredded or inside out; the
  decoder reproduces OMSI's transform but cannot prove a key matches the data.
- **OBJ without normals:** create a temporary OBJ outside tracked paths with
  positions and faces but no `vn` records. `HelloLighting` must print that normals
  are required and exit with status code `1` before a window is created. It must
  not invent normals.
- **Missing HelloLighting texture:** temporarily move the copied
  `Assets/checker.png` beside the built executable, or run from a build without
  it. The example must print a clear diagnostic naming the asset and exit with
  status code `1` before a window is created.

## Recording results

Report the macOS version, architecture (`arm64` or `x86_64`), GPU model, date, and
pass/fail for each check, separately for `HelloTriangle`, `HelloCube`,
`HelloOBJ`, `HelloO3D`, `HelloX`, `HelloScene`, `HelloMaterials`,
`HelloLighting`, `HelloCulling`, `HelloCollision`, `HelloAudio`, `HelloAnimation`,
`HelloLOD`, `HelloStreaming`, and `HelloPicking`, plus the separate
`HelloBenchmark` checks. For `HelloAudio`, also record the output device and that the generated
ambient and two spatial tones were audible.
Do not describe this manual checklist as a CI result.
State separately that the checked-in `HelloO3D` fixture is synthetic, that the
`HelloX` fixture is original project content rather than an exported game asset,
that real OMSI content was not available for validation, and that no broad
third-party `.x` exporter corpus was validated.
