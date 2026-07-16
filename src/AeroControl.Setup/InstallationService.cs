using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace AeroControl.Setup;

internal interface ISetupStartupStore
{
    SetupStartupRegistryValue? Read();

    void Write(SetupStartupRegistryValue value);

    void Delete();
}

internal sealed record SetupStartupRegistryValue(object Value, RegistryValueKind Kind);

internal sealed record InstallationResult(bool Succeeded, string Message);

internal sealed class InstallationService
{
    private static readonly string[] PayloadFiles = ["AeroControl.exe", "LICENSE", "DISCLAIMER.md"];
    internal const string OwnershipMarker = ".aerocontrol-install-root";
    private readonly ISetupStartupStore _startup;
    private readonly Func<bool> _isApplicationRunning;
    private readonly Action<string, string> _copyPreservedContent;
    private readonly Func<string, bool> _deleteDirectory;
    private readonly Func<string, bool> _isReparsePoint;

    public InstallationService()
        : this(new RegistrySetupStartupStore(), null, IsAeroControlRunning)
    {
    }

    internal InstallationService(string? installDirectory)
        : this(new RegistrySetupStartupStore(), installDirectory, IsAeroControlRunning)
    {
    }

    internal InstallationService(
        ISetupStartupStore startup,
        string? installDirectory,
        Func<bool>? isApplicationRunning = null,
        Action<string, string>? copyPreservedContent = null,
        Func<string, bool>? deleteDirectory = null,
        Func<string, bool>? isReparsePoint = null)
    {
        _startup = startup;
        _isApplicationRunning = isApplicationRunning ?? IsAeroControlRunning;
        _copyPreservedContent = copyPreservedContent ?? CopyPreservedContent;
        _deleteDirectory = deleteDirectory ?? TryDeleteDirectory;
        _isReparsePoint = isReparsePoint ?? IsReparsePoint;
        InstallDirectory = installDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AeroControl");
    }

    public string InstallDirectory { get; }

    public string InstalledExecutable => Path.Combine(InstallDirectory, "AeroControl.exe");

    public bool IsInstalled => File.Exists(InstalledExecutable);

