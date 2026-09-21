---
project: mimas
milestone: M2
updated: 2026-09-21
updated_by: builder (opus) — T-0008, finishing M2 in the browser
---

# Where Mimas stands

> **Rewritten, never appended.** One page, always current. History lives in `runs/` and in git.
> Dates in this file are wall-clock. Earlier entries stamped "22 Sep" were written on 21 Sep; the
> run report `R-2026-09-22-T-0007` explains the day-ahead label.

## Right now

**Mimas is on the internet: `https://mimas.laststep.cloud`** — deployed by Rohan on 21 Sep and
confirmed from outside the same day (three green browser smokes, every header as specified, `/ws`
upgrading through nginx, the unit up with zero restarts). **The live site is one deploy behind the
repo**: everything below is built, green and captured, and has not been shipped yet.

Since that deploy, T-0008 closed what a friend meets in the first five minutes:

- **A match no longer throws you out of the room.** After the result both seats are back in the room
  they started in — same code, Ready reset, gear editable — and pressing Ready again starts the next
  match (ADR-032, design `#online` rule 9). A seat whose socket is gone at the result is freed, so a
  friend who dropped can rejoin by code. **That is M2-8.**
- **The page is called Mimas**: full-window dark canvas, a thin loading bar with a percentage, its own
  favicon, no Unity splash, no Unity footer, no 960×600 box.
- **Copy code** beside Copy link; a refused shot tints the hex the rules blamed *and* the wall or
  pillar standing on it; props are hex prisms the size of the hex they block.
- **In production the server only accepts sockets from the real site** (ADR-033), configured in
  `appsettings.Production.json` — no unit change, no `--setup`.
- The browser smoke runs in **Chromium, WebKit and Firefox**.

**323 Core tests and 47 server tests**, the latter playing whole matches over real sockets — including
two matches in a row in one room. Both are ladder rungs, both required, both green on 21 Sep.
Ladder for T-0008: green, 4/4 (`studio/runs/.ladder/T-0008.json`).

## The one thing to do next

**Rohan: deploy, then play.**

```
bash tools/deploy/deploy.sh          # no --setup: nothing on the VPS changes
bash tools/deploy/deploy.sh --smoke
```

Then one bot match on the live site — resign, **Back to room**, Ready, a second match — and then
**send the link to one person who is not Rohan, on another network, and play them**. That single
evening produces the last three facts M2 needs: M2-1, the step-4 evidence for M2-7, and M2-8 in the
wild. Record it as `studio/playtests/<date>-<name>.md`.

Then: a short measuring session against the live site (`R-2026-09-21-T-0008` § "Left for next time"
lists the five commands), and a verifier on T-0008 — which closes T-0005 and T-0006 with it.

## Current milestone: M2 — online

Target: **Sat 10 Oct 2026**, friends playing over the internet. **18 days.**

| Done-when | State |
|---|---|
| M2-1 Two browsers play a full match | **one browser plays the server.** The second seat has three working ways in and has still never been driven by a human. That is the whole remaining game-side gap |
| M2-2 Two players who want to play each other end up in the same match | **done** — room code (OPT-0001, ADR-029) |
| M2-3 Guest auth with a resumable token | **done** (browser reload still untested by a human; see the note under "Things the next agent must not rediscover") |
| M2-4 Server-authoritative 30 s turn | **done**, seen firing live |
| M2-5 Reload within the 60 s grace resyncs | **done** server-side and in the Editor |
| M2-6 Resign and disconnect-forfeit | **done**, both paths seen |
| M2-7 Deployed on the VPS over HTTPS | **live since 21 Sep, confirmed from outside; pending verifier** and one playtest entry from someone who is not Rohan |
| M2-8 Rematch without leaving the room | **built 21 Sep (T-0008), pending verifier.** Nine server tests, the Editor sequence captured; not yet played on the live site |

**Nothing in the ledger is ticked.** `pass` belongs to a verifier, not the builder who wrote the code.

## In flight

| Task | What | Status | Who |
|---|---|---|---|
| **T-0008** | **Finish M2 in the browser** — rematch, the Mimas template, Copy code, the blocker shown, origin check, three-engine smoke | **verify.** Twelve commits, ladder green, run report `R-2026-09-21-T-0008`. Rohan deploys next | verifier needed |
| T-0007 | Deploy | verify — all three parts done, live confirmed 21 Sep | verifier needed |
| T-0002 | Execute the online slice | verify | verifier needed |
| T-0005 | Copy code button | **verify** — executed inside T-0008, clipboard read back in a real browser | closes with T-0008 |
| T-0006 | Refused shots show the blocker | **verify** — executed inside T-0008, two Core tests plus the picture | closes with T-0008 |
| T-0003 | The Web build, in a browser | **blocked** — waiting on Rohan's `?perf=1` reading since 18 Sep. (Its status said `running` with nobody on it, which made the asset guard treat it as the active task; corrected 21 Sep) | Rohan |

