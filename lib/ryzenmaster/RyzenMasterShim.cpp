// Flat C functions around the AMD Ryzen Master Monitoring SDK, so C# can call it with P/Invoke.
// The SDK API is C++ interfaces (IPlatform, ICPUEx). This file only uses them; it contains no AMD code.
// Platform.dll is loaded at run time from the installed SDK (no import lib), like the SDK sample does.
// Build: build.ps1 (needs the SDK headers from %AMDRMMONITORSDKPATH%\include).
#include <windows.h>
#include <cwchar>
#include "IPlatform.h"
#include "IDeviceManager.h"
#include "ICPUEx.h"

typedef IPlatform& (__stdcall* GetPlatformFunc)();

static HMODULE g_module = nullptr;
static IPlatform* g_platform = nullptr;
static ICPUEx* g_cpu = nullptr;

// SEH (__try) can't be in a function with C++ objects that need unwinding, so the SDK calls are in small helpers.
static bool SafeInit(GetPlatformFunc getPlatform, IPlatform** platform, ICPUEx** cpu)
{
    __try
    {
        IPlatform& p = getPlatform();
        if (!p.Init())
            return false;
        *platform = &p;
        *cpu = static_cast<ICPUEx*>(p.GetIDeviceManager().GetDevice(dtCPU, 0));
        return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }
}

static int SafeRead(ICPUEx* cpu, CPUParameters* data)
{
    __try
    {
        return cpu->GetCPUParameters(*data);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return -2;
    }
}

static void SafeUnInit(IPlatform* platform)
{
    __try
    {
        platform->UnInit();
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
    }
}

static void Close()
{
    if (g_platform)
        SafeUnInit(g_platform);
    g_platform = nullptr;
    g_cpu = nullptr;
    if (g_module)
        FreeLibrary(g_module);
    g_module = nullptr;
}

extern "C"
{
    /// Loads the SDK's Platform.dll (full path) and opens the CPU device.
    /// 0 = ok, 1 = Platform.dll not loaded, 2 = no GetPlatform, 3 = Init failed (no admin or no driver), 4 = no CPU device.
    __declspec(dllexport) int __cdecl rm_init(const wchar_t* platformDllPath)
    {
        Close();
        // The SDK's other DLLs (Device.dll) are in the same folder.
        g_module = LoadLibraryExW(platformDllPath, nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        if (!g_module)
            return 1;
        auto getPlatform = reinterpret_cast<GetPlatformFunc>(GetProcAddress(g_module, "GetPlatform"));
        if (!getPlatform)
        {
            Close();
            return 2;
        }
        IPlatform* platform = nullptr;
        ICPUEx* cpu = nullptr;
        if (!SafeInit(getPlatform, &platform, &cpu))
        {
            Close();
            return 3;
        }
        g_platform = platform;
        g_cpu = cpu;
        if (!g_cpu)
        {
            Close();
            return 4;
        }
        return 0;
    }

    /// The SDK's status: 0 = ok, -1 = failure, 4 = unsupported, -2 = crash in the SDK, -3 = not initialized.
    /// AMD: call at most once per second, faster calls load the SMU and can give wrong values.
    __declspec(dllexport) int __cdecl rm_read(double* temperatureC, float* pptW)
    {
        if (!g_cpu)
            return -3;
        CPUParameters data = {};
        int result = SafeRead(g_cpu, &data);
        if (result == 0)
        {
            *temperatureC = data.dTemperature;
            *pptW = data.fPPTValue;
        }
        return result;
    }

    __declspec(dllexport) void __cdecl rm_close(void)
    {
        Close();
    }
}
