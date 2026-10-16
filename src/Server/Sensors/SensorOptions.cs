namespace HwMonitor.Sensors;

public enum SensorSourceKind
{
    /// <summary>HWiNFO shared memory when it is there, else LibreHardwareMonitor.</summary>
    Auto,
    HwInfo,
    Lhm,
}

public sealed class SensorOptions
{
    public const string Section = "Sensors";

    /// <summary>Where real values come from. <see cref="UseFake"/> wins over this.</summary>
    public SensorSourceKind Source { get; set; } = SensorSourceKind.Auto;

    /// <summary>Use generated values instead of real hardware (for tests and UI work).</summary>
    public bool UseFake { get; set; }

    /// <summary>With UseFake: always return the same values (for screenshot tests).</summary>
    public bool FreezeFake { get; set; }

    public int PollIntervalMs { get; set; } = 1000;

    public bool ShowZeroRpmFans { get; set; }

    /// <summary>Read AMD GPUs (and the game FPS) with AMD ADLX instead of LHM. Without an AMD driver it is skipped.</summary>
    public bool UseAdlx { get; set; } = true;

    /// <summary>
    /// AMD CPUs: read temperature and power with the AMD Ryzen Master Monitoring SDK when LHM has no value.
    /// Needs the SDK installed and admin; else it is skipped.
    /// </summary>
    public bool UseRyzenMaster { get; set; } = true;

    /// <summary>HWiNFO temperatures to leave out, by the name shown in the UI (e.g. "ITE IT8688E · Chipset").</summary>
    public string[] HiddenTemps { get; set; } = [];
}
