---
project: mimas
milestone: M2
updated: 2026-09-22
updated_by: fable — T-0009 built (boons groundwork in Core), at verify
---

# Where Mimas stands

> **Rewritten, never appended.** One page, always current. History lives in `runs/` and in git.

## Right now

**Mimas is on the internet: `https://mimas.laststep.cloud`** — deployed by Rohan on 21 Sep and
confirmed from outside the same day. **The live site is the repo as of `73973ee`** (the T-0008 work: the
room outlives the match, the Mimas template, Copy code, the blocker shown, the origin check). Nothing
built since has been deployed, and nothing built since changes what a player sees.

**22 Sep: the boons system exists in the rules engine (T-0009, part 1 of F-boons), at `verify`.** Nine
commits on `main` (`c4d9c85` … `73aa09c` plus the docs commit that follows), Core only, no server file
changed, no client file changed:

- `boons/` and `lineages/` **load and link** with every rule of spec §5: three lineages (Greek, Norse,
  Hindu), six boons each plus a starting Blessing, six Sigil abilities, nine hidden modifiers — placeholder
  numbers, Rohan tunes.
- A unit is **gear + lineage + boons through one overlay** (ADR-034); `MatchState.ResolveAbility` is the
  only ability lookup (a source scan proves it); a Blessing's Strength is its own hidden damage line; an
  immunity is one `Nullify` line; an Enchant's element satisfies a rider.
