---
project: mimas
milestone: M2
updated: 2026-09-22
updated_by: spec session with Rohan (deploy; no code)
---

# Where Mimas stands

> **Rewritten, never appended.** One page, always current. History lives in `runs/` and in git.

## Right now

**The match is on the server.** From the lobby you press **Play vs bot** or **Create room**, get a
four-letter code, pick your gear in the room, press Ready, and play a real match whose truth lives in
`Mimas.Server` — with a 30 s server-authoritative turn, resign, and a reconnect grace. The client holds
a *mirror* of the match rebuilt from its own `PlayerView` (ADR-026), so it can only ever know what the
server chose to send.

**321 Core tests and 34 server tests**, the latter playing whole matches over real sockets. Both are
ladder rungs, both required, both green on 18 Sep.

**It runs in a browser, and on 18 Sep Rohan played it there for the first time.** That play is the most
valuable thing that has happened to this milestone: it found two shipped defects and killed one theory,
none of which any test or ladder rung would have caught.

| What he said | What it was | State |
|---|---|---|
| "wasnt able to put the lobby code to join a room" | **Unity bug 4006** — UI Toolkit text fields lose focus instantly in a Web build. Affects 6000.4.0b11 onward; Unity fixed it in 6000.4.6f1 | **fixed and verified** 18 Sep by the bump to **6000.4.12f1** (ADR-030) |
| "both units and props as pink color" | A **stale build**, made at 21:52 on 17 Sep; the fix was written at 21:54 | **fixed and verified** 18 Sep (`artifacts/verify-0918/arena.png`) |
| "felt like it was dropping frames … bit janky" | **Not resolution** — measured, see below | **open**, now instrumented |

### The hole this opened: joining a room in a browser

Not "typing is awkward" — there was **no working path at all** for a second human, checked rather than
assumed (run report R-2026-09-18-T-0003, findings 7 and 9):

| Path | State |
|---|---|
| Type the code | **works** since the 6000.4.12f1 bump. `abcd` → `ABCD` → `no_such_room` from the server |
| Paste the code | **works** — a real Ctrl+V from a seeded clipboard reaches the field, so `#online` rule 2 is honest again |
| Press **Copy link**, send it | **fixed 18 Sep.** `GUIUtility.systemCopyBuffer` never reached the browser clipboard, so the lobby said "Link copied." over an empty one. Now goes through `navigator.clipboard` |
| `?room=CODE` in the address bar | works, and was the only path before the clipboard fix |

**All four paths now work.** The address bar was the only one for most of 18 Sep; the clipboard fix and
the engine bump restored the other three the same day.

## Current milestone: M2 — online

Target: **Sat 10 Oct 2026**, friends playing over the internet. **18 days.**

| Done-when | State |
|---|---|
| M2-1 Two browsers play a full match | **one browser plays the server.** The second seat now has three working ways in (typed code, pasted code, invite link) and has still never been driven by a human. That is the whole remaining gap |
| M2-2 Two players who want to play each other end up in the same match | **done** — room code (OPT-0001, ADR-029) |
| M2-3 Guest auth with a resumable token | **done** (browser reload still untested by a human) |
| M2-4 Server-authoritative 30 s turn | **done**, seen firing live |
| M2-5 Reload within the 60 s grace resyncs | **done** server-side and in the Editor |
| M2-6 Resign and disconnect-forfeit | **done**, both paths seen |
| M2-7 Deployed on the VPS over HTTPS | **specced 22 Sep, not built** — `docs/specs/2026-09-22-deploy.md`, task T-0007. Still the whole gap to 10 Oct |
| M2-8 Rematch without leaving the room | not started (out of scope by D9) |

**Nothing in the ledger is ticked.** `pass` belongs to a verifier, not the builder who wrote the code.

## In flight

