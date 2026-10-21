using System.Diagnostics;
using System.Numerics;
using Nymeria.Core.Models;
using Nymeria.Graphics;
using Nymeria.Platform;
using SDL3;

namespace Nymeria.Examples;

/// <summary>
/// The shared interactive model-viewer loop used by the format examples.
/// </summary>
/// <remarks>
/// The caller parses a file into <see cref="ModelData"/> and handles load errors.
/// This runner owns the window, GPU renderer, camera, input mapping, and frame
/// loop. It is example code, not an engine component.
/// </remarks>
internal static class ModelViewerApp
{
    private const int TargetFrameMilliseconds = 16;
    private const double MaxFrameDeltaSeconds = 0.1;

    public static int Run(ModelData model)
    {
        try
        {
            (Vector3 center, float radius) = GetBounds(model);
            var camera = new Camera(
                center + new Vector3(0.0f, 0.0f, MathF.Max(radius * 2.5f, 2.0f)))
            {
                NearPlane = MathF.Max(radius / 1000.0f, 0.01f),
                FarPlane = MathF.Max(radius * 10.0f, 100.0f),
                MoveSpeed = MathF.Max(radius, 1.0f) * 1.5f,
            };

            using Window window = Window.Create(new WindowOptions());
            using ModelRenderer renderer = ModelRenderer.Create(window.Handle, model);
            using var controls = new FlyCameraControls(window.Handle, camera);

            var clock = Stopwatch.StartNew();
            double previousSeconds = clock.Elapsed.TotalSeconds;

            while (true)
            {
                long frameStart = Stopwatch.GetTimestamp();

                double nowSeconds = clock.Elapsed.TotalSeconds;
                float deltaSeconds = (float)Math.Min(
                    nowSeconds - previousSeconds,
                    MaxFrameDeltaSeconds);
                previousSeconds = nowSeconds;

                if (!window.ProcessEvents(0))
                {
                    break;
                }

                // Read input after SDL has pumped events so the cached keyboard
                // and mouse state is current for this frame.
                controls.Update(deltaSeconds);
                renderer.Render(camera);

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
                "`dotnet run --project examples/<example>/<example>.csproj`.");
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

    private static (Vector3 Center, float Radius) GetBounds(ModelData model)
    {
        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);

        foreach (MeshData mesh in model.Meshes)
        {
            foreach (Vector3 position in mesh.Positions.Span)
            {
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }
        }

        Vector3 center = (min + max) * 0.5f;

        if (!float.IsFinite(center.X) ||
            !float.IsFinite(center.Y) ||
            !float.IsFinite(center.Z))
        {
            center = Vector3.Zero;
        }

        float radius = 0.0f;

        foreach (MeshData mesh in model.Meshes)
        {
            foreach (Vector3 position in mesh.Positions.Span)
            {
                radius = MathF.Max(radius, Vector3.Distance(position, center));
            }
        }

        if (!float.IsFinite(radius) || radius <= 0.0f)
        {
            radius = 1.0f;
        }

        // Keep the camera and projection finite even for extreme source
        // coordinates. The parser validates finiteness, not useful magnitude.
        radius = MathF.Min(radius, 1_000_000.0f);

        return (center, radius);
    }
}
