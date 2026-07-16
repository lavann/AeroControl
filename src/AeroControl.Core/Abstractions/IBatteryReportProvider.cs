using AeroControl.Core.Models;

namespace AeroControl.Core.Abstractions;

public interface IBatteryReportProvider
{
    Task<BatteryReportData?> GetReportAsync(CancellationToken cancellationToken = default);
}
