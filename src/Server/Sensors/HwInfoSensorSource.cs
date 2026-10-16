using System.Text.RegularExpressions;
using HwMonitor.Models;
using Microsoft.Extensions.Options;

namespace HwMonitor.Sensors;

/// <summary>
/// Turns the HWiNFO shared memory into a snapshot. Matches on HWiNFO's original (English) names,
/// shows the names the user sees in HWiNFO.
/// </summary>
public sealed partial class HwInfoSensorSource : IDisposable
{
    private readonly HwInfoSharedMemory _memory;
    private readonly SensorOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<HwInfoSensorSource> _logger;
    private readonly SourceStatus _status = new("hwinfo", SensorRules.IsAdmin(), null);
    private bool _logged;

    public HwInfoSensorSource(IOptions<SensorOptions> options, TimeProvider time, ILogger<HwInfoSensorSource> logger)
    {
        _options = options.Value;
        _time = time;
        _logger = logger;
        _memory = new HwInfoSharedMemory(time, logger);
    }

    /// <summary>Null when HWiNFO shared memory is not there or not updated.</summary>
    public SensorSnapshot? TryRead()
    {
        if (_memory.TryRead() is not { } sensors)
        {
            _logged = false;
            return null;
        }
        if (!_logged)
            LogSensors(sensors);
        _logged = true;
        return Map(sensors);
    }

    private SensorSnapshot Map(IReadOnlyList<HwInfoSensor> sensors)
    {
        DeviceReading? cpu = null;
        var gpus = new List<DeviceReading>();
        var systemTemps = new List<TempReading>();
        var fans = new List<FanReading>();
        MemoryReading? memory = null;
        var others = new List<HwInfoSensor>();

        // HWiNFO splits one CPU or GPU into several groups ("CPU [#0]: AMD Ryzen 7 7800X3D: Enhanced"); merge them.
        var devices = sensors
            .Select(s => (Sensor: s, Match: DeviceGroup().Match(s.NameOrig)))
            .Where(x => x.Match.Success)
            .GroupBy(x => (Kind: x.Match.Groups["kind"].Value, Index: x.Match.Groups["index"].Value))
            .ToList();

        foreach (var device in devices)
        {
            var groups = device.Select(x => x.Sensor).ToList();
            var first = groups[0];
            var id = Id(first);
            var name = device.First().Match.Groups["name"].Value.Trim();
            var readings = groups.SelectMany(s => s.Readings).ToList();

            if (device.Key.Kind == "CPU")
            {
                cpu ??= ReadCpu(id, name, readings);
                continue;
            }

            var gpu = ReadGpu(id, name, readings);
            if (gpu.TempC is not null || gpu.LoadPercent is not null)
                gpus.Add(gpu);
            foreach (var group in groups)
                AddFans(fans, group, "GPU", r => r.Label);
        }

        var deviceSensors = devices.SelectMany(d => d.Select(x => x.Sensor)).ToHashSet();
        foreach (var sensor in sensors.Where(s => !deviceSensors.Contains(s)))
        {
            if (sensor.NameOrig.StartsWith("System:", StringComparison.Ordinal))
            {
                memory ??= Memory(sensor.Readings, "Physical Memory Used", "Physical Memory Available");
                continue;
            }

            if (DriveGroup().Match(sensor.NameOrig) is { Success: true } drive)
            {
                // NVMe drives also report their "Warning" and "Critical" limits as temperatures.
                static bool IsDriveTemp(HwInfoReading r) => !r.LabelOrig.Contains("Warning") && !r.LabelOrig.Contains("Critical");
                var tempCount = sensor.Readings.Count(r => r.Type == HwInfoReadingType.Temp && IsDriveTemp(r));
                var driveName = DriveNames.Pretty(drive.Groups["model"].Value.Trim());
                AddTemps(systemTemps, sensor, "Drives", r => tempCount == 1 ? driveName : $"{driveName} · {r.Label}", IsDriveTemp);
                continue;
            }

            others.Add(sensor);
        }

        // Board chips, DIMMs, coolers, ...: add the chip name only to names that more than one group has.
        var repeatedTemps = SensorRules.RepeatedNames(others.Select(s => Labels(s, HwInfoReadingType.Temp)));
        var repeatedFans = SensorRules.RepeatedNames(others.Select(s => Labels(s, HwInfoReadingType.Fan)));
        var chips = ChipNames(others);
        foreach (var sensor in others)
        {
            var chip = chips[sensor];
            AddTemps(systemTemps, sensor, "Motherboard",
                r => repeatedTemps.Contains(r.Label) ? $"{chip} · {r.Label}" : r.Label);
            AddFans(fans, sensor, "Motherboard",
                r => repeatedFans.Contains(r.Label) ? $"{chip} · {r.Label}" : r.Label);
        }

        var fps = sensors.SelectMany(s => s.Readings).Where(r => r.LabelOrig.StartsWith("Framerate", StringComparison.Ordinal)).ToList();
        return new SensorSnapshot(_time.GetUtcNow(), cpu, gpus, systemTemps, fans, memory, Fps(fps), _status);
    }

