using Nymeria.Graphics;
using SDL3;

namespace Nymeria.Graphics.Tests;

/// <summary>
/// Tests the GPU backend policy without creating a device or loading a native
/// library, so they run on every CI platform.
/// </summary>
public sealed class GpuBackendSelectionTests
{
    [Fact]
    public void Windows_SelectsDirect3D12WithDxil()
    {
        Assert.True(GpuBackendSelection.TrySelectPolicy(
            GpuBackendSelection.HostPlatform.Windows,
            out GpuBackendSelection.GpuBackendPolicy policy));

        Assert.Equal(SDL.GPUShaderFormat.DXIL, policy.ShaderFormats);
        Assert.Equal(SDL.GPUShaderFormat.DXIL, policy.ShaderFormat);
        Assert.Equal("direct3d12", policy.DriverName);
    }

    [Fact]
    public void Windows_DoesNotRequestMetalOrSpirV()
    {
        GpuBackendSelection.TrySelectPolicy(
            GpuBackendSelection.HostPlatform.Windows,
            out GpuBackendSelection.GpuBackendPolicy policy);

        Assert.False(GpuBackendSelection.Contains(policy.ShaderFormats, SDL.GPUShaderFormat.MSL));
        Assert.False(
            GpuBackendSelection.Contains(policy.ShaderFormats, SDL.GPUShaderFormat.MetalLib));
        Assert.False(GpuBackendSelection.Contains(policy.ShaderFormats, SDL.GPUShaderFormat.SPIRV));
    }

    [Fact]
    public void MacOS_SelectsMetalWithMsl()
    {
        Assert.True(GpuBackendSelection.TrySelectPolicy(
            GpuBackendSelection.HostPlatform.MacOS,
            out GpuBackendSelection.GpuBackendPolicy policy));

        Assert.Equal(SDL.GPUShaderFormat.MSL, policy.ShaderFormats);
        Assert.Equal(SDL.GPUShaderFormat.MSL, policy.ShaderFormat);
        Assert.Equal("metal", policy.DriverName);
    }

    [Fact]
    public void MacOS_DoesNotRequestWindowsOrVulkanFormats()
    {
        GpuBackendSelection.TrySelectPolicy(
            GpuBackendSelection.HostPlatform.MacOS,
            out GpuBackendSelection.GpuBackendPolicy policy);

        Assert.False(GpuBackendSelection.Contains(policy.ShaderFormats, SDL.GPUShaderFormat.DXIL));
        Assert.False(GpuBackendSelection.Contains(policy.ShaderFormats, SDL.GPUShaderFormat.DXBC));
        Assert.False(GpuBackendSelection.Contains(policy.ShaderFormats, SDL.GPUShaderFormat.SPIRV));
    }

    [Fact]
    public void DeferredPlatforms_SelectNoBackendInsteadOfFallingBack()
    {
        Assert.False(GpuBackendSelection.TrySelectPolicy(
            GpuBackendSelection.HostPlatform.Linux,
            out GpuBackendSelection.GpuBackendPolicy linuxPolicy));
        Assert.Equal(SDL.GPUShaderFormat.Invalid, linuxPolicy.ShaderFormats);
        Assert.Equal(SDL.GPUShaderFormat.Invalid, linuxPolicy.ShaderFormat);

        Assert.False(GpuBackendSelection.TrySelectPolicy(
            GpuBackendSelection.HostPlatform.Other,
            out GpuBackendSelection.GpuBackendPolicy otherPolicy));
        Assert.Equal(SDL.GPUShaderFormat.Invalid, otherPolicy.ShaderFormats);
        Assert.Equal(SDL.GPUShaderFormat.Invalid, otherPolicy.ShaderFormat);
    }

    [Fact]
    public void DetectHostPlatform_MapsOperatingSystemChecks()
    {
        Assert.Equal(
            GpuBackendSelection.HostPlatform.MacOS,
            GpuBackendSelection.DetectHostPlatform(true, false, false));
        Assert.Equal(
            GpuBackendSelection.HostPlatform.Windows,
            GpuBackendSelection.DetectHostPlatform(false, true, false));
        Assert.Equal(
            GpuBackendSelection.HostPlatform.Linux,
            GpuBackendSelection.DetectHostPlatform(false, false, true));
        Assert.Equal(
            GpuBackendSelection.HostPlatform.Other,
            GpuBackendSelection.DetectHostPlatform(false, false, false));
    }

