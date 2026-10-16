using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HwMonitor.Sensors;

/// <summary>
/// Reads the CPU temperature and PPT power with the AMD Ryzen Master Monitoring SDK, for when LHM has no value (LHM needs
/// the PawnIO driver for that). Uses the installed SDK (its Platform.dll and driver) through RyzenMasterShim.dll.
/// </summary>
public sealed class RyzenMasterCpuReader : IDisposable
{
    // AMD: at most one call per second, faster calls load the SMU and can give wrong values.
    private static readonly TimeSpan MinReadInterval = TimeSpan.FromMilliseconds(950);

    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private long _lastRead;
    private (double? TempC, double? PowerW) _last;
    private bool _readFailed;

    private RyzenMasterCpuReader(TimeProvider time, ILogger logger)
    {
        _time = time;
        _logger = logger;
    }

    /// <summary>Null when the SDK is not installed or does not start (no admin, no driver, no supported CPU). Only logs.</summary>
    public static RyzenMasterCpuReader? TryCreate(TimeProvider time, ILogger logger)
    {
        var folder = (Registry.GetValue(@"HKEY_LOCAL_MACHINE\Software\AMD\RyzenMasterMonitoringSDK", "InstallationPath", null) as string)
                     ?? Environment.GetEnvironmentVariable("AMDRMMONITORSDKPATH");
        var platformDll = folder is null ? null : Path.Combine(folder, "bin", "Platform.dll");
        if (platformDll is null || !File.Exists(platformDll))
        {
            logger.LogInformation("Ryzen Master SDK not installed");
            return null;
        }

        try
        {
            var result = Init(platformDll);
            if (result != 0)
            {
                var reason = result switch
                {
                    1 => "Platform.dll could not be loaded",
                    2 => "Platform.dll has no GetPlatform",
                    3 => "init failed (needs admin, the Ryzen Master driver and a supported CPU)",
                    4 => "no CPU device",
                    _ => $"error {result}",
                };
                logger.LogInformation("Ryzen Master SDK not available: {Reason}", reason);
                return null;
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            logger.LogInformation("Ryzen Master SDK not available: {Reason}", e.Message);
            return null;
        }

        logger.LogInformation("Ryzen Master SDK started ({Path})", platformDll);
        return new RyzenMasterCpuReader(time, logger);
    }

    /// <summary>The CPU temperature (°C) and PPT power (W). Between calls closer than 1 s it returns the last values.</summary>
    public (double? TempC, double? PowerW) Read()
    {
        var now = _time.GetTimestamp();
        if (_lastRead != 0 && _time.GetElapsedTime(_lastRead, now) < MinReadInterval)
            return _last;
        _lastRead = now;

        var result = ReadNative(out var temp, out var ppt);
        if (result != 0)
        {
            if (!_readFailed)
                _logger.LogWarning("Ryzen Master SDK read failed: status {Status}", result);
            _readFailed = true;
            return _last = (null, null);
        }
        _readFailed = false;
        return _last = (
            SensorRules.IsValidTemp(temp) ? Math.Round(temp, 1) : null,
            ppt > 0 ? Math.Round(ppt, 1) : null);
    }

    public void Dispose() => Close();

    [DllImport("RyzenMasterShim.dll", EntryPoint = "rm_init", CharSet = CharSet.Unicode)]
    private static extern int Init(string platformDllPath);

    [DllImport("RyzenMasterShim.dll", EntryPoint = "rm_read")]
    private static extern int ReadNative(out double temperatureC, out float pptW);

    [DllImport("RyzenMasterShim.dll", EntryPoint = "rm_close")]
    private static extern void Close();
}
