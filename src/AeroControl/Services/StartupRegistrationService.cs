using System.IO;
using System.Security;
using Microsoft.Win32;

namespace AeroControl.Services;

internal interface IStartupValueStore
{
    StartupRegistryValue? Read();

    void Write(StartupRegistryValue value);

    void Delete();
}

internal sealed record StartupRegistryValue(object Value, RegistryValueKind Kind);

public sealed class StartupRegistrationService
{
    private readonly IStartupValueStore _store;

    public StartupRegistrationService()
        : this(new RegistryStartupValueStore())
    {
    }

    internal StartupRegistrationService(IStartupValueStore store)
    {
        _store = store;
    }

    public bool IsEnabled(string executablePath)
    {
        return !string.IsNullOrWhiteSpace(executablePath) &&
            TryGetState(out var state) &&
            state.IsOwnedBy(executablePath);
    }

    internal bool TryGetState(out StartupRegistrationState state)
    {
        try
        {
            state = new StartupRegistrationState(_store.Read());
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
        {
            state = new StartupRegistrationState(null);
            return false;
        }
    }

    public bool SetEnabled(bool enabled, string executablePath)
    {
        if (enabled && string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            if (enabled)
            {
                _store.Write(new StartupRegistryValue(
                    BuildCommand(executablePath),
                    RegistryValueKind.String));
            }
            else
            {
                var existing = new StartupRegistrationState(_store.Read());
                if (!existing.IsEnabled || !existing.IsOwnedBy(executablePath))
                {
                    return true;
                }

                _store.Delete();
            }

            var persisted = new StartupRegistrationState(_store.Read());
            return enabled
                ? persisted.IsOwnedBy(executablePath)
                : !persisted.IsEnabled;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
        {
            return false;
        }
    }

    internal bool Restore(StartupRegistrationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            if (state.IsEnabled)
            {
                _store.Write(state.Value!);
            }
            else
            {
                _store.Delete();
            }

            return Equals(_store.Read(), state.Value);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or SecurityException)
        {
            return false;
        }
    }

    internal static string BuildCommand(string executablePath) =>
        $"\"{executablePath.Trim()}\"";

    private sealed class RegistryStartupValueStore : IStartupValueStore
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AeroControl";

        public StartupRegistryValue? Read()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            if (key?.GetValueNames().Contains(ValueName, StringComparer.OrdinalIgnoreCase) != true)
            {
                return null;
            }

            var value = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return value is null
                ? null
                : new StartupRegistryValue(value, key.GetValueKind(ValueName));
        }

        public void Write(StartupRegistryValue value)
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

internal sealed record StartupRegistrationState(StartupRegistryValue? Value)
{
    public bool IsEnabled => Value is not null;

    public string? Command => Value is { Kind: RegistryValueKind.String, Value: string command }
        ? command
        : null;

    public bool IsOwnedBy(string executablePath) =>
        !string.IsNullOrWhiteSpace(executablePath) &&
        string.Equals(
            Command,
            StartupRegistrationService.BuildCommand(executablePath),
            StringComparison.OrdinalIgnoreCase);
}
