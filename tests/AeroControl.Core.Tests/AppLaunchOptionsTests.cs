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
    }

    [Fact]
    public void Parse_ReadsBatteryDemoCapture()
    {
        var options = AppLaunchOptions.Parse(
            ["--demo", "--view", "battery", "--capture=C:\\capture.png"]);

        Assert.True(options.IsDemo);
        Assert.Equal("C:\\capture.png", options.CapturePath);
        Assert.Equal(AppView.Battery, options.InitialView);
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