- **Boons and lineages reveal** by the six rules of §6.5 (round-start hp/AP Blessings; a line that changed
  a result; a Sigil's first use; the observation rule for Enchants: aim, cost, element, damage), and the
  view, the mirror and the wire carry them.
- `Draft.Offer` is a **pure seeded function**; `Mimas.Core.Session.Session` plays a **best-of-3 with a
  draft between rounds**, wraps the ladder, seats the loser first, keeps reveals across rounds, and replays
  identically from one seed (a golden test with two seeds, 839 and 977 log lines).
- Skeletons (`hits`, the trajectory swap) **parse and fail closed** naming the spec.

**323 → 445 Core tests, 47 server tests untouched.** Ladder for T-0009: see `studio/runs/.ladder/T-0009.json`
and the run report `R-2026-09-22-T-0009`.

## The one thing to do next

Two things, and they do not compete:

1. **The M2 playtest, which only Rohan can do:** send the link to one person who is not Rohan, on another
   network, and play them — with a rematch. That one evening is M2-1, the step-4 evidence for M2-7, and
   M2-8 with a human on the other seat. Record it as `studio/playtests/<date>-<name>.md`.
2. **A Fable session writes the part-2 spec** (`docs/specs/2026-09-21-boons-in-game-outline.md` → a full
   spec, `T-0010`) against the real Core at `main`, asking the outline's §5 questions with 3–4 options
   each; then Opus executes it. Part 2 is what a player sees: the lineage in the room, the draft screen,
   the examine panel's boons, the reveal flyover, the online best-of-3. **The prep note for that session
   is `studio/plans/2026-09-22-part2-spec-session-prep.md`**: what to read, the ten findings this build
   made that the outline lacks, and the six questions to ask in one round.

Then **verifiers**: T-0009 (spec §14 is the checklist), and the M2 set T-0008 (closes T-0005 and T-0006),
T-0007, T-0002 — five tasks sit at `verify` and nothing in the ledger is ticked until someone in a fresh
context agrees.

## Current milestone: M2 — online

Target: **Sat 10 Oct 2026**, friends playing over the internet. **18 days.**

| Done-when | State |
|---|---|
| M2-1 Two browsers play a full match | **one browser plays the server.** The second seat has never been driven by a human. That is the whole remaining game-side gap |
| M2-2 Two players who want to play each other end up in the same match | **done** — room code (OPT-0001, ADR-029) |
| M2-3 Guest auth with a resumable token | **done** (browser reload still untested by a human) |
| M2-4 Server-authoritative 30 s turn | **done**, seen firing live |
| M2-5 Reload within the 60 s grace resyncs | **done** server-side and in the Editor |
| M2-6 Resign and disconnect-forfeit | **done**, both paths seen |
| M2-7 Deployed on the VPS over HTTPS | **live since 21 Sep, confirmed from outside; pending verifier** and one playtest entry from someone who is not Rohan |
| M2-8 Rematch without leaving the room | **built and played live 21 Sep (T-0008), pending verifier** |

**Nothing in the ledger is ticked.** `pass` belongs to a verifier, not the builder who wrote the code.
M3's boons rows (M3-3, M3-4, M3-5, half of M3-6) are player-visible outcomes and wait for part 2.

## In flight

| Task | What | Status | Who |
|---|---|---|---|
| **T-0009** | **Boons groundwork in Core** — definitions, the unit overlay, elements, reveal, draft, `Session` | **verify.** Nine commits, ladder run 22 Sep, run report `R-2026-09-22-T-0009` | verifier needed |
| T-0010 | Boons in the game — online best-of-3 with the draft in the room, presentation | draft (outline only; Fable writes the full spec next, Opus builds) | Fable specs, Opus builds |
| T-0008 | Finish M2 in the browser — rematch, template, Copy code, blocker, origin check, three-engine smoke | verify — deployed and measured live 21 Sep | verifier needed |
| T-0007 | Deploy | verify — live confirmed 21 Sep | verifier needed |
| T-0002 | Execute the online slice | verify | verifier needed |
| T-0005, T-0006 | Copy code; refused shots show the blocker | verify — executed inside T-0008 | close with T-0008 |
| T-0003 | The Web build, in a browser | **blocked** — waiting on Rohan's `?perf=1` reading since 18 Sep | Rohan |

## Blocked

Nothing, except what waits on Rohan below.

## Waiting on Rohan

| What | Why | Since |
|---|---|---|
| **One match against a friend on another network, with a rematch**, recorded as a playtest file | It is M2-1, the last evidence M2-7 needs, and M2-8 with a human opponent | 21 Sep 2026 |
| **Play one bot match at `?perf=1`** and say what the meter showed | The only instrument that sees your 144 Hz vsync; unblocks T-0003 | 18 Sep 2026 |
| **Tune the boon numbers** if you want to before part 2 — `MimasClient/Assets/_Game/Data/boons/*.json`, `abilities/`, `modifiers/` | They are the spec's placeholders, written as given. Nothing enforces them; the tests only check shapes and rules | 22 Sep 2026 |
| Edit `pillars.md` — it is a draft distilled from the design page, and the pillars are yours | | 17 Sep 2026 |
| Optional: should a room show the other seat's chosen preset before the match starts? | design `#q-online-room-loadout` | 17 Sep 2026 |
| Optional: should a room hold your seat for a grace **between** matches? | Today a reload frees the seat. A design question, not a bug | 21 Sep 2026 |
| Answer OQ-N03, N07, N11 on `docs/design/mechanics.xlsx` before anyone builds the board mechanics | jump/teleport crossing a beam; trap consumed on fire; lane and power for structure damage | 21 Sep 2026 |

## Decided on 22 Sep: how boons live in the code (ADR-034, ADR-035)

| Decision | Chosen |
|---|---|
| Where a boon's effect lives | **An overlay on the unit, never a change to a definition.** `BoonOverlay` folds a unit's boons once into flat tables; every entry names its boon; `MatchState.ResolveAbility` is the only place an ability is read, and `ResolveAbilityKnownTo(viewer)` applies only what that viewer has been shown |
| How a boon reveals | **Through the line it puts in the breakdown.** A Blessing's Strength is a `BoonStat` line, an Enchant's damage override too; the existing "a hidden line that changed the number is revealed" loop does the rest. Boon and lineage reveals are keyed apart from ability and modifier reveals (shipped content names a boon and its modifier the same) |
| The "?" count | **One unknown per hidden boon, whether or not it applies** — the same rule hidden modifiers already follow, and the only count a mirror can reproduce. So a hidden boon that attaches a hidden modifier is two "?"s |
| Where the series lives | **A Core state machine, `Mimas.Core.Session.Session`**, that constructs one `MatchState` per round from its own seeded RNG. The server hosts it in part 2; clocks stay outside Core (a timeout pick is a command) |
| The class name | `Session` inside namespace `Mimas.Core.Session`, as the spec names it. From another namespace the bare name finds the namespace first: alias it (`using Session = Mimas.Core.Session.Session;` inside your own namespace block) or write `Session.Session` |

The 21 Sep decisions (D1–D18) are in `docs/specs/2026-09-21-boons-groundwork.md` §2 and, since 22 Sep,
written into the design page's sections and its decision log. The design page's thirteen closed questions
are gone from their sections; `q-draft-tiers` and `q-draft-reroll` stay as "later, not at launch".

## Decided on 21 Sep: the room outlives the match (ADR-032), origins in JSON (ADR-033)

| Decision | Chosen |
|---|---|
| Rematch | **Back to the same room.** Same code, both seats, Ready reset, gear editable; both pressing Ready is the rematch. No offer, no session score (the session score now exists in Core and reaches the room in part 2) |
| The page | **Full-window canvas**, dark, titled `Mimas`, own favicon, percentage bar, no footer |
| Origin check | **`Mimas:AllowedOrigins` in `appsettings.Production.json`.** Empty = any (dev and tests); production names one origin |

## Deploy: what exists, what is proved

`docs/specs/2026-09-22-deploy.md`, all three parts done (T-0007, `verify`). Redeploy is
`bash tools/deploy/deploy.sh` from Git Bash; `--rollback` swaps back. **Proved on the VPS, 21 Sep:** the
page is titled `Mimas`; the four hashed `/Build/` files come back Brotli and `immutable`; `/ws` is `101`
with the site's `Origin` and `403` without; a two-round match was played in room `55QY`; three green live
smokes at `boot 2.8 s, connected 3.2 s, 12.3 MB`; the unit active with `NRestarts=0`. **Not proved:** a
real Safari, and anyone other than Rohan loading it. The rest of the deploy detail is in the spec, the
runbook `docs/deploy-runbook.md` and `R-2026-09-22-T-0007`.

**Redeploying now would ship the boons data files** (the content hash changes; `/health` reports it) with
nothing that reads them online. Harmless, but pointless until part 2.

## Numbers, 21 Sep

Local build: Chromium `boot 662 ms / connected 1155 ms`, WebKit `1121 / 1446`, Firefox `1315 / 1584`,
12.3 MB transferred. Live (Chromium, headless, three runs): `boot 2.8 s, connected 3.2 s, 12.3 MB`. Build
size **12.31 MB** against the 13 MB ratchet. The jank investigation is unchanged: the arena renders in
under 4.17 ms at 2560×1440, the one measured hitch (267 ms, `gc=no`, entering the Arena) has the shape of
first-use shader compilation, and it waits on one `?perf=1` reading from Rohan.

## Design: board mechanics (21 Sep, concept level, no code)

Twenty decisions on buildables, hidden traps, area of influence, a laser-sight prop and start-of-turn
guaranteed actions; `docs/design/mechanics.xlsx` is the source of truth for mechanic detail and numbers.
None of it implemented, none of it on the design page yet by Rohan's choice; OQ-N03, N07 and N11 block a
first slice. M3-or-later; nothing in M2 depends on it.

## Things the next agent must not rediscover

- **The asset guard's active task is "the single task file whose `status` is `running`"** (or
  `TRINETRA_TASK`). Two running tasks, or none, means no task is active and every protected path is refused.
