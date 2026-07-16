using System.ComponentModel;
using System.Runtime.CompilerServices;
using AeroControl.Core.Abstractions;
using AeroControl.Core.Models;

namespace AeroControl.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAeroHardwareService _hardware;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
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

    public MainViewModel(
        IAeroHardwareService hardware,
        bool isDemo,
        bool isElevated)
    {
        _hardware = hardware;
        IsDemo = isDemo;
        IsElevated = isElevated;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var identity = await _hardware.GetDeviceIdentityAsync(cancellationToken);
            DeviceName = string.IsNullOrWhiteSpace(identity.DisplayName)
                ? "Unknown Gigabyte device"
                : identity.DisplayName;
            DeviceDetails = $"BIOS {identity.BiosVersion}  |  {identity.SupportLabel}";

            var capabilities = await _hardware.GetCapabilitiesAsync(cancellationToken);
            CanControlFans = capabilities.CanControlFans;
            CapabilitySummary = capabilities.CanControlFans
                ? $"{capabilities.GetMethods.Count} read methods, {capabilities.SetMethods.Count} write methods"
                : "Fan write methods unavailable";
        }
        catch (Exception exception)
        {
            DeviceDetails = "Hardware discovery unavailable";
            CapabilitySummary = "Firmware interface unavailable";
            StatusMessage = exception.Message;
        }

        await RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var snapshot = await _hardware.GetSnapshotAsync(cancellationToken);
            ApplySnapshot(snapshot);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task<ControlResult> SetFanPercentAsync(
        int percent,
        CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusMessage = $"Applying fixed {percent}% fan duty...";
        try
        {
            var result = await _hardware.SetFixedFanPercentAsync(percent, cancellationToken);
            StatusMessage = result.Message;
            if (result.Succeeded)
            {
                CustomFanPercent = percent;
                await RefreshAsync(cancellationToken);
            }

            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<ControlResult> RestoreAutomaticAsync(
        CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusMessage = "Restoring automatic firmware fan control...";
        try
        {
            var result = await _hardware.RestoreAutomaticFanControlAsync(cancellationToken);
            StatusMessage = result.Message;
            if (result.Succeeded)
            {
                await RefreshAsync(cancellationToken);
            }

            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplySnapshot(HardwareSnapshot snapshot)
    {
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
    }

    private static string FormatTemperature(int? value) => value is int temperature
        ? $"{temperature} C"
        : "-- C";

    private static string FormatRpm(int? value) => value is int rpm
        ? $"{rpm:N0} RPM"
        : "---- RPM";

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

        _disposed = true;
        _refreshGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
