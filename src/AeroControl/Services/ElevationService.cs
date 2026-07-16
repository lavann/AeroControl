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

        var arguments = GetRelaunchArguments(Environment.GetCommandLineArgs().Skip(1));

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas"
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process.Start(startInfo);
            Application.Current.Shutdown();
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static IReadOnlyList<string> GetRelaunchArguments(IEnumerable<string> arguments)
    {
        var source = arguments.ToArray();
        var result = new List<string>();
        for (var index = 0; index < source.Length; index++)
        {
            var argument = source[index];
            if (string.Equals(argument, "--capture", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < source.Length)
                {
                    index++;
                }

                continue;
            }

            if (argument.StartsWith("--capture=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(argument);
        }

        return result;
    }
}
