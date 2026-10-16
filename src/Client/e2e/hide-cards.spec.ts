import { expect, test } from '@playwright/test';

test('hides a card and shows it again', async ({ page }) => {
  await page.goto('/');
  const fans = page.getByTestId('fans-card');
  await expect(fans).toBeVisible();

  await fans.getByRole('button', { name: 'Hide Fans' }).click();
  await expect(fans).toHaveCount(0);
  const hidden = page.getByTestId('hidden-cards');
  await expect(hidden.getByRole('button', { name: 'Fans' })).toBeVisible();

  // Saved in localStorage, so it stays hidden after a reload.
  await page.reload();
  await expect(page.getByTestId('system-card')).toBeVisible();
  await expect(fans).toHaveCount(0);

  await hidden.getByRole('button', { name: 'Fans' }).click();
  await expect(fans).toBeVisible();
  await expect(hidden).toHaveCount(0);
});
