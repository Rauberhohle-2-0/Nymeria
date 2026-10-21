using System.Diagnostics;
using System.Numerics;
using Nymeria.Core.Lighting;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;
using Nymeria.Examples;
using Nymeria.Formats.Obj;
using Nymeria.Formats.Textures;
using Nymeria.Graphics;
using Nymeria.Platform;
using SDL3;

namespace HelloLighting;

internal static class Program
{
    private const int TargetFrameMilliseconds = 16;
    private const double MaxFrameDeltaSeconds = 0.1;
    private const string DefaultModelPath = "Assets/cube.obj";
    private const string TexturePath = "Assets/checker.png";

    private const float InitialLightYaw = 0.55f;
    private const float InitialLightElevation = 0.75f;
    private const float InitialLightIntensity = 1.1f;
    private const float AmbientIntensity = 0.18f;
    private const float MaxEditableLightIntensity = 4.0f;

    private static int Main(string[] args)
    {
        if (args.Length > 1)
        {
            PrintUsage();
            return 1;
        }

        if (args.Length == 1 && (args[0] == "-h" || args[0] == "--help"))
        {
            PrintUsage();
            return 0;
        }

        string modelPath = args.Length == 1
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, DefaultModelPath);
        string texturePath = Path.Combine(AppContext.BaseDirectory, TexturePath);

        ModelData model;
        Texture texture;

