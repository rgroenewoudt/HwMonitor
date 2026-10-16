// Renders public/icons/icon.svg to the PNG sizes the web app manifest needs.
// Run: node scripts/make-icons.mjs
import { chromium } from '@playwright/test';
import { readFile } from 'node:fs/promises';

const svg = await readFile('public/icons/icon.svg', 'utf8');
const sizes = [72, 96, 128, 144, 152, 192, 384, 512];
const browser = await chromium.launch();
const page = await browser.newPage();

async function render(size, file) {
  await page.setViewportSize({ width: size, height: size });
  await page.setContent(`<style>*{margin:0}svg{display:block;width:${size}px;height:${size}px}</style>${svg}`);
  await page.screenshot({ path: `public/icons/${file}`, omitBackground: true });
  console.log(file);
}

for (const size of sizes) await render(size, `icon-${size}x${size}.png`);
// The logo stays inside the inner 64%, so the same picture is safe as a maskable icon.
await render(512, 'icon-maskable-512x512.png');
await browser.close();
