using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using AeroControl.Core.Abstractions;
using AeroControl.Core.Models;
using AeroControl.Core.Services;
using AeroControl.Services;

namespace AeroControl.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable, IAsyncDisposable
{
    private readonly IAeroHardwareService _hardware;
    private readonly IBatteryService _battery;
    private readonly TelemetryHistory _history;
    private readonly DiagnosticsService _diagnostics;
    private readonly SemaphoreSlim _coolingRefreshGate = new(1, 1);
    private readonly SemaphoreSlim _batteryRefreshGate = new(1, 1);
    private string _deviceName = "Detecting hardware";
    private string _deviceDetails = "Firmware interface pending";
    private string _cpuTemperature = "-- C";
    private string _gpuTemperature = "-- C";
    private string _fan1Speed = "---- RPM";
    private string _fan2Speed = "---- RPM";
    private string _fanDuty = "--%";
    private string _fanMode = "UNKNOWN";
    private string _fanHealth = "Unknown";
    private string _connectionState = "Connecting";
    private string _capabilitySummary = "Checking firmware methods";
    private string _statusMessage = "Reading Gigabyte firmware telemetry...";
    private string _lastUpdated = "Not sampled";
    private double _customFanPercent = 80;
    private bool _isBusy;
    private bool _canControlFans;
    private bool _disposed;
    private string _hardwareKey = string.Empty;
    private string _batteryName = "Detecting battery";
    private string _batteryCharge = "--%";
    private string _batteryRetention = "--%";
    private string _batteryState = "Unknown";
    private string _batteryRemaining = "-- Wh";
    private string _batteryFullCapacity = "-- Wh";
    private string _batteryDesignCapacity = "-- Wh";
    private string _batteryCycleCount = "--";
    private string _batteryVoltage = "-- V";
    private string _batteryPowerFlow = "-- W";
    private string _batteryPowerSource = "Unknown";
    private string _batteryConnectionState = "Checking Windows battery data";
    private string _batteryStatusMessage = "Reading standard Windows battery telemetry...";
    private string _batteryLastUpdated = "Not sampled";
    private double _batteryChargeValue;
    private double _batteryRetentionValue;
    private HardwareSnapshot _latestHardware = HardwareSnapshot.Unavailable("Not sampled");
    private BatterySnapshot _latestBattery = BatterySnapshot.Unavailable("Not sampled");
    private IReadOnlyList<double?> _cpuHistory = [];
    private IReadOnlyList<double?> _gpuHistory = [];
    private IReadOnlyList<double?> _fan1History = [];
    private IReadOnlyList<double?> _fan2History = [];
    private IReadOnlyList<double?> _batteryHistory = [];
    private IReadOnlyList<double?> _powerHistory = [];
    private string _monitorSummary = "Waiting for samples";
    private string _diagnosticsDevice = "Not collected";
    private string _diagnosticsSoftware = "Not collected";
    private string _diagnosticsSignature = "Not collected";
    private string _diagnosticsConflicts = "Not collected";
    private string _diagnosticsReadMethods = "Not collected";
    private string _diagnosticsWriteMethods = "Not collected";
    private string _diagnosticsStatus = "Collecting sanitized compatibility data...";
    private DiagnosticsReport? _latestDiagnostics;
    private int _controlInProgress;
    private bool _requiresAutomaticRestore;

    public MainViewModel(
        IAeroHardwareService hardware,
        IBatteryService battery,
        bool isDemo,
        bool isElevated)
        : this(
            hardware,
            battery,
            new TelemetryHistory(),
            new DiagnosticsService(hardware),
            UserPreferences.Default,
            isDemo,
            isElevated)
    {
    }

