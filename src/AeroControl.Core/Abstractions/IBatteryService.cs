using AeroControl.Core.Models;

namespace AeroControl.Core.Abstractions;

public interface IBatteryService : IAsyncDisposable
{
    Task<BatterySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
