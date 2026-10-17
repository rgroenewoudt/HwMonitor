using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Options;

namespace HwMonitor.Sensors;

/// <summary>FPS and 1% low FPS of one process, from the last second / last 10 seconds of frames.</summary>
public sealed record FrameStats(double Fps, double LowFps);

public interface IFrameMonitor
{
    /// <summary>False when the ETW session could not start (no admin); then the source's FPS is used.</summary>
    bool IsRunning { get; }

    /// <summary>Null when the process presented no steady frames lately (not a game, or paused).</summary>
    FrameStats? Read(int pid);
}

/// <summary>
/// Counts frames with ETW, like FpsOverlayer: a real-time session on Microsoft-Windows-DxgKrnl, only event 184
/// (Present), which every D3D/Vulkan/OpenGL app sends per frame. Needs admin. Only the pid that <see cref="Read"/> asks
/// for is recorded; a new pid starts empty. Frames the driver adds later (AMD AFMF) are not counted.
/// </summary>
public sealed class EtwFrameMonitor : IFrameMonitor, IDisposable
{
    private static readonly Guid DxgKrnl = new("802EC45A-1E99-4B83-9920-87C98277BA9D");
    private const int PresentEventId = 184;
    private const string SessionName = "HwMonitor-Frames";
    private const double WindowMs = 10_000;
    private const double GapMs = 1000; // a longer gap (loading, Alt-Tab) is not a frame time
    private const int MinFramesPerSecond = 10; // below this it is a desktop app that redraws now and then
    private const long StaleMs = 2000; // real-time ETW delivers about once per second

    private readonly ILogger<EtwFrameMonitor> _logger;
    private readonly TraceEventSession? _session;
    private readonly Lock _lock = new();
    private readonly List<double> _frames = []; // event times in ms, oldest first
    private volatile int _pid;
    private long _lastEventTick;

    public EtwFrameMonitor(ILogger<EtwFrameMonitor> logger)
    {
        _logger = logger;
        try
        {
            // Replaces a session with the same name that was left behind (process killed).
            _session = new TraceEventSession(SessionName);
            _session.EnableProvider(DxgKrnl, TraceEventLevel.Informational, ulong.MaxValue,
                new TraceEventProviderOptions { EventIDsToEnable = [PresentEventId] });
            _session.Source.AllEvents += OnEvent;
            new Thread(Process) { IsBackground = true, Name = "ETW frames" }.Start();
            logger.LogInformation("Counting frames with ETW");
        }
        catch (Exception ex)
        {
            logger.LogWarning("Cannot count frames with ETW (needs admin), using the FPS of the sensor source: {Error}", ex.Message);
            _session?.Dispose();
            _session = null;
        }
    }

    public bool IsRunning => _session is not null;

    public FrameStats? Read(int pid)
    {
        lock (_lock)
        {
            if (pid != _pid)
            {
                _pid = pid;
                _frames.Clear();
                return null;
            }
            if (_frames.Count == 0 || Environment.TickCount64 - _lastEventTick > StaleMs)
                return null;

            // Measured back from the newest frame, not from now, because events come in batches.
            var newest = _frames[^1];
            var old = _frames.FindIndex(t => t >= newest - WindowMs);
            _frames.RemoveRange(0, old);

            var all = new List<double>(_frames.Count);
            var lastSecond = new List<double>();
            for (var i = 1; i < _frames.Count; i++)
            {
                var frameTime = _frames[i] - _frames[i - 1];
                if (frameTime >= GapMs)
                    continue;
                all.Add(frameTime);
                if (_frames[i] > newest - 1000)
                    lastSecond.Add(frameTime);
            }
            if (lastSecond.Count < MinFramesPerSecond)
                return null;

            // 1% low: the average of the slowest 1% of frame times, as FPS.
            var slowest = all.OrderDescending().Take(Math.Max(1, all.Count / 100)).Average();
            return new FrameStats(Math.Round(1000 / lastSecond.Average(), 1), Math.Round(1000 / slowest, 1));
        }
    }

    private void OnEvent(TraceEvent e)
    {
        if ((int)e.ID != PresentEventId || e.ProviderGuid != DxgKrnl || e.ProcessID != _pid)
            return;
        lock (_lock)
        {
            if (e.ProcessID != _pid)
                return;
            _frames.Add(e.TimeStampRelativeMSec);
            _lastEventTick = Environment.TickCount64;
        }
    }

    private void Process()
    {
        try
        {
            _session!.Source.Process();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ETW frame session stopped");
        }
    }

    public void Dispose() => _session?.Dispose();
}

/// <summary>Moving values for the fake source (fixed with FreezeFake), so the Game card shows in tests.</summary>
public sealed class FakeFrameMonitor(IOptions<SensorOptions> options, TimeProvider time) : IFrameMonitor
{
    private readonly bool _freeze = options.Value.FreezeFake;
    private readonly long _start = time.GetTimestamp();

    public bool IsRunning => true;

    public FrameStats? Read(int pid)
    {
        var t = _freeze ? 0 : time.GetElapsedTime(_start).TotalSeconds;
        var fps = Math.Round(137.5 + 27.5 * Math.Sin(2 * Math.PI * t / 15 + 0.5), 1);
        return new FrameStats(fps, Math.Round(fps * 0.78, 1));
    }
}
