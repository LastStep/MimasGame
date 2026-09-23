---
project: mimas
milestone: M2
updated: 2026-09-24
updated_by: opus — T-0013 at verify (Web build 12.51 MiB, smoke ×3, EditMode 34/34; a browser-only 720 layout loop found and fixed)
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
Every value is on `ArenaCameraRig` on `Cameras/vcam_Tilted` and is live in Play Mode; the default view is an
enum there. Tab and the top-down camera are untouched. ADR-038, spec `docs/specs/2026-09-23-camera.md`, run
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

**473 Core tests, 60 server tests, 34 EditMode tests** (T-0013). The Web build is **12.51 MiB** (13 120 694 B; was
12.57) against the 13 MB ratchet — the serif atlases left. T-0010's run report: `studio/runs/R-2026-09-22-T-0010-build.md`.

## The one thing to do next

1. **The M2 playtest, which only Rohan can do:** send the link to one person who is not Rohan, on another
   network, and play them — now a best-of-3 with a draft. That one evening is M2-1, the step-4 evidence for
   M2-7, and M2-8 with a human on the other seat. Record it as `studio/playtests/<date>-<name>.md`.
   **Deploy first** (`bash tools/deploy/deploy.sh`): the live site is from before boons, and its client
   cannot talk to a server that wants a lineage.
2. **Close T-0010's evidence gap.** Verified 23 Sep: T-0009 passes; T-0010 fails only because five
   player-visible things were written and never looked at (reveal flyover, changed number on the action
   bar with its element word, round card, series banner with Back to room, `boonStat`/`nullify` preview
   label). One practice game in the Editor, five captures, and the verdict flips. Issue list and the small
   follow-ups (a vacuous server assertion, a mislabelled shot): `studio/runs/R-2026-09-23-verify-boons.md`.
   **M3-5 says three ladder maps and two ship** — Rohan decides after the M2 playtest (23 Sep): reword the
   ledger line or build a ladder-position-2 map.
3. **Verify T-0011, the examine plate**, in a fresh context: the run report lists the screenshots, the
   deviations (the builder reads a mirror built from the view, not `Rules`, because in practice `Rules` is the
   truth; commits 5–7 landed as one) and the round-3 preview menu item the §14 captures used.
4. **Verifiers for the M2 set**: T-0008 (closes T-0005 and T-0006), T-0007, T-0002.
5. **Rohan plays the camera and tunes it** (T-0012), then a verifier in a fresh context.
6. **Rohan looks at T-0013 built** (open the Arena, Play, `Mimas/HUD/Preview round 3 (…)`; or play a bot match),
   then approves **T-0014**; **a verifier** checks T-0013 in a fresh context. T-0013 changes the wire (one field on
   the owner's own view, ADR-039), so it deploys together with its server like everything since T-0010.

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
| **T-0010** | **Boons in the game** — the room hosts the session, lineage row, draft over the board, presentation of boons and reveals, practice-mode session, nine boons | **verify — FAILED 23 Sep on evidence, not code.** Five captures from one Editor practice game make it pass; list in `R-2026-09-23-verify-boons` | builder, small |
| T-0009 | Boons groundwork in Core | **verified PASS 23 Sep** (ledger rows are ticked with T-0010, per F-boons) | done pending T-0010 |
| **T-0012** | **The camera** — Q/E turn, WASD pan with a limit, wheel zoom, Space home, V side-on ↔ behind you | **verify** — built 23 Sep, run report `R-2026-09-23-T-0012` | Rohan plays, then verifier |
| **T-0013** | **The match HUD in ink** — layout A, turn track, cost dots, closed-eye mark (one wire field), preview panel, ink draft cards, the band, ink examine plate, ink void | **verify** — built 24 Sep (14 commits `d309c8f`…`fe866ef`), ladder green 4/4, EditMode 34/34 batch, Web build 12.51 MiB, smoke green ×3, the HUD played in the browser at 720/800/1080. Run report `R-2026-09-23-T-0013` | Rohan looks, then verifier |
| **T-0014** | **The lobby and the room in ink** — two seats facing, gear tiles | **plan** — same spec §8; depends on T-0013; Rohan approves after seeing T-0013 built | builder, after T-0013 |
| **T-0011** | **The examine panel** — manuscript plate, one hover panel, `--mimas-*` tokens, `Unit.StatLines`, lineage hues, fonts | **verify** — built 23 Sep, run report `R-2026-09-23-T-0011` | verifier needed |
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
| **Approve T-0014** (the lobby and room) after seeing T-0013 built | It sits at `plan` until then | 23 Sep 2026 |
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
- **The banner colours every line that is not "VICTORY" in the opponent's colour** (`MatchHudView` `banner--lost`),
  the round card included. T-0013's `HudMoment` replaces it.

## Things the next agent must not rediscover

- **Camera keys can be driven for real from `eval`**: `InputSystem.QueueStateEvent(Keyboard.current, new
  KeyboardState(Key.V))`, then an empty `KeyboardState()` in the next eval to release; the same with a
  `MouseState` carrying `scroll` for the wheel. The rig's own polling sees them. The MCP `capture_game_view`
  refuses a `..` path, so it cannot write into `artifacts/`; use `ScreenCapture.CaptureScreenshot` from eval.
- **The Editor never puts a real pointer on UI Toolkit, so some HUD states exist only in the browser.** A 1280×720
  layout loop (the hover panel's height rounding by one device pixel with where its top lands; fixed `2f36241`)
  showed only with the pointer resting on an armed tile, which Editor captures cannot do. Check the HUD in the Web
  build: serve it (`MIMAS_WEB_PATH=<abs>/Build/Web dotnet run --project server/Mimas.Server`), then
  `browser-smoke.mjs --viewport 1280x720 --expect "\[MatchSession\] online" --do "wait:3000,click:640,354,wait:3000,click:640,524,wait:11000,click:239,637,…"`
  (Play vs bot, Ready, the first weapon tile; at 1280×800 add 40 to every y; at 1920×1080 Play vs bot is 960,534 and
  Ready 960,770, the HUD ×1.5). `move:x,y` rests the pointer without pressing (the preview over a target). Count
  `Layout update is struggling` in the output — the smoke fails on it as a console.error anyway.
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