- **The guard refuses the session scratchpad** — its path contains a folder named `Temp`, so a script
  written there and run from there is refused twice (`**/Temp/**`). Write generated files straight to
  their destination with the Write tool (39 data files, this session) and note the false positive.
- **The guard refuses read-only listings and greps** over `.claude/**`, `**/*.meta`, `docs/design/index.html`,
  `studio/game.yaml`, `studio/ledger.json`. Use the Read/Grep/Glob tools; never rephrase a command that writes.
- **Test files are protected (`**/Mimas.Core.Tests/**`) and every task that needs new tests must declare
  them in `allows_assets`**, as T-0008 and T-0009 did. Editor-generated `.meta` files for new Core sources
  the same way. Golden rule 1 still holds: the Editor writes them, you commit them with the asset.
- **The ladder runner wants rung numbers in the task's `ladder:`** (`[0, 1, 2, 5]`), not command strings;
  a spec that lists commands is fine, the task frontmatter is not. On `main` rung 0 checks only the
  working tree, so run the ladder before the final commit.
- **`Mimas.Core.Session.Session` shadows its own namespace** from outside it: alias it.
- **`dotnet run --no-launch-profile` means `ASPNETCORE_ENVIRONMENT=Production`**, which loads the origin
  allow-list; use `dotnet run --project server/Mimas.Server`, whose launch profile sets `Development`.
- **`MIMAS_WEB_PATH` must be an absolute path.**
- **`unity build` and `unity test` refuse while an Editor has the project open.** `unity command menu
  --path "Assets/Refresh"` then `recompile_status` is how a live Editor picks up new Core files and data;
  it regenerates `GameDataManifest` by itself and logs `[GameDataManifest] Rebuilt with N files`.
- **`unity command eval` prints every diagnostic**, ending with a spurious "Unreachable code detected";
  the real error is the first line. `FindFirstObjectByType` is obsolete-as-an-error there; use
  `FindAnyObjectByType`.
- **`capture_game_view --save_path` cannot write under any folder called `Temp`.** Save to `Assets/_Shots/…`.
- **`SaveProjectSettings()` does not persist in batch mode.** `WebBuild` applies the identity values on
  every build; what ships is what the script sets.
- **The client opens no socket until the player presses something.** A smoke uses `?room=ZZZZ`.
- **Build through the script, not the profile.** A Development Build does not link on this Editor.
- **`GUIUtility.systemCopyBuffer` does not reach the browser clipboard.** Use `WebClipboard`.
- **Headless Chromium is a software rasteriser.** Frame verdicts come from `FrameProbe`.
- **Rohan's shell is Windows PowerShell 5.1.** No `&&`; one command per line in anything he will paste.
- **The VPS is shared**; `sites-enabled/laststep.cloud` belongs to another product. nginx 1.24 syntax.
- **No secrets in the repo.** The VPS is reached only through the ssh alias `hostinger`.

## Last playtest

**21 Sep 2026, Rohan, browser vs bot** (`studio/playtests/2026-09-21-rohan.md`). Copy code (T-0005, done),
refused shots (T-0006, done), and whether a turn should end itself at 0 AP (OQ-N16). No frame-meter reading.

**Still unplayed by a human: two browsers against each other** — the whole of M2-1.

## The deadline

From `E:\Studios\Trinetra-Game-Studio\docs\PLAN.md` §10: if two browsers cannot play a full match through
the server by **Fri 2 Oct**, the 10 Oct playtest falls back to the local build and online moves to 17 Oct.
Nothing in the code puts that at risk. What is left is one evening with a second human.
