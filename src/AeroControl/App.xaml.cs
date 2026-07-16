using System.Windows;
using AeroControl.Core.Abstractions;
using AeroControl.Core.Services;
using AeroControl.Services;

namespace AeroControl;

public partial class App : System.Windows.Application
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
        var preferences = options.IsDemo && !string.IsNullOrWhiteSpace(options.CapturePath)
            ? UserPreferences.Default
            : settings.LoadPreferences();
        var initialView = options.HasExplicitView
            ? options.InitialView
            : preferences.RememberLastView && Enum.TryParse<AppView>(preferences.LastView, true, out var rememberedView)
                ? rememberedView
                : AppView.Cooling;
        var window = new MainWindow(
            hardware,
            battery,
            settings,
            preferences,
            options.IsDemo,
            options.CapturePath,
            initialView);
        MainWindow = window;
        window.Show();
    }
}
