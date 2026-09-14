# Networking

## Choice

Custom **ASP.NET Core WebSocket server** + **NativeWebSocket** client (WebGL-compatible via jslib) + **JSON** (Newtonsoft on client; server can use Newtonsoft too for byte-identical DTOs). See `decisions.md` ADR-002 and `research/multiplayer-stack.md` for the comparison.

Why not the alternatives (short): Photon can't be self-hosted at indie prices and Quantum leaks hidden state to every client; Nakama/Colyseus force server rules in Go/TS (no shared C#, no `dotnet test` on the authoritative code); NGO/Mirror/FishNet need one headless Unity process per match; SpacetimeDB's row-level security is still experimental.

## Transport

- `wss://<host>/ws` behind nginx (see `hosting.md`). Plain `ws://localhost:7777/ws` in development.
- Heartbeat: client sends `ping` every 25 s; server closes after 60 s silence. (nginx `proxy_read_timeout` raised to 3600 s anyway.)
- Reconnect: client keeps `sessionToken` + `matchId`; on reconnect server replays the current `PlayerView`.

## Message envelope

```json
{ "t": "queue.join", "p": { "timeControl": "3+2" } }
```

`t` = dotted message type, `p` = payload. Every message type has a DTO class in `Mimas.Core.Protocol` (shared).

### Client → Server

| `t` | Payload | Notes |
|---|---|---|
| `auth.guest` | `{ name }` | Returns `auth.ok { playerId, sessionToken }` |
| `queue.join` | `{ timeControl }` | |
| `queue.leave` | `{}` | |
| `match.command` | `{ matchId, seq, command }` | `command` is a `Mimas.Core.Command` (move / ability / end-turn / boon-pick / resign) |
| `match.resync` | `{ matchId }` | Ask for full `PlayerView` |
| `ping` | `{}` | |

### Server → Client

| `t` | Payload | Notes |
|---|---|---|
| `auth.ok` | `{ playerId, sessionToken }` | |
| `queue.status` | `{ position, waitedMs }` | |
| `match.start` | `{ matchId, seed, mapId, youAre, playerView, clocks }` | |
| `match.events` | `{ matchId, seq, events[], clocks }` | Events already filtered for this player (hidden info) |
| `match.view` | `{ matchId, playerView }` | Full resync |
| `match.end` | `{ matchId, winner, reason, seriesScore }` | |
| `series.boons` | `{ offers[] }` | Between rounds |
| `error` | `{ code, message }` | |
| `pong` | `{}` | |

## Hidden information

The server holds `MatchState` (truth). For each player it computes `PlayerView = MatchState.ViewFor(playerId)`:

- Own units: everything.
- Enemy units: position, class, HP (design decision pending), **revealed** abilities/modifiers only.
- An ability/modifier becomes revealed for the opponent the first time it is used or triggers. `Revealed` flags live in `MatchState` and persist for the whole series.

Events are filtered the same way: an `AbilityUsed` event for a hidden ability arrives at the opponent as `AbilityUsed` (now revealed, with full definition attached); a passive hidden modifier that has no visible effect produces no event for the opponent.

## Clocks

Server-side chess clocks per player; `clocks` (ms remaining per player + serverTime) are attached to every `match.events`. Client displays and predicts; server decides flag-fall.
