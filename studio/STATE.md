---
project: mimas
milestone: M2
updated: 2026-09-22
updated_by: opus — T-0010 built; boons are in the game, online and in practice
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

**452 Core tests (was 445), 58 server tests (was 47), 4 EditMode tests.** The Web build is **12.38 MB**
against the 13 MB ratchet, and boots in 1.5–1.8 s on all three engines. Run report:
`studio/runs/R-2026-09-22-T-0010-build.md`.

## The one thing to do next

1. **The M2 playtest, which only Rohan can do:** send the link to one person who is not Rohan, on another
   network, and play them — now a best-of-3 with a draft. That one evening is M2-1, the step-4 evidence for
   M2-7, and M2-8 with a human on the other seat. Record it as `studio/playtests/<date>-<name>.md`.
   **Deploy first** (`bash tools/deploy/deploy.sh`): the live site is from before boons, and its client
   cannot talk to a server that wants a lineage.
2. **Verifiers.** Six tasks sit at `verify` and nothing in the ledger is ticked until someone in a fresh
   context agrees: **T-0010** (its spec §14 is the checklist), T-0009 (part-1 spec §14), and the M2 set
   T-0008 (closes T-0005 and T-0006), T-0007, T-0002.

## Current milestone: M2 — online

Target: **Sat 10 Oct 2026**, friends playing over the internet. **18 days.**

| Done-when | State |
|---|---|
| M2-1 Two browsers play a full match | **one browser plays the server.** The second seat has never been driven by a human. That is the whole remaining game-side gap |
| M2-2 Two players who want to play each other end up in the same match | **done** — room code (OPT-0001, ADR-029) |
| M2-3 Guest auth with a resumable token | **done** (browser reload still untested by a human) |
| M2-4 Server-authoritative 30 s turn | **done**, seen firing live |
| M2-5 Reload within the 60 s grace resyncs | **done** server-side and in the Editor; since 22 Sep a reload lands back in a draft too |
| M2-6 Resign and disconnect-forfeit | **done**, both paths seen; since 22 Sep both end the series |
| M2-7 Deployed on the VPS over HTTPS | **live since 21 Sep, confirmed from outside; pending verifier** and one playtest entry from someone who is not Rohan. **The live build is now behind `main`** |
| M2-8 Rematch without leaving the room | **built and played live 21 Sep (T-0008), pending verifier.** A rematch is now a new *series* |

**Nothing in the ledger is ticked.** `pass` belongs to a verifier, not the builder who wrote the code.
M3's boons rows — **M3-3, M3-4, M3-5 and the draft half of M3-6** — are now built and are the verifier's to
tick.

## In flight

| Task | What | Status | Who |
|---|---|---|---|
| **T-0010** | **Boons in the game** — the room hosts the session, lineage row, draft over the board, presentation of boons and reveals, practice-mode session, nine boons | **verify.** Eleven commits, 22 Sep, run report `R-2026-09-22-T-0010-build` | verifier needed |
| T-0009 | Boons groundwork in Core | verify | verifier needed |
| T-0008 | Finish M2 in the browser | verify — deployed and measured live 21 Sep | verifier needed |
| T-0007 | Deploy | verify — live confirmed 21 Sep | verifier needed |
| T-0002 | Execute the online slice | verify | verifier needed |
| T-0005, T-0006 | Copy code; refused shots show the blocker | verify — executed inside T-0008 | close with T-0008 |
| T-0003 | The Web build, in a browser | **blocked** — waiting on Rohan's `?perf=1` reading since 18 Sep | Rohan |

## Blocked

Nothing, except what waits on Rohan below.

## Waiting on Rohan

| What | Why | Since |
|---|---|---|
| **Deploy** (`bash tools/deploy/deploy.sh`) | The live site predates boons. Its client cannot start a match against a post-boons server, because `room.loadout` now needs a lineage; an old tab must reload. `/health`'s content hash changes | 22 Sep 2026 |
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

## Things the next agent must not rediscover

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

## Numbers, 22 Sep

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
