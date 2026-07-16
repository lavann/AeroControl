using AeroControl.Core.Models;
using AeroControl.Core.Services;

namespace AeroControl.Core.Tests;

public sealed class GigabyteHardwareServiceTests
{
    [Fact]
    public async Task GetDeviceIdentity_RecognizesVerifiedAeroModel()
    {
        var service = new GigabyteHardwareService(new FakeWmiBridge());

        var identity = await service.GetDeviceIdentityAsync();

        Assert.Equal("GIGABYTE AERO 15-SA", identity.DisplayName);
        Assert.Equal("P75SA", identity.SystemSku);
        Assert.Equal("FB09", identity.BiosVersion);
        Assert.True(identity.FirmwareInterfaceDetected);
        Assert.True(identity.IsVerifiedConfiguration);
    }

    [Fact]
    public async Task GetSnapshot_DecodesTelemetryAndFixedDuty()
    {
        var service = new GigabyteHardwareService(new FakeWmiBridge());

        var snapshot = await service.GetSnapshotAsync();

        Assert.True(snapshot.IsConnected);
        Assert.Equal(70, snapshot.CpuTemperatureCelsius);
        Assert.Equal(61, snapshot.GpuTemperatureCelsius);
        Assert.Equal(4637, snapshot.Fan1Rpm);
        Assert.Equal(4597, snapshot.Fan2Rpm);
        Assert.Equal(80, snapshot.CpuFanDutyPercent);
        Assert.Equal(80, snapshot.GpuFanDutyPercent);
        Assert.Equal(FanControlMode.Fixed, snapshot.FanMode);
        Assert.True(snapshot.FanHealthGood);
    }

    [Fact]
    public async Task SetFixedFanPercent_AppliesVerifiedSequenceToBothFans()
    {
        var bridge = new FakeWmiBridge();
        var service = new GigabyteHardwareService(bridge);

        var result = await service.SetFixedFanPercentAsync(80);

        Assert.True(result.Succeeded);
        Assert.Equal(80, result.ReportedPercent);
        Assert.True(result.RequiresAutomaticRestore);
        Assert.Equal(
            [
                ("SetAutoFanStatus", (byte)0),
                ("SetStepFanStatus", (byte)1),
                ("SetFixedFanStatus", (byte)1),
                ("SetFixedFanSpeed", (byte)183),
                ("SetGPUFanDuty", (byte)183)
            ],
            bridge.Writes);
    }

    [Fact]
    public async Task SetFixedFanPercent_RejectsOutOfRangeWithoutWriting()
    {
        var bridge = new FakeWmiBridge();
        var service = new GigabyteHardwareService(bridge);

        var result = await service.SetFixedFanPercentAsync(20);

        Assert.False(result.Succeeded);
        Assert.Empty(bridge.Writes);
    }