        try
        {
            model = LoadModel(modelPath);
            texture = LoadTexture(texturePath);
        }
        catch (ModelFormatException exception)
        {
            Console.Error.WriteLine($"Could not load OBJ '{modelPath}': {exception.Message}");
            return 1;
        }
        catch (FormatException exception)
        {
            Console.Error.WriteLine($"Could not decode texture '{texturePath}': {exception.Message}");
            return 1;
        }
        catch (FileNotFoundException)
        {
            Console.Error.WriteLine($"Required asset was not found: {modelPath} or {texturePath}");
            return 1;
        }
        catch (DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"An asset directory was not found: {modelPath} or {texturePath}");
            return 1;
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine($"Cannot read an asset: {exception.Message}");
            return 1;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"Could not read an asset: {exception.Message}");
            return 1;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"Invalid asset path: {exception.Message}");
            return 1;
        }

        // The example draws one mesh. Imported models with several meshes use
        // the first; the format parsers keep the rest available to a later
        // renderer without this example pretending to draw them.
        MeshData importedMesh = model.Meshes[0];

        if (importedMesh.Normals.IsEmpty)
        {
            Console.Error.WriteLine(
                $"The mesh in '{modelPath}' has no vertex normals. HelloLighting " +
                "requires normals for diffuse lighting; export the OBJ with 'vn' data.");
            return 1;
        }

        try
        {
            return Run(importedMesh, texture);
        }
        catch (DllNotFoundException exception)
        {
            Console.Error.WriteLine("Nymeria could not load the native SDL3 library.");
            Console.Error.WriteLine(
                "Run `dotnet restore Nymeria.slnx`, then start the example with " +
                "`dotnet run --project examples/HelloLighting/HelloLighting.csproj`.");
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

    private static int Run(MeshData importedMesh, Texture texture)
    {
        var ambient = new AmbientLight(new Vector3(0.55f, 0.65f, 0.90f), AmbientIntensity);
        float lightYaw = InitialLightYaw;
        float lightElevation = InitialLightElevation;
        float lightIntensity = InitialLightIntensity;
        bool directionalEnabled = true;
        bool wasToggleDown = false;
        bool wasLowerDown = false;
        bool wasHigherDown = false;
        bool wasResetDown = false;

        var texturedMaterial = new Material(Vector3.One, texture, "checkerboard");
        var solidMaterial = new Material(
            new Vector3(0.90f, 0.42f, 0.16f),
            null,
            "solid orange");

        var scene = new Scene();
        scene.Add(CreateObject(
            importedMesh,
            texturedMaterial,
            new Vector3(-2.4f, 0.0f, -1.0f),
            new Vector3(1.1f, 1.1f, 1.1f),
            yawRadians: 0.35f,
            pitchRadians: 0.15f));

        // The middle object uses a strongly non-uniform scale, so an incorrect
        // normal transform would make its lighting visibly wrong.
        scene.Add(CreateObject(
            importedMesh,
            texturedMaterial,
            new Vector3(0.0f, 0.0f, 0.0f),
            new Vector3(1.40f, 0.70f, 1.05f),
            yawRadians: -0.55f,
            pitchRadians: -0.10f,
            rollRadians: 0.20f));

        scene.Add(CreateObject(
            importedMesh,
            solidMaterial,
            new Vector3(2.6f, 0.0f, -1.2f),
            new Vector3(0.9f, 0.9f, 0.9f),
            yawRadians: 0.80f,
            pitchRadians: 0.30f));

        ApplyLighting(
            scene,
            ambient,
            lightYaw,
            lightElevation,
            lightIntensity,
            directionalEnabled);

        var camera = new Camera(new Vector3(0.0f, 1.6f, 9.5f))
        {
            MoveSpeed = 4.0f,
        };

        using Window window = Window.Create(new WindowOptions());
        using MaterialRenderer renderer = MaterialRenderer.Create(window.Handle);
        using var controls = new FlyCameraControls(window.Handle, camera);

        Console.WriteLine(
            "HelloLighting: three instances of the imported cube, two textured " +
            "and one solid, lit by ambient light and one directional light.");
        Console.WriteLine(
            "L toggles ambient-only mode, arrow keys move the light, [ and ] " +
            "change directional intensity, and R resets the light. Shared camera " +
            "controls: WASD, Space/Ctrl, Shift, and held right mouse button.");

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
            // state is current for this frame.
            controls.Update(deltaSeconds);

            if (UpdateLightState(
                    deltaSeconds,
                    ref lightYaw,
                    ref lightElevation,
                    ref lightIntensity,
                    ref directionalEnabled,
                    ref wasToggleDown,
                    ref wasLowerDown,
                    ref wasHigherDown,
                    ref wasResetDown))
            {
                ApplyLighting(
                    scene,
                    ambient,
                    lightYaw,
                    lightElevation,
                    lightIntensity,
                    directionalEnabled);
            }

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

    private static void ApplyLighting(
        Scene scene,
        AmbientLight ambient,
        float yawRadians,
        float elevationRadians,
        float intensity,
        bool directionalEnabled)
    {
        DirectionalLight? directional = null;

        if (directionalEnabled)
        {
            // Build the direction toward the light from yaw and elevation, then
            // negate it because DirectionalLight stores the travel direction.
            float horizontal = MathF.Cos(elevationRadians);
            var directionToLight = new Vector3(
                horizontal * MathF.Sin(yawRadians),
                MathF.Sin(elevationRadians),
                horizontal * MathF.Cos(yawRadians));

            directional = new DirectionalLight(
                -directionToLight,
                Vector3.One,
                intensity);
        }

        scene.Lighting = new SceneLighting(ambient, directional);
    }

    private static bool UpdateLightState(
        float deltaSeconds,
        ref float yawRadians,
        ref float elevationRadians,
        ref float intensity,
        ref bool directionalEnabled,
        ref bool wasToggleDown,
        ref bool wasLowerDown,
        ref bool wasHigherDown,
        ref bool wasResetDown)
    {
        ReadOnlySpan<bool> keys = SDL.GetKeyboardState(out _);
        float yawDelta = 0.0f;
        float elevationDelta = 0.0f;

        if (IsKeyDown(keys, SDL.Scancode.Left))
        {
            yawDelta -= 1.2f * deltaSeconds;
        }

        if (IsKeyDown(keys, SDL.Scancode.Right))
        {
            yawDelta += 1.2f * deltaSeconds;
        }

        if (IsKeyDown(keys, SDL.Scancode.Up))
        {
            elevationDelta += 0.8f * deltaSeconds;
        }

        if (IsKeyDown(keys, SDL.Scancode.Down))
        {
            elevationDelta -= 0.8f * deltaSeconds;
        }

        bool changed = yawDelta != 0.0f || elevationDelta != 0.0f;

        if (changed)
        {
            yawRadians = MathF.IEEERemainder(yawRadians + yawDelta, MathF.Tau);
            elevationRadians = Math.Clamp(
                elevationRadians + elevationDelta,
                -1.4f,
                1.4f);
        }

        bool isToggleDown = IsKeyDown(keys, SDL.Scancode.L);
        bool isLowerDown = IsKeyDown(keys, SDL.Scancode.Leftbracket);
        bool isHigherDown = IsKeyDown(keys, SDL.Scancode.Rightbracket);
        bool isResetDown = IsKeyDown(keys, SDL.Scancode.R);

        if (isToggleDown && !wasToggleDown)
        {
            directionalEnabled = !directionalEnabled;
            changed = true;
        }

        if (isLowerDown && !wasLowerDown)
        {
            intensity = Math.Clamp(intensity - 0.1f, 0.0f, MaxEditableLightIntensity);
            changed = true;
        }

        if (isHigherDown && !wasHigherDown)
        {
            intensity = Math.Clamp(intensity + 0.1f, 0.0f, MaxEditableLightIntensity);
            changed = true;
        }

        if (isResetDown && !wasResetDown)
        {
            yawRadians = InitialLightYaw;
            elevationRadians = InitialLightElevation;
            intensity = InitialLightIntensity;
            directionalEnabled = true;
            changed = true;
        }

        wasToggleDown = isToggleDown;
        wasLowerDown = isLowerDown;
        wasHigherDown = isHigherDown;
        wasResetDown = isResetDown;
        return changed;
    }

    private static SceneObject CreateObject(
        MeshData mesh,
        Material material,
        Vector3 position,
        Vector3 scale,
        float yawRadians,
        float pitchRadians,
        float rollRadians = 0.0f)
    {
        var transform = new Transform(
            position,
            Quaternion.CreateFromYawPitchRoll(yawRadians, pitchRadians, rollRadians),
            scale);

        return new SceneObject(mesh, transform, material);
    }

    private static ModelData LoadModel(string path)
    {
        using FileStream stream = File.OpenRead(path);
        var parser = new ObjModelParser(Path.GetFileName(path));
        return parser.Parse(stream);
    }

    private static Texture LoadTexture(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return new TextureLoader().Load(stream, path);
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
        Console.Error.WriteLine("Usage: HelloLighting [path/to/model.obj]");
        Console.Error.WriteLine(
            "With no path, the example loads its copied Assets/cube.obj fixture and " +
            "the copied checkerboard texture. The OBJ must contain vertex normals.");
    }
}
