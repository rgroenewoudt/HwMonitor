namespace HwMonitor.Models;

/// <summary>One reading of all sensors. Sent to the browser every poll.</summary>
public sealed record SensorSnapshot(
    DateTimeOffset Timestamp,
    DeviceReading? Cpu,
    IReadOnlyList<DeviceReading> Gpus,
    IReadOnlyList<TempReading> SystemTemps,
    IReadOnlyList<FanReading> Fans,
    double? Fps,
    SourceStatus Status);

// Id is stable and unique (the LHM identifier), so the UI can tell two identical devices apart.

/// <summary>A CPU or GPU with its main values.</summary>
public sealed record DeviceReading(
    string Id,
    string Name,
    double? TempC,
    double? HotSpotC,
    double? LoadPercent,
    double? PowerW);

/// <summary>A temperature that is not the CPU or GPU, e.g. motherboard, VRM or a drive.</summary>
public sealed record TempReading(string Id, string Name, string Group, double TempC);

public sealed record FanReading(string Id, string Name, string Source, double Rpm);

/// <summary>Tells the UI where the data comes from and if something is missing.</summary>
public sealed record SourceStatus(string Source, bool IsAdmin, bool DriverInstalled, string? Message);
