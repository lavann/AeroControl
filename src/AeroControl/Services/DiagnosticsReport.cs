namespace AeroControl.Services;

public sealed record DiagnosticsReport(
    string SchemaVersion,
    bool IsEvidenceOnly,
    DateTimeOffset GeneratedAt,
    string ApplicationVersion,
    string OperatingSystem,
    string WindowsVersion,
    string ProcessArchitecture,
    string Framework,
    bool IsAdministrator,
    string SignatureStatus,
    string ExecutableSha256,
    string Manufacturer,
    string Model,
    string SystemSku,
    string BiosVersion,
    bool IsVerifiedConfiguration,
    CompatibilityTelemetry Telemetry,
    IReadOnlyList<string> FirmwareReadMethods,
    IReadOnlyList<string> FirmwareWriteMethods,
    IReadOnlyList<string> ConflictingProcesses,
    IReadOnlyList<string> CollectionErrors)
{
    public const string CurrentSchemaVersion = "aerocontrol.compatibility-report.v1";
    public const int MaximumIdentityLength = 128;
    public const int MaximumMethodCount = 256;
    public const int MaximumMethodNameLength = 128;
}

public sealed record CompatibilityTelemetry(
    bool IsAvailable,
    int? CpuTemperatureCelsius,
    int? GpuTemperatureCelsius,
    int? Fan1Rpm,
    int? Fan2Rpm,
    int? CpuFanDutyPercent,
    int? GpuFanDutyPercent,
    bool? FanHealthGood,
    string FanMode,
    int? FixedFanPercent);
