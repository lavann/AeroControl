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

namespace AeroControl;

public partial class MainWindow : Window, IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly AppSettingsStore _settings;
    private readonly bool _isDemo;
    private readonly bool _isElevated;
    private readonly string? _capturePath;
    private readonly DispatcherTimer _refreshTimer;
    private readonly CancellationTokenSource _shutdown = new();
    private bool _automaticRestoreRequired;
    private bool _closingAfterRestore;
    private bool _disposed;
    private bool _demoRiskAccepted;
    private Task<Core.Models.ControlResult>? _activeControlTask;

    public MainWindow(
        IAeroHardwareService hardware,
        AppSettingsStore settings,
        bool isDemo,
        string? capturePath)
    {
        InitializeComponent();
        _settings = settings;
        _isDemo = isDemo;
        _isElevated = ElevationService.IsAdministrator();
        _capturePath = capturePath;
        _viewModel = new MainViewModel(hardware, isDemo, _isElevated);
        DataContext = _viewModel;
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _refreshTimer.Tick += RefreshTimer_Tick;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync(_shutdown.Token);
        UpdateAccessState();
        _refreshTimer.Start();

        if (!string.IsNullOrWhiteSpace(_capturePath))
        {
            await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.Loaded);
            await Task.Delay(250);
            CaptureWindow(_capturePath);
            Application.Current.Shutdown();
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
        await _viewModel.RefreshAsync(_shutdown.Token);
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
        _refreshTimer.Stop();
        if (!_closingAfterRestore && _activeControlTask is { } activeControlTask)
        {
            e.Cancel = true;
            try
            {
                var result = await activeControlTask;
                _automaticRestoreRequired = result.RequiresAutomaticRestore;
            }
            catch (Exception exception)
            {
                _automaticRestoreRequired = true;
                _viewModel.ReportStatus($"Active fan command failed during shutdown: {exception.Message}");
            }

            _activeControlTask = null;
            Close();
            return;
        }

        if (!_closingAfterRestore &&
            _automaticRestoreRequired &&
            RestoreAutomaticOnExitCheckBox.IsChecked == true)
        {
            e.Cancel = true;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Core.Models.ControlResult? result = null;
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
                    _refreshTimer.Start();
                    return;
                }
            }

            _closingAfterRestore = true;
            Close();
            return;
        }

        Dispose();
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
        _shutdown.Cancel();
        _shutdown.Dispose();
        _viewModel.Dispose();
        GC.SuppressFinalize(this);
    }
}
