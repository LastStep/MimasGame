# Spec C: Online slice (M2: server rooms, guest auth, queue, bot seat, clocks, reconnect, lobby scene)

_Work order for one autonomous Claude Code session (executing model: Claude Opus). Written 17 Sep 2026
after a three-round question round with Rohan and a research pass (Lichess lag compensation, card-game
turn timers, ASP.NET Core WebSocket test hosts, Unity WebGL PlayerPrefs). Design source of truth:
`docs/design/index.html` (`#online`, `#time-controls`, `#turns`, `#win-conditions`, `#hidden-info`,
`#bots`, `#presentation`). ADRs written for this slice: ADR-026 (client mirror), ADR-027 (wire and
clock), ADR-028 (lobby scene and the presenter / driver split). Names of existing code were checked on
17 Sep 2026 at commit `15e3be0`. **Both aiming specs are on `main` and verified; 287 Core tests.**_

---

## 0. How to run this session

Read this whole file, then `CLAUDE.md`, then the design page sections named above, then
`docs/networking.md`, `docs/architecture.md`, `docs/decisions.md` (ADR-002, ADR-010, ADR-015,
ADR-017, ADR-018, ADR-026..028). Read every file you touch in full before editing it, in particular
`shared/Mimas.Core/Runtime/Match/MatchState.cs`, `PlayerView.cs`, `MatchEvent.cs`, `Command.cs`,
`EventFilter.cs`, `Units/Unit.cs`, `server/Mimas.Server/Program.cs`, and on the client
`Presentation/Match/LocalMatchSession.cs` (1470 lines), `IMatchHudSource.cs`, `MatchSettings.cs`,
`UI/MatchHudView.cs`, `Board/BoardView.cs`, `Content/ContentBootstrap.cs`.

Rules for the session:

1. **Autonomous.** No questions. Forks have defaults in §12. Balance numbers are not yours to tune.
2. **Commits:** small commits to `main`, `area: what`, in the order of §11. Do not push. Before every
   commit: `dotnet build Mimas.slnx`, `dotnet test shared/Mimas.Core.Tests`, and once it exists
   `dotnet test server/Mimas.Server.Tests`, all green. For client commits additionally: the Editor
   compiles (`unity command recompile_status` → `completed`) and `unity command console` has no
   `error CS` and no `Exception`.
3. **Order of work.** §3 to §6 (data, Core, server, server tests) need no Editor and come first. Do not
   open the client half (§7) until §6 is green and committed. If no Editor is `ready` when you reach §7
   (`unity status --format json`), run `unity pipeline list`, report, and stop after §9's docs for the
   server half; unverified Unity C# is not worth committing.
4. **Never** create, edit or delete `*.meta`, `.unity`, `.prefab`, `.asset`, `.mat` files by hand. Scenes,
   objects, components, serialized fields and assets are made through `unity command …`. This spec
   explicitly allows two `ProjectSettings` changes and nothing else: the scene list
   (`add_scene_to_build`, §7.9) and nothing in `Packages/manifest.json` or `packages-lock.json`
   (NativeWebSocket and Newtonsoft are already installed).
5. **Golden rules** 3 and 4 for everything under `shared/`: no `UnityEngine`, no floats in rules, no
   `DateTime`, no `Guid`, no `Dictionary` iteration order, C# 9. Time and randomness for *seeds* live in
   the server project, never in Core.
6. **Hidden information.** The server never serialises a `MatchState`. Every byte that leaves a room is a
   `PlayerView` for one seat or an event list filtered by `EventFilter.ForPlayer` for that seat. A test
   in §6 asserts it.
7. **Web constraints:** WebGL2, no managed threads on the client (NativeWebSocket's jslib path is
   callback-based; the Editor path needs `DispatchMessageQueue()` from `Update`), no `System.Net.WebSockets`
   on the client, no `Task.Delay` in client code (does not run on WebGL).
8. **Unity CLI gotchas** (all real, from earlier sessions):
   - `export MSYS_NO_PATHCONV=1` before any `unity command` that takes a path.
   - New files on disk are invisible until `unity command menu --path "Assets/Refresh" --timeout 180`;
     then poll `unity command recompile_status` until `completed`. `.meta` files appear only then; commit
     after it.
   - Console: `unity command console`. Grep for `error CS`, `Exception`, `[MatchSession]`,
     `[NetClient]`, `[LobbyView]`, `[BoardView]`, `[ContentBootstrap]`.
   - `capture_game_view --source screen` at native size; `--save_path` under `Assets/Temp/…`, then
     `delete_asset --asset <path> --confirm true` (never `rm`). Copy captures into `artifacts/` with the
     names in §8 before deleting.
   - `eval --code` cannot add `using` or declare classes; use fully qualified names, `eval_file` for
     multi-line snippets, reflection to poke private handlers.
   - Stop Play Mode (`unity command editor_stop`) before editing C#. Check for Play Mode before scene
     edits. Never two Editor commands at once; on a timeout wait 10 s, retry once, then treat the Editor
     as unreachable and report.
   - Long heredocs get truncated: write scripts to the scratchpad directory and run them.
   - `unity command --query <term> --detail full` lists a command's real parameters.
   - A backgrounded `dotnet run` of the server survives `pkill`; use `taskkill //F //IM Mimas.Server.exe`.
9. **No scope creep.** Out of scope: time controls with banks and increments (`timecontrols.json` stays
   loaded and unused), the idle penalty online, ratings, accounts, persistence (no database), the
   session wrapper (best-of-3), character select (the lobby has presets only), any deploy artefact
   (Dockerfile, nginx, VPS), spectating, chat, mobile input, art. Local practice mode must keep working
   exactly as it does today when the Arena scene is opened directly in the Editor.

---

## 1. Goal and result

Two browsers (or the Editor and a browser) play a full match against each other through
`Mimas.Server`, or either plays the server's random bot, with the same board, HUD, previews and
projectiles as the local session. Concretely:

- The **Lobby scene** opens first: a name, a loadout preset, **Play vs bot** and **Find match**. Both
  start a server room; the bot one starts at once. The Arena scene loads when `match.start` arrives and
  the lobby comes back after the result.
- The **server** holds the truth: guest auth with a resumable token, a FIFO queue, rooms with a
  **30 s turn deadline** (plus a lag grace of the measured round trip, at most 1 s), a bot seat, hidden
  information filtered per seat, **resign**, a **60 s reconnect grace** after which the absent player
  forfeits, and a full-view resync.
- The **client mirror** (ADR-026): the client builds a knowledge-limited `MatchState` from the
  `PlayerView` the server attaches to every message, so every existing preview (move options, range
  circles, `CheckTarget`, damage preview with its "?" row) works unchanged and an illegal click is
  refused instantly without a round trip.
- **Done when** (§13): Core and server tests green; from the Editor, a bot match plays start to finish
  online, a refresh of the Web build reconnects into the running match, resign and forfeit both end a
  match with the right banner; a Web build in a browser and the Editor find each other through the queue
  and play a full match.

---

## 2. Decisions locked in the question round (17 Sep 2026)

