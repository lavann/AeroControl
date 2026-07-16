namespace AeroControl.Core.Models;

public sealed record DeviceIdentity(
    string Manufacturer,
    string Model,
    string SystemSku,
    string BiosVersion,
    bool FirmwareInterfaceDetected,
    bool IsVerifiedConfiguration)
{
    public string DisplayName => string.Join(' ', new[] { Manufacturer, Model }
        .Where(value => !string.IsNullOrWhiteSpace(value)));

    public string SupportLabel => IsVerifiedConfiguration
        ? "Verified configuration"
        : FirmwareInterfaceDetected
            ? "Compatible firmware detected; configuration read-only"
            : "Gigabyte firmware interface not detected";

    public string HardwareKey => string.Join('|',
        Manufacturer,
        Model,
        SystemSku,
        BiosVersion);
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
        GetMethods.Contains("GetAutoFanStatus") &&
        GetMethods.Contains("GetStepFanStatus") &&
        GetMethods.Contains("GetFixedFanStatus") &&
        GetMethods.Contains("GetFixedFanSpeed") &&
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
    int? ReportedPercent = null,
    bool RequiresAutomaticRestore = false)
{
    public static ControlResult Failure(
        string message,
        bool requiresAutomaticRestore = false) =>
        new(false, message, RequiresAutomaticRestore: requiresAutomaticRestore);
}
