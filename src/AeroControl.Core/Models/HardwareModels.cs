namespace AeroControl.Core.Models;

public sealed record DeviceIdentity(
    string Manufacturer,
    string Model,
    string BiosVersion,
    bool FirmwareInterfaceDetected,
    bool IsVerifiedModel)
{
    public string DisplayName => string.Join(' ', new[] { Manufacturer, Model }
        .Where(value => !string.IsNullOrWhiteSpace(value)));

    public string SupportLabel => IsVerifiedModel
        ? "Verified model"
        : FirmwareInterfaceDetected
            ? "Compatible firmware detected; model unverified"
            : "Gigabyte firmware interface not detected";
}

public enum FanControlMode
{
    Unknown,
    Automatic,
    Fixed
}

public sealed record HardwareSnapshot(
    DateTimeOffset CapturedAt,
    int? CpuTemperatureCelsius,
    int? GpuTemperatureCelsius,
    int? Fan1Rpm,
    int? Fan2Rpm,
    int? CpuFanDutyPercent,
    int? GpuFanDutyPercent,
    bool? FanHealthGood,
    FanControlMode FanMode,
    int? FixedFanPercent,
    bool IsConnected,
    string? ErrorMessage)
{
    public static HardwareSnapshot Unavailable(string message) => new(
        DateTimeOffset.Now,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        FanControlMode.Unknown,
        null,
        false,
        message);
}

public sealed record HardwareCapabilities(
    IReadOnlySet<string> GetMethods,
    IReadOnlySet<string> SetMethods)
{
    public bool CanReadTelemetry =>
        GetMethods.Contains("getCpuTemp") &&
        GetMethods.Contains("getRpm1") &&
        GetMethods.Contains("getRpm2");

    public bool CanControlFans =>
        SetMethods.Contains("SetAutoFanStatus") &&
        SetMethods.Contains("SetStepFanStatus") &&
        SetMethods.Contains("SetFixedFanStatus") &&
        SetMethods.Contains("SetFixedFanSpeed") &&
        SetMethods.Contains("SetGPUFanDuty");

    public bool CanGet(string methodName) => GetMethods.Contains(methodName);

    public bool CanSet(string methodName) => SetMethods.Contains(methodName);
}

public sealed record ControlResult(
    bool Succeeded,
    string Message,
    int? RequestedPercent = null,
    int? ReportedPercent = null)
{
    public static ControlResult Failure(string message) => new(false, message);
}
