# macOS Manual Smoke Test: Native Window

This checklist validates behavior that headless CI cannot verify. Run it on a
macOS machine after the solution builds. Record the macOS version and CPU
architecture with the result.

## Setup

From the repository root:

```bash
dotnet restore Nymeria.slnx
dotnet build Nymeria.slnx
dotnet run --project examples/HelloWindow/HelloWindow.csproj
```

## Checks

1. **Window appears.** A native window opens without a console error.
2. **Title is correct.** The title bar reads `Nymeria`.
3. **Initial size is reasonable.** The client area is approximately 960 × 540
   and the client area is blank. Blank content is expected in Task 02.
4. **Resize works.** Drag a corner or edge. The window resizes smoothly, remains
   responsive, and does not crash or hang.
5. **Close button exits.** Click the red close button. The process ends, the
   terminal prompt returns, and `echo $?` reports `0`.
6. **Standard quit exits.** Launch again, focus the window, and press `Cmd+Q`.
   The process ends with status code `0`.
7. **Repeated cycles work.** Repeat launch and close at least three times. Each
   launch opens one window; each close leaves no orphan process.
8. **No busy-spin.** While the window is idle and focused, CPU use in Activity
   Monitor is near zero. The event loop waits instead of polling continuously.
9. **No unhandled exceptions.** No exception text appears in the terminal during
   normal launch, resize, or shutdown.

Check the exit code after a normal close:

```bash
echo $?
```

Expected: `0`.

## Failure-path check (optional)

Run the example from a directory where the restored native library is unavailable
only if you can do so without modifying tracked files. Startup must print an
actionable message and exit with status code `1`, not hang or report success.

## Recording results

Report the macOS version, architecture (`arm64` or `x86_64`), date, and pass/fail
for each check. Do not describe this manual checklist as a CI result.
