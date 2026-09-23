#!/usr/bin/env node
// Ladder rung 10 — browser smoke.
//
// Loads a served Mimas Web build in a headless browser, waits for Unity to boot, and fails loudly on
// anything the browser complains about. This is the gate between "it built" and "a friend can play
// it": nothing else in the ladder ever executes the WebGL-specific paths — the NativeWebSocket jslib
// socket, PlayerPrefs reaching IndexedDB, `?room=` off the page URL, Brotli.
//
//   node tools/smoke/browser-smoke.mjs --expect "\\[NetClient\\] connected"
//   node tools/smoke/browser-smoke.mjs --url http://localhost:7777/?room=ABCD --shot artifacts/smoke
//   node tools/smoke/browser-smoke.mjs --do "wait:2000,click:480,420,type:ABCD,shot:after-click,wait:8000"
//   node tools/smoke/browser-smoke.mjs --do "click:239,637,move:891,345,shot:preview"   (move: no press)
//   node tools/smoke/browser-smoke.mjs --url https://mimas.laststep.cloud/ --timing
//   node tools/smoke/browser-smoke.mjs --browser webkit --timing
//   node tools/smoke/browser-smoke.mjs --headed --do "wait:3000,click:640,520,wait:2000,clipmatch:^[A-HJ-NP-Z2-9]{4}$"
//
// --browser chromium|webkit|firefox (default chromium). WebKit and Firefox have to be fetched once:
//
//   npx playwright install webkit firefox
//
// WebKit on Windows is not Safari — it is the same engine without Apple's own quirks — so it catches
// WebGL2 and Brotli-decoding differences and nothing about iOS. The clipboard steps (`clip:`,
// `clipmatch:`) are Chromium-only and are skipped with a printed [skip] line elsewhere, because only
// Chromium can be granted clipboard permission without a user gesture.
//
// --viewport 1920x1080 sizes the window; the default is 1280x800.
//
// --timing adds one line at the end — boot ms, ms to --expect, and the bytes the page pulled down:
//
//   [timing] boot 4213 ms, expect 6870 ms, transferred 12.4 MB
//
// That last number is what a cold load costs a friend, so it is the one written into the baselines
// in docs/roadmap.md. The browser context is new for every run, so there is no cache to disable.
//
// Exit 0 = the page loaded, Unity booted, `--expect` was seen, and nothing wrote to console.error.
// Exit 1 = a real failure, with the browser's own words in the output. Never retry it away.
//
// On "booted": it means createUnityInstance resolved. Unity's splash screen is still on the canvas
// for a few seconds after that, so booted is NOT playable — pass `--expect` with a line the game
// itself logs (`[ContentBootstrap] Content loaded`, `[NetClient] connected`) when you need to know
// the game is alive. An earlier version of this script gated on the template's loading bar being
// hidden; that is `display: none` in the stylesheet until the loader shows it, so it passed in 906 ms
// against a page that had not started loading. Do not reintroduce that check.

import { chromium, webkit, firefox } from 'playwright';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';

const engines = { chromium, webkit, firefox };

const args = parseArgs(process.argv.slice(2));
const browserName = args.browser ?? 'chromium';
const engine = engines[browserName];
if (!engine) {
  console.error(`unknown --browser "${browserName}". One of: ${Object.keys(engines).join(', ')}`);
  process.exit(2);
}
const url = args.url ?? 'http://localhost:7777/';
const bootTimeout = Number(args.timeout ?? 120_000);
const shotDir = args.shot ?? 'artifacts/smoke';
const headed = 'headed' in args;
const timing = 'timing' in args;
// Errors that are known-benign and must be justified in the run report, never added casually.
const allow = args.allow ? new RegExp(args.allow) : null;

const errors = [];
const log = [];

// --timing bookkeeping. `started` is hoisted out of the try block because the console handler needs
// it to stamp the moment --expect matches.
let started = 0;
let expectMs = null;
let transferred = 0;
const bodySizes = [];

function note(line) {
  log.push(line);
  console.log(line);
}

// Two things were tried before this and both were wrong, so they are written down rather than
// rediscovered: polling for `window.createUnityInstance` loses the race (the loader defines it during
// script evaluation and calls it from that same script's onload), and an Object.defineProperty
// accessor is simply overwritten, because the loader declares it as a global `function`. What is left
// is the template's own loading bar, which is honest as long as BOTH phases are checked.
const browser = await engine.launch({ headless: !headed });
const contextOptions = { viewport: parseViewport(args.viewport) };
// Clipboard permission can only be granted without a gesture in Chromium; Firefox and WebKit reject
// the permission name itself, so asking for it there fails the whole run before the page loads.
if (browserName === 'chromium') contextOptions.permissions = ['clipboard-read', 'clipboard-write'];
const context = await browser.newContext(contextOptions);
const page = await context.newPage();

