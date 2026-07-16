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
    private bool _fanModeChanged;
    private bool _closingAfterRestore;
    private bool _disposed;

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

        var result = string.Equals(preset, "auto", StringComparison.OrdinalIgnoreCase)
            ? await _viewModel.RestoreAutomaticAsync(_shutdown.Token)
            : await _viewModel.SetFanPercentAsync(
                int.Parse(preset, CultureInfo.InvariantCulture),
                _shutdown.Token);
        _fanModeChanged |= result.Succeeded;
    }

    private async void CustomFanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy || !EnsureWriteAccess())
        {
            return;
        }

        var result = await _viewModel.SetFanPercentAsync(
            (int)_viewModel.CustomFanPercent,
            _shutdown.Token);
        _fanModeChanged |= result.Succeeded;
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

        return _settings.HasAcceptedCurrentRisk() || ShowRiskNotice();
    }

    private bool ShowRiskNotice()
    {
        var dialog = new RiskAcceptanceWindow
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true && dialog.Accepted)
        {
            _settings.AcceptCurrentRisk();
            UpdateAccessState();
            return true;
        }

        return false;
    }

    private void UpdateAccessState()
    {
        var accepted = _settings.HasAcceptedCurrentRisk();
        RiskStatusText.Text = accepted
            ? "Safety notice accepted for this version"
            : "Firmware writes locked until the safety notice is accepted";
        RestartAdminButton.Visibility = !_isDemo && !_isElevated
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        _refreshTimer.Stop();
        if (!_closingAfterRestore &&
            _fanModeChanged &&
            RestoreAutomaticOnExitCheckBox.IsChecked == true)
        {
            e.Cancel = true;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                await _viewModel.RestoreAutomaticAsync(timeout.Token);
            }
            catch
            {
                // Shutdown must continue even if the firmware provider is unavailable.
            }

            _closingAfterRestore = true;
            Close();
            return;
        }

        Dispose();
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
