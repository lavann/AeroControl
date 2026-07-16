using System.IO;
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

    public AppSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AeroControl",
            "settings.json");
    }

    public bool HasAcceptedCurrentRisk(string hardwareKey)
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

    public bool AcceptCurrentRisk(string hardwareKey)
    {
        if (string.IsNullOrWhiteSpace(hardwareKey))
        {
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var settings = new AppSettings(
                CurrentRiskVersion,
                hardwareKey,
                DateTimeOffset.UtcNow);
            File.WriteAllText(
                _settingsPath,
                JsonSerializer.Serialize(settings, JsonOptions));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    private sealed record AppSettings(
        string? AcceptedRiskVersion = null,
        string? AcceptedHardwareKey = null,
        DateTimeOffset? AcceptedAt = null);
}
