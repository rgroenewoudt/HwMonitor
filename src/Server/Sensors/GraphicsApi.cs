using System.Runtime.InteropServices;
using System.Text;

namespace HwMonitor.Sensors;

/// <summary>
/// Guesses the graphics API of a process from its loaded DLLs (EnumProcessModulesEx). Needs PROCESS_VM_READ, which
/// some anti-cheat games block; then the result is null and no API is shown. Many games load more than one of these
/// DLLs, so the first match in <see cref="Apis"/> wins: vulkan-1.dll is only loaded on purpose, d3d12/d3d11 are also
/// loaded by Windows parts (DXGI, overlays), and opengl32.dll by many libraries.
/// </summary>
public sealed class GraphicsApi(ILogger logger)
{
    private static readonly (string Dll, string Api)[] Apis =
    [
        ("vulkan-1.dll", "Vulkan"),
        ("d3d12.dll", "DX12"),
        ("d3d11.dll", "DX11"),
        ("d3d10_1.dll", "DX10"),
        ("d3d10.dll", "DX10"),
        ("d3d9.dll", "DX9"),
        ("d3d8.dll", "DX8"),
        ("opengl32.dll", "OpenGL"),
    ];

    // A game can load its API DLL some time after it starts, so it is checked again now and then.
    private static readonly TimeSpan RecheckAfter = TimeSpan.FromSeconds(10);

    // Only the poller thread calls Read, and only for the current game, so one pid is kept.
    private int _pid;
    private string? _api;
    private bool _denied;
    private long _checkedTick;

    /// <summary>Null when the process cannot be read or has none of the known DLLs.</summary>
    public string? Read(int pid)
    {
        if (pid != _pid)
            (_pid, _api, _denied, _checkedTick) = (pid, null, false, 0);
        else if (_denied || Environment.TickCount64 - _checkedTick < RecheckAfter.TotalMilliseconds)
            return _api;

        _checkedTick = Environment.TickCount64;
        var modules = ModuleNames(pid);
        if (modules is null)
        {
            // Do not try again for this pid: a protected game should not see a handle request every 10 s.
            _denied = true;
            logger.LogDebug("Cannot read the modules of pid {Pid}, no graphics API", pid);
            return _api = null;
        }

        var api = Apis.FirstOrDefault(a => modules.Contains(a.Dll)).Api;
        if (api != _api)
            logger.LogDebug("Graphics API of pid {Pid}: {Api}", pid, api ?? "none");
        return _api = api;
    }

    private static HashSet<string>? ModuleNames(int pid)
    {
        var process = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, (uint)pid);
        if (process == 0)
            return null;
        try
        {
            var handles = new nint[512];
            while (true)
            {
                var size = (uint)(handles.Length * nint.Size);
                if (!EnumProcessModulesEx(process, handles, size, out var needed, ListModulesAll))
                    return null;
                if (needed <= size)
                {
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var buffer = new StringBuilder(260);
                    for (var i = 0; i < needed / nint.Size; i++)
                    {
                        if (GetModuleBaseName(process, handles[i], buffer, buffer.Capacity) > 0)
                            names.Add(buffer.ToString());
                    }
                    return names;
                }
                handles = new nint[needed / nint.Size + 64];
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmRead = 0x0010;
    private const uint ListModulesAll = 0x03; // 32-bit and 64-bit modules, so 32-bit games work too

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EnumProcessModulesEx(nint process, [Out] nint[] modules, uint size, out uint needed, uint filter);

    [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetModuleBaseName(nint process, nint module, StringBuilder name, int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
}
