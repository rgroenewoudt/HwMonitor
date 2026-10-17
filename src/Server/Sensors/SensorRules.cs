using System.Security.Principal;
using HwMonitor.Models;

namespace HwMonitor.Sensors;

/// <summary>Rules that all real sensor sources (LHM, ADLX) share.</summary>
public static class SensorRules
{
    public const double MaxValidTempC = 125;

    // Unused SuperIO inputs often show 0, -128 or 255 °C.
    public static bool IsValidTemp(double? value) => value is > 0 and < MaxValidTempC;

    public static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Sets each drive's active time on its first temperature that is left (temperature Ids start with the drive's Id).
    /// </summary>
    public static void AddDriveActivity(List<TempReading> temps, IEnumerable<(string DriveId, double ActivePercent)> drives)
    {
        foreach (var (driveId, active) in drives)
        {
            var index = temps.FindIndex(t => t.Group == "Drives" && t.Id.StartsWith(driveId + "/", StringComparison.Ordinal));
            if (index >= 0)
                temps[index] = temps[index] with { ActivePercent = Math.Clamp(Math.Round(active, 1), 0, 100) };
        }
    }

    /// <summary>Names that exist on more than one item (each item gives its own names).</summary>
    public static HashSet<string> RepeatedNames(IEnumerable<IEnumerable<string>> namesPerItem) =>
        namesPerItem.SelectMany(names => names.Distinct())
            .GroupBy(name => name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();
}
