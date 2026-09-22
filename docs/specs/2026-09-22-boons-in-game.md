# Spec D, part 2: Boons in the game — the online best-of-3 with the draft over the board, and the presentation of boons and reveals

_Status: **work order for an autonomous Opus session** (task `T-0010`, lane full, depends on `T-0009`).
Written 22 Sep 2026 by Fable against `main` at `1e46ada` (part 1 landed and at `verify`, 445 Core tests,
47 server tests). The eight questions of the outline (`docs/specs/2026-09-21-boons-in-game-outline.md` §5
plus four found by the part-1 build) were answered by Rohan on 22 Sep 2026 (§2). Design anchors:
`#session`, `#round`, `#draft`, `#character-select`, `#lineage`, `#boons`, `#hidden-info`, `#bots`,
`#online`, `#presentation` (HUD, examine, lobby). Predecessors: `docs/specs/2026-09-21-boons-groundwork.md`
(part 1), `docs/specs/2026-09-17-online-slice.md` (the shape of the server and the client drivers),
`docs/specs/2026-09-21-m2-browser-finish.md` (the room outlives the match)._

---

## 0. How to run this session

Read this whole file, then `CLAUDE.md`, then `studio/STATE.md` ("Things the next agent must not rediscover"
is written for you), then the design sections named above (grep the anchors, never the whole page), then
`docs/networking.md`, `docs/data.md` (Boons, Lineages, Session and draft, Match flow), `docs/architecture.md`,
ADR-026, ADR-027, ADR-032, ADR-034, ADR-035. Read every file you touch in full before editing it, in
particular `shared/Mimas.Core/Runtime/Session/*.cs` (all six), `Match/PlayerView.cs`, `Match/MatchState.cs`
(`FromView`, `ResolveAbility`, `ResolveAbilityKnownTo`), `Protocol/Wire.cs`, `Protocol/Messages.cs`,
`Bots/RandomBot.cs`; `server/Mimas.Server/Rooms/Room.cs`, `RoomRegistry.cs`, `Seat.cs`, `RoomClock.cs`,
`Options/ServerOptions.cs`, `Net/WsConnection.cs`; `server/Mimas.Server.Tests/MatchTests.cs`, `RoomTests.cs`,
`MirrorPlayer.cs`, `FakeClient.cs`, `Loadouts.cs`, `MimasServerFactory.cs`;
`MimasClient/Assets/_Game/Net/NetClient.cs`, `Presentation/Match/IMatchDriver.cs`, `OnlineMatchDriver.cs`,
`LocalMatchDriver.cs`, `MatchSession.cs`, `IMatchHudSource.cs`, `MatchSettings.cs`, `UI/LobbyView.cs`,
`UI/Lobby.uxml`, `UI/Lobby.uss`, `UI/MatchHudView.cs`, `UI/MatchHud.uxml`, `UI/MatchHud.uss`.

Rules for the session:

1. **Autonomous.** No questions. Forks have defaults in §13. Balance numbers are placeholders in JSON and
   are not yours to tune; ship the nine boons of §5 as written and say so.
2. **Everything is in scope this time** — Core, server, client, data, docs — but each area is small and
   named. Every existing constructor keeps working; the 445 Core tests and 47 server tests stay green at
   every commit, with additions. Commit in the order of §12.
3. **Commits:** small commits to `main`, `area: what`. Do not push. Before every commit: `dotnet build
   Mimas.slnx`, `dotnet test shared/Mimas.Core.Tests`, `dotnet test server/Mimas.Server.Tests`, all green;
   after every client commit, `unity command menu --path "Assets/Refresh"`, then `unity command
   recompile_status` → `completed` with no errors, then `unity command console` clean.
4. **Golden rules** 3, 4, 5 for everything under `shared/`: no `UnityEngine`, no floats, no `DateTime`, no
   `Guid`, no dictionary order in anything that decides a result, C# 9, no `record`. Every number that
   could ever be tuned is in JSON. **No clock in Core**: the draft timeout is a `DraftPickCommand` with
   `reason: timeout` submitted by the host, as part 1 built it.
5. **Design first, no drift.** Everything here is `decided` on the design page or was decided by Rohan on
   21 and 22 Sep 2026 (§2). §10 lists the design-page edits that record those decisions; the task declares
   `docs/design/index.html` in `allows_assets`. Where this spec is silent, add an open question to the
   design page and pick the smallest reading; never invent a rule.
6. **The live Editor, and what is edited by hand.** `.unity`, `.prefab`, `.asset` and every `.meta` are the
   Editor's (golden rules 1 and 2): the one scene edit this spec needs (§7.7, assigning a field on
   `LobbyView`) goes through `unity command`, and new `MatchSettings` fields take their values from code
   defaults so `DefaultMatchSettings.asset` is never touched. `.uxml`, `.uss` and `.cs` under
   `MimasClient/Assets/_Game/` are text and are edited by hand, as every client task before this one did.
   After adding any file under `Assets/` (a `.cs`, a `.json`), run `Assets/Refresh`, wait for
   `recompile_status` → `completed`, and commit the Editor-generated `.meta` (and the regenerated
   `GameDataManifest.asset` for data) **with** the file. Each such `.meta` is declared in the task's
   `allows_assets`; if you create a file this spec did not foresee, add its `.meta` there before committing.
7. **`unity build` and `unity test` refuse while an Editor has the project open.** Do all Editor work first
   (§7, §11 steps 1–3), close the Editor, then run EditMode tests and the Web build in batch.
8. **Asset guard.** It scans the whole command text: a read-only `grep`/`cat`/`ls` over
   `docs/design/index.html`, `studio/ledger.json`, `studio/game.yaml`, `.claude/**`, `**/*.meta` or
   `**/Mimas.Core.Tests/**` is refused. Use the Read/Grep/Glob tools for those. **The session scratchpad
   is refused too** (its path contains `Temp`): write generated files straight to their destination.
   The active task is the single task file with `status: running`; set `T-0010` to `running` first and do
   not flip it to `verify` before the last commit (that empties the allowance). Never rephrase a command
   that writes. Note each false positive in the run report.
9. **Run report first.** `studio/runs/R-<date>-T-0010.md` from the studio template, opened before the
   first edit, appended to as you go. The ladder for the task is rungs `[0, 1, 2, 5]` in the task
   frontmatter (numbers, not strings); run `node E:/Studios/Trinetra-Game-Studio/tools/ladder/ladder.mjs
   --project mimas --task T-0010` before the final commit, on the working tree.
10. **No scope creep.** Out of scope: a character-select screen (the room's preset + lineage row is the
    launch shape), tiers/rarity/rerolls, presets on the server, a third map, a balance simulation, the
    board mechanics of `mechanics.xlsx`, a `draft.state` message type, chat, spectating, accounts, any
    change to `rules.json` numbers, any effect type the vocabulary does not have.

---

## 1. Goal and result

**Goal.** A player prays to a lineage in the room, plays a best-of-3 against the other seat with a draft
of three boons between rounds, and sees the boons: their own in the examine panel and on the action bar,
the opponent's as "?" rows until a reveal names the god. Play vs bot plays the same series. Practice mode
in the Editor runs the same session offline, so the draft screen can be seen without a server.

**Result, in the player's words:**

- In the room, under the preset, three lineage buttons (Greek, Norse, Hindu) with a line each; the one you
  pick is remembered. Ready needs both a preset and a lineage. The other seat never sees your choice.
- Round 1 starts as today, with "ROUND 1" on a card for a beat and `ROUND 1 · 0 – 0` in the turn panel.
- A round ends with `ROUND 1 LOST · 0 – 1` (or WON); two seconds later the board dims and three cards
  drop in with a 20 s bar: kind, name, god, what it does, which of your items it lands on. One click
  selects, Confirm keeps it; the bar running out keeps the first card. A green dot says the opponent has
  picked. When both have picked, the Arena reloads on the next map: `ROUND 2`, the loser moves first.
