using System.IO;
using System.Text.Json;

namespace AeroControl.Services;

public sealed class AppSettingsStore
{
    public const string CurrentRiskVersion = "hardware-risk-v1";

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

    public bool HasAcceptedCurrentRisk()
    {
        var settings = Load();
        return string.Equals(
            settings.AcceptedRiskVersion,
            CurrentRiskVersion,
            StringComparison.Ordinal);
    }

    public void AcceptCurrentRisk()
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var settings = new AppSettings(
            CurrentRiskVersion,
            DateTimeOffset.UtcNow);
        File.WriteAllText(
            _settingsPath,
            JsonSerializer.Serialize(settings, JsonOptions));
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
    }

    private sealed record AppSettings(
        string? AcceptedRiskVersion = null,
        DateTimeOffset? AcceptedAt = null);
}
