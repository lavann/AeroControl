using System.Runtime.Versioning;
using AeroControl.Core.Abstractions;
using AeroControl.Core.Models;
using AeroControl.Core.Wmi;

namespace AeroControl.Core.Services;

[SupportedOSPlatform("windows10.0.17763")]
public sealed class GigabyteHardwareService : IAeroHardwareService
{
    private const string FirmwareNamespace = @"\\.\root\WMI";
    private const string SystemNamespace = @"\\.\root\cimv2";
    private const string GetClass = "GB_WMIACPI_Get";
    private const string SetClass = "GB_WMIACPI_Set";

    private static readonly VerifiedConfiguration[] VerifiedConfigurations =
    [
        new("GIGABYTE", "AERO 15-SA", "P75SA", "FB09")
    ];

    private readonly IWmiBridge _wmi;
    private readonly object _controlGate = new();
    private bool _automaticRestoreRequired;

    public GigabyteHardwareService(IWmiBridge? wmi = null)
    {
        _wmi = wmi ?? new SystemManagementBridge();
    }

    public Task<DeviceIdentity> GetDeviceIdentityAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(GetDeviceIdentity, cancellationToken);

    public Task<HardwareCapabilities> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(GetCapabilities, cancellationToken);

    public Task<HardwareSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(GetSnapshot, cancellationToken);

    public Task<ControlResult> SetFixedFanPercentAsync(
        int percent,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => SetFixedFanPercent(percent, cancellationToken));

