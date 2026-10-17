using HwMonitor.Models;
using Microsoft.Extensions.Options;

namespace HwMonitor.Sensors;

/// <summary>Generated values that move up and down. Used for tests and UI work without admin rights.</summary>
public sealed class FakeSensorSource(IOptions<SensorOptions> options, TimeProvider time) : ISensorSource
{
    private readonly bool _freeze = options.Value.FreezeFake;
    private readonly long _start = time.GetTimestamp();

    public SensorSnapshot Read()
    {
        var t = _freeze ? 0 : time.GetElapsedTime(_start).TotalSeconds;

        // Smooth wave between min and max, with a different speed and phase per sensor.
        double Wave(double min, double max, double period, double phase = 0) =>
            Math.Round(min + (max - min) * (0.5 + 0.5 * Math.Sin(2 * Math.PI * t / period + phase)), 1);

        return new SensorSnapshot(
            time.GetUtcNow(),
            new DeviceReading("/amdcpu/0", "AMD Ryzen 9 5950X", Wave(42, 88, 20), Wave(5, 100, 20), Wave(35, 142, 20), null),
            [new DeviceReading("/gpu-amd/0", "AMD Radeon RX 9070", Wave(38, 74, 30, 1), Wave(0, 99, 30, 1), Wave(12, 220, 30, 1), new MemoryReading(Wave(1.2, 13.8, 30, 1), 16))],
            [
                new TempReading("/lpc/0/temperature/0", "System", "Motherboard", Wave(30, 38, 60)),
                new TempReading("/lpc/0/temperature/1", "VRM MOS", "Motherboard", Wave(40, 68, 25, 2)),
                new TempReading("/lpc/0/temperature/2", "Chipset", "Motherboard", Wave(48, 56, 45, 3)),
                new TempReading("/nvme/0/temperature/0", "Samsung SSD 990 PRO 2TB", "Drives", Wave(36, 52, 50, 4), Wave(0, 60, 15, 4)),
            ],
            [
                new FanReading("/lpc/0/fan/0", "CPU Fan", "Motherboard", Math.Round(Wave(650, 1650, 20))),
                new FanReading("/lpc/0/fan/1", "System Fan #1", "Motherboard", Math.Round(Wave(500, 1100, 40, 2))),
                new FanReading("/gpu-amd/0/fan/0", "GPU Fan", "GPU", Math.Round(Wave(0, 1900, 30, 1))),
            ],
            new MemoryReading(Wave(9.5, 21, 45, 2), 31.9),
            Fps: null,
            new SourceStatus("fake", IsAdmin: false, Message: null));
    }

    public void Dispose() { }
}
