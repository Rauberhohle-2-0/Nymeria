using System.Diagnostics;
using System.Numerics;
using Nymeria.Core.Animation;
using Nymeria.Examples;
using Nymeria.Graphics;
using Nymeria.Platform;
using SDL3;

namespace HelloAnimation;

internal static class Program
{
    private const int TargetFrameMilliseconds = 16;
    private const double MaxFrameDeltaSeconds = 0.1;
    private const double StatusIntervalSeconds = 1.0;

    private static int Main(string[] args)
    {
        foreach (string argument in args)
        {
            switch (argument)
            {
                case "-h":
                case "--help":
                    PrintUsage();
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown argument: {argument}");
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
                "`dotnet run --project examples/HelloAnimation/HelloAnimation.csproj`.");
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
            Console.Error.WriteLine($"Invalid animation configuration: {exception.Message}");
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
        var fixture = new AnimationFixture();
        var camera = new Camera(new Vector3(0.0f, 3.6f, 8.0f))
        {
            PitchRadians = -0.28f,
            MoveSpeed = 7.0f,
            FarPlane = 200.0f,
        };

        using Window window = Window.Create(
            new WindowOptions("Nymeria - HelloAnimation"));
        using MaterialRenderer renderer = MaterialRenderer.Create(window.Handle);
        using var controls = new FlyCameraControls(window.Handle, camera);

        Console.WriteLine(
            "HelloAnimation: three independent transform clips on one flat scene. " +
            "The door hinge and wiper base are the meshes' local origins, so the " +
            "object transform itself is the pivot; no parent objects are involved.");
        Console.WriteLine(
            "P pauses or resumes all animations, R restarts them, L toggles looping, " +
            "and - / = change playback speed. Shared camera controls: WASD, " +
            "Space/Ctrl, Shift, and held right mouse button.");
        Console.WriteLine(
            "Diagnostics print once per second: per-player state and time, the " +
            "door and wiper angles, the mover scale, speed, looping, and frame rate.");
        Console.WriteLine();

        var clock = Stopwatch.StartNew();
        double previousSeconds = clock.Elapsed.TotalSeconds;
        double previousStatusSeconds = previousSeconds;
        double nextStatusSeconds = previousSeconds;
        bool wasPauseKeyDown = false;
        bool wasRestartKeyDown = false;
        bool wasLoopKeyDown = false;
        bool wasSlowerKeyDown = false;
        bool wasFasterKeyDown = false;
        int statusFrames = 0;

        PrintStatus(
            fixture,
            renderer,
            window,
            clock,
            statusFrames: 0,
            previousStatusSeconds);

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

            if (Pressed(keys, SDL.Scancode.P, ref wasPauseKeyDown))
            {
                bool paused = fixture.TogglePause();
                Console.WriteLine(paused ? "Animations paused." : "Animations resumed.");
            }

            if (Pressed(keys, SDL.Scancode.R, ref wasRestartKeyDown))
            {
                fixture.RestartAll();
                Console.WriteLine("Animations restarted at time zero.");
            }

            if (Pressed(keys, SDL.Scancode.L, ref wasLoopKeyDown))
            {
                bool looping = fixture.ToggleLooping();
                Console.WriteLine($"Looping {(looping ? "enabled" : "disabled")}.");
            }

            if (Pressed(keys, SDL.Scancode.Minus, ref wasSlowerKeyDown))
            {
                Console.WriteLine(
                    $"Playback speed x{fixture.AdjustSpeed(-0.25f):0.00}.");
            }

            if (Pressed(keys, SDL.Scancode.Equals, ref wasFasterKeyDown))
            {
                Console.WriteLine(
                    $"Playback speed x{fixture.AdjustSpeed(0.25f):0.00}.");
            }

            fixture.Update(deltaSeconds);
            renderer.Render(fixture.Scene, camera);
            statusFrames++;

            if (nowSeconds >= nextStatusSeconds)
            {
                PrintStatus(
                    fixture,
                    renderer,
                    window,
                    clock,
                    statusFrames,
                    previousStatusSeconds);
                statusFrames = 0;
                previousStatusSeconds = nowSeconds;
                nextStatusSeconds = nowSeconds + StatusIntervalSeconds;
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

    private static void PrintStatus(
        AnimationFixture fixture,
        MaterialRenderer renderer,
        Window window,
        Stopwatch clock,
        int statusFrames,
        double previousStatusSeconds)
    {
        double nowSeconds = clock.Elapsed.TotalSeconds;
        double elapsed = nowSeconds - previousStatusSeconds;
        double framesPerSecond = elapsed > 0.0 ? statusFrames / elapsed : 0.0;

        Console.WriteLine(
            $"animation | speed x{fixture.SpeedMultiplier:0.00} | " +
            $"loop {(fixture.IsLooping ? "on " : "off")} | " +
            $"{(fixture.IsPaused ? "paused " : "playing")} | " +
            $"door {Describe(fixture.Door)} {fixture.DoorAngleDegrees,6:F1} deg | " +
            $"wiper {Describe(fixture.Wiper)} {fixture.WiperAngleDegrees,6:F1} deg | " +
            $"mover {Describe(fixture.Mover)} " +
            $"scale {fixture.Mover.TargetTransform.Scale.X:0.00} | " +
            $"fps {framesPerSecond,5:F1} | " +
            $"visible {renderer.LastFrameStatistics.VisibleInstances,2}");

        SDL.SetWindowTitle(
            window.Handle,
            $"Nymeria - HelloAnimation | door {fixture.DoorAngleDegrees:0} deg | " +
            $"wiper {fixture.WiperAngleDegrees:0} deg | " +
            $"mover {fixture.Mover.TargetTransform.Scale.X:0.00}x | " +
            $"{(fixture.IsPaused ? "paused" : "playing")}");
    }

    private static string Describe(AnimationPlayer player)
    {
        string state = player.State switch
        {
            AnimationPlaybackState.Playing => "play",
            AnimationPlaybackState.Paused => "pause",
            AnimationPlaybackState.Stopped => "stop",
            AnimationPlaybackState.Completed => "done",
            _ => "?",
        };

        return $"{state} {player.Time:0.00}/{player.Clip.DurationSeconds:0.00}s";
    }

    private static bool Pressed(
        ReadOnlySpan<bool> keys,
        SDL.Scancode scancode,
        ref bool wasDown)
    {
        bool isDown = IsKeyDown(keys, scancode);
        bool pressed = isDown && !wasDown;
        wasDown = isDown;
        return pressed;
    }

    private static bool IsKeyDown(
        ReadOnlySpan<bool> keys,
        SDL.Scancode scancode)
    {
        int index = (int)scancode;
        return index < keys.Length && keys[index];
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: HelloAnimation [-h|--help]\n" +
            "  P pauses or resumes all animations; R restarts them at time zero;\n" +
            "  L toggles looping; - and = change playback speed.\n" +
            "  WASD, Space/Ctrl, Shift, and held right mouse button move the camera.");
    }
}
