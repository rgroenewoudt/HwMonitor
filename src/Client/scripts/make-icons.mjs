// Renders public/icons/icon.svg to the PNG sizes the web app manifest needs,
// and to ../Server/app.ico (the Windows exe icon).
// Run: node scripts/make-icons.mjs
import { chromium } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';

const svg = await readFile('public/icons/icon.svg', 'utf8');
const sizes = [72, 96, 128, 144, 152, 192, 384, 512];
const icoSizes = [16, 24, 32, 48, 64, 128, 256];
const browser = await chromium.launch();
const page = await browser.newPage();

async function render(size, file) {
  await page.setViewportSize({ width: size, height: size });
  await page.setContent(`<style>*{margin:0}svg{display:block;width:${size}px;height:${size}px}</style>${svg}`);
  const png = await page.screenshot({ path: file && `public/icons/${file}`, omitBackground: true });
  if (file) console.log(file);
  return png;
}

for (const size of sizes) await render(size, `icon-${size}x${size}.png`);
// The logo stays inside the inner 64%, so the same picture is safe as a maskable icon.
await render(512, 'icon-maskable-512x512.png');

// ICO file with PNG images (Windows Vista and newer): 6 byte header, 16 byte entry per image, then the PNGs.
const pngs = [];
for (const size of icoSizes) pngs.push(await render(size));
const header = Buffer.alloc(6 + 16 * pngs.length);
header.writeUInt16LE(1, 2); // type: icon
header.writeUInt16LE(pngs.length, 4);
let offset = header.length;
pngs.forEach((png, i) => {
  const entry = 6 + 16 * i;
  header.writeUInt8(icoSizes[i] % 256, entry); // 0 = 256
  header.writeUInt8(icoSizes[i] % 256, entry + 1);
  header.writeUInt16LE(1, entry + 4); // color planes
  header.writeUInt16LE(32, entry + 6); // bits per pixel
  header.writeUInt32LE(png.length, entry + 8);
  header.writeUInt32LE(offset, entry + 12);
  offset += png.length;
});
await writeFile('../Server/app.ico', Buffer.concat([header, ...pngs]));
console.log('../Server/app.ico');
await browser.close();