| # | Decision | Where it lives |
|---|---|---|
| D1 | One spec, server and client, one session. Server half first, client half only with an Editor. | this file |
| D2 | **Client mirror.** The client never holds the truth. It rebuilds a `MatchState` from the `PlayerView` that accompanies every server message (`MatchState.FromView`) and asks that mirror every preview question. Events are for animation only. Refinement of the answer "mirror advanced by events": rebuilding from the attached view is strictly simpler and cannot drift, and the view is tiny (2 heroes, ≤ 8 props). | ADR-026, `#hidden-info` |
| D3 | **Bot on the server.** `bot.play` opens a room with `RandomBot` in seat 1 and starts at once. Local practice (Arena opened directly in the Editor) stays as an Editor-only fallback through the same presenter. | `#bots`, `#online` |
| D4 | **In-memory server.** No database. Guest token, name and current match id are kept in `PlayerPrefs` so a page reload rejoins within the grace. Ratings and accounts stay M5. | ADR-027 |
| D5 | **Clock: one flat 30 s turn**, no bank, no increment, no time-control choice, **no idle penalty online**. Server-authoritative deadline; every server message carries `clock { activePlayer, turnMs, remainingMs }`; the client counts down from receipt and re-anchors on every message; the server ends the turn at `deadline + min(rtt, 1000 ms)` by submitting `EndTurnCommand(Timeout)`. RTT is measured by server-initiated pings. The rope stays. | ADR-027, `#time-controls`, `#turns` |
| D6 | **Lobby is its own scene** (`Lobby.unity`, build index 0), later home of character select. The connection lives on a persistent `NetClient` object. The Arena scene loads on `match.start` behind a "Loading…" line. | ADR-028, `#presentation` |
| D7 | **Loadout presets** in the lobby, sent with `queue.join` / `bot.play`. Four presets from the shipped items. Presets are client data (`LoadoutPresets.asset`), not a Core concept. | §7.7 |
| D8 | **Resign** button in the HUD (two clicks) and an **opponent-disconnected** line with a countdown; no return within 60 s = forfeit, the clock keeps running meanwhile. | `#win-conditions`, `#online` |
| D9 | **Local end-to-end only.** Optional last step: the server serves `Build/Web` so a browser tab can play without a second web server. No deploy artefacts. | §7.11 |
| D10 | **Wire:** JSON envelope `{ "t": "...", "p": { ... } }`, one hand-written codec in Core (`Mimas.Core.Protocol.Wire`) for commands, events and views, no `TypeNameHandling`, enums as strings, `Hex` as `[q, r]`. | ADR-027 |
| D11 | **Match setup online:** map `arena-4`; seed from the server; first player by seeded coin flip; the bot seat gets the two hidden passives the local bot has (`ward-of-feathers`, `stone-skin`) so the reveal path is exercised online. Humans get no modifiers. | §5.4 |
| D12 | New Core command **`ResignCommand`** (legal at any time while the match runs, never enumerated for bots) and `MatchEndReason.Resign` / `Forfeit`. A disconnect forfeit is a `ResignCommand` with `ResignReason.Disconnect` submitted by the server, so a replay of the command list reproduces the match. | §4.2 |

---

## 3. Data changes (`MimasClient/Assets/_Game/Data/`)

### 3.1 `rules.json` (add one block; everything else unchanged)

```json
"clock": { "turnMs": 30000, "lagGraceMs": 1000, "reconnectGraceMs": 60000 }
```

Required. `turnMs` is the turn deadline for every turn of every player. `lagGraceMs` caps the
round-trip allowance the server adds before it ends a turn. `reconnectGraceMs` is how long a
disconnected seat is kept before it forfeits. These are rules (both sides read them), not balance.

### 3.2 `timecontrols.json`

Untouched, still loaded, unused by M2. The design page marks time controls as proposed for later.

### 3.3 `tools/schemas/rules.schema.json`

Add `clock` to `required` and to `properties`: an object, `additionalProperties: false`, three required
positive integers with descriptions as in §3.1.

### 3.4 Nothing else changes in data. No new files, no map edits, no item edits.

---

## 4. Core changes (`shared/Mimas.Core/Runtime`)

### 4.1 `Data/ClockDef.cs` (new) and `Data/RulesDef.cs`

`ClockDef(int turnMs, int lagGraceMs, int reconnectGraceMs)`, all `> 0` else `ArgumentOutOfRangeException`.
`RulesDef.Clock` (required; a missing block is a `MapLoadException` naming `rules.json`). If test
fixtures construct `RulesDef` directly, give the constructor a defaulted `ClockDef clock = null` that
falls back to `new ClockDef(30000, 1000, 60000)` and say so in the class summary.

### 4.2 `Match/Command.cs`, `Match/MatchEvent.cs`, `Match/MatchState.cs`: resign

- `public enum ResignReason { Player = 0, Disconnect = 1 }`.
- `public sealed class ResignCommand : Command { ResignReason Reason }`.
- `MatchEndReason` gains `Resign = 1`, `Forfeit = 2`.
- `Validate`: a `ResignCommand` is legal for either player at any time while `!IsOver` (also off-turn);
  after the match is over it is rejected with the existing "match over" reason (reuse
  `CommandRejectReason`'s value for that; add one if none exists and say so).
- `Apply`: `IsOver = true; Winner = 1 - player;` emit `MatchEndedEvent(Winner, reason == Player ? Resign : Forfeit)`.
  No turn-ended event, no AP change.
- `EnumerateLegal` never lists a resign (bots do not resign).
- `ToString()`: `P{Player} resigns ({Reason})`.

### 4.3 `Match/MatchState.cs`, `Units/Unit.cs`, `Units/Prop.cs`: the mirror (ADR-026)

```csharp
public static MatchState FromView(ContentCatalog catalog, PlayerView view);   // the mirror
public bool IsMirror { get; }
```

A mirror is a `MatchState` built from one player's view and nothing else:

- Map: `catalog.Maps.Get(view.MapId)`, tile map built as usual. `Setup` is a `MatchSetup` made from the
  two units' public `ItemIds` (`Loadout` per owner), `FirstPlayer = 0` (unknown from a view; document
  that `Setup.FirstPlayer` is meaningless on a mirror). `Rng` seeded with `0` and never used.
- Units: for each `UnitView`, `Unit.FromView(view, catalog)` (new static factory on `Unit`): stats from
  the public items exactly as the truth computes them; **ability ids = the revealed ids only** (`KnownEntry.Id != null`),
  with `AbilitySourceOf` from the items for those ids; **modifier ids = the revealed ids only**;
  `Hp`, `Ap` restored from the view (add `Unit.Restore(int hp, int ap)`, documented as for mirrors and
  future replays; it validates `0 ≤ hp ≤ MaxHp`, `0 ≤ ap`); **`Unit.HiddenAbilityCount`** and
  **`Unit.HiddenModifierCount`** (new, `0` on a truth unit) hold how many entries the view showed as
  hidden, per source item for abilities (`IReadOnlyList<KeyValuePair<string,int>>` or a small
  `HiddenSlot` struct: source item id, count) so `ViewFor` can reproduce the "?" slots.
- Props: `new Prop(id, def, position)` then `Prop.Restore(int hp)`; props absent from the view (destroyed)
  are absent from the mirror.
- `ActivePlayer`, `TurnNumber`, `ActedThisTurn`, `IsOver`, `Winner` from the view.
- Revealed knowledge: every revealed enemy ability and modifier is added to `_revealed` for
  `view.Viewer` so `Knows(viewer, …)` is true for them.
- `ViewFor(viewer)` on a mirror is only legal for `view.Viewer`; other viewers throw
  `InvalidOperationException`. It must reproduce the source view exactly, including the null "?"
  entries (abilities under their source item, modifiers), which is what the hidden counts are for.
- `PreviewAttack(viewer, …)` on a mirror adds the victim's `HiddenModifierCount` to
  `DamageBreakdown.UnknownCount` (a `DamageBreakdown.WithUnknown(int extra)` helper or equivalent), so the
  "?" row survives. `CheckTarget`, `RangeBand`, `MoveOptions`, `AttackTargets`, `Validate`,
  `EnumerateLegal` need no change: they read map, bodies, heights and the acting unit's own abilities.
