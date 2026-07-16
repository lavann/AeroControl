namespace AeroControl.Services;

public sealed record FanProfile(string Name, int Percent);

public sealed record UserPreferences
{
    public bool StartWithWindows { get; init; }

    public bool MinimizeToTray { get; init; }

    public bool EnableNotifications { get; init; } = true;

    public int CpuAlertCelsius { get; init; } = 90;

    public int GpuAlertCelsius { get; init; } = 85;

    public bool EnableFanStallAlert { get; init; } = true;

    public int RefreshIntervalSeconds { get; init; } = 2;

    public int HistoryMinutes { get; init; } = 15;

    public bool RememberLastView { get; init; }

    public string LastView { get; init; } = "Cooling";

    public bool RestoreAutomaticOnExit { get; init; } = true;

    public IReadOnlyList<FanProfile> FanProfiles { get; init; } =
    [
        new FanProfile("Quiet", 70),
        new FanProfile("Balanced", 80),
        new FanProfile("Maximum", 100)
    ];

    public static UserPreferences Default { get; } = new();

    public UserPreferences Normalize()
    {
        var allowedHistory = new[] { 5, 15, 30, 60 };
        var historyMinutes = allowedHistory.Contains(HistoryMinutes) ? HistoryMinutes : 15;
        var profiles = (FanProfiles ?? [])
            .OfType<FanProfile>()
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
            .Select(profile => NormalizeProfile(profile))
            .DistinctBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

        return this with
        {
            CpuAlertCelsius = Math.Clamp(CpuAlertCelsius, 70, 100),
            GpuAlertCelsius = Math.Clamp(GpuAlertCelsius, 65, 100),
            RefreshIntervalSeconds = Math.Clamp(RefreshIntervalSeconds, 1, 30),
            HistoryMinutes = historyMinutes,
            LastView = NormalizeView(LastView),
            FanProfiles = profiles.Length > 0 ? profiles : Default.FanProfiles
        };
    }

    private static FanProfile NormalizeProfile(FanProfile profile)
    {
        var name = profile.Name.Trim();
        return new FanProfile(
            name[..Math.Min(name.Length, 32)],
            Math.Clamp(profile.Percent, 30, 100));
    }

    private static string NormalizeView(string? value) => (value ?? string.Empty).ToLowerInvariant() switch
    {
        "battery" => "Battery",
        "monitor" => "Monitor",
        "diagnostics" => "Diagnostics",
        "profiles" => "Profiles",
        "settings" => "Settings",
        _ => "Cooling"
    };
}
