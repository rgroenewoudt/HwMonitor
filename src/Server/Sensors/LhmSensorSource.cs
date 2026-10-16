using System.Security.Principal;
using HwMonitor.Models;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Options;

namespace HwMonitor.Sensors;

/// <summary>Reads real hardware sensors with LibreHardwareMonitor.</summary>
public sealed class LhmSensorSource : ISensorSource
{
    private const double MaxValidTempC = 125;

    private readonly Computer _computer;
    private readonly SensorOptions _options;
    private readonly SourceStatus _status;
    private readonly TimeProvider _time;

    public LhmSensorSource(IOptions<SensorOptions> options, TimeProvider time, ILogger<LhmSensorSource> logger)
    {
        _options = options.Value;
        _time = time;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsStorageEnabled = true,
        };
        _computer.Open();

        var isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        var driverInstalled = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;
        string? message = (isAdmin, driverInstalled) switch
        {
            (false, _) => "Not running as administrator. CPU, motherboard and fan values may be missing.",
            (_, false) => "PawnIO driver is not installed. CPU, motherboard and fan values may be missing.",
            _ => null,
        };
        _status = new SourceStatus("hardware", isAdmin, driverInstalled, message);

        if (message is not null)
            logger.LogWarning("{Message}", message);
        foreach (var hardware in _computer.Hardware)
            logger.LogInformation("Found hardware: {Type} {Name}", hardware.HardwareType, hardware.Name);
    }

    public SensorSnapshot Read()
    {
        DeviceReading? cpu = null;
        var gpus = new List<DeviceReading>();
        var systemTemps = new List<TempReading>();
        var fans = new List<FanReading>();

        foreach (var hardware in _computer.Hardware)
        {
            UpdateRecursive(hardware);

            switch (hardware.HardwareType)
            {
                case HardwareType.Cpu:
                    cpu ??= ReadCpu(hardware);
                    break;

                case HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel:
                    var gpu = ReadGpu(hardware);
                    if (gpu.TempC is not null || gpu.LoadPercent is not null)
                        gpus.Add(gpu);
                    AddFans(fans, hardware, "GPU", sensor => sensor.Name);
                    break;

                case HardwareType.Motherboard:
                    // All board chips (SuperIO, Embedded Controller) share one group.
                    // Add the chip name only to names that more than one chip has.
                    var chips = Flatten(hardware).ToList();
                    var repeatedTemps = RepeatedNames(chips, SensorType.Temperature);
                    var repeatedFans = RepeatedNames(chips, SensorType.Fan);
                    foreach (var sub in chips)
                    {
                        AddTemps(systemTemps, sub, "Motherboard",
                            sensor => repeatedTemps.Contains(sensor.Name) ? $"{sub.Name} · {sensor.Name}" : sensor.Name);
                        AddFans(fans, sub, "Motherboard",
                            sensor => repeatedFans.Contains(sensor.Name) ? $"{sub.Name} · {sensor.Name}" : sensor.Name);
                    }
                    break;

                case HardwareType.Storage:
                    var tempCount = hardware.Sensors.Count(s => s.SensorType == SensorType.Temperature);
                    AddTemps(systemTemps, hardware, "Drives",
                        sensor => tempCount == 1 ? hardware.Name : $"{hardware.Name} Â· {sensor.Name}");
                    break;

                case HardwareType.Cooler:
                    foreach (var sub in Flatten(hardware))
                    {
                        AddTemps(systemTemps, sub, "Cooling", sensor => $"{sub.Name} Â· {sensor.Name}");
                        AddFans(fans, sub, sub.Name, sensor => sensor.Name);
                    }
                    break;
            }
        }

        return new SensorSnapshot(_time.GetUtcNow(), cpu, gpus, systemTemps, fans, Fps: null, _status);
    }

    private static DeviceReading ReadCpu(IHardware cpu)
    {
        // AMD: "Core (Tctl/Tdie)", Intel: "CPU Package". Fallback: hottest core.
        var temp = Value(cpu, SensorType.Temperature, "Core (Tctl/Tdie)", "Tctl/Tdie", "CPU Package", "Package")
                   ?? cpu.Sensors
                       .Where(s => s.SensorType == SensorType.Temperature && s.Name.Contains("Core") && IsValidTemp(s.Value))
                       .Max(s => (double?)Math.Round(s.Value!.Value, 1));

        return new DeviceReading(
            cpu.Identifier.ToString(),
            cpu.Name,
            temp,
            HotSpotC: null,
            Value(cpu, SensorType.Load, "CPU Total"),
            Value(cpu, SensorType.Power, "Package", "CPU Package"));
    }

    private static DeviceReading ReadGpu(IHardware gpu) => new(
        gpu.Identifier.ToString(),
        gpu.Name,
        Value(gpu, SensorType.Temperature, "GPU Core"),
        Value(gpu, SensorType.Temperature, "GPU Hot Spot", "GPU Hotspot"),
        Value(gpu, SensorType.Load, "GPU Core", "D3D 3D"),
        Value(gpu, SensorType.Power, "GPU Package", "GPU Power", "GPU Core"));

    private static void AddTemps(List<TempReading> list, IHardware hardware, string group, Func<ISensor, string> name)
    {
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType == SensorType.Temperature && IsValidTemp(sensor.Value))
                list.Add(new TempReading(sensor.Identifier.ToString(), name(sensor), group, Math.Round(sensor.Value!.Value, 1)));
        }
    }

    private void AddFans(List<FanReading> list, IHardware hardware, string source, Func<ISensor, string> name)
    {
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType != SensorType.Fan || sensor.Value is not { } rpm)
                continue;
            if (rpm > 0 || _options.ShowZeroRpmFans)
                list.Add(new FanReading(sensor.Identifier.ToString(), name(sensor), source, Math.Round(rpm)));
        }
    }

    /// <summary>Value of the first sensor that matches one of the names, in order of the names.</summary>
    private static double? Value(IHardware hardware, SensorType type, params string[] names)
    {
        foreach (var name in names)
        {
            var sensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name == name && s.Value is not null);
            if (sensor is null)
                continue;
            // Without the driver some sensors exist but stay at 0.
            if ((type == SensorType.Temperature && !IsValidTemp(sensor.Value)) || (type == SensorType.Power && sensor.Value <= 0))
                continue;
            return Math.Round(sensor.Value!.Value, 1);
        }
        return null;
    }

    // Unused SuperIO inputs often show 0, -128 or 255 Â°C.
    private static bool IsValidTemp(float? value) => value is > 0 and < (float)MaxValidTempC;

    /// <summary>Sensor names of this type that exist on more than one of the hardware items.</summary>
    private static HashSet<string> RepeatedNames(IEnumerable<IHardware> hardware, SensorType type) =>
        hardware.SelectMany(h => h.Sensors.Where(s => s.SensorType == type).Select(s => s.Name).Distinct())
            .GroupBy(name => name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

    private static IEnumerable<IHardware> Flatten(IHardware hardware) =>
        new[] { hardware }.Concat(hardware.SubHardware.SelectMany(Flatten));

    private static void UpdateRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware)
            UpdateRecursive(sub);
    }

    public void Dispose() => _computer.Close();
}
