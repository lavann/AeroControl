using AeroControl.Core.Abstractions;
using AeroControl.Core.Models;

namespace AeroControl.Core.Services;

public sealed class DemoBatteryService : IBatteryService
{
    public Task<BatterySnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new BatterySnapshot(
            DateTimeOffset.Now,
            "Aero 15",
            "GIGABYTE",
            "Li-ion",
            84,
            BatteryPowerState.Connected,
            true,
            74_420,
            94_240,
            88_120,
            164,
            16_680,
            0,
            true,
            null));

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
