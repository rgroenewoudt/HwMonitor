using System.Security.Principal;

namespace HwMonitor.Sensors;

/// <summary>Rules that all real sensor sources (LHM, ADLX, HWiNFO) share.</summary>
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

    /// <summary>Names that exist on more than one item (each item gives its own names).</summary>
    public static HashSet<string> RepeatedNames(IEnumerable<IEnumerable<string>> namesPerItem) =>
        namesPerItem.SelectMany(names => names.Distinct())
            .GroupBy(name => name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();
}
