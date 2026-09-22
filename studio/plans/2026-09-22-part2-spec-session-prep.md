---
id: PREP-2026-09-22-B
title: Prep for the next session: write the full part-2 boons spec (T-0010) against the real Core
project: mimas
written: 2026-09-22
by: Fable, at the end of the T-0009 build session
for: the next Fable session (Rohan + Fable), a spec-writing session in the shape of docs/specs/2026-09-21-boons-groundwork.md
---

# Next session: turn the part-2 outline into a spec Opus can execute unattended

**Where things stand:** part 1 is built and at `verify` (`main` at `967b64c`; run report
`studio/runs/R-2026-09-22-T-0009.md`; ladder green 4/4; 445 Core tests). The sequence Rohan chose on
21 Sep: **Fable now writes the full part-2 spec against the real code, asking the outline's §5
questions with 3–4 options each; then T-0010 moves to `approved` and Opus executes it.**

## Read, in this order (and nothing else first)

1. `studio/STATE.md` — one page.
2. `docs/specs/2026-09-21-boons-in-game-outline.md` — the outline being expanded; §5 is the question list.
3. `docs/specs/2026-09-21-boons-groundwork.md` §0 and §12 — the shape the full spec must copy
   (preamble, commit order, forks with defaults, done-when mapped to rungs).
4. The Core as it is, in full: `shared/Mimas.Core/Runtime/Session/*.cs` (six files), `Match/PlayerBuild.cs`,
   `Match/MatchSetup.cs`, the reveal half of `Match/MatchState.cs` (search `RevealBoon`), `Match/PlayerView.cs`,
   `Protocol/Wire.cs` (`View` / `ReadView` and the event switch), `Units/Unit.cs` (`FromView`).
5. `docs/data.md` sections *Boons*, *Lineages*, *Session and draft*, *Match flow*; ADR-034, ADR-035.
6. Server: `server/Mimas.Server/Rooms/Room.cs` (the one `MatchState` per room, ADR-032's rematch loop),
   `Rooms/Seat.cs`, `Net/WsConnection.cs`, `Protocol/Messages.cs`; `docs/networking.md`.
7. Client: `MimasClient/Assets/_Game/Presentation/Match/MatchSession.cs` **only the three
   `_catalog.Abilities.TryGet` sites** (lines near 512, 782, 1090) and `OnlineMatchDriver`;
   `UI/LobbyView.cs` for where the lineage row and the draft go.

## What this build found that the outline does not know

| Finding | Where it lands in the spec |
|---|---|
| **The client reads raw ability defs in three places** (`MatchSession.cs`). Button costs and range bands must come from the mirror's `MatchState.ResolveAbility` or an Enchant shows the base number | Client section: "every number on a button or a band comes from the mirror's resolver"; a test or an Editor assertion that proves it |
| **`Mimas.Core.Session.Session` shadows its namespace.** The server aliases it (`using Session = Mimas.Core.Session.Session;` inside its own namespace block) or names it in full | Server section, one line, so Opus does not lose ten minutes |
| **Session events and `SessionView` are not on the wire.** `Wire.Event` throws on a `SessionEvent`; part 2 encodes `RoundStarted`, `RoundEnded`, `DraftStarted`, `DraftPicked`, `SessionEnded` and a `SessionView` (or folds them into `match.events` — that is outline §5 Q2) | Wire section; `ProtocolTests` round trips |
| **`RevealedEntry` ids are opaque strings** (`boon:` / `lineage:` prefixes inside). The server passes them through; nothing outside Core should parse them | Server section: the session owns the entries; a reconnect re-sends views, never entries |
| **The preview's "?" counts one per hidden boon** (decision 2 in the run report). The HUD's "?" row semantics do not change, but the number can be larger than before | Client section, one sentence, so the HUD copy is not "N hidden modifiers" |
| **`SessionEventFilter` needs the state that produced each event**; one `Session.Apply` can end a round and start the next, and a `RoundStartedEvent` is where the state switches | Server section: `Room.Broadcast` filters through `SessionEventFilter.ForPlayer`, never `EventFilter` directly |
| **Draft timeout is a command** (`DraftPickCommand(player, 0, Timeout)`); a player with zero offers is picked at once | Server section: the room clock gains a draft deadline from `rules.draft.timeoutMs`, submits the command like a turn timeout |
| **The bot has no lineage today** (`Room.cs` creates the bot seat without one). `RandomBot.ChooseDraft` keeps offer 0 | Server section: which lineage the bot prays to (outline Q4 territory; a seeded pick from the three is the smallest reading) |
| **The starting Blessing is appended by `Session`**, not by the room; a build handed to `SessionSetup` needs only gear + lineage | Server section: `room.loadout` gains `lineage`; the bad-loadout error covers an unknown lineage |
| **Redeploying ships the boons data with nothing reading it**; the content hash changes and `/health` reports it | Docs section: say so in the deploy runbook's "what changed" line |

## The question round (outline §5, plus two)

Ask with 3–4 options each, a recommendation first, in one round:

1. A seat that reloads during a draft: back to the draft within a grace, or the timeout pick? (Recommend: the
   reconnect grace already exists for a round; the draft deadline keeps running and a resume shows the draft.)
2. Session messages: new message types, or session events inside `match.events`? (Recommend: events inside
   `match.events` plus a `session` block beside `view` in every message — one filter, one view, one codec.)
3. The draft screen layout (after a mock, following the research-then-options workflow; three cards, a timer,
   the opponent's "picked" state) and where the series score lives in the HUD.
4. `Play vs bot`: a full best-of-3 or a single round by default? (Recommend: the best-of-3, because it is
   the only way Rohan plays the draft before 10 Oct.)
5. **New:** which lineage does the bot pray to — a fixed one, a seeded random one, or the same as the
   player's? (Recommend: seeded random from the three, so every lineage gets seen.)
6. **New:** does the Editor practice mode (`LocalMatchDriver`) run a `Session` too, or stay single-round?
   The outline folded practice mode into part 2; it decides whether the draft screen can be tested without
   a server.

## Traps to write into the spec's preamble

- The asset guard: the scratchpad is refused (`**/Temp/**`) by Bash *and* Write; test files and Editor-generated
  `.meta` files need `allows_assets` entries; the active task must be the only one `running`; flipping the
  task to `verify` before the last commit empties the allowance. All in `studio/STATE.md` "Things the next
  agent must not rediscover".
- The ladder wants rung numbers in the task's `ladder:`; part 2 adds rung 6 (EditMode) and the browser smoke.
- `unity build` / `unity test` refuse while the Editor is open; plan Editor work first, then close, then batch.
- Golden rule 2: scenes and UXML through the live Editor and `unity command`, never by hand.

## Still waiting on Rohan (carried over)

- The M2 playtest against a friend, with a rematch (M2-1).
- The `?perf=1` frame meter reading.
- Tuning the boon numbers if he wants to before part 2 (placeholders as the spec gave them).
- `pillars.md`; OQ-N03, N07, N11 for the board mechanics.

## How to open the next session

"Pick up the next task" is enough: STATE names it. If Rohan is present, open with the six questions above
in one message; if not, write the spec under the recommended defaults, mark each as a fork in §13, and
leave T-0010 at `draft` until he has answered.
