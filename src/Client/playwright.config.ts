import { defineConfig, devices } from '@playwright/test';

/** Fake sensors with moving values. */
export const LIVE_URL = 'http://localhost:5099';
/** Fake sensors with frozen values, for screenshot tests. */
export const FROZEN_URL = 'http://localhost:5098';

// Release build embeds the Angular app; no admin rights needed with fake sensors.
// The servers do not build: a running server locks the files, and a reused server would skip the build.
// `npm run e2e` builds first (dotnet build -c Release).
const server = (url: string, freeze: boolean) => ({
  command: `dotnet run --project ../Server -c Release --no-launch-profile --no-build -- --urls=${url}`,
  url: `${url}/api/snapshot`,
  env: { Sensors__UseFake: 'true', Sensors__FreezeFake: String(freeze) },
  reuseExistingServer: !process.env['CI'],
  timeout: 180_000,
});

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: LIVE_URL,
    trace: 'retain-on-failure',
    // The service worker could serve an old build between test runs.
    serviceWorkers: 'block',
  },
  expect: {
    toHaveScreenshot: { maxDiffPixelRatio: 0.01, animations: 'disabled' },
  },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } } },
    { name: 'pixel7', use: { ...devices['Pixel 7'] } },
    { name: 'pixel7-landscape', use: { ...devices['Pixel 7 landscape'] } },
  ],
  webServer: [server(LIVE_URL, false), server(FROZEN_URL, true)],
});
