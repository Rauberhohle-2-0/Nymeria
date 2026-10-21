using System.Diagnostics;
using System.Numerics;
using Nymeria.Core.Materials;
using Nymeria.Core.Raycasting;
using Nymeria.Core.Scenes;
using Nymeria.Core.Streaming;
using Nymeria.Examples;
using Nymeria.Graphics;
using Nymeria.Platform;
using SDL3;

namespace HelloPicking;

internal static class Program
{
    private const int TargetFrameMilliseconds = 16;
    private const double MaxFrameDeltaSeconds = 0.1;

    private const float MaxPickDistance = 300.0f;
    private const float LoadDistance = 40.0f;
    private const float UnloadDistance = 70.0f;
    private const int MaxLoadsPerUpdate = 9;
    private const double RetryDelaySeconds = 5.0;

    private static readonly Vector3 StartPosition = new(0.0f, 3.5f, 24.0f);
    private const float StartPitch = -0.12f;

    private static int Main(string[] args)
    {
        foreach (string arg in args)
        {
            switch (arg)
            {
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
            return Run();
        }
        catch (DllNotFoundException exception)
        {
            Console.Error.WriteLine("Nymeria could not load the native SDL3 library.");
            Console.Error.WriteLine(
                "Run `dotnet restore Nymeria.slnx`, then start the example with " +
                "`dotnet run --project examples/HelloPicking/HelloPicking.csproj`.");
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
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"Invalid picking configuration: {exception.Message}");
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

    private static int Run()
    {
        var fixture = new PickingFixture();
        var scene = new Scene();
        SceneObject ghost = fixture.CreateGhost();
        scene.Add(ghost);

        var camera = new Camera(StartPosition)
        {
            PitchRadians = StartPitch,
            MoveSpeed = 12.0f,
            FarPlane = 300.0f,
        };

        using Window window = Window.Create(new WindowOptions("Nymeria - HelloPicking"));
        using MaterialRenderer renderer = MaterialRenderer.Create(window.Handle);
        using var controls = new FlyCameraControls(window.Handle, camera);
        using var streamer = new SceneStreamer(
            scene,
            fixture.CreateChunks(),
            new SceneStreamingConfiguration(
                LoadDistance,
                UnloadDistance,
                MaxLoadsPerUpdate,
                RetryDelaySeconds));

        SceneObject? selected = null;
        Material? selectedOriginalMaterial = null;
        bool centerPicking = false;
        bool wasCenterKeyDown = false;
        bool wasGhostKeyDown = false;
        bool wasResetKeyDown = false;
        bool wasLeftButtonDown = false;
        int pickCount = 0;
        string status = "no selection";

        SceneStreamingUpdateResult initial = streamer.Update(camera.Position, 0.0);
        UpdateTitle();

        Console.WriteLine(
            $"HelloPicking: {PickingFixture.ChunkCountPerAxis * PickingFixture.ChunkCountPerAxis} " +
            $"streamed chunks with three shared-mesh boxes each, plus one independent ghost box. " +
            $"Load distance {LoadDistance:F0}, unload distance {UnloadDistance:F0}.");
        Console.WriteLine(
            "Left-click picks with the cursor; V switches to center-of-screen picking. " +
            "G toggles whether the ghost box can be picked, and R resets the camera. " +
            "Shared camera controls: WASD, Space/Ctrl, Shift, and held right mouse button.");
        Console.WriteLine(
            "Picking uses each object's transformed world-space bounds. Clicks print one " +
            "line and the window title shows the current selection; there is no per-frame log.");

        if (initial.FailedLoads != 0 && initial.FirstError is not null)
        {
            Console.WriteLine($"Streaming warning: {initial.FirstError}");
        }

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

            controls.Update(deltaSeconds);

            ReadOnlySpan<bool> keys = SDL.GetKeyboardState(out _);
            bool centerKeyDown = IsKeyDown(keys, SDL.Scancode.V);
            bool ghostKeyDown = IsKeyDown(keys, SDL.Scancode.G);
            bool resetKeyDown = IsKeyDown(keys, SDL.Scancode.R);

            if (centerKeyDown && !wasCenterKeyDown)
            {
                centerPicking = !centerPicking;
                Console.WriteLine(
                    $"Picking mode: {(centerPicking ? "center of viewport" : "mouse cursor")}.");
                UpdateTitle();
            }

            if (ghostKeyDown && !wasGhostKeyDown)
            {
                ghost.IsPickable = !ghost.IsPickable;
                Console.WriteLine(
                    $"Ghost box is {(ghost.IsPickable ? "pickable" : "skipped by picking")}.");
                UpdateTitle();
            }

            if (resetKeyDown && !wasResetKeyDown)
            {
                camera.Position = StartPosition;
                camera.YawRadians = 0.0f;
                camera.PitchRadians = StartPitch;
                Console.WriteLine("Camera reset to the starting position.");
            }

            wasCenterKeyDown = centerKeyDown;
            wasGhostKeyDown = ghostKeyDown;
            wasResetKeyDown = resetKeyDown;

            // Update the active chunk set before picking so a ray can only hit
            // objects that belong to the current scene.
            SceneStreamingUpdateResult update = streamer.Update(
                camera.Position,
                deltaSeconds);

            if (update.FailedLoads != 0 && update.FirstError is not null)
            {
                Console.WriteLine($"Streaming warning: {update.FirstError}");
            }

            if (selected is not null && !scene.Contains(selected))
            {
                // The selection's chunk was unloaded. Drop the reference instead
                // of keeping a stale selection from a previous frame.
                selected = null;
                selectedOriginalMaterial = null;
                status = "no selection (unloaded)";
                Console.WriteLine("Selection cleared because its chunk was unloaded.");
                UpdateTitle();
            }

            SDL.MouseButtonFlags buttons = SDL.GetMouseState(out _, out _);
            bool leftButtonDown = (buttons & SDL.MouseButtonFlags.Left) != 0;

            if (leftButtonDown && !wasLeftButtonDown)
            {
                Pick(!centerPicking);
            }

            wasLeftButtonDown = leftButtonDown;

            renderer.Render(scene, camera);

            long elapsedMilliseconds =
                (Stopwatch.GetTimestamp() - frameStart) * 1000 / Stopwatch.Frequency;
            int delayMilliseconds = TargetFrameMilliseconds - (int)elapsedMilliseconds;

            if (delayMilliseconds > 0)
            {
                SDL.Delay((uint)delayMilliseconds);
            }
        }

        return 0;

        void Pick(bool useCursor)
        {
            pickCount++;

            if (!SDL.GetWindowSizeInPixels(
                    window.Handle,
                    out int drawableWidth,
                    out int drawableHeight) ||
                !SDL.GetWindowSize(
                    window.Handle,
                    out int logicalWidth,
                    out int logicalHeight) ||
                drawableWidth <= 0 ||
                drawableHeight <= 0 ||
                logicalWidth <= 0 ||
                logicalHeight <= 0)
            {
                Console.WriteLine($"Pick {pickCount}: no usable viewport.");
                return;
            }

            Vector2 pixel;

            if (useCursor)
            {
                // SDL mouse coordinates are logical window pixels, while the
                // renderer's projection uses the drawable size. Convert once at
                // this boundary so the ray uses the same pixel space as the
                // camera matrices.
                SDL.GetMouseState(out float mouseX, out float mouseY);
                float scaleX = (float)drawableWidth / logicalWidth;
                float scaleY = (float)drawableHeight / logicalHeight;
                pixel = new Vector2(mouseX * scaleX, mouseY * scaleY);
            }
            else
            {
                pixel = new Vector2(drawableWidth * 0.5f, drawableHeight * 0.5f);
            }

            Matrix4x4 view = camera.ViewMatrix;
            Matrix4x4 projection = camera.CreateProjectionMatrix(
                (float)drawableWidth / drawableHeight);

            if (!ViewportRay.TryCreate(
                    pixel,
                    drawableWidth,
                    drawableHeight,
                    view,
                    projection,
                    out Ray ray))
            {
                Console.WriteLine($"Pick {pickCount}: no ray (viewport or matrices unavailable).");
                return;
            }

            if (!SceneRaycaster.TryRaycast(
                    scene,
                    ray,
                    MaxPickDistance,
                    out RaycastHit hit))
            {
                RestoreSelection();
                selected = null;
                selectedOriginalMaterial = null;
                status = "no selection (miss)";
                Console.WriteLine(
                    $"Pick {pickCount}: miss ({(useCursor ? "cursor" : "center")}).");
                UpdateTitle();
                return;
            }

            RestoreSelection();
            selected = hit.SceneObject;
            selectedOriginalMaterial = selected.Material;
            selected.Material = fixture.HighlightMaterial;

            string label = fixture.GetLabel(selected) ?? "scene object";
            status = $"{label} at {hit.Distance:F2} units";
            Console.WriteLine(
                $"Pick {pickCount}: {status} " +
                $"({(useCursor ? "cursor" : "center")}, bounds).");
            UpdateTitle();
        }

        void RestoreSelection()
        {
            // Only restore material state while the object is still in the
            // scene. An unloaded object may reference the highlight material,
            // but it is no longer rendered and must not be mutated here.
            if (selected is not null &&
                selectedOriginalMaterial is not null &&
                scene.Contains(selected))
            {
                selected.Material = selectedOriginalMaterial;
            }
        }

        void UpdateTitle()
        {
            string title =
                $"Nymeria - HelloPicking | {status} | " +
                $"{(centerPicking ? "center" : "cursor")} | " +
                $"ghost {(ghost.IsPickable ? "pickable" : "skipped")} | " +
                $"{pickCount} picks";

            SDL.SetWindowTitle(window.Handle, title);
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: HelloPicking [-h|--help]\n" +
            "  Left-click picks with the cursor; V toggles center-of-screen picking.\n" +
            "  G toggles the ghost box's pickability; R resets the camera.");
    }

    private static bool IsKeyDown(
        ReadOnlySpan<bool> keys,
        SDL.Scancode scancode)
    {
        int index = (int)scancode;
        return index < keys.Length && keys[index];
    }
}
