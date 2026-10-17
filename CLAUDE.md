# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

A web dashboard for live CPU/GPU/system temperatures and fan speeds of the Windows PC it runs on. ASP.NET Core (.NET 10, `net10.0-windows`) server + Angular 22 client (standalone, signals, zoneless, PWA, custom SCSS, no UI library). Values come from HWiNFO shared memory when HWiNFO runs, else from LibreHardwareMonitor (LHM). `SensorSnapshot.Fps` comes from HWiNFO's framerate sensor (all GPUs), or in LHM mode from AMD ADLX (only AMD GPUs).

## Commands

```powershell
# Server (src/Server)
dotnet build                              # Debug: no web UI embedded
dotnet run --launch-profile fake          # fake sensors, no admin needed, http://0.0.0.0:5080
dotnet run --launch-profile http          # real sensors (needs admin)
dotnet publish -c Release                 # single exe (+ appsettings.json), runs ng build first
dotnet publish -c Release -p:RequireAdmin=false -o <dir>   # same exe without the admin prompt (for testing)

# Client (src/Client)
npm start                                 # ng serve :4200, proxies /api and /hub to :5080
npx ng build                              # outputs straight into ../Server/wwwroot
npx tsc -p tsconfig.e2e.json              # type-check the Playwright tests
npm run e2e                               # Release build, then all Playwright tests (starts its own fake servers)
npm run e2e -- e2e/layout.spec.ts --project=pixel7               # one file, one device
npm run e2e -- -g "reconnects"                                  # one test by name
npm run e2e -- --update-snapshots         # after a planned UI change
npx playwright test                       # without the build step (uses the last Release build)
npm run icons                             # re-render PNG icons from public/icons/icon.svg
```

## Architecture

**Data flow:** `ISensorSource.Read()` → `SensorPoller` (BackgroundService, `PeriodicTimer`) keeps the latest `SensorSnapshot` and broadcasts it as `"snapshot"` on the SignalR hub `/hub/sensors`. `/api/snapshot` returns the latest one (503 before the first poll). The client `SensorService` loads `/api/snapshot` once, then listens on the hub and exposes `snapshot` and `status` signals.

