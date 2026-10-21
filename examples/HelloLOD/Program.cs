using System.Diagnostics;
using System.Numerics;
using Nymeria.Core.Diagnostics;
using Nymeria.Core.Lod;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;
using Nymeria.Examples;
using Nymeria.Graphics;
using Nymeria.Platform;
using SDL3;

namespace HelloLOD;

internal static class Program
{
    private const int TargetFrameMilliseconds = 16;
    private const double MaxFrameDeltaSeconds = 0.1;
    private const double StatisticsIntervalSeconds = 1.0;

    private const float FirstThreshold = 14.0f;
    private const float SecondThreshold = 38.0f;
    private const float MaximumDistance = 120.0f;
    private const float Hysteresis = 0.75f;
    private const float ThresholdScaleStep = 0.1f;
    private const float MinimumThresholdScale = 0.5f;
    private const float MaximumThresholdScale = 3.0f;

    private const int Columns = 3;
    private const int Rows = 15;
    private const float ColumnSpacing = 7.0f;
    private const float RowSpacing = 9.0f;

    private static int Main(string[] args)
    {
        bool lodEnabled = true;
        bool distanceCullingEnabled = true;
        bool frustumCullingEnabled = true;

        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--no-lod":
                    lodEnabled = false;
                    break;
                case "--no-distance-culling":
                    distanceCullingEnabled = false;
                    break;
                case "--no-culling":
                    frustumCullingEnabled = false;
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
            return Run(lodEnabled, distanceCullingEnabled, frustumCullingEnabled);
        }
        catch (DllNotFoundException exception)
        {
            Console.Error.WriteLine("Nymeria could not load the native SDL3 library.");
            Console.Error.WriteLine(
                "Run `dotnet restore Nymeria.slnx`, then start the example with " +
                "`dotnet run --project examples/HelloLOD/HelloLOD.csproj`.");
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

    private static int Run(
        bool lodEnabled,
        bool distanceCullingEnabled,
        bool frustumCullingEnabled)
    {
        MeshData high = LodFixture.CreateHighDetail();
        MeshData medium = LodFixture.CreateMediumDetail();
        MeshData low = LodFixture.CreateLowDetail();
        Material[] materials = CreateMaterials();
        var scene = new Scene();

        float thresholdScale = 1.0f;
        MeshLod lod = CreateLod(high, medium, low, thresholdScale);
        PopulateScene(scene, high, materials, lod);

        var camera = new Camera(new Vector3(0.0f, 4.0f, 14.0f))
        {
            PitchRadians = -0.08f,
            MoveSpeed = 14.0f,
            FarPlane = 220.0f,
        };

        using Window window = Window.Create(new WindowOptions("Nymeria - HelloLOD"));
        using MaterialRenderer renderer = MaterialRenderer.Create(window.Handle);
        using var controls = new FlyCameraControls(window.Handle, camera);

        renderer.LodEnabled = lodEnabled;
        renderer.DistanceCullingEnabled = distanceCullingEnabled;
        renderer.FrustumCullingEnabled = frustumCullingEnabled;

        Console.WriteLine(
            $"HelloLOD: {scene.Count} instances, three explicit sphere variants " +
            $"({high.Indices.Length / 3}, {medium.Indices.Length / 3}, and " +
            $"{low.Indices.Length / 3} triangles), one shared mesh upload per variant.");
        Console.WriteLine(
            "L toggles LOD, X toggles distance culling, F toggles frustum culling. " +
            "[ and ] change both thresholds; R resets them. Shared camera controls: " +
            "WASD, Space/Ctrl, Shift, and held right mouse button.");
        Console.WriteLine(
            "Statistics print once per second: candidates, frustum-culled, " +
            "distance-culled, drawn, actual draw calls, submitted triangles, " +
            "per-level usage, and render-time distribution.");
        PrintStatistics(
            renderer.LastFrameStatistics,
            lodEnabled,
            distanceCullingEnabled,
            frustumCullingEnabled,
            thresholdScale,
            framesPerSecond: 0.0,
            renderAverage: 0.0,
            renderP95: 0.0);

        var clock = Stopwatch.StartNew();
        double previousSeconds = clock.Elapsed.TotalSeconds;
        double previousStatisticsSeconds = previousSeconds;
        int statisticsFrames = 0;
        var renderWindow = new FrameTimingWindow(capacity: 120, warmupSamples: 20);
        bool wasLodKeyDown = false;
        bool wasDistanceKeyDown = false;
        bool wasCullingKeyDown = false;
        bool wasDecreaseKeyDown = false;
        bool wasIncreaseKeyDown = false;
        bool wasResetKeyDown = false;

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
            bool lodKeyDown = IsKeyDown(keys, SDL.Scancode.L);
            bool distanceKeyDown = IsKeyDown(keys, SDL.Scancode.X);
            bool cullingKeyDown = IsKeyDown(keys, SDL.Scancode.F);
            bool decreaseKeyDown = IsKeyDown(keys, SDL.Scancode.Leftbracket);
            bool increaseKeyDown = IsKeyDown(keys, SDL.Scancode.Rightbracket);
            bool resetKeyDown = IsKeyDown(keys, SDL.Scancode.R);

            if (lodKeyDown && !wasLodKeyDown)
            {
                lodEnabled = !lodEnabled;
                renderer.LodEnabled = lodEnabled;
                Console.WriteLine($"LOD {(lodEnabled ? "enabled" : "disabled")}.");
            }

            if (distanceKeyDown && !wasDistanceKeyDown)
            {
                distanceCullingEnabled = !distanceCullingEnabled;
                renderer.DistanceCullingEnabled = distanceCullingEnabled;
                Console.WriteLine(
                    $"Distance culling {(distanceCullingEnabled ? "enabled" : "disabled")}.");
            }

            if (cullingKeyDown && !wasCullingKeyDown)
            {
                frustumCullingEnabled = !frustumCullingEnabled;
                renderer.FrustumCullingEnabled = frustumCullingEnabled;
                Console.WriteLine(
                    $"Frustum culling {(frustumCullingEnabled ? "enabled" : "disabled")}.");
            }

            bool thresholdsChanged = false;

            if (decreaseKeyDown && !wasDecreaseKeyDown)
            {
                thresholdScale = Math.Max(
                    MinimumThresholdScale,
                    thresholdScale - ThresholdScaleStep);
                thresholdsChanged = true;
            }

            if (increaseKeyDown && !wasIncreaseKeyDown)
            {
                thresholdScale = Math.Min(
                    MaximumThresholdScale,
                    thresholdScale + ThresholdScaleStep);
                thresholdsChanged = true;
            }

            if (resetKeyDown && !wasResetKeyDown)
            {
                thresholdScale = 1.0f;
                thresholdsChanged = true;
            }

            if (thresholdsChanged)
            {
                lod = CreateLod(high, medium, low, thresholdScale);
                ApplyLod(scene, lod);
                Console.WriteLine(
                    $"Thresholds: {FirstActivation(thresholdScale):F1} / " +
                    $"{SecondActivation(thresholdScale):F1}, " +
                    $"max {MaximumRenderDistance(thresholdScale):F1}, " +
                    $"hysteresis {Hysteresis:F2}.");
            }

            wasLodKeyDown = lodKeyDown;
            wasDistanceKeyDown = distanceKeyDown;
            wasCullingKeyDown = cullingKeyDown;
            wasDecreaseKeyDown = decreaseKeyDown;
            wasIncreaseKeyDown = increaseKeyDown;
            wasResetKeyDown = resetKeyDown;

            long renderStart = Stopwatch.GetTimestamp();
            renderer.Render(scene, camera);
            long renderEnd = Stopwatch.GetTimestamp();
            renderWindow.AddSample(ElapsedMilliseconds(renderStart, renderEnd));
            statisticsFrames++;

            double statisticsSeconds = nowSeconds - previousStatisticsSeconds;

            if (statisticsSeconds >= StatisticsIntervalSeconds)
            {
                double framesPerSecond = statisticsFrames / statisticsSeconds;
                FrameTimingSummary renderSummary = renderWindow.GetSummary();
                PrintStatistics(
                    renderer.LastFrameStatistics,
                    lodEnabled,
                    distanceCullingEnabled,
                    frustumCullingEnabled,
                    thresholdScale,
                    framesPerSecond,
                    renderSummary.AverageMilliseconds,
                    renderSummary.P95Milliseconds);
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

    private static MeshLod CreateLod(
        MeshData high,
        MeshData medium,
        MeshData low,
        float thresholdScale)
    {
        return new MeshLod(
            [
                new MeshLodLevel(high, 0.0f),
                new MeshLodLevel(medium, FirstActivation(thresholdScale)),
                new MeshLodLevel(low, SecondActivation(thresholdScale)),
            ],
            maximumRenderDistance: MaximumRenderDistance(thresholdScale),
            hysteresis: Hysteresis);
    }

    private static float FirstActivation(float scale) => FirstThreshold * scale;

    private static float SecondActivation(float scale) => SecondThreshold * scale;

    private static float MaximumRenderDistance(float scale) => MaximumDistance * scale;

    private static void PopulateScene(
        Scene scene,
        MeshData high,
        Material[] materials,
        MeshLod lod)
    {
        // Three columns recede from the camera so all three levels and the
        // distance-cull boundary are visible without moving.
        for (int row = 0; row < Rows; row++)
        {
            for (int column = 0; column < Columns; column++)
            {
                int index = (row * Columns) + column;
                float x = (column - 1) * ColumnSpacing;
                float z = 2.0f - (row * RowSpacing);
                float y = 1.5f + ((row % 3) * 0.35f);
                float scale = 1.1f + ((index % 4) * 0.12f);

                scene.Add(new SceneObject(
                    high,
                    new Transform(
                        new Vector3(x, y, z),
                        Quaternion.CreateFromYawPitchRoll(index * 0.11f, 0.0f, 0.0f),
                        new Vector3(scale, scale, scale)),
                    materials[index % materials.Length],
                    lod));
            }
        }

        // Larger objects exercise the bounding-volume-aware distance: their
        // nearest surface is closer than their origin, so they switch later.
        AddLargeInstance(scene, high, materials[0], lod, new Vector3(0.0f, 4.0f, -22.0f), 2.6f);
        AddLargeInstance(scene, high, materials[1], lod, new Vector3(-15.0f, 5.0f, -62.0f), 3.4f);
        AddLargeInstance(scene, high, materials[2], lod, new Vector3(15.0f, 6.0f, -98.0f), 4.2f);
    }

    private static void AddLargeInstance(
        Scene scene,
        MeshData high,
        Material material,
        MeshLod lod,
        Vector3 position,
        float scale)
    {
        scene.Add(new SceneObject(
            high,
            new Transform(position, Quaternion.Identity, new Vector3(scale)),
            material,
            lod));
    }

    private static void ApplyLod(Scene scene, MeshLod lod)
    {
        foreach (SceneObject sceneObject in scene.Objects)
        {
            // Replacing the configuration resets each object's level, so the
            // new thresholds take effect deterministically.
            sceneObject.Lod = lod;
        }
    }

    private static Material[] CreateMaterials()
    {
        return
        [
            new Material(new Vector3(0.90f, 0.32f, 0.22f), null, "warm red"),
            new Material(new Vector3(0.22f, 0.55f, 0.92f), null, "blue"),
            new Material(new Vector3(0.30f, 0.80f, 0.42f), null, "green"),
            new Material(new Vector3(0.92f, 0.76f, 0.24f), null, "yellow"),
        ];
    }

    private static void PrintStatistics(
        MaterialRenderer.VisibilityStatistics statistics,
        bool lodEnabled,
        bool distanceCullingEnabled,
        bool frustumCullingEnabled,
        float thresholdScale,
        double framesPerSecond,
        double renderAverage,
        double renderP95)
    {
        Console.WriteLine(
            $"lod {(lodEnabled ? "on " : "off")} | " +
            $"distance {(distanceCullingEnabled ? "on " : "off")} | " +
            $"frustum {(frustumCullingEnabled ? "on " : "off")} | " +
            $"thresholds {FirstActivation(thresholdScale):F1}/" +
            $"{SecondActivation(thresholdScale):F1} " +
            $"max {MaximumRenderDistance(thresholdScale):F1} | " +
            $"fps {framesPerSecond,5:F1} | " +
            $"render avg {renderAverage:F2} p95 {renderP95:F2} ms | " +
            $"candidates {statistics.TotalInstances,3} | " +
            $"frustum culled {statistics.CulledInstances,3} | " +
            $"distance culled {statistics.DistanceCulledInstances,3} | " +
            $"drawn {statistics.VisibleInstances,3} | " +
            $"draw calls {statistics.DrawCalls,3} | " +
            $"triangles {statistics.SubmittedTriangles,6} | " +
            $"LOD {statistics.LodUsage[0]}/{statistics.LodUsage[1]}/" +
            $"{statistics.LodUsage[2]}");
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: HelloLOD [--no-lod] [--no-distance-culling] [--no-culling]\n" +
            "  --no-lod                 Start with LOD disabled; L toggles it.\n" +
            "  --no-distance-culling    Start with distance culling disabled; X toggles it.\n" +
            "  --no-culling             Start with frustum culling disabled; F toggles it.\n" +
            "  -h, --help               Show this help.");
    }

    private static double ElapsedMilliseconds(long startTimestamp, long endTimestamp)
    {
        return (endTimestamp - startTimestamp) * 1000.0 / Stopwatch.Frequency;
    }

    private static bool IsKeyDown(
        ReadOnlySpan<bool> keys,
        SDL.Scancode scancode)
    {
        int index = (int)scancode;
        return index < keys.Length && keys[index];
    }
}
