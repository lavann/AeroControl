using System.Text.Json;
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
        var bridge = new FakeWmiBridge();
        var service = new DiagnosticsService(
            new GigabyteHardwareService(bridge),
            executablePath,
            () => ["ControlCenter", "FanControl"]);

        var report = await service.CaptureAsync();
        var json = DiagnosticsService.ToJson(report);

        Assert.Equal(DiagnosticsReport.CurrentSchemaVersion, report.SchemaVersion);
        Assert.True(report.IsEvidenceOnly);
        Assert.Equal("GIGABYTE", report.Manufacturer);
        Assert.Equal("AERO 15-SA", report.Model);
        Assert.Equal("P75SA", report.SystemSku);
        Assert.True(report.IsVerifiedConfiguration);
        Assert.True(report.Telemetry.IsAvailable);
        Assert.Equal(70, report.Telemetry.CpuTemperatureCelsius);
        Assert.Equal(4637, report.Telemetry.Fan1Rpm);
        Assert.Equal("Fixed", report.Telemetry.FanMode);
        Assert.Contains("getRpm1", report.FirmwareReadMethods, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(["ControlCenter", "FanControl"], report.ConflictingProcesses);
        Assert.Equal(64, report.ExecutableSha256.Length);
        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(executablePath, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("serial", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mac", json, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(bridge.Writes);
    }

    [Fact]
    public async Task Export_UsesStableEvidenceOnlySchema()
    {
        var service = new DiagnosticsService(
            new DemoHardwareService(),
            typeof(DiagnosticsServiceTests).Assembly.Location,
            () => []);

        var json = DiagnosticsService.ToJson(await service.CaptureAsync());
        using var document = JsonDocument.Parse(json);

        Assert.Equal(
            [
                "SchemaVersion",
                "IsEvidenceOnly",
                "GeneratedAt",
                "ApplicationVersion",
                "OperatingSystem",
                "WindowsVersion",
                "ProcessArchitecture",
                "Framework",
                "IsAdministrator",
                "SignatureStatus",
                "ExecutableSha256",
                "Manufacturer",
                "Model",
                "SystemSku",
                "BiosVersion",
                "IsVerifiedConfiguration",
                "Telemetry",
                "FirmwareReadMethods",
                "FirmwareWriteMethods",
                "ConflictingProcesses",
                "CollectionErrors"
            ],
            document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            DiagnosticsReport.CurrentSchemaVersion,
            document.RootElement.GetProperty("SchemaVersion").GetString());
        Assert.True(document.RootElement.GetProperty("IsEvidenceOnly").GetBoolean());
        Assert.Equal(
            [
                "IsAvailable",
                "CpuTemperatureCelsius",
                "GpuTemperatureCelsius",
                "Fan1Rpm",
                "Fan2Rpm",
                "CpuFanDutyPercent",
                "GpuFanDutyPercent",
                "FanHealthGood",
                "FanMode",
                "FixedFanPercent"
            ],
            document.RootElement.GetProperty("Telemetry")
                .EnumerateObject()
                .Select(property => property.Name));
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
        Assert.False(report.Telemetry.IsAvailable);
        Assert.Null(report.Telemetry.CpuTemperatureCelsius);
        Assert.Null(report.Telemetry.Fan1Rpm);
        Assert.NotEmpty(report.CollectionErrors);
        var json = DiagnosticsService.ToJson(report);
        Assert.DoesNotContain("Injected", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("denial", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Capture_BoundsProviderDataAndRejectsMalformedMethodNames()
    {
        var bridge = new FakeWmiBridge
        {
            Manufacturer = new string('M', DiagnosticsReport.MaximumIdentityLength + 20),
            Model = new string('X', DiagnosticsReport.MaximumIdentityLength + 20),
            SystemSku = "C:\\Users\\private-user\\secret"
        };
        for (var index = 0; index < DiagnosticsReport.MaximumMethodCount + 20; index++)
        {
            bridge.GetMethods.Add($"ReadMethod{index:D3}");
        }
        bridge.GetMethods.Add("C:\\Users\\private-user\\secret");
        var service = new DiagnosticsService(
            new GigabyteHardwareService(bridge),
            typeof(DiagnosticsServiceTests).Assembly.Location,
            () => ["ControlCenter", "C:\\Users\\private-user\\secret"]);

        var report = await service.CaptureAsync();
        var json = DiagnosticsService.ToJson(report);

        Assert.Equal(DiagnosticsReport.MaximumIdentityLength, report.Manufacturer.Length);
        Assert.Equal(DiagnosticsReport.MaximumIdentityLength, report.Model.Length);
        Assert.Empty(report.SystemSku);
        Assert.Equal(DiagnosticsReport.MaximumMethodCount, report.FirmwareReadMethods.Count);
        Assert.All(
            report.FirmwareReadMethods,
            methodName => Assert.InRange(
                methodName.Length,
                1,
                DiagnosticsReport.MaximumMethodNameLength));
        Assert.Equal(["ControlCenter"], report.ConflictingProcesses);
        Assert.DoesNotContain("private-user", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(bridge.Writes);
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
