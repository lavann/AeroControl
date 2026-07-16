using System.Globalization;
using System.Text;
using AeroControl.Core.Models;

namespace AeroControl.Core.Services;

public sealed class TelemetryHistory
{
    private const int MaximumSamples = 3_600;
    private readonly object _gate = new();
    private readonly List<TelemetryPoint> _samples = [];

    public event EventHandler? Changed;

    public void Record(HardwareSnapshot hardware, BatterySnapshot battery)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        ArgumentNullException.ThrowIfNull(battery);

        var capturedAt = hardware.CapturedAt >= battery.CapturedAt
            ? hardware.CapturedAt
            : battery.CapturedAt;
        lock (_gate)
        {
            _samples.Add(new TelemetryPoint(
                capturedAt,
                hardware.CpuTemperatureCelsius,
                hardware.GpuTemperatureCelsius,
                hardware.Fan1Rpm,
                hardware.Fan2Rpm,
                battery.ChargePercent,
                battery.PowerRateMilliwatts));
            if (_samples.Count > MaximumSamples)
            {
                _samples.RemoveRange(0, _samples.Count - MaximumSamples);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<TelemetryPoint> GetRecent(int minutes, DateTimeOffset? now = null)
    {
        var boundedMinutes = Math.Clamp(minutes, 1, 60);
        var cutoff = (now ?? DateTimeOffset.Now).AddMinutes(-boundedMinutes);
        lock (_gate)
        {
            return _samples
                .Where(sample => sample.CapturedAt >= cutoff)
                .ToArray();
        }
    }

    public string ToCsv(int minutes, DateTimeOffset? now = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("captured_at,cpu_c,gpu_c,fan1_rpm,fan2_rpm,battery_percent,battery_power_mw");
        foreach (var sample in GetRecent(minutes, now))
        {
            builder.Append(sample.CapturedAt.ToString("O", CultureInfo.InvariantCulture));
            Append(builder, sample.CpuTemperatureCelsius);
            Append(builder, sample.GpuTemperatureCelsius);
            Append(builder, sample.Fan1Rpm);
            Append(builder, sample.Fan2Rpm);
            Append(builder, sample.BatteryChargePercent);
            Append(builder, sample.BatteryPowerMilliwatts);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, int? value)
    {
        builder.Append(',');
        if (value.HasValue)
        {
            builder.Append(value.Value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
