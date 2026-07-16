namespace AeroControl.Services;

public sealed record DiagnosticsReport(
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
    IReadOnlyList<string> FirmwareReadMethods,
    IReadOnlyList<string> FirmwareWriteMethods,
    IReadOnlyList<string> ConflictingProcesses,
    IReadOnlyList<string> CollectionErrors);
