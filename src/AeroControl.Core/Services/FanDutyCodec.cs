namespace AeroControl.Core.Services;

public static class FanDutyCodec
{
    public const int MinimumPercent = 30;
    public const int MaximumPercent = 100;
    public const byte MaximumRawDuty = 229;

    public static byte Encode(int percent)
    {
        if (percent is < MinimumPercent or > MaximumPercent)
        {
            throw new ArgumentOutOfRangeException(
                nameof(percent),
                percent,
                $"Fan duty must be between {MinimumPercent}% and {MaximumPercent}%.");
        }

        return checked((byte)Math.Round(
            percent * MaximumRawDuty / 100d,
            MidpointRounding.AwayFromZero));
    }

    public static int Decode(int rawDuty)
    {
        if (rawDuty is < 0 or > MaximumRawDuty)
        {
            throw new ArgumentOutOfRangeException(nameof(rawDuty));
        }

        return (int)Math.Round(
            rawDuty * 100d / MaximumRawDuty,
            MidpointRounding.AwayFromZero);
    }
}
