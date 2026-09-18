// Does Unity's GUIUtility.systemCopyBuffer actually reach the browser clipboard?
// LobbyView.HandleCopyLink writes it and then tells the player "Link copied." If the write does not
// leave Unity's own enclosed buffer, that message is a lie and the only working join path is broken.
import { chromium } from 'playwright';

const url = 'http://localhost:7777/';
const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({
  viewport: { width: 1280, height: 800 },
  permissions: ['clipboard-read', 'clipboard-write'],
});
const page = await context.newPage();
page.on('console', (m) => { const t = m.text(); if (/^\[[A-Za-z]/.test(t)) console.log(`  ${t}`); });

await page.goto(url, { waitUntil: 'domcontentloaded' });
await page.waitForSelector('#unity-canvas');
await page.waitForFunction(() => getComputedStyle(document.querySelector('#unity-loading-bar')).display !== 'none');
await page.waitForFunction(() => getComputedStyle(document.querySelector('#unity-loading-bar')).display === 'none', { timeout: 120000 });
console.log('booted');

// Seed the real clipboard with a sentinel, so "unchanged" is distinguishable from "empty".
await page.evaluate(() => navigator.clipboard.writeText('SENTINEL-BEFORE-COPY'));
console.log('clipboard seeded: SENTINEL-BEFORE-COPY');

const box = await page.locator('#unity-canvas').boundingBox();
const click = async (x, y, label) => {
  await page.mouse.move(box.x + x, box.y + y);
  await page.waitForTimeout(150);
  await page.mouse.click(box.x + x, box.y + y);
  console.log(`click ${label}`);
};

await page.waitForTimeout(3500);
await click(480, 298, 'Play vs bot');
await page.waitForTimeout(4000);
await click(480, 240, 'Copy link');
await page.waitForTimeout(1500);

const after = await page.evaluate(() => navigator.clipboard.readText());
console.log(`\nclipboard after Copy link: ${JSON.stringify(after)}`);
console.log(after.includes('?room=')
  ? 'RESULT: systemCopyBuffer DOES reach the browser clipboard.'
  : 'RESULT: it does NOT — "Link copied." is a lie in the browser.');

await page.screenshot({ path: 'artifacts/clipboard/after-copy.png' });
await browser.close();
