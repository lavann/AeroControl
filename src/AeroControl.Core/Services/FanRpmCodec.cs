namespace AeroControl.Core.Services;

public static class FanRpmCodec
{
    public const int MaximumPlausibleRpm = 10_000;

    public static int? Decode(int? packedValue)
    {
        if (packedValue is null or < 0 or > ushort.MaxValue)
        {
            return null;
        }

        var value = packedValue.Value;
        var rpm = ((value & byte.MaxValue) << 8) | ((value >> 8) & byte.MaxValue);
        return rpm <= MaximumPlausibleRpm ? rpm : null;
    }
}
