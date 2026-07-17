using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using AeroControl.Core.Abstractions;
using AeroControl.Core.Models;

namespace AeroControl.Services;

public sealed class DiagnosticsService
{
    private static readonly HashSet<string> ConflictingProcessNames = new(
        ["ControlCenter", "CloudMatrixControlCenter", "FanControl", "NbfcService", "GHelper"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly IAeroHardwareService _hardware;
    private readonly Func<IReadOnlyList<string>> _processInventory;
    private readonly string _executablePath;

    public DiagnosticsService(
        IAeroHardwareService hardware,
        string? executablePath = null,
        Func<IReadOnlyList<string>>? processInventory = null)
    {
        _hardware = hardware;
        _executablePath = executablePath ?? Environment.ProcessPath ?? string.Empty;
        _processInventory = processInventory ?? GetConflictingProcesses;
    }

    public async Task<DiagnosticsReport> CaptureAsync(
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        DeviceIdentity identity;
        HardwareCapabilities capabilities;
        try
        {
            identity = await _hardware.GetDeviceIdentityAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.Add(SanitizeFailure("Device identity", exception));
            identity = new DeviceIdentity(string.Empty, string.Empty, string.Empty, string.Empty, false, false);
        }

        try
        {
            capabilities = await _hardware.GetCapabilitiesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.Add(SanitizeFailure("Firmware methods", exception));
            capabilities = new HardwareCapabilities(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        HardwareSnapshot snapshot;
        try
        {
            snapshot = await _hardware.GetSnapshotAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.Add(SanitizeFailure("Telemetry", exception));
            snapshot = HardwareSnapshot.Unavailable(string.Empty);
        }
        if (!snapshot.IsConnected && !errors.Contains("Telemetry: unavailable."))
        {
            errors.Add("Telemetry: unavailable.");
        }

        IReadOnlyList<string> conflicts;
        try
        {
            conflicts = _processInventory();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.Add(SanitizeFailure("Process inventory", exception));
            conflicts = [];
        }

        return new DiagnosticsReport(
            DiagnosticsReport.CurrentSchemaVersion,
            true,
            DateTimeOffset.Now,
            GetApplicationVersion(),
            RuntimeInformation.OSDescription,
            Environment.OSVersion.Version.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.FrameworkDescription,
            ElevationService.IsAdministrator(),
            GetSignatureStatus(_executablePath),
            GetSha256(_executablePath),
            BoundIdentity(identity.Manufacturer),
            BoundIdentity(identity.Model),
            BoundIdentity(identity.SystemSku),
            BoundIdentity(identity.BiosVersion),
            identity.IsVerifiedConfiguration,
            new CompatibilityTelemetry(
                snapshot.IsConnected,
                BoundReading(snapshot.CpuTemperatureCelsius, -50, 150),
                BoundReading(snapshot.GpuTemperatureCelsius, -50, 150),
                BoundReading(snapshot.Fan1Rpm, 0, 10_000),
                BoundReading(snapshot.Fan2Rpm, 0, 10_000),
                BoundReading(snapshot.CpuFanDutyPercent, 0, 100),
                BoundReading(snapshot.GpuFanDutyPercent, 0, 100),
                snapshot.FanHealthGood,
                snapshot.FanMode.ToString(),
                BoundReading(snapshot.FixedFanPercent, 0, 100)),
            BoundMethodNames(capabilities.GetMethods),
            BoundMethodNames(capabilities.SetMethods),
            conflicts
                .Where(ConflictingProcessNames.Contains)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            errors);
    }

    public static string ToJson(DiagnosticsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions);
    }

    private static string GetApplicationVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            assembly.GetName().Version?.ToString() ??
            "Unknown";
    }

    private static string SanitizeFailure(string operation, Exception exception) => exception switch
    {
        UnauthorizedAccessException => $"{operation}: access denied.",
        OperationCanceledException => $"{operation}: canceled.",
        _ => $"{operation}: unavailable."
    };

    private static string BoundText(string value, int maximumLength) =>
        new(value
            .Where(character => !char.IsControl(character))
            .Take(maximumLength)
            .ToArray());

    private static string BoundIdentity(string value)
    {
        var bounded = BoundText(value, DiagnosticsReport.MaximumIdentityLength);
        return Path.IsPathFullyQualified(bounded) ? string.Empty : bounded;
    }

    private static string[] BoundMethodNames(IEnumerable<string> methodNames) => methodNames
        .Select(methodName => BoundText(methodName, DiagnosticsReport.MaximumMethodNameLength))
        .Where(methodName => methodName.Length > 0 &&
            methodName.All(character => char.IsLetterOrDigit(character) || character == '_'))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .Take(DiagnosticsReport.MaximumMethodCount)
        .ToArray();

    private static int? BoundReading(int? value, int minimum, int maximum) =>
        value.HasValue && value.Value >= minimum && value.Value <= maximum
            ? value
            : null;

    private static string GetSignatureStatus(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return "Executable unavailable";
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new PEReader(stream);
            return reader.PEHeaders.PEHeader?.CertificateTableDirectory.Size > 0
                ? "Embedded Authenticode signature present"
                : "Not signed";
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException)
        {
            return "Signature status unavailable";
        }
    }

    private static string GetSha256(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string[] GetConflictingProcesses()
    {
        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (ConflictingProcessNames.Contains(process.ProcessName))
                    {
                        matches.Add(process.ProcessName);
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
                {
                    // Processes can exit while the read-only inventory is running.
                }
            }
        }

        return matches.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
