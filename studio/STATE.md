---
project: mimas
milestone: M2
updated: 2026-09-22
updated_by: builder (opus) — T-0007 Part 1, the deploy artefacts
---

# Where Mimas stands

> **Rewritten, never appended.** One page, always current. History lives in `runs/` and in git.

## Right now

**The match is on the server, and the game is one command away from the internet.** From the lobby you
press **Play vs bot** or **Create room**, get a four-letter code, pick your gear in the room, press
Ready, and play a real match whose truth lives in `Mimas.Server` — with a 30 s server-authoritative
turn, resign, and a reconnect grace. The client holds a *mirror* of the match rebuilt from its own
`PlayerView` (ADR-026), so it can only ever know what the server chose to send.

**321 Core tests and 34 server tests**, the latter playing whole matches over real sockets. Both are
ladder rungs, both required, both green on 22 Sep.

**It runs in a browser.** Rohan played it there on 18 Sep — the most valuable thing that has happened
to this milestone, and the only reason two shipped defects were found. All four ways into a room
(type the code, paste it, invite link, `?room=` in the address bar) now work.

**What is new today (22 Sep): everything needed to deploy, and nothing deployed.** T-0007 Part 1 is
done and proved locally. Part 2 is Rohan's, and it is the only thing standing between here and a link
a friend can open.

## The one thing to do next

**Rohan: follow `docs/deploy-runbook.md`.** §1 is one DNS record in Hostinger hPanel (type A, name
`mimas`, pointing where `laststep.cloud` points). §2 is `bash tools/deploy/deploy.sh --setup`, once.
§3 is `bash tools/deploy/deploy.sh`, every time after. Nothing can be measured on the internet until
the A record exists, and nothing else in M2 is blocked on anything.

## Current milestone: M2 — online

Target: **Sat 10 Oct 2026**, friends playing over the internet. **18 days.**

| Done-when | State |
|---|---|
| M2-1 Two browsers play a full match | **one browser plays the server.** The second seat has three working ways in and has still never been driven by a human. That is the whole remaining game-side gap |
| M2-2 Two players who want to play each other end up in the same match | **done** — room code (OPT-0001, ADR-029) |
| M2-3 Guest auth with a resumable token | **done** (browser reload still untested by a human) |
| M2-4 Server-authoritative 30 s turn | **done**, seen firing live |
| M2-5 Reload within the 60 s grace resyncs | **done** server-side and in the Editor |
| M2-6 Resign and disconnect-forfeit | **done**, both paths seen |
| M2-7 Deployed on the VPS over HTTPS | **artefacts written and rehearsed 22 Sep; live pending Rohan.** See below |
| M2-8 Rematch without leaving the room | not started (out of scope by D9) |

**Nothing in the ledger is ticked.** `pass` belongs to a verifier, not the builder who wrote the code.

## Deploy: what exists, what is proved, what is not

`docs/specs/2026-09-22-deploy.md` in three parts. **Part 1 is done** (T-0007, run report
`R-2026-09-22-T-0007`, six commits on `main`, nothing pushed).

| Artefact | What it does |
|---|---|
| `MIMAS_BIND=loopback` in `Program.cs` | the server binds `127.0.0.1` only, which is what makes an unfirewalled VPS safe. Unset behaves exactly as before, so dev and tests are untouched |
| `tools/deploy/nginx-mimas.conf` | the real host, its own certificate, Brotli headers, the `/ws` proxy. Its upgrade map writes `$mimas_connection_upgrade`, **not** the conventional name — two files in `sites-enabled/` declaring one map variable is a duplicate-map error that would take the other tenant's site down with ours |
| `nginx-mimas-bootstrap.conf` | port 80 only, alive just long enough for certbot to answer the challenge |
| `mimas-server.service` | systemd, user `mimas`, restart on failure, logs to journald |
| `vps-setup.sh` | idempotent. Refuses until DNS points at the box; makes the user, dirs, cert, nginx site and unit; **restores the previous nginx file if `nginx -t` fails** |
| `vps-release.sh` | the `.next` / `.prev` swap for both halves, restart, 15 s health wait, `--rollback` |
| `deploy.sh` | the one command. `--help --dry-run --skip-build --setup --rollback --smoke` |
| `browser-smoke.mjs --timing` | boot ms, ms to `--expect`, bytes transferred |
| `docs/deploy-runbook.md` | what Rohan reads |

