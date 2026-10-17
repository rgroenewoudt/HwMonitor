import { expect, test } from '@playwright/test';

// The test servers listen on localhost only, so the real check never warns; the API is mocked here.
const blocked = { blockedPorts: [5080], profiles: ['Public'], canOpen: true };
const open = { blockedPorts: [], profiles: [], canOpen: true };

test('opens a blocked firewall port', async ({ page }) => {
  await page.route('/api/firewall', (route) => route.fulfill({ json: blocked }));
  let posted = false;
  await page.route('/api/firewall/open', (route) => {
    posted = route.request().method() === 'POST';
    return route.fulfill({ json: open });
  });

  await page.goto('/');
  const warning = page.getByTestId('firewall-warning');
  await expect(warning).toContainText('Windows Firewall blocks TCP port 5080 on the Public network');

  await warning.getByRole('button', { name: 'Open port 5080' }).click();
  await expect(page.getByTestId('firewall-opened')).toBeVisible();
  await expect(warning).toHaveCount(0);
  expect(posted).toBe(true);
});

test('shows the error when the port cannot be opened', async ({ page }) => {
  await page.route('/api/firewall', (route) => route.fulfill({ json: blocked }));
  await page.route('/api/firewall/open', (route) =>
    route.fulfill({ status: 403, json: { detail: 'Run HwMonitor as administrator to change the firewall.' } }),
  );

  await page.goto('/');
  const warning = page.getByTestId('firewall-warning');
  await warning.getByRole('button', { name: 'Open port 5080' }).click();
  await expect(warning).toContainText('Run HwMonitor as administrator to change the firewall.');
});

test('without admin there is no button', async ({ page }) => {
  await page.route('/api/firewall', (route) => route.fulfill({ json: { ...blocked, canOpen: false } }));

  await page.goto('/');
  const warning = page.getByTestId('firewall-warning');
  await expect(warning).toContainText('Run HwMonitor as administrator');
  await expect(warning.getByRole('button')).toHaveCount(0);
});

test('no warning when the port is open', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByTestId('cpu-card')).toBeVisible();
  await expect(page.getByTestId('firewall-warning')).toHaveCount(0);
});