- `Start()`, `Apply()`, `TryApply()` on a mirror throw `InvalidOperationException("A mirror only answers questions.")`.
  `Validate` works (the client uses it to refuse an illegal click before sending).
- Keep the existing constructor untouched; share the map/registry setup through a private constructor.

### 4.4 `Protocol/Wire.cs`, `Protocol/Messages.cs` (new namespace `Mimas.Core.Protocol`; ADR-027)

One hand-written codec over `Newtonsoft.Json.Linq` (Core already uses it for data), no attributes, no
reflection, no `TypeNameHandling`:

```csharp
public static class Wire
{
    public static JArray Hex(Hex h);                    // [q, r]
    public static Hex ReadHex(JToken t);
    public static JObject Command(Command c);            // { "type": "move" | "attack" | "endTurn" | "resign", "player", ... }
    public static Command ReadCommand(JObject o);
    public static JObject Event(MatchEvent e);           // { "type": "turnStarted" | "unitMoved" | "apSpent" | "attackResolved" | "abilityRevealed" | "modifierRevealed" | "propDestroyed" | "unitDied" | "turnEnded" | "matchEnded", ... }
    public static MatchEvent ReadEvent(JObject o);
    public static JArray Events(IReadOnlyList<MatchEvent> events);
    public static List<MatchEvent> ReadEvents(JArray a);
    public static JObject View(PlayerView v);
    public static PlayerView ReadView(JObject o);
    public static JObject Breakdown(DamageBreakdown b);  // { total, unknown, lines: [{ kind, id, owner, ownerUnitId, amount, hidden }] }
    public static DamageBreakdown ReadBreakdown(JObject o);
    public static JObject MovePlan(MovePlan p);          // { origin, destination, path: [[q,r]…], entered: [[q,r]…], traversal, cost }
    public static MovePlan ReadMovePlan(JObject o);
}
public sealed class WireException : Exception { … }      // unknown type, missing field, wrong shape
```

Field names: `move` = `{ player, unitId, abilityId, to }`; `attack` = `{ player, unitId, abilityId, target }`;
`endTurn` = `{ player, reason: "player" | "timeout" }`; `resign` = `{ player, reason: "player" | "disconnect" }`.
Events carry every constructor argument under camelCase names; `attackResolved` carries
`breakdown`, `damage`, `targetHpAfter`, `targetIsProp`. View: `{ viewer, activePlayer, turnNumber, acted, over, winner, mapId,
units: [{ id, owner, items, pos, hp, maxHp, ap, apPerTurn, body, aim, mine, abilities: [{ id, source }], modifiers: [{ id }] }],
props: [{ id, def, pos, hp, maxHp, alive, body, aim, damageable }] }` with `null` for hidden ids.
Enums (`TraversalKind`, `DamageLineKind`, `DamageLineOwner`, `EndTurnReason`, `ResignReason`,
`MatchEndReason`) are written as lower-camel strings, read case-sensitively. `PlayerView` needs a public
constructor (or a `PlayerView.Create(...)` factory) that takes the lists; add it next to `Build`.

`Messages.cs`: string constants for every `t` in §5.1 (`Messages.AuthGuest = "auth.guest"` …) and the
error codes, so the server and the client cannot misspell a type.

### 4.5 Untouched

`Combat/*`, `Movement/*`, `Geometry/*`, `Content/*` (except reading `clock`), `Bots/RandomBot.cs`,
`EventFilter.cs` (it already does what the server needs), `Rng.cs`.

---

## 5. Server (`server/Mimas.Server`)

### 5.1 Protocol (replaces the tables in `docs/networking.md`; rewrite that file in §9)

Envelope `{ "t": "<type>", "p": { ... } }`, one JSON text frame per message, UTF-8, ≤ 64 KB.

Client → server:

