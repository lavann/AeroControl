using System.Globalization;
using AeroControl.Core.Abstractions;
using AeroControl.Core.Wmi;

namespace AeroControl.Core.Tests;

internal sealed class FakeWmiBridge : IWmiBridge
{
    public HashSet<string> GetMethods { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "getCpuTemp",
        "getGpuTemp1",
        "getRpm1",
        "getRpm2",
        "GetCPUFanDuty",
        "GetGPUFanDuty",
        "GetFixedFanSpeed",
        "GetAutoFanStatus",
        "GetStepFanStatus",
        "GetFixedFanStatus"
    };

    public HashSet<string> SetMethods { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "SetAutoFanStatus",
        "SetStepFanStatus",
        "SetFixedFanStatus",
        "SetFixedFanSpeed",
        "SetGPUFanDuty"
    };

    public Dictionary<string, int> Readings { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["getCpuTemp"] = 70,
        ["getGpuTemp1"] = 61,
        ["getRpm1"] = 4637,
        ["getRpm2"] = 4597,
        ["GetCPUFanDuty"] = 183,
        ["GetGPUFanDuty"] = 183,
        ["GetFixedFanSpeed"] = 183,
        ["GetAutoFanStatus"] = 0,
        ["GetStepFanStatus"] = 1,
        ["GetFixedFanStatus"] = 1
    };

    public List<(string Method, byte Value)> Writes { get; } = [];

    public string Manufacturer { get; set; } = "GIGABYTE";

    public string Model { get; set; } = "AERO 15-SA";

    public string SystemSku { get; set; } = "P75SA";

    public string BiosVersion { get; set; } = "FB09";

    public string? FailOnceOnWriteMethod { get; set; }

    public string? IgnoreWriteMethod { get; set; }

    public bool FailMethodDiscovery { get; set; }

    public IReadOnlySet<string> GetMethodNames(string namespacePath, string className)
    {
        if (FailMethodDiscovery)
        {
            throw new UnauthorizedAccessException("Injected discovery denial.");
        }

        return className.EndsWith("_Get", StringComparison.OrdinalIgnoreCase)
            ? GetMethods
            : SetMethods;
    }

    public WmiCallResult Invoke(
        string namespacePath,
        string className,
        string methodName,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        if (className.EndsWith("_Get", StringComparison.OrdinalIgnoreCase))
        {
            if (!Readings.TryGetValue(methodName, out var reading))
            {
                throw new InvalidOperationException($"No fake reading for {methodName}.");
            }

            return new WmiCallResult(new Dictionary<string, object?>
            {
                ["Data"] = reading
            });
        }

        var value = Convert.ToByte(arguments?["Data"], CultureInfo.InvariantCulture);
        Writes.Add((methodName, value));
        if (string.Equals(FailOnceOnWriteMethod, methodName, StringComparison.OrdinalIgnoreCase))
        {
            FailOnceOnWriteMethod = null;
            throw new InvalidOperationException($"Injected failure for {methodName}.");
        }

        if (string.Equals(IgnoreWriteMethod, methodName, StringComparison.OrdinalIgnoreCase))
        {
            return new WmiCallResult(new Dictionary<string, object?>());
        }

        ApplyWrite(methodName, value);
        return new WmiCallResult(new Dictionary<string, object?>());
    }

    public IReadOnlyDictionary<string, object?> QueryFirst(
        string namespacePath,
        string query)
    {
        if (query.Contains("Win32_ComputerSystem", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Manufacturer"] = Manufacturer,
                ["Model"] = Model,
                ["SystemSKUNumber"] = SystemSku
            };
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SMBIOSBIOSVersion"] = BiosVersion
        };
    }

    private void ApplyWrite(string methodName, byte value)
    {
        switch (methodName)
        {
            case "SetAutoFanStatus":
                Readings["GetAutoFanStatus"] = value;
                break;
            case "SetFixedFanStatus":
                Readings["GetFixedFanStatus"] = value;
                break;
            case "SetStepFanStatus":
                Readings["GetStepFanStatus"] = value;
                break;
            case "SetFixedFanSpeed":
                Readings["GetFixedFanSpeed"] = value;
                Readings["GetCPUFanDuty"] = value;
                break;
            case "SetGPUFanDuty":
                Readings["GetGPUFanDuty"] = value;
                break;
        }
    }
}
