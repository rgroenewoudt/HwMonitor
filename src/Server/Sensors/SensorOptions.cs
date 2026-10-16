namespace HwMonitor.Sensors;

public sealed class SensorOptions
{
    public const string Section = "Sensors";

    /// <summary>Use generated values instead of real hardware (for tests and UI work).</summary>
    public bool UseFake { get; set; }

    /// <summary>With UseFake: always return the same values (for screenshot tests).</summary>
    public bool FreezeFake { get; set; }

    public int PollIntervalMs { get; set; } = 1000;

    public bool ShowZeroRpmFans { get; set; }
}