- The series ends with `VICTORY` / `DEFEAT`, `series 2 – 1 · by elimination` (or `opponent resigned`,
  `opponent left`, `you resigned`), then **Back to room**: same code, both seats, Ready reset, preset and
  lineage editable. Ready again is a new series.
- Resign concedes the **series**. A seat gone for the 60 s grace, in a round or in a draft, forfeits the
  series. A reload during a draft comes back into the draft with the seconds that are left.
- Examine on your hero: a `Boons` list (name, kind, god) and your lineage under the name. On the enemy:
  `Unknown lineage` and one `?` row per boon they hold, replaced by the name and god as reveals happen; a
  flyover says `Revealed: Agni's Crown` with `Hindu · Enchant` under it, and the lineage tag appears on the
  enemy's nameplate. A Sigil's ability sits under its item on the action bar with the item's mark; an
  Enchant-changed cost, range or damage is what the button shows, with a small mark; the range circles
  already use the resolved numbers.
- Play vs bot: the bot prays to a lineage drawn at random, drafts, and plays the full best-of-3.
- Practice mode (open the Arena scene in the Editor): the same series, the bot on seat 1, the draft
  overlay working with no server.
- Nine more boons, three per lineage, so every draft offers a real choice in every kind.

**Not in the result:** tiers, rerolls, a character-select screen, presets stored on the server, the
opponent's preset in the room.

---

## 2. Decisions locked in the question rounds

### 22 Sep 2026 (this spec's round, mock `artifacts/draft-screen-mocks.html`)

| # | Question | Decision |
|---|---|---|
| P1 | Where the draft happens | **Over the dimmed board, in the Arena scene** (mock option A). The round banner shrinks to a score line, the board dims, the three cards drop in with the timer, the opponent's picked dot and Confirm. No trip to the lobby; the next round reloads the Arena for its map. A reload mid-draft lands in the Arena with the cards |
| P2 | Where the series score lives | **In the turn panel** (mock option a): a line `ROUND 2 · 0 – 1` above the turn owner. One line added to a panel that exists |
| P3 | Wire shape | **Everything through the existing channels.** The five session events are encoded in `events`; a `session` block sits beside `view` in `match.start`, `match.events`, `match.view`, `match.rejected`; `DraftPickCommand` is a command type `draftPick` sent through `match.command`. No new message types. One filter, one view, one codec |
| P4 | A reload or drop during the draft | **The draft clock keeps running; a resume shows the draft** with the seconds left. Past the deadline the timeout picked offer 0 and the next round has started. The 60 s reconnect grace runs across the draft; its expiry forfeits the **session** (`#session` rule 7) |
| P5 | Play vs bot | **The full best-of-3** with the bot drafting (offer 0, after its think delay) |
| P6 | The bot's lineage | **Drawn at random from the three** with a seed the room draws at creation, so every lineage gets seen and a log line names it. The two dev passives (`ward-of-feathers`, `stone-skin`) retire; the bot's Blessing is its lineage's |
| P7 | Editor practice mode | **Runs a `Session` too**: `LocalMatchDriver` hosts `Mimas.Core.Session.Session` with the bot on seat 1; `DefaultMatchSettings` gains a lineage per side; the draft overlay and the round flow are testable in the Editor |
| P8 | What Resign concedes | **The series.** Resign ends the session: the opponent wins, the banner says so, back to the room. A disconnect past the grace does the same. Core gains a session-level end with a reason |

### Carried from 21 Sep 2026 (part-1 spec §2, unchanged)

D1 the room outlives the match (ADR-032); D5 one flat 30 s turn online; D10 symmetric draft, counts in
data; D11 Enchants reveal on the contradicting observation; D13 round 1 by coin flip then the loser first;
D15 the three starting Blessings; D17 the ladder wraps; D18 no tiers, rerolls or cap at launch. The
outline's own decisions stand: the lineage is chosen in the room under the preset, after gear; the bot
seat is created ready; presets stay client data.

---

## 3. Architecture (read before the sections that follow)

**The room hosts a `Session` the way it hosted a `MatchState`.** `Room.Session` is non-null exactly while
`Phase == Playing`; the room's own life stays `Waiting → Playing → Waiting` (ADR-032), and one `Playing`
is now one whole best-of-3. Inside it the session's phase is `Round → Draft → Round → … → Over`. The
room never reads the session's rules: it forwards commands, arms clocks, submits the timeouts and
forfeits **as commands**, filters events per seat through `SessionEventFilter.ForPlayer` and sends
`SessionView.For(session, seat)` beside the round's `PlayerView`. Nothing but a view for one seat or the
events filtered for that seat ever leaves `Room.Broadcast` (golden rule 6).

**Two counters, two names.** `Room.Series` (was `Round`) counts the sessions played in the room's life,
1 for the first; `Session.Round` counts rounds inside one session, 1..3. On the wire `match.start` carries
both: `series` and `round`. ADR-032's `round` field is superseded (ADR-036).

**One `match.start` per round.** Any event batch that contains a `RoundStartedEvent` goes out as
`match.start` (with `view`, `session`, `clock`, and the whole filtered batch as `events`); every other batch
goes out as `match.events`. Round 1's `Session.Start()` returns `[RoundStarted, TurnStarted, …]`, which is
exactly what the room sent before. The client's rule is as simple: a `match.start` whose `round` is not
the one on screen **reloads the Arena scene**, and the fresh scene consumes it as it consumes any pending
start today (`NetClient.PendingMatch`). A `match.start` with the same `round` is a reconnect, as today.

**The draft is a state of the Arena scene, not a screen of its own** (P1). Between rounds `view` is null
on the wire, the driver's `Rules` is null, the presenter keeps the board it has (dimmed) and draws the
draft overlay from the `session` block. A reload during a draft receives `match.start` with `view: null`,
`session.phase: "draft"` and `mapId` = the next round's map; the presenter builds that board dimmed and
shows the overlay. When the second pick starts the next round the server sends the next `match.start` and
the scene reloads.

**One clock shape, two modes.** `clock { activePlayer, turnMs, remainingMs }` is unchanged in a round.
In a draft it is `{ activePlayer: -1, turnMs: rules.draft.timeoutMs, remainingMs }`: one deadline for both
seats, armed when the `DraftStartedEvent` goes out; the client re-anchors on every message exactly as it
does for a turn.

**Practice mode is the same machine.** `LocalMatchDriver` hosts a `Session` and the same `RandomBot`; the
session survives the Arena reload between rounds inside a persistent `LocalSessionHost` object, the way
`NetClient` carries the pending start online. The presenter does not know which driver it has (ADR-028).

**Every ability number on the client comes from the mirror's resolver** (ADR-034). The three
`_catalog.Abilities.TryGet` sites in `MatchSession` become `Rules.ResolveAbility` /
`Rules.ResolveAbilityKnownTo`, and an EditMode test scans the source to keep it so.

```
Room.Phase:   Waiting ──both Ready──▶ Playing ──SessionEnded──▶ Waiting
                                        │
Session.Phase:                 Round ──MatchEnded──▶ [RoundEnded] ──score < roundsToWin──▶ Draft ──both picked──▶ Round …
                                        │                            └──score == roundsToWin──▶ Over
                                        └──Resign/Forfeit (round or draft)──▶ Over
```

---

## 4. Core changes (`shared/Mimas.Core/Runtime`)

Small, and all in `Session/` and `Protocol/`. No change to `MatchState`, `Unit`, the overlay or the
reveal rules.

### 4.1 The session ends on a resign or a forfeit (P8)

- New enum `SessionEndReason { Score = 0, Resign = 1, Forfeit = 2 }` in `Session/SessionEvent.cs`.
- `SessionEndedEvent` gains `Reason` (constructor `(winner, score0, score1, reason)`; `ToString` names it).
  Every existing caller passes `Score`.
- `Session.EndRound`: after the `RoundEndedEvent`, if `ended.Reason` is `Resign` or `Forfeit`, the session
  ends at once — `Phase = Over`, `Winner = ended.Winner`, `SessionEndedEvent(Winner, _score[0], _score[1],
  Resign / Forfeit)` — whatever the score. The resigned round **is** scored first (the round ended, the
  `RoundEndedEvent` says how). The elimination path is unchanged.