| `t` | Payload | Reply / effect |
|---|---|---|
| `auth.guest` | `{ name }` (1..24 chars, trimmed; empty → `Guest-<4 digits>`) | `auth.ok { playerId, token, name }` |
| `auth.resume` | `{ token }` | `auth.ok` and, if the player's seat is in a live room, a fresh `match.start`; unknown token → `error { code: "bad_token" }` |
| `queue.join` | `{ loadout: { weapon, crown, boots, armour } }` | `queue.status { position, waitedMs }` now and every 5 s; `match.start` when paired; `error { code: "in_match" }` if already seated; `error { code: "bad_loadout" }` if an item id is unknown or in the wrong slot |
| `queue.leave` | `{}` | `queue.status { position: -1 }` |
| `bot.play` | `{ loadout }` | `match.start` at once, seat 0 = you, seat 1 = `RandomBot` ("Random Bot") |
| `match.command` | `{ matchId, command }` (`command` per `Wire.Command`) | `match.events` to both seats, or `match.rejected` to the sender |
| `match.resync` | `{ matchId }` | `match.view` |
| `pong` | `{ t }` (echo of the server's `ping.t`) | updates the connection's RTT estimate |

Server → client:

| `t` | Payload |
|---|---|
| `auth.ok` | `{ playerId, token, name }` |
| `queue.status` | `{ position, waitedMs }` (`position` 1-based; `-1` = not queued) |
| `match.start` | `{ matchId, seq, mapId, youAre, opponentName, view, clock, events }` (`events` = the filtered start events, normally one `turnStarted`; on a reconnect an empty list) |
| `match.events` | `{ matchId, seq, events, view, clock }` (events filtered for this seat; `view` = this seat's fresh `PlayerView`) |
| `match.rejected` | `{ matchId, reason, view, clock }` (`reason` = `CommandResult.ToString()`; the view lets the client re-sync its HUD) |
| `match.view` | `{ matchId, seq, view, clock }` |
| `opponent.status` | `{ matchId, connected, graceMs }` (`graceMs` = time left before forfeit when `connected` is false) |
| `ping` | `{ t }` every 5 s per connection; the client answers `pong { t }` immediately |
| `error` | `{ code, message }` |

`clock` = `{ activePlayer, turnMs, remainingMs }`, `remainingMs = max(0, deadline − now)` at send time.
`seq` increments per room per outgoing batch (`match.start` of a fresh room is `0`); it is for logs and
for the client to drop a stale batch after a reconnect, not for gap replay (a reconnect always brings a
full view). Any message before `auth.*` → `error { code: "unauthenticated" }`. Unknown `t` →
`error { code: "unknown_type" }`. Malformed JSON → `error { code: "bad_json" }` (both exist today).

### 5.2 Files

| File | Responsibility |
|---|---|
| `Program.cs` | Options, singletons (`PlayerRegistry`, `Matchmaker`, `RoomRegistry`, `ContentCatalog`), `/health` (adds `rooms`, `queued`, `players`), `/ws` → `WsConnection.RunAsync`, optional static files (§7.11). Ends with `public partial class Program { }` so tests can host it. |
| `Options/ServerOptions.cs` | Bound from configuration section `Mimas`: `TurnMs?`, `LagGraceMs?`, `ReconnectGraceMs?` (null = `rules.clock`), `BotThinkMs = 1000`, `MapId = "arena-4"`, `BotModifierIds = ["ward-of-feathers", "stone-skin"]`, `PingIntervalMs = 5000`, `IdleCloseMs = 60000`. Tests override through `UseSetting("Mimas:TurnMs", "300")`. |
| `Net/WsConnection.cs` | One per socket. Receive loop (64 KB buffer, text frames only, size-capped), an outgoing `Channel<string>` drained by one writer task so sends never interleave, `Send(string t, JObject p)`, `Closed` event, `Player` (after auth), `RttMs` (EWMA of pong round trips, `0` until the first pong), server `ping` every `PingIntervalMs`, close after `IdleCloseMs` without a pong. Dispatches `t` to `PlayerRegistry` / `Matchmaker` / `RoomRegistry`. |
| `Players/PlayerRegistry.cs`, `Players/Player.cs` | `Guest(name) → Player { Id (int, from 1), Token (32 hex chars from `RandomNumberGenerator`), Name }`; `TryResume(token)`; `Player.Connection` (current or null), `Player.RoomId` (or null). Thread-safe (`lock`). |
| `Matchmaking/Matchmaker.cs` | One FIFO list of `(Player, Loadout, joinedAt)`. `Join` pairs the first two immediately and asks `RoomRegistry` to open a room; `Leave`; a 5 s `PeriodicTimer` loop sends `queue.status`. A player whose connection closes leaves the queue. |
| `Rooms/RoomRegistry.cs` | `Open(seat0, seat1)` (seats are `HumanSeat(Player, Loadout)` or `BotSeat(Loadout)`), `TryGet(matchId)`, `Close(matchId)`, `Count`. Match ids are `int` from 1. |
| `Rooms/Seat.cs` | `Index`, `Name`, `Loadout`, `IsBot`, `Connection` (null when disconnected or bot), `DisconnectedAt` (`long?` ms), `RttMs`. |
| `Rooms/Room.cs` | The truth for one match. See §5.3. |
| `Rooms/RoomClock.cs` | Pure helper: `Deadline`, `Remaining(now)`, `Grace(seat)`, testable without sockets. |

Use `Environment.TickCount64` or `Stopwatch.GetTimestamp` for time inside rooms; `DateTime.UtcNow` only
for logs and `/health`.

### 5.3 `Room` behaviour

- Construction: `MatchSetup(options.MapId, seat0.Loadout, seat1.Loadout, firstPlayer)`, bot seat gets
  `options.BotModifierIds` via `WithModifier`; seed from `RandomNumberGenerator.GetInt32`; `firstPlayer
  = (int)(new Rng(seed).Range(0, 2))`; `new MatchState(catalog, setup, seed)`; `Start()`; seat names
  (player names, "Random Bot"); `seq = 0`; deadline armed for the active seat; `match.start` to each human.
- Every entry point (`HandleCommand`, `HandleResync`, `OnConnectionClosed`, `Attach`, the tick) takes
  one `lock (_gate)`; sends only enqueue.
- `HandleCommand(seat, JObject command)`: `Wire.ReadCommand`; `command.Player != seat.Index` →
  `match.rejected { reason: "not your seat" }`; `TryApply` → reject with `result.ToString()` and the
  seat's view; on success `Broadcast(events)`.
- `Broadcast(events)`: `seq++`; for each human seat `EventFilter.ForPlayer(events, seat.Index, state, into)`,
  send `match.events { …, view: state.ViewFor(seat.Index), clock }`. If the batch contains a
  `TurnStartedEvent` re-arm the deadline for the new active seat. If `state.IsOver`: log the result and
  `RoomRegistry.Close` after the sends are enqueued; clear `Player.RoomId` for both.
- Tick loop: one `Task` per room on a `PeriodicTimer(TimeSpan.FromMilliseconds(100))`, cancelled on
  close. Under the lock: (1) if `now ≥ deadline + Grace(activeSeat)` and `!IsOver` →
  `TryApply(new EndTurnCommand(active, EndTurnReason.Timeout))` and broadcast; (2) if the active seat is
  the bot and `now ≥ botDue` → `bot.Choose(state, 1)` → apply, broadcast, `botDue = now + BotThinkMs`
  (a bot never times out; `Grace(bot) = 0`); (3) for each human seat with `DisconnectedAt` older than
  `ReconnectGraceMs` and `!IsOver` → `TryApply(new ResignCommand(seat.Index, ResignReason.Disconnect))`
  and broadcast.
- `Grace(seat) = min(seat.RttMs, LagGraceMs)`: a command that left the client before the deadline is
  never refused (Lichess compensates up to 1 s per move from a measured ping; same idea).
- `OnConnectionClosed(seat)`: `Connection = null`, `DisconnectedAt = now`, other seat gets
  `opponent.status { connected: false, graceMs }`. The clock is not paused.
- `Attach(seat, connection)` (from `auth.resume`): `Connection = connection`, `DisconnectedAt = null`,
  send `match.start` (current `seq`, current view, clock, `events: []`), other seat gets
  `opponent.status { connected: true }`. A reconnect after the room closed gets only `auth.ok`
  (the client then shows "Match lost" and returns to the lobby).
- `HandleResync` → `match.view`.
- Log one line per room event (open, command, timeout, disconnect, reconnect, forfeit, resign, close).

### 5.4 Loadout validation

`queue.join` / `bot.play` build a `Loadout` from the four ids; `catalog.GetItemForSlot` for each slot
must succeed, else `error { code: "bad_loadout", message }` and nothing is queued.

### 5.5 `Mimas.slnx`

Add `server/Mimas.Server.Tests/Mimas.Server.Tests.csproj` under the `/server/` folder.

---

## 6. Tests

### 6.1 Core (`shared/Mimas.Core.Tests`; names exact, one behaviour each)

`DataTests.cs`: `Rules_ClockBlock_Parsed`, `Rules_MissingClock_Throws`, `Rules_ClockNonPositive_Throws`.

`MatchTests.cs`: `Resign_OnOwnTurn_EndsMatchOpponentWins`, `Resign_OffTurn_IsLegal`,
`Resign_AfterMatchOver_Rejected`, `Resign_Disconnect_EndsWithForfeit`, `EnumerateLegal_NeverContainsResign`.

New `ProtocolTests.cs`: `Wire_Hex_RoundTrip`, `Wire_MoveCommand_RoundTrip`, `Wire_AttackCommand_RoundTrip`,
`Wire_EndTurnTimeout_RoundTrip`, `Wire_ResignDisconnect_RoundTrip`, `Wire_EveryEventType_RoundTrip`
(drive a seeded bot game on arena-4 until every one of the ten event types has appeared, round-trip each,
compare field by field), `Wire_AttackResolved_KeepsTrimmedBreakdown` (filter for the opponent first,
then round-trip: unknown count and line count survive), `Wire_View_RoundTrip_HiddenEntriesStayNull`,
`Wire_UnknownType_ThrowsWireException`, `Wire_MissingField_ThrowsWireException`,
`Wire_Encode_IsDeterministic` (same object twice → identical `ToString(Formatting.None)`),
`Wire_Enums_AreStrings` (no bare integers for `traversal`, `kind`, `owner`, `reason`).

New `MirrorTests.cs` (the ADR-026 invariant; run 20 seeded random-bot games on `arena-4` and at every
step, for each viewer `v`, with `mirror = MatchState.FromView(catalog, truth.ViewFor(v))`):
`Mirror_ViewFor_ReproducesSourceView` (compare `Wire.View(...).ToString()` strings),
`Mirror_MoveOptions_MatchTruth` (destination sets for the viewer's unit, every movement ability),
`Mirror_CheckTarget_MatchesTruth` (reason and `BlockedAt` for every map hex, every attack ability of the
viewer's unit), `Mirror_PreviewAttack_MatchesTruthWithViewerKnowledge` (`Total`, `UnknownCount`, line ids for
every legal target), `Mirror_EnumerateLegal_MatchesTruth` (as sets of `ToString()`),
`Mirror_Validate_MatchesTruth` (for every legal command and for three illegal ones per step),
`Mirror_Apply_Throws`, `Mirror_ViewForOtherPlayer_Throws`, `Mirror_HiddenCounts_ProduceQuestionMarkSlots`
(a unit with two hidden modifiers and one hidden ability under the crown shows exactly those nulls).

Target: ≥ 320 Core tests, all green, `< 2 s`.

### 6.2 Server (`server/Mimas.Server.Tests`, new xUnit project)

Packages: `Microsoft.AspNetCore.Mvc.Testing`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`,
project reference to `Mimas.Server`. Host with `WebApplicationFactory<Program>` and
`factory.Server.CreateWebSocketClient()` (one per fake client; `ConnectAsync(new Uri("ws://localhost/ws"), ct)`).
Data path: the factory sets `MIMAS_DATA_PATH` to `<repo>/MimasClient/Assets/_Game/Data`, found by walking
up from `AppContext.BaseDirectory` to the directory containing `Mimas.slnx`. Default test options:
`Mimas:TurnMs = 400`, `Mimas:LagGraceMs = 0`, `Mimas:ReconnectGraceMs = 400`, `Mimas:BotThinkMs = 0`,
`Mimas:PingIntervalMs = 100`.

Helper `FakeClient`: `SendAsync(t, p)`, `ExpectAsync(t, timeout = 5 s)` (buffers other types; auto-answers
`ping` with `pong`), `Received` list, `CloseAsync()`. Helper `MirrorPlayer`: given the last `view`, builds
`MatchState.FromView`, picks a random legal command with a seeded `Rng`, sends it; this is the client
mirror used as a test double, which is the point.

Tests (names exact):

- `Auth_Guest_ReturnsIdAndToken`, `Auth_EmptyName_GetsGuestName`, `Auth_Resume_SamePlayerId`,
  `Auth_ResumeUnknownToken_Error`, `Unauthenticated_Command_Error`.
- `BotPlay_StartsMatch_ViewClockAndOpponentName` (`youAre == 0`, two units, eight props, `clock.turnMs == 400`).
- `BotMatch_MirrorPlayerPlaysToTheEnd` (loop: on my turn pick via `MirrorPlayer`; expect `match.events`
  until a `matchEnded` event; assert the final view says `over` and names a winner; ≤ 60 s).
- `Command_WrongSeat_Rejected`, `Command_Illegal_RejectedWithView`, `Command_UnknownMatch_Error`.
- `Turn_TimesOut_ServerEndsTurn` (do nothing; expect a `turnEnded` with `reason == "timeout"` then
  `turnStarted` for the other seat within 2 s).
- `Queue_TwoPlayers_Paired_DifferentSeats` (`youAre` 0 and 1, same `matchId`, opponent names cross).
- `Queue_Leave_NoMatch`, `Queue_JoinWhileSeated_Error`, `Queue_BadLoadout_Error`.
- `TwoHumans_MirrorPlayersPlayToTheEnd` (both fake clients play by mirror; both receive the same
  `matchEnded`; every `match.events` batch has `view.viewer == youAre`).
- `HiddenInfo_OpponentViewHidesAbilitiesAndModifiers` (in a two-human match with the bot modifiers moved
  onto seat 1 through options, seat 0's view shows `null` ids for seat 1's abilities and modifiers until
  used; after seat 1 attacks, seat 0's next view shows that ability id).
- `HiddenInfo_NoHiddenLineLeaksBeforeReveal` (scan every `attackResolved.breakdown.lines` seat 0 ever
  receives: a `hidden` line owned by seat 1's unit appears only in a batch that also contains, earlier,
  a `modifierRevealed` for it, or after one did).
- `Reconnect_WithinGrace_GetsMatchStartAndOpponentIsTold` (close seat 1's socket; seat 0 receives
  `opponent.status { connected: false }`; new socket `auth.resume` → `match.start` with the same
  `matchId`; seat 0 receives `connected: true`).
- `Disconnect_BeyondGrace_Forfeit` (close seat 1; seat 0 receives `matchEnded` with `reason == "forfeit"`
  and `winner == 0` within 2 s).
- `Resign_EndsMatch_OpponentWins`, `Resign_OffTurn_Allowed`.
- `Resync_ReturnsCurrentView`.
- `Clock_RemainingDecreasesBetweenMessages`.
- `Health_ReportsRoomsAndQueue`.

Target: every test green, whole project `< 30 s`.

---

## 7. Client (`MimasClient/Assets/_Game`)

### 7.1 Assemblies

`Net/Mimas.Client.Net.asmdef` exists (references `Mimas.Core`, precompiled `Newtonsoft.Json.dll`). Add a
reference to NativeWebSocket's assembly definition (find its name with
`unity command find_assets --type AssemblyDefinitionAsset --name NativeWebSocket`; it is inside
`Packages/com.endel.nativewebsocket`). `Presentation/Mimas.Client.Presentation.asmdef` adds
`Mimas.Client.Net`. `UI/Mimas.Client.UI.asmdef` adds `Mimas.Client.Net` (the lobby view talks to the
client directly). Edit asmdef JSON on disk (they are text), then refresh.

### 7.2 `Net/NetClient.cs` (MonoBehaviour, `Mimas.Client.Net`)

- Singleton: `public static NetClient Instance`; `Awake` keeps the first, `DontDestroyOnLoad`, destroys
  duplicates (the Lobby scene has one; the Arena scene has none).
- `[SerializeField] private string _url = "ws://localhost:7777/ws";` In a WebGL build, `Application.absoluteURL`
  query `ws=<url>` overrides it (so a build on any host can be pointed at any server); otherwise if the
  page is `https:` use `wss://<host>/ws`, if `http:` use `ws://<host>:7777/ws`. In the Editor the field wins.
- API: `Connect()`, `Disconnect()`, `Send(string t, JObject p)`, `State` (`Disconnected | Connecting |
  Connected | Authenticated`), events `Connected`, `Disconnected(string reason)`, `MessageReceived(string t, JObject p)`;
  `Update()` calls `_socket.DispatchMessageQueue()` under `#if !UNITY_WEBGL || UNITY_EDITOR`.
  `ping` is answered with `pong` inside the client before any other handler sees it.
- Identity: `PlayerName` (PlayerPrefs `mimas.name`), `Token` (`mimas.token`), `PlayerId`. `Authenticate(name)`
  sends `auth.resume` when a token exists else `auth.guest`; on `auth.ok` stores token and name and calls
  `PlayerPrefs.Save()` (on WebGL the IndexedDB sync happens on `Save`, not on quit). A `bad_token`
  clears the token and retries as a guest once.
- Match bookkeeping: `PendingMatch` (the last `match.start` payload as `JObject`, cleared when the
  Arena consumed it), `CurrentMatchId` (PlayerPrefs `mimas.matchId`, cleared on `matchEnded` and on
  "match lost"), `LastResult` (`{ won, reason, opponentName }` for the lobby to show).
- Reconnect: when the socket closes while `CurrentMatchId != 0`, retry `Connect()` every 2 s until
  `Authenticated`, then either a `match.start` arrives (rejoined) or nothing follows the `auth.ok` for
  2 s (room gone → `MatchLost` event). Use coroutines or `Update` timers, not `Task.Delay`.
- Logging prefix `[NetClient]`; log every state change and every `error` message; never log the token.

### 7.3 Presenter and drivers (ADR-028)

Rename `Presentation/Match/LocalMatchSession.cs` to `MatchSession.cs` **with `unity command rename_asset`**
so the script GUID and the scene reference on `/Session` survive, then rename the class. Serialized field
names stay, so no `FormerlySerializedAs` is needed. The presenter keeps every board, HUD, aim, playback
and facing responsibility it has today; what moves out is *where the state comes from*:

```csharp
public interface IMatchDriver
{
    int LocalPlayer { get; }
    MatchState Rules { get; }                 // the truth (local) or the mirror (online); never null once Ready
    PlayerView View { get; }                  // latest projection for LocalPlayer
    bool Ready { get; }
    bool Submit(Command command);             // local: validate + apply; online: validate on the mirror, then send
    void Resign();
    string OpponentName { get; }
    string OpponentStatus { get; }            // null, "Opponent disconnected · 47 s", "Reconnecting…", "Match lost"
    float TurnSecondsRemaining { get; }
    float TurnSecondsTotal { get; }
    bool CanResign { get; }
    event Action<IReadOnlyList<MatchEvent>> EventsArrived;   // filtered for LocalPlayer, in order
    event Action Resynced;                    // a fresh full view replaced Rules/View; presenter snaps everything
    event Action StatusChanged;               // OpponentStatus / clock total changed
    void Tick(float deltaTime);               // presenter calls from Update
    void Dispose();
}
```

- `Presentation/Match/LocalMatchDriver.cs` (plain class): owns the `MatchState` truth, the `RandomBot`, the
  fake clock (turn seconds from `rules.clock.turnMs` unless `MatchSettings.TurnSecondsOverride > 0`,
  idle penalty as today), the bot think timer, `MatchSettings` loadouts and modifiers. Behaviour identical
  to today's local session: `Submit` = `TryApply` then raise `EventsArrived` with
  `EventFilter.ForPlayer(events, LocalPlayer, state)`; the timeout submits `EndTurnCommand(Timeout)`;
  `Resign` applies `ResignCommand(LocalPlayer)`.
- `Presentation/Match/OnlineMatchDriver.cs` (plain class over `NetClient`): built from the `match.start`
  payload; `Rules = MatchState.FromView(catalog, Wire.ReadView(view))`; on `match.events` with a newer
  `seq`: rebuild the mirror from the attached view, anchor the clock (`remainingMs` at receipt), raise
  `EventsArrived(Wire.ReadEvents(events))`; on `match.rejected`: rebuild from the view, log the reason,
  raise `StatusChanged`; on `match.view` or a second `match.start` (reconnect): rebuild, raise `Resynced`;
  on `opponent.status`: update the status line (countdown ticks locally from `graceMs`);
  `Submit`: `Rules.Validate(command).Ok` else refuse locally; then `Send("match.command", …)`;
  `Resign`: send a `ResignCommand`; `TurnSecondsRemaining` = anchored remaining minus elapsed since receipt,
  never negative; `TurnSecondsTotal = turnMs / 1000`.
- `MatchSession` picks the driver in `Start`: `NetClient.Instance != null && NetClient.Instance.PendingMatch != null`
  → online, else local (Editor practice). Then it builds the board for the driver's map
  (`BoardView.Build(string mapId)`, new overload; set `_buildOnAwake` to false on `/Board` in the Arena
  scene with `set_serialized_field`, and keep `Build()` working for anything else). All `_state.` reads
  become `_driver.Rules`, all `_view` reads `_driver.View`; `Submit` goes through the driver; the clock
  fields read the driver; `IsPlaying` gating of the clock stays only in the local driver (the server does
  not wait for animations; the online rope may reach zero while a hit still plays, which is correct).
- `Resynced`: clear the playback queue, snap both `UnitView`s to the view positions, rebuild prop views
  (destroyed ones removed), refresh HUD, disarm.
- After `MatchEndedEvent`: banner text as today plus a reason line ("by elimination", "by resignation",
  "opponent left", "you resigned"); after 3 s a **Back to lobby** button (online: `NetClient.LastResult`
  set, `SceneManager.LoadScene("Lobby")`; local: reloads the Arena scene).
- Keep `[Tooltip]`s current; log prefix becomes `[MatchSession]`.

### 7.4 `IMatchHudSource` and `MatchHudView`

Add to the interface: `string OpponentName`, `string OpponentStatus`, `bool CanResign`, `void Resign()`,
`string BannerDetail`, `bool ShowBackToLobby`, `void BackToLobby()`. `MatchHud.uxml` gains, next to
`end-turn`, a `resign` button (`Button`, class `resign`; first click turns its label to "Confirm resign"
for 3 s, second click calls `Resign()`, disabled when `!CanResign`), a `status-line` label under
`turn-panel` (hidden when `OpponentStatus` is null), a `banner-detail` label under `banner`, and a
`banner-button` ("Back to lobby", hidden until `ShowBackToLobby`). `turn-owner` shows "YOUR TURN" /
"<OpponentName>'S TURN" (upper-cased name, ≤ 16 chars, else truncated with "…"). USS: match the existing
palette and sizes; nothing else in the HUD moves.

### 7.5 Lobby scene

`unity command create_scene --path Assets/_Game/Scenes/Lobby.unity`, then build (all through commands):

| Object | Components / fields |
|---|---|
| `Main Camera` | `Camera` (solid colour background, dark), `AudioListener` |
| `Content` | `ContentBootstrap` with the same `GameDataManifest` asset the Arena uses |
| `Net` | `NetClient` |
| `LobbyUI` | `UIDocument` (source `Assets/_Game/UI/Lobby.uxml`, the existing `MatchHudPanelSettings`), `LobbyView` |

`UI/Lobby.uxml` + `UI/Lobby.uss` (UI Toolkit, screen-space, centred column ≤ 480 px): title "MIMAS",
`name` `TextField` (prefilled from `NetClient.PlayerName`, else `Guest-<4 digits>`), `preset`
`DropdownField` (choices from `LoadoutPresets`), `play-bot` `Button` ("Play vs bot"), `find-match`
`Button` ("Find match" ↔ "Cancel" while queued), `status` `Label` ("Connecting…", "In queue · 12 s",
"Match found · loading…", "Server unreachable · retrying", "Match lost", ""), `last-result` `Label`
("Victory vs Random Bot · by elimination"), `server` small `Label` showing the resolved URL.

`UI/LobbyView.cs` (`Mimas.Client.UI`): binds the elements, on either button: save the name
(`PlayerPrefs` + `NetClient.PlayerName`), `Connect()` if needed, `Authenticate(name)`, then
`bot.play` / `queue.join` with the preset's loadout; on `queue.status` update the label; on `match.start`
set `status` to "Match found · loading…", disable buttons, `SceneManager.LoadSceneAsync("Arena")`. On
`Awake`, if `NetClient.CurrentMatchId != 0` (page reload mid-match): "Reconnecting…", `Connect()` +
`Authenticate`, and load the Arena when `match.start` arrives, or show "Match lost" on `MatchLost`.
Buttons are disabled while a request is in flight. Log prefix `[LobbyView]`.

### 7.6 Loading

`SceneManager.LoadSceneAsync("Arena")` with the lobby's status label as the loading line (the Arena is
small; a progress bar is not worth it). The Arena's `MatchSession` reads `NetClient.Instance.PendingMatch`
in `Start` and clears it. `Lobby` is loaded again with `LoadScene` after a match; the `NetClient` survives
(`DontDestroyOnLoad`) and the Lobby's own `Net` object destroys itself as a duplicate.

### 7.7 `LoadoutPresets.asset`

`Presentation/Match/LoadoutPresets.cs`: `ScriptableObject` with `List<Entry> { string Name; LoadoutSettings Loadout }`.
Create `Assets/_Game/Settings/LoadoutPresets.asset` via `create_asset` and fill through
`set_serialized_field` with exactly:

| Name | weapon | crown | boots | armour |
|---|---|---|---|---|
| Longbow · Leaping | `longbow` | `ember-circlet` | `leaping-boots` | `leather-jerkin` |
| Longbow · Blink | `longbow` | `ember-circlet` | `blink-boots` | `leather-jerkin` |
| Flintlock · Leaping | `flintlock` | `ember-circlet` | `leaping-boots` | `leather-jerkin` |
| Flintlock · Blink | `flintlock` | `ember-circlet` | `blink-boots` | `leather-jerkin` |

The bot's own loadout is the server's business (§5.3: it uses the second preset's items, `flintlock` /
`blink-boots`, hard-coded in `ServerOptions.BotLoadout`).

### 7.8 `MatchSettings`

Unchanged fields; `TimeControlId` becomes unused by the driver (keep the field, update its tooltip:
"unused since M2; the turn length is `rules.clock.turnMs` unless overridden below"). `ResolveTurnSeconds`
reads `catalog.Rules.Clock.TurnMs` instead of the time control.

### 7.9 Build settings (the one allowed `ProjectSettings` change)

`unity command add_scene_to_build --path Assets/_Game/Scenes/Lobby.unity --enabled true` then the same
for `Arena.unity`; verify with `get_build_settings` that Lobby is index 0. Commit
`ProjectSettings/EditorBuildSettings.asset` with the scene and say so in the commit message.

### 7.10 `link.xml`

Already preserves `Mimas.Core` and `Newtonsoft.Json`. Add nothing unless the Web build strips a
NativeWebSocket type (then preserve that assembly and note it).

### 7.11 Serving the Web build from the server (optional, last)

If `MIMAS_WEB_PATH` is set, `Program.cs` serves that directory at `/` with `UseDefaultFiles` +
`UseStaticFiles` and an `OnPrepareResponse` that, for `.br` files, sets `Content-Encoding: br` and the
type of the inner extension (`.wasm` → `application/wasm`, `.js` → `application/javascript`, `.data` →
`application/octet-stream`), `Cache-Control: no-store`. Then `unity build MimasClient --profile "Web Release"
--output-path Build/Web` and `MIMAS_WEB_PATH=Build/Web dotnet run --project server/Mimas.Server` gives
`http://localhost:7777/` a playable page whose socket defaults to `ws://localhost:7777/ws`. Browsers accept
`br` on `localhost` without TLS. If the build or the page fails, report it and leave the server change in
(it is inert without the variable).

---

## 8. In-Editor and browser verification (do all that the environment allows; capture what you see)

Start the server in the background first (`dotnet run --project server/Mimas.Server`, wait for `/health`).

1. **Local practice unchanged.** Open `Arena.unity` directly, Play, one move, one attack, End Turn;
   console clean. `artifacts/online-local-practice.png`.
2. **Bot match online.** Open `Lobby.unity`, Play; through `eval` invoke `LobbyView`'s play-bot handler
   (reflection); Arena loads; `status-line` empty; the rope runs from 30 s; submit a move and an attack
   through reflection on `MatchSession` as earlier sessions did; the bot answers; server log shows the
   commands. `artifacts/online-lobby.png`, `artifacts/online-match.png`.
3. **Timeout.** Wait past 30 s without acting: the server ends the turn, the bot plays, your rope
   restarts. Read the turn number through `eval` to confirm.
4. **Rejected command.** Submit an off-turn command through reflection bypassing the mirror check; the
   client logs the `match.rejected` reason and nothing changes on the board.
5. **Resign.** Two clicks on the resign button (invoke the handler twice); "DEFEAT · you resigned";
   Back to lobby returns to the Lobby with the result line. `artifacts/online-result.png`.
6. **Reconnect.** In a fresh bot match, `taskkill` the server: status shows "Reconnecting…"; restart the
   server: the client authenticates, the room is gone, "Match lost", Back to lobby. Then, if a Web build
   exists (§7.11): play a bot match in the browser, refresh the tab mid-match, and confirm the same
   match resumes with the board where it was. `artifacts/online-reconnect.png`.
7. **Two clients.** Editor in Play from the Lobby with **Find match**, browser tab with **Find match**;
   both load the Arena with different `youAre`; a full match to the end; the loser sees DEFEAT, the
   winner VICTORY. If no Web build could be made, run the two-human server test instead and say so.

Console must show no `error CS`, no `Exception`; `[NetClient]` lines only at state changes.

---

## 9. Documentation updates

### 9.1 `docs/data.md`

Rules section: add `clock` to the JSON block and a paragraph; the table row for `timecontrols.json` says
"loaded, unused until time controls return (design: #time-controls)". Match flow section: mention
`ResignCommand`, `MatchEndReason`, `MatchState.FromView` (link ADR-026) and `Mimas.Core.Protocol.Wire`.

### 9.2 `docs/networking.md`

Rewrite the transport, envelope, message tables, hidden information, clocks and reconnect sections to
match §5.1 and §5.3 exactly (this file is the wire reference from now on). Keep the "Choice" section.

### 9.3 `docs/architecture.md`

Server table: replace the placeholder rows with the real components (§5.2). Client table: add
`Net` (`NetClient`) and `Lobby`; note the presenter / driver split and that Core comes into the client for
the mirror. Determinism contract: add "the client mirror is rebuilt from views, never advanced by commands".

### 9.4 `docs/design/index.html`

The page already carries the decisions (17 Sep rows in the decision log, ADR-026..028 references,
`#online` decided, `#time-controls` proposed, `#turns` idle penalty deferred, `#presentation` Lobby
subsection, `#hidden-info` mirror rule, `#bots` server bot). Your job is status only:
`#online` → `data-impl="implemented"` for the M2 scope (add a `.drift-note` listing what of the section
is later: ratings, pools, accounts); `#time-controls` stays `proposed` / `not-started` with its note;
`#win-conditions` → resign and forfeit implemented, series score still not; `#hidden-info` stays
`partial` (note the mirror shipped); `#bots` → `partial` note updated; `#presentation` Lobby →
implemented; roadmap row M2 → "done <date>"; changelog row "M2 shipped: …" with the test counts. Log a
decision-log row only for a decision you had to make yourself (§12 forks do not count).

### 9.5 `docs/roadmap.md`

M2 row status "done <date>"; a "M2 progress (<date>): the online slice" section in the style of the
existing ones (what shipped, numbers, not done / deferred, what the play test caught); baseline rows for
Core and server test counts. Next: M3 (elements, lineages, boons, draft, session wrapper) or a balance
pass; do not decide, list both.

### 9.6 `docs/decisions.md`

ADR-026..028 exist. Add an ADR only for an architectural choice you made that they do not cover.

### 9.7 `CLAUDE.md`

Repo layout row for `server/` mentions `Mimas.Server.Tests`; Commands block adds
`dotnet test server/Mimas.Server.Tests` and `MIMAS_WEB_PATH`; Live-Editor workflow unchanged. Golden rule
6 gains "(the client's `MatchState` is a mirror built from a `PlayerView`, ADR-026)".

---

## 10. Verification commands (run all before the final commit)

```bash
dotnet build Mimas.slnx
dotnet test shared/Mimas.Core.Tests            # ≥ 320, green
dotnet test server/Mimas.Server.Tests          # all green, < 30 s
grep -rn "UnityEngine\|System.Threading.Tasks\|DateTime\|Guid\." shared/Mimas.Core/Runtime/Protocol shared/Mimas.Core/Runtime/Match | grep -v "///"   # nothing
grep -rn "float\|double" shared/Mimas.Core/Runtime/Protocol shared/Mimas.Core/Runtime/Data/ClockDef.cs       # nothing
grep -rn "MatchState.Apply\|\.Apply(" MimasClient/Assets/_Game/Presentation/Match/OnlineMatchDriver.cs       # nothing
grep -rn "System.Net.WebSockets\|Task.Delay" MimasClient/Assets/_Game                                            # nothing
curl -s http://localhost:7777/health          # rooms, queued, players present
unity command console                          # no error CS, no Exception
```

---

## 11. Commit plan (each step green before committing; `git add` only the files you touched)

1. `core: clock block in rules, resign command, forfeit end reason` (§3, §4.1, §4.2, their tests)
2. `core: wire codec for commands, events and views` (§4.4, `ProtocolTests`)
3. `core: mirror match state built from a player view` (§4.3, `MirrorTests`)
4. `server: connections, guest auth, rooms with a turn deadline and a bot seat` (§5 without reconnect / forfeit / queue; `Mimas.Server.Tests` project with the auth, bot, command, timeout tests; `Mimas.slnx`)
5. `server: matchmaking queue, reconnect grace, forfeit, resign, resync` (rest of §5 and §6.2)
6. `docs: networking and architecture for the online server` (§9.1–9.3 server parts; can be folded into 5)
7. `client: net client with token resume and pending match` (§7.1, §7.2)
8. `client: match session split into presenter and drivers; online driver` (§7.3, §7.8, `BoardView.Build(mapId)`)
9. `client: hud resign, opponent status, result detail and back to lobby` (§7.4)
10. `client: lobby scene, loadout presets, scene list` (§7.5–7.7, §7.9; includes `ProjectSettings/EditorBuildSettings.asset`)
11. `server: serve the web build when MIMAS_WEB_PATH is set` (§7.11, optional)
12. `docs: design statuses, roadmap and data for the online slice` (§9.4–9.7) plus `artifacts/*.png`

---

## 12. Defaults for forks the session may hit

| If… | Then… |
|---|---|
| `CommandRejectReason` has no "match over" member | Add `MatchOver` and use it for every command after `IsOver`; tests that expected another reason for post-match commands are updated and the change is named in the commit. |
| `Unit` cannot be built with an explicit ability list without duplicating the item logic | Build it from items as today, then `RemoveAbility` / `RemoveModifier` the ones the view hides; keep `HiddenAbilityCount` per source item so the "?" slots are reproduced. |
| `DamageCalculator.Compute` needs to know about hidden counts | Do not touch the calculator; patch the breakdown in `MatchState.PreviewAttack` only. |
| `PlayerView` has no public constructor | Add `PlayerView.Create(...)` mirroring `Build`'s private constructor; keep `Build` as is. |
| `TestFixtures` build a `RulesDef` without a clock | Defaulted `ClockDef` parameter (§4.1). |
| NativeWebSocket's asmdef has a different name or none | If none, its scripts are in the package's runtime assembly; reference that assembly's asmdef name; if the package is not resolvable in the Editor, stop the client half and report (manifest edits are not allowed by this spec). |
| `DispatchMessageQueue` does not exist on the installed version | Call whatever the installed version documents for main-thread delivery in the Editor; the WebGL path is callback-driven regardless. |
| `PeriodicTimer` / `Channel<T>` unavailable | They are in .NET 10; if the project targets lower, use `System.Threading.Timer` and `SemaphoreSlim(1,1)`. |
| `WebApplicationFactory` cannot see `Program` | Add `public partial class Program { }` at the end of `Program.cs` (top-level statements) and an `InternalsVisibleTo` is not needed. |
| Tests cannot find the data folder | Walk up from `AppContext.BaseDirectory` until a directory containing `Mimas.slnx`; set `MIMAS_DATA_PATH` before the factory builds the host. |
| The bot's `Choose` returns null on its turn | It only happens when nothing is legal; submit `EndTurnCommand(1, Player)` for it. |
| A `TurnStartedEvent` for the bot arrives in the same batch as the human's end turn | Re-arm the deadline from the *last* `TurnStartedEvent` in the batch; `botDue = now + BotThinkMs`. |
| Renaming `LocalMatchSession.cs` through `rename_asset` fails | Do it with `move_asset` to the new path; if both fail, keep the file name and rename only the class after checking the scene still references it (`get_component_properties` on `/Session`). |
| `set_serialized_field` cannot set `_buildOnAwake` | Use `eval` with `SerializedObject` on the `/Board` component; save the scene. |
| `add_scene_to_build` is missing | `eval`: `UnityEditor.EditorBuildSettings.scenes = new[] { new UnityEditor.EditorBuildSettingsScene("Assets/_Game/Scenes/Lobby.unity", true), … }`. |
| The Web build fails or takes more than 20 minutes | Skip §7.11's build, keep the server change, run the two-human server test for step 7 of §8, report. |
| The browser refuses `Content-Encoding: br` from `localhost` | Report it; do not change the build profile or compression settings. |
| `PlayerPrefs.Save()` throws on WebGL in the Editor path | Guard with try/catch; the Editor stores prefs in the registry anyway. |
| `MatchState.FromView` per message is measurably slow (> 5 ms in the Editor profiler) | Cache the built `TileMap` per map id inside the mirror factory; the rest is two units and a few props. |
| You need a rule the design page does not have | Pick the smallest option, add it as a `proposed` bullet with an `<li id="q-…">` open question, continue. |

---

## 13. Definition of done (copy this list into the final report with ticks)

- [ ] `dotnet build Mimas.slnx`, `dotnet test shared/Mimas.Core.Tests` (≥ 320) and `dotnet test server/Mimas.Server.Tests` green; every §10 grep empty.
- [ ] Data: `rules.json` has `clock`; schema updated; nothing else in `Data/` changed.
- [ ] Core: `ClockDef`, `RulesDef.Clock`, `ResignCommand`, `ResignReason`, `MatchEndReason.Resign/Forfeit`, `MatchState.FromView` / `IsMirror`, `Unit.FromView` / `Restore` / hidden counts, `Prop.Restore`, `Mimas.Core.Protocol.Wire` / `Messages` / `WireException`, `PlayerView.Create`.
- [ ] Server: `WsConnection`, `PlayerRegistry`, `Matchmaker`, `RoomRegistry`, `Room`, `RoomClock`, `Seat`, `ServerOptions`; `/health` reports `rooms`, `queued`, `players`; never serialises a `MatchState`; `Mimas.Server.Tests` in `Mimas.slnx`.
- [ ] Client: `NetClient`, `IMatchDriver`, `LocalMatchDriver`, `OnlineMatchDriver`, `MatchSession` (renamed), `BoardView.Build(mapId)`, HUD resign / status / detail / back button, `Lobby.unity` + `Lobby.uxml/uss` + `LobbyView`, `LoadoutPresets.asset` with the four presets, scene list Lobby (0) + Arena (1); local practice still works from the Arena scene.
- [ ] §8 steps done with `artifacts/online-*.png`, or each skipped step named with the reason.
- [ ] Docs: `data.md`, `networking.md`, `architecture.md`, design page statuses + changelog + roadmap row, `roadmap.md` section and baselines, `CLAUDE.md`.
- [ ] Commits on `main` as in §11; nothing pushed; `ProjectSettings/EditorBuildSettings.asset` is the only `ProjectSettings` change and it is named in its commit.
- [ ] Final report: test counts, every §12 default taken, what was verified in the Editor and in a browser, anything unverified or left undone, and the one-line hand-off "M2 shipped; next is M3 or a balance pass".