    [Fact]
    public async Task SetFixedFanPercent_ReturnsFailureWhenDiscoveryIsDenied()
    {
        var bridge = new FakeWmiBridge
        {
            FailMethodDiscovery = true
        };
        var service = new GigabyteHardwareService(bridge);

        var result = await service.SetFixedFanPercentAsync(80);

        Assert.False(result.Succeeded);
        Assert.Contains("denied", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(bridge.Writes);
    }

    [Fact]
    public async Task SetFixedFanPercent_ReturnsFailureWhenCanceledBeforeMutation()
    {
        var bridge = new FakeWmiBridge();
        var service = new GigabyteHardwareService(bridge);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.SetFixedFanPercentAsync(80, cancellation.Token);

        Assert.False(result.Succeeded);
        Assert.Contains("canceled", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(bridge.Writes);
    }

    [Fact]
    public async Task SetFixedFanPercent_RequiresAllReadbackMethodsBeforeMutation()
    {
        var bridge = new FakeWmiBridge();
        bridge.GetMethods.Remove("GetGPUFanDuty");
        var service = new GigabyteHardwareService(bridge);

        var result = await service.SetFixedFanPercentAsync(80);

        Assert.False(result.Succeeded);
        Assert.Contains("readback", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(bridge.Writes);
    }

    [Theory]
    [InlineData("SetAutoFanStatus")]
    [InlineData("SetStepFanStatus")]
    [InlineData("SetFixedFanStatus")]
    [InlineData("SetFixedFanSpeed")]
    [InlineData("SetGPUFanDuty")]
    public async Task SetFixedFanPercent_RollsBackAfterEveryPartialFailure(string failingMethod)
    {
        var bridge = new FakeWmiBridge
        {
            FailOnceOnWriteMethod = failingMethod
        };
        var service = new GigabyteHardwareService(bridge);

        var result = await service.SetFixedFanPercentAsync(80);

        Assert.False(result.Succeeded);
        Assert.False(result.RequiresAutomaticRestore);
        Assert.Equal(
            [
                ("SetFixedFanStatus", (byte)0),
                ("SetStepFanStatus", (byte)0),
                ("SetAutoFanStatus", (byte)1)
            ],
            bridge.Writes.TakeLast(3));
        Assert.Equal(0, bridge.Readings["GetFixedFanStatus"]);
        Assert.Equal(0, bridge.Readings["GetStepFanStatus"]);
        Assert.Equal(1, bridge.Readings["GetAutoFanStatus"]);
    }

    [Fact]
    public async Task SetFixedFanPercent_RollsBackWhenDutyReadbackDoesNotMatch()
    {
        var bridge = new FakeWmiBridge
        {
            IgnoreWriteMethod = "SetGPUFanDuty"
        };
        var service = new GigabyteHardwareService(bridge);

        var result = await service.SetFixedFanPercentAsync(100);

        Assert.False(result.Succeeded);
        Assert.Contains("readback mismatch", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.RequiresAutomaticRestore);
        Assert.Equal(1, bridge.Readings["GetAutoFanStatus"]);
    }

    [Theory]
    [InlineData("OTHER", "AERO 15-SA", "P75SA", "FB09")]
    [InlineData("GIGABYTE", "AERO 15-XA", "P75XA", "FB09")]
    [InlineData("GIGABYTE", "AERO 15-SA", "OTHER", "FB09")]
    [InlineData("GIGABYTE", "AERO 15-SA", "P75SA", "FB10")]
    public async Task SetFixedFanPercent_RejectsUnverifiedConfigurationWithoutWriting(
        string manufacturer,
        string model,
        string systemSku,
        string biosVersion)
    {
        var bridge = new FakeWmiBridge
        {
            Manufacturer = manufacturer,
            Model = model,
            SystemSku = systemSku,
            BiosVersion = biosVersion
        };
        var service = new GigabyteHardwareService(bridge);

        var result = await service.SetFixedFanPercentAsync(80);

        Assert.False(result.Succeeded);
        Assert.Contains("unverified configuration", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(bridge.Writes);
    }

    [Fact]
    public async Task RestoreAutomaticFanControl_DisablesFixedModeFirst()
    {
        var bridge = new FakeWmiBridge();
        var service = new GigabyteHardwareService(bridge);

        var result = await service.RestoreAutomaticFanControlAsync();

        Assert.True(result.Succeeded);
        Assert.False(result.RequiresAutomaticRestore);
        Assert.Equal(
            [
                ("SetFixedFanStatus", (byte)0),
                ("SetStepFanStatus", (byte)0),
                ("SetAutoFanStatus", (byte)1)
            ],
            bridge.Writes);
    }

    [Fact]
    public async Task RestoreAutomaticFanControl_ReportsUnverifiedReadback()
    {
        var bridge = new FakeWmiBridge
        {
            IgnoreWriteMethod = "SetAutoFanStatus"
        };
        var service = new GigabyteHardwareService(bridge);

        var result = await service.RestoreAutomaticFanControlAsync();

        Assert.False(result.Succeeded);
        Assert.True(result.RequiresAutomaticRestore);
        Assert.Contains("could not be verified", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
