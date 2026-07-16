using System.IO;
using System.Security;
using System.Windows;

namespace AeroControl.Setup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var silentActionRequested = e.Args.Any(argument =>
            string.Equals(argument, "--install", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(argument, "--uninstall", StringComparison.OrdinalIgnoreCase));
        SetupLaunchOptions options;
        try
        {
            options = SetupLaunchOptions.Parse(e.Args);
        }
        catch (ArgumentException exception)
        {
            if (!silentActionRequested)
            {
                MessageBox.Show(exception.Message, "Invalid setup option", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown(2);
            return;
        }

        var rootOverride = Environment.GetEnvironmentVariable("AEROCONTROL_SETUP_ROOT");
        if (!TryResolveInstallRoot(rootOverride, out var installRoot, out var rootError))
        {
            if (options.Action == SetupAction.Interactive)
            {
                MessageBox.Show(rootError, "Invalid setup root", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            Shutdown(2);
            return;
        }

        var installer = new InstallationService(installRoot);
        if (options.Action != SetupAction.Interactive)
        {
            var result = options.Action == SetupAction.Install
                ? installer.Install(AppContext.BaseDirectory, options.StartWithWindows)
                : installer.Uninstall();
            Shutdown(result.Succeeded ? 0 : 1);
            return;
        }

        var window = new MainWindow(installer, options.CapturePath);
        MainWindow = window;
        window.Show();
    }

    internal static bool TryResolveInstallRoot(
        string? rootOverride,
        out string? installRoot,
        out string error,
        Func<string, bool>? isReparsePoint = null)
    {
        installRoot = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(rootOverride))
        {
            return true;
        }

        try
        {
            isReparsePoint ??= path =>
                (new DirectoryInfo(path).Attributes & FileAttributes.ReparsePoint) != 0;
            installRoot = Path.GetFullPath(rootOverride);
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!installRoot.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase))
            {
                error = "The setup override is restricted to the current temporary directory.";
                return false;
            }

            var relativePath = Path.GetRelativePath(temporaryRoot, installRoot);
            var currentPath = temporaryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var part in relativePath.Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries))
            {
                currentPath = Path.Combine(currentPath, part);
                if (File.Exists(currentPath))
                {
                    error = "The setup override cannot traverse a file path.";
                    return false;
                }

                if (Directory.Exists(currentPath) && isReparsePoint(currentPath))
                {
                    error = "The setup override cannot traverse a reparse point.";
                    return false;
                }
            }

            if (!string.Equals(Path.GetFileName(installRoot), "AeroControl", StringComparison.OrdinalIgnoreCase))
            {
                error = "The setup override must end in an AeroControl directory.";
                return false;
            }

            if (!Directory.Exists(installRoot))
            {
                return true;
            }

            if (isReparsePoint(installRoot))
            {
                error = "The setup override cannot target a reparse point.";
                return false;
            }

            var marker = Path.Combine(installRoot, InstallationService.OwnershipMarker);
            if (!File.Exists(marker) && Directory.EnumerateFileSystemEntries(installRoot).Any())
            {
                error = "The setup override directory is not empty and is not owned by AeroControl.";
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or SecurityException)
        {
            installRoot = null;
            error = "The setup override path is invalid or inaccessible.";
            return false;
        }
    }
}