- Every device, temp and fan has an `Id` (the LHM identifier, e.g. `/lpc/nct6799d/0/fan/1`). Use it as `track` key; names are not unique (two identical GPUs, sensors from several board chips).
- The poller passes `Fps` through `FpsFilter` (median of the last 3 polls, null = 0): HWiNFO/ADLX give single >1000 FPS spikes when switching to a game.
- Game (`Sensors/ForegroundApp.cs`, `GameTracker`, `components/game-card.ts`): while there is an FPS value, the poller sets `Game` to the foreground window's app (name from FileDescription → ProductName → window title → exe name; exe path via `QueryFullProcessImageName` with limited rights, so anti-cheat games work). A new app is only shown after 2 polls in front (Alt-Tab); the desktop/taskbar keeps the last game. The icon (64 px PNG, `System.Drawing.Common`) is cached in memory and served at `/api/game-icon/{iconId}` (hash of the path, never a path from the request). With `UseFake`, `FakeForegroundApp` returns "Fake Game" without an icon, and the fake FPS is a wave, so the Game card is in the tests and screenshots.
- When `Read()` throws, the poller keeps the last values but sets `status.message`, so the UI shows the error.
- `Server/Models/SensorSnapshot.cs` is mirrored by hand in `Client/src/app/sensors/models.ts`. Change both together.
- `Program.cs` chooses the source: `Sensors:UseFake` → `FakeSensorSource` (sine waves; `FreezeFake` = fixed values); else `Sensors:Source` = `Auto` (default) / `HwInfo` → `AutoSensorSource`, `Lhm` → `LhmSensorSource`. Only the poller thread calls `Read()`; LHM's `Computer` is not thread-safe.
- HWiNFO (`HwInfoSharedMemory` reads `Global\HWiNFO_SENS_SM2`, `HwInfoSensorSource` maps it): `AutoSensorSource` uses it every poll when it is there and updated (≤10 s old); else it creates `LhmSensorSource` lazily (so the LHM driver is not loaded while HWiNFO works) and keeps it. `HwInfo` mode never falls back, it shows a message. Element sizes come from the header and are checked (too small = "no HWiNFO"); the mapping is closed and opened again when HWiNFO stops. Matching uses HWiNFO's original English names (`*Orig`, ANSI = Latin1), the UI shows the user names. CPU/GPU are split in several groups (`CPU [#0]: <name>: Enhanced`, GPUs are `dGPU [#0]:` / `iGPU`) and are merged. °F is converted. FPS: `PresentMon` group `Framerate Presented (avg)` first (the GPU group's own `Framerate` is often 0). AMD has no free/total VRAM in HWiNFO: used = `GPU D3D Memory Dedicated`, total = registry `HardwareInformation.qwMemorySize` of the display driver with the same `DriverDesc` (`GpuMemorySize`). Drive groups `S.M.A.R.T.: <model> (<serial>) [C:]`; board sensors get the chip name in brackets as prefix, `#2` for the same chip twice. Ids: `/hwinfo/{sensorId:x}/{instance}[/{readingId:x}]`, so they change when the source switches. Names were tuned on one AMD system (Ryzen 5950X, RX 9070, Gigabyte X570); Intel/NVIDIA names are guesses. With `Logging:LogLevel:HwMonitor.Sensors=Debug` all labels are logged.
- `Sensors:HiddenTemps` (UI names, e.g. `ITE IT8688E · Chipset`) removes HWiNFO temperatures. Temps with group `Drives` get the Drives card, all other groups (`Motherboard`, `Cooling`) the Motherboard card (`TempsCard`, split in `app.ts`).
- `SensorRules` has the shared value rules (`IsValidTemp`, `RepeatedNames`, `IsAdmin`) for LHM, ADLX and HWiNFO.
- Sensor name mapping in `LhmSensorSource` (e.g. AMD `Core (Tctl/Tdie)`, Intel `CPU Package`, `GPU Hot Spot`) is based on LHM sensor names. Temps ≤0 or ≥125 °C and power ≤0 W are treated as "no value" (unused SuperIO inputs, missing driver).
- Drive names: many drives report only a part number (`WDS100T1X0E-00AFY0`). `DriveNames.Pretty` maps it with `Sensors/drive-names.json` (embedded, generated from the Nanohub SSD/HDD CSV files): vendor word removed, only A-Z/0-9 kept, longest `match` prefix wins. No match → the original name.
- AMD ADLX (`AdlxGpuReader`, used inside `LhmSensorSource`): when it starts and finds a GPU, LHM's `GpuAmd` items are skipped and the AMD GPUs, their fans and `Fps` come from ADLX (Ids `/adlx/gpu/{UniqueId}`). When it fails (no AMD driver, `Sensors:UseAdlx=false`) it logs once and LHM is used. It calls the public SWIG bindings (`ADLXWrapper.Bindings`) directly, because the wrapper classes have no usage, VRAM or FPS.
- AMD Ryzen Master Monitoring SDK (`RyzenMasterCpuReader`, used inside `LhmSensorSource`): LHM 0.9.6 needs the PawnIO driver for CPU temperature/power; without it they are null. Then the CPU card's `TempC`/`PowerW` come from the SDK's `GetCPUParameters` (`dTemperature`, `fPPTValue`); the Id stays LHM's. Only when the CPU is AMD, running as admin, the SDK is installed (`HKLM\Software\AMD\RyzenMasterMonitoringSDK`), and `Sensors:UseRyzenMaster` is on; else it logs once and is skipped. At most one SDK call per ~1 s (AMD: faster calls load the SMU); in between the last values are returned.
- Memory is in GB: system RAM = `Memory Used` + `Memory Available` from the "Total Memory" item (not "Virtual Memory"), shown on the CPU card. GPU VRAM = `GPU Memory Used` + `GPU Memory Free` (MB), fallback `D3D Dedicated Memory Used/Free`.
- The SignalR client uses WebSockets with `skipNegotiation` and retries forever; it also restarts on `visibilitychange` (phones waking up) when it is disconnected, reconnecting, or got no message for 5 s (a dead socket can still look connected).
- Firewall warning (`Firewall/WindowsFirewall.cs`, `components/firewall-warning.ts`): `GET /api/firewall` checks the Windows Firewall rules (COM `HNetCfg.FwPolicy2`) for the non-loopback Kestrel ports, so the tests (localhost only) never warn. Only answered for requests from this PC (other devices could not load the page anyway). Rules for other programs, services, store apps (`LocalUserOwner` set), interfaces or specific local addresses (Tailscale) are ignored; a matching block rule wins. `POST /api/firewall/open` (needs admin, Origin = Host, Host = localhost/IP) adds `HwMonitor (TCP <port>)` (LocalSubnet, current profiles), turns off block rules for this exe (from "Cancel" in the Windows prompt), and returns a new check. `INetFwMgr.IsPortAllowed` was tried and gives wrong answers.
- Cards can be hidden: `CardVisibilityService` keeps hidden keys (`game`, `cpu`, `gpu:<id>`, `board`, `drives`, `fans`) in localStorage. `app.html` projects an `app-hide-button` into each card's header (`<ng-content />`); hidden cards show as "show again" chips above the grid.
- Temperature color levels (`cool/warm/hot/none`) come from `temperature.ts` and are set as `data-level` attributes; global `styles.scss` maps them to `--level-color`. Shared `.card` / `.card-head` styles are global, component styles are inline in each component.