    public bool IsStartupEnabled()
    {
        try
        {
            return string.Equals(
                GetOwnedCommand(_startup.Read()),
                Quote(InstalledExecutable),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
        {
            return false;
        }
    }

    public InstallationResult Install(string sourceDirectory, bool startWithWindows)
    {
        foreach (var payload in PayloadFiles)
        {
            if (!File.Exists(Path.Combine(sourceDirectory, payload)))
            {
                return new InstallationResult(false, $"Missing installation file: {payload}");
            }
        }

        if (_isApplicationRunning())
        {
            return new InstallationResult(false, "Close AeroControl before installing or updating it.");
        }

        if (!IsInstallRootSafe(out var rootError))
        {
            return new InstallationResult(false, rootError);
        }

        var stagingDirectory = $"{InstallDirectory}.staging-{Guid.NewGuid():N}";
        var backupDirectory = $"{InstallDirectory}.backup-{Guid.NewGuid():N}";
        SetupStartupRegistryValue? previousStartup;
        try
        {
            previousStartup = _startup.Read();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return new InstallationResult(false, "The current sign-in startup setting could not be read; installation was not changed.");
        }

        var existingMoved = false;
        var newActivated = false;
        var committed = false;
        var retainBackup = false;
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            foreach (var payload in PayloadFiles)
            {
                File.Copy(
                    Path.Combine(sourceDirectory, payload),
                    Path.Combine(stagingDirectory, payload),
                    false);
            }
            File.WriteAllText(Path.Combine(stagingDirectory, OwnershipMarker), "AeroControl per-user installation");

            if (Directory.Exists(InstallDirectory))
            {
                _copyPreservedContent(InstallDirectory, stagingDirectory);
            }

            if (Directory.Exists(InstallDirectory))
            {
                Directory.Move(InstallDirectory, backupDirectory);
                existingMoved = true;
            }

            Directory.Move(stagingDirectory, InstallDirectory);
            newActivated = true;

            if (!SetStartup(startWithWindows))
            {
                throw new IOException("The requested sign-in startup state could not be verified.");
            }

            committed = true;
            if (existingMoved && !_deleteDirectory(backupDirectory))
            {
                if (File.Exists(Path.Combine(backupDirectory, "AeroControl.exe")))
                {
                    var filesRestored = RollbackFiles(backupDirectory, existingMoved, newActivated);
                    var startupRestored = RestoreStartup(previousStartup);
                    retainBackup = !filesRestored;
                    return new InstallationResult(
                        false,
                        filesRestored && startupRestored
                            ? "The previous installation could not be retired, so the update was rolled back."
                            : "The previous installation could not be retired, and rollback could not be fully verified.");
                }

                retainBackup = true;
                return new InstallationResult(
                    false,
                    "AeroControl was updated, but non-executable files from the previous installation could not be fully removed.");
            }

            return new InstallationResult(
                true,
                startWithWindows
                    ? "AeroControl installed and enabled for sign-in startup."
                    : "AeroControl installed for the current Windows user.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            var filesRestored = RollbackFiles(backupDirectory, existingMoved, newActivated);
            var startupRestored = RestoreStartup(previousStartup);
            retainBackup = !filesRestored;
            var rollbackMessage = filesRestored && startupRestored
                ? "The prior installation and startup setting were restored."
                : "Rollback could not be fully verified; the backup directory was retained for recovery.";
            return new InstallationResult(false, $"Installation failed: {exception.Message} {rollbackMessage}");
        }
        finally
        {
            _deleteDirectory(stagingDirectory);
            if (!committed && !retainBackup)
            {
                _deleteDirectory(backupDirectory);
            }
        }
    }

    public InstallationResult Uninstall()
    {
        if (_isApplicationRunning())
        {
            return new InstallationResult(false, "Close AeroControl before removing it.");
        }

        if (!IsInstallRootSafe(out var rootError))
        {
            return new InstallationResult(false, rootError);
        }

        SetupStartupRegistryValue? previousStartup;
        try
        {
            previousStartup = _startup.Read();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return new InstallationResult(false, "The current sign-in startup setting could not be read; installed files were left unchanged.");
        }

        var removalDirectory = $"{InstallDirectory}.remove-{Guid.NewGuid():N}";
        var moved = false;
        try
        {
            if (!File.Exists(Path.Combine(InstallDirectory, OwnershipMarker)))
            {
                return new InstallationResult(
                    false,
                    "The installation ownership marker is missing, so files and startup settings were left unchanged.");
            }

            if (!SetStartup(false))
            {
                var startupRestored = RestoreStartup(previousStartup);
                return new InstallationResult(
                    false,
                    startupRestored
                        ? "The sign-in startup value could not be removed; its prior value was restored and installed files were left unchanged."
                        : "The sign-in startup value could not be removed or restored; installed files were left unchanged.");
            }

            Directory.Move(InstallDirectory, removalDirectory);
            moved = true;
            Directory.CreateDirectory(InstallDirectory);
            _copyPreservedContent(removalDirectory, InstallDirectory);
            if (!Directory.EnumerateFileSystemEntries(InstallDirectory).Any())
            {
                Directory.Delete(InstallDirectory);
            }

            if (!_deleteDirectory(removalDirectory))
            {
                if (File.Exists(Path.Combine(removalDirectory, "AeroControl.exe")))
                {
                    var filesRestored = RollbackUninstall(removalDirectory, moved);
                    var startupRestored = RestoreStartup(previousStartup);
                    return new InstallationResult(
                        false,
                        filesRestored && startupRestored
                            ? "The installation could not be removed, so its files and startup setting were restored."
                            : "The installation could not be removed, and rollback could not be fully verified.");
                }

                return new InstallationResult(
                    false,
                    "The AeroControl executable and startup setting were removed, but non-executable installation files could not be fully cleaned up.");
            }

            return new InstallationResult(true, "AeroControl was removed for this Windows user.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            var filesRestored = RollbackUninstall(removalDirectory, moved);
            var startupRestored = RestoreStartup(previousStartup);
            return new InstallationResult(
                false,
                filesRestored && startupRestored
                    ? $"Removal failed: {exception.Message} The prior installation was restored."
                    : $"Removal failed: {exception.Message} Rollback could not be fully verified.");
        }
    }

    private bool SetStartup(bool enabled)
    {
        try
        {
            if (enabled)
            {
                _startup.Write(new SetupStartupRegistryValue(
                    Quote(InstalledExecutable),
                    RegistryValueKind.String));
            }
            else
            {
                var existing = _startup.Read();
                if (existing is null)
                {
                    return true;
                }

                if (!string.Equals(GetOwnedCommand(existing), Quote(InstalledExecutable), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                _startup.Delete();
            }

            var persisted = GetOwnedCommand(_startup.Read());
            return enabled
                ? string.Equals(persisted, Quote(InstalledExecutable), StringComparison.OrdinalIgnoreCase)
                : persisted is null;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
        {
            return false;
        }
    }

    private bool IsInstallRootSafe(out string error)
    {
        try
        {
            if (File.Exists(InstallDirectory))
            {
                error = "The installation path is occupied by a file; no changes were made.";
                return false;
            }

            if (Directory.Exists(InstallDirectory) && _isReparsePoint(InstallDirectory))
            {
                error = "The installation directory is a reparse point; no changes were made.";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            error = "The installation directory could not be safely inspected; no changes were made.";
            return false;
        }
    }

    private bool RollbackFiles(string backupDirectory, bool existingMoved, bool newActivated)
    {
        try
        {
            if (newActivated && Directory.Exists(InstallDirectory))
            {
                Directory.Delete(InstallDirectory, true);
            }

            if (existingMoved && Directory.Exists(backupDirectory))
            {
                Directory.Move(backupDirectory, InstallDirectory);
            }

            return !existingMoved || Directory.Exists(InstallDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    private bool RollbackUninstall(string removalDirectory, bool moved)
    {
        try
        {
            if (!moved)
            {
                return true;
            }

            if (Directory.Exists(InstallDirectory))
            {
                Directory.Delete(InstallDirectory, true);
            }

            if (Directory.Exists(removalDirectory))
            {
                Directory.Move(removalDirectory, InstallDirectory);
            }

            return Directory.Exists(InstallDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    private bool RestoreStartup(SetupStartupRegistryValue? value)
    {
        try
        {
            if (value is null)
            {
                _startup.Delete();
            }
            else
            {
                _startup.Write(value);
            }

            return Equals(_startup.Read(), value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    private static void CopyPreservedContent(string sourceDirectory, string destinationDirectory)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            if (PayloadFiles.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(file), OwnershipMarker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), false);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var info = new DirectoryInfo(directory);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            var destination = Path.Combine(destinationDirectory, info.Name);
            Directory.CreateDirectory(destination);
            CopyDirectory(directory, destination);
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), false);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var info = new DirectoryInfo(directory);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            var destination = Path.Combine(destinationDirectory, info.Name);
            Directory.CreateDirectory(destination);
            CopyDirectory(directory, destination);
        }
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                File.Delete(Path.Combine(path, "AeroControl.exe"));
                Directory.Delete(path, true);
            }

            return !Directory.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    private static bool IsReparsePoint(string path) =>
        (new DirectoryInfo(path).Attributes & FileAttributes.ReparsePoint) != 0;

    private static bool IsAeroControlRunning()
    {
        var processes = Process.GetProcessesByName("AeroControl");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static string? GetOwnedCommand(SetupStartupRegistryValue? value) =>
        value is { Kind: RegistryValueKind.String, Value: string command }
            ? command
            : null;

    private sealed class RegistrySetupStartupStore : ISetupStartupStore
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AeroControl";

        public SetupStartupRegistryValue? Read()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            if (key?.GetValueNames().Contains(ValueName, StringComparer.OrdinalIgnoreCase) != true)
            {
                return null;
            }

            var value = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return value is null
                ? null
                : new SetupStartupRegistryValue(value, key.GetValueKind(ValueName));
        }

        public void Write(SetupStartupRegistryValue value)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            key.SetValue(ValueName, value.Value, value.Kind);
        }

        public void Delete()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(ValueName, false);
        }
    }
}