| Task | What | Status | Who |
|---|---|---|---|
| **T-0007** | **Deploy: `mimas.laststep.cloud`, one-command redeploy, a runbook for Rohan** | **approved — next Opus session executes Part 1 of the spec** | builder (opus) |
| T-0002 | Execute the online slice | verify | builder |
| T-0003 | The Web build, in a browser | running — day 2 | builder |
| T-0005 | Copy code button beside Copy link | todo (from 21 Sep playtest) | builder |
| T-0006 | Refused shots show the blocking hex; prop art fills its hex | todo (from 21 Sep playtest) | builder |

## Blocked

| Task | Blocked by | Since |
|---|---|---|
| — | nothing | |

## Waiting on Rohan

| What | Why | Since |
|---|---|---|
| **Play one bot match at `?perf=1`** and say what the meter showed, or when it hitched | It is the only instrument that sees his 144 Hz vsync. Nothing outside the game can. This is the last of the three findings still open | 18 Sep 2026 |
| Edit `pillars.md` — it is a draft distilled from the design page, and the pillars are yours | | 17 Sep 2026 |
| Optional: should a room show the other seat's chosen preset before the match starts? | design `#q-online-room-loadout` | 17 Sep 2026 |
| Answer OQ-N03, N07, N11 on `docs/design/mechanics.xlsx` before anyone builds the board mechanics | jump/teleport crossing a beam; trap consumed on fire; lane and power for structure damage | 21 Sep 2026 |
| **After T-0007 Part 1 lands: add the `mimas` A record in Hostinger hPanel and follow `docs/deploy-runbook.md`** | You chose to run the first deploy yourself (spec D4). Nothing can be measured on the internet until you do | 22 Sep 2026 |
| Commit the 21 Sep design files and today's spec files, or say who should | Uncommitted since 21 Sep: `docs/design/mechanics.xlsx`, the board-mechanics brief and research, the playtest, T-0005/T-0006, the prep plan; today: the deploy spec, T-0007, P-T-0007, F-deploy, STATE | 22 Sep 2026 |

## Decided on 18 Sep

- **Engine: 6000.4.12f1 now, 6.7 LTS when it ships, 6.6 skipped entirely.** Rohan considered jumping
  straight to 6000.6.1f1 and decided against it once the release picture was clear: `unity releases`
  reports 6.6 as `lts=False stream=SUPPORTED`, and **6.7 LTS is due Q4 2026** with Unity 7 in beta from
  December — so 6.6 is a ~3-month stop, not a resting place, and going there means two upgrades instead
  of one. 6000.4.12f1 is a patch bump on the stream we are already on and contains the 4006 fix
  (`fixedInVersion: 6000.4.6f1`).
  What 6.6 *would* have bought, and what 6.7 will: production WebGPU — compute shaders, GPU skinning,
  **VFX Graph in a browser**, WebAssembly64, progressive asset loading. That lifts the constraint behind
  ADR-008 and golden rule 9, so the "no VFX Graph" rule is worth revisiting at 6.7, not before.
  **When any upgrade happens, check `com.unity.pipeline 0.7.0-exp.1` first** — it is an experimental
  package and the bridge every `unity command` goes through. If it does not resolve, the live-Editor
  path is gone and golden rule 2 has no way to be satisfied.
- **The custom Web template comes after deploy is green.** It replaces the 960×600 box, the Unity
  footer, the `Unity Web Player | MimasClient` title and the splash — all one file, so it is opened
  once. Deploy is what can slip 10 Oct; presentation is not.
- **Resolution: nothing to cap.** Decided to cap-and-tune from measurement; then the measurement said
  there is nothing to cap.

## The jank, and what it is not

Measured in a live bot match on Rohan's own GPU, resizing the drawing buffer to what his fullscreen
asks for (`tools/smoke/browser-perf.mjs`, `artifacts/perf-*`):

| Drawing buffer | Mpx | mean fps | p50 | p95 | max | frames >33 ms |
|---|---|---|---|---|---|---|
| 960×600 | 0.58 | 240 | 4.2 | 4.3 | 4.7 | 0 |
| **2560×1440** (his fullscreen) | **3.69** | **240** | **4.2** | **4.3** | **4.9** | **0** |
| 5120×2880 | 14.75 | 156 | 4.3 | 8.5 | 37.7 | 1 |

