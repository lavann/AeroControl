namespace AeroControl.Core.Models;

public sealed record AlertThresholds(
    int CpuTemperatureCelsius,
    int GpuTemperatureCelsius,
    bool EnableFanStallAlert);

public sealed record AlertNotice(string Key, string Title, string Message);
