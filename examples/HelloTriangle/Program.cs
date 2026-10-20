using Nymeria.Platform;

namespace HelloTriangle;

internal static class Program
{
    private const int EventWaitMilliseconds = 100;

    private static int Main()
    {
        try
        {
            using Window window = Window.Create(new WindowOptions());
            using TriangleRenderer renderer = TriangleRenderer.Create(window.Handle);

            renderer.Render();

            while (window.ProcessEvents(EventWaitMilliseconds))
            {
                renderer.Render();
            }

            return 0;
        }
        catch (DllNotFoundException exception)
        {
            Console.Error.WriteLine("Nymeria could not load the native SDL3 library.");
            Console.Error.WriteLine(
                "Run `dotnet restore Nymeria.slnx`, then start the example with " +
                "`dotnet run --project examples/HelloTriangle/HelloTriangle.csproj`.");
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
}