    public MainViewModel(
        IAeroHardwareService hardware,
        IBatteryService battery,
        TelemetryHistory history,
        DiagnosticsService diagnostics,
        UserPreferences preferences,
        bool isDemo,
        bool isElevated)
    {
        _hardware = hardware;
        _battery = battery;
        _history = history;
        _diagnostics = diagnostics;
        Settings = new SettingsViewModel(preferences);
        IsDemo = isDemo;
        IsElevated = isElevated;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<HardwareSnapshot>? HardwareSnapshotUpdated;

    public SettingsViewModel Settings { get; }

    public bool IsDemo { get; }

    public bool IsElevated { get; }

    public string EnvironmentLabel => IsDemo ? "DEMO DATA" : "LIVE HARDWARE";

    public string PrivilegeLabel => IsDemo
        ? "Writes simulated"
        : IsElevated
            ? "Administrator access"
            : "Monitoring only; elevation required for writes";

    public string DeviceName
    {
        get => _deviceName;
        private set => SetField(ref _deviceName, value);
    }

    public string DeviceDetails
    {
        get => _deviceDetails;
        private set => SetField(ref _deviceDetails, value);
    }

    public string CpuTemperature
    {
        get => _cpuTemperature;
        private set => SetField(ref _cpuTemperature, value);
    }

    public string GpuTemperature
    {
        get => _gpuTemperature;
        private set => SetField(ref _gpuTemperature, value);
    }

    public string Fan1Speed
    {
        get => _fan1Speed;
        private set => SetField(ref _fan1Speed, value);
    }

    public string Fan2Speed
    {
        get => _fan2Speed;
        private set => SetField(ref _fan2Speed, value);
    }

    public string FanDuty
    {
        get => _fanDuty;
        private set => SetField(ref _fanDuty, value);
    }

    public string FanMode
    {
        get => _fanMode;
        private set => SetField(ref _fanMode, value);
    }

    public string FanHealth
    {
        get => _fanHealth;
        private set => SetField(ref _fanHealth, value);
    }

    public string ConnectionState
    {
        get => _connectionState;
        private set => SetField(ref _connectionState, value);
    }

    public string CapabilitySummary
    {
        get => _capabilitySummary;
        private set => SetField(ref _capabilitySummary, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string LastUpdated
    {
        get => _lastUpdated;
        private set => SetField(ref _lastUpdated, value);
    }

    public double CustomFanPercent
    {
        get => _customFanPercent;
        set
        {
            var rounded = Math.Round(value);
            if (SetField(ref _customFanPercent, rounded))
            {
                OnPropertyChanged(nameof(CustomFanLabel));
            }
        }
    }

    public string CustomFanLabel => $"{CustomFanPercent:0}%";

    public string HardwareKey
    {
        get => _hardwareKey;
        private set => SetField(ref _hardwareKey, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public bool CanControlFans
    {
        get => _canControlFans;
        private set => SetField(ref _canControlFans, value);
    }

    public string BatteryName
    {
        get => _batteryName;
        private set => SetField(ref _batteryName, value);
    }

    public string BatteryCharge
    {
        get => _batteryCharge;
        private set => SetField(ref _batteryCharge, value);
    }

    public string BatteryRetention
    {
        get => _batteryRetention;
        private set => SetField(ref _batteryRetention, value);
    }

    public string BatteryState
    {
        get => _batteryState;
        private set => SetField(ref _batteryState, value);
    }

    public string BatteryRemaining
    {
        get => _batteryRemaining;
        private set => SetField(ref _batteryRemaining, value);
    }

    public string BatteryFullCapacity
    {
        get => _batteryFullCapacity;
        private set => SetField(ref _batteryFullCapacity, value);
    }

    public string BatteryDesignCapacity
    {
        get => _batteryDesignCapacity;
        private set => SetField(ref _batteryDesignCapacity, value);
    }

    public string BatteryCycleCount
    {
        get => _batteryCycleCount;
        private set => SetField(ref _batteryCycleCount, value);
    }

    public string BatteryVoltage
    {
        get => _batteryVoltage;
        private set => SetField(ref _batteryVoltage, value);
    }

    public string BatteryPowerFlow
    {
        get => _batteryPowerFlow;
        private set => SetField(ref _batteryPowerFlow, value);
    }

    public string BatteryPowerSource
    {
        get => _batteryPowerSource;
        private set => SetField(ref _batteryPowerSource, value);
    }

    public string BatteryConnectionState
    {
        get => _batteryConnectionState;
        private set => SetField(ref _batteryConnectionState, value);
    }

    public string BatteryStatusMessage
    {
        get => _batteryStatusMessage;
        private set => SetField(ref _batteryStatusMessage, value);
    }

    public string BatteryLastUpdated
    {
        get => _batteryLastUpdated;
        private set => SetField(ref _batteryLastUpdated, value);
    }

    public double BatteryChargeValue
    {
        get => _batteryChargeValue;
        private set => SetField(ref _batteryChargeValue, value);
    }

    public double BatteryRetentionValue
    {
        get => _batteryRetentionValue;
        private set => SetField(ref _batteryRetentionValue, value);
    }

    public IReadOnlyList<double?> CpuHistory
    {
        get => _cpuHistory;
        private set => SetField(ref _cpuHistory, value);
    }

    public IReadOnlyList<double?> GpuHistory
    {
        get => _gpuHistory;
        private set => SetField(ref _gpuHistory, value);
    }

    public IReadOnlyList<double?> Fan1History
    {
        get => _fan1History;
        private set => SetField(ref _fan1History, value);
    }

    public IReadOnlyList<double?> Fan2History
    {
        get => _fan2History;
        private set => SetField(ref _fan2History, value);
    }

    public IReadOnlyList<double?> BatteryHistory
    {
        get => _batteryHistory;
        private set => SetField(ref _batteryHistory, value);
    }

    public IReadOnlyList<double?> PowerHistory
    {
        get => _powerHistory;
        private set => SetField(ref _powerHistory, value);
    }

    public string MonitorSummary
    {
        get => _monitorSummary;
        private set => SetField(ref _monitorSummary, value);
    }

    public string DiagnosticsDevice
    {
        get => _diagnosticsDevice;
        private set => SetField(ref _diagnosticsDevice, value);
    }

    public string DiagnosticsSoftware
    {
        get => _diagnosticsSoftware;
        private set => SetField(ref _diagnosticsSoftware, value);
    }

    public string DiagnosticsSignature
    {
        get => _diagnosticsSignature;
        private set => SetField(ref _diagnosticsSignature, value);
    }

    public string DiagnosticsConflicts
    {
        get => _diagnosticsConflicts;
        private set => SetField(ref _diagnosticsConflicts, value);
    }

    public string DiagnosticsReadMethods
    {
        get => _diagnosticsReadMethods;
        private set => SetField(ref _diagnosticsReadMethods, value);
    }

    public string DiagnosticsWriteMethods
    {
        get => _diagnosticsWriteMethods;
        private set => SetField(ref _diagnosticsWriteMethods, value);
    }

    public string DiagnosticsStatus
    {
        get => _diagnosticsStatus;
        private set => SetField(ref _diagnosticsStatus, value);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var identity = await _hardware.GetDeviceIdentityAsync(cancellationToken);
            DeviceName = string.IsNullOrWhiteSpace(identity.DisplayName)
                ? "Unknown Gigabyte device"
                : identity.DisplayName;
            DeviceDetails = $"SKU {identity.SystemSku}  |  BIOS {identity.BiosVersion}  |  {identity.SupportLabel}";
            HardwareKey = identity.HardwareKey;

            var capabilities = await _hardware.GetCapabilitiesAsync(cancellationToken);
            CanControlFans = capabilities.CanControlFans && (IsDemo || identity.IsVerifiedConfiguration);
            CapabilitySummary = CanControlFans
                ? $"{capabilities.GetMethods.Count} read methods, {capabilities.SetMethods.Count} write methods"
                : capabilities.CanControlFans
                    ? "Write methods detected; configuration unverified"
                    : "Fan write or readback methods unavailable";
        }
        catch (Exception exception)
        {
            DeviceDetails = "Hardware discovery unavailable";
            CapabilitySummary = "Firmware interface unavailable";
            StatusMessage = exception.Message;
        }

        await RefreshAsync(cancellationToken);
        await RefreshDiagnosticsAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var refreshed = await Task.WhenAll(
            RefreshCoolingAsync(false, cancellationToken),
            RefreshBatteryAsync(false, cancellationToken));
        if (refreshed.All(value => value))
        {
            RecordHistorySample();
        }
    }

    public void UpdateMonitorWindow()
    {
        var samples = _history.GetRecent(Settings.HistoryMinutes);
        CpuHistory = samples.Select(sample => ToDouble(sample.CpuTemperatureCelsius)).ToArray();
        GpuHistory = samples.Select(sample => ToDouble(sample.GpuTemperatureCelsius)).ToArray();
        Fan1History = samples.Select(sample => ToDouble(sample.Fan1Rpm)).ToArray();
        Fan2History = samples.Select(sample => ToDouble(sample.Fan2Rpm)).ToArray();
        BatteryHistory = samples.Select(sample => ToDouble(sample.BatteryChargePercent)).ToArray();
        PowerHistory = samples
            .Select(sample => sample.BatteryPowerMilliwatts.HasValue
                ? Math.Abs(sample.BatteryPowerMilliwatts.Value) / 1000d
                : (double?)null)
            .ToArray();
        MonitorSummary = samples.Count == 0
            ? $"Waiting for samples | {Settings.HistoryMinutes}-minute window"
            : $"{samples.Count} samples | {Settings.HistoryMinutes}-minute window | session only";
    }

    public string GetHistoryCsv() => _history.ToCsv(Settings.HistoryMinutes);

    public async Task RefreshDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        DiagnosticsStatus = "Collecting sanitized compatibility data...";
        DiagnosticsReport report;
        try
        {
            report = await _diagnostics.CaptureAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            DiagnosticsStatus = "Sanitized diagnostics are unavailable.";
            DiagnosticsDevice = "Unavailable";
            DiagnosticsSoftware = "Unavailable";
            DiagnosticsSignature = "Unavailable";
            DiagnosticsConflicts = "Unavailable";
            DiagnosticsReadMethods = "Unavailable";
            DiagnosticsWriteMethods = "Unavailable";
            _latestDiagnostics = null;
            return;
        }
        _latestDiagnostics = report;
        DiagnosticsDevice = string.Join(
            " | ",
            new[]
            {
                report.Manufacturer,
                report.Model,
                $"SKU {report.SystemSku}",
                $"BIOS {report.BiosVersion}"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        DiagnosticsSoftware = $"AeroControl {report.ApplicationVersion} | {report.OperatingSystem} | " +
            $"{report.ProcessArchitecture} | {report.Framework}";
        DiagnosticsSignature = $"{report.SignatureStatus} | SHA-256 {report.ExecutableSha256}";
        DiagnosticsConflicts = report.ConflictingProcesses.Count == 0
            ? "No known competing fan-control applications detected"
            : string.Join(", ", report.ConflictingProcesses);
        DiagnosticsReadMethods = report.FirmwareReadMethods.Count == 0
            ? "No read methods available"
            : string.Join(", ", report.FirmwareReadMethods);
        DiagnosticsWriteMethods = report.FirmwareWriteMethods.Count == 0
            ? "No write methods available"
            : string.Join(", ", report.FirmwareWriteMethods);
        DiagnosticsStatus = report.CollectionErrors.Count == 0
            ? "Sanitized diagnostics ready. Export excludes serials, UUIDs, MAC addresses, usernames, and raw paths."
            : string.Join(" | ", report.CollectionErrors);
    }

    public async Task<string> GetDiagnosticsJsonAsync(CancellationToken cancellationToken = default)
    {
        if (_latestDiagnostics is null)
        {
            await RefreshDiagnosticsAsync(cancellationToken);
        }

        return GetCachedDiagnosticsJson();
    }

    internal string GetCachedDiagnosticsJson() => _latestDiagnostics is null
        ? "{\"Status\":\"Sanitized diagnostics are unavailable.\"}"
        : DiagnosticsService.ToJson(_latestDiagnostics);

    private async Task<HardwareSnapshot> GetHardwareSnapshotSafelyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _hardware.GetSnapshotAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return HardwareSnapshot.Unavailable(exception.Message);
        }
    }

    private async Task<BatterySnapshot> GetBatterySnapshotSafelyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _battery.GetSnapshotAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return BatterySnapshot.Unavailable(exception.Message);
        }
    }

    public async Task<ControlResult> SetFanPercentAsync(
        int percent,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _controlInProgress, 1, 0) != 0)
        {
            return ControlResult.Failure("Another fan command is already running.", _requiresAutomaticRestore);
        }

