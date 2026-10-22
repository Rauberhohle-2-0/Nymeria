using System.Diagnostics;
using System.Numerics;
using Nymeria.Examples;
using Nymeria.Graphics;
using Nymeria.Platform;
using SDL3;

namespace HelloAudio;

internal static class Program
{
    private const int TargetFrameMilliseconds = 16;
    private const double MaxFrameDeltaSeconds = 0.1;
    private const double StatusIntervalSeconds = 1.0;

    private static readonly Vector3 StartPosition = new(0.0f, 2.0f, 8.0f);
    private const float StartPitch = -0.08f;

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
                "`dotnet run --project examples/HelloAudio/HelloAudio.csproj`.");
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

    private static int Run()
    {
        var camera = new Camera(StartPosition)
        {
            PitchRadians = StartPitch,
            MoveSpeed = 6.0f,
            FarPlane = 200.0f,
        };

        using Window window = Window.Create(
            new WindowOptions("Nymeria - HelloAudio"));
        using MaterialRenderer renderer = MaterialRenderer.Create(window.Handle);
        using var controls = new FlyCameraControls(window.Handle, camera);

        var scene = new AudioDemoScene();
        AudioDemo? demo = AudioDemo.TryCreate(out string audioMessage);

        Console.WriteLine(audioMessage);
        PrintControls();

        // The listener belongs to the audio system; copy the initial camera state
        // before reporting so the first status line is accurate.
        demo?.Update(camera, 0.0f);
        PrintStatus(demo, camera, audioMessage);

        try
        {
            return RunLoop(window, renderer, camera, controls, scene, demo, audioMessage);
        }
        finally
        {
            demo?.Dispose();
        }
    }

    private static int RunLoop(
        Window window,
        MaterialRenderer renderer,
        Camera camera,
        FlyCameraControls controls,
        AudioDemoScene scene,
        AudioDemo? demo,
        string audioMessage)
    {
        var clock = Stopwatch.StartNew();
        double previousSeconds = clock.Elapsed.TotalSeconds;
        double nextStatusSeconds = previousSeconds;
        bool wasAmbientKeyDown = false;
        bool wasSourceAPauseKeyDown = false;
        bool wasSourceAStopKeyDown = false;
        bool wasMasterDownKeyDown = false;
        bool wasMasterUpKeyDown = false;
        bool wasSourceADownKeyDown = false;
        bool wasSourceAUpKeyDown = false;
        bool wasStereoDownKeyDown = false;
        bool wasStereoUpKeyDown = false;
        bool wasStereoPresetKeyDown = false;
        bool wasSweepKeyDown = false;
        bool wasResetKeyDown = false;
        bool reportedDeviceLoss = false;

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
            demo?.Update(camera, deltaSeconds);

            if (demo is not null)
            {
                scene.SetSourceAPosition(demo.SourceAPosition);
            }

            ReadOnlySpan<bool> keys = SDL.GetKeyboardState(out _);

            if (Pressed(keys, SDL.Scancode.P, ref wasAmbientKeyDown))
            {
                demo?.ToggleAmbientPause();
            }

            if (Pressed(keys, SDL.Scancode.O, ref wasSourceAPauseKeyDown))
            {
                demo?.ToggleSourceAPause();
            }

            if (Pressed(keys, SDL.Scancode.X, ref wasSourceAStopKeyDown))
            {
                demo?.ToggleSourceA();
            }

            if (Pressed(keys, SDL.Scancode.Leftbracket, ref wasMasterDownKeyDown))
            {
                demo?.AdjustMasterGain(-0.1f);
            }

            if (Pressed(keys, SDL.Scancode.Rightbracket, ref wasMasterUpKeyDown))
            {
                demo?.AdjustMasterGain(0.1f);
            }

            if (Pressed(keys, SDL.Scancode.Minus, ref wasSourceADownKeyDown))
            {
                demo?.AdjustSourceAGain(-0.1f);
            }

            if (Pressed(keys, SDL.Scancode.Equals, ref wasSourceAUpKeyDown))
            {
                demo?.AdjustSourceAGain(0.1f);
            }

            if (Pressed(keys, SDL.Scancode.Comma, ref wasStereoDownKeyDown))
            {
                demo?.AdjustStereoEffectStrength(-0.1f);
            }

            if (Pressed(keys, SDL.Scancode.Period, ref wasStereoUpKeyDown))
            {
                demo?.AdjustStereoEffectStrength(0.1f);
            }

            if (Pressed(keys, SDL.Scancode.T, ref wasStereoPresetKeyDown))
            {
                demo?.CycleStereoEffectPreset();
            }

            if (Pressed(keys, SDL.Scancode.S, ref wasSweepKeyDown))
            {
                demo?.ToggleSweep();
            }

            if (Pressed(keys, SDL.Scancode.R, ref wasResetKeyDown))
            {
                camera.Position = StartPosition;
                camera.YawRadians = 0.0f;
                camera.PitchRadians = StartPitch;
            }

            if (demo is not null && demo.IsDeviceLost && !reportedDeviceLoss)
            {
                Console.WriteLine(
                    "The audio device was lost; voices were stopped and new " +
                    "playback is refused. Restart the example to reopen a device.");
                reportedDeviceLoss = true;
            }

            if (nowSeconds >= nextStatusSeconds)
            {
                PrintStatus(demo, camera, audioMessage);
                nextStatusSeconds = nowSeconds + StatusIntervalSeconds;
            }

            renderer.Render(scene.Scene, camera);

            double frameMilliseconds =
                Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds;

            if (frameMilliseconds < TargetFrameMilliseconds)
            {
                Thread.Sleep((int)(TargetFrameMilliseconds - frameMilliseconds));
            }
        }

        return 0;
    }

    private static void PrintStatus(
        AudioDemo? demo,
        Camera camera,
        string audioMessage)
    {
        Console.WriteLine(
            $"listener: pos ({camera.Position.X:0.0}, {camera.Position.Y:0.0}, " +
            $"{camera.Position.Z:0.0}), forward ({camera.Forward.X:0.00}, " +
            $"{camera.Forward.Y:0.00}, {camera.Forward.Z:0.00})");

        Console.WriteLine(demo is null ? audioMessage : demo.BuildStatus());
        Console.WriteLine();
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

    private static void PrintControls()
    {
        Console.WriteLine(
            "Controls: hold the right mouse button and use WASD, Space/Ctrl, " +
            "and Shift to move the listener.");
        Console.WriteLine(
            "P pauses/resumes the 2D ambient loop. O pauses/resumes spatial " +
            "source A. X stops source A and starts a new voice on the same clip.");
        Console.WriteLine(
            "[ / ] change master gain. - / = change source A gain. R resets the " +
            "listener. Walk between and around the two markers to hear distance " +
            "attenuation and left/right panning.");
        Console.WriteLine(
            ", / . change the positional stereo effect strength by 10%. T cycles " +
            "it through 0%, 50%, and 100%. S starts or stops moving source A left " +
            "to right around the listener at a fixed distance; compare 0%, 50%, " +
            "and 100% while it sweeps.");
        Console.WriteLine(
            "Spatialization is basic: linear distance attenuation and stereo " +
            "panning, not HRTF, occlusion, reverb, or surround sound.");
        Console.WriteLine();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("HelloAudio - Nymeria audio and spatial-sound example.");
        Console.WriteLine(
            "Run: dotnet run --project examples/HelloAudio/HelloAudio.csproj");
        Console.WriteLine(
            "The example generates its own test audio and needs no asset files.");
        Console.WriteLine(
            "If no audio device is available it keeps rendering and reports " +
            "the failure instead of exiting.");
    }
}
