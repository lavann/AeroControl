using AeroControl.Core.Models;

namespace AeroControl.Core.Services;

public sealed class AlertEvaluator
{
    private const int RequiredSamples = 3;
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);
    private readonly Dictionary<string, int> _consecutive = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastRaised = new(StringComparer.Ordinal);

    public IReadOnlyList<AlertNotice> Evaluate(
        HardwareSnapshot snapshot,
        AlertThresholds thresholds,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(thresholds);
        var observedAt = now ?? DateTimeOffset.Now;
        var alerts = new List<AlertNotice>();

        Observe(
            "cpu-hot",
            snapshot.CpuTemperatureCelsius is int cpu && cpu >= thresholds.CpuTemperatureCelsius,
            observedAt,
            () => new AlertNotice(
                "cpu-hot",
                "CPU temperature is high",
                $"CPU temperature remained at or above {thresholds.CpuTemperatureCelsius} C."),
            alerts);
        Observe(
            "gpu-hot",
            snapshot.GpuTemperatureCelsius is int gpu && gpu >= thresholds.GpuTemperatureCelsius,
            observedAt,
            () => new AlertNotice(
                "gpu-hot",
                "GPU temperature is high",
                $"GPU temperature remained at or above {thresholds.GpuTemperatureCelsius} C."),
            alerts);

        var checkFans = thresholds.EnableFanStallAlert &&
            snapshot.FanMode == FanControlMode.Fixed &&
            snapshot.FixedFanPercent is >= 50;
        ObserveFan("fan1-stall", "Fan 1", snapshot.Fan1Rpm, checkFans, observedAt, alerts);
        ObserveFan("fan2-stall", "Fan 2", snapshot.Fan2Rpm, checkFans, observedAt, alerts);
        return alerts;
    }

    public void Reset()
    {
        _consecutive.Clear();
    }

    private void ObserveFan(
        string key,
        string fanName,
        int? rpm,
        bool enabled,
        DateTimeOffset observedAt,
        ICollection<AlertNotice> alerts)
    {
        Observe(
            key,
            enabled && rpm is >= 0 and < 500,
            observedAt,
            () => new AlertNotice(
                key,
                $"{fanName} may be stalled",
                $"{fanName} remained below 500 RPM while fixed fan duty was active."),
            alerts);
    }

    private void Observe(
        string key,
        bool abnormal,
        DateTimeOffset observedAt,
        Func<AlertNotice> createNotice,
        ICollection<AlertNotice> alerts)
    {
        if (!abnormal)
        {
            _consecutive[key] = 0;
            return;
        }

        var count = _consecutive.GetValueOrDefault(key) + 1;
        _consecutive[key] = count;
        if (count < RequiredSamples ||
            _lastRaised.TryGetValue(key, out var lastRaised) && observedAt - lastRaised < Cooldown)
        {
            return;
        }

        _lastRaised[key] = observedAt;
        _consecutive[key] = 0;
        alerts.Add(createNotice());
    }
}
