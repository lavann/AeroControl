using AeroControl.Core.Abstractions;
using AeroControl.Core.Models;
using AeroControl.Core.Services;

namespace AeroControl.Core.Tests;

public sealed class BatteryServiceTests
{
    [Fact]
    public void ParseReport_ReadsStructuredBatteryData()
    {
        const string xml = """
            <BatteryReport xmlns="http://schemas.microsoft.com/battery/2012">
              <Batteries>
                <Battery>
                  <Id>Aero 15</Id>
                  <Manufacturer>GIGABYTE</Manufacturer>
                  <Chemistry>LI-I</Chemistry>
                  <DesignCapacity>94240</DesignCapacity>
                  <FullChargeCapacity>90000</FullChargeCapacity>
                  <CycleCount>120</CycleCount>
                </Battery>
              </Batteries>
            </BatteryReport>
            """;

        var report = PowerCfgBatteryReportProvider.ParseReport(xml);

        Assert.NotNull(report);
        Assert.Equal("Aero 15", report.Name);
        Assert.Equal("GIGABYTE", report.Manufacturer);
        Assert.Equal("LI-I", report.Chemistry);
        Assert.Equal(94_240, report.DesignCapacityMilliwattHours);
        Assert.Equal(90_000, report.FullChargeCapacityMilliwattHours);
        Assert.Equal(120, report.CycleCount);
    }

    [Fact]
    public async Task GetSnapshot_CombinesReportAndLiveWindowsTelemetry()
    {
        var bridge = new FakeBatteryWmiBridge();
        var report = new BatteryReportData(
            "Aero 15",
            "GIGABYTE",
            "LI-I",
            94_240,
            90_000,
            120);
        var service = new WindowsBatteryService(
            bridge,
            new FakeBatteryReportProvider(report));

        var snapshot = await service.GetSnapshotAsync();

        Assert.True(snapshot.IsConnected);
        Assert.Equal(80, snapshot.ChargePercent);
        Assert.Equal(96, snapshot.CapacityRetentionPercent);
        Assert.Equal(BatteryPowerState.Connected, snapshot.PowerState);
        Assert.True(snapshot.PowerOnline);
        Assert.Equal(72_000, snapshot.RemainingCapacityMilliwattHours);
        Assert.Equal(16_725, snapshot.VoltageMillivolts);
        Assert.Equal(0, snapshot.PowerRateMilliwatts);
        Assert.Null(snapshot.ErrorMessage);
    }

    [Fact]
    public async Task GetSnapshot_FallsBackToWmiWhenDesignReportIsUnavailable()
    {
        var bridge = new FakeBatteryWmiBridge();
        bridge.Battery["EstimatedChargeRemaining"] = null;
        bridge.Status["PowerOnline"] = false;
        bridge.Status["Discharging"] = true;
        bridge.Status["RemainingCapacity"] = 45_000;
        bridge.Status["DischargeRate"] = 20_000;
        var service = new WindowsBatteryService(
            bridge,
            new FakeBatteryReportProvider((BatteryReportData?)null));

        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal(50, snapshot.ChargePercent);
        Assert.Equal(BatteryPowerState.Discharging, snapshot.PowerState);
        Assert.Equal(-20_000, snapshot.PowerRateMilliwatts);
        Assert.Equal(90_000, snapshot.FullChargeCapacityMilliwattHours);
        Assert.Equal(120, snapshot.CycleCount);
        Assert.Null(snapshot.CapacityRetentionPercent);
    }

    [Fact]
    public async Task GetSnapshot_ReturnsUnavailableWhenWindowsReportsNoBattery()
    {
        var bridge = new FakeBatteryWmiBridge();
        bridge.Battery.Clear();
        bridge.Status.Clear();
        bridge.FullCapacity.Clear();
        bridge.Cycles.Clear();
        var service = new WindowsBatteryService(
            bridge,
            new FakeBatteryReportProvider((BatteryReportData?)null));

        var snapshot = await service.GetSnapshotAsync();

        Assert.False(snapshot.IsConnected);
        Assert.Equal(BatteryPowerState.Unknown, snapshot.PowerState);
    }

    [Fact]
    public async Task GetSnapshot_RetriesDesignReportAfterInternalTimeout()
    {
        var report = new BatteryReportData(
            "Aero 15",
            "GIGABYTE",
            "LI-I",
            94_240,
            90_000,
            120);
        var provider = new FakeBatteryReportProvider(
        [
            _ => Task.FromException<BatteryReportData?>(new OperationCanceledException()),
            _ => Task.FromResult<BatteryReportData?>(report)
        ]);
        var service = new WindowsBatteryService(new FakeBatteryWmiBridge(), provider);

        var first = await service.GetSnapshotAsync();
        var second = await service.GetSnapshotAsync();

        Assert.Null(first.DesignCapacityMilliwattHours);
        Assert.Contains("timed out", first.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(94_240, second.DesignCapacityMilliwattHours);
        Assert.Null(second.ErrorMessage);
        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public void CapacityRetention_RejectsZeroFullCapacity()
    {
        var snapshot = new BatterySnapshot(
            DateTimeOffset.Now,
            "Battery",
            string.Empty,
            string.Empty,
            0,
            BatteryPowerState.Unknown,
            null,
            0,
            94_240,
            0,
            null,
            null,
            null,
            true,
            null);

        Assert.Null(snapshot.CapacityRetentionPercent);
    }

    [Fact]
    public async Task GetSnapshot_CanceledWaiterDoesNotCancelSharedReport()
    {
        var completion = new TaskCompletionSource<BatteryReportData?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeBatteryReportProvider(
        [
            _ => completion.Task
        ]);
        var service = new WindowsBatteryService(new FakeBatteryWmiBridge(), provider);
        using var canceled = new CancellationTokenSource();

        var canceledCaller = service.GetSnapshotAsync(canceled.Token);
        var successfulCaller = service.GetSnapshotAsync();
        canceled.Cancel();
        completion.SetResult(new BatteryReportData(
            "Aero 15",
            "GIGABYTE",
            "LI-I",
            94_240,
            90_000,
            120));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledCaller);
        var snapshot = await successfulCaller;
        Assert.Equal(94_240, snapshot.DesignCapacityMilliwattHours);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task Dispose_CancelsAndAwaitsSharedReportProvider()
    {
        var provider = new LifetimeAwareBatteryReportProvider();
        var service = new WindowsBatteryService(new FakeBatteryWmiBridge(), provider);

        var snapshotTask = service.GetSnapshotAsync();
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));

        await provider.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var snapshot = await snapshotTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(snapshot.IsConnected);
        Assert.Contains("timed out", snapshot.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetSnapshot_UsesCimFullyChargedFallback()
    {
        var bridge = new FakeBatteryWmiBridge();
        bridge.Battery["BatteryStatus"] = 3;
        bridge.Status.Remove("PowerOnline");
        bridge.Status.Remove("Charging");
        bridge.Status.Remove("Discharging");
        var service = new WindowsBatteryService(
            bridge,
            new FakeBatteryReportProvider((BatteryReportData?)null));

        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal(BatteryPowerState.FullyCharged, snapshot.PowerState);
    }

    private sealed class LifetimeAwareBatteryReportProvider : IBatteryReportProvider
    {
        public TaskCompletionSource Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Canceled { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<BatteryReportData?> GetReportAsync(
            CancellationToken cancellationToken = default)
        {
            using var registration = cancellationToken.Register(() => Canceled.TrySetResult());
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }
}
