using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AeroControl.Services;

namespace AeroControl.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private bool _startWithWindows;
    private bool _minimizeToTray;
    private bool _enableNotifications;
    private int _cpuAlertCelsius;
    private int _gpuAlertCelsius;
    private bool _enableFanStallAlert;
    private int _refreshIntervalSeconds;
    private int _historyMinutes;
    private bool _rememberLastView;
    private bool _restoreAutomaticOnExit;

    public SettingsViewModel(UserPreferences preferences)
    {
        var normalized = preferences.Normalize();
        _startWithWindows = normalized.StartWithWindows;
        _minimizeToTray = normalized.MinimizeToTray;
        _enableNotifications = normalized.EnableNotifications;
        _cpuAlertCelsius = normalized.CpuAlertCelsius;
        _gpuAlertCelsius = normalized.GpuAlertCelsius;
        _enableFanStallAlert = normalized.EnableFanStallAlert;
        _refreshIntervalSeconds = normalized.RefreshIntervalSeconds;
        _historyMinutes = normalized.HistoryMinutes;
        _rememberLastView = normalized.RememberLastView;
        _restoreAutomaticOnExit = normalized.RestoreAutomaticOnExit;
        LastView = normalized.LastView;
        FanProfiles = new ObservableCollection<FanProfile>(normalized.FanProfiles);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FanProfile> FanProfiles { get; }

    public string LastView { get; set; }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetField(ref _startWithWindows, value);
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetField(ref _minimizeToTray, value);
    }

    public bool EnableNotifications
    {
        get => _enableNotifications;
        set => SetField(ref _enableNotifications, value);
    }

    public int CpuAlertCelsius
    {
        get => _cpuAlertCelsius;
        set => SetField(ref _cpuAlertCelsius, value);
    }

    public int GpuAlertCelsius
    {
        get => _gpuAlertCelsius;
        set => SetField(ref _gpuAlertCelsius, value);
    }

    public bool EnableFanStallAlert
    {
        get => _enableFanStallAlert;
        set => SetField(ref _enableFanStallAlert, value);
    }

    public int RefreshIntervalSeconds
    {
        get => _refreshIntervalSeconds;
        set => SetField(ref _refreshIntervalSeconds, value);
    }

    public int HistoryMinutes
    {
        get => _historyMinutes;
        set => SetField(ref _historyMinutes, value);
    }

    public bool RememberLastView
    {
        get => _rememberLastView;
        set => SetField(ref _rememberLastView, value);
    }

    public bool RestoreAutomaticOnExit
    {
        get => _restoreAutomaticOnExit;
        set => SetField(ref _restoreAutomaticOnExit, value);
    }

    public UserPreferences ToPreferences() => new UserPreferences
    {
        StartWithWindows = StartWithWindows,
        MinimizeToTray = MinimizeToTray,
        EnableNotifications = EnableNotifications,
        CpuAlertCelsius = CpuAlertCelsius,
        GpuAlertCelsius = GpuAlertCelsius,
        EnableFanStallAlert = EnableFanStallAlert,
        RefreshIntervalSeconds = RefreshIntervalSeconds,
        HistoryMinutes = HistoryMinutes,
        RememberLastView = RememberLastView,
        LastView = LastView,
        RestoreAutomaticOnExit = RestoreAutomaticOnExit,
        FanProfiles = FanProfiles.ToArray()
    }.Normalize();

    public void Apply(UserPreferences preferences)
    {
        var normalized = preferences.Normalize();
        StartWithWindows = normalized.StartWithWindows;
        MinimizeToTray = normalized.MinimizeToTray;
        EnableNotifications = normalized.EnableNotifications;
        CpuAlertCelsius = normalized.CpuAlertCelsius;
        GpuAlertCelsius = normalized.GpuAlertCelsius;
        EnableFanStallAlert = normalized.EnableFanStallAlert;
        RefreshIntervalSeconds = normalized.RefreshIntervalSeconds;
        HistoryMinutes = normalized.HistoryMinutes;
        RememberLastView = normalized.RememberLastView;
        RestoreAutomaticOnExit = normalized.RestoreAutomaticOnExit;
        LastView = normalized.LastView;
        FanProfiles.Clear();
        foreach (var profile in normalized.FanProfiles)
        {
            FanProfiles.Add(profile);
        }
    }

    public bool AddProfile(string name, int percent)
    {
        var normalized = new FanProfile(name, percent);
        var preferences = ToPreferences() with
        {
            FanProfiles = FanProfiles.Append(normalized).ToArray()
        };
        var updated = preferences.Normalize().FanProfiles;
        if (updated.Count == FanProfiles.Count)
        {
            return false;
        }

        FanProfiles.Clear();
        foreach (var profile in updated)
        {
            FanProfiles.Add(profile);
        }

        return true;
    }

    public bool RemoveProfile(FanProfile profile) => FanProfiles.Remove(profile);

    public void ReplaceProfiles(IEnumerable<FanProfile> profiles)
    {
        FanProfiles.Clear();
        foreach (var profile in profiles)
        {
            FanProfiles.Add(profile);
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
