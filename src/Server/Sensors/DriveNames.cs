using System.Text.Json;
using System.Text.RegularExpressions;

namespace HwMonitor.Sensors;

/// <summary>
/// Readable names for drives that report only a part number, e.g. <c>WDS100T1X0E-00AFY0</c> → "WD Black SN850 1TB".
/// The list is <c>drive-names.json</c>, embedded in the exe.
/// </summary>
public static partial class DriveNames
{
    private sealed record Entry(string Match, string Name);

    // Longest first, so "CSSDF480GBMP510B" wins over "CSSDF480GBMP510".
    private static readonly Lazy<Entry[]> Entries = new(() =>
    {
        using var stream = typeof(DriveNames).Assembly.GetManifestResourceStream("drive-names.json")
                           ?? throw new InvalidOperationException("drive-names.json is not embedded.");
        var entries = JsonSerializer.Deserialize<Entry[]>(stream, JsonSerializerOptions.Web) ?? [];
        return entries.OrderByDescending(e => e.Match.Length).ToArray();
    });

    /// <summary>The readable name, or <paramref name="model"/> itself when the drive is not in the list.</summary>
    public static string Pretty(string model)
    {
        // "WDC WD40EFRX-68N32N0" → "WD40EFRX68N32N0"; the part after the dash is a firmware/batch suffix.
        var key = NonAlphanumeric().Replace(VendorPrefix().Replace(model.ToUpperInvariant(), ""), "");
        if (key.Length == 0)
            return model;
        return Entries.Value.FirstOrDefault(e => key.StartsWith(e.Match, StringComparison.Ordinal))?.Name ?? model;
    }

    [GeneratedRegex(@"^(WDC|WD|INTEL|KINGSTON|TOSHIBA|SAMSUNG|HGST|SEAGATE|CRUCIAL|ADATA|CORSAIR|GIGABYTE)\s+")]
    private static partial Regex VendorPrefix();

    [GeneratedRegex("[^A-Z0-9]")]
    private static partial Regex NonAlphanumeric();
}