## Blocked

Nothing, except what waits on Rohan below.

## Deploy: what exists, what is proved, what is not

`docs/specs/2026-09-22-deploy.md` in three parts. **All three are done** (T-0007, run report
`R-2026-09-22-T-0007`, status `verify`). Redeploy is `bash tools/deploy/deploy.sh` from Git Bash;
`--rollback` swaps back to `.prev`. **The next deploy carries one new file** —
`server/Mimas.Server/appsettings.Production.json` — which turns on the origin allow-list. It lands in
the publish output by itself; no unit, nginx or `--setup` change.

| Artefact | What it does |
|---|---|
| `MIMAS_BIND=loopback` in `Program.cs` | the server binds `127.0.0.1` only, which is what makes an unfirewalled VPS safe |
| `Mimas:AllowedOrigins` (`appsettings.Production.json`) | in production, only `https://mimas.laststep.cloud` may open `/ws`; anything else, including a missing `Origin`, is 403 with a log line. Empty/unset = any, which is dev and every test (ADR-033) |
| `tools/deploy/nginx-mimas.conf` | the real host, its own certificate, Brotli headers, the `/ws` proxy. Its upgrade map writes `$mimas_connection_upgrade`, **not** the conventional name — two maps for one variable would take the other tenant's site down. It forwards `Origin` untouched, so the new check needs nothing here |
| `mimas-server.service` | systemd, user `mimas`, restart on failure, logs to journald, `ASPNETCORE_ENVIRONMENT=Production` (which is what loads the allow-list) |
| `vps-setup.sh` / `vps-release.sh` / `deploy.sh` | one-time setup; the `.next`/`.prev` swap; the one command. `--help --dry-run --skip-build --setup --rollback --smoke` |
| `browser-smoke.mjs` | `--browser chromium\|webkit\|firefox`, `--viewport WxH`, `--timing`, and `clipmatch:` for the clipboard |
| `docs/deploy-runbook.md` | what Rohan reads. §7 now covers "curl on /ws says 403" (curl sends no `Origin`) |

**Layout on the VPS:** everything Mimas owns lives under
`/home/mimas/servers/mimas.laststep.cloud/` (`web/`, `server/`, `deploy/`). `/home/mimas` is **755**,
not 750, because nginx serves these files off disk; the unit's `ProtectHome` is **`read-only`**, not
`true`, which would have hidden the binary from its own service.

**Proved on the VPS, 21 Sep:** `/health` content hash `ea75e2db…`; the four hashed `/Build/` files
come back `Content-Encoding: br` with the right inner type and `immutable`; `/` is `no-cache`; `/ws`
returns `101`; three green live smokes at `boot 3.7–9.5 s, connected 6.2–12.0 s, 12.3 MB`; the unit
active with `NRestarts=0`; the cert exists. **Not proved:** a real Safari, anyone other than Rohan
loading it, and everything in this session's changes *on the box* — none of it has been deployed yet.

## Numbers, 21 Sep (local build)

| Engine | Boot | Connected | Transferred |
|---|---|---|---|
| Chromium | 662 ms | 1155 ms | 12.3 MB |
| WebKit | 1121 ms | 1446 ms | 12.3 MB |
| Firefox | 1315 ms | 1584 ms | 12.3 MB |

The 18 Sep local baseline was `boot 0.6 s / connected 3.1 s` **with** the Unity splash. Boot is
unchanged; connected fell to 1.16 s. The live baseline (`7.8 s to connected, 12.3 MB`) is re-measured
after the next deploy — the splash removal should show in *connected*, not in *boot*. Build size
**12.31 MB** against the 13 MB ratchet.

## Waiting on Rohan

| What | Why | Since |
|---|---|---|
| **Deploy, then one match against a friend on another network**, recorded as a playtest file | It is M2-1, the last evidence M2-7 needs, and the first live run of everything T-0008 built | 21 Sep 2026 |
| **Play one bot match at `?perf=1`** and say what the meter showed, or when it hitched | It is the only instrument that sees your 144 Hz vsync. It also unblocks T-0003 | 18 Sep 2026 |
| Edit `pillars.md` — it is a draft distilled from the design page, and the pillars are yours | | 17 Sep 2026 |
| Optional: should a room show the other seat's chosen preset before the match starts? | design `#q-online-room-loadout` | 17 Sep 2026 |
| Optional: should a room hold your seat for a grace **between** matches, so a page reload after a result comes back to the room? | Today a reload frees the seat and you need the code again. It is a design question, not a bug | 21 Sep 2026 |
| Answer OQ-N03, N07, N11 on `docs/design/mechanics.xlsx` before anyone builds the board mechanics | jump/teleport crossing a beam; trap consumed on fire; lane and power for structure damage | 21 Sep 2026 |

