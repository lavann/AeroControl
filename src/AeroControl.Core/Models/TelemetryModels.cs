namespace AeroControl.Core.Models;

public sealed record TelemetryPoint(
    DateTimeOffset CapturedAt,
    int? CpuTemperatureCelsius,
    int? GpuTemperatureCelsius,
    int? Fan1Rpm,
    int? Fan2Rpm,
    int? BatteryChargePercent,
    int? BatteryPowerMilliwatts);
