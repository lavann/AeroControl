using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AeroControl.Core.Abstractions;
using AeroControl.Services;
using AeroControl.ViewModels;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace AeroControl;

public partial class MainWindow : Window, IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly AppSettingsStore _settings;
    private readonly StartupRegistrationService _startup = new();
    private readonly TrayIconService _tray = new();
    private readonly Core.Services.AlertEvaluator _alerts = new();
    private readonly bool _isDemo;
    private readonly bool _isElevated;
    private readonly string? _capturePath;
    private readonly AppView _initialView;
    private readonly DispatcherTimer _refreshTimer;
    private readonly AsyncCloseGate _closeGate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private bool _automaticRestoreRequired;
    private bool _allowFinalClose;
    private bool _disposed;
    private bool _demoRiskAccepted;
    private Task<Core.Models.ControlResult>? _activeControlTask;
    private UserPreferences _persistedPreferences;

    internal MainWindow(
        IAeroHardwareService hardware,
        IBatteryService battery,
        AppSettingsStore settings,
        UserPreferences preferences,
        bool isDemo,
        string? capturePath,
        AppView initialView)
    {
        InitializeComponent();
        _settings = settings;
        _isDemo = isDemo;
        _isElevated = ElevationService.IsAdministrator();
        _capturePath = capturePath;
        _initialView = initialView;
        var startupEnabled = string.IsNullOrWhiteSpace(capturePath) &&
            _startup.IsEnabled(Environment.ProcessPath ?? string.Empty);
        _persistedPreferences = preferences.Normalize() with
        {
            StartWithWindows = startupEnabled
        };
        _viewModel = new MainViewModel(
            hardware,
            battery,
            new Core.Services.TelemetryHistory(),
            new DiagnosticsService(
                hardware,
                processInventory: string.IsNullOrWhiteSpace(capturePath) ? null : () => []),
            _persistedPreferences,
            isDemo,
            _isElevated);
        DataContext = _viewModel;
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_persistedPreferences.RefreshIntervalSeconds)
        };
        _refreshTimer.Tick += RefreshTimer_Tick;
        _viewModel.HardwareSnapshotUpdated += ViewModel_HardwareSnapshotUpdated;
        _tray.OpenRequested += Tray_OpenRequested;
        _tray.ExitRequested += Tray_ExitRequested;
        _tray.SetVisible(
            string.IsNullOrWhiteSpace(_capturePath) &&
            (_persistedPreferences.MinimizeToTray || _persistedPreferences.EnableNotifications));
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync(_shutdown.Token);
            MainTabs.SelectedIndex = (int)_initialView;

            UpdateAccessState();
            _refreshTimer.Start();

            if (!string.IsNullOrWhiteSpace(_capturePath))
            {
                await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.Loaded);
                await Task.Delay(250, _shutdown.Token);
                if (MainTabs.SelectedIndex != (int)_initialView)
                {
                    throw new InvalidOperationException(
                        $"AeroControl could not select the requested {_initialView} view.");
                }

                CaptureWindow(_capturePath);
                Application.Current.Shutdown();
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Normal window shutdown while startup telemetry is still loading.
        }
    }

    private async void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !ReferenceEquals(e.OriginalSource, MainTabs))
        {
            return;
        }

        var view = (AppView)Math.Clamp(MainTabs.SelectedIndex, 0, Enum.GetValues<AppView>().Length - 1);
        _viewModel.Settings.LastView = view.ToString();
        if (view == AppView.Monitor)
        {
            _viewModel.UpdateMonitorWindow();
        }
        else if (view == AppView.Diagnostics)
        {
            try
            {
                await _viewModel.RefreshDiagnosticsAsync(_shutdown.Token);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
        }

        if (_persistedPreferences.RememberLastView && string.IsNullOrWhiteSpace(_capturePath))
        {
            var preferences = _persistedPreferences with { LastView = view.ToString() };
            if (_settings.SavePreferences(preferences))
            {
                _persistedPreferences = preferences.Normalize();
            }
        }
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            await _viewModel.RefreshAsync(_shutdown.Token);
        }
        catch (OperationCanceledException)
        {
            // The window is shutting down.
        }
    }

    private void ViewModel_HardwareSnapshotUpdated(Core.Models.HardwareSnapshot snapshot)
    {
        if (!_viewModel.Settings.EnableNotifications)
        {
            _alerts.Reset();
            return;
        }

        var thresholds = new Core.Models.AlertThresholds(
            _viewModel.Settings.CpuAlertCelsius,
            _viewModel.Settings.GpuAlertCelsius,
            _viewModel.Settings.EnableFanStallAlert);
        foreach (var alert in _alerts.Evaluate(snapshot, thresholds))
        {
            _tray.SetVisible(true);
            _tray.ShowAlert(alert.Title, alert.Message);
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _viewModel.Settings.MinimizeToTray)
        {
            Hide();
            _tray.SetVisible(true);
        }
    }

    private void Tray_OpenRequested(object? sender, EventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Tray_ExitRequested(object? sender, EventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        Close();
    }

    private void ExportHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfSaveFileDialog
        {
            Title = "Export AeroControl session history",
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"AeroControl-history-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        TryWriteExport(dialog.FileName, _viewModel.GetHistoryCsv(), "History export");
    }

    private async void RefreshDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.RefreshDiagnosticsAsync(_shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async void ExportDiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfSaveFileDialog
        {
            Title = "Export sanitized AeroControl diagnostics",
            Filter = "JSON files (*.json)|*.json",
            FileName = $"AeroControl-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            TryWriteExport(
                dialog.FileName,
                await _viewModel.GetDiagnosticsJsonAsync(_shutdown.Token),
                "Diagnostics export");
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async void ApplyProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy || _activeControlTask is not null)
        {
            ProfilesStatusText.Text = "A fan command is already running.";
            return;
        }

        if (ProfilesList.SelectedItem is not FanProfile profile || !EnsureWriteAccess())
        {
            ProfilesStatusText.Text = "Select a profile before applying it.";
            return;
        }

        var result = await TrackControlAsync(
            _viewModel.SetFanPercentAsync(profile.Percent, _shutdown.Token));
        ProfilesStatusText.Text = result.Message;
    }

    private void AddProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.Settings.AddProfile(
                ProfileNameTextBox.Text,
                (int)Math.Round(ProfilePercentSlider.Value)))
        {
            ProfilesStatusText.Text = "Use a unique non-empty profile name (maximum 12 profiles).";
            return;
        }

        ProfileNameTextBox.Clear();
        PersistProfiles("Profile saved.");
    }

    private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is not FanProfile profile ||
            !_viewModel.Settings.RemoveProfile(profile))
        {
            ProfilesStatusText.Text = "Select a profile to delete.";
            return;
        }

        PersistProfiles("Profile deleted.");
    }

    private void ProfilePercentSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ProfilePercentLabel is not null)
        {
            ProfilePercentLabel.Text = $"{Math.Round(e.NewValue):0}%";
        }
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Settings.LastView = ((AppView)Math.Clamp(
            MainTabs.SelectedIndex,
            0,
            Enum.GetValues<AppView>().Length - 1)).ToString();
        var preferences = _viewModel.Settings.ToPreferences();
        var executable = Environment.ProcessPath ?? string.Empty;
        if (!_startup.TryGetState(out var previousStartup))
        {
            RejectSettingsSave("Windows startup registration could not be read; settings were not saved.");
            return;
        }

        if (!_startup.SetEnabled(preferences.StartWithWindows, executable))
        {
            var restored = _startup.Restore(previousStartup);
            RejectSettingsSave(restored
                ? "Windows startup registration could not be verified; the prior command was restored and settings were not saved."
                : "Windows startup registration failed and its prior command could not be restored. Review the Windows startup setting.");
            return;
        }

        if (!_settings.SavePreferences(preferences))
        {
            var restored = _startup.Restore(previousStartup);
            RejectSettingsSave(restored
                ? "Settings could not be written; startup registration was restored."
                : "Settings could not be written, and startup registration could not be restored. Review the Windows startup setting.");
            return;
        }

        _persistedPreferences = preferences;
        _viewModel.Settings.Apply(preferences);
        _refreshTimer.Interval = TimeSpan.FromSeconds(preferences.RefreshIntervalSeconds);
        RestoreAutomaticOnExitCheckBox.IsChecked = preferences.RestoreAutomaticOnExit;
        _tray.SetVisible(preferences.MinimizeToTray || preferences.EnableNotifications);
        _viewModel.UpdateMonitorWindow();
        SettingsStatusText.Text = "Settings saved for this Windows user.";
    }

    private void RejectSettingsSave(string message)
    {
        _viewModel.Settings.Apply(_persistedPreferences);
        SettingsStatusText.Text = message;
    }

    private void PersistProfiles(string successMessage)
    {
        if (_isDemo && !string.IsNullOrWhiteSpace(_capturePath))
        {
            ProfilesStatusText.Text = successMessage;
            return;
        }

        var preferences = (_persistedPreferences with
        {
            FanProfiles = _viewModel.Settings.FanProfiles.ToArray()
        }).Normalize();
        if (_settings.SavePreferences(preferences))
        {
            _persistedPreferences = preferences;
            _viewModel.Settings.ReplaceProfiles(preferences.FanProfiles);
            ProfilesStatusText.Text = successMessage;
        }
        else
        {
            ProfilesStatusText.Text = "Profile changed in this session but could not be saved.";
        }
    }

    private void TryWriteExport(string path, string content, string title)
    {
        try
        {
            File.WriteAllText(path, content);
            MessageBox.Show(this, $"Saved to:\n{path}", title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, $"{title} failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void FanPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy || sender is not Button button || button.Tag is not string preset)
        {
            return;
        }

        if (!EnsureWriteAccess())
        {
            return;
        }

        var restoringAutomatic = string.Equals(preset, "auto", StringComparison.OrdinalIgnoreCase);
        var controlTask = restoringAutomatic
            ? _viewModel.RestoreAutomaticAsync(_shutdown.Token)
            : _viewModel.SetFanPercentAsync(
                int.Parse(preset, CultureInfo.InvariantCulture),
                _shutdown.Token);
        await TrackControlAsync(controlTask);
    }

    private async void CustomFanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy || !EnsureWriteAccess())
        {
            return;
        }

        await TrackControlAsync(_viewModel.SetFanPercentAsync(
            (int)_viewModel.CustomFanPercent,
            _shutdown.Token));
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.RefreshAsync(_shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Normal window shutdown during a manual refresh.
        }
    }

    private void AccessButton_Click(object sender, RoutedEventArgs e)
    {
        ShowRiskNotice();
    }

    private void RestartAdminButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ElevationService.RestartAsAdministrator())
        {
            MessageBox.Show(
                this,
                "AeroControl could not request administrator access.",
                "Elevation failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool EnsureWriteAccess()
    {
        if (!_isDemo && !_isElevated)
        {
            MessageBox.Show(
                this,
                "Firmware writes require administrator access. Use the restart button at the bottom of the dashboard.",
                "Administrator access required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (string.IsNullOrWhiteSpace(_viewModel.HardwareKey))
        {
            MessageBox.Show(
                this,
                "AeroControl could not identify this hardware configuration. Firmware writes remain locked.",
                "Hardware identity unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var accepted = _isDemo
            ? _demoRiskAccepted
            : _settings.HasAcceptedCurrentRisk(_viewModel.HardwareKey);
        return accepted || ShowRiskNotice();
    }

    private bool ShowRiskNotice()
    {
        var dialog = new RiskAcceptanceWindow
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true && dialog.Accepted)
        {
            if (_isDemo)
            {
                _demoRiskAccepted = true;
            }
            else if (!string.IsNullOrWhiteSpace(_viewModel.HardwareKey))
            {
                if (!_settings.AcceptCurrentRisk(_viewModel.HardwareKey))
                {
                    MessageBox.Show(
                        this,
                        "AeroControl could not save the safety acknowledgement. Firmware writes remain locked.",
                        "Settings unavailable",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return false;
                }
            }
            else
            {
                return false;
            }

            UpdateAccessState();
            return true;
        }

        return false;
    }

    private void UpdateAccessState()
    {
        var accepted = _isDemo
            ? _demoRiskAccepted
            : !string.IsNullOrWhiteSpace(_viewModel.HardwareKey) &&
                _settings.HasAcceptedCurrentRisk(_viewModel.HardwareKey);
        RiskStatusText.Text = accepted
            ? _isDemo
                ? "Safety notice accepted for this demo session"
                : "Safety notice accepted for this hardware and version"
            : "Firmware writes locked until the safety notice is accepted";
        RestartAdminButton.Visibility = !_isDemo && !_isElevated
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowFinalClose)
        {
            Dispose();
            return;
        }

        e.Cancel = true;
        if (!_closeGate.TryBegin(CompleteShutdownAsync, out var shutdownOperation))
        {
            return;
        }

        _refreshTimer.Stop();
        var shouldClose = await shutdownOperation;
        if (!shouldClose)
        {
            _closeGate.Reset(shutdownOperation);
            _refreshTimer.Start();
            return;
        }

        _allowFinalClose = true;
        RequestCloseAfterHandler();
    }

    private async Task<bool> CompleteShutdownAsync()
    {
        if (_activeControlTask is { } activeControlTask)
        {
            try
            {
                var result = await activeControlTask;
                _automaticRestoreRequired = result.RequiresAutomaticRestore;
            }
            catch (Exception exception)
            {
                _automaticRestoreRequired = true;
                _viewModel.ReportStatus(
                    $"Active fan command failed during shutdown: {exception.Message}");
            }

            _activeControlTask = null;
        }

        if (_automaticRestoreRequired && _persistedPreferences.RestoreAutomaticOnExit)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Core.Models.ControlResult result;
            try
            {
                result = await _viewModel.RestoreAutomaticAsync(timeout.Token);
            }
            catch (Exception exception)
            {
                result = Core.Models.ControlResult.Failure(
                    $"Automatic restoration failed: {exception.Message}",
                    true);
            }

            _automaticRestoreRequired = result.RequiresAutomaticRestore;
            if (!result.Succeeded)
            {
                var choice = MessageBox.Show(
                    this,
                    $"{result.Message}\n\nAeroControl could not verify automatic fan control. Keep the app open and retry?",
                    "Automatic fan restoration not verified",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.Yes);
                if (choice == MessageBoxResult.Yes)
                {
                    return false;
                }
            }
        }

        _shutdown.Cancel();
        try
        {
            await _viewModel.DisposeAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"AeroControl could not complete battery-report cleanup.\n\n{exception.Message}",
                "Battery cleanup warning",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        return true;
    }

    private void RequestCloseAfterHandler()
    {
        Dispatcher.BeginInvoke(Close, DispatcherPriority.ApplicationIdle);
    }

    private async Task<Core.Models.ControlResult> TrackControlAsync(
        Task<Core.Models.ControlResult> controlTask)
    {
        _activeControlTask = controlTask;
        try
        {
            var result = await controlTask;
            _automaticRestoreRequired = result.RequiresAutomaticRestore;
            return result;
        }
        catch (Exception exception)
        {
            _automaticRestoreRequired = true;
            var result = Core.Models.ControlResult.Failure(
                $"Fan command failed unexpectedly: {exception.Message}",
                true);
            _viewModel.ReportStatus(result.Message);
            return result;
        }
        finally
        {
            if (ReferenceEquals(_activeControlTask, controlTask))
            {
                _activeControlTask = null;
            }
        }
    }

    private void CaptureWindow(string path)
    {
        var outputPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var width = Math.Max(1, (int)Math.Ceiling(RootGrid.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(RootGrid.ActualHeight));
        var bitmap = new RenderTargetBitmap(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(RootGrid);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshTimer.Stop();
        _viewModel.HardwareSnapshotUpdated -= ViewModel_HardwareSnapshotUpdated;
        _tray.Dispose();
        _shutdown.Cancel();
        _shutdown.Dispose();
        _viewModel.Dispose();
        GC.SuppressFinalize(this);
    }
}
