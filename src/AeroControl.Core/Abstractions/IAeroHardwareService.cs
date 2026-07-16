using AeroControl.Core.Models;

namespace AeroControl.Core.Abstractions;

public interface IAeroHardwareService
{
    Task<DeviceIdentity> GetDeviceIdentityAsync(CancellationToken cancellationToken = default);

    Task<HardwareCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);

    Task<HardwareSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    Task<ControlResult> SetFixedFanPercentAsync(
        int percent,
        CancellationToken cancellationToken = default);

    Task<ControlResult> RestoreAutomaticFanControlAsync(
        CancellationToken cancellationToken = default);
}
