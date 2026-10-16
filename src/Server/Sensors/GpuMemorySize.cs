using System.Collections.Concurrent;
using Microsoft.Win32;

namespace HwMonitor.Sensors;

/// <summary>
/// Total VRAM of a GPU from the display driver's registry key (<c>HardwareInformation.qwMemorySize</c>).
/// Readable without admin. Found by the driver name, e.g. "AMD Radeon RX 9070".
/// </summary>
public static class GpuMemorySize
{
    private const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    private static readonly ConcurrentDictionary<string, double?> Cache = new();

    public static double? TotalGb(string gpuName) => Cache.GetOrAdd(gpuName, Read);

    private static double? Read(string gpuName)
    {
        try
        {
            using var displays = Registry.LocalMachine.OpenSubKey(DisplayClass);
            foreach (var keyName in displays?.GetSubKeyNames() ?? [])
            {
                using var key = displays!.OpenSubKey(keyName);
                if (key?.GetValue("DriverDesc") is not string desc || !desc.Equals(gpuName, StringComparison.OrdinalIgnoreCase))
                    continue;
                // REG_QWORD on new drivers, REG_BINARY on some older ones.
                var bytes = key.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long value => value,
                    byte[] { Length: >= 8 } raw => BitConverter.ToInt64(raw),
                    _ => 0,
                };
                if (bytes > 0)
                    return bytes / 1024.0 / 1024 / 1024;
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // No total then; the card shows no memory bar.
        }
        return null;
    }
}
