using System.Diagnostics;
using System.Numerics;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;
using Nymeria.Examples;
using Nymeria.Graphics;
using Nymeria.Platform;
using SDL3;

namespace HelloScene;

internal static class Program
{
    private const int TargetFrameMilliseconds = 16;
    private const double MaxFrameDeltaSeconds = 0.1;

    private static int Main()
    {
        try
        {
            MeshData sharedMesh = SceneFixtures.CreateCubeMesh();
            var scene = new Scene();
            AddInitialObjects(scene, sharedMesh);
            int addedObjectCount = 0;

            var camera = new Camera(new Vector3(0.0f, 1.5f, 9.0f))
            {
                MoveSpeed = 4.0f,
            };

            using Window window = Window.Create(new WindowOptions());
            using SceneRenderer renderer = SceneRenderer.Create(window.Handle);
            using var controls = new FlyCameraControls(window.Handle, camera);

            Console.WriteLine(
                "HelloScene: E adds an object, Q removes the last object, " +
                "R resets the scene. Use the shared free-camera controls to move " +
                "and look.");

            var clock = Stopwatch.StartNew();
            double previousSeconds = clock.Elapsed.TotalSeconds;
            bool wasAddDown = false;
            bool wasRemoveDown = false;
            bool wasResetDown = false;

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
                UpdateScene(
                    scene,
                    sharedMesh,
                    ref addedObjectCount,
                    ref wasAddDown,
                    ref wasRemoveDown,
                    ref wasResetDown);

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
        }
        catch (DllNotFoundException exception)
        {
            Console.Error.WriteLine("Nymeria could not load the native SDL3 library.");
            Console.Error.WriteLine(
                "Run `dotnet restore Nymeria.slnx`, then start the example with " +
                "`dotnet run --project examples/HelloScene/HelloScene.csproj`.");
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

    private static void UpdateScene(
        Scene scene,
        MeshData sharedMesh,
        ref int addedObjectCount,
        ref bool wasAddDown,
        ref bool wasRemoveDown,
        ref bool wasResetDown)
    {
        ReadOnlySpan<bool> keys = SDL.GetKeyboardState(out _);
        bool isAddDown = IsKeyDown(keys, SDL.Scancode.E);
        bool isRemoveDown = IsKeyDown(keys, SDL.Scancode.Q);
        bool isResetDown = IsKeyDown(keys, SDL.Scancode.R);

        if (isAddDown && !wasAddDown)
        {
            AddRuntimeObject(scene, sharedMesh, addedObjectCount);
            addedObjectCount++;
        }

        if (isRemoveDown && !wasRemoveDown && scene.Count > 0)
        {
            scene.Remove(scene.Objects[^1]);
        }

        if (isResetDown && !wasResetDown)
        {
            scene.Clear();
            AddInitialObjects(scene, sharedMesh);
            addedObjectCount = 0;
        }

        wasAddDown = isAddDown;
        wasRemoveDown = isRemoveDown;
        wasResetDown = isResetDown;
    }

    private static void AddInitialObjects(Scene scene, MeshData sharedMesh)
    {
        // Every object references the same mesh. The renderer uploads it once
        // and reuses that GPU geometry for all four instances.
        scene.Add(CreateObject(
            sharedMesh,
            new Vector3(-3.2f, 0.0f, -1.5f),
            new Vector3(1.0f, 1.0f, 1.0f),
            yawRadians: 0.4f,
            pitchRadians: 0.15f));
        scene.Add(CreateObject(
            sharedMesh,
            new Vector3(0.0f, 0.0f, 0.0f),
            new Vector3(1.25f, 1.25f, 1.25f),
            yawRadians: -0.5f,
            pitchRadians: -0.1f,
            rollRadians: 0.2f));
        scene.Add(CreateObject(
            sharedMesh,
            new Vector3(3.2f, 0.0f, -1.5f),
            new Vector3(0.75f, 0.75f, 0.75f),
            yawRadians: 0.8f,
            pitchRadians: 0.3f));
        scene.Add(CreateObject(
            sharedMesh,
            new Vector3(0.0f, 2.8f, -4.5f),
            new Vector3(1.5f, 0.6f, 0.9f),
            yawRadians: 0.2f,
            pitchRadians: -0.4f));
    }

    private static void AddRuntimeObject(Scene scene, MeshData sharedMesh, int index)
    {
        float angle = index * 0.65f;
        float radius = 2.5f + (index % 3) * 0.7f;
        var position = new Vector3(
            MathF.Cos(angle) * radius,
            0.5f + (index % 2) * 1.2f,
            -2.5f + (MathF.Sin(angle) * radius));
        float scale = 0.55f + (index % 4) * 0.1f;

        scene.Add(CreateObject(
            sharedMesh,
            position,
            new Vector3(scale, scale * 0.8f, scale),
            yawRadians: angle,
            pitchRadians: 0.15f + (index % 3) * 0.1f));
    }

    private static SceneObject CreateObject(
        MeshData mesh,
        Vector3 position,
        Vector3 scale,
        float yawRadians,
        float pitchRadians = 0.0f,
        float rollRadians = 0.0f)
    {
        var transform = new Transform(
            position,
            Quaternion.CreateFromYawPitchRoll(yawRadians, pitchRadians, rollRadians),
            scale);

        return new SceneObject(mesh, transform);
    }

    private static bool IsKeyDown(
        ReadOnlySpan<bool> keys,
        SDL.Scancode scancode)
    {
        int index = (int)scancode;
        return index < keys.Length && keys[index];
    }
}
