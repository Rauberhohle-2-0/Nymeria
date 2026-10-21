using System.Diagnostics;
using System.Numerics;
using Nymeria.Examples;
using Nymeria.Graphics;
using Nymeria.Platform;
using SDL3;

namespace HelloCube;

internal static class Program
{
    private const int TargetFrameMilliseconds = 16;
    private const double MaxFrameDeltaSeconds = 0.1;
    private const double RotationRadiansPerSecond = 0.9;

    private static int Main()
    {
        try
        {
            using Window window = Window.Create(new WindowOptions());
            using CubeRenderer renderer = CubeRenderer.Create(window.Handle);

            var camera = new Camera(new Vector3(0.0f, 1.0f, 9.0f));
            using var controls = new FlyCameraControls(window.Handle, camera);

            var clock = Stopwatch.StartNew();
            double previousSeconds = clock.Elapsed.TotalSeconds;
            double rotationRadians = 0.0;

            while (true)
            {
                long frameStart = Stopwatch.GetTimestamp();

                double nowSeconds = clock.Elapsed.TotalSeconds;
                double deltaSeconds = Math.Min(
                    nowSeconds - previousSeconds,
                    MaxFrameDeltaSeconds);
                previousSeconds = nowSeconds;
                rotationRadians += deltaSeconds * RotationRadiansPerSecond;

                if (!window.ProcessEvents(0))
                {
                    break;
                }

                // Read input after SDL has pumped events so the cached keyboard
                // and mouse state is current for this frame.
                controls.Update((float)deltaSeconds);
                renderer.Render(camera, rotationRadians);

                long elapsedMilliseconds =
                    (Stopwatch.GetTimestamp() - frameStart) * 1000 / Stopwatch.Frequency;
                int delayMilliseconds = TargetFrameMilliseconds - (int)elapsedMilliseconds;

                if (delayMilliseconds > 0)
                {
                    SDL.Delay((uint)delayMilliseconds);
                }
            }

            return 0;
        }
        catch (DllNotFoundException exception)
        {
            Console.Error.WriteLine("Nymeria could not load the native SDL3 library.");
            Console.Error.WriteLine(
                "Run `dotnet restore Nymeria.slnx`, then start the example with " +
                "`dotnet run --project examples/HelloCube/HelloCube.csproj`.");
            Console.Error.WriteLine($"Details: {exception.Message}");
            return 1;
        }
        catch (BadImageFormatException exception)
        {
            Console.Error.WriteLine(
                "The native SDL3 library does not match this process architecture.");
            Console.Error.WriteLine($"Details: {exception.Message}");
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
}