- `Session.Validate` in `SessionPhase.Draft`: a `ResignCommand` whose `Player` is 0 or 1 is `Accepted`
  (everything else in a draft still needs a `DraftPickCommand`). `Session.Apply` of a `ResignCommand` in a
  draft: no round is scored; `Phase = Over`, `Winner = 1 − player`, the offers are cleared, one
  `SessionEndedEvent` with `Resign` (reason `Player`) or `Forfeit` (reason `Disconnect`).
- `Session.EnumerateLegal` in a draft: unchanged (the bot never resigns; a resign is never "legal to
  choose", as `MatchState` treats it today — check and mirror what `MatchState.EnumerateLegal` does with
  `ResignCommand`).

### 4.2 The session view knows the next map and the target

`SessionView` gains:

- `string NextMapId` — the map the next round plays (`session.Ladder[session.Round % session.Ladder.Count]`),
  null when `IsOver`. What a reconnect during a draft builds its dimmed board from, and the "Round 2 on
  Board" line.
- `string MapId` — the current round's map when a round runs (`Match.MapId`), else `NextMapId`.
- `int RoundsToWin` — from `session.RoundsToWin`, for the score line's pips if a HUD ever wants them.
- `public static SessionView Create(...)` — a factory from parts, the twin of `PlayerView.Create`: what
  `Wire.ReadSession` calls, and the only way a client obtains one. `For(session, viewer)` stays the only
  path from a `Session`.

### 4.3 Wire (`Protocol/Wire.cs`, ADR-027 shape: hand-written, enums as lower-camel strings, read
case-sensitively, unknown = `WireException`)

Commands:

| `type` | Fields | Class |
|---|---|---|
| `draftPick` | `player`, `offerIndex`, `reason` (`player` / `timeout`) | `DraftPickCommand` |

Events (the five `SessionEvent`s; `Wire.Event` currently throws on them):

| `type` | Fields |
|---|---|
| `roundStarted` | `round`, `mapId`, `firstPlayer` |
| `roundEnded` | `round`, `winner`, `reason` (the existing `MatchEndReasonName`), `score` = `[score0, score1]` |
| `draftStarted` | `round`, `offers0` (array of ids **or `null`**), `offers1` (same) — after `SessionEventFilter` one side is null, and null it stays |
| `draftPicked` | `player`, `boonId` (string or `null`), `reason` (`player` / `timeout`) |
| `sessionEnded` | `winner`, `score` = `[score0, score1]`, `reason` (`score` / `resign` / `forfeit`) |

The session view, `Wire.Session(SessionView)` → `JObject` and `Wire.ReadSession(JObject, PlayerView match)`
→ `SessionView` (the round's view is **not** nested; it stays the `view` field beside, and the reader is
handed it):

```json
{ "viewer": 0, "round": 2, "phase": "draft", "score": [1, 0], "roundsToWin": 2, "isOver": false, "winner": -1,
  "mapId": "board-3", "nextMapId": "board-3",
  "myBuild": { "loadout": { "weapon": "longbow", "crown": "ember-circlet", "boots": "leaping-boots", "armour": "leather-jerkin" },
               "lineage": "hindu", "boons": ["vayu-breath", "agni-crown"] },
  "opponentLineage": null, "opponentBoons": [ { "id": null }, { "id": "thor-vigour" } ],
  "myOffers": ["agni-warmth", "agni-crown", "vayu-wings"], "iHavePicked": false, "opponentHasPicked": true }
```

`phase` is `round` / `draft` / `over`. `winner` is `-1` while the session runs. A hidden opponent boon is
`{ "id": null }` and stays null through `ReadSession` (the same rule as a hidden entry in a view).
`Wire.Encode` stays deterministic (field order fixed, no dictionaries).

### 4.4 Nothing else

`Draft`, `SessionEventFilter`, `DraftPickCommand`, `RandomBot`, `PlayerBuild`, `MatchSetup`, `MatchState`
are untouched. `MatchSetup.WithModifier` stays (tests use it); its two callers outside Core go (§6.6, §7.3).

---

## 5. Data changes (`MimasClient/Assets/_Game/Data/`)

Nine boons, one per kind per lineage, and one ability. Numbers are placeholders in the same spirit as
part 1's; Rohan tunes after playing. Names follow `<God>'s <thing>` (`Hermes' Heels` and `Zeus' Favour`
already use the bare apostrophe after an s, so a card's "god" is the name up to the first apostrophe).
No schema changes; every effect below is in the vocabulary of `docs/data.md` and uses only fields that
part 1 built (`stat`, `abilityOverride` on `range` / `damage` / `cost`, `grantAbility`).

| File | Kind | Lineage | `requires` | `effects` | Notes |
|---|---|---|---|---|---|
| `boons/hera-resolve.json` | blessing | greek | — | `stat ap +1`, `stat hp −4` | "Hera's Resolve": one more action every turn, at a cost. The AP Blessing reveals at round start (§6.5 rule of part 1) |
| `boons/hermes-sandals.json` | enchant | greek | `{ slot: boots }` | `abilityOverride target boots, field range, amount +1` | "Hermes' Sandals": every movement the boots grant reaches one further. `exclusive: []` |
| `boons/nike-jab.json` | sigil | greek | `{ slot: weapon }` | `grantAbility target weapon, ability jab` | "Nike's Jab": the shipped `jab` (weapon attack, cost 1) which no item grants |
| `boons/thor-might.json` | blessing | norse | — | `stat power.weapon +1`, **`stackable: true`** | "Thor's Might": the first shipped stackable boon; may be drafted again |
| `boons/tyr-edge.json` | enchant | norse | `{ slot: weapon }` | `abilityOverride target weapon, field damage, amount +1` | "Tyr's Edge": every weapon attack hits 1 harder; reveals through the damage line |
| `boons/tyr-strike.json` | sigil | norse | `{ slot: weapon }` | `grantAbility target weapon, ability strike` | "Tyr's Strike": the shipped `strike` (weapon attack, cost 2) |
| `boons/hanuman-heart.json` | blessing | hindu | — | `stat hp +3`, **`stackable: true`** | "Hanuman's Heart" |
| `boons/vayu-haste.json` | enchant | hindu | `{ slot: weapon }` | `abilityOverride target weapon, field cost, amount −1` | "Vayu's Haste": weapon shots cost one less (floored by `rules.boons.minCost`); reveals through the cost observation |
| `boons/indra-storm.json` | sigil | hindu | `{ slot: crown }` | `grantAbility target crown, ability storm-bolt` | "Indra's Storm": a new spell |
| `abilities/storm-bolt.json` | attack, `category: spell` | — | — | `cost 2, damage 3, range 4, minRange 1, trajectory direct, lineOfSight true, elements ["lightning"]`, `icon: storm-bolt` | Copy `zeus-bolt.json`'s shape and change the numbers and element; a crown Sigil grants `spell` attacks (lane rule) |

Each lineage's `pool[]` gains its three ids. Descriptions, one line each, in the JSON, in the voice of
the existing ones ("Hera steels you: one more action every turn, four fewer Health"). Every `.json` needs
its `.meta` and the regenerated `GameDataManifest.asset` from the Editor (§0.6).

Loader consequences to check, not to work around: every lineage still holds all three kinds and still
covers every item (`ShippedLineages_CoverEveryItem`); `nike-jab` and `tyr-strike` grant abilities no item
already has; `vayu-haste`'s `cost` applies to the weapon's attacks; `hermes-sandals`' `range` applies to
`jump` and `teleport`; the repo-data test that enumerates shipped ability and modifier ids grows by
`storm-bolt` (that is the assertion's job, as part 1 noted). `docs/data.md` "Shipped:" lines gain them.

---

## 6. Server (`server/Mimas.Server`)

### 6.1 The seat prays to a lineage

- `Seat.LineageId` (`string?`), cleared with the rest. `Seat.ToString` unchanged.
- `room.loadout { loadout, lineage, ready }`. `RoomRegistry.HandleLoadout`: after the four items,
  `p.Value<string>("lineage")`; missing or empty → `bad_loadout` "A lineage is required: one of greek,
  norse, hindu." (list the catalogue's ids, sorted); unknown → `bad_loadout` with the catalogue's message
  (`_catalog.GetLineage(id)` throws; catch what `GetItemForSlot`'s catch already catches). Nothing is
  stored until everything validates. `Room.SetLoadout(player, loadout, lineage, ready)`.
- `room.state` does **not** carry the lineage (hidden, `#lineage` rule 3, and `q-online-room-loadout`).
- The bot seat: at `Room.Open(vsBot: true)`, `LineageId = _options.BotLineageId ?? PickBotLineage()`.
  `PickBotLineage` draws a `RoomSeed` once (`RandomNumberGenerator.GetInt32(1, int.MaxValue)`, stored on
  the room) and picks `catalog.Lineages.All` sorted by id at `new Mimas.Core.Rng(RoomSeed).Range(0, count)`.
  The log line names it: `room {Code} opened by {Player} vs bot ({Lineage})`.