    [Fact]
    public void DeviceMatchesPolicy_AcceptsOnlyTheExpectedDriverAndFormat()
    {
        GpuBackendSelection.TrySelectPolicy(
            GpuBackendSelection.HostPlatform.Windows,
            out GpuBackendSelection.GpuBackendPolicy windows);

        Assert.True(GpuBackendSelection.DeviceMatchesPolicy(
            windows,
            "direct3d12",
            SDL.GPUShaderFormat.DXIL));

        // A D3D12 device that also offers legacy DXBC still satisfies DXIL.
        Assert.True(GpuBackendSelection.DeviceMatchesPolicy(
            windows,
            "direct3d12",
            SDL.GPUShaderFormat.DXIL | SDL.GPUShaderFormat.DXBC));

        Assert.False(GpuBackendSelection.DeviceMatchesPolicy(
            windows,
            "vulkan",
            SDL.GPUShaderFormat.DXIL));
        Assert.False(GpuBackendSelection.DeviceMatchesPolicy(
            windows,
            "direct3d12",
            SDL.GPUShaderFormat.SPIRV));
        Assert.False(GpuBackendSelection.DeviceMatchesPolicy(
            windows,
            null,
            SDL.GPUShaderFormat.DXIL));
    }

    [Fact]
    public void DeviceMatchesPolicy_RejectsTheOtherPlatformBackend()
    {
        GpuBackendSelection.TrySelectPolicy(
            GpuBackendSelection.HostPlatform.MacOS,
            out GpuBackendSelection.GpuBackendPolicy macos);
        GpuBackendSelection.TrySelectPolicy(
            GpuBackendSelection.HostPlatform.Windows,
            out GpuBackendSelection.GpuBackendPolicy windows);

        // Neither platform silently accepts the other platform's device.
        Assert.False(GpuBackendSelection.DeviceMatchesPolicy(
            macos,
            windows.DriverName,
            windows.ShaderFormats));
        Assert.False(GpuBackendSelection.DeviceMatchesPolicy(
            windows,
            macos.DriverName,
            macos.ShaderFormats));

        Assert.True(GpuBackendSelection.DeviceMatchesPolicy(
            macos,
            "metal",
            SDL.GPUShaderFormat.MSL));
    }

    [Fact]
    public void AcceptsShaderCode_RejectsBytesThatDoNotMatchTheDevice()
    {
        // Metal device: MSL source is accepted, DXIL bytecode is not.
        Assert.True(GpuBackendSelection.AcceptsShaderCode(
            SDL.GPUShaderFormat.MSL,
            SDL.GPUShaderFormat.MSL));
        Assert.False(GpuBackendSelection.AcceptsShaderCode(
            SDL.GPUShaderFormat.MSL,
            SDL.GPUShaderFormat.DXIL));

        // Direct3D 12 device: DXIL is accepted, MSL must never be relabelled.
        Assert.True(GpuBackendSelection.AcceptsShaderCode(
            SDL.GPUShaderFormat.DXIL,
            SDL.GPUShaderFormat.DXIL));
        Assert.False(GpuBackendSelection.AcceptsShaderCode(
            SDL.GPUShaderFormat.DXIL,
            SDL.GPUShaderFormat.MSL));
        Assert.False(GpuBackendSelection.AcceptsShaderCode(
            SDL.GPUShaderFormat.DXIL,
            SDL.GPUShaderFormat.SPIRV));

        // A device with no format accepts nothing.
        Assert.False(GpuBackendSelection.AcceptsShaderCode(
            SDL.GPUShaderFormat.Invalid,
            SDL.GPUShaderFormat.MSL));
    }

    [Fact]
    public void Contains_RequiresEveryBitOfTheRequestedFormat()
    {
        Assert.True(GpuBackendSelection.Contains(
            SDL.GPUShaderFormat.MSL | SDL.GPUShaderFormat.DXIL,
            SDL.GPUShaderFormat.MSL));
        Assert.False(GpuBackendSelection.Contains(
            SDL.GPUShaderFormat.DXIL,
            SDL.GPUShaderFormat.MSL));

        // The zero value must never count as "present".
        Assert.False(GpuBackendSelection.Contains(
            SDL.GPUShaderFormat.MSL,
            SDL.GPUShaderFormat.Invalid));
    }

