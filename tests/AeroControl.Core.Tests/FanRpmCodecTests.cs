using AeroControl.Core.Services;

namespace AeroControl.Core.Tests;

public sealed class FanRpmCodecTests
{
    [Theory]
    [InlineData(34828, 3208)]
    [InlineData(13580, 3125)]
    [InlineData(7189, 5404)]
    [InlineData(48660, 5310)]
    [InlineData(7442, 4637)]
    [InlineData(62737, 4597)]
    public void Decode_UnpacksGigabyteByteOrder(int packedValue, int expectedRpm)
    {
        Assert.Equal(expectedRpm, FanRpmCodec.Decode(packedValue));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65535)]
    [InlineData(65536)]
    public void Decode_RejectsInvalidOrImplausibleValues(int packedValue)
    {
        Assert.Null(FanRpmCodec.Decode(packedValue));
    }

    [Fact]
    public void Decode_AcceptsStoppedFanAndCeiling()
    {
        Assert.Equal(0, FanRpmCodec.Decode(0));
        Assert.Equal(10_000, FanRpmCodec.Decode(4135));
    }

    [Fact]
    public void Decode_RejectsValueAboveCeilingAndNull()
    {
        Assert.Null(FanRpmCodec.Decode(4391));
        Assert.Null(FanRpmCodec.Decode(null));
    }
}
