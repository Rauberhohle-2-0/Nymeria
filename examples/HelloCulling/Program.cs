using System.Diagnostics;
using System.Numerics;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;
using Nymeria.Examples;
using Nymeria.Graphics;
using Nymeria.Platform;
using SDL3;

namespace HelloCulling;

internal static class Program
{
    private const int TargetFrameMilliseconds = 16;
    private const double MaxFrameDeltaSeconds = 0.1;
    private const double StatisticsIntervalSeconds = 1.0;

    private const int GridColumns = 21;
    private const int GridRows = 21;
    private const int BehindCameraRows = 5;
    private const int BehindCameraColumns = 12;
    private const int BeyondFarPlaneCount = 10;
    private const int OffscreenSideCount = 40;

    private static int Main(string[] args)
    {
        bool cullingEnabled = true;

        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--no-culling":
                    cullingEnabled = false;
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
            return Run(cullingEnabled);
        }
        catch (DllNotFoundException exception)
        {
            Console.Error.WriteLine("Nymeria could not load the native SDL3 library.");
            Console.Error.WriteLine(
                "Run `dotnet restore Nymeria.slnx`, then start the example with " +
                "`dotnet run --project examples/HelloCulling/HelloCulling.csproj`.");
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

    private static int Run(bool cullingEnabled)
    {
        MeshData sharedMesh = CullingFixture.CreateCubeMesh();
        Material[] sharedMaterials = CreateMaterials();
        Scene scene = CreateScene(sharedMesh, sharedMaterials);

        var camera = new Camera(new Vector3(0.0f, 10.0f, 26.0f))
        {
            PitchRadians = -0.18f,
            MoveSpeed = 8.0f,
            FarPlane = 120.0f,
        };

        using Window window = Window.Create(new WindowOptions("Nymeria - HelloCulling"));
        using MaterialRenderer renderer = MaterialRenderer.Create(window.Handle);
        using var controls = new FlyCameraControls(window.Handle, camera);

        renderer.FrustumCullingEnabled = cullingEnabled;

        Console.WriteLine(
            $"HelloCulling: {scene.Count} instances of one shared cube mesh, " +
            $"{sharedMaterials.Length} shared materials, and one GPU mesh upload.");
        Console.WriteLine(
            "C toggles frustum culling. Start with --no-culling to compare. " +
            "Shared camera controls: WASD, Space/Ctrl, Shift, and held right " +
            "mouse button.");
        Console.WriteLine(
            "Statistics print once per second: instances tested, submitted, " +
            "frustum-culled, and actual indexed draw calls.");
        PrintStatistics(renderer.LastFrameStatistics, cullingEnabled, framesPerSecond: 0.0);

        var clock = Stopwatch.StartNew();
        double previousSeconds = clock.Elapsed.TotalSeconds;
        double previousStatisticsSeconds = previousSeconds;
        int statisticsFrames = 0;
        bool wasToggleDown = false;

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
            // state is current for this frame.
            controls.Update(deltaSeconds);

            ReadOnlySpan<bool> keys = SDL.GetKeyboardState(out _);
            bool isToggleDown = IsKeyDown(keys, SDL.Scancode.C);

            if (isToggleDown && !wasToggleDown)
            {
                cullingEnabled = !cullingEnabled;
                renderer.FrustumCullingEnabled = cullingEnabled;
                Console.WriteLine(
                    $"Frustum culling {(cullingEnabled ? "enabled" : "disabled")}.");
            }

            wasToggleDown = isToggleDown;

            renderer.Render(scene, camera);
            statisticsFrames++;

            double statisticsSeconds = nowSeconds - previousStatisticsSeconds;

            if (statisticsSeconds >= StatisticsIntervalSeconds)
            {
                double framesPerSecond = statisticsFrames / statisticsSeconds;
                PrintStatistics(
                    renderer.LastFrameStatistics,
                    cullingEnabled,
                    framesPerSecond);
                previousStatisticsSeconds = nowSeconds;
                statisticsFrames = 0;
            }

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

    private static Material[] CreateMaterials()
    {
        // Materials are immutable CPU data and are shared by many instances, so
        // the renderer never uploads a texture or duplicates material state.
        return
        [
            new Material(new Vector3(0.90f, 0.32f, 0.22f), null, "warm red"),
            new Material(new Vector3(0.22f, 0.55f, 0.92f), null, "blue"),
            new Material(new Vector3(0.30f, 0.80f, 0.42f), null, "green"),
            new Material(new Vector3(0.92f, 0.76f, 0.24f), null, "yellow"),
        ];
    }

    private static Scene CreateScene(MeshData mesh, Material[] materials)
    {
        var scene = new Scene();

        // A dense deterministic grid. Its near rows extend past the horizontal
        // view, while far rows remain visible, so some instances cross each
        // frustum plane as the camera moves.
        for (int row = 0; row < GridRows; row++)
        {
            float z = -55.0f + (row * 3.0f);

            for (int column = 0; column < GridColumns; column++)
            {
                float x = -30.0f + (column * 3.0f);
                float y = 0.5f + ((column + row) % 3) * 0.8f;
                float yaw = (column * 0.37f) + (row * 0.19f);
                float pitch = ((column + (row * 2)) % 5) * 0.06f;
                float scaleX = ((column + row) % 5 == 0) ? -1.1f : 1.1f;
                float scaleY = 0.8f + ((column + (row * 3)) % 4) * 0.15f;
                float scaleZ = 1.0f + ((column + (row * 2)) % 3) * 0.2f;

                scene.Add(new SceneObject(
                    mesh,
                    new Transform(
                        new Vector3(x, y, z),
                        Quaternion.CreateFromYawPitchRoll(yaw, pitch, 0.0f),
                        new Vector3(scaleX, scaleY, scaleZ)),
                    materials[(column + (row * 3)) % materials.Length]));
            }
        }

        // These are behind the camera's initial -Z view direction.
        for (int row = 0; row < BehindCameraRows; row++)
        {
            for (int column = 0; column < BehindCameraColumns; column++)
            {
                int index = (row * BehindCameraColumns) + column;

                scene.Add(new SceneObject(
                    mesh,
                    TransformAt(
                        new Vector3(
                            -22.0f + (column * 4.0f),
                            1.0f + (column % 2),
                            30.0f + (row * 2.0f)),
                        index * 0.3f),
                    materials[index % materials.Length]));
            }
        }

        // These are beyond the camera's 120-unit far plane.
        for (int i = 0; i < BeyondFarPlaneCount; i++)
        {
            scene.Add(new SceneObject(
                mesh,
                TransformAt(
                    new Vector3(-18.0f + (i * 4.0f), 1.0f, -160.0f),
                    i * 0.4f),
                materials[i % materials.Length]));
        }

        // These are far outside the horizontal view but within the far plane.
        for (int i = 0; i < OffscreenSideCount; i++)
        {
            float x = i < OffscreenSideCount / 2 ? -90.0f : 90.0f;
            scene.Add(new SceneObject(
                mesh,
                TransformAt(
                    new Vector3(x, 1.0f, -10.0f - ((i % 10) * 6.0f)),
                    i * 0.5f),
                materials[i % materials.Length]));
        }

        return scene;
    }

    private static Transform TransformAt(Vector3 position, float yawRadians)
    {
        return new Transform(
            position,
            Quaternion.CreateFromYawPitchRoll(yawRadians, 0.0f, 0.0f),
            Vector3.One);
    }

    private static void PrintStatistics(
        MaterialRenderer.VisibilityStatistics statistics,
        bool cullingEnabled,
        double framesPerSecond)
    {
        Console.WriteLine(
            $"culling {(cullingEnabled ? "on " : "off")} | " +
            $"fps {framesPerSecond,5:F1} | " +
            $"instances {statistics.TotalInstances,4} | " +
            $"submitted {statistics.VisibleInstances,4} | " +
            $"culled {statistics.CulledInstances,4} | " +
            $"draw calls {statistics.DrawCalls,4}");
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: HelloCulling [--no-culling]\n" +
            "  --no-culling  Start with frustum culling disabled; C toggles it.\n" +
            "  -h, --help    Show this help.");
    }

    private static bool IsKeyDown(
        ReadOnlySpan<bool> keys,
        SDL.Scancode scancode)
    {
        int index = (int)scancode;
        return index < keys.Length && keys[index];
    }
}
