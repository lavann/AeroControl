using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AeroControl.Setup;

public partial class MainWindow : Window
{
    private readonly InstallationService _installer;
    private readonly string? _capturePath;

    internal MainWindow(InstallationService installer, string? capturePath = null)
    {
        InitializeComponent();
        _installer = installer;
        _capturePath = capturePath;
        InstallPathTextBox.Text = string.IsNullOrWhiteSpace(_capturePath)
            ? _installer.InstallDirectory
            : @"%LOCALAPPDATA%\AeroControl";
        StartupCheckBox.IsChecked = string.IsNullOrWhiteSpace(_capturePath) && _installer.IsStartupEnabled();
        StatusText.Text = string.IsNullOrWhiteSpace(_capturePath) && _installer.IsInstalled
            ? "AeroControl is installed for this user. Install again to update it."
            : "Ready to install AeroControl for this user.";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_capturePath))
        {
            return;
        }

        await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.Loaded);
        var outputPath = Path.GetFullPath(_capturePath);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(RootGrid.ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(RootGrid.ActualHeight)),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(RootGrid);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
        Application.Current.Shutdown();
    }

    private void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        var result = _installer.Install(
            AppContext.BaseDirectory,
            StartupCheckBox.IsChecked == true);
        StatusText.Text = result.Message;
        if (!result.Succeeded || LaunchCheckBox.IsChecked != true)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _installer.InstalledExecutable,
            UseShellExecute = true
        });
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        var result = _installer.Uninstall();
        StatusText.Text = result.Message;
    }
}
