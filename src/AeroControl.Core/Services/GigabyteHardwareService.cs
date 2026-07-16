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

    private static readonly HashSet<string> VerifiedModels = new(
        ["AERO 15-SA"],
        StringComparer.OrdinalIgnoreCase);

    private readonly IWmiBridge _wmi;

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
        Task.Run(() => SetFixedFanPercent(percent), cancellationToken);

    public Task<ControlResult> RestoreAutomaticFanControlAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(RestoreAutomaticFanControl, cancellationToken);

    private DeviceIdentity GetDeviceIdentity()
    {
        var system = _wmi.QueryFirst(
            SystemNamespace,
            "SELECT Manufacturer, Model FROM Win32_ComputerSystem");
        var bios = _wmi.QueryFirst(
            SystemNamespace,
            "SELECT SMBIOSBIOSVersion FROM Win32_BIOS");

        var manufacturer = GetString(system, "Manufacturer");
        var model = GetString(system, "Model");
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
            biosVersion,
            firmwareDetected,
            VerifiedModels.Contains(model));
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
            var fan1Rpm = TryRead(capabilities, "getRpm1", errors);
            var fan2Rpm = TryRead(capabilities, "getRpm2", errors);
            var cpuDutyRaw = TryRead(capabilities, "GetCPUFanDuty", errors);
            var gpuDutyRaw = TryRead(capabilities, "GetGPUFanDuty", errors);
            var fixedDutyRaw = TryRead(capabilities, "GetFixedFanSpeed", errors);
            var automaticStatus = TryRead(capabilities, "GetAutoFanStatus", errors);
            var fixedStatus = TryRead(capabilities, "GetFixedFanStatus", errors);

            var mode = fixedStatus is > 0
                ? FanControlMode.Fixed
                : automaticStatus is > 0
                    ? FanControlMode.Automatic
                    : FanControlMode.Unknown;

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
                DecodeDuty(cpuDutyRaw),
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

    private ControlResult SetFixedFanPercent(int percent)
    {
        byte duty;
        try
        {
            duty = FanDutyCodec.Encode(percent);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return ControlResult.Failure(exception.Message);
        }

        try
        {
            var capabilities = GetCapabilities();
            if (!capabilities.CanControlFans)
            {
                return ControlResult.Failure(
                    "This firmware does not expose the required fixed-fan methods.");
            }

            InvokeSet("SetAutoFanStatus", 0);
            InvokeSet("SetStepFanStatus", 1);
            InvokeSet("SetFixedFanStatus", 1);
            InvokeSet("SetFixedFanSpeed", duty);
            InvokeSet("SetGPUFanDuty", duty);

            var reportedRaw = TryRead(capabilities, "GetFixedFanSpeed", []);
            var reportedPercent = DecodeDuty(reportedRaw);
            return new ControlResult(
                true,
                $"Fixed fan duty set to {percent}%.",
                percent,
                reportedPercent);
        }
        catch (UnauthorizedAccessException)
        {
            return ControlResult.Failure(
                "Firmware write was denied. Restart AeroControl as administrator.");
        }
        catch (Exception exception)
        {
            return ControlResult.Failure($"Fan command failed: {exception.Message}");
        }
    }

    private ControlResult RestoreAutomaticFanControl()
    {
        try
        {
            var capabilities = GetCapabilities();
            if (!capabilities.CanControlFans)
            {
                return ControlResult.Failure(
                    "This firmware does not expose the required fan-control methods.");
            }

            InvokeSet("SetFixedFanStatus", 0);
            InvokeSet("SetStepFanStatus", 0);
            InvokeSet("SetAutoFanStatus", 1);
            return new ControlResult(true, "Automatic fan control restored.");
        }
        catch (UnauthorizedAccessException)
        {
            return ControlResult.Failure(
                "Firmware write was denied. Restart AeroControl as administrator.");
        }
        catch (Exception exception)
        {
            return ControlResult.Failure($"Automatic mode failed: {exception.Message}");
        }
    }

    private int? TryRead(
        HardwareCapabilities capabilities,
        string methodName,
        List<string> errors)
    {
        if (!capabilities.CanGet(methodName))
        {
            return null;
        }

        try
        {
            return _wmi.Invoke(FirmwareNamespace, GetClass, methodName).GetInt32("Data");
        }
        catch (Exception exception)
        {
            errors.Add($"{methodName}: {exception.Message}");
            return null;
        }
    }

    private void InvokeSet(string methodName, byte value)
    {
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
}
