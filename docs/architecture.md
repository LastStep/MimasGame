# Architecture

## Overview

```
┌──────────────────────────┐   wss (JSON)   ┌──────────────────────────┐
│  MimasClient (Unity Web) │ ◄────────────► │  Mimas.Server (ASP.NET)  │
│  rendering, input, UI,   │                │  rooms by code, guest    │
│  audio, camera           │                │  auth, clocks, bot seat, │
│  ─ uses Core for:        │                │  hidden info             │
│    the MIRROR (ADR-026), │                │  ─ uses Core for:        │
│    local bot / practice  │                │    THE authoritative sim │
└───────────┬──────────────┘                └───────────┬──────────────┘
            │                                           │
            └──────────────  Mimas.Core  ───────────────┘
               pure C# rules engine, deterministic, JSON data, unit-tested
```

## Mimas.Core (shared/Mimas.Core)

| Area | Responsibility |
|---|---|
| `Hex` | Axial/cube coordinate struct: distance, neighbours, rings, lines, rounding (Red Blob Games) |
| `Grid` | `TileMap`: dictionary of `Hex → Tile`; tiles have terrain, height, effects, occupant. Any shape (ring, board) is just the set of hexes in the JSON map |
| `Pathfinding` | BFS/Dijkstra for movement range per movement type (walk / jump / fly / teleport), A* for paths |
| `Units` | `Unit` (owner, gear, lineage, boons, position, hp, ap, ability ids, modifier ids; `PublicStats` = base + items, `Stats` = plus boon stats, floored), `BoonOverlay` (a unit's boons folded once into tables, every entry tagged with its boon — ADR-034), `UnitSet` (occupancy; dead units occupy nothing), `Prop` |
| `Combat` | `AttackTargeting` (range band + line of sight), `DamageCalculator` -> `DamageBreakdown` (fixed-order signed lines, floor 0), `Knowledge` (full vs one player's view: preview and actual share one code path) |
| `Session` | `Session` (the best-of-N as a state machine **above** `MatchState`: builds, score, ladder, session-long reveals, the draft; constructs one round at a time from its own seeded `Rng` — ADR-035), `Draft` (offers as a pure seeded function), `DraftPickCommand` (a timeout is the same command with another reason), session events, `SessionEventFilter`, `SessionView` |
| `Match` | `MatchState` (one round's full truth; `Validate` pure, `Apply` sole mutator -> events, `EnumerateLegal`; `ResolveAbility`, the only place a unit's ability is read, with the boon overlay applied; also `FromView`, the client's read-only **mirror**, ADR-026), `Command`s (move / attack / end turn incl. timeout / resign incl. disconnect forfeit), `MatchEvent`s (incl. boon and lineage reveals), `PlayerView` (what one player may see), `EventFilter` (per-player event trimming), `MatchSetup`, `PlayerBuild` (gear + lineage + boons) |
| `Protocol` | `Wire` (hand-written JSON codec for commands, events and views), `Messages` (every wire type and error code as a constant), `WireException`. No attributes, no reflection, no `TypeNameHandling` (ADR-027) |
| `Bots` | `IBot` (`Choose`, `ChooseDraft`), `RandomBot` (uniform over `EnumerateLegal`, own RNG; keeps offer 0 in a draft) |
| `Data` | JSON parsers for terrains, rules (including the `clock` block both sides time a turn by, `elements`, `series`, `draft`, `boons`), maps, abilities (movement, attack incl. `element`), gear + stat blocks, modifiers (incl. `elements`, `itemKinds`, `nullify`), boons (the closed effect vocabulary), lineages, time controls into immutable definition objects |
| `Content` | `ContentCatalog`: loads the whole data folder from `(path, text)` pairs, links cross references, sorted `DefinitionTable<T>`s, content hash for client/server parity |
| `Movement` | `MovementDef` (data) + one `IMovementResolver` per geometry (walk / jump / teleport) behind a string-keyed registry; `MovePlan` = path to animate + tiles entered |
| `Rng` | Seeded deterministic RNG (xoshiro/PCG) |

Design rule: **Commands in, Events out.** `MatchState.Apply(Command) → IReadOnlyList<Event>`; the client animates Events; the server filters Events per player before sending. `Session.Apply` is the same shape one level up: it forwards round commands to the current `MatchState`, takes draft picks itself, and returns one list of round and session events.

Design rule: **definitions are immutable; a unit carries an overlay** (ADR-034). Rules code never reads a raw `AbilityDef` for a unit's ability; it asks `MatchState.ResolveAbility`, and every number a boon contributes is a breakdown line that names the boon, which is what reveal keys on.

## Mimas.Server (server/Mimas.Server)

| Component | Responsibility |
|---|---|
| `Program` | Loads the catalogue before it can serve, `/health` (rooms, waiting rooms, players), `/ws`, and optionally the Web build when `MIMAS_WEB_PATH` is set |
| `Net/WsConnection` | One socket: a capped text-only read loop, a channel drained by one writer so frames never interleave, server pings for the round-trip estimate, and which registry a message belongs to |
| `Players/PlayerRegistry` | Guests by id and by token. In memory only (D4) — no database, but a token that survives a page refresh |
| `Rooms/RoomRegistry` | Every open room, by match id and by four-letter code; owns code generation and who is allowed near which room |
| `Rooms/Room` | One `Session` — a whole best-of-3 — + two seats, all under one lock (ADR-036). Validates and applies commands (a draft pick among them), broadcasts per-seat views, session blocks and filtered events, and runs the tick in **two modes**: the turn deadline and the bot's move inside a round, one draft deadline and the bot's pick between rounds. The reconnect graces run in both, and expiry forfeits the series. **The split rule:** a batch carrying a `roundStarted` goes out as `match.start`, everything else as `match.events`, so every round begins with one. `Room.Series` counts the sessions played in the room; the round inside one is the session's |
| `Rooms/RoomClock` | A turn's deadline and the measured lag allowance. Pure arithmetic, testable without sockets |
| `Rooms/Seat` | Who is in a place, what they chose, whether they are ready, and the socket they are on — which outlives the connection, because that is what a reconnect grace is |
| Ratings, persistence | M5. There is no database in M2 and nothing survives a restart |

One process hosts many rooms (`Dictionary<MatchId, Room>`); a 1v1 turn-based room costs ~KBs, so a €4 VPS handles thousands.

## MimasClient (Unity)

| Layer | Tech |
|---|---|
| Rendering | URP 17 (Forward), WebGL2, low-poly stylised |
| Camera | Cinemachine 3.1.x — two `CinemachineCamera`s (tilted / top-down), blend on toggle |
| UI | UI Toolkit (UXML/USS) |
| Input | Input System 1.19 |
| Audio | FMOD for Unity 2.03 (banks loaded async in a loading scene; unlock on first user gesture) |
| VFX | Shuriken particle systems + Shader Graph |
| Networking | NativeWebSocket (jslib on WebGL) + Newtonsoft JSON |
| Assets | Addressables (LZ4) for anything not needed at first frame |

Folder layout: `Assets/_Game/{Content,Net,Presentation,UI,Audio,Data,Art,Scenes,Editor,Tests}` each with an `.asmdef`. Core comes in as local package `com.mimas.core` from `../../shared/Mimas.Core` (see `Packages/manifest.json`). `Content` holds the generated `GameDataManifest` asset and `ContentBootstrap`, the one place the client loads JSON; `Presentation` depends on it, on `Net` and on Core.

Two scenes: **Lobby** (build index 0) and **Arena** (1).

| Piece | Responsibility |
|---|---|
| `Net/NetClient` | The one socket, alive across scene loads. Owns the connection, the guest identity and `match.start`; knows nothing about matches or rules |
| `UI/LobbyView` | Name, `Play vs bot` / `Create room` / `Join room`, then the room: code, seats, loadout preset, Ready |
| `Presentation/MatchSession` | The presenter: board, HUD, aiming, playback, facing. Does not know whether the match is local |
| `Presentation/IMatchDriver` | Where the match comes from. `LocalMatchDriver` (the truth, the bot, the ADR-015 clock) or `OnlineMatchDriver` (the mirror, the server's clock) — ADR-028. Both carry a `SessionView` beside the round; `Rules` and `View` are **null between rounds**, and a `NextRound` event tells the presenter to reload the Arena for the next one |
| `Presentation/LocalSessionHost` | The practice series, `DontDestroyOnLoad`, so one `Session` and one bot outlive the Arena reload that every round after the first causes (P7). `LocalMatchDriver` is the per-scene adapter over it |
| `Presentation/MatchSession` | The presenter also owns the series' presentation: the score line in the turn panel, the round cards, the draft over the dimmed board (built from `SessionView.MyOffers`, sent back as `SubmitDraftPick`), the series banner, and the boon and lineage reveals |

**Core comes into the client for the mirror.** `OnlineMatchDriver` rebuilds a `MatchState` from the
`PlayerView` on every message and asks it every preview question, so the online game and the practice
game are answered by one implementation of the rules rather than two.

## Determinism contract

Same `(seed, map, commands)` ⇒ identical `MatchState` on client and server. Enables replays, spectating, bots, balance simulations, and cheap server-side validation.

Two rules keep it honest:

- **The client mirror is rebuilt from views, never advanced by commands.** It answers questions and
  refuses to be mutated (`Start`, `Apply`, `TryApply` all throw on a mirror), so it cannot drift.
- **Every clock decision is a command.** A timeout is `EndTurnCommand(Timeout)` and a disconnect forfeit
  is `ResignCommand(Disconnect)`, both submitted by the server through the same path a player uses — so
  replaying the command list reproduces the match with no clock and no sockets.
