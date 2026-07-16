using AeroControl.Services;

namespace AeroControl.Core.Tests;

public sealed class AppLaunchOptionsTests
{
    [Fact]
    public void Parse_DefaultsToCooling()
    {
        var options = AppLaunchOptions.Parse([]);

        Assert.False(options.IsDemo);
        Assert.Null(options.CapturePath);
        Assert.Equal(AppView.Cooling, options.InitialView);
        Assert.False(options.HasExplicitView);
    }

    [Fact]
    public void Parse_ReadsBatteryDemoCapture()
    {
        var options = AppLaunchOptions.Parse(
            ["--demo", "--view", "battery", "--capture=C:\\capture.png"]);

        Assert.True(options.IsDemo);
        Assert.Equal("C:\\capture.png", options.CapturePath);
        Assert.Equal(AppView.Battery, options.InitialView);
        Assert.True(options.HasExplicitView);
    }

    [Theory]
    [InlineData("monitor", "Monitor")]
    [InlineData("diagnostics", "Diagnostics")]
    [InlineData("profiles", "Profiles")]
    [InlineData("settings", "Settings")]
    public void Parse_ReadsAdditionalViews(string value, string expected)
    {
        var options = AppLaunchOptions.Parse(["--view", value]);

        Assert.Equal(expected, options.InitialView.ToString());
        Assert.True(options.HasExplicitView);
    }

    [Theory]
    [InlineData("--view")]
    [InlineData("--view=unknown")]
    [InlineData("--capture")]
    [InlineData("--unknown")]
    public void Parse_RejectsInvalidOptions(string argument)
    {
        Assert.Throws<ArgumentException>(() => AppLaunchOptions.Parse([argument]));
    }
}
