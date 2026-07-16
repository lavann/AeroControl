using AeroControl.Core.Abstractions;
using AeroControl.Core.Models;

namespace AeroControl.Core.Tests;

internal sealed class FakeBatteryReportProvider : IBatteryReportProvider
{
    private readonly Queue<Func<CancellationToken, Task<BatteryReportData?>>> _responses;

    public FakeBatteryReportProvider(BatteryReportData? report)
        : this([_ => Task.FromResult(report)])
    {
    }

    public FakeBatteryReportProvider(
        IEnumerable<Func<CancellationToken, Task<BatteryReportData?>>> responses)
    {
        _responses = new Queue<Func<CancellationToken, Task<BatteryReportData?>>>(responses);
    }

    public int CallCount { get; private set; }

    public Task<BatteryReportData?> GetReportAsync(
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        var response = _responses.Count > 1
            ? _responses.Dequeue()
            : _responses.Peek();
        return response(cancellationToken);
    }
}
