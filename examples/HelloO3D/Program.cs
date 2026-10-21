using Nymeria.Core.Models;
using Nymeria.Examples;
using Nymeria.Formats.O3d;

namespace HelloO3D;

internal static class Program
{
    private const string DefaultAssetPath = "Assets/cube.o3d";

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
            Console.Error.WriteLine($"Could not load O3D '{path}': {exception.Message}");
            return 1;
        }
        catch (FileNotFoundException)
        {
            Console.Error.WriteLine($"O3D file not found: {path}");
            return 1;
        }
        catch (DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"O3D directory not found: {path}");
            return 1;
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine($"Cannot read O3D file '{path}': {exception.Message}");
            return 1;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"Invalid O3D path '{path}': {exception.Message}");
            return 1;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"Could not read O3D file '{path}': {exception.Message}");
            return 1;
        }

        return ModelViewerApp.Run(model);
    }

    private static ModelData LoadModel(string path)
    {
        using FileStream stream = File.OpenRead(path);
        var parser = new O3dModelParser(Path.GetFileName(path));
        return parser.Parse(stream);
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: HelloO3D [path/to/model.o3d]");
        Console.Error.WriteLine(
            "With no path, the example loads its checked-in Assets/cube.o3d fixture.");
    }
}