// A line the game itself must print before this run counts as a pass.
const expect = args.expect ? new RegExp(args.expect) : null;
let expectSeen = false;

page.on('console', (msg) => {
  const text = msg.text();
  const line = `[console.${msg.type()}] ${text}`;
  log.push(line);
  if (expect && expect.test(text) && !expectSeen) {
    expectSeen = true;
    expectMs = Date.now() - started;
  }
  if (msg.type() === 'error') {
    if (allow && allow.test(text)) note(`[allowed] ${text}`);
    else errors.push(line);
  } else if (msg.type() === 'warning') {
    console.log(line);
  } else if (/^\[[A-Za-z]/.test(text)) {
    console.log(line);   // the game's own [Tag] lines are the interesting ones
  }
});

page.on('pageerror', (err) => {
  const line = `[pageerror] ${err.message}`;
  log.push(line);
  errors.push(line);
});

page.on('requestfailed', (req) => {
  const line = `[requestfailed] ${req.url()} — ${req.failure()?.errorText ?? 'unknown'}`;
  log.push(line);
  errors.push(line);
});

page.on('response', (res) => {
  if (res.status() >= 400) {
    const line = `[http ${res.status()}] ${res.url()}`;
    log.push(line);
    errors.push(line);
  }
  if (timing) {
    // Content-Length is what actually crossed the wire for Unity's Brotli files, which is the number
    // that matters. Where a response has no header (chunked, or served from the loader's blob), fall
    // back to reading the body — which can reject if the page moves on first, so it is best-effort.
    const len = res.headers()['content-length'];
    if (len !== undefined) transferred += Number(len) || 0;
    else bodySizes.push(res.body().then((b) => { transferred += b.length; }).catch(() => {}));
  }
});

let failure = null;
try {
  note(`> ${url}  (${browserName}${headed ? ', headed' : ''})`);
  started = Date.now();
  await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 30_000 });

  await page.waitForSelector('#unity-canvas', { timeout: 30_000 });
  note(`canvas present (${Date.now() - started} ms)`);

  // Phase 1: the template shows the loading bar the moment it starts fetching. Without this check the
  // "hidden" test below passes instantly, because the stylesheet hides the bar to begin with.
  await page.waitForFunction(() => {
    const bar = document.querySelector('#unity-loading-bar');
    return bar && getComputedStyle(bar).display !== 'none';
  }, { timeout: 30_000 });
  note(`loader started (${Date.now() - started} ms)`);

  // Phase 2: the template hides it again from createUnityInstance's .then, i.e. the instance exists.
  await page.waitForFunction(() => {
    const bar = document.querySelector('#unity-loading-bar');
    return bar && getComputedStyle(bar).display === 'none';
  }, { timeout: bootTimeout });
  const bootMs = Date.now() - started;
  note(`unity booted (${bootMs} ms) — splash is still on the canvas at this point`);

  await mkdir(shotDir, { recursive: true });

  // Steps first, then the assertion. The client connects lazily — nothing reaches the socket until
  // something is clicked — so checking --expect before the steps can only ever time out.
  await runSteps(page, args.do ?? '', shotDir, note, browserName);

  if (expect) {
    const deadline = Date.now() + Number(args['expect-timeout'] ?? 60_000);
    while (!expectSeen && Date.now() < deadline) await page.waitForTimeout(250);
    if (!expectSeen) throw new Error(`never saw a console line matching /${args.expect}/`);
    note(`saw /${args.expect}/ (${Date.now() - started} ms)`);
  }
  await page.screenshot({ path: path.join(shotDir, 'final.png') });
  note(`screenshot ${path.join(shotDir, 'final.png')}`);
  note(`cold boot to instance: ${bootMs} ms`);
  if (timing) {
    await Promise.all(bodySizes);
    const mb = (transferred / 1048576).toFixed(1);
    note(`[timing] boot ${bootMs} ms, expect ${expectMs === null ? 'n/a' : `${expectMs} ms`}, transferred ${mb} MB`);
  }
} catch (e) {
  failure = e;
} finally {
  try {
    await mkdir(shotDir, { recursive: true });
    if (failure) await page.screenshot({ path: path.join(shotDir, 'failure.png') }).catch(() => {});
    await writeFile(path.join(shotDir, 'console.log'), log.join('\n') + '\n');
  } catch {}
  await browser.close();
}

if (failure) {
  console.error(`\nFAILED: ${failure.message}`);
  if (errors.length) console.error(errors.join('\n'));
  process.exit(1);
}

