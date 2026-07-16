using AeroControl.Services;
using Microsoft.Win32;

namespace AeroControl.Core.Tests;

public sealed class AppSafetyTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "AeroControl.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void RiskAcceptance_IsBoundToExactHardwareKey()
    {
        var store = new AppSettingsStore(Path.Combine(_directory, "settings.json"));
        const string acceptedHardware = "GIGABYTE|AERO 15-SA|P75SA|FB09";

        Assert.True(store.AcceptCurrentRisk(acceptedHardware));

        Assert.True(store.HasAcceptedCurrentRisk(acceptedHardware));
        Assert.False(store.HasAcceptedCurrentRisk("GIGABYTE|AERO 15-SA|P75SA|FB10"));
        Assert.False(store.HasAcceptedCurrentRisk(string.Empty));
    }

    [Fact]
    public void Preferences_AndRiskAcceptancePreserveEachOther()
    {
        var store = new AppSettingsStore(Path.Combine(_directory, "settings.json"));
        const string acceptedHardware = "GIGABYTE|AERO 15-SA|P75SA|FB09";
        var preferences = UserPreferences.Default with
        {
            StartWithWindows = true,
            HistoryMinutes = 30,
            LastView = "Diagnostics",
            FanProfiles = [new FanProfile("Work", 90)]
        };

        Assert.True(store.SavePreferences(preferences));
        Assert.True(store.AcceptCurrentRisk(acceptedHardware));

        var loaded = store.LoadPreferences();
        Assert.True(loaded.StartWithWindows);
        Assert.Equal(30, loaded.HistoryMinutes);
        Assert.Equal("Diagnostics", loaded.LastView);
        Assert.Equal(new FanProfile("Work", 90), Assert.Single(loaded.FanProfiles));
        Assert.True(store.HasAcceptedCurrentRisk(acceptedHardware));
    }

    [Fact]
    public async Task Settings_CoordinateConcurrentStoreInstances()
    {
        var settingsPath = Path.Combine(_directory, "settings.json");
        var preferencesStore = new AppSettingsStore(settingsPath);
        var riskStore = new AppSettingsStore(settingsPath);
        const string hardware = "GIGABYTE|AERO 15-SA|P75SA|FB09";
        var preferences = UserPreferences.Default with { HistoryMinutes = 60 };

        var results = await Task.WhenAll(
            Task.Run(() => preferencesStore.SavePreferences(preferences)),
            Task.Run(() => riskStore.AcceptCurrentRisk(hardware)));

        Assert.All(results, Assert.True);
        Assert.Equal(60, preferencesStore.LoadPreferences().HistoryMinutes);
        Assert.True(riskStore.HasAcceptedCurrentRisk(hardware));
    }

    [Fact]
    public void Preferences_NormalizeUnsafeAndUnsupportedValues()
    {
        var store = new AppSettingsStore(Path.Combine(_directory, "settings.json"));
        var preferences = UserPreferences.Default with
        {
            CpuAlertCelsius = 200,
            GpuAlertCelsius = 20,
            RefreshIntervalSeconds = 0,
            HistoryMinutes = 999,
            LastView = "unknown",
            FanProfiles =
            [
                new FanProfile("  Work  ", 120),
                new FanProfile("work", 40),
                new FanProfile(string.Empty, 80)
            ]
        };

        Assert.True(store.SavePreferences(preferences));

        var loaded = store.LoadPreferences();
        Assert.Equal(100, loaded.CpuAlertCelsius);
        Assert.Equal(65, loaded.GpuAlertCelsius);
        Assert.Equal(1, loaded.RefreshIntervalSeconds);
        Assert.Equal(15, loaded.HistoryMinutes);
        Assert.Equal("Cooling", loaded.LastView);
        Assert.Equal(new FanProfile("Work", 100), Assert.Single(loaded.FanProfiles));
    }

    [Fact]
    public void Preferences_MalformedOrPartialJsonFallsBackSafely()
    {
        var settingsPath = Path.Combine(_directory, "settings.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(settingsPath, "{\"Preferences\":{\"LastView\":null,\"FanProfiles\":null}}");
        var store = new AppSettingsStore(settingsPath);

        var preferences = store.LoadPreferences();

        Assert.Equal("Cooling", preferences.LastView);
        Assert.NotEmpty(preferences.FanProfiles);
        Assert.True(preferences.RestoreAutomaticOnExit);
        Assert.True(preferences.EnableNotifications);
        Assert.True(preferences.EnableFanStallAlert);

        File.WriteAllText(settingsPath, "{invalid-json");
        var fallback = store.LoadPreferences();
        Assert.Equal(UserPreferences.Default.LastView, fallback.LastView);
        Assert.Equal(UserPreferences.Default.RefreshIntervalSeconds, fallback.RefreshIntervalSeconds);
        Assert.Equal(UserPreferences.Default.FanProfiles, fallback.FanProfiles);
    }

    [Fact]
    public void StartupRegistration_QuotesPathAndIsFullyReversible()
    {
        var values = new FakeStartupValueStore();
        var startup = new StartupRegistrationService(values);
        const string executable = @"C:\Users\Example User\AppData\Local\AeroControl\AeroControl.exe";

        Assert.True(startup.SetEnabled(true, executable));
        Assert.Equal($"\"{executable}\"", values.Value);
        Assert.True(startup.IsEnabled(executable));

        Assert.True(startup.SetEnabled(false, executable));
        Assert.Null(values.Value);
        Assert.False(startup.IsEnabled(executable));
    }

    [Fact]
    public void StartupRegistration_PreservesAnotherAeroControlCopyAndRestoresExactCommand()
    {
        var values = new FakeStartupValueStore();
        const string installedCommand = "\"C:\\Installed\\AeroControl.exe\"";
        values.Write(installedCommand);
        var startup = new StartupRegistrationService(values);

        Assert.False(startup.IsEnabled("C:\\Portable\\AeroControl.exe"));
        Assert.True(startup.TryGetState(out var previous));
        Assert.True(startup.SetEnabled(true, "C:\\Portable\\AeroControl.exe"));
        Assert.Equal("\"C:\\Portable\\AeroControl.exe\"", values.Value);

        Assert.True(startup.Restore(previous));
        Assert.Equal(installedCommand, values.Value);
        Assert.True(startup.SetEnabled(false, "C:\\Portable\\AeroControl.exe"));
        Assert.Equal(installedCommand, values.Value);
    }

    [Fact]
    public void StartupRegistration_DoesNotOwnOrRewriteExpandableValue()
    {
        var values = new FakeStartupValueStore();
        var original = new StartupRegistryValue(
            "\"%LOCALAPPDATA%\\Installed\\AeroControl.exe\"",
            RegistryValueKind.ExpandString);
        values.Write(original);
        var startup = new StartupRegistrationService(values);

        Assert.False(startup.IsEnabled("C:\\Installed\\AeroControl.exe"));
        Assert.True(startup.TryGetState(out var state));
        Assert.True(startup.SetEnabled(false, "C:\\Installed\\AeroControl.exe"));
        Assert.Equal(original, values.RegistryValue);
        Assert.True(startup.Restore(state));
        Assert.Equal(original, values.RegistryValue);
    }

    [Theory]
    [MemberData(nameof(RelaunchArguments))]
    public void ElevationRelaunch_RemovesCaptureOptions(
        string[] arguments,
        string[] expected)
    {
        Assert.Equal(expected, ElevationService.GetRelaunchArguments(arguments));
    }

    public static TheoryData<string[], string[]> RelaunchArguments => new()
    {
        {
            ["--demo", "--capture", "C:\\path with spaces\\image.png", "--other"],
            ["--demo", "--other"]
        },
        {
            ["--capture=C:\\image.png", "--demo"],
            ["--demo"]
        },
        {
            ["--demo"],
            ["--demo"]
        },
        {
            ["--view", "battery", "--capture", "C:\\image.png"],
            ["--view", "battery"]
        }
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }

        GC.SuppressFinalize(this);
    }

    private sealed class FakeStartupValueStore : IStartupValueStore
    {
        public string? Value => RegistryValue?.Value as string;

        public StartupRegistryValue? RegistryValue { get; private set; }

        public StartupRegistryValue? Read() => RegistryValue;

        public void Write(string value)
        {
            RegistryValue = new StartupRegistryValue(value, RegistryValueKind.String);
        }

        public void Write(StartupRegistryValue value)
        {
            RegistryValue = value;
        }

        public void Delete()
        {
            RegistryValue = null;
        }
    }
}
