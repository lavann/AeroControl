using System.Globalization;
using System.Runtime.Versioning;
using AeroControl.Core.Abstractions;
using AeroControl.Core.Models;

namespace AeroControl.Core.Services;

[SupportedOSPlatform("windows10.0.17763")]
public sealed class WindowsBatteryService : IBatteryService
{
    private const string CimNamespace = @"\\.\root\cimv2";
    private const string WmiNamespace = @"\\.\root\wmi";

    private readonly IWmiBridge _wmi;
    private readonly IBatteryReportProvider _reportProvider;
    private readonly object _reportGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task<BatteryReportData?>? _reportTask;
    private BatteryReportData? _report;
    private bool _reportLoaded;
    private bool _disposed;

    public WindowsBatteryService(
        IWmiBridge? wmi = null,
        IBatteryReportProvider? reportProvider = null)
    {
        _wmi = wmi ?? new Wmi.SystemManagementBridge();
        _reportProvider = reportProvider ?? new PowerCfgBatteryReportProvider();
    }

    public async Task<BatterySnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        BatteryReportData? report = null;
        try
        {
            report = await GetReportAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            errors.Add("Battery design data: report generation timed out.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.Add($"Battery design data: {exception.Message}");
        }

        return await Task.Run(
            () => GetSnapshot(report, errors),
            cancellationToken);
    }

    private async Task<BatteryReportData?> GetReportAsync(CancellationToken cancellationToken)
    {
        Task<BatteryReportData?> reportTask;
        lock (_reportGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_reportLoaded)
            {
                return _report;
            }

            _reportTask ??= _reportProvider.GetReportAsync(_lifetime.Token);
            reportTask = _reportTask;
        }

        try
        {
            var report = await reportTask.WaitAsync(cancellationToken);
            lock (_reportGate)
            {
                if (ReferenceEquals(_reportTask, reportTask))
                {
                    _report = report;
                    _reportLoaded = true;
                    _reportTask = null;
                }
            }

            return report;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            lock (_reportGate)
            {
                if (ReferenceEquals(_reportTask, reportTask) && reportTask.IsCompleted)
                {
                    _reportTask = null;
                }
            }

            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task<BatteryReportData?>? reportTask;
        lock (_reportGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            reportTask = _reportTask;
        }

        await _lifetime.CancelAsync();
        if (reportTask is not null)
        {
            try
            {
                await reportTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The provider owns process termination, output drain, and report cleanup.
            }
        }

        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private BatterySnapshot GetSnapshot(
        BatteryReportData? report,
        List<string> errors)
    {
        var battery = QueryFirst(
            CimNamespace,
            "SELECT Name, EstimatedChargeRemaining, BatteryStatus, DesignVoltage FROM Win32_Battery",
            errors);
        var status = QueryFirst(
            WmiNamespace,
            "SELECT PowerOnline, Charging, Discharging, RemainingCapacity, Voltage, ChargeRate, DischargeRate " +
            "FROM BatteryStatus WHERE Active = TRUE",
            errors);
        var fullCapacity = QueryFirst(
            WmiNamespace,
            "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity WHERE Active = TRUE",
            errors);
        var cycleCount = QueryFirst(
            WmiNamespace,
            "SELECT CycleCount FROM BatteryCycleCount WHERE Active = TRUE",
            errors);

        var connected = battery.Count > 0 || status.Count > 0;
        if (!connected)
        {
            return BatterySnapshot.Unavailable(
                errors.Count > 0 ? errors[0] : "Windows did not report a battery.");
        }

        var remaining = GetInt32(status, "RemainingCapacity");
        var design = report?.DesignCapacityMilliwattHours;
        var full = report?.FullChargeCapacityMilliwattHours ??
            GetInt32(fullCapacity, "FullChargedCapacity");
        var chargePercent = GetInt32(battery, "EstimatedChargeRemaining") ??
            CalculatePercent(remaining, full);
        var powerOnline = GetBoolean(status, "PowerOnline");
        var batteryStatus = GetInt32(battery, "BatteryStatus");
        var charging = GetBoolean(status, "Charging") ?? batteryStatus is 6 or 7 or 8 or 9;
        var discharging = GetBoolean(status, "Discharging") ?? batteryStatus == 1;
        var powerState = GetPowerState(
            chargePercent,
            powerOnline,
            charging,
            discharging,
            batteryStatus);
        var chargeRate = GetInt32(status, "ChargeRate");
        var dischargeRate = GetInt32(status, "DischargeRate");
        var powerRate = charging
            ? chargeRate
            : discharging
                ? Negate(dischargeRate)
                : chargeRate is 0 && dischargeRate is 0
                    ? 0
                    : null;

        return new BatterySnapshot(
            DateTimeOffset.Now,
            Coalesce(GetString(battery, "Name"), report?.Name, "Battery"),
            report?.Manufacturer ?? string.Empty,
            report?.Chemistry ?? string.Empty,
            chargePercent,
            powerState,
            powerOnline,
            remaining,
            design,
            full,
            report?.CycleCount ?? GetInt32(cycleCount, "CycleCount"),
            GetInt32(status, "Voltage") ?? GetInt32(battery, "DesignVoltage"),
            powerRate,
            true,
            errors.Count > 0 ? errors[0] : null);
    }

    private IReadOnlyDictionary<string, object?> QueryFirst(
        string namespacePath,
        string query,
        List<string> errors)
    {
        try
        {
            return _wmi.QueryFirst(namespacePath, query);
        }
        catch (Exception exception)
        {
            errors.Add($"Battery telemetry: {exception.Message}");
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static BatteryPowerState GetPowerState(
        int? chargePercent,
        bool? powerOnline,
        bool charging,
        bool discharging,
        int? batteryStatus)
    {
        if (charging)
        {
            return BatteryPowerState.Charging;
        }

        if (discharging)
        {
            return BatteryPowerState.Discharging;
        }

        if (powerOnline == true || chargePercent is >= 99 || batteryStatus == 3)
        {
            return chargePercent is >= 99 || batteryStatus == 3
                ? BatteryPowerState.FullyCharged
                : BatteryPowerState.Connected;
        }

        return powerOnline == false
            ? BatteryPowerState.OnBattery
            : BatteryPowerState.Unknown;
    }

    private static int? CalculatePercent(int? value, int? maximum) =>
        value is >= 0 && maximum is > 0
            ? (int)Math.Round(
                100d * value.Value / maximum.Value,
                MidpointRounding.AwayFromZero)
            : null;

    private static int? Negate(int? value) => value.HasValue ? -value.Value : null;

    private static string Coalesce(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string GetString(
        IReadOnlyDictionary<string, object?> values,
        string name) =>
        values.TryGetValue(name, out var value)
            ? value?.ToString()?.Trim() ?? string.Empty
            : string.Empty;

    private static int? GetInt32(
        IReadOnlyDictionary<string, object?> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (
            exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static bool? GetBoolean(
        IReadOnlyDictionary<string, object?> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (
            exception is FormatException or InvalidCastException)
        {
            return null;
        }
    }
}
