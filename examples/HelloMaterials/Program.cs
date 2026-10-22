using System.Diagnostics;
using System.Numerics;
using Nymeria.Core.Materials;
using Nymeria.Core.Models;
using Nymeria.Core.Scenes;
using Nymeria.Examples;
using Nymeria.Formats.Textures;
using Nymeria.Graphics;
using Nymeria.Platform;
using SDL3;

namespace HelloMaterials;

internal static class Program
{
    private const int TargetFrameMilliseconds = 16;
    private const double MaxFrameDeltaSeconds = 0.1;

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

        try
        {
            Texture baseColorTexture = LoadBaseColorTexture(args);

            using Window window = Window.Create(new WindowOptions());
            using MaterialRenderer renderer = MaterialRenderer.Create(window.Handle);

            // A compressed DDS variant may not be sampleable on this backend.
            // Report it clearly and keep the demo usable with generated content
            // instead of failing while a frame is being recorded.
            if (!renderer.SupportsTexture(baseColorTexture))
            {
                string description = DescribeTexture(baseColorTexture);
                Console.Error.WriteLine(
                    $"The GPU backend cannot sample {description}. Falling back to the " +
                    "generated checkerboard texture.");
                baseColorTexture = MaterialFixtures.CreateCheckerboardTexture();
            }

            var checkerMaterial = new Material(
                Vector3.One,
                baseColorTexture,
                "checkerboard");
            var tintedCheckerMaterial = new Material(
                new Vector3(0.55f, 0.75f, 1.0f),
                baseColorTexture,
                "blue-tinted checkerboard");
            var solidMaterial = new Material(
                new Vector3(0.90f, 0.40f, 0.15f),
                null,
                "solid orange");

            MeshData sharedMesh = MaterialFixtures.CreateCubeMesh();
            var scene = new Scene();

            // Two objects share one material and one mesh. The renderer uploads
            // both the geometry and the checkerboard texture once.
            scene.Add(CreateObject(
                sharedMesh,
                checkerMaterial,
                new Vector3(-3.2f, 0.0f, -1.5f),
                new Vector3(1.0f, 1.0f, 1.0f),
                yawRadians: 0.0f,
                pitchRadians: 0.0f));
            scene.Add(CreateObject(
                sharedMesh,
                checkerMaterial,
                new Vector3(0.0f, 0.0f, 0.0f),
                new Vector3(1.25f, 1.25f, 1.25f),
                yawRadians: -0.5f,
                pitchRadians: -0.1f,
                rollRadians: 0.2f));

            // The same texture with a stronger tint shows how the shader
            // combines the sampled RGB value with the material base color.
            scene.Add(CreateObject(
                sharedMesh,
                tintedCheckerMaterial,
                new Vector3(3.2f, 0.0f, -1.5f),
                new Vector3(0.9f, 0.9f, 0.9f),
                yawRadians: 0.8f,
                pitchRadians: 0.3f));

            // A material without a texture uses the renderer's white fallback,
            // so the solid base color is shown directly.
            scene.Add(CreateObject(
                sharedMesh,
                solidMaterial,
                new Vector3(0.0f, 2.8f, -4.5f),
                new Vector3(1.5f, 0.6f, 0.9f),
                yawRadians: 0.2f,
                pitchRadians: -0.4f));

            var camera = new Camera(new Vector3(0.0f, 1.5f, 9.0f))
            {
                MoveSpeed = 4.0f,
            };

            using var controls = new FlyCameraControls(window.Handle, camera);

            Console.WriteLine(
                "HelloMaterials: two objects share the base-color material, " +
                "one uses a blue-tinted variant of the same texture, and one is " +
                "solid orange through the white fallback texture.");
            Console.WriteLine(
                "Rotate the camera to check the texture orientation, then " +
                "resize the window. WASD moves, Space/Ctrl moves up/down, hold " +
                "the right mouse button to look, and closing the window exits.");

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
                // and mouse state is current for this frame.
                controls.Update(deltaSeconds);

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
                "`dotnet run --project examples/HelloMaterials/HelloMaterials.csproj`.");
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

    private static Texture LoadBaseColorTexture(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine(
                "No texture path supplied. Using the generated 128 x 128 checkerboard " +
                "texture, which needs no external asset.");
            return MaterialFixtures.CreateCheckerboardTexture();
        }

        string path = args[0];

        try
        {
            using FileStream stream = File.OpenRead(path);
            Texture texture = new TextureLoader().Load(stream, path);
            Console.WriteLine($"Loaded base-color texture '{path}': {DescribeTexture(texture)}.");
            return texture;
        }
        catch (FormatException exception)
        {
            Console.Error.WriteLine(
                $"Could not decode texture '{path}': {exception.Message}");
        }
        catch (FileNotFoundException)
        {
            Console.Error.WriteLine($"Texture file not found: {path}");
        }
        catch (DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"Texture directory not found: {path}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine($"Cannot read texture '{path}': {exception.Message}");
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"Could not read texture '{path}': {exception.Message}");
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"Invalid texture path '{path}': {exception.Message}");
        }

        Console.Error.WriteLine(
            "Falling back to the generated checkerboard texture so the example " +
            "keeps running.");
        return MaterialFixtures.CreateCheckerboardTexture();
    }

    private static string DescribeTexture(Texture texture)
    {
        if (texture is CompressedTextureData compressed)
        {
            string colorSpace = compressed.IsSrgb ? " sRGB" : string.Empty;
            return $"{compressed.Width} x {compressed.Height} " +
                   $"{CompressedTextureData.GetFormatName(compressed.Format)}" +
                   $"{colorSpace}, {compressed.MipLevelCount} mip level(s), " +
                   $"{compressed.ByteCount} encoded bytes";
        }

        return $"{texture.Width} x {texture.Height}, {texture.MipLevelCount} mip " +
               $"level(s), {((TextureData)texture).ByteCount} RGBA bytes";
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

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "Usage: HelloMaterials [path/to/texture.{png,jpg,jpeg,tga,bmp,dds}]");
        Console.Error.WriteLine(
            "With no path, the example uses a generated checkerboard texture and " +
            "needs no external asset. PNG, JPEG, BMP, and DDS are identified by " +
            "their signatures; TGA is recognized by a .tga or .targa name and " +
            "validated by its header. BMP supports uncompressed 24-bit BGR, " +
            "32-bit BGRA/BGRX, 8-bit indexed, and standard 32-bit BI_BITFIELDS " +
            "images. Limits: 32 MiB input, 4096 x 4096 pixels, 64 MiB decoded " +
            "payload.");
    }
}
