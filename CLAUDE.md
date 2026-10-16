# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

A web dashboard for live CPU/GPU/system temperatures and fan speeds of the Windows PC it runs on. ASP.NET Core (.NET 10, `net10.0-windows`) server + Angular 22 client (standalone, signals, zoneless, PWA, custom SCSS, no UI library). `SensorSnapshot.Fps` comes from AMD ADLX (only AMD GPUs; PresentMon for others is planned).

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
- When `Read()` throws, the poller keeps the last values but sets `status.message`, so the UI shows the error.
- `Server/Models/SensorSnapshot.cs` is mirrored by hand in `Client/src/app/sensors/models.ts`. Change both together.
- `LhmSensorSource` (real) vs `FakeSensorSource` (sine waves; `FreezeFake` = fixed values) are chosen in `Program.cs` by `Sensors:UseFake`. Only the poller thread calls `Read()`; LHM's `Computer` is not thread-safe.
- Sensor name mapping in `LhmSensorSource` (e.g. AMD `Core (Tctl/Tdie)`, Intel `CPU Package`, `GPU Hot Spot`) is based on LHM sensor names. Temps ≤0 or ≥125 °C and power ≤0 W are treated as "no value" (unused SuperIO inputs, missing driver).
- Drive names: many drives report only a part number (`WDS100T1X0E-00AFY0`). `DriveNames.Pretty` maps it with `Sensors/drive-names.json` (embedded, generated from the Nanohub SSD/HDD CSV files): vendor word removed, only A-Z/0-9 kept, longest `match` prefix wins. No match → the original name.
- AMD ADLX (`AdlxGpuReader`, used inside `LhmSensorSource`): when it starts and finds a GPU, LHM's `GpuAmd` items are skipped and the AMD GPUs, their fans and `Fps` come from ADLX (Ids `/adlx/gpu/{UniqueId}`). When it fails (no AMD driver, `Sensors:UseAdlx=false`) it logs once and LHM is used. It calls the public SWIG bindings (`ADLXWrapper.Bindings`) directly, because the wrapper classes have no usage, VRAM or FPS.
- Memory is in GB: system RAM = `Memory Used` + `Memory Available` from the "Total Memory" item (not "Virtual Memory"), shown on the CPU card. GPU VRAM = `GPU Memory Used` + `GPU Memory Free` (MB), fallback `D3D Dedicated Memory Used/Free`.
- The SignalR client uses WebSockets with `skipNegotiation` and retries forever; it also restarts on `visibilitychange` (phones waking up) when it is disconnected, reconnecting, or got no message for 5 s (a dead socket can still look connected).
- Temperature color levels (`cool/warm/hot/none`) come from `temperature.ts` and are set as `data-level` attributes; global `styles.scss` maps them to `--level-color`. Shared `.card` / `.card-head` styles are global, component styles are inline in each component.

## Build / hosting details that are easy to break

- The Angular build is **embedded in the exe** (`EmbeddedResource` + `ManifestEmbeddedFileProvider`), not copied next to it. `Server/wwwroot` is generated and git-ignored.
- In Release, `wwwroot\**` is added to `EmbeddedResource` **inside** the `EmbedClient` target, not at evaluation time, because `ng build` changes the hashed file names. Including it statically gives `CS1566 Could not find file`. `BuildClient` has Inputs/Outputs (client files vs `wwwroot\index.html`), so `ng build` only runs when the client changed; `EmbedClient` is separate so it also runs when `BuildClient` is skipped.
- Only file names with an Angular hash (`main-XIAXEOH3.js`) are served as `immutable`; everything else is `no-cache`.
- A Debug build without `wwwroot/index.html` has no embedded manifest; `Program.cs` then uses a `NullFileProvider` and logs a warning.
- `app.manifest` (`requireAdministrator`) is only applied when publishing (`_IsPublishing`), so `dotnet run` and the tests work without admin. A published exe started from a non-admin shell fails with "Permission denied".
- `ContentRootPath = AppContext.BaseDirectory`, so `appsettings.json` next to the exe is found from any working folder.
- ADLX DLLs are prebuilt and committed in `lib/adlx/` (`build.ps1` rebuilds them: pinned ADLXWrapper + ADLX SDK, adds an `IADLXFPS*` pointer helper to the swig file, static VC++ runtime). `ADLXBindings.dll` is native: copied next to the exe in dev, bundled in the single-file exe on publish.
- Trimming is off on purpose (LHM and SignalR use reflection).

## Tests (Playwright, `src/Client/e2e`)

- Projects: `desktop` (1440×900), `pixel7`, `pixel7-landscape`. `layout.spec.ts` expects 4 / 1 / 2 grid columns for them.
- `playwright.config.ts` starts two servers with `dotnet run -c Release --no-build`: live fake on `:5099` (default `baseURL`) and frozen fake on `:5098` (only `visual.spec.ts`). They never build (locked files, reused servers); `npm run e2e` runs `dotnet build -c Release` first. `reuseExistingServer` is on locally, so servers already running on those ports are used instead (handy for testing a published exe).
- Screenshot baselines are in `e2e/visual.spec.ts-snapshots/` (`-win32` suffix). The visual test blocks Google Fonts and masks the "Updated" time.
- `reconnect.spec.ts` uses `page.routeWebSocket` to drop the hub connection; `context.setOffline` does not close an open WebSocket.
- Service workers are blocked in tests (`serviceWorkers: 'block'`).

## Environment notes

- Without admin, CPU, motherboard and fan values may be missing; the snapshot `status.message` shows a warning in the UI.
- The Wake Lock button is only shown in a secure context (https/localhost), so it is hidden over plain http on the LAN.
