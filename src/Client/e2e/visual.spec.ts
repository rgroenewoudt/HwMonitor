import { expect, test } from '@playwright/test';
import { FROZEN_URL } from '../playwright.config';

// Frozen fake values, so the page looks the same every run.
test.use({ baseURL: FROZEN_URL });

test('dashboard looks right', async ({ page }) => {
  // Web fonts load from the internet; use the fallback font for stable screenshots.
  await page.route(/fonts\.(googleapis|gstatic)\.com/, (route) => route.abort());
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/');
  await expect(page.getByTestId('status-badge')).toHaveText('Live');
  await expect(page.getByTestId('fan-tile')).toHaveCount(3);

  await expect(page).toHaveScreenshot('dashboard.png', {
    fullPage: true,
    // The time in the header changes every second.
    mask: [page.locator('.updated')],
  });
});