**At the resolution he played, the arena renders in under 4.17 ms against a 6.94 ms budget at 144 Hz —
about 4× headroom.** Capping would have cost sharpness and fixed nothing.

But every run measured an **idle** arena, so the cause is transient. `FrameProbe` (`?perf=1`) now ships
in the build and logs every frame over 50 ms with the scene, the timestamp and **whether the collector
ran** — the bit that separates a GC hitch from a shader compiling. Its first run already showed a
**267 ms frame entering the Arena with `gc=no`**, which is the shape of first-use shader compilation.
That was under a software rasteriser, so the number is inflated; the attribution is not.

## Design: board mechanics (21 Sep, concept level, no code)

Rohan spent a session on five ideas: buildables, hidden traps, area of influence, a laser-sight prop
and start-of-turn "guaranteed actions". Twenty decisions, none implemented, none on the design page yet
by his choice. **Fog of war is in** (a hero sees its *aura*; builds add vision), three innate actions
(move, build, destroy), structures as owned bodies, traps that are a marker if placed in enemy vision
and invisible otherwise, a per-attack `needsVision` flag with blind shots, and guaranteed actions in
placement order. The records:

| File | What |
|---|---|
| `docs/design/mechanics.xlsx` | **Source of truth for mechanic detail and numbers** from now on. 14 sheets: registry (M-ids), relations, trigger vocabulary, buildables, shipped content, tunables, open questions, decisions |
| `docs/design/2026-09-21-board-mechanics.md` | The concepts in prose, what they change on the design page, the risks |
| `studio/decisions/RESEARCH-2026-09-21-board-mechanics.md` | Precedents (Into the Breach, Hearthstone Secrets, Techies, Wesnoth) with sources |

Three open questions block a first slice (OQ-N03, N07, N11 on the workbook; N08 was answered in the
wrap-up: the laser hurts its own builder). Nothing in M2 depends on any of this; it is M3-or-later design.

## Decided on 22 Sep: deploy, and how

The spec session happened. Rohan chose **deploy** over board mechanics, and then, one round at a time:

| Decision | Chosen |
|---|---|
| Address | **`mimas.laststep.cloud`** on the existing Hostinger VPS, permanent. Own nginx file, own Let's Encrypt cert; the `laststep.cloud` site's file is never edited |
| Who deploys | **Rohan**, from `tools/deploy/deploy.sh` in Git Bash, with `docs/deploy-runbook.md` for the one-time VPS setup. **Agents only ever `--dry-run`.** Automation is a later decision; the ssh alias is already on this PC for it |
| Process on the VPS | systemd unit as a `mimas` user + `dotnet publish -r linux-x64 --self-contained`; nothing to install on the box. Docker is there but is another container beside sixteen |
| Transfer | `tar` over `ssh` (Git Bash has no rsync) |
| Gate | 25 MB hard fail in the script; the 13 MB ratchet stays |

The spec is `docs/specs/2026-09-22-deploy.md` in three parts: **Part 1** one Opus session writes the
server bind flag, six files in `tools/deploy/`, `--timing` in the smoke harness, the runbook and the
doc rewrites, and proves them locally (dry-run, the Linux publish running inside WSL, the gate tripping,
the local smoke). **Part 2** is Rohan with the runbook. **Part 3** is a short session that runs the
smoke against the live URL, writes the cold-load baseline, and hands to a verifier. Task `T-0007`, plan
`P-T-0007`, F-deploy `approved`.

**The VPS, surveyed read-only on 22 Sep** (spec §3 has the table): Ubuntu 24.04, nginx 1.24 with no
brotli module (irrelevant, the files are pre-compressed), certbot with the nginx plugin and a renewal
timer, Hostinger DNS and **not Cloudflare**, sixteen Docker containers all on loopback, a `webhook`
service on `*:9000`, no firewall, **port 7777 free**, no .NET. The alias `ssh hostinger` (root) now
works from Windows as well as WSL; the key was copied over on 22 Sep.

