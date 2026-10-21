using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Nymeria.Core.Diagnostics;
using Nymeria.Core.Lod;
using Nymeria.Core.Scenes;
using Nymeria.Examples;
using Nymeria.Graphics;
using Nymeria.Platform;
using SDL3;

namespace HelloBenchmark;

/// <summary>
/// A deterministic, auto-exiting CPU benchmark for the scene and material
/// submission path.
/// </summary>
internal static class Program
{
    private const int TargetFrameMilliseconds = 16;
    private const int DefaultInstances = 2048;
    private const int DefaultWarmupFrames = 120;
    private const int DefaultSampleFrames = 600;
    private const int MaximumFrames = 100_000;

    private static int Main(string[] args)
    {
        // Keep reports reproducible across machines and locales.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        if (!TryParseOptions(args, out BenchmarkOptions options))
        {
            return 1;
        }

        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        try
        {
            return Run(options);
        }
        catch (DllNotFoundException exception)
        {
            Console.Error.WriteLine("Nymeria could not load the native SDL3 library.");
            Console.Error.WriteLine(
                "Run `dotnet restore Nymeria.slnx`, then start the benchmark with " +
                "`dotnet run --project examples/HelloBenchmark/HelloBenchmark.csproj`.");
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
            Console.Error.WriteLine($"Nymeria benchmark failed: {exception.Message}");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Nymeria benchmark stopped because of an unexpected error.");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int Run(BenchmarkOptions options)
    {
        (Scene scene, int materialCount, int highTriangles, int mediumTriangles, int lowTriangles) =
            BenchmarkScene.Create(options.Instances);
        var camera = new Camera(new Vector3(0.0f, 10.0f, 26.0f))
        {
            PitchRadians = -0.18f,
            FarPlane = 120.0f,
        };

        using Window window = Window.Create(new WindowOptions("Nymeria - HelloBenchmark"));
        using MaterialRenderer renderer = MaterialRenderer.Create(window.Handle);

        if (!SDL.GetWindowSize(window.Handle, out int logicalWidth, out int logicalHeight) ||
            !SDL.GetWindowSizeInPixels(
                window.Handle,
                out int drawableWidth,
                out int drawableHeight))
        {
            throw new InvalidOperationException(
                $"SDL could not query the benchmark window size: {SDL.GetError()}");
        }

        PrintHeader(
            options,
            scene,
            materialCount,
            highTriangles,
            mediumTriangles,
            lowTriangles,
            logicalWidth,
            logicalHeight,
            drawableWidth,
            drawableHeight);

        ReadOnlySpan<bool> cullingModes = options.CullingMode switch
        {
            CullingMode.Off => [false],
            CullingMode.On => [true],
            _ => [false, true],
        };
        ReadOnlySpan<bool> lodModes = options.LodMode switch
        {
            LodMode.Off => [false],
            LodMode.On => [true],
            _ => [false, true],
        };

        foreach (bool culling in cullingModes)
        {
            foreach (bool lod in lodModes)
            {
                if (!RunMode(
                        window,
                        renderer,
                        scene,
                        camera,
                        options,
                        culling,
                        lod))
                {
                    return 2;
                }
            }
        }

        return 0;
    }

    private static bool RunMode(
        Window window,
        MaterialRenderer renderer,
        Scene scene,
        Camera camera,
        BenchmarkOptions options,
        bool cullingEnabled,
        bool lodEnabled)
    {
        renderer.FrustumCullingEnabled = cullingEnabled;
        renderer.LodEnabled = lodEnabled;
        renderer.DistanceCullingEnabled = options.DistanceCullingEnabled;

        for (int frame = 0; frame < options.WarmupFrames; frame++)
        {
            if (!RenderWarmupFrame(window, renderer, scene, camera, options.PacingEnabled))
            {
                Console.Error.WriteLine(
                    "The window closed during the benchmark warm-up. Measurements were not collected.");
                return false;
            }
        }

        var frameWindow = new FrameTimingWindow(options.SampleFrames);
        var eventWindow = new FrameTimingWindow(options.SampleFrames);
        var renderWindow = new FrameTimingWindow(options.SampleFrames);
        var pacingWindow = new FrameTimingWindow(options.SampleFrames);

        long allocatedBytes = 0;
        long drawCallsTotal = 0;
        int drawCallsMaximum = 0;
        long visibleInstancesTotal = 0;
        long culledInstancesTotal = 0;
        long distanceCulledInstancesTotal = 0;
        long lodDrawnInstancesTotal = 0;
        long submittedTrianglesTotal = 0;
        long submittedTrianglesMaximum = 0;
        var lodLevelTotals = new long[MeshLod.MaxLevels];
        int testedInstances = 0;
        int skippedFrames = 0;
        int gc0Before = GC.CollectionCount(0);
        int gc1Before = GC.CollectionCount(1);
        int gc2Before = GC.CollectionCount(2);

        for (int frame = 0; frame < options.SampleFrames; frame++)
        {
            long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
            long frameStart = Stopwatch.GetTimestamp();

            if (!window.ProcessEvents(0))
            {
                Console.Error.WriteLine(
                    "The window closed during the measured interval. Measurements are incomplete.");
                return false;
            }

            long eventEnd = Stopwatch.GetTimestamp();
            renderer.Render(scene, camera);
            long renderEnd = Stopwatch.GetTimestamp();

            MaterialRenderer.VisibilityStatistics statistics = renderer.LastFrameStatistics;
            testedInstances = statistics.TotalInstances;

            if (statistics.TotalInstances == 0)
            {
                // A minimized window or an unusable drawable reports no work.
                skippedFrames++;
            }
            else
            {
                drawCallsTotal += statistics.DrawCalls;
                drawCallsMaximum = Math.Max(drawCallsMaximum, statistics.DrawCalls);
                visibleInstancesTotal += statistics.VisibleInstances;
                culledInstancesTotal += statistics.CulledInstances;
                distanceCulledInstancesTotal += statistics.DistanceCulledInstances;
                lodDrawnInstancesTotal += statistics.LodDrawnInstances;
                submittedTrianglesTotal += statistics.SubmittedTriangles;
                submittedTrianglesMaximum = Math.Max(
                    submittedTrianglesMaximum,
                    statistics.SubmittedTriangles);

                for (int level = 0; level < MeshLod.MaxLevels; level++)
                {
                    lodLevelTotals[level] += statistics.LodUsage[level];
                }
            }

            double busyMilliseconds = ElapsedMilliseconds(frameStart, renderEnd);
            long pacingStart = renderEnd;

            if (options.PacingEnabled)
            {
                int delayMilliseconds =
                    TargetFrameMilliseconds - (int)busyMilliseconds;

                if (delayMilliseconds > 0)
                {
                    SDL.Delay((uint)delayMilliseconds);
                }
            }

            long frameEnd = Stopwatch.GetTimestamp();
            long allocationAfter = GC.GetAllocatedBytesForCurrentThread();

            frameWindow.AddSample(ElapsedMilliseconds(frameStart, frameEnd));
            eventWindow.AddSample(ElapsedMilliseconds(frameStart, eventEnd));
            renderWindow.AddSample(ElapsedMilliseconds(eventEnd, renderEnd));

            if (options.PacingEnabled)
            {
                pacingWindow.AddSample(ElapsedMilliseconds(pacingStart, frameEnd));
            }

            allocatedBytes += Math.Max(0, allocationAfter - allocationBefore);
        }

        FrameTimingSummary frameSummary = frameWindow.GetSummary();
        FrameTimingSummary eventSummary = eventWindow.GetSummary();
        FrameTimingSummary renderSummary = renderWindow.GetSummary();
        FrameTimingSummary pacingSummary = pacingWindow.GetSummary();
        double sampleCount = options.SampleFrames;

        Console.WriteLine();
        Console.WriteLine(
            $"mode: culling {(cullingEnabled ? "on " : "off")} | " +
            $"lod {(lodEnabled ? "on " : "off")} | " +
            $"distance {(options.DistanceCullingEnabled ? "on " : "off")} | " +
            $"samples {frameSummary.SampleCount} | skipped {skippedFrames}");

        if (skippedFrames > 0)
        {
            Console.WriteLine(
                "  warning: frames without a drawable are included; totals are not a throughput measure");
        }

        Console.WriteLine(
            $"  total frame: avg {frameSummary.AverageMilliseconds:F2} ms | " +
            $"p95 {frameSummary.P95Milliseconds:F2} ms | " +
            $"max {frameSummary.MaximumMilliseconds:F2} ms | " +
            $"fps {frameSummary.FramesPerSecond:F1}");
        Console.WriteLine(
            $"  events:      avg {eventSummary.AverageMilliseconds:F2} ms | " +
            $"p95 {eventSummary.P95Milliseconds:F2} ms | " +
            $"max {eventSummary.MaximumMilliseconds:F2} ms");
        Console.WriteLine(
            $"  render:      avg {renderSummary.AverageMilliseconds:F2} ms | " +
            $"p95 {renderSummary.P95Milliseconds:F2} ms | " +
            $"max {renderSummary.MaximumMilliseconds:F2} ms");
        Console.WriteLine(
            options.PacingEnabled
                ? $"  pacing:      avg {pacingSummary.AverageMilliseconds:F2} ms | " +
                  $"p95 {pacingSummary.P95Milliseconds:F2} ms"
                : "  pacing:      disabled (swapchain presentation may still throttle)");
        Console.WriteLine(
            $"  instances:   tested {testedInstances} | " +
            $"visible avg {visibleInstancesTotal / sampleCount:F1} | " +
            $"frustum culled avg {culledInstancesTotal / sampleCount:F1} | " +
            $"distance culled avg {distanceCulledInstancesTotal / sampleCount:F1} | " +
            $"lod drawn avg {lodDrawnInstancesTotal / sampleCount:F1}");
        Console.WriteLine(
            $"  draw calls:  avg {drawCallsTotal / sampleCount:F1} | " +
            $"max {drawCallsMaximum}");
        Console.WriteLine(
            $"  triangles:   avg {submittedTrianglesTotal / sampleCount:F1} | " +
            $"max {submittedTrianglesMaximum}");
        Console.WriteLine(
            $"  lod usage:   level 0 avg {lodLevelTotals[0] / sampleCount:F1} | " +
            $"level 1 avg {lodLevelTotals[1] / sampleCount:F1} | " +
            $"level 2 avg {lodLevelTotals[2] / sampleCount:F1}");
        Console.WriteLine(
            $"  managed:     avg {allocatedBytes / sampleCount:F0} bytes/frame | " +
            $"GC +{GC.CollectionCount(0) - gc0Before}/" +
            $"+{GC.CollectionCount(1) - gc1Before}/" +
            $"+{GC.CollectionCount(2) - gc2Before}");

        return true;
    }

    private static bool RenderWarmupFrame(
        Window window,
        MaterialRenderer renderer,
        Scene scene,
        Camera camera,
        bool pacingEnabled)
    {
        long frameStart = Stopwatch.GetTimestamp();

        if (!window.ProcessEvents(0))
        {
            return false;
        }

        renderer.Render(scene, camera);

        if (pacingEnabled)
        {
            int delayMilliseconds =
                TargetFrameMilliseconds - (int)ElapsedMilliseconds(
                    frameStart,
                    Stopwatch.GetTimestamp());

            if (delayMilliseconds > 0)
            {
                SDL.Delay((uint)delayMilliseconds);
            }
        }

        return true;
    }

    private static void PrintHeader(
        BenchmarkOptions options,
        Scene scene,
        int materialCount,
        int highTriangles,
        int mediumTriangles,
        int lowTriangles,
        int logicalWidth,
        int logicalHeight,
        int drawableWidth,
        int drawableHeight)
    {
        Console.WriteLine("Nymeria HelloBenchmark");
        Console.WriteLine(
            $"runtime: {RuntimeInformation.FrameworkDescription} | " +
            $"os: {RuntimeInformation.OSDescription} | " +
            $"arch: {RuntimeInformation.ProcessArchitecture} | " +
            $"logical processors: {Environment.ProcessorCount}");
        Console.WriteLine(
            $"window: {logicalWidth}x{logicalHeight} logical | " +
            $"drawable {drawableWidth}x{drawableHeight}");
        Console.WriteLine("backend: SDL3 GPU, Metal on macOS");
        Console.WriteLine(
            $"scene: {scene.Count} instances | three shared cube variants " +
            $"({highTriangles}/{mediumTriangles}/{lowTriangles} triangles) | " +
            $"{materialCount} shared materials");
        Console.WriteLine(
            options.PacingEnabled
                ? $"pacing: {TargetFrameMilliseconds} ms target delay"
                : "pacing: disabled");
        Console.WriteLine(
            $"warm-up: {options.WarmupFrames} frames | samples: " +
            $"{options.SampleFrames} frames per mode");
        Console.WriteLine(
            "timings are CPU wall-clock durations; render includes culling, " +
            "command recording, submission, and swapchain acquisition, not GPU execution");
        Console.WriteLine(
            "culling comparison runs off before on, with LOD off before on for " +
            "each culling mode; every mode has its own warm-up");
        Console.WriteLine(
            options.DistanceCullingEnabled
                ? "distance culling: configured maximum enforced while LOD is on"
                : "distance culling: disabled (configured maximum ignored)");
    }

    private static bool TryParseOptions(string[] args, out BenchmarkOptions options)
    {
        options = new BenchmarkOptions();

        for (int i = 0; i < args.Length; i++)
        {
            string argument = args[i];

            switch (argument)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    return true;
                case "--no-pacing":
                    options.PacingEnabled = false;
                    break;
                case "--no-distance-culling":
                    options.DistanceCullingEnabled = false;
                    break;
                case "--lod":
                    if (!TryReadValue(args, ref i, argument, out string lod) ||
                        !Enum.TryParse(lod, ignoreCase: true, out LodMode lodMode) ||
                        !Enum.IsDefined(lodMode))
                    {
                        Console.Error.WriteLine(
                            "The --lod value must be on, off, or both.");
                        return false;
                    }

                    options.LodMode = lodMode;
                    break;
                case "--culling":
                    if (!TryReadValue(args, ref i, argument, out string culling))
                    {
                        return false;
                    }

                    if (!Enum.TryParse(culling, ignoreCase: true, out CullingMode mode) ||
                        !Enum.IsDefined(mode))
                    {
                        Console.Error.WriteLine(
                            "The --culling value must be on, off, or both.");
                        return false;
                    }

                    options.CullingMode = mode;
                    break;
                case "--instances":
                    if (!TryReadValue(args, ref i, argument, out string instances) ||
                        !TryParseRange(
                            instances,
                            1,
                            Scene.MaxObjectCount,
                            out int instanceCount))
                    {
                        Console.Error.WriteLine(
                            $"The --instances value must be between 1 and {Scene.MaxObjectCount}.");
                        return false;
                    }

                    options.Instances = instanceCount;
                    break;
                case "--warmup":
                    if (!TryReadValue(args, ref i, argument, out string warmup) ||
                        !TryParseRange(warmup, 0, MaximumFrames, out int warmupFrames))
                    {
                        Console.Error.WriteLine(
                            $"The --warmup value must be between 0 and {MaximumFrames}.");
                        return false;
                    }

                    options.WarmupFrames = warmupFrames;
                    break;
                case "--frames":
                    if (!TryReadValue(args, ref i, argument, out string frames) ||
                        !TryParseRange(frames, 1, MaximumFrames, out int sampleFrames))
                    {
                        Console.Error.WriteLine(
                            $"The --frames value must be between 1 and {MaximumFrames}.");
                        return false;
                    }

                    options.SampleFrames = sampleFrames;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {argument}");
                    PrintUsage();
                    return false;
            }
        }

        return true;
    }

    private static bool TryReadValue(
        string[] args,
        ref int index,
        string argument,
        out string value)
    {
        if (index + 1 >= args.Length)
        {
            Console.Error.WriteLine($"Missing value after {argument}.");
            PrintUsage();
            value = string.Empty;
            return false;
        }

        index++;
        value = args[index];
        return true;
    }

    private static bool TryParseRange(
        string value,
        int minimum,
        int maximum,
        out int result)
    {
        return int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out result) &&
            result >= minimum &&
            result <= maximum;
    }