    public Task<ControlResult> RestoreAutomaticFanControlAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => RestoreAutomaticFanControl(cancellationToken));

    private DeviceIdentity GetDeviceIdentity()
    {
        var system = _wmi.QueryFirst(
            SystemNamespace,
            "SELECT Manufacturer, Model, SystemSKUNumber FROM Win32_ComputerSystem");
        var bios = _wmi.QueryFirst(
            SystemNamespace,
            "SELECT SMBIOSBIOSVersion FROM Win32_BIOS");

        var manufacturer = GetString(system, "Manufacturer");
        var model = GetString(system, "Model");
        var systemSku = GetString(system, "SystemSKUNumber");
        var biosVersion = GetString(bios, "SMBIOSBIOSVersion");

        var firmwareDetected = false;
        try
        {
            firmwareDetected = GetCapabilities().CanReadTelemetry;
        }
        catch
        {
            // Identity remains useful even when WMI access requires elevation.
        }

        return new DeviceIdentity(
            manufacturer,
            model,
            systemSku,
            biosVersion,
            firmwareDetected,
            IsVerifiedConfiguration(manufacturer, model, systemSku, biosVersion));
    }

    private HardwareCapabilities GetCapabilities()
    {
        var getMethods = _wmi.GetMethodNames(FirmwareNamespace, GetClass);
        var setMethods = _wmi.GetMethodNames(FirmwareNamespace, SetClass);
        return new HardwareCapabilities(getMethods, setMethods);
    }

    private HardwareSnapshot GetSnapshot()
    {
        try
        {
            var capabilities = GetCapabilities();
            if (!capabilities.CanReadTelemetry)
            {
                return HardwareSnapshot.Unavailable(
                    "Required Gigabyte telemetry methods are not available.");
            }

            var errors = new List<string>();
            var cpuTemperature = TryRead(capabilities, "getCpuTemp", errors);
            var gpuTemperature = TryRead(capabilities, "getGpuTemp1", errors);
            var fan1RpmRaw = TryRead(capabilities, "getRpm1", errors);
            var fan2RpmRaw = TryRead(capabilities, "getRpm2", errors);
            var fan1Rpm = DecodeRpm("Fan 1", fan1RpmRaw, errors);
            var fan2Rpm = DecodeRpm("Fan 2", fan2RpmRaw, errors);
            var cpuDutyRaw = TryRead(capabilities, "GetCPUFanDuty");
            var gpuDutyRaw = TryRead(capabilities, "GetGPUFanDuty");
            var fixedDutyRaw = TryRead(capabilities, "GetFixedFanSpeed");
            var automaticStatus = TryRead(capabilities, "GetAutoFanStatus");
            var fixedStatus = TryRead(capabilities, "GetFixedFanStatus");

            var mode = fixedStatus is > 0
                ? FanControlMode.Fixed
                : automaticStatus is > 0
                    ? FanControlMode.Automatic
                    : FanControlMode.Unknown;
            var cpuDutyPercent = DecodeDuty(cpuDutyRaw) ??
                (mode == FanControlMode.Fixed ? DecodeDuty(fixedDutyRaw) : null);

            var readingsAvailable = new int?[]
            {
                cpuTemperature,
                gpuTemperature,
                fan1Rpm,
                fan2Rpm
            }.Any(value => value.HasValue);

            bool? health = fan1Rpm.HasValue && fan2Rpm.HasValue
                ? fan1Rpm > 0 && fan2Rpm > 0
                : null;

            return new HardwareSnapshot(
                DateTimeOffset.Now,
                cpuTemperature,
                gpuTemperature,
                fan1Rpm,
                fan2Rpm,
                cpuDutyPercent,
                DecodeDuty(gpuDutyRaw),
                health,
                mode,
                DecodeDuty(fixedDutyRaw),
                readingsAvailable,
                errors.Count > 0 ? errors[0] : null);
        }
        catch (UnauthorizedAccessException)
        {
            return HardwareSnapshot.Unavailable(
                "Gigabyte firmware access was denied. Restart AeroControl as administrator.");
        }
        catch (Exception exception)
        {
            return HardwareSnapshot.Unavailable(exception.Message);
        }
    }

    private ControlResult SetFixedFanPercent(
        int percent,
        CancellationToken cancellationToken)
    {
        byte duty;
        try
        {
            duty = FanDutyCodec.Encode(percent);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return ControlResult.Failure(exception.Message, _automaticRestoreRequired);
        }

        lock (_controlGate)
        {
            HardwareCapabilities? capabilities = null;
            var mutationStarted = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var identity = GetDeviceIdentity();
                if (!identity.IsVerifiedConfiguration)
                {
                    return ControlResult.Failure(
                        $"Firmware writes are disabled for unverified configuration {identity.HardwareKey}.",
                        _automaticRestoreRequired);
                }

                capabilities = GetCapabilities();
                if (!capabilities.CanControlFans)
                {
                    return ControlResult.Failure(
                        "This firmware does not expose the required fan-control and readback methods.",
                        _automaticRestoreRequired);
                }

                mutationStarted = true;
                InvokeSet("SetAutoFanStatus", 0, cancellationToken);
                InvokeSet("SetStepFanStatus", 1, cancellationToken);
                InvokeSet("SetFixedFanStatus", 1, cancellationToken);
                InvokeSet("SetFixedFanSpeed", duty, cancellationToken);
                InvokeSet("SetGPUFanDuty", duty, cancellationToken);
                VerifyFixedMode(capabilities, duty);

                _automaticRestoreRequired = true;
                return new ControlResult(
                    true,
                    $"Fixed fan duty set and verified at {percent}%.",
                    percent,
                    percent,
                    true);
            }
            catch (Exception exception)
            {
                if (!mutationStarted || capabilities is null)
                {
                    return ControlResult.Failure(
                        FormatControlFailure("Fan command", exception),
                        _automaticRestoreRequired);
                }

                var rollback = TryRestoreAutomatic(capabilities, CancellationToken.None);
                _automaticRestoreRequired = !rollback.Succeeded;
                return ControlResult.Failure(
                    $"{FormatControlFailure("Fan command", exception)} {rollback.Message}",
                    _automaticRestoreRequired);
            }
        }
    }

    private ControlResult RestoreAutomaticFanControl(CancellationToken cancellationToken)
    {
        lock (_controlGate)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var identity = GetDeviceIdentity();
                if (!identity.IsVerifiedConfiguration)
                {
                    return ControlResult.Failure(
                        $"Firmware writes are disabled for unverified configuration {identity.HardwareKey}.",
                        _automaticRestoreRequired);
                }

                var capabilities = GetCapabilities();
                if (!capabilities.CanControlFans)
                {
                    return ControlResult.Failure(
                        "This firmware does not expose the required fan-control and readback methods.",
                        _automaticRestoreRequired);
                }

                _automaticRestoreRequired = true;
                var result = TryRestoreAutomatic(capabilities, cancellationToken);
                _automaticRestoreRequired = !result.Succeeded;
                return result with { RequiresAutomaticRestore = _automaticRestoreRequired };
            }
            catch (Exception exception)
            {
                return ControlResult.Failure(
                    FormatControlFailure("Automatic mode", exception),
                    _automaticRestoreRequired);
            }
        }
    }

    private ControlResult TryRestoreAutomatic(
        HardwareCapabilities capabilities,
        CancellationToken cancellationToken = default)
    {
        try
        {
            InvokeSet("SetFixedFanStatus", 0, cancellationToken);
            InvokeSet("SetStepFanStatus", 0, cancellationToken);
            InvokeSet("SetAutoFanStatus", 1, cancellationToken);
            VerifyAutomaticMode(capabilities);
            return new ControlResult(true, "Automatic fan control restored and verified.");
        }
        catch (Exception exception)
        {
            return ControlResult.Failure(
                $"Automatic rollback could not be verified: {exception.Message}",
                true);
        }
    }

    private void VerifyFixedMode(HardwareCapabilities capabilities, byte duty)
    {
        var fixedStatus = ReadRequired(capabilities, "GetFixedFanStatus");
        var stepStatus = ReadRequired(capabilities, "GetStepFanStatus");
        var automaticStatus = ReadRequired(capabilities, "GetAutoFanStatus");
        var fixedDuty = ReadRequired(capabilities, "GetFixedFanSpeed");
        var gpuDuty = ReadRequired(capabilities, "GetGPUFanDuty");

        if (fixedStatus <= 0 || stepStatus <= 0 || automaticStatus != 0 ||
            fixedDuty != duty || gpuDuty != duty)
        {
            throw new InvalidOperationException(
                $"Fan readback mismatch (fixed={fixedStatus}, step={stepStatus}, auto={automaticStatus}, " +
                $"fixedDuty={fixedDuty}, gpuDuty={gpuDuty}, expected={duty}).");
        }
    }

    private void VerifyAutomaticMode(HardwareCapabilities capabilities)
    {
        var fixedStatus = ReadRequired(capabilities, "GetFixedFanStatus");
        var stepStatus = ReadRequired(capabilities, "GetStepFanStatus");
        var automaticStatus = ReadRequired(capabilities, "GetAutoFanStatus");
        if (fixedStatus != 0 || stepStatus != 0 || automaticStatus <= 0)
        {
            throw new InvalidOperationException(
                $"Automatic-mode readback mismatch (fixed={fixedStatus}, step={stepStatus}, auto={automaticStatus}).");
        }
    }

    private int ReadRequired(HardwareCapabilities capabilities, string methodName)
    {
        if (!capabilities.CanGet(methodName))
        {
            throw new InvalidOperationException($"Required readback method {methodName} is unavailable.");
        }

        return _wmi.Invoke(FirmwareNamespace, GetClass, methodName).GetInt32("Data")
            ?? throw new InvalidOperationException($"Required readback method {methodName} returned no data.");
    }

    private int? TryRead(
        HardwareCapabilities capabilities,
        string methodName,
        List<string>? errors = null)
    {
        if (!capabilities.CanGet(methodName))
        {
            return null;
        }

        try
        {
            var value = _wmi.Invoke(FirmwareNamespace, GetClass, methodName).GetInt32("Data");
            if (!value.HasValue)
            {
                errors?.Add($"{methodName}: returned no numeric Data value.");
            }

            return value;
        }
        catch (Exception exception)
        {
            errors?.Add($"{methodName}: {exception.Message}");
            return null;
        }
    }

    private static int? DecodeRpm(
        string fanName,
        int? packedValue,
        List<string> errors)
    {
        var rpm = FanRpmCodec.Decode(packedValue);
        if (packedValue.HasValue && !rpm.HasValue)
        {
            errors.Add($"{fanName} returned an implausible packed RPM value.");
        }

        return rpm;
    }

    private void InvokeSet(
        string methodName,
        byte value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _wmi.Invoke(
            FirmwareNamespace,
            SetClass,
            methodName,
            new Dictionary<string, object?>
            {
                ["Data"] = value
            });
    }

    private static int? DecodeDuty(int? rawDuty) => rawDuty is >= 0 and <= FanDutyCodec.MaximumRawDuty
        ? FanDutyCodec.Decode(rawDuty.Value)
        : null;

    private static string GetString(
        IReadOnlyDictionary<string, object?> values,
        string name) =>
        values.TryGetValue(name, out var value) ? value?.ToString()?.Trim() ?? string.Empty : string.Empty;

    private static string FormatControlFailure(string operation, Exception exception) => exception switch
    {
        OperationCanceledException => $"{operation} was canceled.",
        UnauthorizedAccessException => $"{operation} was denied. Restart AeroControl as administrator.",
        _ => $"{operation} failed: {exception.Message}"
    };

    private static bool IsVerifiedConfiguration(
        string manufacturer,
        string model,
        string systemSku,
        string biosVersion) =>
        VerifiedConfigurations.Any(configuration => configuration.Matches(
            manufacturer,
            model,
            systemSku,
            biosVersion));

    private sealed record VerifiedConfiguration(
        string Manufacturer,
        string Model,
        string SystemSku,
        string BiosVersion)
    {
        public bool Matches(
            string manufacturer,
            string model,
            string systemSku,
            string biosVersion) =>
            string.Equals(Manufacturer, manufacturer, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Model, model, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(SystemSku, systemSku, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(BiosVersion, biosVersion, StringComparison.OrdinalIgnoreCase);
    }
}
