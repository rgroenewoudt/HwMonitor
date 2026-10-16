using HwMonitor.Models;
using Microsoft.Extensions.Options;

namespace HwMonitor.Sensors;

/// <summary>
/// HWiNFO first: when its shared memory is there, all values come from HWiNFO. Else LibreHardwareMonitor (+ ADLX).
/// With <see cref="SensorSourceKind.HwInfo"/> it never falls back.
/// </summary>
public sealed class AutoSensorSource : ISensorSource
{
    private const string NoHwInfoMessage = "HWiNFO shared memory not found. Start HWiNFO and turn on 'Shared Memory Support'.";

    private readonly IServiceProvider _services;
    private readonly SensorOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<AutoSensorSource> _logger;
    private readonly HwInfoSensorSource _hwInfo;
    private readonly SourceStatus _noHwInfoStatus = new("hwinfo", SensorRules.IsAdmin(), NoHwInfoMessage);
    private LhmSensorSource? _lhm;
    private bool? _usingHwInfo;

    public AutoSensorSource(IServiceProvider services, IOptions<SensorOptions> options, TimeProvider time, ILogger<AutoSensorSource> logger)
    {
        _services = services;
        _options = options.Value;
        _time = time;
        _logger = logger;
        _hwInfo = ActivatorUtilities.CreateInstance<HwInfoSensorSource>(services);
    }

    private bool HwInfoOnly => _options.Source == SensorSourceKind.HwInfo;

    public SensorSnapshot Read()
    {
        var snapshot = _hwInfo.TryRead();
        if (snapshot is null && _usingHwInfo != false)
        {
            if (HwInfoOnly)
                _logger.LogWarning("{Message}", NoHwInfoMessage);
            else
                _logger.LogInformation("HWiNFO shared memory not found, using LibreHardwareMonitor");
        }
        _usingHwInfo = snapshot is not null;

        if (snapshot is not null)
            return snapshot;
        if (HwInfoOnly)
            return new SensorSnapshot(_time.GetUtcNow(), null, [], [], [], null, null, _noHwInfoStatus);

        // Created only when needed, so the LHM driver is not loaded while HWiNFO works. Kept for the next switch.
        _lhm ??= ActivatorUtilities.CreateInstance<LhmSensorSource>(_services);
        return _lhm.Read();
    }

    public void Dispose()
    {
        _hwInfo.Dispose();
        _lhm?.Dispose();
    }
}
