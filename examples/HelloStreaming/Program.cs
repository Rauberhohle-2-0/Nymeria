using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Nymeria.Core.Diagnostics;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;
using Nymeria.Core.Streaming;
using Nymeria.Examples;
using Nymeria.Graphics;
using Nymeria.Platform;
using SDL3;

namespace HelloStreaming;

internal static class Program
{
    private const int TargetFrameMilliseconds = 16;
    private const double MaxFrameDeltaSeconds = 0.1;
    private const double StatisticsIntervalSeconds = 1.0;

    private const float DefaultLoadDistance = 55.0f;
    private const float DefaultUnloadDistance = 85.0f;
    private const int MaxLoadsPerUpdate = 4;
    private const double RetryDelaySeconds = 5.0;

    private static int Main(string[] args)
    {
        bool streamingEnabled = true;
        bool cullingEnabled = true;
        float loadDistance = DefaultLoadDistance;
        float unloadDistance = DefaultUnloadDistance;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--no-streaming":
                    streamingEnabled = false;
                    break;
                case "--no-culling":
                    cullingEnabled = false;
                    break;
                case "--load-distance":
                    if (!TryReadDistance(args, ref index, out loadDistance))
                    {
                        return 1;
                    }

                    break;
                case "--unload-distance":
                    if (!TryReadDistance(args, ref index, out unloadDistance))
                    {
                        return 1;
                    }

                    break;
                case "-h":
                case "--help":
                    PrintUsage();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[index]}");
                    PrintUsage();
                    return 1;
            }
        }

        try
        {
            return Run(streamingEnabled, cullingEnabled, loadDistance, unloadDistance);
        }
        catch (DllNotFoundException exception)
        {
            Console.Error.WriteLine("Nymeria could not load the native SDL3 library.");
            Console.Error.WriteLine(
                "Run `dotnet restore Nymeria.slnx`, then start the example with " +
                "`dotnet run --project examples/HelloStreaming/HelloStreaming.csproj`.");
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
            Console.Error.WriteLine($"Invalid streaming configuration: {exception.Message}");
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
        bool streamingEnabled,
        bool cullingEnabled,
        float loadDistance,
        float unloadDistance)
    {
        MeshData sharedCube = StreamingFixture.CreateSharedCubeMesh();
        Material[] sharedMaterials = CreateMaterials();
        IReadOnlyList<SceneChunkDefinition> chunks = StreamingFixture.CreateChunks(
            sharedCube,
            sharedMaterials);
        var scene = new Scene();
        var configuration = new SceneStreamingConfiguration(
            loadDistance,
            unloadDistance,
            maxLoadsPerUpdate: MaxLoadsPerUpdate,
            retryDelaySeconds: RetryDelaySeconds);
        var camera = new Camera(new Vector3(0.0f, 12.0f, 70.0f))
        {
            PitchRadians = -0.12f,
            MoveSpeed = 30.0f,
            FarPlane = 500.0f,
        };

        using Window window = Window.Create(
            new WindowOptions("Nymeria - HelloStreaming"));
        using MaterialRenderer renderer = MaterialRenderer.Create(window.Handle);
        using var controls = new FlyCameraControls(window.Handle, camera);
        using var streamer = new SceneStreamer(scene, chunks, configuration);

        renderer.FrustumCullingEnabled = cullingEnabled;

        // Make the first frame useful. In the streaming baseline this loads only
        // the configured budget; --no-streaming fills the initial demand set and
        // then freezes it for a no-churn comparison.
        SceneStreamingUpdateResult initial = streamer.Update(camera.Position, 0.0);

        if (!streamingEnabled)
        {
            for (int update = 0; update < 1024; update++)
            {
                SceneStreamingUpdateResult fill = streamer.Update(camera.Position, 0.0);
                initial = fill;

                if (fill.LoadedChunks == 0)
                {
                    break;
                }
            }
        }

        Console.WriteLine(
            $"HelloStreaming: {chunks.Count} deterministic chunks, one shared cube " +
            $"mesh, and {sharedMaterials.Length} shared materials. Load distance " +
            $"{configuration.LoadDistance:F0}, unload distance " +
            $"{configuration.UnloadDistance:F0}, hysteresis " +
            $"{configuration.UnloadDistance - configuration.LoadDistance:F0}, " +
            $"max {configuration.MaxLoadsPerUpdate} loads per update.");
        Console.WriteLine(
            "S toggles automatic streaming, T cycles teleport positions, C toggles " +
            "frustum culling, R returns to the start. Shared camera controls: WASD, " +
            "Space/Ctrl, Shift, and held right mouse button.");
        Console.WriteLine(
            "Statistics print once per second: known/loaded/unloaded/failed chunks, " +
            "streamed objects, cumulative and last-update loads/unloads/failures, " +
            "streaming time, pruned GPU entries, FPS, and renderer draw counts.");

        int totalPrunedResources = 0;

        if (initial.FailedLoads != 0 && initial.FirstError is not null)
        {
            Console.WriteLine($"Streaming warning: {initial.FirstError}");
        }

        PrintStatistics(
            streamer.Statistics,
            streamingEnabled,
            cullingEnabled,
            camera,
            initial,
            updateSummary: default,
            renderSummary: default,
            renderer.LastFrameStatistics,
            framesPerSecond: 0.0,
            lastPrunedResources: 0,
            totalPrunedResources: 0);

        var clock = Stopwatch.StartNew();
        double previousSeconds = clock.Elapsed.TotalSeconds;
        double previousStatisticsSeconds = previousSeconds;
        int statisticsFrames = 0;
        var renderWindow = new FrameTimingWindow(capacity: 120, warmupSamples: 20);
        var streamWindow = new FrameTimingWindow(capacity: 120, warmupSamples: 20);
        bool wasStreamingKeyDown = false;
        bool wasTeleportKeyDown = false;
        bool wasCullingKeyDown = false;
        bool wasResetKeyDown = false;
        int teleportIndex = 0;

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
            bool streamingKeyDown = IsKeyDown(keys, SDL.Scancode.S);
            bool teleportKeyDown = IsKeyDown(keys, SDL.Scancode.T);
            bool cullingKeyDown = IsKeyDown(keys, SDL.Scancode.C);
            bool resetKeyDown = IsKeyDown(keys, SDL.Scancode.R);

            if (streamingKeyDown && !wasStreamingKeyDown)
            {
                streamingEnabled = !streamingEnabled;
                Console.WriteLine(
                    $"Automatic streaming {(streamingEnabled ? "enabled" : "disabled; " +
                        "the active chunk set is frozen")}.");
            }

            if (teleportKeyDown && !wasTeleportKeyDown)
            {
                teleportIndex = (teleportIndex + 1) % 3;
                ApplyTeleport(camera, teleportIndex);
                Console.WriteLine(
                    $"Teleported to ({camera.Position.X:F0}, {camera.Position.Y:F0}, " +
                    $"{camera.Position.Z:F0}).");
            }

            if (cullingKeyDown && !wasCullingKeyDown)
            {
                cullingEnabled = !cullingEnabled;
                renderer.FrustumCullingEnabled = cullingEnabled;
                Console.WriteLine(
                    $"Frustum culling {(cullingEnabled ? "enabled" : "disabled")}.");
            }

            if (resetKeyDown && !wasResetKeyDown)
            {
                teleportIndex = 0;
                ApplyTeleport(camera, teleportIndex);
                Console.WriteLine("Camera reset to the starting position.");
            }

            wasStreamingKeyDown = streamingKeyDown;
            wasTeleportKeyDown = teleportKeyDown;
            wasCullingKeyDown = cullingKeyDown;
            wasResetKeyDown = resetKeyDown;

            SceneStreamingUpdateResult update = default;
            double updateMilliseconds = 0.0;
            int prunedResources = 0;
            double pruneMilliseconds = 0.0;

            if (streamingEnabled)
            {
                long updateStart = Stopwatch.GetTimestamp();
                update = streamer.Update(camera.Position, deltaSeconds);
                long updateEnd = Stopwatch.GetTimestamp();
                updateMilliseconds = ElapsedMilliseconds(updateStart, updateEnd);
                streamWindow.AddSample(updateMilliseconds);

                if (update.UnloadedChunks != 0)
                {
                    long pruneStart = Stopwatch.GetTimestamp();
                    prunedResources = renderer.PruneUnusedResources(scene);
                    long pruneEnd = Stopwatch.GetTimestamp();
                    pruneMilliseconds = ElapsedMilliseconds(pruneStart, pruneEnd);
                    totalPrunedResources += prunedResources;
                }

                if (update.LoadedChunks != 0 ||
                    update.UnloadedChunks != 0 ||
                    update.FailedLoads != 0)
                {
                    SceneStreamingStatistics snapshot = streamer.Statistics;
                    Console.WriteLine(
                        $"stream: loaded {update.LoadedChunks}, unloaded " +
                        $"{update.UnloadedChunks}, failed {update.FailedLoads}, " +
                        $"active {snapshot.LoadedChunks}, objects " +
                        $"{snapshot.ActiveSceneObjects}, update " +
                        $"{updateMilliseconds:F2} ms" +
                        (prunedResources != 0
                            ? $", pruned {prunedResources} GPU entries in " +
                              $"{pruneMilliseconds:F2} ms"
                            : string.Empty));
                }

                if (update.FirstError is not null)
                {
                    Console.WriteLine($"Streaming warning: {update.FirstError}");
                }
            }

            long renderStart = Stopwatch.GetTimestamp();
            renderer.Render(scene, camera);
            long renderEnd = Stopwatch.GetTimestamp();
            renderWindow.AddSample(ElapsedMilliseconds(renderStart, renderEnd));
            statisticsFrames++;

            double statisticsSeconds = nowSeconds - previousStatisticsSeconds;

            if (statisticsSeconds >= StatisticsIntervalSeconds)
            {
                double framesPerSecond = statisticsFrames / statisticsSeconds;
                PrintStatistics(
                    streamer.Statistics,
                    streamingEnabled,
                    cullingEnabled,
                    camera,
                    update,
                    streamWindow.GetSummary(),
                    renderWindow.GetSummary(),
                    renderer.LastFrameStatistics,
                    framesPerSecond,
                    prunedResources,
                    totalPrunedResources);
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
        return
        [
            new Material(new Vector3(0.90f, 0.32f, 0.22f), null, "warm red"),
            new Material(new Vector3(0.22f, 0.55f, 0.92f), null, "blue"),
            new Material(new Vector3(0.30f, 0.80f, 0.42f), null, "green"),
            new Material(new Vector3(0.92f, 0.76f, 0.24f), null, "yellow"),
            new Material(new Vector3(0.72f, 0.38f, 0.88f), null, "violet"),
            new Material(new Vector3(0.32f, 0.82f, 0.84f), null, "cyan"),
        ];
    }

    private static void ApplyTeleport(Camera camera, int index)
    {
        switch (index)
        {
            case 0:
                camera.Position = new Vector3(0.0f, 12.0f, 70.0f);
                camera.YawRadians = 0.0f;
                camera.PitchRadians = -0.12f;
                break;
            case 1:
                camera.Position = new Vector3(-90.0f, 10.0f, -90.0f);
                camera.YawRadians = 2.356f;
                camera.PitchRadians = -0.10f;
                break;
            default:
                camera.Position = new Vector3(90.0f, 10.0f, 90.0f);
                camera.YawRadians = -0.785f;
                camera.PitchRadians = -0.10f;
                break;
        }
    }

    private static void PrintStatistics(
        SceneStreamingStatistics streaming,
        bool streamingEnabled,
        bool cullingEnabled,
        Camera camera,
        SceneStreamingUpdateResult update,
        FrameTimingSummary updateSummary,
        FrameTimingSummary renderSummary,
        MaterialRenderer.VisibilityStatistics visibility,
        double framesPerSecond,
        int lastPrunedResources,
        int totalPrunedResources)
    {
        Console.WriteLine(
            $"stream {(streamingEnabled ? "on " : "off")} | " +
            $"focus {camera.Position.X,5:F0},{camera.Position.Z,5:F0} | " +
            $"known {streaming.KnownChunks,2} | " +
            $"active {streaming.LoadedChunks,2} | " +
            $"unloaded {streaming.UnloadedChunks,2} | " +
            $"failed {streaming.FailedChunks,2} | " +
            $"objects {streaming.ActiveSceneObjects,4} | " +
            $"loads {streaming.SuccessfulLoads,4}/{streaming.LoadAttempts,4} | " +
            $"unloads {streaming.Unloads,4} | " +
            $"failures {streaming.LoadFailures,3}/{streaming.ReleaseFailures,3} | " +
            $"last +{update.LoadedChunks}/-{update.UnloadedChunks} | " +
            $"update {updateSummary.AverageMilliseconds:F2} " +
            $"p95 {updateSummary.P95Milliseconds:F2} " +
            $"max {updateSummary.MaximumMilliseconds:F2} ms | " +
            $"pruned {lastPrunedResources}/{totalPrunedResources} | " +
            $"fps {framesPerSecond,5:F1} | " +
            $"render {renderSummary.AverageMilliseconds:F2} " +
            $"p95 {renderSummary.P95Milliseconds:F2} ms | " +
            $"culling {(cullingEnabled ? "on " : "off")} | " +
            $"candidates {visibility.TotalInstances,4} | " +
            $"frustum culled {visibility.CulledInstances,4} | " +
            $"draws {visibility.DrawCalls,4}");
    }

    private static bool TryReadDistance(
        string[] args,
        ref int index,
        out float distance)
    {
        distance = 0.0f;

        if (index + 1 >= args.Length)
        {
            Console.Error.WriteLine($"Missing value after {args[index]}.");
            PrintUsage();
            return false;
        }

        string value = args[++index];

        if (!float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out distance) ||
            !float.IsFinite(distance))
        {
            Console.Error.WriteLine(
                $"Invalid distance '{value}'. Expected a finite invariant-culture number.");
            return false;
        }

        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: HelloStreaming [--no-streaming] [--no-culling] " +
            "[--load-distance <number>] [--unload-distance <number>]\n" +
            "  --no-streaming       Load the starting demand set, then freeze the active set.\n" +
            "  --no-culling         Start with frustum culling disabled; C toggles it.\n" +
            "  --load-distance N    Inclusive chunk demand distance (default 55).\n" +
            "  --unload-distance N  Distance beyond which a chunk unloads; must exceed load.\n" +
            "  -h, --help           Show this help.");
    }

    private static double ElapsedMilliseconds(long start, long end)
    {
        return (end - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static bool IsKeyDown(
        ReadOnlySpan<bool> keys,
        SDL.Scancode scancode)
    {
        int index = (int)scancode;
        return index < keys.Length && keys[index];
    }
}
