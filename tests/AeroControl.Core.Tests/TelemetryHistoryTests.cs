using System.Globalization;
using AeroControl.Core.Models;
using AeroControl.Core.Services;

namespace AeroControl.Core.Tests;

public sealed class TelemetryHistoryTests
{
    [Fact]
    public void Record_CombinesCoolingAndBatteryWithoutPersonalData()
    {
        var history = new TelemetryHistory();
        var now = DateTimeOffset.Parse("2026-07-16T12:00:00Z", CultureInfo.InvariantCulture);

        history.Record(Hardware(now), Battery(now.AddSeconds(1)));

        var point = Assert.Single(history.GetRecent(15, now.AddSeconds(2)));
        Assert.Equal(now.AddSeconds(1), point.CapturedAt);
        Assert.Equal(70, point.CpuTemperatureCelsius);
        Assert.Equal(4600, point.Fan1Rpm);
        Assert.Equal(83, point.BatteryChargePercent);
    }

    [Fact]
    public void GetRecent_FiltersBySelectedWindow()
    {
        var history = new TelemetryHistory();
        var now = DateTimeOffset.Parse("2026-07-16T12:00:00Z", CultureInfo.InvariantCulture);
        history.Record(Hardware(now.AddMinutes(-20)), Battery(now.AddMinutes(-20)));
        history.Record(Hardware(now.AddMinutes(-2)), Battery(now.AddMinutes(-2)));

        Assert.Single(history.GetRecent(5, now));
        Assert.Equal(2, history.GetRecent(30, now).Count);
    }

    [Fact]
    public void ToCsv_UsesInvariantStructuredColumns()
    {
        var history = new TelemetryHistory();
        var now = DateTimeOffset.Parse("2026-07-16T12:00:00Z", CultureInfo.InvariantCulture);
        history.Record(Hardware(now), Battery(now));

        var csv = history.ToCsv(15, now);

        Assert.Contains("captured_at,cpu_c,gpu_c,fan1_rpm", csv, StringComparison.Ordinal);
        Assert.Contains("2026-07-16T12:00:00.0000000+00:00,70,61,4600,4550,83,-12000", csv, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.UserName, csv, StringComparison.OrdinalIgnoreCase);
    }

    private static HardwareSnapshot Hardware(DateTimeOffset capturedAt) => new(
        capturedAt,
        70,
        61,
        4600,
        4550,
        80,
        80,
        true,
        FanControlMode.Fixed,
        80,
        true,
        null);

    private static BatterySnapshot Battery(DateTimeOffset capturedAt) => new(
        capturedAt,
        "Battery",
        "GIGABYTE",
        "LI-I",
        83,
        BatteryPowerState.Discharging,
        false,
        78_000,
        94_240,
        90_000,
        120,
        16_700,
        -12_000,
        true,
        null);
}
