using ADLXWrapper;
using ADLXWrapper.Bindings;
using HwMonitor.Models;

namespace HwMonitor.Sensors;

/// <summary>
/// Reads AMD GPUs and the game FPS with AMD ADLX (the AMD driver's own API).
/// Uses the SWIG bindings of ADLXWrapper directly: its high-level classes have no usage, VRAM or FPS.
/// </summary>
public sealed class AdlxGpuReader : IDisposable
{
    private readonly SafeADLXHelper _helper;
    private readonly IADLXPerformanceMonitoringServices _perf;
    private readonly IReadOnlyList<Gpu> _gpus;
    private readonly bool _showZeroRpmFans;
    private readonly SWIGTYPE_p_p_adlx__IADLXGPUMetrics _metricsPtr = ADLX.new_metricsP_Ptr();
    private readonly SWIGTYPE_p_p_adlx__IADLXFPS _fpsPtr = ADLX.new_fpsP_Ptr();

    private AdlxGpuReader(SafeADLXHelper helper, IADLXPerformanceMonitoringServices perf, IReadOnlyList<Gpu> gpus, bool showZeroRpmFans)
    {
        _helper = helper;
        _perf = perf;
        _gpus = gpus;
        _showZeroRpmFans = showZeroRpmFans;
    }

    public IEnumerable<string> GpuNames => _gpus.Select(g => g.Name);

    /// <summary>Null when ADLX is not there (no AMD GPU or driver) or finds no GPU. That is normal, so it only logs.</summary>
    public static AdlxGpuReader? TryCreate(bool showZeroRpmFans, ILogger logger)
    {
        SafeADLXHelper? helper = null;
        try
        {
            helper = new SafeADLXHelper();
            helper.Initialize().ThrowIfError("Initialize");
            var system = helper.GetSystemServices();

            var perfPtr = ADLX.new_performanceP_Ptr();
            system.GetPerformanceMonitoringServices(perfPtr).ThrowIfError("Get performance monitoring services");
            var perf = ADLX.performanceP_Ptr_value(perfPtr);
            ADLX.delete_performanceP_Ptr(perfPtr);

            var gpus = ReadGpuList(system, perf);
            if (gpus.Count == 0)
            {
                logger.LogInformation("ADLX found no GPU");
                perf.Release();
                helper.Terminate();
                helper.Dispose();
                return null;
            }
            return new AdlxGpuReader(helper, perf, gpus, showZeroRpmFans);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
                                      or TypeInitializationException or ADLXEception)
        {
            logger.LogInformation("ADLX not available: {Reason}", e.Message);
            try { helper?.Terminate(); helper?.Dispose(); } catch (Exception) { /* nothing more to do */ }
            return null;
        }
    }

    private static List<Gpu> ReadGpuList(IADLXSystem system, IADLXPerformanceMonitoringServices perf)
    {
        var listPtr = ADLX.new_gpuListP_Ptr();
        system.GetGPUs(listPtr).ThrowIfError("Get GPUs");
        var list = ADLX.gpuListP_Ptr_value(listPtr);
        var gpuPtr = ADLX.new_gpuP_Ptr();
        var supportPtr = ADLX.new_metricsSupportP_Ptr();
        var gpus = new List<Gpu>();
        try
        {
            for (var i = list.Begin(); i < list.End(); i++)
            {
                list.At(i, gpuPtr).ThrowIfError("Get GPU");
                var native = ADLX.gpuP_Ptr_value(gpuPtr);

                var namePtr = ADLX.new_stringP_Ptr();
                native.Name(namePtr).ThrowIfError("Get GPU name");
                var name = ADLX.stringP_Ptr_value(namePtr);
                ADLX.delete_stringP_Ptr(namePtr);

                var uniqueId = 0;
                native.UniqueId(ref uniqueId).ThrowIfError("Get GPU unique id");

                var vramPtr = ADLX.new_uintP();
                var totalVramMb = native.TotalVRAM(vramPtr).HasError() ? 0 : ADLX.uintP_value(vramPtr);
                ADLX.delete_uintP(vramPtr);

                perf.GetSupportedGPUMetrics(native, supportPtr).ThrowIfError("Get supported GPU metrics");
                var support = ADLX.metricsSupportP_Ptr_value(supportPtr);
                gpus.Add(new Gpu(native, $"/adlx/gpu/{uniqueId}", name, totalVramMb,
                    Temp: IsSupported(support.IsSupportedGPUTemperature),
                    Usage: IsSupported(support.IsSupportedGPUUsage),
                    BoardPower: IsSupported(support.IsSupportedGPUTotalBoardPower),
                    Power: IsSupported(support.IsSupportedGPUPower),
                    Fan: IsSupported(support.IsSupportedGPUFanSpeed),
                    Vram: IsSupported(support.IsSupportedGPUVRAM)));
                support.Release();
            }
        }
        finally
        {
            ADLX.delete_metricsSupportP_Ptr(supportPtr);
            ADLX.delete_gpuP_Ptr(gpuPtr);
            list.Release();
            ADLX.delete_gpuListP_Ptr(listPtr);
        }
        return gpus;
    }

