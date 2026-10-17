using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace HwMonitor.Sensors;

/// <summary>
/// The app in front. <see cref="IconId"/> is null when the exe has no readable icon. Cached by path, so
/// <see cref="Pid"/> is set again on every read (a restarted game gets a new one).
/// </summary>
public sealed record ForegroundAppInfo(string Path, string Name, string? IconId, int Pid = 0);

/// <summary>Finds the app in front (the one HWiNFO/PresentMon and ADLX measure the FPS of) and its icon.</summary>
public interface IForegroundApp
{
    /// <summary>
    /// Null when there is no window in front or it is blocked: an exe in the Windows folder (desktop, taskbar, Start)
    /// or a known app that draws with Direct3D but is not a game (Windows Terminal, browsers).
    /// </summary>
    ForegroundAppInfo? Read();

    /// <summary>The icon as PNG, by <see cref="ForegroundAppInfo.IconId"/>; null when unknown.</summary>
    byte[]? Icon(string id);

    /// <summary>The graphics API of a process (e.g. "DX12", "Vulkan"); null when unknown.</summary>
    string? GraphicsApi(int pid);
}

/// <summary>
/// Uses the foreground window. The exe path comes from QueryFullProcessImageName with limited rights, which also works for
/// most protected (anti-cheat) processes, unlike Process.MainModule. Name: FileDescription, else ProductName, else the
/// window title, else the exe name.
/// </summary>
public sealed class ForegroundApp(ILogger<ForegroundApp> logger) : IForegroundApp
{
    private const int IconSize = 64;
    private static readonly string WindowsFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
    // These can draw more than 10 frames/s (scrolling text, video), so ETW sees FPS.
    private static readonly HashSet<string> BlockedExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal.exe", "Code.exe", "devenv.exe", "chrome.exe", "msedge.exe", "firefox.exe", "Discord.exe",
        "obs64.exe", "vlc.exe",
    };

    // Only exes that were in front are added, so this stays small.
    private readonly ConcurrentDictionary<string, ForegroundAppInfo> _apps = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte[]> _icons = new();
    private readonly GraphicsApi _graphicsApi = new(logger);

    public ForegroundAppInfo? Read()
    {
        var window = GetForegroundWindow();
        if (window == 0 || GetWindowThreadProcessId(window, out var pid) == 0 || pid == 0)
            return null;
        var path = ExePath(pid);
        if (path is null)
        {
            var title = WindowTitle(window);
            return title is null ? null : new ForegroundAppInfo($"pid:{pid}", title, null, (int)pid);
        }
        if (IsBlocked(path))
            return null;
        return _apps.GetOrAdd(path, p => Create(p, window)) with { Pid = (int)pid };
    }

    public byte[]? Icon(string id) => _icons.GetValueOrDefault(id);

    public string? GraphicsApi(int pid) => _graphicsApi.Read(pid);

    private static bool IsBlocked(string path) =>
        path.StartsWith(WindowsFolder, StringComparison.OrdinalIgnoreCase) ||
        BlockedExes.Contains(System.IO.Path.GetFileName(path));

    private ForegroundAppInfo Create(string path, nint window)
    {
        string? name = null;
        try
        {
            var version = FileVersionInfo.GetVersionInfo(path);
            name = Clean(version.FileDescription) ?? Clean(version.ProductName);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "No version info for {Path}", path);
        }
        name ??= WindowTitle(window) ?? System.IO.Path.GetFileNameWithoutExtension(path);

        string? iconId = null;
        if (ReadIcon(path) is { } png)
        {
            iconId = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())))[..16];
            _icons[iconId] = png;
        }
        logger.LogDebug("Foreground app: {Name} ({Path})", name, path);
        return new ForegroundAppInfo(path, name, iconId);
    }

    private byte[]? ReadIcon(string path)
    {
        try
        {
            // Store / Game Pass exes in WindowsApps often deny access; then there is no icon.
            using var icon = System.Drawing.Icon.ExtractIcon(path, 0, IconSize);
            if (icon is null)
                return null;
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "No icon for {Path}", path);
            return null;
        }
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string? ExePath(uint pid)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == 0)
            return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string? WindowTitle(nint window)
    {
        var buffer = new StringBuilder(256);
        return GetWindowText(window, buffer, buffer.Capacity) > 0 ? Clean(buffer.ToString()) : null;
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder text, int maxCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(nint process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
}

/// <summary>A fixed game for the fake source (tests and UI work), so the result does not depend on the desktop.</summary>
public sealed class FakeForegroundApp : IForegroundApp
{
    private static readonly ForegroundAppInfo Game = new(@"C:\Games\Fake Game\FakeGame.exe", "Fake Game", null, 1);

    public ForegroundAppInfo? Read() => Game;

    public byte[]? Icon(string id) => null;

    public string? GraphicsApi(int pid) => "DX12";
}
