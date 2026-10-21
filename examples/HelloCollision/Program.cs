using System.Diagnostics;
using System.Numerics;
using Nymeria.Core.Collision;
using Nymeria.Core.Scenes;
using Nymeria.Examples;
using Nymeria.Graphics;
using Nymeria.Platform;
using SDL3;

namespace HelloCollision;

internal static class Program
{
    private const int TargetFrameMilliseconds = 16;
    private const double MaxFrameDeltaSeconds = 0.1;
    private const double StatisticsIntervalSeconds = 1.0;

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
                "`dotnet run --project examples/HelloCollision/HelloCollision.csproj`.");
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
            Console.Error.WriteLine($"Invalid collision configuration: {exception.Message}");
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
        var scene = new Scene();
        var fixture = new CollisionFixture(scene);
        var camera = new Camera(new Vector3(0.0f, 10.0f, 20.0f))
        {
            PitchRadians = -0.25f,
            MoveSpeed = 12.0f,
            FarPlane = 200.0f,
        };

        using Window window = Window.Create(
            new WindowOptions("Nymeria - HelloCollision"));
        using MaterialRenderer renderer = MaterialRenderer.Create(window.Handle);
        using var controls = new FlyCameraControls(window.Handle, camera);

        var pairs = new List<CollisionPair>();
        var bodies = new List<CollisionBody>(fixture.AnimatedBodies)
        {
            new(fixture.Player, fixture.Player.Material, "player"),
        };

        // Add the static bodies too, so every object's base material is restored
        // after the previous frame's contact highlight.
        foreach (SceneObject sceneObject in scene.Objects)
        {
            if (!bodies.Exists(body => ReferenceEquals(body.SceneObject, sceneObject)))
            {
                bodies.Add(new CollisionBody(
                    sceneObject,
                    sceneObject.Material,
                    "static body"));
            }
        }

        Console.WriteLine(
            $"HelloCollision: {scene.Count} bodies with {CountColliders(scene)} active " +
            $"colliders. Boxes use local AABB colliders and spheres use local sphere " +
            $"colliders; render meshes are separate.");
        Console.WriteLine(
            "Arrow keys move the cyan player box. P pauses or resumes animation, R " +
            "resets the player and animation, and the highlighted bodies are the " +
            "current overlapping pairs. Shared camera controls: WASD, Space/Ctrl, " +
            "Shift, and held right mouse button.");
        Console.WriteLine(
            "Diagnostics print once per second: active colliders, tested unordered " +
            "pairs, overlapping pairs, player contact, and collision-query time. " +
            "This is detection only; overlap does not move or resolve bodies.");

        var clock = Stopwatch.StartNew();
        double previousSeconds = clock.Elapsed.TotalSeconds;
        double previousStatisticsSeconds = previousSeconds;
        double animationSeconds = 0.0;
        bool animationPaused = false;
        bool wasPauseKeyDown = false;
        bool wasResetKeyDown = false;
        int statisticsFrames = 0;
        int lastPairCount = 0;
        bool lastPlayerContact = false;
        double lastQueryMilliseconds = 0.0;

        fixture.UpdateAnimation(animationSeconds);
        UpdateTitle(window, animationPaused, lastPairCount, lastPlayerContact);

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
            bool pauseKeyDown = IsKeyDown(keys, SDL.Scancode.P);
            bool resetKeyDown = IsKeyDown(keys, SDL.Scancode.R);

            if (pauseKeyDown && !wasPauseKeyDown)
            {
                animationPaused = !animationPaused;
                Console.WriteLine(
                    $"Animation {(animationPaused ? "paused" : "resumed")}.");
            }

            if (resetKeyDown && !wasResetKeyDown)
            {
                animationSeconds = 0.0;
                fixture.ResetPlayer();
                fixture.UpdateAnimation(animationSeconds);
                Console.WriteLine("Player and animation reset.");
            }

            wasPauseKeyDown = pauseKeyDown;
            wasResetKeyDown = resetKeyDown;

            float moveX = Axis(
                IsKeyDown(keys, SDL.Scancode.Right),
                IsKeyDown(keys, SDL.Scancode.Left));
            float moveZ = Axis(
                IsKeyDown(keys, SDL.Scancode.Down),
                IsKeyDown(keys, SDL.Scancode.Up));
            var moveDirection = new Vector3(moveX, 0.0f, moveZ);

            if (moveDirection.LengthSquared() > 0.0f)
            {
                fixture.MovePlayer(Vector3.Normalize(moveDirection), deltaSeconds);
            }

            if (!animationPaused)
            {
                animationSeconds += deltaSeconds;
                fixture.UpdateAnimation(animationSeconds);
            }

            // Restore every base material before applying this frame's contact
            // highlight. The materials are shared references owned by the fixture.
            foreach (CollisionBody body in bodies)
            {
                body.SceneObject.Material = body.BaseMaterial;
            }

            long queryStart = Stopwatch.GetTimestamp();
            pairs.Clear();
            lastPairCount = SceneCollision.AppendOverlapPairs(scene, pairs);
            lastPlayerContact = SceneCollision.Overlaps(scene, fixture.Player);
            long queryEnd = Stopwatch.GetTimestamp();
            lastQueryMilliseconds = ElapsedMilliseconds(queryStart, queryEnd);

            foreach (CollisionPair pair in pairs)
            {
                pair.First.Material = fixture.HighlightMaterial;
                pair.Second.Material = fixture.HighlightMaterial;
            }

            renderer.Render(scene, camera);
            statisticsFrames++;

            double statisticsSeconds = nowSeconds - previousStatisticsSeconds;

            if (statisticsSeconds >= StatisticsIntervalSeconds)
            {
                int colliders = CountColliders(scene);
                long testedPairs = (long)colliders * (colliders - 1) / 2;
                double framesPerSecond = statisticsFrames / statisticsSeconds;

                Console.WriteLine(
                    $"collision | colliders {colliders,2} | " +
                    $"tested pairs {testedPairs,3} | " +
                    $"overlapping pairs {lastPairCount,2} | " +
                    $"player {(lastPlayerContact ? "contact" : "clear")} | " +
                    $"animation {(animationPaused ? "paused" : "on    ")} | " +
                    $"query {lastQueryMilliseconds:F3} ms | " +
                    $"fps {framesPerSecond,5:F1} | " +
                    $"rendered {renderer.LastFrameStatistics.VisibleInstances,2}");

                UpdateTitle(window, animationPaused, lastPairCount, lastPlayerContact);
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

    private static int CountColliders(Scene scene)
    {
        int colliders = 0;

        foreach (SceneObject sceneObject in scene.Objects)
        {
            if (sceneObject.Collider is not null)
            {
                colliders++;
            }
        }

        return colliders;
    }

    private static void UpdateTitle(
        Window window,
        bool animationPaused,
        int pairCount,
        bool playerContact)
    {
        SDL.SetWindowTitle(
            window.Handle,
            $"Nymeria - HelloCollision | {pairCount} overlapping pairs | " +
            $"player {(playerContact ? "contact" : "clear")} | " +
            $"animation {(animationPaused ? "paused" : "on")}");
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Usage: HelloCollision [-h|--help]\n" +
            "  Arrow keys move the player box; P pauses animation; R resets.\n" +
            "  WASD, Space/Ctrl, Shift, and held right mouse button move the camera.");
    }

    private static double ElapsedMilliseconds(long start, long end)
    {
        return (end - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static float Axis(bool positive, bool negative)
    {
        return (positive ? 1.0f : 0.0f) - (negative ? 1.0f : 0.0f);
    }

    private static bool IsKeyDown(
        ReadOnlySpan<bool> keys,
        SDL.Scancode scancode)
    {
        int index = (int)scancode;
        return index < keys.Length && keys[index];
    }
}