## Last playtest

**21 Sep 2026, Rohan, browser vs bot** (`studio/playtests/2026-09-21-rohan.md`). Three notes: a Copy
code button is missing (**T-0005**); shots refused where the picture looks open, almost certainly the
placeholder prop box being narrower than the hex column the rules block (**T-0006**, show the blocking
hex); and whether a turn should end itself at 0 AP, parked as **OQ-N16** for the full gameplay design.
No frame-meter reading was reported.

## The two things that matter next

1. **Deploy** (`F-deploy`, M2-7, T-0007). Nothing about 10 October works without it. The spec exists;
   the next Opus session builds Part 1; Rohan then deploys.
2. **Two browsers against each other** (M2-1) — a window and an incognito window, joined by code. Every
   piece now works in isolation; nobody has put them together.

From `E:\Studios\Trinetra-Game-Studio\docs\PLAN.md` §10: if two browsers cannot play a full match
through the server by **Fri 2 Oct**, the 10 Oct playtest falls back to the local build and online moves
to 17 Oct. The game code is not what puts that at risk; deploy is.

## Things the next agent must not rediscover

- **Build through the script, not the profile.** `unity build MimasClient --target WebGL
  --execute-method Mimas.Client.Editor.WebBuild.Build --output-path Build/Web`. The `Web Release`
  **profile** carries its own PlayerSettings snapshot with `webGLExceptionSupport: 0` — and, per the
  18 Sep research, the WebGL **template** too. Both belong in `WebBuild.cs`.
- **A Development Build does not link** on 6000.4.4f1: `wasm-ld: undefined symbol:
  unitytls_ssl_set_client_transport_id`. No public report of it exists. Release is unaffected. Do not
  "fix" it with `ERROR_ON_UNDEFINED_SYMBOLS=0` — that moves the failure to runtime. Recheck on 6.6.
- **Unity's issue tracker has a JSON API**: `https://issuetracker.unity.com/api/v1.0/issues/<id>` gives
  `firstAffectedVersion` and `fixedInVersion` per stream. The web page is a SPA and tells you nothing.
- **`GUIUtility.systemCopyBuffer` does not reach the browser clipboard.** Use `WebClipboard`.
- **Headless Chromium is a software rasteriser** (it advertises ASTC/ETC; a real GPU advertises
  `EXT_disjoint_timer_query_webgl2`). Headed Chromium runs rAF at its own ceiling — 240 Hz here — not
  the display's. Neither reproduces a 144 Hz vsync. Frame verdicts come from `FrameProbe`, not the harness.
- **"Booted" is not "playable".** `createUnityInstance` resolving leaves the splash on the canvas for
  seconds. Wait first, or you will measure the lobby and call it the arena. Cost: two wasted runs and
  one wrong conclusion.
- **The asset guard's false positives now number seven**, all read-only commands: `2>&1` and
  `2>/dev/null`; `>(` inside a C# generic; a protected path named in prose in a `git commit -m`; a
  `find … -not -path "*/Library/*"`; a `sed 's///'` over a protected file; **the literal string
  `com.unity.…` in a comment**, which matches `**/*.unity`; and (22 Sep) a Python `open('studio/ledger.json')`
  for reading. Restructure (the Read tool reads the ledger fine); never rephrase to slip past.
- **The VPS is shared.** `sites-enabled/laststep.cloud` belongs to another product on the same box.
  Mimas has its own file, unit, user and two directories, and that is the whole footprint.
- **nginx 1.24 syntax.** `listen 443 ssl http2;` is right on the VPS; `http2 on;` needs 1.25.

## Previous playtest

**18 Sep 2026, Rohan, the browser build.** Three findings, all above. Two shipped defects found, one
theory killed.

**Still unplayed by a human: two browsers against each other** — the whole of M2-1.
