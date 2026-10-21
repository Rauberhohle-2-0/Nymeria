using Nymeria.Core.Models;
using Nymeria.Examples;
using Nymeria.Formats.Obj;

namespace HelloOBJ;

internal static class Program
{
    private const string DefaultAssetPath = "Assets/cube.obj";

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
            Console.Error.WriteLine($"Could not load OBJ '{path}': {exception.Message}");
            return 1;
        }
        catch (FileNotFoundException)
        {
            Console.Error.WriteLine($"OBJ file not found: {path}");
            return 1;
        }
        catch (DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"OBJ directory not found: {path}");
            return 1;
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine($"Cannot read OBJ file '{path}': {exception.Message}");
            return 1;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"Invalid OBJ path '{path}': {exception.Message}");
            return 1;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"Could not read OBJ file '{path}': {exception.Message}");
            return 1;
        }

        return ModelViewerApp.Run(model);
    }

    private static ModelData LoadModel(string path)
    {
        using FileStream stream = File.OpenRead(path);
        var parser = new ObjModelParser(Path.GetFileName(path));
        return parser.Parse(stream);
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: HelloOBJ [path/to/model.obj]");
        Console.Error.WriteLine(
            "With no path, the example loads its checked-in Assets/cube.obj fixture.");
    }
}
