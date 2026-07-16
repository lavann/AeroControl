using AeroControl.Services;

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
}