if (errors.length) {
  console.error(`\nFAILED: ${errors.length} browser error(s)\n` + errors.join('\n'));
  process.exit(1);
}

note('\nOK — no pageerror, no console.error, no failed request.');

/** `wait:2000,click:480,420,shot:lobby` — ordered steps, so a scenario needs no second file. */
async function runSteps(page, spec, dir, say, browserName = 'chromium') {
  if (!spec) return;
  const parts = spec.split(',').map((s) => s.trim()).filter(Boolean);
  for (let i = 0; i < parts.length; i++) {
    const [verb, first] = parts[i].split(':');
    if (verb === 'wait') {
      await page.waitForTimeout(Number(first));
      say(`wait ${first} ms`);
    } else if (verb === 'shot') {
      const file = path.join(dir, `${first}.png`);
      await page.screenshot({ path: file });
      say(`screenshot ${file}`);
    } else if (verb === 'click') {
      // Canvas-relative CSS pixels: the next part is the y, since the split ate the comma.
      const x = Number(first);
      const y = Number(parts[++i]);
      const box = await page.locator('#unity-canvas').boundingBox();
      if (!box) throw new Error('cannot click: #unity-canvas has no box');
      await page.mouse.move(box.x + x, box.y + y);
      await page.waitForTimeout(120);
      // Held for a few frames, as a hand does: a press and release inside one frame never reads as
      // wasPressedThisFrame to the Input System, so a board click would silently do nothing.
      await page.mouse.click(box.x + x, box.y + y, { delay: 80 });
      say(`click ${x},${y} (canvas-relative)`);
    } else if (verb === 'move') {
      // Rests the pointer without pressing, in steps so the page sees it travel: how an armed attack's
      // preview over the target is reached, since a click there would fire it.
      const x = Number(first);
      const y = Number(parts[++i]);
      const box = await page.locator('#unity-canvas').boundingBox();
      if (!box) throw new Error('cannot move: #unity-canvas has no box');
      await page.mouse.move(box.x + x, box.y + y, { steps: 8 });
      say(`move ${x},${y} (canvas-relative)`);
    } else if (verb === 'key') {
      await page.keyboard.press(first);
      say(`key ${first}`);
    } else if (verb === 'clip') {
      // Seeds the REAL browser clipboard, so a following `key:Control+v` is a genuine paste rather
      // than a synthetic text insertion. The difference matters: CDP insertText targets a DOM text
      // element and Unity's canvas is not one.
      if (browserName !== 'chromium') { say(`[skip] clipboard step: ${browserName}`); continue; }
      await page.evaluate((t) => navigator.clipboard.writeText(t), first);
      say(`clipboard := "${first}"`);
    } else if (verb === 'clipmatch') {
      // Reads the REAL clipboard back and fails unless it matches. This is how Copy code is proved:
      // the game wrote it through WebClipboard, and nothing but the browser's own clipboard is asked.
      if (browserName !== 'chromium') { say(`[skip] clipboard step: ${browserName}`); continue; }
      const text = await page.evaluate(() => navigator.clipboard.readText());
      if (!new RegExp(first).test(text)) throw new Error(`clipboard is "${text}", which does not match /${first}/`);
      say(`clipboard is "${text}" — matches /${first}/`);
    } else if (verb === 'insert') {
      // Text with no key events at all — how a paste and an IME commit arrive. If this lands where
      // `type:` does not, the break is in key handling and an HTML/IME path would work.
      await page.keyboard.insertText(first);
      say(`insertText "${first}"`);
    } else if (verb === 'type') {
      // Typed one character at a time, like a player: a TextField that never sees keydown/keypress
      // is the WebGL failure this step exists to catch.
      await page.keyboard.type(first, { delay: 60 });
      say(`type "${first}"`);
    } else {
      throw new Error(`unknown step "${parts[i]}"`);
    }
  }
}

/** `--viewport 1920x1080`; the default is the 1280x800 every earlier run used. */
function parseViewport(spec) {
  if (!spec || spec === true) return { width: 1280, height: 800 };
  const [w, h] = String(spec).toLowerCase().split('x').map(Number);
  if (!w || !h) throw new Error(`--viewport wants WIDTHxHEIGHT, got "${spec}"`);
  return { width: w, height: h };
}

function parseArgs(argv) {
  const out = {};
  for (let i = 0; i < argv.length; i++) {
    if (!argv[i].startsWith('--')) continue;
    const key = argv[i].slice(2);
    const next = argv[i + 1];
    if (next && !next.startsWith('--')) { out[key] = next; i++; }
    else out[key] = true;
  }
  return out;
}