    private static DeviceReading ReadCpu(string id, string name, List<HwInfoReading> readings)
    {
        // AMD: "CPU (Tctl/Tdie)", Intel: "CPU Package". Fallback: hottest core.
        var temp = Temp(readings, "CPU (Tctl/Tdie)", "CPU Package", "CPU (Tdie)", "CPU Die (average)")
                   ?? readings
                       .Where(r => r.Type == HwInfoReadingType.Temp && r.LabelOrig.StartsWith("Core", StringComparison.Ordinal))
                       .Select(r => Celsius(r))
                       .Where(t => SensorRules.IsValidTemp(t))
                       .Max(t => (double?)Math.Round(t, 1));

        return new DeviceReading(
            id,
            name,
            temp,
            HotSpotC: null,
            Value(readings, HwInfoReadingType.Usage, "Total CPU Usage"),
            Power(readings, "CPU Package Power", "CPU PPT"),
            Memory: null);
    }

    private static DeviceReading ReadGpu(string id, string name, List<HwInfoReading> readings) => new(
        id,
        name,
        Temp(readings, "GPU Temperature"),
        Temp(readings, "GPU Hot Spot Temperature", "GPU Hotspot Temperature", "GPU Hot Spot"),
        Value(readings, HwInfoReadingType.Usage, "GPU Core Load", "GPU Utilization", "GPU D3D Usage"),
        Power(readings, "GPU Power", "Total Board Power (TBP)", "Total Graphics Power (TGP)", "GPU ASIC Power"),
        Memory(readings, "GPU Memory Allocated", "GPU Memory Available") ?? DedicatedMemory(name, readings));

    /// <summary>HWiNFO has no free or total VRAM for AMD; the total comes from the driver's registry key.</summary>
    private static MemoryReading? DedicatedMemory(string gpuName, List<HwInfoReading> readings)
    {
        if (readings.FirstOrDefault(r => r.LabelOrig == "GPU D3D Memory Dedicated") is not { } used
            || GpuMemorySize.TotalGb(gpuName) is not { } totalGb)
            return null;
        return new MemoryReading(Math.Round(Gb(used), 1), Math.Round(totalGb, 1));
    }

    /// <summary>Used and total memory in GB. Total = used + free.</summary>
    private static MemoryReading? Memory(IEnumerable<HwInfoReading> readings, string usedName, string freeName)
    {
        var list = readings.ToList();
        if (list.FirstOrDefault(r => r.LabelOrig == usedName) is not { } used
            || list.FirstOrDefault(r => r.LabelOrig == freeName) is not { } free)
            return null;
        var usedGb = Gb(used);
        var totalGb = usedGb + Gb(free);
        return totalGb > 0 ? new MemoryReading(Math.Round(usedGb, 1), Math.Round(totalGb, 1)) : null;
    }

    private static double Gb(HwInfoReading r) => r.Unit switch
    {
        "GB" => r.Value,
        "KB" => r.Value / 1024 / 1024,
        _ => r.Value / 1024, // HWiNFO uses MB
    };

    /// <summary>
    /// The game FPS: the "PresentMon" group first. The GPU group's own "Framerate" often stays 0 (it only works for some
    /// games), so a name only counts when its value is above 0.
    /// </summary>
    private static double? Fps(List<HwInfoReading> readings)
    {
        foreach (var name in (string[])["Framerate Presented (avg)", "Framerate Displayed (avg)", "Framerate (Presented)", "Framerate"])
        {
            if (readings.FirstOrDefault(r => r.LabelOrig == name && r.Value > 0) is { } reading)
                return Math.Round(reading.Value, 1);
        }
        return null;
    }

    private static void AddTemps(List<TempReading> list, HwInfoSensor sensor, string group, Func<HwInfoReading, string> name,
        Func<HwInfoReading, bool>? include = null)
    {
        foreach (var r in sensor.Readings)
        {
            if (r.Type == HwInfoReadingType.Temp && Celsius(r) is var temp && SensorRules.IsValidTemp(temp) && (include?.Invoke(r) ?? true))
                list.Add(new TempReading(Id(sensor, r), name(r), group, Math.Round(temp, 1)));
        }
    }

