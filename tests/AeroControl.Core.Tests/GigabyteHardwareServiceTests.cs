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
        Assert.Equal("FB09", identity.BiosVersion);
        Assert.True(identity.FirmwareInterfaceDetected);
        Assert.True(identity.IsVerifiedModel);
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
    public async Task RestoreAutomaticFanControl_DisablesFixedModeFirst()
    {
        var bridge = new FakeWmiBridge();
        var service = new GigabyteHardwareService(bridge);

        var result = await service.RestoreAutomaticFanControlAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(
            [
                ("SetFixedFanStatus", (byte)0),
                ("SetStepFanStatus", (byte)0),
                ("SetAutoFanStatus", (byte)1)
            ],
            bridge.Writes);
    }
}
