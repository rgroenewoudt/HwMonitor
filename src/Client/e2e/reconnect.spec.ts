import { expect, test, WebSocketRoute } from '@playwright/test';

test('reconnects after the connection drops', async ({ page }) => {
  let connections = 0;
  let first: WebSocketRoute | undefined;

  await page.routeWebSocket('**/hub/sensors', (ws) => {
    connections++;
    if (connections === 2) {
      // The first retry fails too, so the "reconnecting" state lasts a moment.
      ws.close();
      return;
    }
    ws.connectToServer();
    first ??= ws;
  });

  await page.goto('/');
  const badge = page.getByTestId('status-badge');
  await expect(badge).toHaveText('Live');

  await first!.close();
  await expect(badge).toHaveText('Reconnecting…');
  await expect(badge).toHaveText('Live', { timeout: 10_000 });
  expect(connections).toBeGreaterThanOrEqual(3);

  // And values flow again.
  const cpuTemp = page.getByTestId('cpu-card').getByTestId('gauge-value');
  const value = await cpuTemp.textContent();
  await expect.poll(() => cpuTemp.textContent(), { timeout: 5_000 }).not.toBe(value);
});
