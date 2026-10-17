import { expect, test } from '@playwright/test';

test('shows all cards and live values', async ({ page }) => {
  await page.goto('/');

  await expect(page.getByTestId('status-badge')).toHaveText('Live');
  await expect(page.getByTestId('cpu-card')).toBeVisible();
  await expect(page.getByTestId('cpu-card')).toContainText('AMD Ryzen 9 5950X');
  await expect(page.getByTestId('gpu-card')).toBeVisible();
  await expect(page.getByTestId('board-card')).toContainText('Motherboard');
  await expect(page.getByTestId('board-card').getByTestId('system-temp')).toHaveCount(3);
  await expect(page.getByTestId('drives-card')).toContainText('Drives');
  await expect(page.getByTestId('drives-card').getByTestId('system-temp')).toHaveCount(1);
  await expect(page.getByTestId('fan-tile')).toHaveCount(3);
  // The fake source reports a fixed game without an icon, so the fallback icon is shown.
  await expect(page.getByTestId('game-name')).toHaveText('Fake Game');
  await expect(page.getByTestId('fps')).toHaveText(/^\d+FPS$/);
  await expect(page.getByTestId('game-icon')).toHaveCount(0);

  // Values come in over SignalR every second, so they must change.
  const cpuTemp = page.getByTestId('cpu-card').getByTestId('gauge-value');
  const first = await cpuTemp.textContent();
  await expect.poll(() => cpuTemp.textContent(), { timeout: 5_000 }).not.toBe(first);

  const fanRpm = page.getByTestId('fan-rpm').first();
  const firstRpm = await fanRpm.textContent();
  await expect.poll(() => fanRpm.textContent(), { timeout: 5_000 }).not.toBe(firstRpm);
});

test('has no warning with fake sensors', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByTestId('cpu-card')).toBeVisible();
  await expect(page.getByTestId('warning')).toHaveCount(0);
});

test('API returns a snapshot', async ({ request }) => {
  const response = await request.get('/api/snapshot');
  expect(response.ok()).toBe(true);
  const body = await response.json();
  expect(body.cpu.tempC).toBeGreaterThan(0);
  expect(body.systemTemps.length).toBeGreaterThan(0);
  expect(body.status.source).toBe('fake');
});
