using HwMonitor.Models;

namespace HwMonitor.Sensors;

/// <summary>Reads all sensors. Only called from one thread (the poller).</summary>
public interface ISensorSource : IDisposable
{
    SensorSnapshot Read();
}
