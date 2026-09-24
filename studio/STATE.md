---
project: mimas
milestone: M2
updated: 2026-09-24
updated_by: opus — the parallel verification round (nine verifiers + one capture builder; 4 pass, 7 fail — mostly small or paper; M2-4 and M2-6 ticked; the deploy rollback is broken; T-0010 fails on practice-mode code)
---

# Where Mimas stands

> **Rewritten, never appended.** One page, always current. History lives in `runs/` and in git.

## Right now

**Mimas is on the internet: `https://mimas.laststep.cloud`** — deployed by Rohan on 21 Sep and confirmed
from outside the same day. **The live site is still the repo as of `73973ee`** (the T-0008 work). Everything
below this line is built but **not deployed**, and it changes what a player sees, so the next deploy is a
real one.

**22 Sep: boons are in the game (T-0010, part 2 of F-boons), at `verify`.** Eleven commits on `main`
(`e4a70b1` … `c813a61`), touching Core, the server, the data, the client and the docs. What a player does
now that they could not yesterday:

- **Prays to a god in the room.** Three buttons under the preset — Greek, Norse, Hindu — each naming its
  line and the Blessing it starts you with. The pick is remembered, changing it un-readies you like
  changing your gear, and the other seat never sees it. Ready needs both.
- **Plays a best-of-3, not a match.** Round 1 on Open Field, round 2 on Arena, round 3 back on Open Field
  (the ladder wraps). `ROUND 2 · 1 – 0` sits above the turn owner all round; a round opens with its own
  card and ends with a banner that has no way out of it, because the series goes on.
- **Drafts a boon between rounds.** Two seconds after the round's result the board goes dark and three
  cards drop in — a Blessing, an Enchant and a Sigil — each with its god, what it does and the item of
  yours it lands on. A click selects, Confirm keeps it, 20 s keeps the first. A green dot says the
  opponent has picked. A reload mid-draft comes back into the draft with the seconds that are left.
- **Sees the boons.** Own ones in the examine panel by kind and god, with the lineage under the name; the
  enemy's as one `?` row each under `Unknown lineage`, turning into names as reveals happen, with a
  `Revealed: Agni's Crown / Hindu · Enchant` flyover and the god appearing on their nameplate.
- **Resigning concedes the series**, not the round; so does leaving and not coming back.
- **Play vs bot is the whole series**: the bot prays to a random god and drafts.
- **Nine more boons** (three per lineage, one of each kind) and a new spell, Indra's Storm.

**23 Sep: the examine plate is built (T-0011, F-examine-panel), at `verify`.** Click a hero with nothing
armed and a paper plate appears on the right, a layer over the rest of the HUD: the painting in the lineage's hues with the name in a
serif, health and action points with a bar and eggs, standing height, six stats as base then a green or red
net, boons oldest to latest, and the four items with their actions as named tiles — violet-edged with a
diamond when an Enchant changed one, with a triangle when a Sigil added one, dashed `?` when it is theirs
and unseen. Rest on anything for one hover panel to the left with the numbers, the sentence, the damage
line and what a boon changed. The enemy's plate is vermilion with grey `?` after the lane stats while a boon
of theirs is hidden. Any click outside closes it: on the board it only closes; on the HUD it closes and
the action still happens (arm Move with the plate open and the board is ready to move). Escape and ✕ too;
it stays open across turns. Rohan played it on 23 Sep and asked for that, no slide, and bigger type — done
the same day. It is the first screen in the new interface language: `Theme.uss` declares every `--mimas-*` token,
three fonts ship as Latin-subset TextCore assets, glyphs are drawn with `Painter2D`. Run report:
`studio/runs/R-2026-09-23-T-0011.md`; screenshots in `artifacts/t0011/shots/`.

**23 Sep, later: the camera is the player's (T-0012, F-camera), at `verify`.** Rohan asked for it and
answered four questions the same day. Hold Q/E to turn round the arena (free, 90°/s), W A S D to slide
across it (stops at the outermost tiles plus a margin in tiles), the wheel to zoom (10–32), Space to glide
home to the default view centred on the arena, V to flip between **side-on** — your end always on the left,
so online seat 2 now sees itself left too — and **behind you** (past your end, looking across, 55° / 25).
Every value is on `ArenaCameraRig` on `Cameras/vcam_Tilted`; speeds, limits and smoothing are live in Play Mode, but
a view's angle and distance and the default view (an enum there) only show at the next Space or V. Tab and the top-down camera are untouched. ADR-038, spec `docs/specs/2026-09-23-camera.md`, run
report `studio/runs/R-2026-09-23-T-0012.md`, captures in `artifacts/t0012/`.