**Proved locally, not on the VPS:** the full dry run exits 0 including a real Unity build (12.31 MB,
same as 18 Sep, and it needs **no live Editor** — batch mode opens its own); the size gate exits 1 at
a fake 1 MB limit; the `linux-x64` self-contained publish **actually runs inside WSL** and reports the
same content hash as the Windows server (`ea75e2db…`); loopback answers and the LAN address is
refused; the browser smoke is green at `boot 616 ms, expect 3040 ms, transferred 12.3 MB` on
localhost. Only two read-only `ssh hostinger` commands were run all session — agents never deploy
(decision D4).

**Not proved:** anything that needs the VPS. The nginx file has never been parsed by nginx (Docker
Desktop was not running, so the optional local parse was skipped); `tar` from Git Bash has never met
Ubuntu's `tar` (mitigated with `--format=ustar`); `ProtectSystem=strict` has never started the
process. Spec §13 has a fork for each.

## In flight

| Task | What | Status | Who |
|---|---|---|---|
| **T-0007** | **Deploy** | **Part 1 done; waiting on Rohan's deploy, then Part 3 measures and hands to a verifier** | builder (opus) |
| T-0002 | Execute the online slice | verify | builder |
| T-0003 | The Web build, in a browser | running — day 2 | builder |
| T-0005 | Copy code button beside Copy link | todo (from 21 Sep playtest) | builder |
| T-0006 | Refused shots show the blocking hex; prop art fills its hex | todo (from 21 Sep playtest) | builder |

## Blocked

| Task | Blocked by | Since |
|---|---|---|
| T-0007 Part 3 | Rohan's first deploy (Part 2) | 22 Sep 2026 |

## Waiting on Rohan

| What | Why | Since |
|---|---|---|
| **Add the `mimas` A record, then follow `docs/deploy-runbook.md`** | You chose to run deploys yourself (spec D4). It is the only thing blocking M2-7, and M2-7 is the only thing that can slip 10 Oct | 22 Sep 2026 |
| **Play one bot match at `?perf=1`** and say what the meter showed, or when it hitched | It is the only instrument that sees your 144 Hz vsync. Nothing outside the game can | 18 Sep 2026 |
| Edit `pillars.md` — it is a draft distilled from the design page, and the pillars are yours | | 17 Sep 2026 |
| Optional: should a room show the other seat's chosen preset before the match starts? | design `#q-online-room-loadout` | 17 Sep 2026 |
| Answer OQ-N03, N07, N11 on `docs/design/mechanics.xlsx` before anyone builds the board mechanics | jump/teleport crossing a beam; trap consumed on fire; lane and power for structure damage | 21 Sep 2026 |
| Commit the 21 Sep design files, or say who should | Still uncommitted: `docs/design/mechanics.xlsx`, the board-mechanics brief and research, the playtest, T-0005/T-0006, the prep plan. Today's deploy work **is** committed | 22 Sep 2026 |

## Decided on 22 Sep: deploy, and how (ADR-031)

| Decision | Chosen |
|---|---|
| Address | **`mimas.laststep.cloud`** on the existing Hostinger VPS, permanent. Own nginx file, own Let's Encrypt cert; the `laststep.cloud` site's file is never edited |
| Who deploys | **Rohan**, from `tools/deploy/deploy.sh` in Git Bash, with `docs/deploy-runbook.md` for the one-time VPS setup. **Agents only ever `--dry-run`** |
| Process on the VPS | systemd unit as a `mimas` user + `dotnet publish -r linux-x64 --self-contained`; nothing to install on the box |
| Transfer | `tar` over `ssh` (Git Bash has no rsync) |
| Gate | 25 MB hard fail in the script; the 13 MB ratchet stays |