    [Fact]
    public void DescribeFormat_NamesTheSupportedFormats()
    {
        Assert.Equal("MSL", GpuBackendSelection.DescribeFormat(SDL.GPUShaderFormat.MSL));
        Assert.Equal("metallib", GpuBackendSelection.DescribeFormat(
            SDL.GPUShaderFormat.MetalLib));
        Assert.Equal("DXIL", GpuBackendSelection.DescribeFormat(SDL.GPUShaderFormat.DXIL));
        Assert.Equal("DXBC", GpuBackendSelection.DescribeFormat(SDL.GPUShaderFormat.DXBC));
        Assert.Equal("SPIR-V", GpuBackendSelection.DescribeFormat(SDL.GPUShaderFormat.SPIRV));
        Assert.Equal("none", GpuBackendSelection.DescribeFormat(
            SDL.GPUShaderFormat.Invalid));
    }

    [Fact]
    public void DescribeFormats_IsStableAndOmitsAbsentFormats()
    {
        Assert.Equal(
            "none",
            GpuBackendSelection.DescribeFormats(SDL.GPUShaderFormat.Invalid));
        Assert.Equal(
            "MSL",
            GpuBackendSelection.DescribeFormats(SDL.GPUShaderFormat.MSL));
        Assert.Equal(
            "DXBC, DXIL",
            GpuBackendSelection.DescribeFormats(
                SDL.GPUShaderFormat.DXIL | SDL.GPUShaderFormat.DXBC));
    }

    [Fact]
    public void UnsupportedPlatformMessage_NamesTheSupportedBackends()
    {
        string message = GpuBackendSelection.CreateUnsupportedPlatformMessage(
            GpuBackendSelection.HostPlatform.Linux);

        Assert.Contains("Linux", message, StringComparison.Ordinal);
        Assert.Contains("Metal", message, StringComparison.Ordinal);
        Assert.Contains("Direct3D 12", message, StringComparison.Ordinal);
        Assert.Contains("not implemented", message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoBackendMessage_ReportsFormatsDriversAndSdlError()
    {
        GpuBackendSelection.TrySelectPolicy(
            GpuBackendSelection.HostPlatform.Windows,
            out GpuBackendSelection.GpuBackendPolicy policy);

        string message = GpuBackendSelection.CreateNoBackendMessage(
            GpuBackendSelection.HostPlatform.Windows,
            policy,
            ["vulkan"],
            "no GPU device available");

        Assert.Contains("DXIL", message, StringComparison.Ordinal);
        Assert.Contains("direct3d12", message, StringComparison.Ordinal);
        Assert.Contains("vulkan", message, StringComparison.Ordinal);
        Assert.Contains("no GPU device available", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DriverMismatchMessage_ReportsActualAndExpectedDriver()
    {
        GpuBackendSelection.TrySelectPolicy(
            GpuBackendSelection.HostPlatform.Windows,
            out GpuBackendSelection.GpuBackendPolicy policy);

        string message = GpuBackendSelection.CreateDriverMismatchMessage(
            GpuBackendSelection.HostPlatform.Windows,
            policy,
            "vulkan",
            SDL.GPUShaderFormat.SPIRV);

        Assert.Contains("'vulkan'", message, StringComparison.Ordinal);
        Assert.Contains("'direct3d12'", message, StringComparison.Ordinal);
        Assert.Contains("SPIR-V", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShaderCodeMismatchMessage_StatesBothFormats()
    {
        string message = GpuBackendSelection.CreateShaderCodeMismatchMessage(
            "direct3d12",
            SDL.GPUShaderFormat.DXIL,
            SDL.GPUShaderFormat.MSL);

        Assert.Contains("DXIL", message, StringComparison.Ordinal);
        Assert.Contains("MSL", message, StringComparison.Ordinal);
        Assert.Contains("direct3d12", message, StringComparison.Ordinal);
    }
}
