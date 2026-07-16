using System.IO;
using System.Security;
using System.Text.Json;

namespace AeroControl.Services;

public sealed class AppSettingsStore
{
    public const string CurrentRiskVersion = "hardware-risk-v2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly object _gate = new();

    public AppSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AeroControl",
            "settings.json");
    }

    public bool HasAcceptedCurrentRisk(string hardwareKey)
    {
        lock (_gate)
        {
            var settings = Load();
            return string.Equals(
                settings.AcceptedRiskVersion,
                CurrentRiskVersion,
                StringComparison.Ordinal) &&
                string.Equals(
                    settings.AcceptedHardwareKey,
                    hardwareKey,
                    StringComparison.OrdinalIgnoreCase);
        }
    }

    public bool AcceptCurrentRisk(string hardwareKey)
    {
        if (string.IsNullOrWhiteSpace(hardwareKey))
        {
            return false;
        }

        lock (_gate)
        {
            using var processLock = TryAcquireWriteLock();
            if (processLock is null)
            {
                return false;
            }

            var settings = Load() with
            {
                AcceptedRiskVersion = CurrentRiskVersion,
                AcceptedHardwareKey = hardwareKey,
                AcceptedAt = DateTimeOffset.UtcNow
            };
            return Save(settings);
        }
    }

    public UserPreferences LoadPreferences()
    {
        lock (_gate)
        {
            return (Load().Preferences ?? UserPreferences.Default).Normalize();
        }
    }

    public bool SavePreferences(UserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        lock (_gate)
        {
            using var processLock = TryAcquireWriteLock();
            if (processLock is null)
            {
                return false;
            }

            var settings = Load() with
            {
                Preferences = preferences.Normalize()
            };
            return Save(settings);
        }
    }

    private bool Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        var temporaryPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, _settingsPath, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                // A failed cleanup must not turn an already-failed settings write into a crash.
            }
        }
    }

    private FileStream? TryAcquireWriteLock()
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        try
        {
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    return new FileStream(
                        $"{_settingsPath}.lock",
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None);
                }
                catch (IOException) when (attempt < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }

        return null;
    }

    private AppSettings Load()
    {
        try
        {
            return File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath))
                    ?? new AppSettings()
                : new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (NotSupportedException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
        catch (SecurityException)
        {
            return new AppSettings();
        }
    }

    private sealed record AppSettings(
        string? AcceptedRiskVersion = null,
        string? AcceptedHardwareKey = null,
        DateTimeOffset? AcceptedAt = null,
        UserPreferences? Preferences = null);
}
