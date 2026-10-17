namespace HwMonitor.Models;

/// <summary>One reading of all sensors. Sent to the browser every poll.</summary>
public sealed record SensorSnapshot(
    DateTimeOffset Timestamp,
    DeviceReading? Cpu,
    IReadOnlyList<DeviceReading> Gpus,
    IReadOnlyList<TempReading> SystemTemps,
    IReadOnlyList<FanReading> Fans,
    MemoryReading? Memory,
    double? Fps,
    SourceStatus Status,
    GameInfo? Game = null,
    double? FpsLow = null);

// FpsLow is the 1% low FPS (only when frames are counted with ETW).

/// <summary>The app in front, which the FPS is for. The icon is a PNG at /api/game-icon/{IconId}.</summary>
public sealed record GameInfo(string Name, string? IconId);

// Id is stable and unique (the LHM identifier), so the UI can tell two identical devices apart.

/// <summary>A CPU or GPU with its main values. Memory is the GPU's own memory (VRAM); null for the CPU.</summary>
public sealed record DeviceReading(
    string Id,
    string Name,
    double? TempC,
    double? HotSpotC,
    double? LoadPercent,
    double? PowerW,
    MemoryReading? Memory);

/// <summary>Used and total memory in GB (system RAM or GPU memory).</summary>
public sealed record MemoryReading(double UsedGb, double TotalGb);

/// <summary>A temperature that is not the CPU or GPU, e.g. motherboard, VRM or a drive.</summary>
public sealed record TempReading(string Id, string Name, string Group, double TempC);

public sealed record FanReading(string Id, string Name, string Source, double Rpm);

/// <summary>Tells the UI where the data comes from and if something is missing.</summary>
public sealed record SourceStatus(string Source, bool IsAdmin, string? Message);