## Build / hosting details that are easy to break

- The Angular build is **embedded in the exe** (`EmbeddedResource` + `ManifestEmbeddedFileProvider`), not copied next to it. `Server/wwwroot` is generated and git-ignored.
- In Release, `wwwroot\**` is added to `EmbeddedResource` **inside** the `EmbedClient` target, not at evaluation time, because `ng build` changes the hashed file names. Including it statically gives `CS1566 Could not find file`. `BuildClient` has Inputs/Outputs (client files vs `wwwroot\index.html`), so `ng build` only runs when the client changed; `EmbedClient` is separate so it also runs when `BuildClient` is skipped.
- Only file names with an Angular hash (`main-XIAXEOH3.js`) are served as `immutable`; everything else is `no-cache`.
- A Debug build without `wwwroot/index.html` has no embedded manifest; `Program.cs` then uses a `NullFileProvider` and logs a warning.
- `app.manifest` (`requireAdministrator`) is only applied when publishing (`_IsPublishing`), so `dotnet run` and the tests work without admin. A published exe started from a non-admin shell fails with "Permission denied".
- `ContentRootPath = AppContext.BaseDirectory`, so `appsettings.json` next to the exe is found from any working folder.
- ADLX DLLs are prebuilt and committed in `lib/adlx/` (`build.ps1` rebuilds them: pinned ADLXWrapper + ADLX SDK, adds an `IADLXFPS*` pointer helper to the swig file, static VC++ runtime). `ADLXBindings.dll` is native: copied next to the exe in dev, bundled in the single-file exe on publish.
- `lib/ryzenmaster/RyzenMasterShim.dll` is prebuilt and committed (`build.ps1` compiles `RyzenMasterShim.cpp` with `cl`, using the headers from the installed SDK). It loads the SDK's `Platform.dll` by full path at run time and wraps the C++ calls in SEH. The SDK's headers, libs, DLLs and driver are AMD confidential (SDK `License.rtf`): never copy them into the repo or bundle them.
- All `*.dll` files are stored with Git LFS (`.gitattributes`); a clone needs `git lfs install` first.
- Trimming is off on purpose (LHM and SignalR use reflection).

## Tests (Playwright, `src/Client/e2e`)

- Projects: `desktop` (1440×900), `pixel7`, `pixel7-landscape`. `layout.spec.ts` expects 4 / 1 / 2 grid columns for them.
- `playwright.config.ts` starts two servers with `dotnet run -c Release --no-build`: live fake on `:5099` (default `baseURL`) and frozen fake on `:5098` (only `visual.spec.ts`). They never build (locked files, reused servers); `npm run e2e` runs `dotnet build -c Release` first. `reuseExistingServer` is on locally, so servers already running on those ports are used instead (handy for testing a published exe).
- Screenshot baselines are in `e2e/visual.spec.ts-snapshots/` (`-win32` suffix). The visual test blocks Google Fonts and masks the "Updated" time.
- `reconnect.spec.ts` uses `page.routeWebSocket` to drop the hub connection; `context.setOffline` does not close an open WebSocket.
- Service workers are blocked in tests (`serviceWorkers: 'block'`).

## Environment notes

- Without admin, CPU, motherboard and fan values may be missing; the snapshot `status.message` shows a warning in the UI. Reading HWiNFO needs no admin (but the published exe still asks for it, for the LHM fallback).
- The free HWiNFO version turns shared memory off after 12 hours; then the app falls back to LHM.
- The Wake Lock button is only shown in a secure context (https/localhost), so it is hidden over plain http on the LAN.