    private void AddFans(List<FanReading> list, HwInfoSensor sensor, string source, Func<HwInfoReading, string> name)
    {
        foreach (var r in sensor.Readings)
        {
            if (r.Type == HwInfoReadingType.Fan && (r.Value > 0 || _options.ShowZeroRpmFans))
                list.Add(new FanReading(Id(sensor, r), name(r), source, Math.Round(r.Value)));
        }
    }

    /// <summary>Value of the first reading that matches one of the names, in order of the names.</summary>
    private static double? Value(List<HwInfoReading> readings, HwInfoReadingType type, params string[] names)
    {
        foreach (var name in names)
        {
            if (readings.FirstOrDefault(r => r.Type == type && r.LabelOrig == name) is { } reading)
                return Math.Round(reading.Value, 1);
        }
        return null;
    }

    private static double? Temp(List<HwInfoReading> readings, params string[] names)
    {
        foreach (var name in names)
        {
            if (readings.FirstOrDefault(r => r.Type == HwInfoReadingType.Temp && r.LabelOrig == name) is { } reading
                && Celsius(reading) is var temp && SensorRules.IsValidTemp(temp))
                return Math.Round(temp, 1);
        }
        return null;
    }

    private static double? Power(List<HwInfoReading> readings, params string[] names)
    {
        foreach (var name in names)
        {
            if (readings.FirstOrDefault(r => r.Type == HwInfoReadingType.Power && r.LabelOrig == name) is { Value: > 0 } reading)
                return Math.Round(reading.Value, 1);
        }
        return null;
    }

    // HWiNFO can be set to show °F.
    private static double Celsius(HwInfoReading r) => r.Unit.EndsWith('F') ? (r.Value - 32) * 5 / 9 : r.Value;

    /// <summary>
    /// Short name per group: the chip in brackets ("GIGABYTE X570 AORUS ELITE (ITE IT8688E)" → "ITE IT8688E"),
    /// with "#2", "#3" when a board has the same chip more than once.
    /// </summary>
    private static Dictionary<HwInfoSensor, string> ChipNames(List<HwInfoSensor> sensors)
    {
        var result = new Dictionary<HwInfoSensor, string>();
        foreach (var same in sensors.GroupBy(s => ChipName().Match(s.Name) is { Success: true } m ? m.Groups["chip"].Value : s.Name))
        {
            var number = 0;
            foreach (var sensor in same)
                result[sensor] = ++number == 1 ? same.Key : $"{same.Key} #{number}";
        }
        return result;
    }

    private static IEnumerable<string> Labels(HwInfoSensor sensor, HwInfoReadingType type) =>
        sensor.Readings.Where(r => r.Type == type).Select(r => r.Label);

    private static string Id(HwInfoSensor sensor) => $"/hwinfo/{sensor.Id:x}/{sensor.Instance}";

    private static string Id(HwInfoSensor sensor, HwInfoReading reading) => $"{Id(sensor)}/{reading.Id:x}";

    private void LogSensors(IReadOnlyList<HwInfoSensor> sensors)
    {
        _logger.LogInformation("Using HWiNFO shared memory");
        foreach (var sensor in sensors)
        {
            _logger.LogInformation("Found HWiNFO sensor: {Name}", sensor.NameOrig);
            foreach (var r in sensor.Readings)
                _logger.LogDebug("  {Label} ({Type}, {Unit}) = {Value}", r.LabelOrig, r.Type, r.Unit, r.Value);
        }
    }

    /// <summary>
    /// "CPU [#0]: AMD Ryzen 7 7800X3D: Enhanced" → kind CPU, index 0, name "AMD Ryzen 7 7800X3D".
    /// GPUs are "dGPU [#0]: …" (or "iGPU", older versions "GPU").
    /// </summary>
    [GeneratedRegex(@"^(?<kind>CPU|[di]?GPU) \[#(?<index>\d+)\]:\s*(?<name>[^:]+)")]
    private static partial Regex DeviceGroup();

    /// <summary>"S.M.A.R.T.: WDS100T1X0E-00AFY0 (204540801238) [C:]" → model without the serial and the drive letters.</summary>
    [GeneratedRegex(@"^(S\.M\.A\.R\.T\.|Drive):\s*(?<model>.+?)(\s*\([^)]*\))?(\s*\[[^\]]*\])?$")]
    private static partial Regex DriveGroup();

    [GeneratedRegex(@"\((?<chip>[^()]+)\)$")]
    private static partial Regex ChipName();

    public void Dispose() => _memory.Dispose();
}