**The VPS, surveyed read-only on 22 Sep:** Ubuntu 24.04, nginx 1.24 with no brotli module (irrelevant,
the files are pre-compressed), certbot with the nginx plugin and a renewal timer, Hostinger DNS and
**not Cloudflare**, sixteen Docker containers all on loopback, a `webhook` service on `*:9000`, no
firewall, **port 7777 free**, no .NET. Re-checked today: `nginx/1.24.0 (Ubuntu)`, and
`/etc/letsencrypt/live` holds only `laststep.cloud` — so `vps-setup.sh`'s certbot branch will run.

## Decided on 18 Sep

- **Engine: 6000.4.12f1 now, 6.7 LTS when it ships, 6.6 skipped entirely** (ADR-030). 6.6 is
  `lts=False`, 6.7 LTS is due Q4 2026, so 6.6 is a ~3-month stop and two upgrades instead of one.
  What 6.7 buys: production WebGPU — compute shaders, GPU skinning, **VFX Graph in a browser**,
  WebAssembly64. That lifts the constraint behind ADR-008 and golden rule 9, so "no VFX Graph" is
  worth revisiting **at 6.7, not before**.
  **When any upgrade happens, check `com.unity.pipeline 0.7.0-exp.1` first** — it is experimental and
  is the bridge every `unity command` goes through. If it does not resolve, the live-Editor path is
  gone and golden rule 2 has no way to be satisfied.
- **The custom Web template comes after deploy is green.** It replaces the 960×600 box, the Unity
  footer, the `Unity Web Player | MimasClient` title and the splash — all one file.
- **Resolution: nothing to cap.** Measured, then found there was nothing to cap.

## The jank, and what it is not

Measured in a live bot match on Rohan's own GPU (`tools/smoke/browser-perf.mjs`, `artifacts/perf-*`):
at **2560×1440**, his fullscreen, the arena renders in **under 4.17 ms against a 6.94 ms budget at
144 Hz** — about 4× headroom, 0 frames over 33 ms. Only at 5120×2880 (14.75 Mpx) does it degrade.
Capping would have cost sharpness and fixed nothing.

But every run measured an **idle** arena, so the cause is transient. `FrameProbe` (`?perf=1`) ships in
the build and logs every frame over 50 ms with the scene, the timestamp and **whether the collector
ran** — the bit that separates a GC hitch from a shader compiling. Its first run showed a **267 ms
frame entering the Arena with `gc=no`**, the shape of first-use shader compilation. That was under a
software rasteriser, so the number is inflated; the attribution is not. **Still open, still waiting on
one `?perf=1` reading from Rohan.**

## Design: board mechanics (21 Sep, concept level, no code)

Twenty decisions from a session on buildables, hidden traps, area of influence, a laser-sight prop and
start-of-turn guaranteed actions. **Fog of war is in** (a hero sees its *aura*; builds add vision),
three innate actions (move, build, destroy), structures as owned bodies, traps that are a marker in
enemy vision and invisible otherwise, a per-attack `needsVision` flag with blind shots, and guaranteed
actions in placement order. None of it implemented, none of it on the design page yet by Rohan's
choice.

| File | What |
|---|---|
| `docs/design/mechanics.xlsx` | **Source of truth for mechanic detail and numbers** from now on. 14 sheets |
| `docs/design/2026-09-21-board-mechanics.md` | The concepts in prose, what they change, the risks |
| `studio/decisions/RESEARCH-2026-09-21-board-mechanics.md` | Precedents with sources |

OQ-N03, N07 and N11 block a first slice. **Nothing in M2 depends on any of it**; it is M3-or-later.

## Things the next agent must not rediscover

- **The client opens no socket until the player presses something** (`LobbyView.cs` 126, 270). So
  `browser-smoke.mjs --expect "\[NetClient\] connected"` against a bare `/` can never pass. Use
  `?room=ZZZZ`, which auto-joins (`LobbyView.cs` 195) and drives connect → authenticate →
  `no_such_room` — a console *warning*, so the smoke stays green. The spec's own command was wrong and
  cost one wasted run.
- **Build through the script, not the profile.** `unity build MimasClient --target WebGL
  --execute-method Mimas.Client.Editor.WebBuild.Build --output-path Build/Web`. The `Web Release`
  **profile** carries its own PlayerSettings snapshot (`webGLExceptionSupport: 0`, and the template).
  Both belong in `WebBuild.cs`. `CLAUDE.md` said otherwise until today; it is fixed.
