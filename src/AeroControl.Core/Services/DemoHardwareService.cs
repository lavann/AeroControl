using AeroControl.Core.Abstractions;
using AeroControl.Core.Models;

namespace AeroControl.Core.Services;

public sealed class DemoHardwareService : IAeroHardwareService
{
    private readonly object _gate = new();
    private FanControlMode _mode = FanControlMode.Fixed;
    private int _fixedPercent = 80;

    private static readonly HardwareCapabilities Capabilities = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "getCpuTemp",
            "getGpuTemp1",
            "getRpm1",
            "getRpm2",
            "GetCPUFanDuty",
            "GetGPUFanDuty",
            "GetFixedFanSpeed",
            "GetAutoFanStatus",
            "GetFixedFanStatus"
        },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SetAutoFanStatus",
            "SetStepFanStatus",
            "SetFixedFanStatus",
            "SetFixedFanSpeed",
            "SetGPUFanDuty"
        });

    public Task<DeviceIdentity> GetDeviceIdentityAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new DeviceIdentity(
            "GIGABYTE",
            "AERO 15-SA",
            "FB09",
            true,
            true));

    public Task<HardwareCapabilities> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Capabilities);

    public Task<HardwareSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var effectivePercent = _mode == FanControlMode.Automatic ? 62 : _fixedPercent;
            var fan1 = EstimateRpm(effectivePercent);
            var fan2 = fan1 - 40;
            var cpuTemperature = 86 - (int)Math.Round(effectivePercent * 0.20);
            var gpuTemperature = 73 - (int)Math.Round(effectivePercent * 0.15);

            return Task.FromResult(new HardwareSnapshot(
                DateTimeOffset.Now,
                cpuTemperature,
                gpuTemperature,
                fan1,
                fan2,
                effectivePercent,
                effectivePercent,
                true,
                _mode,
                _mode == FanControlMode.Fixed ? _fixedPercent : null,
                true,
                null));
        }
    }

    public Task<ControlResult> SetFixedFanPercentAsync(
        int percent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            FanDutyCodec.Encode(percent);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return Task.FromResult(ControlResult.Failure(exception.Message));
        }

        lock (_gate)
        {
            _mode = FanControlMode.Fixed;
            _fixedPercent = percent;
        }

        return Task.FromResult(new ControlResult(
            true,
            $"Demo fan duty set to {percent}%.",
            percent,
            percent));
    }

    public Task<ControlResult> RestoreAutomaticFanControlAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _mode = FanControlMode.Automatic;
        }

        return Task.FromResult(new ControlResult(true, "Demo automatic fan control restored."));
    }

    private static int EstimateRpm(int percent)
    {
        if (percent <= 80)
        {
            return (int)Math.Round(1577 + 38.25 * percent);
        }

        return (int)Math.Round(1569 + 38.35 * percent);
    }
}
