import { expect, test, WebSocketRoute } from '@playwright/test';

test('reloads after a reconnect when the server has a newer build', async ({ page }) => {
  let hub: WebSocketRoute | undefined;
  await page.routeWebSocket('**/hub/sensors', (ws) => {
    ws.connectToServer();
    hub = ws;
  });
  // Only the update check uses ngsw-bypass, so the page itself loads normally.
  await page.route((url) => url.searchParams.has('ngsw-bypass'), async (route) => {
    const response = await route.fetch();
    const html = (await response.text()).replace(/main-[A-Z0-9]{8}\.js/, 'main-NEWBUILD.js');
    await route.fulfill({ response, body: html });
  });

  await page.goto('/');
  await expect(page.getByTestId('status-badge')).toHaveText('Live');

  const reload = page.waitForEvent('load');
  await hub!.close();
  await reload;

  // The server still sends the "new" build (like a cache with old files): no second reload.
  let loads = 0;
  page.on('load', () => loads++);
  await expect(page.getByTestId('status-badge')).toHaveText('Live');
  const check = page.waitForRequest((request) => request.url().includes('ngsw-bypass'));
  await hub!.close();
  await check;
  await expect(page.getByTestId('status-badge')).toHaveText('Live');
  await page.waitForTimeout(500);
  expect(loads).toBe(0);
});

test('does not reload after a reconnect when the build is the same', async ({ page }) => {
  let hub: WebSocketRoute | undefined;
  await page.routeWebSocket('**/hub/sensors', (ws) => {
    ws.connectToServer();
    hub = ws;
  });
  let checks = 0;
  page.on('request', (request) => {
    if (request.url().includes('ngsw-bypass')) checks++;
  });
  let loads = 0;
  page.on('load', () => loads++);

  await page.goto('/');
  await expect(page.getByTestId('status-badge')).toHaveText('Live');
  expect(checks).toBe(0);

  await hub!.close();
  await expect.poll(() => checks).toBe(1);
  await expect(page.getByTestId('status-badge')).toHaveText('Live');
  await page.waitForTimeout(500);
  expect(loads).toBe(1);
});
