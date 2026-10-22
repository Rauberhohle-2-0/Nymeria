using SDL3;

namespace Nymeria.Graphics;

/// <summary>
/// Chooses the SDL3 GPU backend Nymeria uses on the host platform.
/// </summary>
/// <remarks>
/// <para>
/// This type is the only place that knows which backend a platform prefers. It
/// holds no GPU state and makes no SDL call, so every decision below is
/// unit-testable without a device.
/// </para>
/// <para>
/// Nymeria requests one shader format per platform and lets SDL select the driver
/// that consumes it: Metal Shading Language on macOS, where SDL selects the
/// <c>metal</c> driver, and DXIL on Windows, where SDL selects the
/// <c>direct3d12</c> driver. Linux has a planned Vulkan/SPIR-V target but no
/// implementation, so it is rejected explicitly rather than falling back to an
/// untested backend.
/// </para>
/// </remarks>
internal static class GpuBackendSelection
{
    /// <summary>
    /// The platforms for which Nymeria has a GPU backend policy.
    /// </summary>
    internal enum HostPlatform
    {
        MacOS,
        Windows,
        Linux,
        Other,
    }

    /// <summary>
    /// The backend Nymeria asks SDL for on one platform.
    /// </summary>
    /// <param name="ShaderFormats">
    /// The shader format bitmask passed to SDL when probing and creating the
    /// device. SDL selects a driver that consumes these formats.
    /// </param>
    /// <param name="ShaderFormat">
    /// The single bytecode format the created device must accept and the format
    /// shaders must be created with.
    /// </param>
    /// <param name="DriverName">
    /// The SDL driver name the created device must report.
    /// </param>
    internal readonly record struct GpuBackendPolicy(
        SDL.GPUShaderFormat ShaderFormats,
        SDL.GPUShaderFormat ShaderFormat,
        string DriverName);

    /// <summary>
    /// Gets the platform of the running process.
    /// </summary>
    internal static HostPlatform CurrentPlatform { get; } = DetectHostPlatform(
        OperatingSystem.IsMacOS(),
        OperatingSystem.IsWindows(),
        OperatingSystem.IsLinux());

    /// <summary>
    /// Maps operating-system checks to a <see cref="HostPlatform"/>.
    /// </summary>
    internal static HostPlatform DetectHostPlatform(bool isMacOS, bool isWindows, bool isLinux)
    {
        if (isMacOS)
        {
            return HostPlatform.MacOS;
        }

        if (isWindows)
        {
            return HostPlatform.Windows;
        }

        if (isLinux)
        {
            return HostPlatform.Linux;
        }

        return HostPlatform.Other;
    }

    /// <summary>
    /// Selects the backend policy for a platform.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> for a platform with an implemented backend, and
    /// <see langword="false"/> for Linux and unknown platforms, which are
    /// deliberately unsupported rather than mapped to an untested backend.
    /// </returns>
    internal static bool TrySelectPolicy(HostPlatform platform, out GpuBackendPolicy policy)
    {
        switch (platform)
        {
            case HostPlatform.MacOS:
                policy = new GpuBackendPolicy(
                    SDL.GPUShaderFormat.MSL,
                    SDL.GPUShaderFormat.MSL,
                    "metal");
                return true;

            case HostPlatform.Windows:
                policy = new GpuBackendPolicy(
                    SDL.GPUShaderFormat.DXIL,
                    SDL.GPUShaderFormat.DXIL,
                    "direct3d12");
                return true;

            default:
                policy = default;
                return false;
        }
    }

    /// <summary>
    /// Reports whether a shader-format bitmask includes a specific format.
    /// </summary>
    internal static bool Contains(
        SDL.GPUShaderFormat formats,
        SDL.GPUShaderFormat format)
    {
        return format != SDL.GPUShaderFormat.Invalid && (formats & format) == format;
    }

    /// <summary>
    /// Reports whether a created device matches the policy it was requested with.
    /// </summary>
    /// <remarks>
    /// Both halves are required: the driver name proves which backend SDL picked,
    /// and the device formats prove that backend accepts the bytecode Nymeria can
    /// produce. Checking only the operating system would assume support.
    /// </remarks>
    internal static bool DeviceMatchesPolicy(
        GpuBackendPolicy policy,
        string? driverName,
        SDL.GPUShaderFormat deviceFormats)
    {
        return string.Equals(driverName, policy.DriverName, StringComparison.Ordinal)
            && Contains(deviceFormats, policy.ShaderFormat);
    }

    /// <summary>
    /// Reports whether shader bytes in <paramref name="codeFormat"/> may be
    /// submitted to a device that uses <paramref name="deviceShaderFormat"/>.
    /// </summary>
    /// <remarks>
    /// This keeps device capability separate from the bytecode format a caller
    /// supplies. Shader bytes are never relabelled to satisfy a device.
    /// </remarks>
    internal static bool AcceptsShaderCode(
        SDL.GPUShaderFormat deviceShaderFormat,
        SDL.GPUShaderFormat codeFormat)
    {
        return deviceShaderFormat != SDL.GPUShaderFormat.Invalid
            && codeFormat == deviceShaderFormat;
    }

    /// <summary>
    /// Gets a readable name for a platform.
    /// </summary>
    internal static string DescribePlatform(HostPlatform platform)
    {
        return platform switch
        {
            HostPlatform.MacOS => "macOS",
            HostPlatform.Windows => "Windows",
            HostPlatform.Linux => "Linux",
            _ => "this platform",
        };
    }

