namespace AeroControl.Core.Models;

public enum BatteryPowerState
{
    Unknown,
    Charging,
    Discharging,
    FullyCharged,
    Connected,
    OnBattery
}

public sealed record BatteryReportData(
    string Name,
    string Manufacturer,
    string Chemistry,
    int? DesignCapacityMilliwattHours,
    int? FullChargeCapacityMilliwattHours,
    int? CycleCount);

public sealed record BatterySnapshot(
    DateTimeOffset CapturedAt,
    string Name,
    string Manufacturer,
    string Chemistry,
    int? ChargePercent,
    BatteryPowerState PowerState,
    bool? PowerOnline,
    int? RemainingCapacityMilliwattHours,
    int? DesignCapacityMilliwattHours,
    int? FullChargeCapacityMilliwattHours,
    int? CycleCount,
    int? VoltageMillivolts,
    int? PowerRateMilliwatts,
    bool IsConnected,
    string? ErrorMessage)
{
    public int? CapacityRetentionPercent =>
        DesignCapacityMilliwattHours is > 0 && FullChargeCapacityMilliwattHours is > 0
            ? (int)Math.Round(
                100d * FullChargeCapacityMilliwattHours.Value / DesignCapacityMilliwattHours.Value,
                MidpointRounding.AwayFromZero)
            : null;

    public static BatterySnapshot Unavailable(string message) => new(
        DateTimeOffset.Now,
        "Battery unavailable",
        string.Empty,
        string.Empty,
        null,
        BatteryPowerState.Unknown,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        false,
        message);
}
