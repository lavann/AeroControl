using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace AeroControl.Services;

public static class ElevationService
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool RestartAsAdministrator()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return false;
        }

        var arguments = Environment.GetCommandLineArgs()
            .Skip(1)
            .Where(argument => !string.Equals(argument, "--capture", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = string.Join(' ', arguments.Select(QuoteArgument)),
                UseShellExecute = true,
                Verb = "runas"
            });
            Application.Current.Shutdown();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string QuoteArgument(string argument) =>
        argument.Contains(' ') ? $"\"{argument.Replace("\"", "\\\"")}\"" : argument;
}
