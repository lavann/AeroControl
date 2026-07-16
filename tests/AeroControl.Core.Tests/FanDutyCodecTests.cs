using AeroControl.Core.Services;

namespace AeroControl.Core.Tests;

public sealed class FanDutyCodecTests
{
    [Theory]
    [InlineData(70, 160)]
    [InlineData(80, 183)]
    [InlineData(100, 229)]
    public void Encode_MapsVerifiedPresets(int percent, byte expectedRawDuty)
    {
        Assert.Equal(expectedRawDuty, FanDutyCodec.Encode(percent));
    }

    [Theory]
    [InlineData(160, 70)]
    [InlineData(183, 80)]
    [InlineData(229, 100)]
    public void Decode_MapsVerifiedPresets(int rawDuty, int expectedPercent)
    {
        Assert.Equal(expectedPercent, FanDutyCodec.Decode(rawDuty));
    }

    [Theory]
    [InlineData(29)]
    [InlineData(101)]
    public void Encode_RejectsUnsafeRange(int percent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FanDutyCodec.Encode(percent));
    }
}