    private static double ElapsedMilliseconds(long startTimestamp, long endTimestamp)
    {
        return (endTimestamp - startTimestamp) * 1000.0 / Stopwatch.Frequency;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: HelloBenchmark [options]\n" +
            "  --instances <count>   Scene instances (1..100000, default 2048).\n" +
            "  --culling <mode>      on, off, or both (default both).\n" +
            "  --lod <mode>          on, off, or both (default off).\n" +
            "  --no-distance-culling Disable the configured maximum render distance.\n" +
            "  --warmup <frames>     Frames excluded before measuring (default 120).\n" +
            "  --frames <count>      Measured frames per mode (default 600).\n" +
            "  --no-pacing           Disable the 16 ms frame-budget delay.\n" +
            "  -h, --help            Show this help.");
    }

    private sealed class BenchmarkOptions
    {
        public int Instances { get; set; } = DefaultInstances;

        public CullingMode CullingMode { get; set; } = CullingMode.Both;

        public LodMode LodMode { get; set; } = LodMode.Off;

        public int WarmupFrames { get; set; } = DefaultWarmupFrames;

        public int SampleFrames { get; set; } = DefaultSampleFrames;

        public bool PacingEnabled { get; set; } = true;

        public bool DistanceCullingEnabled { get; set; } = true;

        public bool ShowHelp { get; set; }
    }

    private enum CullingMode
    {
        Off = 0,
        On = 1,
        Both = 2,
    }

    private enum LodMode
    {
        Off = 0,
        On = 1,
        Both = 2,
    }
}
