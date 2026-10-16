# HwMonitor

A web dashboard for live CPU, GPU and system temperatures and fan speeds of this Windows PC.
Open it on the PC, or on a phone or tablet on the same network.

- **Server**: ASP.NET Core (.NET 10), [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), AMD [ADLX](https://gpuopen.com/adlx/) (via [ADLXWrapper](https://github.com/Rem0o/ADLXWrapper)), SignalR
- **Client**: Angular 22 (standalone, signals, zoneless), PWA, custom CSS
- **Tests**: Playwright (desktop, Pixel 7, Pixel 7 landscape)

## Requirements

- Windows 10/11, x64
- **Administrator rights** for CPU, motherboard, drive and fan sensors. Without them you get fewer values, and the page shows a warning.
- AMD GPUs and the game FPS are read with AMD ADLX, which comes with the AMD driver. Without it, AMD GPUs come from LibreHardwareMonitor and there is no FPS.
- To build: .NET 10 SDK and Node.js 24

## Run (published exe)

```powershell
cd src/Server
dotnet publish -c Release
```

Result in `src/Server/bin/Release/net10.0-windows/win-x64/publish/`:

| File | |
| --- | --- |
| `HwMonitor.exe` | Self-contained single file, ReadyToRun, Angular app embedded. Asks for admin rights when started. |
| `appsettings.json` | Optional. Port, poll interval, logging. |

Start `HwMonitor.exe` and open <http://localhost:5080>.

### Open from your phone

1. Allow the port in Windows Firewall (once, in an admin PowerShell):
   ```powershell
   New-NetFirewallRule -DisplayName "HwMonitor" -Direction Inbound -Protocol TCP -LocalPort 5080 -Profile Private -Action Allow
   ```
   Your network must be set to **Private** in Windows.
2. Open `http://<pc-ip>:5080` in Chrome on the phone (`ipconfig` shows the IP).
3. Chrome menu → **Add to Home screen** to open it like an app.

There is **no login**. Everyone on your network can see the values (read-only). Do not use this on public networks.

Note: the "keep screen on" button and the full PWA install need https or localhost, so on plain http over the
network the button is hidden. "Add to Home screen" still works.

## Settings (`appsettings.json`)

| Key | Default | |
| --- | --- | --- |
| `Urls` | `http://0.0.0.0:5080` | Address and port |
| `Sensors:PollIntervalMs` | `1000` | How often values update (min 250) |
| `Sensors:ShowZeroRpmFans` | `false` | Also show fans that stand still / are not connected |
| `Sensors:UseAdlx` | `true` | Read AMD GPUs and FPS with ADLX (skipped when there is no AMD driver) |
| `Sensors:UseFake` | `false` | Generated values, no admin needed (tests, UI work) |
| `Sensors:FreezeFake` | `false` | With `UseFake`: values do not move (screenshot tests) |

Environment variables also work, e.g. `Sensors__UseFake=true`.

## Develop

```powershell
# Terminal 1 (admin for real sensors), or use the "fake" profile without admin:
cd src/Server
dotnet run --launch-profile fake

# Terminal 2
cd src/Client
npm start          # http://localhost:4200, /api and /hub go to :5080
```

A Debug build of the server does not contain the web UI; use `ng serve` (above) or build with `-c Release`.

## Tests

```powershell
cd src/Client
npm run e2e                              # Release build, then starts fake servers on :5099 and :5098 itself
npx playwright show-report               # screenshots and traces
npm run e2e -- --update-snapshots        # after a planned UI change
npx playwright test                      # no build step: uses the last Release build
```

To test the real single exe: `dotnet publish -c Release -p:RequireAdmin=false -o <dir>`, start it twice with
`Sensors__UseFake=true` on `--urls=http://localhost:5099` and (also with `Sensors__FreezeFake=true`) on `:5098`,
then run `npx playwright test`. Playwright reuses running servers.

## Later

- FPS for NVIDIA/Intel GPUs via PresentMon (now only AMD, via ADLX)
- History charts, alerts, run as Windows Service
