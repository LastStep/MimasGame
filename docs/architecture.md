# Architecture

## Overview

```
┌──────────────────────────┐   wss (JSON)   ┌──────────────────────────┐
│  MimasClient (Unity Web) │ ◄────────────► │  Mimas.Server (ASP.NET)  │
│  rendering, input, UI,   │                │  matchmaking, rooms,     │
│  audio, camera           │                │  clocks, hidden info,    │
│  ─ uses Core for:        │                │  ratings, persistence    │
│    legal-move preview,   │                │  ─ uses Core for:        │
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
| `Units` | `Unit` (owner, class, position, hp, ap, ability ids, modifier ids), `UnitSet` (occupancy; dead units occupy nothing) |
| `Combat` | `AttackTargeting` (range band + line of sight), `DamageCalculator` -> `DamageBreakdown` (fixed-order signed lines, floor 0), `Knowledge` (full vs one player's view: preview and actual share one code path) |
| `Match` | `MatchState` (full truth; `Validate` pure, `Apply` sole mutator -> events, `EnumerateLegal`), `Command`s (move / attack / end turn incl. timeout), `MatchEvent`s, `PlayerView` (what one player may see), `EventFilter` (per-player event trimming), `MatchSetup` |
| `Bots` | `IBot`, `RandomBot` (uniform over `EnumerateLegal`, own RNG) |
| `Boons` | Draft offers and application (M3) |
| `Data` | JSON parsers for terrains, rules, maps, abilities (movement, attack), classes + stat blocks, modifiers, time controls into immutable definition objects |
| `Content` | `ContentCatalog`: loads the whole data folder from `(path, text)` pairs, links cross references, sorted `DefinitionTable<T>`s, content hash for client/server parity |
| `Movement` | `MovementDef` (data) + one `IMovementResolver` per geometry (walk / jump / teleport) behind a string-keyed registry; `MovePlan` = path to animate + tiles entered |
| `Rng` | Seeded deterministic RNG (xoshiro/PCG) |

Design rule: **Commands in, Events out.** `MatchState.Apply(Command) → IReadOnlyList<Event>`; the client animates Events; the server filters Events per player before sending.

## Mimas.Server (server/Mimas.Server)

| Component | Responsibility |
|---|---|
| `WsEndpoint` | `/ws` WebSocket; envelope `{ "t": "<type>", "p": {...} }` |
| `Sessions` | Auth (guest token first; accounts later), connection ↔ player |
| `Matchmaker` | Queue per time-control; rating window widens with wait time |
| `Room` | One `MatchState` + two connections + clocks; validates and applies Commands; broadcasts filtered Events; handles reconnect |
| `Clocks` | Per-player chess clocks; server is the timekeeper |
| `Ratings` | Glicko-2 (planned) |
| `Persistence` | SQLite first (single VPS); Postgres if needed |

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

Folder layout: `Assets/_Game/{Content,Presentation,UI,Audio,Data,Art,Scenes,Editor,Tests}` each with an `.asmdef`. Core comes in as local package `com.mimas.core` from `../../shared/Mimas.Core` (see `Packages/manifest.json`). `Content` holds the generated `GameDataManifest` asset and `ContentBootstrap`, the one place the client loads JSON; `Presentation` depends on it and on Core only.

## Determinism contract

Same `(seed, map, commands)` ⇒ identical `MatchState` on client and server. Enables replays, spectating, bots, balance simulations, and cheap server-side validation.
