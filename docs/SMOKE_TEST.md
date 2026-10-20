# macOS Manual Smoke Test: Window and GPU Triangle

This checklist validates behavior that headless CI cannot verify. Run it on a
macOS machine after the solution builds. Record the macOS version, CPU
architecture, GPU model, and date with the result.

## Setup

From the repository root:

```bash
dotnet restore Nymeria.slnx
dotnet build Nymeria.slnx
dotnet run --project examples/HelloTriangle/HelloTriangle.csproj
```

The triangle shader is
`examples/HelloTriangle/Shaders/Triangle.metal`. It is embedded in
`HelloTriangle` and compiled by SDL's Metal backend at startup, so there is no
shader compiler step and no external shader file to deploy.

## Checks

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
pass/fail for each check. Do not describe this manual checklist as a CI result.