        IsBusy = true;
        StatusMessage = $"Applying fixed {percent}% fan duty...";
        try
        {
            var result = await _hardware.SetFixedFanPercentAsync(percent, cancellationToken);
            _requiresAutomaticRestore = result.RequiresAutomaticRestore;
            StatusMessage = result.Message;
            if (result.Succeeded)
            {
                CustomFanPercent = percent;
                await RefreshCoolingAsync(true, cancellationToken);
            }

            return result;
        }
        finally
        {
            IsBusy = false;
            Volatile.Write(ref _controlInProgress, 0);
        }
    }

    public async Task<ControlResult> RestoreAutomaticAsync(
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _controlInProgress, 1, 0) != 0)
        {
            return ControlResult.Failure("Another fan command is already running.", _requiresAutomaticRestore);
        }

        IsBusy = true;
        StatusMessage = "Restoring automatic firmware fan control...";
        try
        {
            var result = await _hardware.RestoreAutomaticFanControlAsync(cancellationToken);
            _requiresAutomaticRestore = result.RequiresAutomaticRestore;
            StatusMessage = result.Message;
            if (result.Succeeded)
            {
                await RefreshCoolingAsync(true, cancellationToken);
            }

            return result;
        }
        finally
        {
            IsBusy = false;
            Volatile.Write(ref _controlInProgress, 0);
        }
    }

    public void ReportStatus(string message)
    {
        StatusMessage = message;
    }

    private async Task<bool> RefreshCoolingAsync(
        bool waitForTurn,
        CancellationToken cancellationToken)
    {
        if (!await EnterRefreshAsync(_coolingRefreshGate, waitForTurn, cancellationToken))
        {
            return false;
        }

        try
        {
            ApplySnapshot(await GetHardwareSnapshotSafelyAsync(cancellationToken));
            return true;
        }
        finally
        {
            _coolingRefreshGate.Release();
        }
    }

    private async Task<bool> RefreshBatteryAsync(
        bool waitForTurn,
        CancellationToken cancellationToken)
    {
        if (!await EnterRefreshAsync(_batteryRefreshGate, waitForTurn, cancellationToken))
        {
            return false;
        }

        try
        {
            ApplyBatterySnapshot(await GetBatterySnapshotSafelyAsync(cancellationToken));
            return true;
        }
        finally
        {
            _batteryRefreshGate.Release();
        }
    }

    private static async Task<bool> EnterRefreshAsync(
        SemaphoreSlim gate,
        bool waitForTurn,
        CancellationToken cancellationToken)
    {
        if (waitForTurn)
        {
            await gate.WaitAsync(cancellationToken);
            return true;
        }

        return await gate.WaitAsync(0, cancellationToken);
    }

    private void RecordHistorySample()
    {
        _history.Record(_latestHardware, _latestBattery);
        UpdateMonitorWindow();
    }

    private void ApplySnapshot(HardwareSnapshot snapshot)
    {
        _latestHardware = snapshot;
        CpuTemperature = FormatTemperature(snapshot.CpuTemperatureCelsius);
        GpuTemperature = FormatTemperature(snapshot.GpuTemperatureCelsius);
        Fan1Speed = FormatRpm(snapshot.Fan1Rpm);
        Fan2Speed = FormatRpm(snapshot.Fan2Rpm);
        FanDuty = snapshot.CpuFanDutyPercent is int duty ? $"{duty}%" : "--%";
        FanMode = snapshot.FanMode.ToString().ToUpperInvariant();
        FanHealth = snapshot.FanHealthGood switch
        {
            true => "Good",
            false => "Attention",
            null => "Unknown"
        };
        ConnectionState = snapshot.IsConnected ? "Firmware online" : "Firmware unavailable";
        LastUpdated = snapshot.IsConnected
            ? $"Updated {snapshot.CapturedAt:HH:mm:ss}"
            : "No live sample";
        StatusMessage = snapshot.ErrorMessage ?? (snapshot.IsConnected
            ? "Telemetry is live. Firmware writes remain risk-gated."
            : "Restart as administrator if this model requires elevated WMI access.");
        HardwareSnapshotUpdated?.Invoke(snapshot);
    }

    private void ApplyBatterySnapshot(BatterySnapshot snapshot)
    {
        _latestBattery = snapshot;
        BatteryName = string.IsNullOrWhiteSpace(snapshot.Name)
            ? "Windows battery"
            : snapshot.Name;
        BatteryCharge = FormatPercent(snapshot.ChargePercent);
        BatteryChargeValue = ClampPercent(snapshot.ChargePercent);
        BatteryRetention = FormatPercent(snapshot.CapacityRetentionPercent);
        BatteryRetentionValue = ClampPercent(snapshot.CapacityRetentionPercent);
        BatteryState = FormatBatteryState(snapshot.PowerState);
        BatteryRemaining = FormatCapacity(snapshot.RemainingCapacityMilliwattHours);
        BatteryFullCapacity = FormatCapacity(snapshot.FullChargeCapacityMilliwattHours);
        BatteryDesignCapacity = FormatCapacity(snapshot.DesignCapacityMilliwattHours);
        BatteryCycleCount = snapshot.CycleCount?.ToString(CultureInfo.InvariantCulture) ?? "--";
        BatteryVoltage = snapshot.VoltageMillivolts is int voltage
            ? $"{voltage / 1000d:0.00} V"
            : "-- V";
        BatteryPowerFlow = FormatPower(snapshot.PowerRateMilliwatts);
        BatteryPowerSource = snapshot.PowerOnline switch
        {
            true => "AC power",
            false => "Battery power",
            null => "Unknown source"
        };
        BatteryConnectionState = snapshot.IsConnected
            ? "Windows battery online"
            : "Battery unavailable";
        BatteryLastUpdated = snapshot.IsConnected
            ? $"Updated {snapshot.CapturedAt:HH:mm:ss}"
            : "No live sample";
        BatteryStatusMessage = snapshot.ErrorMessage ?? (snapshot.IsConnected
            ? "Read-only Windows telemetry. AeroControl does not change battery firmware settings."
            : "Windows did not expose battery telemetry on this system.");
    }

    private static string FormatTemperature(int? value) => value is int temperature
        ? $"{temperature} C"
        : "-- C";

    private static string FormatRpm(int? value) => value is int rpm
        ? $"{rpm:N0} RPM"
        : "---- RPM";

    private static string FormatPercent(int? value) => value is int percent
        ? $"{percent}%"
        : "--%";

    private static double ClampPercent(int? value) => Math.Clamp(value ?? 0, 0, 100);

    private static double? ToDouble(int? value) => value.HasValue ? value.Value : null;

    private static string FormatCapacity(int? value) => value is int capacity
        ? $"{capacity / 1000d:0.0} Wh"
        : "-- Wh";

    private static string FormatPower(int? value) => value switch
    {
        null => "-- W",
        0 => "Idle",
        > 0 => $"+{value.Value / 1000d:0.0} W",
        _ => $"{value.Value / 1000d:0.0} W"
    };

    private static string FormatBatteryState(BatteryPowerState state) => state switch
    {
        BatteryPowerState.FullyCharged => "Fully charged",
        BatteryPowerState.OnBattery => "On battery",
        _ => state.ToString()
    };

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _battery.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
