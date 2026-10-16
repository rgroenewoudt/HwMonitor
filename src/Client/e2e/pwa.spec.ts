import { expect, test } from '@playwright/test';

test('has a valid web app manifest', async ({ page, request }) => {
  await page.goto('/');
  const href = await page.locator('link[rel="manifest"]').getAttribute('href');
  const response = await request.get(`/${href}`);
  expect(response.ok()).toBe(true);

  const manifest = await response.json();
  expect(manifest.name).toBe('HwMonitor');
  expect(manifest.display).toBe('standalone');
  expect(manifest.theme_color).toBe('#070a12');

  const sizes = manifest.icons.map((icon: { sizes: string }) => icon.sizes);
  expect(sizes).toContain('192x192');
  expect(sizes).toContain('512x512');
  for (const icon of manifest.icons) {
    expect((await request.get(`/${icon.src}`)).ok(), icon.src).toBe(true);
  }
});

test('has mobile meta tags', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('meta[name="theme-color"]')).toHaveAttribute('content', '#070a12');
  await expect(page.locator('meta[name="viewport"]')).toHaveAttribute('content', /viewport-fit=cover/);
});

test('unknown paths fall back to the app', async ({ request }) => {
  const response = await request.get('/some/deep/link');
  expect(response.ok()).toBe(true);
  expect(await response.text()).toContain('<app-root>');
});
