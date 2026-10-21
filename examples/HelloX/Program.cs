using Nymeria.Core.Models;
using Nymeria.Examples;
using Nymeria.Formats.DirectX;

namespace HelloX;

internal static class Program
{
    private const string DefaultAssetPath = "Assets/cube.x";

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

        string path = args.Length == 1
            ? args[0]
            : Path.Combine(AppContext.BaseDirectory, DefaultAssetPath);

        ModelData model;

        try
        {
            model = LoadModel(path);
        }
        catch (ModelFormatException exception)
        {
            Console.Error.WriteLine($"Could not load DirectX .x '{path}': {exception.Message}");
            return 1;
        }
        catch (FileNotFoundException)
        {
            Console.Error.WriteLine($"DirectX .x file not found: {path}");
            return 1;
        }
        catch (DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"DirectX .x directory not found: {path}");
            return 1;
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine($"Cannot read DirectX .x file '{path}': {exception.Message}");
            return 1;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"Invalid DirectX .x path '{path}': {exception.Message}");
            return 1;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"Could not read DirectX .x file '{path}': {exception.Message}");
            return 1;
        }

        return ModelViewerApp.Run(model);
    }

    private static ModelData LoadModel(string path)
    {
        using FileStream stream = File.OpenRead(path);
        var parser = new XModelParser(Path.GetFileName(path));
        return parser.Parse(stream);
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: HelloX [path/to/model.x]");
        Console.Error.WriteLine(
            "With no path, the example loads its checked-in Assets/cube.x fixture.");
        Console.Error.WriteLine(
            "Only text .x files (xof 0302txt or 0303txt) with static meshes are " +
            "supported; binary, compressed, skinned, and animated files are " +
            "rejected with a diagnostic.");
    }
}