**23 Sep, evening: the rest of the HUD is designed (F-hud-restyle); T-0013 approved, T-0014 at `plan`.** Rohan chose
"restyle the rest of the HUD" for the session and decided it over three rounds on the canvas
(https://claude.ai/artifact/K9qZcJQ115G5G685sdMAwy, page "HUD", newest at the top): **layout A as drawn** (his
Draft 1 in ink — AP and three captioned lane rows bottom left, boons down the right edge, Resign above End
Turn), a **turn track** (your end left, pips, a marker, the active half burning to the centre), **cost dots**,
a **closed-eye mark on your own actions the opponent has not seen**, the **attack preview as the hover panel
over the target**, **ink draft cards**, the **band** for round moments, an **ink void**, the **room as two
seats facing** with **gear tiles**, and — his own point — **the examine plate goes ink** so nothing is left on
paper (the serif leaves the build). The design is the UI book (`docs/ui/hud.md`, `between-rounds.md`,
`lobby.md`, amended `language.md` and `examine.md`); the work order is `docs/specs/2026-09-23-hud-restyle.md`;
ADR-039 (the owner's view carries what the opponent has seen of it — the one wire field) and ADR-040 (one
surface; the bar and the plate share the tile model). **T-0013** (match HUD, between rounds, ink plate, the
wire field) can land before the 10 Oct playtest; **T-0014** (lobby and room) follows it. Rohan **approved T-0013** the same
evening; T-0014 waits until he has seen T-0013 built. **M3-5 (three maps vs two) is decided after the M2 playtest** (Rohan, 23 Sep).

**24 Sep: the match HUD is in ink (T-0013, F-hud-restyle), at `verify`.** Fourteen commits on `main`
(`d309c8f` … `fe866ef`). A thin turn track across the top (names, round pips, the active half burning to the centre
in the last seconds); AP and three captioned lane rows bottom left with cost dots, violet diamond/triangle for a
boon's change, and a **closed eye on every action the opponent has not seen you use** (ADR-039's one wire field —
it stays gone across the series); boons down the right; the attack preview as the hover panel over the target
(reason first when refused, one `?` row); ink draft cards; round moments in a band; the examine plate in ink; the
serif gone; the Arena on black. Captures `artifacts/t0013/shots/` (Editor, both sizes) and
`artifacts/t0013/browser-hud/` (the Web build). Run report `studio/runs/R-2026-09-23-T-0013.md`.

**24 Sep, later: the lobby and the room are in ink (T-0014, F-hud-restyle), at `verify`.** Rohan said "lets execute
t-0014" on 24 Sep; three commits (`7932513`, `eacc750`, `90e4475`) plus the studio one. The lobby is one ink column
over three faint washes in the gods' colours, with the last result and the series score ("VICTORY VS GUEST-4417 ·
SERIES 1 – 0"). The room puts **you on the left whichever seat you hold**, with your name, **one gear tile per
preset** (no dropdown) and the three gods as rows with a coloured square and emblem; **them on the right** with only
a name and READY / CHOOSING (RANDOM BOT · READY for the bot, WAITING FOR A PLAYER when empty). Ready locks the tiles
and the gods. Nothing about how a room works changed. Two things found on the way: **translucent colours draw ~60%
brighter than the canvas** (the project is linear; every screen, the HUD too — a follow-up below), and **the kerning
fix of T-0013 did not survive glyphs drawn after it** ("V I C TORY" came back; the lobby's own captions were never
fixed) — fixed in `FontSpacing`. Run report `studio/runs/R-2026-09-24-T-0014.md`; captures `artifacts/t0014/shots/`
(items 1–5, both sizes) and `artifacts/t0014/seat2-*/` (seat 2 in the browser).

**24 Sep, afternoon: the parallel verification round.** Eight fresh verifiers at once, one per task at `verify`; the
round report is **`studio/runs/R-2026-09-24-verify-round.md`** (verdicts, numbers, every issue by owner). **PASS:**
T-0013 (the HUD), T-0012 (the camera), T-0002 (the online slice — **M2-4 and M2-6 ticked**), T-0005 (copy code).
**FAIL:** T-0007 — **the deploy rollback deletes the build it should restore** (never run; do not run it until
fixed); T-0008 — a stale room after a lost match when the socket was down; T-0014 — the lobby's last-result line drops
"resigned" when the resigner was behind; T-0006 — the second looks-open sight case; T-0011 and T-0001 — paper only,
**closed by Rohan the same day** ("pass"; both now `done`). **T-0010** got its captures from a builder beside the round and a ninth verifier
re-checked it: four of the five missing things are now seen, but the captures exposed **six practice-mode defects in
T-0010's own code** — practice rounds 2 and 3 open with no round band, and a fresh practice series plays round 1 on an
empty board (no units, no action bar). No hidden-information leak, no weakened test, the ledger touched only by the
flips.