## Decided on 21 Sep: the room outlives the match (ADR-032), origins in JSON (ADR-033)

| Decision | Chosen |
|---|---|
| Rematch | **Back to the same room.** Same code, both seats, Ready reset, gear editable; both pressing Ready is the rematch. No offer, no session score. A forfeited seat is freed, so the room stays joinable |
| The page | **Full-window canvas**, dark, titled `Mimas`, own favicon, percentage bar, no footer. Splash off; `productName` Mimas, `companyName` Trinetra, all four set by `WebBuild.cs` on every build |
| Origin check | **`Mimas:AllowedOrigins` in `appsettings.Production.json`.** Empty = any, so dev and tests are untouched; production names one origin. Rejected: an nginx-level check, which would hide the refusal from the log and from the tests |

## Decided on 22 Sep: deploy, and how (ADR-031)

| Decision | Chosen |
|---|---|
| Address | **`mimas.laststep.cloud`** on the existing Hostinger VPS, permanent. Own nginx file, own Let's Encrypt cert; the `laststep.cloud` site's file is never edited |
| Who deploys | **Rohan**, from `tools/deploy/deploy.sh` in Git Bash. **Agents only ever `--dry-run`** |
| Process | systemd unit as a `mimas` user + `dotnet publish -r linux-x64 --self-contained`; nothing to install on the box |
| Transfer | `tar` over `ssh` (Git Bash has no rsync) |
| Gate | 25 MB hard fail in the script; the 13 MB ratchet stays |

**The VPS, surveyed read-only:** Ubuntu 24.04, nginx 1.24 with no brotli module (irrelevant, the files
are pre-compressed), certbot with the nginx plugin and a renewal timer, Hostinger DNS and **not
Cloudflare**, sixteen Docker containers all on loopback, a `webhook` service on `*:9000`, no firewall,
port 7777 free, no .NET.

## Decided on 18 Sep

- **Engine: 6000.4.12f1 now, 6.7 LTS when it ships, 6.6 skipped entirely** (ADR-030). What 6.7 buys:
  production WebGPU — compute shaders, GPU skinning, **VFX Graph in a browser**, WebAssembly64. That
  lifts the constraint behind ADR-008 and golden rule 9, so "no VFX Graph" is worth revisiting **at
  6.7, not before**. **When any upgrade happens, check `com.unity.pipeline 0.7.0-exp.1` first** — it is
  experimental and is the bridge every `unity command` goes through.
- **Resolution: nothing to cap.** Measured, then found there was nothing to cap.

## The jank, and what it is not

At **2560×1440** the arena renders in **under 4.17 ms against a 6.94 ms budget at 144 Hz** — about 4×
headroom, 0 frames over 33 ms. Only at 5120×2880 does it degrade. Every run measured an **idle** arena,
so the cause is transient. `FrameProbe` (`?perf=1`) ships in the build and logs every frame over 50 ms
with the scene, the timestamp and **whether the collector ran**. Its first run showed a **267 ms frame
entering the Arena with `gc=no`**, the shape of first-use shader compilation — under a software
rasteriser, so the number is inflated and the attribution is not. **Still open, still waiting on one
`?perf=1` reading from Rohan.**

## Design: board mechanics (21 Sep, concept level, no code)

Twenty decisions from a session on buildables, hidden traps, area of influence, a laser-sight prop and
start-of-turn guaranteed actions. **Fog of war is in**, three innate actions (move, build, destroy),
structures as owned bodies, traps that are a marker in enemy vision and invisible otherwise, a
per-attack `needsVision` flag with blind shots, and guaranteed actions in placement order. None of it
implemented, none of it on the design page yet by Rohan's choice.

| File | What |
|---|---|
| `docs/design/mechanics.xlsx` | **Source of truth for mechanic detail and numbers** from now on. 14 sheets |
| `docs/design/2026-09-21-board-mechanics.md` | The concepts in prose, what they change, the risks |
| `studio/decisions/RESEARCH-2026-09-21-board-mechanics.md` | Precedents with sources |

OQ-N03, N07 and N11 block a first slice. **Nothing in M2 depends on any of it**; it is M3-or-later.

## Things the next agent must not rediscover

