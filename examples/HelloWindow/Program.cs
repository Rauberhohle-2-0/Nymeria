using System.Runtime.InteropServices;
using Nymeria.Platform;
using SDL3;

namespace HelloWindow;

/// <summary>
/// Exercises the native SDL3 video runtime without creating a GPU device.
/// </summary>
/// <remarks>
/// <para>
/// This is the smallest supported window path. It initializes SDL video through
/// <see cref="Window.Create"/>, shows a resizable window, pumps the event queue,
/// and disposes the window and SDL on the process main thread. It never creates a
/// GPU device, loads a shader, or presents a frame, so it separates native SDL3
/// deployment from the GPU backend and shader work that depend on it.
/// </para>
/// <para>
/// Run it with no arguments for an interactive window, or with <c>--smoke</c> to
/// pump a fixed number of events and exit without user input.
/// </para>
/// </remarks>
internal static class Program
{
    private const int EventWaitMilliseconds = 100;
    private const int SmokeEventWaitMilliseconds = 10;
    private const int SmokeEventCycleCount = 30;

    private static int Main(string[] args)
    {
        bool isSmokeRun = false;

        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--smoke":
                    isSmokeRun = true;
                    break;
                case "-h":
                case "--help":
                    PrintUsage();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown argument: {arg}");
                    PrintUsage();
                    return 1;
            }
        }

        try
        {
            return Run(isSmokeRun);
        }
        catch (DllNotFoundException exception)
        {
            Console.Error.WriteLine("Nymeria could not load the native SDL3 library.");
            Console.Error.WriteLine(
                "On Windows this is usually Windows error 0x8007007E " +
                "(ERROR_MOD_NOT_FOUND): the process found no SDL3.dll to load.");
            Console.Error.WriteLine(
                "Restore and rebuild so NuGet deploys the SDL3-CS.Windows runtime " +
                "assets into the output directory:");
            Console.Error.WriteLine("  dotnet restore Nymeria.slnx");
            Console.Error.WriteLine("  dotnet build Nymeria.slnx");
            Console.Error.WriteLine(
                "If the build succeeded, check that a matching SDL3.dll exists under " +
                "bin/<Configuration>/net10.0/runtimes/win-<arch>/native/, or run a " +
                "RID-specific build such as `dotnet build -r win-x64`.");
            Console.Error.WriteLine($"Details: {exception}");
            return 1;
        }
        catch (BadImageFormatException exception)
        {
            Console.Error.WriteLine(
                "The native SDL3 library does not match this process architecture.");
            Console.Error.WriteLine(
                "Build for the architecture of the running process, for example " +
                "`dotnet build -r win-x64` on an x64 host or `-r win-arm64` on an " +
                "arm64 host, instead of mixing an x64 SDL3.dll into an arm64 process.");
            Console.Error.WriteLine($"Details: {exception}");
            return 1;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine($"Nymeria startup failed: {exception.Message}");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Nymeria stopped because of an unexpected error.");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int Run(bool isSmokeRun)
    {
        Console.WriteLine(
            $"host: {RuntimeInformation.OSDescription} " +
            $"({RuntimeInformation.ProcessArchitecture})");

        using Window window = Window.Create(new WindowOptions());

        PrintWindowDiagnostics(window);

        if (isSmokeRun)
        {
            int completedCycles = 0;

            for (int cycle = 0; cycle < SmokeEventCycleCount; cycle++)
            {
                if (!window.ProcessEvents(SmokeEventWaitMilliseconds))
                {
                    Console.WriteLine("A quit request ended the smoke run early.");
                    break;
                }

                completedCycles++;
            }

            Console.WriteLine($"event pump: {completedCycles} cycles");
            Console.WriteLine("window smoke test passed");

            return 0;
        }

        Console.WriteLine("Close the window or use the standard quit shortcut to exit.");

        while (window.ProcessEvents(EventWaitMilliseconds))
        {
            // This example has nothing to draw: pumping events is the whole loop.
        }

        Console.WriteLine("window closed by the user");

        return 0;
    }

    /// <summary>
    /// Reports the window sizes SDL negotiated, which proves the native library
    /// loaded and answered a call rather than merely resolving at load time.
    /// </summary>
    private static void PrintWindowDiagnostics(Window window)
    {
        if (!SDL.GetWindowSize(window.Handle, out int logicalWidth, out int logicalHeight))
        {
            Console.WriteLine(
                $"window: created (logical size unavailable: {SDL.GetError()})");
            return;
        }

        if (!SDL.GetWindowSizeInPixels(
                window.Handle,
                out int drawableWidth,
                out int drawableHeight))
        {
            Console.WriteLine(
                $"window: {logicalWidth}x{logicalHeight} logical " +
                $"(drawable size unavailable: {SDL.GetError()})");
            return;
        }

        Console.WriteLine(
            $"window: {logicalWidth}x{logicalHeight} logical | " +
            $"drawable {drawableWidth}x{drawableHeight}");
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: HelloWindow [-h|--help] [--smoke]\n" +
            "  Opens a native SDL3 window and pumps its event queue. No GPU device\n" +
            "  is created and nothing is rendered.\n" +
            "  --smoke  Pump a fixed number of events, then exit without user input.");
    }
}
