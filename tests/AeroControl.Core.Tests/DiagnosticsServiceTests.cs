using AeroControl.Core.Services;
using AeroControl.Services;
using AeroControl.ViewModels;

namespace AeroControl.Core.Tests;

public sealed class DiagnosticsServiceTests
{
    [Fact]
    public async Task Capture_ContainsSanitizedCompatibilityData()
    {
        var executablePath = typeof(DiagnosticsServiceTests).Assembly.Location;
        var service = new DiagnosticsService(
            new GigabyteHardwareService(new FakeWmiBridge()),
            executablePath,
            () => ["ControlCenter", "FanControl"]);

        var report = await service.CaptureAsync();
        var json = DiagnosticsService.ToJson(report);

        Assert.Equal("GIGABYTE", report.Manufacturer);
        Assert.Equal("AERO 15-SA", report.Model);
        Assert.Equal("P75SA", report.SystemSku);
        Assert.True(report.IsVerifiedConfiguration);
        Assert.Contains("getRpm1", report.FirmwareReadMethods, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(["ControlCenter", "FanControl"], report.ConflictingProcesses);
        Assert.Equal(64, report.ExecutableSha256.Length);
        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(executablePath, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("serial", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mac", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Capture_DegradesWithoutLeakingWhenHardwareDiscoveryFails()
    {
        var bridge = new FakeWmiBridge
        {
            FailMethodDiscovery = true
        };
        var service = new DiagnosticsService(
            new GigabyteHardwareService(bridge),
            typeof(DiagnosticsServiceTests).Assembly.Location,
            () => []);

        var report = await service.CaptureAsync();

        Assert.Empty(report.FirmwareReadMethods);
        Assert.Empty(report.FirmwareWriteMethods);
        Assert.NotEmpty(report.CollectionErrors);
        var json = DiagnosticsService.ToJson(report);
        Assert.DoesNotContain("Injected", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("denial", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Capture_SanitizesProcessInventoryFailure()
    {
        var service = new DiagnosticsService(
            new DemoHardwareService(),
            typeof(DiagnosticsServiceTests).Assembly.Location,
            () => throw new UnauthorizedAccessException("C:\\Users\\private-user\\secret"));

        var report = await service.CaptureAsync();
        var json = DiagnosticsService.ToJson(report);

        Assert.Contains("Process inventory: access denied.", report.CollectionErrors);
        Assert.DoesNotContain("private-user", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Export_WhenCaptureUnavailableReturnsFixedSanitizedJson()
    {
        using var viewModel = new MainViewModel(
            new DemoHardwareService(),
            new DemoBatteryService(),
            true,
            false);

        var json = viewModel.GetCachedDiagnosticsJson();

        Assert.Equal("{\"Status\":\"Sanitized diagnostics are unavailable.\"}", json);
    }
}