- **The asset guard's active task is "the single task file whose `status` is `running`"** (or
  `TRINETRA_TASK` in the environment). Two running tasks, or none, means *no* task is active and every
  protected path is refused — including the ones your own task declares. Before doing anything that
  touches a declared path, check that yours is the only `running` one.
- **`dotnet run --no-launch-profile` means `ASPNETCORE_ENVIRONMENT=Production`**, which now loads the
  origin allow-list — so the Editor's socket (no `Origin` header) is refused with a 403 and the lobby
  just says "Server unreachable · retrying". Use `dotnet run --project server/Mimas.Server`, whose
  launch profile sets `Development`.
- **`MIMAS_WEB_PATH` must be an absolute path.** `dotnet run --project X` runs with the *project*
  directory as its working directory, so `Build/Web` resolves under `server/Mimas.Server/` and the
  static-file branch stays silently inert. `CLAUDE.md` still says otherwise.
- **`unity build` and `unity test` refuse while an Editor has the project open.** Plan a session as:
  everything that needs the live Editor, then `unity close MimasClient`, then the batch work.
- **`unity command eval` prints every diagnostic**, ending with a spurious "Unreachable code detected";
  the real error is the **first** line, so never `tail -1` a failed eval. `FindFirstObjectByType` is
  obsolete-as-an-error there now — use `FindAnyObjectByType`. Private members are reachable by
  reflection; `MatchSession.Submit` is public.
- **`capture_game_view --save_path` cannot write under any folder called `Temp`** (the guard protects
  `**/Temp/**`). Save to `Assets/_Shots/…`, copy it out, then `delete_asset --asset … --confirm true`.
- **`SaveProjectSettings()` does not persist in batch mode.** `WebBuild` applies the identity values
  (product name, company, splash off, template) on every build and logs them, but the committed
  `ProjectSettings` file still records the old ones — as it still records the old exception-support
  value from T-0003. What ships is what the script sets.
- **The client opens no socket until the player presses something.** So a smoke against a bare `/` can
  never see `[NetClient] connected`. Use `?room=ZZZZ`, which auto-joins and drives connect →
  authenticate → `no_such_room` — a console *warning*, so the smoke stays green.
- **Build through the script, not the profile.** `unity build MimasClient --target WebGL
  --execute-method Mimas.Client.Editor.WebBuild.Build --output-path Build/Web`.
- **A Development Build does not link** on this Editor: `wasm-ld: undefined symbol:
  unitytls_ssl_set_client_transport_id`. Release is unaffected.
- **`GUIUtility.systemCopyBuffer` does not reach the browser clipboard.** Use `WebClipboard` — and the
  smoke can now read the real clipboard back (`clipmatch:`), which is how Copy code was proved.
- **Headless Chromium is a software rasteriser.** Frame verdicts come from `FrameProbe`, not from a
  browser. "Booted" is not "playable" either.
- **The asset guard scans the whole command text**, so a protected path is refused wherever it appears
  — in a commit message body, an `echo`, a `grep` pattern, even a `find … -not -path`. Ten known
  false positives now, all read-only. Restructure the command or use the Read/Grep tools; **never**
  rephrase the part that actually touches files.
- **Rohan's shell is Windows PowerShell 5.1.** No `&&` — a chained command fails at parse time, so
  *nothing* runs. One command per line in anything he will paste. `bash`, `ssh`, `git`, `dotnet`,
  `node`, `unity`, `wsl` are all on his Windows PATH. Any `tar | ssh` pipeline is Git Bash only.
- **The VPS is shared.** `sites-enabled/laststep.cloud` belongs to another product on the same box.
- **nginx 1.24 syntax.** `listen 443 ssl http2;` is right on the VPS; `http2 on;` needs 1.25.
- **No secrets in the repo.** The VPS is reached only through the ssh alias `hostinger`.

## Last playtest

**21 Sep 2026, Rohan, browser vs bot** (`studio/playtests/2026-09-21-rohan.md`). Three notes: a Copy
code button is missing (**T-0005 — done**); shots refused where the picture looks open (**T-0006 —
done**, and the two cases are now Core tests); and whether a turn should end itself at 0 AP, parked as
**OQ-N16**. No frame-meter reading was reported.

**Still unplayed by a human: two browsers against each other** — the whole of M2-1.

## The deadline

From `E:\Studios\Trinetra-Game-Studio\docs\PLAN.md` §10: if two browsers cannot play a full match
through the server by **Fri 2 Oct**, the 10 Oct playtest falls back to the local build and online moves
to 17 Oct. Nothing in the code puts that at risk any more. What is left is one deploy and one evening
with a second human.