### 6.2 The room hosts the session

`using Session = Mimas.Core.Session.Session;` **after** the file-scoped `namespace Mimas.Server.Rooms;`
line (the class shadows its namespace from outside it; STATE says so).

- `Room.Session` (`Session?`) replaces `Room.State`; keep a `State` property = `Session?.Match` for the
  places that only need the round (the tick's active seat, `Reject`). Non-null exactly while `Playing`.
- `Room.Round` → `Room.Series` (rematch counter, ADR-032); the log lines say `series {Series} round
  {Session.Round}`.
- `StartIfBothReady`: both seats need `Loadout`, `LineageId` and `Ready`. Seed from `RandomNumberGenerator`
  as today; **no room-side coin flip** (the session flips its own); `new SessionSetup(new
  PlayerBuild(seat0.Loadout, seat0.LineageId), new PlayerBuild(seat1.Loadout, seat1.LineageId))`;
  `Session = new Session(_catalog, setup, seed)`; `_bot = new RandomBot(seed ^ 0x9E3779B9)` when a seat is
  a bot; `Series++`; `_seq = 0`; `IReadOnlyList<MatchEvent> started = Session.Start()`; arm the turn clock;
  `SendMatchStart(seat, started)` for each human seat; `StartTicking`. The `firstPlayer` log value comes
  from the `RoundStartedEvent` in `started`.
- `HandleCommand`: `Wire.ReadCommand` (which now reads `draftPick`); the seat check as today;
  `Session.TryApply(command, _scratch, out result)`; refusal → `Reject` (whose payload may now carry
  `view: null`); success → `Broadcast(_scratch)`.
- `Broadcast(events)`: `_seq++`; per human seat `_filtered.Clear(); SessionEventFilter.ForPlayer(events,
  seat.Index, Session, _filtered)` — **never `EventFilter` directly** (the session filter picks the state
  that produced each event; one `Apply` can end a round and start the next). Then **the split rule**: if
  `events` contains a `RoundStartedEvent`, send `match.start` (§6.4 payload, `events` = the filtered
  batch); else send `match.events`. Clocks: the last `TurnStartedEvent` in the batch arms the turn clock
  when `Session.Phase == Round`; a `DraftStartedEvent` arms `_draftDeadline = now + DraftTimeoutMs` and
  `_botDueAt = now + BotThinkMs`. After sending: if `Session.IsOver` → log the `SessionEndedEvent`
  (`winner`, `score`, `reason`) → `ReturnToWaiting()`. A round ending without ending the session logs the
  `RoundEndedEvent` and returns to waiting **nothing**.
- `ReturnToWaiting`: as today, plus `Session = null`; seats keep `Loadout` **and** `LineageId`.
- `SendMatchStart(seat, events)`: `events` null on a reconnect (as today).
- `HandleResync` and `Reject`: payloads per §6.4; `view` is `null` in a draft, never omitted.
- `Reattach` in `Playing`: `SendMatchStart(seat, null)` works for a draft too (the payload rule).
- `OnConnectionClosed` in `Playing`: unchanged (`DisconnectedAt`, `TellOpponentAbout`) — in a draft as well.

### 6.3 The tick

```
lock: if Session == null || Phase != Playing → return
switch Session.Phase:
  Round:  as today, with State = Session.Match:
          1. the active human seat's turn clock → Apply(EndTurnCommand(active, Timeout))
          2. the bot, when due → Apply(_bot.Choose(State, bot) ?? EndTurn)
          3. graces → Apply(ResignCommand(seat, Disconnect))
          after every Apply: if Session == null || Session.Phase != Round → return
  Draft:  1. now >= _draftDeadline → for each human seat with !Session.HasPicked(seat): Apply(DraftPickCommand(seat, 0, Timeout))
          2. the bot, when due and !HasPicked → Apply(_bot.ChooseDraft(Session, bot))
          3. graces → Apply(ResignCommand(seat, Disconnect))   // accepted in a draft since §4.1; ends the session
          after every Apply: if Session == null || Session.Phase != Draft → return
  Over:   return (ReturnToWaiting already ran)
```

The draft deadline has no lag grace (nobody is racing a move) — `Expired` is `now >= _draftDeadline`.
`Apply` is the existing private helper (`TryApply` + `Broadcast`, warning on refusal).

### 6.4 Payloads (the wire reference `docs/networking.md` is rewritten from this table)

| `t` | Payload |
|---|---|
| `room.loadout` (c→s) | `{ loadout: { weapon, crown, boots, armour }, lineage, ready }` |
| `match.command` (c→s) | `{ matchId, command }` — `command` may now be `{ type: "draftPick", player, offerIndex, reason: "player" }` |
| `match.start` | `{ matchId, series, round, seq, mapId, youAre, opponentName, view, session, clock, events }` — `series` counts sessions in the room (1 for the first), `round` the round in the session; `view` is `null` in a draft; `mapId` is the round's map, or the next round's in a draft; `events` is the filtered batch that started the round (a `roundStarted` and a `turnStarted` at least; the preceding `draftPicked` too from round 2), empty on a reconnect |
| `match.events` | `{ matchId, seq, events, view, session, clock }` — `view` `null` between rounds |
| `match.rejected` | `{ matchId, reason, view, session, clock }` |
| `match.view` | `{ matchId, seq, view, session, clock }` |
| `clock` | `{ activePlayer, turnMs, remainingMs }` — in a draft `activePlayer: -1`, `turnMs: rules.draft.timeoutMs` (or the option), `remainingMs` to the draft deadline |
| `session` | §4.3's object, `Wire.Session(SessionView.For(Session, seat.Index))` |

Unchanged: `auth.*`, `room.create`, `room.join`, `bot.play`, `room.leave`, `room.state`, `room.left`,
`opponent.status`, `match.resync`, `ping`/`pong`, `error` and every error code (`bad_loadout` covers the
lineage). `seq` semantics unchanged.

### 6.5 Options (`ServerOptions`)

- `int? DraftTimeoutMs` — null uses `rules.draft.timeoutMs`; tests override it.
- `string? BotLineageId` — null draws at random (§6.1); tests that want a known reveal set it.
- **Remove** `BotModifierIds` (P6). `BotLoadout`, `BotName`, `BotThinkMs` stay.

### 6.6 Tests (`server/Mimas.Server.Tests`, xUnit, over real sockets)

`Loadouts.Ready(loadout)` and `NotReady` gain a `lineage` parameter with a default (`"greek"` for Bow,
`"norse"` for Gun — add `Loadouts.ReadyWith(loadout, lineage)`), so every existing test compiles and
passes with lineages. `MirrorPlayer` learns the session: `Absorb` reads `session` too (`Wire.ReadSession`
with the view or null); `IsDrafting`; `Choose()` in a draft returns `DraftPickCommand(Seat, rng over
myOffers)` or null once picked; `PlayToTheEndAsync` loops on `match.events` / `match.start` until
`session.isOver`, resending on every `match.start` a fresh mirror. New file `SessionTests.cs`; new tests:

- `Loadout_WithoutLineage_BadLoadout`, `Loadout_UnknownLineage_BadLoadout` (in `RoomTests`).
- `TwoHumans_PlayABestOfThree_WithADraftBetweenRounds` — the whole series with `MirrorPlayer`s; asserts a
  `roundEnded`, a `draftStarted` whose `offers<other>` is null, `draftPicked` for the opponent with
  `boonId: null`, a second `match.start` with `round: 2` and a `mapId` different from round 1's
  (`board-3` after `arena-4`, ladder order), `session.score` consistent on both sides, a final
  `sessionEnded` with `reason: "score"`, then `room.state` with the same code; every `view` and every
  `session.viewer` belongs to its seat (`AssertEveryViewBelongsTo` extended to `session`).
- `Draft_Timeout_ServerPicksFirstOffer` — `DraftTimeoutMs: 300`; nobody picks; `draftPicked { reason:
  "timeout" }` for both and the next `match.start`.
- `Draft_Reconnect_ResumesIntoTheDraft` — drop the socket during the draft, `auth.resume` → `match.start`
  with `view: null`, `session.phase: "draft"`, `myOffers` present, `clock.activePlayer: -1`.
- `Draft_DisconnectBeyondGrace_ForfeitsTheSession` — `sessionEnded { reason: "forfeit" }`, the other
  seat wins, `room.state` follows.
- `Resign_InRound_EndsTheSeries` — `roundEnded { reason: "resign" }` then `sessionEnded { reason: "resign" }`.
- `Resign_InDraft_EndsTheSeries` — `sessionEnded { reason: "resign" }`, no `roundEnded`.
- `HiddenInfo_SessionBlockNeverCarriesTheOtherSeatsOffersOrPick` — sweep every message both seats
  received: `session.myOffers` only ever lists that seat's, `opponentBoons` ids are null unless a
  `boonRevealed` to that seat preceded them, `draftPicked.boonId` is null for the other player.
- `BotPlay_PlaysABestOfThree_AndDrafts` — the bot room to `sessionEnded`; `opponentBoons.Count` grows by
  one after the draft; with `BotLineageId: "norse"` the hp Blessing (`thor-vigour`, hp at round start)
  arrives as a `boonRevealed` in round 1's start events and `session.opponentLineage` becomes `norse`.
- `Match_TwoRoundsInOneRoom_BothComplete` → becomes `Match_TwoSeriesInOneRoom_BothComplete` (`series`
  1 then 2).

Ratchet: `server_test_count` grows from 47 by at least nine.

---

## 7. Client (`MimasClient/Assets/_Game`, presentation only)

### 7.1 `NetClient`

- `LineagePref = "mimas.lineage"` beside `NamePref`.
- `OnMatchStart` unchanged in behaviour (`PendingMatch`, `CurrentMatchId`); its log line names `series`
  and `round`.
- Nothing else: the session is the driver's business.

### 7.2 `IMatchDriver`

Add:

```csharp
/// The session as this seat sees it. Never null once Ready. Rules and View are null between rounds.
SessionView Session { get; }
/// Sends a draft pick; false when refused here (no draft, already picked, bad index).
bool SubmitDraftPick(int offerIndex);
/// A match.start for a later round arrived: the presenter reloads the scene; the fresh one consumes it.
event Action NextRound;
```

`Ready` means "there is a session"; `Rules` / `View` may be null in a draft and after the series. Every
presenter path that touches `Rules` or `View` null-checks (`IsMyTurn`, `CanEndTurn`, `Actions`,
`RefreshView`, hover, click).

### 7.3 `OnlineMatchDriver`

- `Adopt(p)`: `view` null → `_view = null; _rules = null`; `session` → `_session = Wire.ReadSession(...,
  _view)`; `_matchOver = _session.IsOver`. The clock block is read as today (one shape).
- `Ready => _session != null`. `CanResign => Ready && !_matchOver && !_lost` (a resign in a draft is legal).
- `MatchStart` handling: `int round = p.Value<int>("round")`; if `round != _round` (first message sets
  `_round`), raise `NextRound` and return (the scene will reload and a new driver will be built from
  `NetClient.PendingMatch`); else the reconnect path as today.
- `Submit(command)`: a `DraftPickCommand` is checked against `_session` (phase `Draft`, `!IHavePicked`,
  index in `MyOffers`) and sent through `match.command`; anything else needs `_rules` and goes as today.
  `SubmitDraftPick(i)` = `Submit(new DraftPickCommand(LocalPlayer, i))`.
- `Resign()` sends `ResignCommand` in a draft as well.

### 7.4 `LocalMatchDriver` and `LocalSessionHost` (P7)

- New `Presentation/Match/LocalSessionHost.cs`: a `MonoBehaviour` created once (`DontDestroyOnLoad`,
  static `Instance`) that owns the `Session`, the `RandomBot`, the two clocks (turn and draft) and the
  filtered-events plumbing, so it survives the Arena reload between rounds. `LocalMatchDriver` becomes the
  per-scene adapter over it: if `LocalSessionHost.Instance` exists and its session is not over, adopt it;
  else create one from `MatchSettings`.
- Construction: `new SessionSetup(new PlayerBuild(settings.PlayerLoadout.ToLoadout(), settings.PlayerLineage),
  new PlayerBuild(settings.OpponentLoadout.ToLoadout(), settings.OpponentLineage))`;
  `new Session(catalog, setup, settings.Seed)`; `RandomBot(settings.Seed ^ 0x9E3779B9u)`.
- `Begin()`: `Raise(session.Start())` on a fresh session; on an adopted one, `Raise` nothing and
  `Resynced` (the scene came up mid-session).
- `Raise(events)`: `SessionEventFilter.ForPlayer(events, Local, session, _filtered)`; `_state =
  session.Match` (null in a draft); `_view = _state?.ViewFor(Local)`; `Session = SessionView.For(session,
  Local)`; `ReadClock` also handles `DraftStartedEvent` (start the draft clock from
  `catalog.Rules.Draft.TimeoutMs`) and `RoundStartedEvent` (after round 1: raise `NextRound` so the scene
  reloads and adopts the host).
- `Tick`: in a round as today (turn clock, idle penalty, bot); in a draft: the draft clock → `Submit(new
  DraftPickCommand(Local, 0, Timeout))` on expiry; the bot after `OpponentThinkSeconds` →
  `_bot.ChooseDraft(session, Bot)`.
- `CanResign` stays `false` in practice (the design's line; restarting the scene is the same thing).
- `MatchSettings`: `PlayerLineage = "hindu"`, `OpponentLineage = "norse"` (`[Tooltip("lineages/*.json id")]`,
  defaults in code so the asset is untouched); **remove** `PlayerModifierIds` and `OpponentModifierIds`
  (P6; the Blessing replaces them); `FirstPlayer` keeps its field with the tooltip "Unused since part 2 of
  boons: the session flips its own coin (design #round rule 2)". Nothing is hand-edited in
  `DefaultMatchSettings.asset`; a stale serialized field is harmless.

### 7.5 `MatchSession` (the presenter)

- `BeginMatch`: after the driver is ready, `string mapId = _driver.Session.MapId` (the round's map, or the
  next one in a draft); `_board.Build(mapId)`; unit views spawn only when `_driver.View != null`; a draft
  on arrival (reconnect) dims the board and opens the overlay (§7.6).
- **The three catalogue lookups** (`RefreshActions` near line 512, `PlayAttack` near 782,
  `AddAbilityEntries` near 1090) become `Rules.ResolveAbility(unit, id, out def)` for the local unit and the
  attack's attacker, and `Rules.ResolveAbilityKnownTo(LocalPlayer, unit, id, out def)` for the examine
  panel's enemy entries. A def the mirror cannot resolve (a hidden enemy ability that has not been used)
  keeps the "Unknown ability" row. `_catalog.Abilities` is not referenced in `MatchSession.cs` after this
  (§7.9 proves it; `_catalog.Boons`, `_catalog.Lineages`, `_catalog.Modifiers`, `_catalog.Items` stay
  legitimate for names and icons).
- Events (`Play`):
  - `RoundStartedEvent` → `SeriesLine = "ROUND {n} · {s0} – {s1}"` (own score first: `"ROUND 2 · 0 – 1"`
    reads `you – them`), a round card (`Banner = "ROUND {n}"`, `BannerDetail = "{map name} · you move first"`
    / `"… · opponent moves first"`) that clears itself after `RoundCardSeconds = 1.5`.
  - `MatchEndedEvent` → `Disarm()` only (no banner).
  - `RoundEndedEvent` → `Banner = "ROUND {n} WON"` / `"LOST"`, `BannerDetail = "{s0} – {s1} · by elimination"`
    (or resigned / left); **no Back button** (the series goes on). If the session is over, the
    `SessionEndedEvent` that follows replaces it.
  - `DraftStartedEvent` → after `DraftRevealDelaySeconds = 2` (the banner lands first), open the overlay:
    the banner shrinks to the headline, the board dims (`BoardView.SetDimmed(true)`, a new method that
    darkens the tile material tint — or the HUD's own full-screen scrim if the board has no cheap way;
    §13), `HudDraft` built from `Session.MyOffers` (§7.6). The draft clock is the driver's
    `TurnSecondsRemaining` / `Total` (one shape).
  - `DraftPickedEvent` → own: `Picked = true`, status "Waiting for {opponent}…"; opponent's: `OpponentPicked
    = true`.
  - `SessionEndedEvent` → `Banner = "VICTORY"` / `"DEFEAT"`, `BannerDetail = "series {s0} – {s1} · by
    elimination"` / `"opponent resigned"` / `"you resigned"` / `"opponent left"` / `"you were
    disconnected"`; `_backToLobbyAt` as today; close the overlay if open.
  - `BoonRevealedEvent` → `Flyover { Headline: "Revealed: {boon.Name}", Detail: "{lineage.Name} · {Kind}" }`
    over the unit; a `HudMarker` with the boon's icon; `RefreshExamine`.
  - `LineageRevealedEvent` → the unit's `HudUnit.LineageTag = lineage.Name`; flyover `"Lineage revealed:
    {Name}"`; `RefreshExamine`.
  - `NextRound` (driver event) → `SceneManager.LoadScene("Arena")`.
- `HandleResynced`: if `Session.IsOver` → the final banner from `Session.Winner` and the last known reason
  (`"by elimination"` when unknown); else if `Session.Phase == Draft` → the overlay; else as today.
- `BackToLobby`: `LastResult { Won = Session.Winner == LocalPlayer, Reason = _bannerDetail, OpponentName }`.
- `RefreshExamine` (own and enemy hero): `Subtitle` = the lineage name for a known lineage, `"Unknown
  lineage"` for a hidden one; new `HudExamine.Boons` — own: one entry per `UnitView.Boons` with `Name`,
  `Description` (the boon's), `Icon`, `Group = kind name` ("Blessing" / "Enchant" / "Sigil"); enemy: hidden
  entries `Name = "Unknown boon"`, `Description = "Revealed when it changes something you can see."`,
  `Hidden = true`, in grant order, so the count is public and the identity is not (`#hidden-info` rule 5).
- `HudAction`: `Detail` gains the element(s) as its first word(s) ("Fire · Lobbed · range 1–5"); new
  `Modified` (bool) true when the resolved def differs from the catalogue's base def in `Cost`, `Range`,
  `MinRange` or `Damage` (own unit only; everything about it is known) — the button shows a small mark.
- `HudPreviewLine`: a `boonStat` line's label is the boon's name (`_catalog.Boons`), a `nullify` line's is
  `"Immune ({element})"` — find where breakdown lines become labels (`RefreshPreview` / the tooltip) and add
  the two kinds; `DamageLineKind.BoonStat` and `Nullify` are on the wire since part 1.

### 7.6 `IMatchHudSource`, `MatchHudView`, `MatchHud.uxml` / `.uss`

New on `IMatchHudSource`:

```csharp
string SeriesLine { get; }          // "ROUND 2 · 0 – 1", null before the first round
HudDraft Draft { get; }             // null when no draft is open
void SelectDraftCard(int index);    // highlights; -1 clears
void ConfirmDraft();                // sends the selected card; ignored when nothing is selected or already picked
```

```csharp
public sealed class HudDraftCard { public string Id, Name, Kind, God, Lineage, Effect, Attach, Icon; }
public sealed class HudDraft
{
    public string Headline;              // "ROUND 1 LOST · 0 – 1"
    public string NextRoundLine;         // "Choose one boon · Round 2 on Board"
    public List<HudDraftCard> Cards = new List<HudDraftCard>();
    public int Selected = -1;
    public bool Picked;                  // mine
    public bool OpponentPicked;
    public string Status;                // "Guest-4471 has picked" / "Waiting for Guest-4471…"
    public float SecondsRemaining, SecondsTotal;
}
```

`Attach` is `"On you"` for a Blessing, `"On your {item.Name}"` for the item in `requires.slot` of the
local build (`Session.MyBuild.Loadout`). `God` is the name up to its first apostrophe. `HudUnit` gains
`string LineageTag` (null until revealed) drawn on the nameplate.

`MatchHud.uxml` (by hand, the names the view binds to):

- inside `turn-panel`, above `turn-owner`: `<ui:Label name="series-line" class="series-line" />`.
- a new block `draft-panel` (hidden by default, full-screen scrim `draft-scrim`): `draft-headline`,
  `draft-next`, `draft-cards` (container; three `draft-card` elements built in code from a small
  `VisualTreeAsset`-free template: `card-kind`, `card-name`, `card-god`, `card-effect`, `card-attach`,
  `card-icon`), `draft-timer-track` / `draft-timer-fill`, `draft-status` (with a `draft-dot` element),
  `draft-confirm` button.
- under `examine-subtitle` nothing new (the subtitle carries the lineage); after `examine-abilities`:
  `examine-boons-caption` and `examine-boons` (the same `FillEntries` fills them, grouped by kind).
- in the unit tag template: `tag-lineage` label.
- `.uss`: `.kind--blessing`, `.kind--enchant`, `.kind--sigil` colours; `.draft-card--selected`;
  `.action--modified` mark; `.series-line`. Colours are the mock's greys and the three kind hues; no
  new fonts.

`MatchHudView`: `RefreshDraft()` in `Refresh()`; `UpdateDraftTimer()` in `Update()` from the source's
`Draft.SecondsRemaining`; click on a card → `SelectDraftCard`; `draft-confirm` → `ConfirmDraft`;
`RefreshBanner` unchanged but the back button obeys `ShowBackToLobby` only (a round banner never shows it).

### 7.7 `LobbyView` and `Lobby.uxml` — the lineage row

- `Lobby.uxml`, room panel, under `preset`: a `lineage-row` with three `ui:Button`s `lineage-0`,
  `lineage-1`, `lineage-2` (filled from the catalogue in id order: greek, hindu, norse), each showing the
  lineage's `Name` and `Description` and a third line `Starts with {startingBlessing.Name}`. The chosen one
  gets `lineage--selected`. `Lobby.uss` styles the row to fit the ≤ 480 px column (three columns, or
  stacked under 400 px).
- `LobbyView`: `[SerializeField] private ContentBootstrap _content;` — **assigned in `Lobby.unity` through
  the live Editor** (`unity command` on the `LobbyUI` object; if the Lobby scene has no `ContentBootstrap`
  object, add one the way the Arena has it — through the Editor, and commit the scene with its state
  unchanged otherwise). The catalogue gives names, descriptions and the starting Blessings; nothing about
  a lineage is hard-coded in C#.
- `_lineage` (id) is read from `PlayerPrefs` (`NetClient.LineagePref`) at bind, defaulting to the first
  by id (`greek`); clicking a button sets it, saves it, and un-readies exactly as changing the preset does.
- `BuildLoadout(ready)` adds `["lineage"] = _lineage`; `HandleReady` refuses with "Pick a lineage." if
  none (cannot happen with the default, kept for honesty).
- `Explain`: `bad_loadout` → "That loadout or lineage is not valid." Room status "Pick your gear." →
  "Pick your gear and your god."
- `ShowRoom`/`DrawSeat`: unchanged; the other seat's lineage is not shown.

### 7.8 Web build and smoke

After the client commits and with the Editor closed: `unity build MimasClient --target WebGL
--execute-method Mimas.Client.Editor.WebBuild.Build --output-path Build/Web` (the script, not the profile),
size under the 13 MB ratchet; serve with `MIMAS_WEB_PATH=<absolute>/Build/Web dotnet run --project
server/Mimas.Server`; then `node tools/smoke/browser-smoke.mjs --expect "\\[NetClient\\] connected"` and a
`--do` script that presses Play vs bot and waits for `[NetClient] match … series 1 round 1` (the new log
line), with `--shot artifacts/smoke` of the room panel showing the lineage row; Chromium, then `--browser
webkit` and `firefox` as T-0008 did. The two-browser best-of-3 with a draft is **played, not scripted**:
two headed windows (`--headed` twice, or two browser profiles) through a whole series against each other
on `localhost`, screenshots of the draft overlay and the series result into `artifacts/`, named in the run
report. The draft overlay is also captured in the Editor from practice mode (§7.4) with
`capture_game_view --save_path Assets/_Shots/draft-overlay.png` (never under a `Temp` folder).

### 7.9 EditMode test

`MimasClient/Assets/_Game/Tests/EditMode/MirrorResolverTests.cs`:
`MatchSession_ReadsAbilitiesOnlyThroughTheMirror` reads `Presentation/Match/MatchSession.cs` as text and
asserts it contains no `_catalog.Abilities` (the Core has the same kind of source-scan test for
`ResolveAbility`). Runs with `unity test MimasClient --mode EditMode --report-format junit --output
artifacts/editmode.xml --timeout 600` (exit 8 = failed, never retry).

---

## 8. Docs and design

### 8.1 `docs/networking.md`

Rewrite the message tables from §6.4; the "What is not here yet" paragraph loses the best-of-3 line and
gains "tiers, rerolls, a character-select screen, presets on the server"; the hidden-information section
gains the session block's rules (own offers only; the opponent's pick is "picked"; opponent boons null
until revealed; the lineage null until revealed); the clocks section gains the draft mode; the rematch
paragraph says "a new series" and names `series`; a short "Session on the wire" section explains the
split rule (a batch with `roundStarted` is a `match.start`) and the reload rule on the client.

### 8.2 `docs/data.md`

"Session and draft": add the resign/forfeit rule (§4.1), `SessionEndReason`, `NextMapId`; "Boons" and
"Lineages" "Shipped:" lines list nine boons per lineage and `storm-bolt`; the stackable example names
`thor-might`.

### 8.3 `docs/architecture.md`

The `Mimas.Server` table: `Room` hosts a `Session`; the tick's two modes; the split rule. The client
table: `LocalSessionHost`; `IMatchDriver.Session`; the presenter's draft state.

### 8.4 `docs/decisions.md` — ADR-036

**The room hosts the session; the session rides the existing wire; resign concedes the series.** One
row in the ADR-027 style: the decision (a `Session` per `Playing`; `series` and `round`; session events in
`events`; a `session` block beside `view`; `draftPick` through `match.command`; one `match.start` per
round and the client's reload rule; `clock.activePlayer: -1` in a draft; resign and forfeit end the
session with a reason; the bot's random lineage), why (one filter, one view, one codec, no second redraw
path on the client; the reload is the path the client already has for a match start; a series that
continues after a resign leaves the other player waiting through up to three forfeits; `#session` rule 7
already says a forfeit is the session's), alternatives (the outline's six message types; a round-scoped
resign; a draft screen in the lobby scene, rejected in the 22 Sep mock round). Note that it supersedes
ADR-032's meaning of `round` in `match.start`.

### 8.5 The design page (`docs/design/index.html`, declared in `allows_assets`; edit by the Edit tool,
never through a shell command; keep the page's vocabulary)

- `#session`: rule 7 → "A player who resigns, or who leaves and does not reconnect within the grace period,
  forfeits the **session** (decided 22 Sep 2026, P8)." Drift note → `data-impl="implemented"`: "The server
  hosts it since part 2 (T-0010): one session per Playing room; resign and forfeit end it."
- `#round`: no rule change; the drift note mentions the round card and the score line.
- `#draft`: `data-impl="implemented"`; a rule 8: "**Where** (decided 22 Sep 2026, P1): over the dimmed
  board in the Arena, cards with kind, name, god, effect and the item they attach to; a click and Confirm,
  or the timeout." Drift note replaced by "Implemented in part 2: the overlay, the timer, the server's
  timeout pick, the bot's pick."
- `#character-select`: `data-impl="partial"`, note: "Launch shape (22 Sep 2026): the room's preset dropdown
  is step 1 and a lineage row under it is step 2; the full screen is later. The bot's lineage is drawn at
  random (P6)."
- `#lineage`: `data-impl="implemented"`; the drift note says the row in the room, the bot's random pick,
  nine boons per lineage.
- `#boons`: `data-impl="implemented"`.
- `#hidden-info`: `data-impl="implemented"`; the drift note names the examine list, the flyover, the
  nameplate tag, the "?" rows.
- `#bots`: close `q-bots-draft` — remove the open question, add to the paragraph: "Decided 22 Sep 2026: the
  bot prays to a lineage drawn at random from the three with the room's seed, drafts the first offer, and
  plays the full best-of-3 (P5, P6). Its two dev passives retired with part 2."
- `#online`: rule 9 amended — "…both pressing Ready starts the next **series**. Within a series the
  rounds follow each other with the draft between them; the room goes back to Waiting only when the series
  ends." Rule 10 new: "**The session on the wire** (decided 22 Sep 2026, P3): session events travel in the
  same event list as a round's, a `session` block sits beside the view on every match message, a draft
  pick is a command, and every round begins with a `match.start` (ADR-036)." The drift note's date list
  gains 22 Sep.
- `#presentation`: HUD list gains "Series line in the turn panel: `ROUND 2 · 0 – 1` (decided 22 Sep 2026,
  P2)" and "Draft overlay over the dimmed board (P1)"; Examine gains "own boons listed with kind and god
  under the lineage; enemy boons as `?` rows"; the lobby list's room line gains "a lineage row under the
  preset"; `q-pres-select` becomes "Character select is not mocked yet; the draft screen was decided on
  22 Sep 2026 (cards over the dimmed board, `artifacts/draft-screen-mocks.html` option A)."
- The decision log at the page's end: eight rows, P1–P8, dated 22 Sep 2026.

### 8.6 The rest

- `docs/deploy-runbook.md` §3 gains one line: "What changed since 21 Sep: the boons content (the `/health`
  content hash changes), the session wire (an old browser tab must reload), `room.loadout` requires
  `lineage`." Rohan runs deploys; the builder does not.
- `studio/STATE.md` rewritten, never appended: what a player sees now, the ladder result, what waits on
  Rohan (the two-browser series, tuning the eighteen + nine numbers), the verifier list.
- `studio/tasks/T-0010-boons-in-game.md` → `status: verify` after the last commit.
- The ledger is the verifier's: M3-3, M3-4, M3-5, and the draft half of M3-6.

---

## 9. Tests (one behaviour each, `Method_Scenario_Expected`)

**Core** (`shared/Mimas.Core.Tests`, protected: the task declares `SessionTests.cs`, `ProtocolTests.cs`):

- `Session_ResignInRound_EndsTheSessionWithResign` (round scored, then `SessionEndedEvent(Resign)`,
  `IsOver`, `Winner` = the other).
- `Session_ForfeitInRound_EndsTheSessionWithForfeit`.
- `Session_ResignInDraft_EndsTheSessionWithoutScoring`.
- `Session_EliminationSeries_EndsWithScore` (the existing best-of-3 test asserts the reason).
- `SessionView_NextMapId_WrapsTheLadder`; `SessionView_Create_RoundTripsFor`.
- `Wire_DraftPick_RoundTrip` (both reasons); `Wire_EveryEventType_RoundTrip` extended by the five
  (`draftStarted` with one side null stays null); `Wire_Session_RoundTrip_HiddenBoonsStayNull`;
  `Wire_Session_Encode_IsDeterministic`.
- The golden replay of part 1 is unchanged (bots never resign).

**Server**: §6.6. **EditMode**: §7.9. **Manual, in the run report with screenshots**: the draft overlay in
practice mode; a two-browser series; the lineage row; the examine panel on both heroes; a reveal flyover.

---

## 10. Verification commands (run all before the final commit)

```bash
dotnet build Mimas.slnx
dotnet test shared/Mimas.Core.Tests                                  # ≥ 445 + the §9 additions
dotnet test server/Mimas.Server.Tests                                # ≥ 47 + 9
unity command menu --path "Assets/Refresh"; unity command recompile_status   # completed, no errors
unity command console                                                 # error: 0
# close the Editor, then:
unity test MimasClient --mode EditMode --report-format junit --output artifacts/editmode.xml --timeout 600
unity build MimasClient --target WebGL --execute-method Mimas.Client.Editor.WebBuild.Build --output-path Build/Web
MIMAS_WEB_PATH=E:/Unity\ Projects/MimasGame/Build/Web dotnet run --project server/Mimas.Server   # absolute path
node tools/smoke/browser-smoke.mjs --expect "\\[NetClient\\] connected" --timing
node E:/Studios/Trinetra-Game-Studio/tools/ladder/ladder.mjs --project mimas --task T-0010
```

---

## 11. Editor workflow, in order

1. Editor open and `ready`: the client edits of §7 (text by hand), the one scene assignment of §7.7
   through `unity command`, `Assets/Refresh` after each new file, `recompile_status`, `console`; play the
   practice mode from the Arena scene to the draft overlay and capture it (§7.8).
2. Data files of §5 with the Editor open: `Assets/Refresh`, commit the `.meta` files and the manifest with
   the JSON.
3. Close the Editor. EditMode tests, the Web build, the smoke, the two-browser series (§7.8).

---

## 12. Commit plan (each step green before committing; `git add` only the files you touched)

1. `core: the session ends on a resign or a forfeit, and its view knows the next map` — §4.1, §4.2, the
   Core tests of §9.
2. `core: session events, the draft pick and the session view on the wire` — §4.3, the protocol tests.
3. `data: nine more boons and Indra's Storm` — §5 with `.meta` and the manifest (Editor open).
4. `server: the room hosts the session — lineage in the room, draft clock, bot draft, forfeit ends the series`
   — §6 whole, `SessionTests.cs` and the amended room/match tests. 47 → ≥ 56.
5. `client: the lineage row in the room` — §7.7, `NetClient.LineagePref`, the scene assignment.
6. `client: the online driver follows the session; one match.start per round reloads the arena` — §7.2,
   §7.3, the null-safe presenter paths of §7.5 (no visuals yet beyond not crashing between rounds).
7. `client: practice mode runs a session` — §7.4.
8. `client: the draft over the dimmed board, the series line, round cards` — §7.5 events, §7.6 overlay
   and series line, `.uxml`/`.uss`.
9. `client: boons in examine, reveal flyovers, resolved numbers on the action bar` — §7.5 examine /
   flyover / `HudAction.Modified` / preview labels, the three resolver sites, §7.9 EditMode test.
10. `docs: networking, data, architecture, ADR-036, design page, runbook, STATE` — §8; the run report;
    the task to `verify`.

Commits 4 to 6 leave the deployed site's old client unable to start a match against the new server
(`room.loadout` needs `lineage`); that is fine on `main` and the reason nothing is deployed until the
verifier passes and Rohan runs the deploy.

---

## 13. Defaults for forks the session may hit

| Fork | Default |
|---|---|
| `BoardView` has no cheap dim | A full-screen scrim element in the HUD (`draft-scrim`, 72 % black) over the board layer; no shader change |
| `BoardView.Build` is not re-entrant | It never needs to be: every new round is a scene reload (§3) |
| The Lobby scene has no `ContentBootstrap` | Add one through the Editor, wired like the Arena's; if the lobby's asmdef cannot reference `Mimas.Client.Content`, add the reference in the asmdef (text) |
| A `match.start` arrives while the presenter is mid-animation | Reload anyway; nothing on a finished round is worth waiting for |
| `SessionEndedEvent` and `RoundEndedEvent` in one batch | The round banner is replaced by the series banner immediately; only the series banner shows the Back button |
| A resign click in a draft online | Sent as `ResignCommand`; the HUD's two-click resign is unchanged |
| The opponent picked before my overlay opened (2 s delay) | `OpponentPicked` is read from the session block when the overlay opens, not only from the event |
| Draft timer already below zero on the client (late message) | Show 0 s; the server's `draftPicked { timeout }` arrives and the flow continues |
| `Wire.ReadSession` with a `view` that is not null but `phase != round` | `WireException` ("a session in phase X carries no view") |
| The card's god for a name without an apostrophe | The whole name |
| `Attach` when `requires.kind` does not match the local item | Cannot be offered (`Draft.IsApplicable`); if it ever appears, `"On your {slot}"` |
| `MatchSettings.FirstPlayer` | Kept, tooltip says unused; no asset edit |
| `LocalSessionHost` when the Arena is opened after a finished practice session | A fresh session from settings; the host is replaced |
| `MirrorPlayer.Choose()` in a draft | A random offer of `myOffers` (its own seeded `Rng`), so a test series drafts differently on each seat |
| The bot's think delay in a draft | The same `BotThinkMs` |
| `unity build`'s size over 13 MB | Stop and report; the ratchet is not moved by the work it judges |
| The EditMode source-scan finds `_catalog.Abilities` in a comment | The scan ignores lines starting with `//` and `///` |

---

## 14. Definition of done (copy into the final report with ticks)

- [ ] `dotnet build Mimas.slnx` clean; Core tests ≥ 445 + §9; server tests ≥ 56; no existing assertion
      softened or removed.
- [ ] Two fake clients play a whole best-of-3 over sockets with a draft between rounds, on two maps in
      ladder order, and every `view` and `session` each received is its own (§6.6).
- [ ] A draft timeout, a reconnect into a draft, a forfeit from a draft, a resign in a round and in a
      draft all end as §6.6 says.
- [ ] The bot room plays a best-of-3, drafts, and its lineage is drawn at random (or fixed by option).
- [ ] `room.loadout` without a lineage is `bad_loadout`; the lineage never appears in `room.state`.
- [ ] Core: `SessionEndReason`; `SessionView.NextMapId` / `Create`; `draftPick` and the five session events
      and the session view round-trip on the wire.
- [ ] Nine boons and `storm-bolt` load with their `.meta` and the manifest; every lineage has nine.
- [ ] Client: the lineage row in the room (persisted); one `match.start` per round reloads the Arena; the
      draft overlay over the dimmed board with timer, dot and Confirm; the series line; round cards; the
      series banner and Back to room; examine boons and lineage; the reveal flyovers and the nameplate tag;
      `HudAction.Modified` and element words; `boonStat` / `nullify` preview labels; practice mode runs a
      session; `_catalog.Abilities` gone from `MatchSession.cs` (EditMode test green).
- [ ] `unity command console` clean; EditMode tests green; Web build under 13 MB; smoke green on three
      engines; screenshots of the overlay (Editor and browser), the room's lineage row, the examine panel
      and a reveal in `artifacts/` and named in the run report.
- [ ] Docs of §8 done; ADR-036 written; the design page edited through the declared allowance; the
      deploy runbook line; STATE rewritten; run report complete; `T-0010` at `verify`.
- [ ] Left for Rohan, written in STATE: the two-browser series with a friend, deploying, tuning the
      twenty-seven boons.
