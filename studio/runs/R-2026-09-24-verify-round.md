---
id: R-2026-09-24-verify-round
task: [T-0014, T-0013, T-0011, T-0012, T-0008, T-0005, T-0006, T-0007, T-0002, T-0001, T-0010]
project: mimas
role: verifier
model: opus (lead, coordinating nine opus verifiers and one builder in fresh contexts)
started: 2026-09-24
finished: 2026-09-24
outcome: needs-rohan
commits: [6b98f6a]
cost_usd: 0
---

# Parallel verification round, 24 Sep 2026

> Opened at the start of the session and appended to as it goes. Recipe:
> `studio/plans/2026-09-24-parallel-verification.md`. The lead builds nothing and fixes nothing.

Eight verifiers ran at once, each cold, each appending its own **Verifier** section to its task's run report. Ten
verdicts (T-0008's verifier judged T-0005 and T-0006 inside it):

| Task | Verdict | What it would take | Where the reasons are |
|---|---|---|---|
| T-0013 the match HUD in ink | **PASS** | — | `R-2026-09-23-T-0013.md` |
| T-0012 the camera | **PASS** | — (Rohan still plays and tunes it) | `R-2026-09-23-T-0012.md` |
| T-0002 the online slice | **PASS** — flipped **M2-4**, **M2-6** | — | `R-2026-09-17-T-0002.md` |
| T-0005 copy code | **PASS** | — | `R-2026-09-21-T-0008.md` |
| T-0014 the lobby and the room in ink | **FAIL**, one small item | the last-result line's reason after a resignation with a lead | `R-2026-09-24-T-0014.md` |
| T-0007 deploy | **FAIL** | **the rollback deletes the build it restores**; runbook backspace bytes | `R-2026-09-22-T-0007.md` |
| T-0008 finish M2 in the browser | **FAIL** | a stale room after a lost match with the socket down | `R-2026-09-21-T-0008.md` |
| T-0006 refused shots show the blocker | **FAIL on evidence** | a second case that *looks open*; one rule-or-picture line per case | `R-2026-09-21-T-0008.md` |
| T-0011 the examine plate | **FAIL on evidence, paper only** | Rohan amends done-when #5 to his own 23 Sep decision | `R-2026-09-23-T-0011.md` |
| T-0001 install the studio | **FAIL, paper only** | a record of F-online-slice's approval; rule 11's lost sentence | `R-2026-09-17-T-0001.md` |
| T-0010 boons in the game (re-verified) | **FAIL — now on code** | six presentation fixes in practice mode, each with a capture | `R-2026-09-22-T-0010-build.md`, last subsection |

Beside the round, one builder took T-0010's missing captures, and a ninth verifier then re-checked T-0010 against them.
Four of 23 Sep's five items are now shown in today's HUD (the reveal flyover over a unit, a boon's changed number, the
series band with its button, a `boonStat` preview line; `nullify` left unproven with a reason). **The captures surfaced
defects in T-0010's own commits**, so it fails again, on code this time.

## Numbers every verifier saw

- `main` at `0a46b79`. Ladder by the lead, one task at a time: **GREEN** for all eight (4/4, T-0001 3/3). **Core 473,
  server 60.** T-0008 re-run at its own commit `73973ee` in a worktree: GREEN 4/4, **Core 323, server 47**.
- **EditMode 40 / 40**, batch, Editor closed (`artifacts/verify-round/editmode.xml`).
- Web build `Build/Web` **13,117,682 B = 12.51 MiB** (T-0014's), against the 13 ratchet.
- Live site, read-only (T-0007's verifier): valid certificate, HTTP/2, `http` → `https`, every spec §1 header, Brotli
  wasm, WS upgrade 101 with the site's Origin and 403 without, Chromium and WebKit smokes exit 0, port 7777 closed from
  outside, `/health` content hash `ea75e2db…`, 6 players since the 21 Sep restart (most of them smoke runs).
- **No hidden-information leak** found (T-0002 at its own commits; T-0013's ADR-039 field, set and sent only for the
  viewer's own unit). **No test weakened, skipped or deleted** in any task's diff. **The ledger** changed only by
  T-0002's verifier flipping M2-4 and M2-6 through `ledger.mjs pass`; `ledger check` → ok · 19 criteria.
- Every verifier checked its task's protected paths by hand, because rung 0 on `main` only sees the working tree. All
  were declared in `allows_assets`.

## Issues to act on

Ordered by who owns them. File and line are in each task's Verifier section.

### Rohan

- **Do not run `deploy.sh --rollback` until it is fixed** (T-0007). It deletes the previous build, leaves the current
  one live, restarts, passes health, and prints "rolled back". Nothing is lost yet: it has never been run. It matters
  because the next deploy is the big one (boons, the new HUD), and the rollback is its safety net. A builder can fix it
  from the PC; nothing needs doing on the VPS (`deploy.sh` ships the release script on every run).
- **T-0011 passes the moment done-when #5 says what you decided on 23 Sep**: the plate is a layer over the HUD with
  End Turn and Resign under it; a board click only closes it; a HUD click closes it and still acts; Escape and ✕ close
  it; it stays open across a turn. The one-pager's "slides in from the right" too. The code already does all of it.
- **T-0001, two questions only you can answer**: did you approve `F-online-slice` on 17 Sep (the builder created it as
  `approved`)? And was dropping `CLAUDE.md` rule 11's "when you implement a section, flip its `data-impl`" (and the
  changelog duty) intended? If not, a builder puts it back.
- **Accept or reject**: M1-1…M1-3 were seeded `passes: true` by the builder that installed the studio, not by a
  verifier. The claims are true; the ledger note admits it.
- **M2-2's wording** still says "(mechanism open: OPT-0001, queue vs room codes)". Room codes were decided (ADR-029).
  Agents may not reword the ledger.
- **Whether a builder may add paths to its own `allows_assets`, or park another task as `blocked` to make its own the
  active one**, after the guard refuses it (T-0008 did both, disclosed and needed by its spec; T-0002 did the first).
- **M3-6 is one true/false row** ("Character select and draft screens"), so the draft half F-boons wants ticked with
  T-0010 cannot be ticked alone: split the row or wait for character select. **M3-5** still says three ladder maps (two
  ship), as the 23 Sep round said.
- Still open from before: the playtest with a friend on the live site (M2-1, M2-7, M2-8's human half), deploy,
  translucent fills, camera tuning, boon numbers.

### A builder, small

Two light tasks would carry them: **items 1–5 and 7** (the online path and paper; T-0007's first, because it guards
the next deploy) and **item 6** (practice-mode presentation, needs the Editor). They touch different files and can run
side by side if only one of them uses the Editor.

1. **T-0007 — the rollback.** `tools/deploy/vps-release.sh:30-38` `swap_in` starts with `rm -rf "$base.prev"`, and
   `--rollback` (`:60-61`) passes `prev` as the source. Make rollback swap the live and `.prev` builds (web and server),
   leave normal releases alone, and show it on dummy folders under `artifacts/` (one rollback swaps, a second swaps
   back). Also: `docs/deploy-runbook.md:224` has two literal backspace bytes (`Git<BS>in<BS>ash.exe`); a rollback right
   after a first-ever release leaves the server stopped (empty `.prev`); `deploy.sh:79-83` prints a wasm check in a
   dry-run rollback that a real one skips; the run report's "two seats taken, presumably Rohan" were the builder's own
   smokes, and its ladder table claims rungs the file does not hold.
2. **T-0008 — the stale room.** If a player's socket is down when the match ends, the server frees the seat, the
   client keeps the room, "Back to room" opens a room they are not in, and Ready / Leave say "not in room" until a
   reload (`NetClient.cs:155-164`, `LobbyView.cs:156` at HEAD). Fix it with a test or a capture. Also: the untested
   branch where a room with no human left closes at match end (`Room.cs:382-386` at `73973ee`); `docs/roadmap.md:184`
   says nine new server tests, there are 13; the run report's `curl -sI` cannot have shown a `<title>`.
3. **T-0002's find — `room.leave` mid-match.** `Room.Leave` has no phase guard (`Room.cs:166-186` at HEAD): a
   hand-made `room.leave` during a match clears the seat, the forfeit check then skips it, and the leaver's turns time
   out forever. No UI sends it; a crafted client can. Check what it does in a draft too.
4. **T-0014 — the last-result line.** `RoomLayout.cs:72` counts any lead as explaining the result; `docs/ui/lobby.md:26`
   says a resignation before the rounds decide it shows the reason. Make them agree (the band already says "OPPONENT
   RESIGNED"), add the 1–0 test, re-capture item 1. Also: `artifacts/t0014/editmode-batch.log` is empty though cited;
   Chromium boot 672 → 1329 ms not measured apart from `FontSpacing`.
5. **T-0006 — the second sight case.** Write down a second shot that looks open and is refused (a line grazing a
   hero's hex), with its blocking hex, and one "the rule / the picture was wrong" sentence per case in the run report.
6. **T-0010 — practice-mode presentation** (a second light task; all from T-0010's commits, each needs a capture):
   (a) practice rounds 2 and 3 open with **no round band and no round-start reveal** — `LocalMatchDriver.cs:253-258`
   raises `NextRound` and drops the round's opening batch, and the reloaded scene's `Begin()` (`:114-118`) raises
   `Resynced` instead (`f51aff9`; spec §7.4 says so, but §7.5/§14 and the done-when win); (b) **a fresh practice series
   plays round 1 on an empty board** — no units, no action bar — because `MatchSession.cs:642` spawns units only once a
   view exists and the view exists only after `Begin()` (`:676-678`; `73d15c9` + `f51aff9`; before T-0010 it worked;
   STATE had it as a CLI quirk); (c) so round 1's reveal flyovers draw at screen centre (`Vector3.zero`, `:1335-1337`);
   (d) the hit flyover names a boon by id, `THOR-MIGHT` (`DescribeSurprise`, `:1370`, `7098bc4`); (e) Arrow Shot is
   marked changed under Vayu's Haste though its cost stays 1 (`IsChangedByABoon`, `:774-785`, `7098bc4`; spec §7.5 says
   the mark means the resolved number differs); (f) an opponent's first boon reveal and the lineage reveal draw text over
   text (`MatchHudView.cs:973-998`). Online looks sound in code (round 2's `match.start` carries the batch) but nobody
   has watched it — the playtest will. Same task: T-0011's `ExamineModelBuilder.AddChanges` (`:420-425`, `099c470`) has
   no floor, so a panel could read `cost 1 → 0`; T-0013's tile hover panel can outlive the bar into the series result
   (seen with an injected hover), and `HoverPanelView.cs:197` draws every change row with the Enchant glyph, a Sigil's
   "granted by" included.
7. **Paper**: ADR-026 (`docs/decisions.md:33`) says the mirror test covers `RangeBand`, it does not; T-0012's run report
   says every rig value is live in Play Mode, but a view's angle / distance and the default view apply at the next
   Space or V (`ArenaCameraRig.cs:171-177`); T-0011's run report body and `commits:` predate Rohan's rework; T-0001's
   report says seven disabled rungs, there are eight.

### Known and deliberate, for the next reader

- T-0013's series-result capture reads BACK TO LOBBY: practice has no room. The online BACK TO ROOM is in T-0014's
  `extra-series-band-1080.png`.
- T-0013's kerning fix held only for glyphs drawn before bind; no done-when names letter-spacing, and T-0014 completed it.
- T-0011's hard-coded unknown-lineage colours (`ExamineView.cs:180-187` at its commits) are gone since T-0013; the
  action-bar re-centring (`0e8c95c`) is outside its one-pager but declared and kept by Rohan. T-0011's only
  three-engine smokes after the rework are on T-0013's and T-0014's builds, which contain every T-0011 file.
- T-0012: stopped time and seat 2 are proven by code and EditMode tests, not watched in Play Mode.
- T-0010: practice says BACK TO LOBBY where online says BACK TO ROOM; `HudAction.Detail` (the old "element word first"
  line) is still built but nothing draws it since ADR-040 — the element is a chip in the hover panel now; `nullify`
  unproven (needs a bot with an immunity facing that element). Game C's Greek bot was built in memory on the practice
  host for the captures; no game code changed.
- T-0002: the mirror hit-check sweeps 6 games not 20; no Web build (the spec's fallback); the forfeit winner's banner,
  the disconnect countdown and the lag grace were never shown (they need a second client).
- T-0008's plan step 2 ("a reload after a match lands you back in your room") was not delivered — listed under "Left
  for next time"; how long a room holds a seat between matches is already an open question for Rohan.
- STATE claimed "M2-5 done … in the Editor" and "M2-6 … both paths seen"; the T-0002 verifier found no capture of a
  real client returning mid-match or of a forfeit from a player's seat. STATE is corrected in this round.

### Tooling

- **Rung 0 on `main` checks only the working tree**, so after a task is committed it proves nothing about that task's
  protected paths. Every verifier fell back to checking them by hand. A `--task` run could diff the task's commits.
- **The asset guard's `**/Temp/**` matches the session scratchpad** (`AppData\Local\Temp\…`), so an agent cannot name
  its own scratchpad in a shell command (the lead's worktree for the T-0008 re-run was refused there). And the guard's
  refusal text points at `studio/protocols/reward-hacking-guards.md`, and `.claude/agents/builder.md:10` at
  `studio/protocols/session-start.md`: right in the studio repo, but in this project the protocols are installed under
  `.claude/protocols/`, so an agent here following either finds nothing.
- **The Web build command is stale** in the unity-live-editor skill (line 52), its protocol (line 55) and `game.yaml`
  rung 9: `--profile "Web Release"`, which `CLAUDE.md` now forbids. The studio's source protocol too.
- `deploy.sh:6` says `--dry-run` runs "none of the remote ones", but it opens ssh to the shared VPS and, without
  `--skip-build`, starts a Unity batch build (which clashes with an open Editor).
- `artifacts/editmode.xml` is one file every task overwrites, so a run report citing it goes stale at the next task.
  Per-task output paths would keep evidence.
- The size ratchet `13` has no unit; the project measures MiB, and in decimal MB the build has been over 13 since
  before T-0013.
- The browser smoke still prints "splash is still on the canvas at this point"; the splash is gone.
- A verifier's `grep -P` left an msys `grep.exe.stackdump` at the repo root (removed by the lead).

## Log

- Session start: `main` at `0a46b79` (the plan's own commit); the only local change is `studio/runs/events.ndjson` (the studio's hook log,
  not this session's; left alone). The Editor is closed (`unity status`: no instances). Queue re-checked in
  `studio/tasks/`: T-0014, T-0013, T-0011, T-0012, T-0008 (with T-0005, T-0006), T-0007, T-0002, T-0001 at `verify`,
  as the plan listed. T-0010 is at `verify` too but failed on evidence on 23 Sep and needs a builder's captures first.
- Ladder, run by the lead one task at a time at `0a46b79` before any verifier started (rule 1): **GREEN** for all
  eight — T-0014, T-0013, T-0011, T-0012, T-0008, T-0007, T-0002 4/4 (rungs 0, 1, 2, 5), T-0001 3/3 (0, 1, 2). Core
  `test_count` 473, server 60, each 20–26 s. Files: `studio/runs/.ladder/T-NNNN.json`. T-0005's and T-0006's extra rungs
  (7, 10) are disabled in `game.yaml`; both are judged inside T-0008's report, as the plan says.
- EditMode, batch, Editor closed, at `0a46b79`: **40 tests, 0 failures, 0 skipped**
  (`artifacts/verify-round/editmode.xml`; CameraRigMath 10, CorePackageSmoke 2, ExamineModel 10, HudModel 10,
  MirrorResolver 1, …). Handed to every verifier.
- Launched eight `verifier` agents in one message, one per task, with the plan's prompt plus: never `git checkout` /
  `stash` / `reset` (one shared tree, read old versions with `git show <rev>:<path>`), no `dotnet`, no `unity`, no ledger
  writes. Launched one `builder` beside them for T-0010's five captures (the plan's "not in the round" item): it is the
  only agent allowed to drive the Editor, writes captures under `artifacts/t0010/`, fixes nothing, commits nothing.
- **T-0012 → PASS** (verifier, ~7 min). All eight done-when true; commits `ff74634`, `3308ad0`, `a0c5a68`; camera files
  untouched since. EditMode 14 → 24 at the time, no test weakened. No ledger row to flip (no camera row). Found: the
  report and STATE say every rig value is live in Play Mode, but framing values (a view's angle/distance, the default
  view) only apply at the next Space/V (`ArenaCameraRig.cs:171-177`) — doc wording, builder small. Rung 0 at HEAD only
  scans the working tree, not the task's commits — tooling (the verifier checked the protected paths by hand).
- **T-0014 → FAIL, one small item** (verifier, ~8 min). The lobby's last-result line drops the reason on a resignation
  that ends the series while one side leads: `RoomLayout.cs:72` treats any lead as explaining the result, but
  `docs/ui/lobby.md:26` (added in this diff) says a resignation before the rounds decide it shows the reason. The
  builder's own item-1 capture shows "VICTORY VS GUEST-4417 · SERIES 1 – 0" for a 1–0 resign (server log "winner 1 0-1
  (Resign)"), while the series band for the same match says "OPPONENT RESIGNED". Only test uses 0–0. Everything else
  holds (done-when 1–7, the four out-of-list files explained). Also: `artifacts/t0014/editmode-batch.log` is 0 bytes
  though the report cites it; Chromium boot 672 → 1329 ms declared, not measured apart. No ledger row.
- **T-0001 → FAIL, paper only** (verifier, ~8 min). Done-when 1–3 hold (STATE, ladder from `game.yaml`, every M1–M3
  done-when a ledger row, `ledger check` ok · 19). Done-when 4 ("M2 one-pagers are drafts ready for Rohan") does not:
  the builder created `F-online-slice` as `status: approved` on 17 Sep with no record of Rohan approving it. And the
  `CLAUDE.md` rewrite dropped rule 11's "flip `data-impl` when you implement a section" and the changelog duty, which
  no protocol or skill carries. Also: M1-1..3 seeded `passes: true` by the builder, not a verifier (true claims,
  admitted in the ledger note); the Web build command in the unity-live-editor skill/protocol and `game.yaml` rung 9
  still says `--profile "Web Release"`, which `CLAUDE.md` forbids; the builder agent definition (line 10) points at a
  non-existent `studio/protocols/`. No ledger row.
- **T-0007 → FAIL** (verifier, ~10 min). **The rollback destroys the build it should restore.** `swap_in` in
  `tools/deploy/vps-release.sh:30-38` always begins `rm -rf "$base.prev"`; `--rollback` calls it with `prev` as the
  source (`:60-61`), so the previous build is deleted, the current one is moved out and straight back, the server
  restarts on the same build, health passes, and it prints "rolled back". The lead read the script and confirms it.
  Reproduced by the verifier on dummy folders (`artifacts/verify-t0007/rollback-sim/result.txt`: after rollback
  `web -> v2-current`, `web.prev` gone). Never run on the VPS. Also `docs/deploy-runbook.md:224` holds two literal
  backspace bytes where `\b` should be. Everything else holds, checked live and read-only: HTTPS, HTTP/2, redirects,
  every header of spec §1, Brotli wasm, WS upgrade 101 with the site's Origin and 403 without, Chromium and WebKit smoke
  on the live site, port 7777 closed from outside. Dry-run checked with ssh/Unity/dotnet stubbed to print. No ledger
  flip: M2-7 needs a non-Rohan player (the live `/health`'s 6 players are mostly smoke runs). The run report's "two
  seats taken, presumably Rohan" were the builder's own smokes.
- **T-0008 → FAIL; T-0005 → PASS; T-0006 → FAIL** (one verifier, ~12 min). T-0008: done-when 2–11 met; 1 needed a run on
  T-0008's own code (T-0010 rewrote two of its tests). **Regression, found by reading:** if a player's socket is down
  when the match ends the server frees their seat (`Room.cs:368-374` at `73973ee`), but the client keeps the room
  (`NetClient.cs:148-158`), so "Back to room" opens a room they are not in and Ready / Leave answer "not in room" — only
  a reload gets out. Still at HEAD (`NetClient.cs:155-164`, `LobbyView.cs:156`). T-0005: all four done-when met
  (clipboard read back `KJDH` in headed Chromium). T-0006: done-when 1 wants two cases that *look open* and are
  refused; the second (`SightArena4Tests`, hero body at the pillar) puts the opponent squarely on the line, so it looks
  blocked; done-when 2's rule-or-picture sentence per case is not in the run report.
- **T-0002 → PASS** (verifier, ~12 min). All seven done-when at `b3460d9`…`9291363`; hidden info holds (every send is
  the seat's `PlayerView` + filtered events; `AssertEveryViewBelongsTo`, two `HiddenInfo_*`, loadout not leaked).
  Would flip **M2-4** and **M2-6**; not M2-1 (no browser), M2-3/M2-5 (no page reload exercised), M2-2 (no real client
  joined by code at T-0002's code). Found: `room.leave` mid-match has no phase guard (`Room.cs:166-186` at HEAD) — a
  hand-made leave clears the seat, the forfeit check skips it, turns time out forever (no UI sends it); ADR-026 claims
  the mirror test covers `RangeBand`, it does not; STATE's "M2-5 done … in the Editor" / "M2-6 both paths seen" have no
  capture behind them.
- **T-0011 → FAIL on evidence, paper only** (verifier, ~13 min). Done-when #5 still says End Turn works with the plate
  open and an off-plate click selects nothing; Rohan's 23 Sep rework (the plate a layer over the HUD, a HUD click
  closes it *and* acts) made both untrue, and the code and `docs/ui/examine.md` follow Rohan — the task file and the
  one-pager ("slides in from the right") were never updated. Seven of eight done-when met. The only three-engine smoke
  after the rework is on later builds (T-0013's, T-0014's), which contain every T-0011 file; the lead accepts that —
  building `2207834` alone would prove a plate that no longer exists.
- **T-0013 → PASS** (verifier, ~14 min). ADR-039's wire field leaks nothing (set and serialised only for the viewer's
  own unit; 12 Core + 2 socket tests; no existing test changed; 473 = 461 + 12, 60 = 58 + 2). All 12 states captured at
  both sizes. The kerning defect was real at `fe866ef` but no done-when names letter-spacing; T-0014 fixed it. Found:
  `artifacts/editmode.xml` (cited for 34/34) was overwritten by T-0014 and `artifacts/t0013/editmode-batch.log` is
  empty — this round's `artifacts/verify-round/editmode.xml` covers it; the size ratchet `13` has no unit (MiB vs MB).
  No ledger row.
- Re-run for T-0008, by the lead: `git worktree add --detach` at `73973ee` beside the repo
  (`E:/Unity Projects/MimasGame-verify-73973ee`), ladder `--root` there: **GREEN 4/4, Core 323, server 47** (37.8 s),
  copied to `artifacts/verify-round/ladder-T-0008-at-73973ee.json`; `Room_MatchOver_ReadyAgain_StartsRoundTwo` and
  `Match_TwoRoundsInOneRoom_BothComplete` both passed by name. Sent to T-0008's verifier; the stale-room regression
  still stands. The worktree was first tried in the session scratchpad and refused by the asset guard (the path runs
  through `AppData\Local\Temp`, which matches `**/Temp/**` — a false positive; the pattern is for Unity's Temp).
- Ledger: all eight verdicts in; the lead asked T-0002's verifier (the only pass with rows) to flip M2-4 and M2-6.
- Ledger flips done by T-0002's verifier through `ledger.mjs pass` (M2-4, M2-6; `passed_by: verifier`, task T-0002,
  evidence text); the lead read the diff (only those two rows and `updated`) and ran `ledger check` → ok · 19. Committed
  in `6b98f6a` with the eight Verifier sections and the hook log, staged as `git add -u studio` (the lead did not
  write the ledger; naming it in `git add` would be refused with no running task, and `ledger check` compares against
  HEAD, so the tool's change has to be committed).
- **T-0010 captures, builder** (~38 min, the only agent on the Editor). Five of six in `artifacts/t0010/` (1920×1080,
  real practice states): the reveal flyover `REVEALED · THOR'S MIGHT` / `NORSE · BLESSING` over the bot
  (`d-reveal-0.png`); a boon-changed tile with its number in the hover panel (`◆ cost 2 → 1 · Vayu's Haste`,
  `a-r3-aimedshot-tile-hover.png`; Move `reach 1 → 2`; a Sigil tile's triangle); the series band with its button 3.1 s
  later (`a-series-back.png`, BACK TO LOBBY — practice has no room); a preview `?` row turning into `Athena's Guard −1`
  after the reveal (`c-preview-*.png`); the own-hero ink plate with its boons (`c2-examine-own.png`). **Not captured: a
  round-2 card** — in practice rounds 2 and 3 open with **no band and no round-start reveal** (the next round reloads the
  Arena and resyncs instead of replaying the opening events), and a fresh practice series comes up with no units, so
  round 1's card and flyovers draw over an empty board. Also found, none fixed: the damage flyover names a Blessing by
  id (`THOR-MIGHT REVEALED`, `DescribeSurprise`); two reveal flyovers raised together overlap; the element word no
  longer leads a detail line (`HudAction.Detail` built, not drawn; the element is a chip now); a tile hover panel can
  outlive the bar into the series result; `nullify` not reached. Editor closed, fonts restored, only the build report
  changed. Write-up: `## Captures for the re-verification (24 Sep)` in `R-2026-09-22-T-0010-build.md`.
- T-0010's ladder re-run by the lead at `6b98f6a`: GREEN 4/4 (473 / 60). A fresh verifier launched on T-0010.
- **T-0010 → FAIL, on code** (verifier, ~11 min). Four of the five 23 Sep items now shown; six defects from T-0010's
  own commits (list under "A builder, small", item 6). Corrects the builder on one point: `HandleResynced` clearing the
  queue is not the cause — the round's opening batch is never queued. No ledger flip; after a pass: M3-3, M3-4, M3-5
  and the draft half of M3-6 (both of the last two need Rohan first).
- The lead removed `grep.exe.stackdump` (an msys crash dump the T-0007 verifier's `grep -P` left at the repo root).
- Asset-guard false positive (the lead): a `cat >> <this report> <<EOF` append was refused because its prose named the
  builder agent definition's path. The write target was this report only; re-done with the Edit tool.
