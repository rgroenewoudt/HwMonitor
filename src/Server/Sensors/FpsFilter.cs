namespace HwMonitor.Sensors;

/// <summary>
/// Removes short FPS spikes. ADLX sometimes reports >1000 FPS for one poll when switching to a
/// game (loading screen, first frames). Returns the median of the last 3 polls, with "no FPS" counted as 0, so one
/// odd value is never shown and FPS appears one poll later than before.
/// </summary>
public sealed class FpsFilter
{
    private readonly double[] _last = new double[3];
    private int _next;

    public double? Add(double? fps)
    {
        _last[_next] = fps ?? 0;
        _next = (_next + 1) % _last.Length;
        var sorted = _last.Order().ToArray();
        var median = sorted[sorted.Length / 2];
        return median > 0 ? median : null;
    }
}