    /// <summary>
    /// Gets a readable name for a single shader format.
    /// </summary>
    internal static string DescribeFormat(SDL.GPUShaderFormat format)
    {
        return format switch
        {
            SDL.GPUShaderFormat.MSL => "MSL",
            SDL.GPUShaderFormat.MetalLib => "metallib",
            SDL.GPUShaderFormat.DXIL => "DXIL",
            SDL.GPUShaderFormat.DXBC => "DXBC",
            SDL.GPUShaderFormat.SPIRV => "SPIR-V",
            SDL.GPUShaderFormat.Private => "private",
            _ => "none",
        };
    }

    /// <summary>
    /// Gets a stable, comma-separated name for a shader-format bitmask.
    /// </summary>
    internal static string DescribeFormats(SDL.GPUShaderFormat formats)
    {
        if (formats == SDL.GPUShaderFormat.Invalid)
        {
            return "none";
        }

        var names = new List<string>();

        foreach (SDL.GPUShaderFormat format in Enum.GetValues<SDL.GPUShaderFormat>())
        {
            if (format != SDL.GPUShaderFormat.Invalid && Contains(formats, format))
            {
                names.Add(DescribeFormat(format));
            }
        }

        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    /// <summary>
    /// Describes a driver list for a diagnostic message.
    /// </summary>
    internal static string DescribeDrivers(IReadOnlyList<string> drivers)
    {
        return drivers.Count == 0 ? "none" : string.Join(", ", drivers);
    }

    /// <summary>
    /// Builds the message used when a platform has no implemented GPU backend.
    /// </summary>
    internal static string CreateUnsupportedPlatformMessage(HostPlatform platform)
    {
        return $"GPU rendering is not supported on {DescribePlatform(platform)}. " +
            "Nymeria provides MSL shaders for SDL's Metal backend on macOS and DXIL " +
            "shaders for SDL's Direct3D 12 backend on Windows. Linux is planned to " +
            "use Vulkan with SPIR-V but is not implemented, so no untested backend " +
            "is selected as a fallback.";
    }

    /// <summary>
    /// Builds the message used when no SDL driver accepts the requested formats.
    /// </summary>
    internal static string CreateNoBackendMessage(
        HostPlatform platform,
        GpuBackendPolicy policy,
        IReadOnlyList<string> drivers,
        string? sdlError)
    {
        string message = $"No SDL3 GPU driver on {DescribePlatform(platform)} accepts " +
            $"{DescribeFormat(policy.ShaderFormat)} shaders, which is the format " +
            $"Nymeria provides there. Nymeria expects the '{policy.DriverName}' " +
            $"driver for this platform. SDL3 GPU drivers in this build: " +
            $"{DescribeDrivers(drivers)}. SDL error: {DescribeError(sdlError)}";

        if (platform == HostPlatform.Windows)
        {
            message += " Direct3D 12 requires Windows 10 or newer and a " +
                "Direct3D 12-capable graphics driver; update the graphics driver " +
                "if the backend is missing.";
        }

        return message;
    }

    /// <summary>
    /// Builds the message used when SDL reports support but device creation fails.
    /// </summary>
    internal static string CreateDeviceCreationFailureMessage(
        HostPlatform platform,
        GpuBackendPolicy policy,
        IReadOnlyList<string> drivers,
        string? sdlError)
    {
        return $"SDL could not create a {DescribeFormat(policy.ShaderFormat)} GPU " +
            $"device on {DescribePlatform(platform)}. SDL3 GPU drivers in this " +
            $"build: {DescribeDrivers(drivers)}. " +
            $"SDL error: {DescribeError(sdlError)}";
    }

    /// <summary>
    /// Builds the message used when the created device reports an unexpected driver.
    /// </summary>
    internal static string CreateDriverMismatchMessage(
        HostPlatform platform,
        GpuBackendPolicy policy,
        string? driverName,
        SDL.GPUShaderFormat deviceFormats)
    {
        return $"SDL created a GPU device on {DescribePlatform(platform)} with the " +
            $"driver '{driverName ?? "unknown"}' instead of the expected " +
            $"'{policy.DriverName}'. The device reports " +
            $"{DescribeFormats(deviceFormats)} shader support, and Nymeria requested " +
            $"{DescribeFormat(policy.ShaderFormat)}. Do not force a different backend " +
            "through SDL_HINT_GPU_DRIVER.";
    }

    /// <summary>
    /// Builds the message used when the device driver lacks the required format.
    /// </summary>
    internal static string CreateDeviceFormatMismatchMessage(
        HostPlatform platform,
        GpuBackendPolicy policy,
        string? driverName,
        SDL.GPUShaderFormat deviceFormats)
    {
        return $"The '{driverName ?? "unknown"}' GPU driver on " +
            $"{DescribePlatform(platform)} accepts {DescribeFormats(deviceFormats)} " +
            $"shaders, but Nymeria provides {DescribeFormat(policy.ShaderFormat)} " +
            "shaders there, so it cannot create a matching shader on this device.";
    }

    /// <summary>
    /// Builds the message used when shader bytes do not match the device format.
    /// </summary>
    internal static string CreateShaderCodeMismatchMessage(
        string? driverName,
        SDL.GPUShaderFormat deviceShaderFormat,
        SDL.GPUShaderFormat codeFormat)
    {
        return $"The '{driverName ?? "unknown"}' GPU device requires " +
            $"{DescribeFormat(deviceShaderFormat)} shader code, but the supplied " +
            $"shader bytes are {DescribeFormat(codeFormat)}. Shader assets for this " +
            "backend are not implemented yet: Nymeria currently embeds MSL source " +
            "only, so rendering on this backend needs the shader work planned for " +
            "the next Windows task.";
    }

    private static string DescribeError(string? sdlError)
    {
        return string.IsNullOrWhiteSpace(sdlError) ? "no detail reported" : sdlError;
    }
}