**473 Core tests, 60 server tests, 40 EditMode tests** (T-0014). The Web build is **12.51 MiB** (13 117 682 B after T-0014; 13 120 694 B after T-0013; was
12.57) against the 13 MB ratchet — the serif atlases left. T-0010's run report: `studio/runs/R-2026-09-22-T-0010-build.md`.

## The one thing to do next

**Next agent sessions: two light builder tasks for the round's fixes, the rollback first.** The lists, with files and
lines, are "A builder, small" in **`studio/runs/R-2026-09-24-verify-round.md`**.
- **Online path and paper** (items 1–5, 7): (1) the deploy rollback (`tools/deploy/vps-release.sh` `swap_in` deletes
  `.prev` before swapping it in — it guards the next deploy, so it goes first), (2) the stale room after a lost match
  with the socket down (T-0008), (3) `room.leave` mid-match skips the forfeit (found verifying T-0002), (4) the lobby's
  last-result reason (T-0014), (5) the second looks-open sight case (T-0006), (7) paper. No Editor needed except for
  T-0014's re-capture.
- **Practice-mode presentation** (item 6, needs the Editor): T-0010's six — the round band and round-start reveals
  dropped in practice rounds 2 and 3, the empty board on a fresh practice series, flyovers at screen centre, a boon
  named by id in the hit flyover, a tile marked changed when its number is not, two reveal flyovers on top of each
  other — plus T-0011's unfloored `AddChanges` and T-0013's hover panel outliving the bar and its one-glyph change rows.

Then fresh verifiers re-check T-0007, T-0008, T-0014, T-0006 and T-0010. No task files exist yet — Rohan says go, or the
producer writes them.

1. **Rohan: what is left of the round's questions** (T-0011 and T-0001 he closed on 24 Sep): accept M1-1…M1-3 having
   been seeded `passes: true` by a builder; reword M2-2 (room codes are decided); say whether a builder may widen its
   own `allows_assets` after a guard refusal.
2. **Do not run `deploy.sh --rollback`** until the fix lands. A plain deploy is fine.
3. **The M2 playtest, which only Rohan can do:** send the link to one person who is not Rohan, on another
   network, and play them — now a best-of-3 with a draft. That one evening is M2-1, the last evidence for
   M2-7, and M2-8 with a human on the other seat. Record it as `studio/playtests/<date>-<name>.md`.
   **Deploy first** (`bash tools/deploy/deploy.sh`): the live site is from before boons, and its client
   cannot talk to a server that wants a lineage. T-0013 changes the wire too (ADR-039), so it all goes together.
4. **T-0010** waits on the practice-mode task above; its captures (24 Sep) are in `artifacts/t0010/`, the table in
   T-0010's build report just above its Verifier section. **Ledger, Rohan's call before it can pass:** M3-5 says three
   ladder maps and two ship (decide after the playtest: reword or build a ladder-position-2 map), and M3-6 is one row
   for "character select and draft screens", so its draft half cannot be ticked alone (split it or wait).
