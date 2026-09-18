#!/usr/bin/env node
// Frame-time probe for a served Mimas Web build.
//
// `browser-smoke.mjs` answers "did it run and did it complain". This answers "was it smooth", which
// is the only honest reply to "it felt janky". It boots the build, optionally clicks its way into a
// match, then samples requestAnimationFrame deltas and reports the distribution — because the number
// that matters is not the average frame, it is the worst one in twenty.
//
//   node tools/smoke/browser-perf.mjs --seconds 20 --click 480,298 --settle 9000
//   node tools/smoke/browser-perf.mjs --headless        # SwiftShader: use for CI, never for a verdict
//
// Run it HEADED by default and on the machine whose feel is in question. Headless Chromium falls back
// to a software rasteriser, where every number here is fiction.
//
// A frame budget of 16.7 ms is 60 fps. Chromium drives rAF at the display's refresh rate, so on a
// 60 Hz panel a perfect run is a wall of 16.7s and the interesting column is `>33ms` — one dropped
// frame — and `>50ms`, which is the point a human calls it a stutter.

import { chromium } from 'playwright';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';

const args = parseArgs(process.argv.slice(2));
const url = args.url ?? 'http://localhost:7777/';
const seconds = Number(args.seconds ?? 20);
const settle = Number(args.settle ?? 9000);
const outDir = args.out ?? 'artifacts/perf';
const headed = !('headless' in args);

const log = [];
const note = (l) => { log.push(l); console.log(l); };

const browser = await chromium.launch({ headless: !headed });
const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
const page = await context.newPage();

page.on('console', (m) => {
  const t = m.text();
  log.push(`[console.${m.type()}] ${t}`);
  if (m.type() === 'error' || /^\[[A-Za-z]/.test(t)) console.log(`[console.${m.type()}] ${t}`);
});
page.on('pageerror', (e) => note(`[pageerror] ${e.message}`));

note(`> ${url}  (${headed ? 'headed — real GPU' : 'HEADLESS — software rasteriser, numbers are fiction'})`);
await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 30_000 });
await page.waitForSelector('#unity-canvas', { timeout: 30_000 });
await page.waitForFunction(() => {
  const b = document.querySelector('#unity-loading-bar');
  return b && getComputedStyle(b).display !== 'none';
}, { timeout: 30_000 });
await page.waitForFunction(() => {
  const b = document.querySelector('#unity-loading-bar');
  return b && getComputedStyle(b).display === 'none';
}, { timeout: Number(args.timeout ?? 120_000) });
note('unity booted');

// The canvas's CSS size, which is what every --click coordinate is relative to.
const box = await page.locator('#unity-canvas').boundingBox();
const dims = await page.evaluate(() => {
  const c = document.querySelector('#unity-canvas');
  return { cssW: c.clientWidth, cssH: c.clientHeight, bufW: c.width, bufH: c.height, dpr: devicePixelRatio };
});
note(`canvas css ${dims.cssW}x${dims.cssH}, drawing buffer ${dims.bufW}x${dims.bufH}, dpr ${dims.dpr}`);

// A headed window that never got OS focus swallows CDP mouse events: the first run of this script
// clicked "Play vs bot" twenty times and measured the idle lobby.
await page.bringToFront();

for (const spec of [].concat(args.click ?? [])) {
  const [x, y] = String(spec).split(',').map(Number);
  await page.mouse.move(box.x + x, box.y + y);
  await page.waitForTimeout(150);
  await page.mouse.click(box.x + x, box.y + y);
  note(`click ${x},${y}`);
  await page.waitForTimeout(settle);
}

note(`sampling ${seconds}s of requestAnimationFrame…`);
const result = await page.evaluate(async (ms) => {
  const deltas = [];
  let last = performance.now();
  await new Promise((done) => {
    const end = last + ms;
    function tick(now) {
      deltas.push(now - last);
      last = now;
      if (now < end) requestAnimationFrame(tick); else done();
    }
    requestAnimationFrame(tick);
  });
  const mem = performance.memory
    ? { usedMB: +(performance.memory.usedJSHeapSize / 1048576).toFixed(1) }
    : null;
  return { deltas: deltas.slice(1), mem };  // drop the first: it spans the evaluate call
}, seconds * 1000);

const d = result.deltas.slice().sort((a, b) => a - b);
const q = (p) => d[Math.min(d.length - 1, Math.floor(d.length * p))];
const over = (t) => result.deltas.filter((x) => x > t).length;
const total = result.deltas.reduce((a, b) => a + b, 0);

const report = {
  frames: d.length,
  seconds: +(total / 1000).toFixed(1),
  fps_mean: +((d.length / total) * 1000).toFixed(1),
  p50: +q(0.5).toFixed(1),
  p95: +q(0.95).toFixed(1),
  p99: +q(0.99).toFixed(1),
  max: +d[d.length - 1].toFixed(1),
  over33: over(33),
  over50: over(50),
  over100: over(100),
  worst10: d.slice(-10).map((x) => +x.toFixed(1)),
  heapMB: result.mem?.usedMB ?? null,
  canvas: dims,
};

note('');
note(`frames ${report.frames} in ${report.seconds}s — mean ${report.fps_mean} fps`);
note(`frame ms: p50 ${report.p50}  p95 ${report.p95}  p99 ${report.p99}  max ${report.max}`);
note(`dropped:  >33ms ${report.over33} (${((report.over33 / report.frames) * 100).toFixed(1)}%)   >50ms ${report.over50}   >100ms ${report.over100}`);
note(`worst 10: ${report.worst10.join(', ')}`);
if (report.heapMB != null) note(`js heap: ${report.heapMB} MB`);

await mkdir(outDir, { recursive: true });
await page.screenshot({ path: path.join(outDir, 'perf.png') });
await writeFile(path.join(outDir, 'perf.json'), JSON.stringify(report, null, 2) + '\n');
await writeFile(path.join(outDir, 'console.log'), log.join('\n') + '\n');
note(`\nwrote ${path.join(outDir, 'perf.json')}`);

await browser.close();

function parseArgs(argv) {
  const out = {};
  for (let i = 0; i < argv.length; i++) {
    if (!argv[i].startsWith('--')) continue;
    const key = argv[i].slice(2);
    const next = argv[i + 1];
    if (next && !next.startsWith('--')) {
      out[key] = key in out ? [].concat(out[key], next) : next;
      i++;
    } else out[key] = true;
  }
  return out;
}
