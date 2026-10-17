using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace HwMonitor.Sensors;

/// <summary>A Steam game. <see cref="IconPath"/> is Steam's cached 32 px JPG icon; null when Steam has not cached it.</summary>
public sealed record SteamGameInfo(string AppId, string Name, string? IconPath);

/// <summary>
/// Finds the Steam game of an exe in <c>&lt;library&gt;\steamapps\common\&lt;installdir&gt;\</c>: the name comes from the
/// <c>appmanifest_&lt;appid&gt;.acf</c> next to it with the same <c>installdir</c>, the icon from
/// <c>&lt;SteamPath&gt;\appcache\librarycache\&lt;appid&gt;\&lt;sha1&gt;.jpg</c>.
/// </summary>
public static partial class SteamGame
{
    private const string Common = @"\steamapps\common\";

    /// <summary>Null when the exe is not in a Steam library or has no manifest. Throws on IO errors.</summary>
    public static SteamGameInfo? Find(string exePath)
    {
        var start = exePath.IndexOf(Common, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;
        var library = exePath[..start];
        var rest = exePath[(start + Common.Length)..];
        var slash = rest.IndexOf('\\');
        if (slash <= 0)
            return null;
        var installDir = rest[..slash];

        var manifests = Path.Combine(library, "steamapps");
        foreach (var file in Directory.EnumerateFiles(manifests, "appmanifest_*.acf"))
        {
            var text = File.ReadAllText(file);
            if (!string.Equals(Value(text, "installdir"), installDir, StringComparison.OrdinalIgnoreCase))
                continue;
            var appId = Value(text, "appid");
            var name = Value(text, "name") is { } raw ? CleanName(raw) : null;
            if (appId is null || name is null)
                return null;
            return new SteamGameInfo(appId, name, IconPath(appId));
        }
        return null;
    }

    /// <summary>Removes ™, ® and the em dash, which look noisy on the card.</summary>
    public static string? CleanName(string name)
    {
        var clean = Spaces().Replace(name.Replace("™", "").Replace("®", "").Replace("—", " "), " ").Trim();
        return clean.Length == 0 ? null : clean;
    }

    private static string? IconPath(string appId)
    {
        if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is not string steam)
            return null;
        var folder = Path.Combine(steam.Replace('/', '\\'), "appcache", "librarycache", appId);
        if (!Directory.Exists(folder))
            return null;
        return Directory.EnumerateFiles(folder, "*.jpg").FirstOrDefault(f => IconName().IsMatch(Path.GetFileName(f)));
    }

    // The first match is the top-level key: it comes before the nested sections.
    private static string? Value(string text, string key)
    {
        var match = Regex.Match(text, $"\"{key}\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Replace("\\\"", "\"").Replace(@"\\", @"\") : null;
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex("^[0-9a-f]{40}\\.jpg$", RegexOptions.IgnoreCase)]
    private static partial Regex IconName();
}