5. **Rohan plays the camera and tunes it** (T-0012 passed; a view's angle/distance shows at the next Space or V), and
   **looks at T-0013 and T-0014 built** (a bot match from the lobby, or the Arena → Play → `Mimas/HUD/Preview round 3
   (…)`). A task that passed stays at `verify` until he has.

## Current milestone: M2 — online

Target: **Sat 10 Oct 2026**, friends playing over the internet. **18 days.**

| Done-when | State |
|---|---|
| M2-1 Two browsers play a full match | **one browser plays the server.** The second seat has never been driven by a human. That is the whole remaining game-side gap |
| M2-2 Two players who want to play each other end up in the same match | **done** — room code (OPT-0001, ADR-029) |
| M2-3 Guest auth with a resumable token | **done** (browser reload still untested by a human) |
| M2-4 Server-authoritative 30 s turn | **ticked 24 Sep** (T-0002's verifier) |
| M2-5 Reload within the 60 s grace resyncs | **built and tested server-side**; since 22 Sep a reload lands back in a draft too. No capture exists of a real client coming back into a running match (T-0002's verifier) — the playtest can show it |
| M2-6 Resign and disconnect-forfeit | **ticked 24 Sep** (T-0002's verifier, on server and Core tests); since 22 Sep both end the series. A crafted `room.leave` mid-match skips both — a builder fix |
| M2-7 Deployed on the VPS over HTTPS | **live since 21 Sep, confirmed from outside, and re-checked read-only 24 Sep** — but T-0007 **failed** on its rollback, and the row also needs one playtest entry from someone who is not Rohan. **The live build is behind `main`** |
| M2-8 Rematch without leaving the room | **built and played live 21 Sep; T-0008 failed 24 Sep** on a stale-room regression (the rematch itself holds). A rematch is now a new *series* |

**Ticked in M2: M2-4 and M2-6 only**, by a verifier. `pass` belongs to a verifier, not the builder who wrote the code.
M3's boons rows — **M3-3, M3-4, M3-5 and the draft half of M3-6** — are now built and are the verifier's to
tick.

## In flight

| Task | What | Status | Who |
|---|---|---|---|
Verdicts of 24 Sep are in `R-2026-09-24-verify-round.md`; each task's reasons are in its own run report's Verifier section.

| Task | What | Status | Who |
|---|---|---|---|
| **T-0010** | **Boons in the game** — the room hosts the session, lineage row, draft over the board, presentation of boons and reveals, practice-mode session, nine boons | **verify — FAIL again 24 Sep, now on code**: captures taken; four of five items seen; six practice-mode defects from its own commits | builder, small (practice-mode task) |
| T-0009 | Boons groundwork in Core | **verified PASS 23 Sep** (ledger rows are ticked with T-0010, per F-boons) | done pending T-0010 |
| **T-0013** | **The match HUD in ink** (one wire field, ADR-039) | **verify — PASS 24 Sep** | Rohan looks |
| **T-0012** | **The camera** | **verify — PASS 24 Sep** | Rohan plays and tunes |
| T-0002 | The online slice | **verify — PASS 24 Sep**; M2-4, M2-6 ticked | Rohan's step 4 is the playtest |
| T-0005 | Copy code | **verify — PASS 24 Sep** (inside T-0008) | — |
| **T-0007** | Deploy | **verify — FAIL 24 Sep: the rollback deletes the build it restores.** The live site itself checks out | builder, small — first |
| **T-0008** | Finish M2 in the browser | **verify — FAIL 24 Sep**: stale room after a lost match with the socket down. Its ladder at its own commit is green (323 / 47) | builder, small |
| **T-0014** | The lobby and the room in ink | **verify — FAIL 24 Sep**, one item: the last-result line drops "resigned" when the loser resigned while behind | builder, small |
| T-0006 | Refused shots show the blocker | **verify — FAIL on evidence 24 Sep**: a second case that looks open; a rule-or-picture line per case | builder, small |
| T-0011 | The examine plate | **done 24 Sep** — Rohan: "pass"; done-when #5 amended to his 23 Sep rework (the verifier's only item) | — |
| T-0001 | Install the studio | **done 24 Sep** — Rohan: "pass and ignore, probably redundant now" | — |
| T-0003 | The Web build, in a browser | **blocked** — waiting on Rohan's `?perf=1` reading since 18 Sep | Rohan |

## Blocked

Nothing, except what waits on Rohan below.

## Waiting on Rohan

| What | Why | Since |
|---|---|---|
| **Look at the lobby and the room** (T-0014) and the HUD (T-0013) built; then say whether translucent fills should match the canvas exactly | Every `you at N%` fill draws ~60% brighter than the canvas (linear colour space); matching it is a change to every screen | 24 Sep 2026 |
| **The verification round's remaining questions** — M1-1…3 seeded by a builder; M2-2's wording; M3-5 and M3-6's wording; may a builder widen its own `allows_assets` | Ledger and process calls only you can make (T-0011 and T-0001 closed 24 Sep). Details: `R-2026-09-24-verify-round.md` "Rohan" | 24 Sep 2026 |
| **Do not run `deploy.sh --rollback`** | It deletes the previous build and reports success (T-0007). A builder fix is first in the queue | 24 Sep 2026 |
| **Deploy** — a fresh Web build from `f3c205d` is in `Build/Web` and smoke-tested on three engines, so `bash tools/deploy/deploy.sh --skip-build` (PowerShell: `& 'C:\Program Files\Git\bin\bash.exe' tools/deploy/deploy.sh --skip-build`) ships it with a matching server | The live site predates boons. Its client cannot start a match against a post-boons server, because `room.loadout` now needs a lineage; an old tab must reload. `/health`'s content hash changes | 22 Sep 2026 |
| **One best-of-3 against a friend on another network**, recorded as a playtest file | It is M2-1, the last evidence M2-7 needs, and M2-8 with a human opponent. It is also the only thing that will say whether the draft reads in three seconds | 21 Sep 2026 |
| **Tune the boon numbers** — `MimasClient/Assets/_Game/Data/boons/*.json` (27 of them now), `abilities/`, `modifiers/` | Every number is a placeholder written as the spec gave it. Nothing enforces them; the tests only check shapes and rules | 22 Sep 2026 |
| **Play one bot match at `?perf=1`** and say what the meter showed | The only instrument that sees your 144 Hz vsync; unblocks T-0003 | 18 Sep 2026 |
| Edit `pillars.md` — it is a draft distilled from the design page, and the pillars are yours | | 17 Sep 2026 |
| Optional: should a room show the other seat's chosen preset before the match starts? | design `#q-online-room-loadout` | 17 Sep 2026 |
| Optional: should a room hold your seat for a grace **between** matches? | Today a reload frees the seat. A design question, not a bug | 21 Sep 2026 |
| Answer OQ-N03, N07, N11 on `docs/design/mechanics.xlsx` before anyone builds the board mechanics | jump/teleport crossing a beam; trap consumed on fire; lane and power for structure damage | 21 Sep 2026 |

## Decided on 22 Sep: how boons reach the player (P1–P8, ADR-036)

| Decision | Chosen |
|---|---|
| P1 Where the draft happens | **Over the dimmed board, in the Arena scene**; the next round reloads the Arena for its map |
| P2 Where the series score lives | **In the turn panel**: `ROUND 2 · 0 – 1` above the turn owner |
| P3 Wire shape | **Existing channels only**: session events inside `events`, a `session` block beside `view`, `draftPick` through `match.command`, one `match.start` per round; `series` and `round` are two fields |
| P4 Reload during the draft | The draft clock keeps running; a resume shows the draft; the 60 s grace runs across it and forfeits the **session** |
| P5 Play vs bot | The full best-of-3, the bot drafting offer 0 |
| P6 The bot's lineage | Seeded random from the three (`BotLineageId` to fix it); the two dev passives retired |
| P7 Editor practice mode | Runs a `Session` too, inside a persistent `LocalSessionHost` |
| P8 What Resign concedes | **The series**; a disconnect forfeit too |

## Follow-ups from T-0011 (not built, by the spec's scope)

- ~~**The action bar in the new language** … then the draft cards, the round banner and the lobby.~~
  **Designed 23 Sep evening → T-0013 / T-0014** (above).
- **Art slots** the plate leaves as placeholders: portrait per hero, lineage emblems (today the mock's
  laurel/hammer/lotus strokes), item squares (today a lineage-hued gradient), action icons (today a letter;
  each tile carries its icon key as an `icon--<key>` class for a sprite to bind to).
- **`RevealedAt` on the wire?** The enemy's boons are listed in the order this client saw them revealed;
  after a reload they fall back to grant order (`docs/ui/examine.md` open question 4). A design question.
- ~~The action bar sits ~220px right of centre~~ — **fixed in `0e8c95c`** (centred in pixels from its measured
  width); T-0013 left-anchors the bar anyway.
- **Translucent tokens draw brighter than the canvas** (found by T-0014, 24 Sep): the project is linear
  (`m_ActiveColorSpace: 1`), so UI Toolkit blends `you` at 14% over ink to rgb(42, 90, 88) where the canvas has
  (31, 56, 60). Every screen, the HUD included. This Unity has no `PanelSettings.forceGammaRendering`. Options: tokens as
  pre-mixed opaque colours where the ground is known, or alphas re-derived for linear blending. Rohan's call (it changes
  every screen); the lobby's wash already does the first (`Ramps.LobbyWash` mixes onto the ink).
- **The gear tiles all show the sword and the boot** (the book's slot glyphs); per-item icons wait for art.
- **The banner colours every line that is not "VICTORY" in the opponent's colour** (`MatchHudView` `banner--lost`),
  the round card included. T-0013's `HudMoment` replaces it.

## Things the next agent must not rediscover

- **To run a task's ladder at its own commit** (a verifier wants the task's code, not `main`'s): `git worktree add
  --detach "E:/Unity Projects/MimasGame-verify-<sha>" <sha>`, then `ladder.mjs --root "<that path>" --task T-NNNN`, then
  `git worktree remove` it. Own `bin/`/`obj/`, so it can run beside anything. **Not in the session scratchpad**: its
  path runs through `AppData\Local\Temp`, which the asset guard's `**/Temp/**` refuses in any shell command.
- **A parallel verification round works** (24 Sep, eight verifiers at once, ~15 min wall clock): the recipe is
  `studio/plans/2026-09-24-parallel-verification.md`. Tell every verifier never to `git checkout`/`stash`/`reset` (one
  shared tree) and to read old versions with `git show <rev>:<path>`. Append to a log with the Edit tool, not `cat >>`:
  a log line that names a protected path in prose gets the whole append refused.

- **Camera keys can be driven for real from `eval`**: `InputSystem.QueueStateEvent(Keyboard.current, new
  KeyboardState(Key.V))`, then an empty `KeyboardState()` in the next eval to release; the same with a
  `MouseState` carrying `scroll` for the wheel. The rig's own polling sees them. The MCP `capture_game_view`
  refuses a `..` path, so it cannot write into `artifacts/`; use `ScreenCapture.CaptureScreenshot` from eval.
- **The Editor never puts a real pointer on UI Toolkit, so some HUD states exist only in the browser.** A 1280×720
  layout loop (the hover panel's height rounding by one device pixel with where its top lands; fixed `2f36241`)
  showed only with the pointer resting on an armed tile, which Editor captures cannot do. Check the HUD in the Web
  build: serve it (`MIMAS_WEB_PATH=<abs>/Build/Web dotnet run --project server/Mimas.Server`), then
  `browser-smoke.mjs --viewport 1280x720 --expect "\[MatchSession\] online" --do "wait:3000,click:640,357,wait:3000,click:640,605,wait:11000,click:239,637,…"`
  (Play vs bot, Ready, the first weapon tile). **The lobby moved with T-0014:** at 1280×720 Play vs bot is 640,357 and
  Ready 640,605; at 1920×1080 960,535 and 960,908 (both measured); the HUD's positions are unchanged (×1.5 at 1080). `move:x,y` rests the pointer without pressing (the preview over a target). Count
  `Layout update is struggling` in the output — the smoke fails on it as a console.error anyway.
- **A second player without a second browser:** `node artifacts/t0014/seat.mjs create|join <CODE> [ready,resign]
  [name]` is a guest on the real protocol (auth, room, loadout, a resign 12 s into the series); Node 22's built-in
  WebSocket, nothing to install. It is how T-0014 took every room state in the Editor.
- **UI Toolkit buttons can be pressed from `eval`** by sending `NavigationSubmitEvent.GetPooled()` to the button
  (`artifacts/t0014/click.cs`); `eval` wants `FindAnyObjectByType` (FindFirst is obsolete and fails the compile) and
  `UQueryExtensions.Q<T>(root, name)` spelled out (the extension method is not found there).
- **Open a scene from `eval` through `EditorBuildSettings.scenes[i].path`**: the asset guard refuses any command text
  holding the scene's file extension, a read-only open included. The Editor starts on an untitled scene after `unity open`.
- **A dynamic font brings a glyph's kerning pairs back flagged when the glyph is first drawn**, so clearing the flag
  once is not enough: `FontSpacing` adds every tracked character before clearing, and both the lobby and the HUD call it
  at bind. Any new screen with tracked text calls `FontSpacing.RepairLoaded()` when it binds.
- **Batch `unity test` leaves the font caches alone; every batch Web build rewrites them.** `git restore
  MimasClient/Assets/_Game/UI/Fonts` after each build.
- **Never call `unity command run_tests` while the Editor is in Play Mode.** The job queues behind Play
  Mode and every later CLI command times out behind it (`editor_stop` and `unity close` included); it took
  Rohan closing the Editor by hand on 23 Sep. **It wedged a second time outside Play Mode** (23 Sep, late, a
  second `run_tests` right after `editor_stop`): prefer batch `unity test` with the Editor closed for EditMode
  (34 tests, under a minute).
- **Play Mode and the batch Web build both write the dynamic font atlases into the font assets** under `UI/Fonts/` (5 KB → up to
  730 KB each). It is a cache (`ClearDynamicDataOnBuild` is on): with the Editor **closed**,
  `git restore MimasClient/Assets/_Game/UI/Fonts` before committing. Never commit the populated ones.
- **Practice from the CLI:** `editor_play` on the Lobby, `SceneManager.LoadScene("Arena")` via `eval`, then
  load it **once more** — the first Arena load from a playing Lobby comes up with no units (the session is
  ready and the view has two units; no views were spawned). `artifacts/t0011/start.sh` does all of it.
  **This is a product defect, not a CLI quirk** (T-0010's re-verification, 24 Sep): a fresh practice series plays
  round 1 on an empty board for a player too — `MatchSession.cs:642` spawns units only once a view exists, and the
  view exists only after `Begin()`. It is on the practice-mode fix list; rewrite this note when it lands.
- **The examine plate's acceptance states** come from `Mimas/Examine/Preview round 3 (your hero | the
  enemy)` in Play Mode (time stops; `End preview` restarts it). Captures with
  `ScreenCapture.CaptureScreenshot` straight into `artifacts/` are native size and leave no `.meta`.
- **A UI Toolkit hover or press can be driven** with `PointerEnterEvent.GetPooled()` / `PointerDownEvent`
  sent to the element: the Input System's simulated mouse does not reach UI Toolkit in an unfocused Editor.
- **A board click is read twice**: by `BoardInputController` in its `Update` and by UI Toolkit later the
  same frame (a frame later on the Web). Anything that reacts to presses and appears because of a board
  click (the examine plate's close-on-outside-press) must ignore the press that opened it.
- **Keep a task `running` for any rework after it reached `verify`**: its `allows_assets` only apply while it
  is the one running task. Never write a protected path from a script file — the guard only reads the
  command text, so it cannot stop it, and the next commit is refused anyway.
- **Closing examine must clear the model, not only the ids.** Arming an action once cleared the ids and
  left `_examine` drawn: the plate stayed up, ate the board, and `CloseExamine` refused because the ids
  were already empty — the player was stuck (found by Rohan, 23 Sep).
- **The browser smoke's `click` now holds the button 80 ms**: an instant click never reads as
  `wasPressedThisFrame`, so board clicks silently did nothing.
- **The asset guard reads shell variables literally**: `git add $C/…meta` is refused even when the path is
  declared. Write the paths out.

- **The design canvas refuses a publish after anyone saved from the page** ("conflict … saved from inside the
  page"), even when you re-read `project/canvas.json` with a `path`: read the artifact's URL itself
  (`action: "read"`, no path), then publish. Re-read the index into `artifacts/UI Drafts/hud-mock/live/` right
  before each publish (Rohan moves frames by hand; the editor caps a title note's `maxW` at 8000). The HUD
  rounds' generators are `artifacts/UI Drafts/hud-mock/gen9.py`–`gen11.py` (1920×1080 boards, names and costs
  read from the shipped JSON) with `gen9_index.py`–`gen11_index.py` for the index.
- **USS has no gradients.** Every fade (the plate's wash, the painting, and after T-0013 the floors, the band,
  the draft card wash) is a generated white-alpha texture tinted by a `--mimas-*` token (`UI/Ramps.cs`).
- **The Arena's grey void is Unity's default procedural skybox** (`Main Camera` clears to Skybox); the Lobby's
  background is its USS. T-0013 sets the Arena camera to solid ink.
- **The UI book is `docs/ui/`.** One page per screen (`examine.md` first) with element ids that become UXML
  names, data sources, asset gaps and acceptance screenshots; `language.md` holds every token and maps it to a
  `--mimas-*` USS variable. Design a screen there before touching UXML. The mock canvas and its Python
  generators are under `artifacts/UI Drafts/` (gitignored); the pinned render is under `docs/ui/mockups/`.
- **The asset guard scans shell command text for protected paths, even in prose and even for reads**: the
  design page, anything under `.claude/`, the studio config and ledger files, and any path containing `Temp`.
  Use the Read/Grep/Edit tools for those files and keep their names out of shell commands.

- **`Room.Session` is one whole best-of-3.** `Room.Round` is gone; it is `Room.Series` (rematches) and the
  session owns the round. The map comes from the session's ladder, so `ServerOptions.MapId` is gone too:
  **round 1 online is `board-3`, not `arena-4`.**
- **Between rounds there is no view.** `IMatchDriver.Rules` and `.View` are null in a draft and after the
  series, and `Session` is what is never null. Every presenter path that touches them must null-check —
  `CanAct` did not, and the Arena threw on its first frame.
- **A round's final view is never sent.** `Session.Match` is null the moment a round ends, so the last view
  a seat gets is the one before the killing blow; the death and the result arrive as events. Assert on
  `sessionEnded` and the session block, never on `view.IsOver`.
- **A resign ends the series** (P8). Any Core test that used `session.Apply(new ResignCommand(…))` to reach
  a draft now ends the whole thing: `SessionTests.EliminateRound` is the way to end a round instead.
- **`Mimas.Core.Session.Session` shadows its own namespace** from outside it: alias it
  (`using Session = Mimas.Core.Session.Session;` after the namespace line).
- **The asset guard's active task is "the single task file whose `status` is `running`"** (or
  `TRINETRA_TASK`). Two running tasks, or none, means no task is active and every protected path is refused.
- **The guard scans the whole command text, prose included.** A commit message that names a `.asset` or a
  `.meta` file is refused; so is a read-only `find … -not -path "./Library/*"`, and so is a
  `unity command open_scene --path ".../Lobby.unity"` even when that scene is in `allows_assets`. Reword the
  prose, use the Read/Grep/Glob tools, or drive the Editor through its MCP tools — never rephrase a command
  that writes.
- **A new folder under `Assets/` needs its folder `.meta` declared too** (`Assets/_Shots.meta` sits beside
  `Assets/_Shots/`, not inside it).
- **The Unity MCP `eval` round trip costs seconds.** A 20 s draft window cannot be hit with "eval, check,
  capture": schedule the screenshot from inside the Editor (an `EditorApplication.update` closure calling
  `ScreenCapture.CaptureScreenshot`) in the same eval that ends the round.
- **The ladder runner wants rung numbers in the task's `ladder:`** (`[0, 1, 2, 5]`), not command strings.
  On `main` rung 0 checks only the working tree, so run the ladder before the final commit.
- **`dotnet run --no-launch-profile` means `ASPNETCORE_ENVIRONMENT=Production`**, which loads the origin
  allow-list; use `dotnet run --project server/Mimas.Server`, whose launch profile sets `Development`.
- **`MIMAS_WEB_PATH` must be an absolute path.**
- **`unity build` and `unity test` refuse while an Editor has the project open.** `unity command menu
  --path "Assets/Refresh"` then `recompile_status` is how a live Editor picks up new Core files and data;
  it regenerates `GameDataManifest` by itself and logs `[GameDataManifest] Rebuilt with N files`.
  `unity command menu --path "File/Exit"` closes it (it reports a transport error and exits anyway);
  `unity open MimasClient` opens it again, ready in about a minute.
- **`unity command console` prints every entry** — pipe it through `grep -o '"groundTruth":{[^}]*}'` for
  the error count instead of reading the dump.
- **`capture_game_view --save_path` cannot write under any folder called `Temp`.** Save to `Assets/_Shots/…`.
- **The client opens no socket until the player presses something.** A smoke uses `?room=ZZZZ`.
- **Build through the script, not the profile.** A Development Build does not link on this Editor.
- **`GUIUtility.systemCopyBuffer` does not reach the browser clipboard.** Use `WebClipboard`.
- **Headless Chromium is a software rasteriser.** Frame verdicts come from `FrameProbe`.
- **Rohan's shell is Windows PowerShell 5.1.** No `&&`; one command per line in anything he will paste.
- **The VPS is shared**; `sites-enabled/laststep.cloud` belongs to another product. nginx 1.24 syntax.
- **No secrets in the repo.** The VPS is reached only through the ssh alias `hostinger`.

## Numbers, 24 Sep

**The build ready to deploy** (24 Sep, after the round, for Rohan's deploy): `Build/Web` from `f3c205d`, batch through
`WebBuild.Build`, **13,130,142 B (12.52 MiB)** — +12 KB on T-0014's with no client change between (not investigated).
Local server + that build, 1280×720, Play vs bot into a live match: Chromium `boot 721 ms`, WebKit `2285`, Firefox
`1894`, 12.5 MB each, all OK — no page error, no console.error; client and server content hash `647914b4…`. Logs and
shots `artifacts/verify-round/smoke-*`. Ladder at `f3c205d` GREEN 4/4 (473 / 60).

T-0014: Web build **13,117,682 B** (12.51 MiB; −3 KB); smoke on it, local server, 1280×720, Play vs bot to a match:
Chromium `boot 1329 ms`, WebKit `1248`, Firefox `1638`, 12.5 MB each. The Chromium boot is up from T-0013's 672 ms; the
lobby now rasterises ~60 capitals per font at bind (`FontSpacing`) — not measured apart. EditMode 40.

Web build **12.51 MiB** (13,120,694 B, `du -sb Build/Web`; the T-0011 build measured the same way was 13,175,763 B)
against the 13 MB ratchet — the three serif atlases gone (`.data.br` −73 KB), the new HUD code in (`.wasm.br`
+16 KB). Smoke on the T-0013 build, local server: Chromium `boot 672 / connected 1119 ms`, WebKit `1130 / 1428`,
Firefox `1472 / 1703`, 12.5 MB transferred, no errors. 23 Sep (T-0011): 12.57 MiB; Chromium `1147 / 1728`,
WebKit `1154 / 1463`, Firefox `1419 / 1654`. 22 Sep's figures follow.

Web build **12.38 MB** (was 12.31) against the 13 MB ratchet; local boot, all three engines against the
new build: Chromium `boot 1553 ms / connected 2645 ms`, WebKit `1756 / 2257`, Firefox `1636 / 2082`,
12.4 MB transferred. Live numbers are still 21 Sep's (`boot 2.8 s, connected 3.2 s`) and predate boons. The
jank investigation is unchanged and still waits on one `?perf=1` reading from Rohan.

## Design: board mechanics (21 Sep, concept level, no code)

Twenty decisions on buildables, hidden traps, area of influence, a laser-sight prop and start-of-turn
guaranteed actions; `docs/design/mechanics.xlsx` is the source of truth for mechanic detail and numbers.
None of it implemented, none of it on the design page yet by Rohan's choice; OQ-N03, N07 and N11 block a
first slice. M3-or-later; nothing in M2 depends on it.

## Last playtest

**21 Sep 2026, Rohan, browser vs bot** (`studio/playtests/2026-09-21-rohan.md`). Copy code (T-0005, done),
refused shots (T-0006, done), and whether a turn should end itself at 0 AP (OQ-N16). No frame-meter reading.

**Still unplayed by a human: two browsers against each other** — the whole of M2-1 — and now also **a
best-of-3 with a draft between rounds**, which nobody has played end to end except a bot and a test.

## The deadline

From `E:\Studios\Trinetra-Game-Studio\docs\PLAN.md` §10: if two browsers cannot play a full match through
the server by **Fri 2 Oct**, the 10 Oct playtest falls back to the local build and online moves to 17 Oct.
Nothing in the code puts that at risk. What is left is one evening with a second human.
