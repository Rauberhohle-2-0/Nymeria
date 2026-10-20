using SDL3;

namespace Nymeria.Platform;

/// <summary>
/// A resizable native desktop window backed by SDL3.
/// </summary>
/// <remarks>
/// This type is not thread-safe. Creating, pumping events for, and disposing a
/// window must all happen on the process main thread, which macOS requires for
/// its native windowing APIs. Task 02 supports one window per process.
/// </remarks>
public sealed class Window : IDisposable
{
    private const int DefaultEventWaitMilliseconds = 100;

    private static int s_isCreated;

    private readonly IntPtr _handle;
    private readonly uint _id;
    private bool _disposed;

    private Window(IntPtr handle, uint id)
    {
        _handle = handle;
        _id = id;
    }

    /// <summary>
    /// Initializes SDL video and creates a visible, resizable window.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The call is not on the main thread, another window already exists, or SDL
    /// initialization or window creation fails.
    /// </exception>
    public static Window Create(WindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!SDL.IsMainThread())
        {
            throw new InvalidOperationException(
                "SDL windows must be created on the process main thread. " +
                "Call Window.Create from Main, not from a worker thread.");
        }

        if (Interlocked.CompareExchange(ref s_isCreated, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "Only one Nymeria window may be created per process in this version.");
        }

        bool initialized = false;

        try
        {
            if (!SDL.Init(SDL.InitFlags.Video))
            {
                throw new InvalidOperationException(
                    $"SDL video initialization failed: {SDL.GetError()}");
            }

            initialized = true;

            IntPtr handle = SDL.CreateWindow(
                options.Title,
                options.Width,
                options.Height,
                SDL.WindowFlags.Resizable);

            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"SDL window creation failed: {SDL.GetError()}");
            }

            return new Window(handle, SDL.GetWindowID(handle));
        }
        catch
        {
            if (initialized)
            {
                SDL.Quit();
            }

            Volatile.Write(ref s_isCreated, 0);
            throw;
        }
    }

    /// <summary>
    /// Waits for at least one event, processes all currently queued events, and
    /// reports whether the window should remain open.
    /// </summary>
    /// <param name="waitTimeoutMilliseconds">
    /// Maximum time to block when no event is pending. Use a positive value to
    /// avoid busy-spinning; zero only polls the queue.
    /// </param>
    /// <returns><see langword="false"/> when the application has been asked to quit.</returns>
    public bool ProcessEvents(int waitTimeoutMilliseconds = DefaultEventWaitMilliseconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(waitTimeoutMilliseconds);

        if (!SDL.WaitEventTimeout(out SDL.Event firstEvent, waitTimeoutMilliseconds))
        {
            return true;
        }

        SDL.Event currentEvent = firstEvent;
        bool isOpen = true;

        do
        {
            if (IsCloseRequest(currentEvent))
            {
                isOpen = false;
            }
        }
        while (SDL.PollEvent(out currentEvent));

        return isOpen;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            SDL.DestroyWindow(_handle);
        }
        finally
        {
            SDL.Quit();
            Volatile.Write(ref s_isCreated, 0);
        }
    }

    private bool IsCloseRequest(SDL.Event currentEvent)
    {
        if (currentEvent.Type == (uint)SDL.EventType.Quit)
        {
            return true;
        }

        return currentEvent.Type == (uint)SDL.EventType.WindowCloseRequested
            && currentEvent.Window.WindowID == _id;
    }
}
