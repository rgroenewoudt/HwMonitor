import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  await page.goto('/');
  await expect(page.getByTestId('fans-card')).toBeVisible();
});

test('has no horizontal scroll', async ({ page }) => {
  const { scrollWidth, clientWidth } = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(scrollWidth).toBeLessThanOrEqual(clientWidth);
});

test('uses the right number of columns', async ({ page }, testInfo) => {
  const columns = await page.locator('main.grid > *').evaluateAll(
    (cards) => new Set(cards.map((card) => Math.round(card.getBoundingClientRect().left))).size,
  );
  const expected = { desktop: 4, pixel7: 1, 'pixel7-landscape': 2 }[testInfo.project.name];
  expect(columns).toBe(expected);
});

test('cards fit inside the screen', async ({ page }) => {
  const width = page.viewportSize()!.width;
  for (const box of await page.locator('main.grid > *').evaluateAll((cards) =>
    cards.map((card) => card.getBoundingClientRect().toJSON() as DOMRect),
  )) {
    expect(box.left).toBeGreaterThanOrEqual(0);
    expect(box.right).toBeLessThanOrEqual(width);
  }
});

test('touch targets are at least 44px', async ({ page }) => {
  const buttons = page.getByRole('button');
  for (let i = 0; i < (await buttons.count()); i++) {
    const box = (await buttons.nth(i).boundingBox())!;
    expect(box.width).toBeGreaterThanOrEqual(44);
    expect(box.height).toBeGreaterThanOrEqual(44);
  }
});
