using System.Globalization;

namespace AeroControl.Core.Wmi;

public sealed record WmiCallResult(IReadOnlyDictionary<string, object?> Values)
{
    public int? GetInt32(string name)
    {
        if (!Values.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (
            exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }
}
