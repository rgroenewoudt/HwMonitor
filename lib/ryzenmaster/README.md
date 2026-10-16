# Ryzen Master shim (prebuilt)

`RyzenMasterShim.dll` (native x64) gives the
[AMD Ryzen Master Monitoring SDK](https://www.amd.com/en/developer/ryzen-master-monitoring-sdk.html) three plain C
functions, so C# can call it (the SDK API is C++ interfaces):

- `rm_init(platformDllPath)`: loads the SDK's `Platform.dll` by full path, `GetPlatform().Init()`, opens CPU device 0.
  Returns 0 = ok, 1 = DLL not loaded, 2 = no `GetPlatform`, 3 = init failed (no admin / no driver / CPU not supported),
  4 = no CPU device.
- `rm_read(&temperatureC, &pptW)`: `ICPUEx::GetCPUParameters` → `dTemperature`, `fPPTValue`. Returns the SDK status
  (0 = ok, -1 = failure, 4 = unsupported), -2 = crash in the SDK (caught with SEH), -3 = not initialized.
  AMD: at most once per second.
- `rm_close()`.

The SDK is **not** shipped: `Platform.dll`, `Device.dll` and the `AMDRyzenMasterDriverV32` driver come from the SDK
install (`HKLM\Software\AMD\RyzenMasterMonitoringSDK\InstallationPath`). Static VC++ runtime, depends only on
`KERNEL32.dll`. Built against SDK 3.1.1.5478.

## Rebuild

Needs once: VS Build Tools with the C++ workload, and the SDK installed. Then:

```powershell
lib/ryzenmaster/build.ps1
```

## License

The SDK's license (`License.rtf` in the SDK folder) says its files are AMD confidential. Its headers, libs and DLLs must
not be copied into this repo. `RyzenMasterShim.cpp` only includes the headers from the install folder.