    private delegate ADLX_RESULT BoolGetter(ref bool value);

    private static bool IsSupported(BoolGetter getter)
    {
        var value = false;
        return !getter(ref value).HasError() && value;
    }

    /// <summary>One reading per GPU; adds the GPU fans to <paramref name="fans"/>. A GPU that fails this time is left out.</summary>
    public List<DeviceReading> ReadGpus(List<FanReading> fans)
    {
        var readings = new List<DeviceReading>();
        foreach (var gpu in _gpus)
        {
            if (_perf.GetCurrentGPUMetrics(gpu.Native, _metricsPtr).HasError())
                continue;
            var metrics = ADLX.metricsP_Ptr_value(_metricsPtr);
            try
            {
                var temp = gpu.Temp ? ValidTemp(Get(metrics.GPUTemperature)) : null;
                var load = gpu.Usage ? Get(metrics.GPUUsage) : null;
                var power = (gpu.BoardPower ? ValidPower(Get(metrics.GPUTotalBoardPower)) : null)
                            ?? (gpu.Power ? ValidPower(Get(metrics.GPUPower)) : null);
                MemoryReading? memory = gpu.Vram && gpu.TotalVramMb > 0 && GetInt(metrics.GPUVRAM) is { } usedMb
                    ? new MemoryReading(Math.Round(usedMb / 1024.0, 1), Math.Round(gpu.TotalVramMb / 1024.0, 1))
                    : null;

                if (gpu.Fan && GetInt(metrics.GPUFanSpeed) is { } rpm && (rpm > 0 || _showZeroRpmFans))
                    fans.Add(new FanReading($"{gpu.Id}/fan", "GPU Fan", "GPU", rpm));

                if (temp is not null || load is not null)
                    readings.Add(new DeviceReading(gpu.Id, gpu.Name, temp, load is { } l ? Math.Round(l, 1) : null, power, memory));
            }
            finally
            {
                metrics.Release();
            }
        }
        return readings;
    }

    /// <summary>FPS of the game in front; null when no game runs (ADLX then returns an error).</summary>
    public double? ReadFps()
    {
        if (_perf.GetCurrentFPS(_fpsPtr).HasError())
            return null;
        var fps = ADLX.fpsP_Ptr_value(_fpsPtr);
        try
        {
            var value = 0;
            return !fps.FPS(ref value).HasError() && value > 0 ? value : null;
        }
        finally
        {
            fps.Release();
        }
    }

    private delegate ADLX_RESULT DoubleGetter(ref double value);
    private delegate ADLX_RESULT IntGetter(ref int value);

    private static double? Get(DoubleGetter getter)
    {
        double value = 0;
        return getter(ref value).HasError() ? null : value;
    }

    private static int? GetInt(IntGetter getter)
    {
        var value = 0;
        return getter(ref value).HasError() ? null : value;
    }

    private static double? ValidTemp(double? value) => SensorRules.IsValidTemp(value) ? Math.Round(value!.Value, 1) : null;

    private static double? ValidPower(double? value) => value > 0 ? Math.Round(value.Value, 1) : null;

    public void Dispose()
    {
        foreach (var gpu in _gpus)
            gpu.Native.Release();
        _perf.Release();
        ADLX.delete_metricsP_Ptr(_metricsPtr);
        ADLX.delete_fpsP_Ptr(_fpsPtr);
        _helper.Terminate();
        _helper.Dispose();
    }

    private sealed record Gpu(
        IADLXGPU Native, string Id, string Name, uint TotalVramMb,
        bool Temp, bool Usage, bool BoardPower, bool Power, bool Fan, bool Vram);
}
