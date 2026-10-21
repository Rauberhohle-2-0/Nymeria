using System.Numerics;
using Nymeria.Graphics;
using SDL3;

namespace Nymeria.Examples;

/// <summary>
/// Maps SDL3 keyboard and mouse state to the free-fly camera for this example.
/// </summary>
/// <remarks>
/// This is example input mapping, not a reusable input system. SDL's cached
/// state is read after <c>Window.ProcessEvents</c> pumps the event queue. Right
/// mouse button hold enables relative mouse mode; releasing the button, losing
/// input focus, minimizing, or disposing restores the cursor.
/// </remarks>
internal sealed class FlyCameraControls : IDisposable
{
    private readonly IntPtr _window;
    private readonly Camera _camera;
    private bool _isLooking;
    private bool _suppressLookUntilRelease;
    private bool _disposed;

    public FlyCameraControls(IntPtr window, Camera camera)
    {
        if (window == IntPtr.Zero)
        {
            throw new ArgumentException("A native window handle is required.", nameof(window));
        }

        _window = window;
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
    }

    public void Update(float deltaSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!HasInputFocus())
        {
            // Do not re-capture the cursor when focus returns while the right
            // button is still held. The user must release and press it again.
            _suppressLookUntilRelease |= _isLooking;
            StopLooking();
            return;
        }

        SDL.MouseButtonFlags buttons = SDL.GetMouseState(out _, out _);
        bool isRightButtonDown =
            (buttons & SDL.MouseButtonFlags.Right) != 0;

        if (!isRightButtonDown)
        {
            _suppressLookUntilRelease = false;
        }

        if (isRightButtonDown && !_isLooking && !_suppressLookUntilRelease)
        {
            StartLooking();
        }
        else if (!isRightButtonDown && _isLooking)
        {
            StopLooking();
        }

        if (_isLooking)
        {
            SDL.GetRelativeMouseState(out float deltaX, out float deltaY);
            _camera.LookByMouse(deltaX, deltaY);
        }

        ReadOnlySpan<bool> keys = SDL.GetKeyboardState(out _);
        float right = Axis(
            IsKeyDown(keys, SDL.Scancode.D),
            IsKeyDown(keys, SDL.Scancode.A));
        float up = Axis(
            IsKeyDown(keys, SDL.Scancode.Space),
            IsKeyDown(keys, SDL.Scancode.LCtrl));
        float forward = Axis(
            IsKeyDown(keys, SDL.Scancode.W),
            IsKeyDown(keys, SDL.Scancode.S));
        bool isSprinting =
            IsKeyDown(keys, SDL.Scancode.LShift) ||
            IsKeyDown(keys, SDL.Scancode.RShift);

        _camera.Move(
            new Vector3(right, up, forward),
            deltaSeconds,
            isSprinting);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _isLooking = false;
        SDL.GetRelativeMouseState(out _, out _);
        SDL.SetWindowRelativeMouseMode(_window, false);
    }

    private bool HasInputFocus()
    {
        SDL.WindowFlags flags = SDL.GetWindowFlags(_window);

        return (flags & SDL.WindowFlags.InputFocus) != 0 &&
               (flags & SDL.WindowFlags.Minimized) == 0;
    }

    private void StartLooking()
    {
        _isLooking = true;

        // Relative mode hides the cursor and keeps reporting motion at the
        // window edge while the button is held. If the platform refuses it, SDL
        // still accumulates relative motion, so mouse look remains usable.
        SDL.SetWindowRelativeMouseMode(_window, true);

        // Discard motion accumulated before capture began so the view does not
        // jump when the button is pressed.
        SDL.GetRelativeMouseState(out _, out _);
    }

    private void StopLooking()
    {
        if (!_isLooking)
        {
            return;
        }

        _isLooking = false;
        SDL.GetRelativeMouseState(out _, out _);
        SDL.SetWindowRelativeMouseMode(_window, false);
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
