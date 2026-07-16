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

        var isDemo = e.Args.Any(argument =>
            string.Equals(argument, "--demo", StringComparison.OrdinalIgnoreCase));
        var capturePath = GetOptionValue(e.Args, "--capture");

        IAeroHardwareService hardware = isDemo
            ? new DemoHardwareService()
            : new GigabyteHardwareService();
        var settings = new AppSettingsStore();
        var window = new MainWindow(hardware, settings, isDemo, capturePath);
        MainWindow = window;
        window.Show();
    }

    private static string? GetOptionValue(string[] arguments, string optionName)
    {
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (argument.StartsWith($"{optionName}=", StringComparison.OrdinalIgnoreCase))
            {
                return argument[(optionName.Length + 1)..];
            }

            if (string.Equals(argument, optionName, StringComparison.OrdinalIgnoreCase) &&
                index + 1 < arguments.Length)
            {
                return arguments[index + 1];
            }
        }

        return null;
    }
}
