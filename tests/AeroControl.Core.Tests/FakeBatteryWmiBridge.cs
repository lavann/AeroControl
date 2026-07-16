using AeroControl.Core.Abstractions;
using AeroControl.Core.Wmi;

namespace AeroControl.Core.Tests;

internal sealed class FakeBatteryWmiBridge : IWmiBridge
{
    public Dictionary<string, object?> Battery { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Name"] = "Aero 15",
        ["EstimatedChargeRemaining"] = 80,
        ["BatteryStatus"] = 2,
        ["DesignVoltage"] = 16_725
    };

    public Dictionary<string, object?> Status { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PowerOnline"] = true,
        ["Charging"] = false,
        ["Discharging"] = false,
        ["RemainingCapacity"] = 72_000,
        ["Voltage"] = 16_725,
        ["ChargeRate"] = 0,
        ["DischargeRate"] = 0
    };

    public Dictionary<string, object?> FullCapacity { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FullChargedCapacity"] = 90_000
    };

    public Dictionary<string, object?> Cycles { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CycleCount"] = 120
    };

    public IReadOnlySet<string> GetMethodNames(string namespacePath, string className) =>
        throw new NotSupportedException();

    public WmiCallResult Invoke(
        string namespacePath,
        string className,
        string methodName,
        IReadOnlyDictionary<string, object?>? arguments = null) =>
        throw new NotSupportedException();

    public IReadOnlyDictionary<string, object?> QueryFirst(string namespacePath, string query)
    {
        if (query.Contains("Win32_Battery", StringComparison.OrdinalIgnoreCase))
        {
            return Battery;
        }

        if (query.Contains("BatteryStatus", StringComparison.OrdinalIgnoreCase))
        {
            return Status;
        }

        if (query.Contains("BatteryFullChargedCapacity", StringComparison.OrdinalIgnoreCase))
        {
            return FullCapacity;
        }

        if (query.Contains("BatteryCycleCount", StringComparison.OrdinalIgnoreCase))
        {
            return Cycles;
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }
}
