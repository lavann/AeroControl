using System.Windows;
using AeroControl.Core.Abstractions;
using AeroControl.Core.Services;
using AeroControl.Services;

namespace AeroControl;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppLaunchOptions options;
        try
        {
            options = AppLaunchOptions.Parse(e.Args);
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(
                exception.Message,
                "Invalid AeroControl option",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        IAeroHardwareService hardware = options.IsDemo
            ? new DemoHardwareService()
            : new GigabyteHardwareService();
        IBatteryService battery = options.IsDemo
            ? new DemoBatteryService()
            : new WindowsBatteryService();
        var settings = new AppSettingsStore();
        var window = new MainWindow(
            hardware,
            battery,
            settings,
            options.IsDemo,
            options.CapturePath,
            options.InitialView);
        MainWindow = window;
        window.Show();
    }
}