- **`unity build` in batch mode needs no running Editor.** `unity status` reporting
  `STATUS_NO_INSTANCES` is not a blocker for a build — only for `unity command`.
- **A Development Build does not link** on 6000.4.4f1: `wasm-ld: undefined symbol:
  unitytls_ssl_set_client_transport_id`. Release is unaffected. Do not "fix" it with
  `ERROR_ON_UNDEFINED_SYMBOLS=0` — that moves the failure to runtime.
- **Unity's issue tracker has a JSON API**: `https://issuetracker.unity.com/api/v1.0/issues/<id>`
  gives `firstAffectedVersion` and `fixedInVersion`. The web page is a SPA and tells you nothing.
- **`GUIUtility.systemCopyBuffer` does not reach the browser clipboard.** Use `WebClipboard`.
- **Headless Chromium is a software rasteriser.** Headed Chromium runs rAF at its own ceiling — 240 Hz
  here — not the display's. Neither reproduces a 144 Hz vsync. Frame verdicts come from `FrameProbe`.
- **"Booted" is not "playable".** `createUnityInstance` resolving leaves the splash on the canvas for
  seconds. Cost once: two wasted runs and one wrong conclusion.
- **The asset guard's false positives now number eight**, all read-only or harmless: `2>&1` and
  `2>/dev/null`; `>(` inside a C# generic; a protected path named in prose in a `git commit -m`; a
  `find … -not -path "*/Library/*"`; a `sed 's///'` over a protected file; the literal string
  `com.unity.…` in a comment (matches `**/*.unity`); a Python `open('studio/ledger.json')` for
  reading; and (22 Sep) **the session scratchpad**, which lives under Windows' per-user temp folder
  and so matches the Unity temp pattern — writing a helper script there from Bash is blocked.
  The general shape, which is the thing actually worth knowing: **the guard scans the whole command
  text**, so a protected path is blocked wherever it appears — in a `git commit` message body, in an
  `echo` label, in a grep pattern — even when the command writes nothing anywhere. Three of today's
  four hits were that. Restructure the command, or word the prose differently; use the Write/Edit
  tools for ordinary markdown. **Never** rephrase the part that actually touches files to slip past.
- **Rohan's shell is Windows PowerShell 5.1, not Git Bash.** It has **no `&&`** — a chained command
  fails at parse time with `The token '&&' is not a valid statement separator`, so *nothing* runs and
  it looks like the first command failed. Write one command per line in anything he will paste. What
  he can run: `bash`, `ssh`, `git`, `dotnet`, `node`, `unity`, `wsl` are all on his Windows PATH, so
  `bash tools/deploy/deploy.sh` works from PowerShell unchanged (verified). What he cannot: any
  `tar | ssh` pipeline, because PowerShell pipes objects, not bytes — that is Git Bash only.
- **The VPS is shared.** `sites-enabled/laststep.cloud` belongs to another product on the same box.
  Mimas has its own file, unit, user and two directories, and that is the whole footprint.
- **nginx 1.24 syntax.** `listen 443 ssl http2;` is right on the VPS; `http2 on;` needs 1.25.
- **No secrets in the repo.** The VPS is reached only through the ssh alias `hostinger`. The only
  IPv4 literals in `tools/deploy/` are two `127.0.0.1`. Do not add an address "for clarity".

## Last playtest

**21 Sep 2026, Rohan, browser vs bot** (`studio/playtests/2026-09-21-rohan.md`). Three notes: a Copy
code button is missing (**T-0005**); shots refused where the picture looks open, almost certainly the
placeholder prop box being narrower than the hex column the rules block (**T-0006**); and whether a
turn should end itself at 0 AP, parked as **OQ-N16**. No frame-meter reading was reported.

**Still unplayed by a human: two browsers against each other** — the whole of M2-1 — and, from today,
**anything at all over the internet**.

## The deadline

From `E:\Studios\Trinetra-Game-Studio\docs\PLAN.md` §10: if two browsers cannot play a full match
through the server by **Fri 2 Oct**, the 10 Oct playtest falls back to the local build and online
moves to 17 Oct. The game code is not what puts that at risk; deploy is — and deploy is now one DNS
record and two commands away.
