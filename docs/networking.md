# Networking

**This file is the wire reference.** It describes what `Mimas.Server` and `Mimas.Core.Protocol` actually
do as of M2 (17 Sep 2026), not what they might one day do. Where it disagrees with the code, the code is
right and this is a bug.

## Choice

Custom **ASP.NET Core WebSocket server** + **NativeWebSocket** client (WebGL-compatible via jslib) + **JSON** (Newtonsoft on client; server can use Newtonsoft too for byte-identical DTOs). See `decisions.md` ADR-002 and `research/multiplayer-stack.md` for the comparison.

Why not the alternatives (short): Photon can't be self-hosted at indie prices and Quantum leaks hidden state to every client; Nakama/Colyseus force server rules in Go/TS (no shared C#, no `dotnet test` on the authoritative code); NGO/Mirror/FishNet need one headless Unity process per match; SpacetimeDB's row-level security is still experimental.

## Transport

- `wss://<host>/ws` behind nginx (see `hosting.md`) — in production `wss://mimas.laststep.cloud/ws`.
  Plain `ws://localhost:7777/ws` in development.
  A Web build works its own out: `?ws=<url>` in the page query wins, then the page's own scheme
  (`https:` → `wss://<host>/ws`, `http:` → `ws://<host>:7777/ws`). In the Editor the serialized field on
  `NetClient` wins, so one build can be pointed anywhere without a rebuild.
- One JSON **text** frame per message, UTF-8, **64 KB** cap. A binary or oversized frame closes the socket.
- Heartbeat: the **server** pings every `PingIntervalMs` (5 s) and the client answers `pong` immediately.
  The round trip measured this way is what the turn deadline's lag grace is drawn from. A connection that
  has not answered for `IdleCloseMs` (60 s) is closed.
- Reconnect: the client keeps its `token` and `matchId` in `PlayerPrefs`; `auth.resume` puts it back in its
  seat and replays the current `PlayerView` as a fresh `match.start`.

### Origins

`Mimas:AllowedOrigins` (configuration, not code) is the list of origins that may open `/ws`, written the
way a browser writes them — scheme, host, and a port if it is not the default, no trailing slash. **Empty
or unset means any origin**, which is development, the tests and a plain `dotnet run`. Production names
its one origin in `appsettings.Production.json`, so moving the site is a redeploy and not a VPS setup
(ADR-033). A handshake from anywhere else — or with no `Origin` header at all, which no browser does — is
`403` with a warning line naming the origin. The refusal is deliberately the server's and not nginx's: it
is in the log, and it is in the tests.

## Message envelope

```json
{ "t": "room.join", "p": { "code": "FROG" } }
```

`t` = dotted message type, `p` = payload. Every type and error code is a constant in
`Mimas.Core.Protocol.Messages`, shared by both sides, so neither can misspell one at the other.

Commands, events and views are encoded by `Mimas.Core.Protocol.Wire`: a hand-written codec with no
attributes, no reflection and no `TypeNameHandling` (ADR-027). Enums are lower-camel strings both ways
and are read case-sensitively — an unknown string is a malformed frame, never a silent default. A `Hex`
is `[q, r]`. A hidden ability or modifier id is `null` and stays `null`.

### How two players meet

**Room codes, not a queue** (OPT-0001, 17 Sep 2026). Four friends in two arranged pairs are not a
matchmaking problem: a queue pairs them in the order they click. A code is four characters from
`ABCDEFGHJKLMNPQRSTUVWXYZ23456789` (no `I`, `O`, `0`, `1` — it gets read aloud as often as pasted),
case-insensitive on the way in. A matchmaking queue returns at M5 with ratings.

Gear is chosen **in the room**, once you can see who you are playing, and the match starts when both
seats are ready.

**The room outlives the match** (ADR-032, design `#online` rule 9, 21 Sep 2026). A room's life is
`Waiting → Playing → Waiting → Playing → …`; it reaches `Over` only when nobody human is left in it, and
that is the only thing that forgets its code. After a result both seats keep their place and their gear,
`ready` goes back to `false` for the humans (the bot seat stays ready), and both pressing Ready again
starts the next match — there is no rematch offer and no session score, because the room *is* the offer.
A seat whose socket is gone at the result is **freed** rather than held: the reconnect grace is something
a match owes a player, and there is no match. So a friend who dropped can rejoin by code, and a different
friend can take the seat. `matchId` is the room's id and is reused across rounds; `round` in `match.start`
counts them, 1 for the first. Closing the tab in a waiting room frees the seat as it always did.

### Client → Server

| `t` | Payload | Reply / effect |
|---|---|---|
| `auth.guest` | `{ name }` (1..24 chars, trimmed; empty → `Guest-<4 digits>`) | `auth.ok` |
| `auth.resume` | `{ token }` | `auth.ok` (with `room` when the seat is in a room that is waiting), and a fresh `match.start` if the seat is still live, or `room.state` if the room is between matches; unknown token → `error { code: "bad_token" }` |
| `room.create` | `{}` | `room.state`; you are seat 0 |
| `room.join` | `{ code }` | `room.state` to both seats |
| `bot.play` | `{}` | `room.state` with seat 1 = `Random Bot`, already ready |
| `room.loadout` | `{ loadout: { weapon, crown, boots, armour }, ready }` | `room.state` to both; `match.start` when both seats are ready |
| `room.leave` | `{}` | `room.left` to you, `room.state` to the other seat |
| `match.command` | `{ matchId, command }` | `match.events` to both seats, or `match.rejected` to the sender |
| `match.resync` | `{ matchId }` | `match.view` |
| `pong` | `{ t }` | updates the connection's round-trip estimate |

### Server → Client

| `t` | Payload |
|---|---|
| `auth.ok` | `{ playerId, token, name, room? }` — `room` is the four-letter code, present only when the resumed player is seated in a room that is waiting |
| `room.state` | `{ code, youAre, seats: [ { name, ready, bot, present } … ] }`, always two entries |
| `room.left` | `{}` |
| `match.start` | `{ matchId, round, seq, mapId, youAre, opponentName, view, clock, events }` (`events` = the filtered start events, normally one `turnStarted`; empty on a reconnect. `round` is 1 for the first match in a room, 2 for the rematch, …) |
| `match.events` | `{ matchId, seq, events, view, clock }` |
| `match.rejected` | `{ matchId, reason, view, clock }` |
| `match.view` | `{ matchId, seq, view, clock }` |
| `opponent.status` | `{ matchId, connected, graceMs }` |
| `ping` | `{ t }` |
| `error` | `{ code, message }` |

Error codes: `unauthenticated`, `unknown_type`, `bad_json`, `bad_token`, `in_match`, `in_room`,
`not_in_room`, `no_such_room`, `room_full`, `bad_loadout`, `unknown_match`.

`seq` increments per room per outgoing batch (`match.start` of a fresh room is `0`). It is for logs and
for dropping a batch that was in flight when a socket died — not for gap replay, because a reconnect
always brings a full view.

A seat's chosen loadout is **not** sent to the other seat before the match starts. Items are public once
it begins (they are in every `PlayerView`), but showing a preset during selection would invent a
counter-pick rule the design page does not have — open question `q-online-room-loadout`.

## Hidden information

The server holds `MatchState` (the truth) and **never serialises it**. Everything that leaves a room is
either a `PlayerView` for one seat or a list of events passed through `EventFilter.ForPlayer` for that
seat. One place in the code does both (`Room.Broadcast`), which is the guarantee as a location rather
than a promise; `TwoHumans_MirrorPlayersPlayToTheEnd` asserts every view a player ever received was
their own.

- Own units: everything.
- Enemy units: position, gear, hit points, action points, heights — and **revealed** abilities and
  modifiers only. A hidden entry keeps its slot with a `null` id, so the opponent can see *that* there is
  something and not *what*. For an ability the granting item is still named: which item you wear is
  public, what it does for you is not.
- An ability is revealed the first time it is used in front of the other player; a hidden modifier the
  first time it changes a number. Reveals are per viewer and persist.
- A hidden line never appears in a damage breakdown the viewer has not earned:
  `HiddenInfo_NoHiddenLineLeaksBeforeReveal` scans a whole match to check it.

### The client mirror (ADR-026)

The client does not hold the truth. It rebuilds a knowledge-limited `MatchState` from the `PlayerView`
attached to every message (`MatchState.FromView`) and asks that mirror every preview question — reachable
tiles, range bands, `CheckTarget`, the damage preview and its "?" row. So:

- the same rules code answers in the online game and the local one, with no second implementation to drift;
- an illegal click is refused instantly, locally, and never becomes a round trip;
- the client can only ever know what the server chose to send.

It is **rebuilt** per message, not advanced by events: a view is two heroes and a handful of props, and a
state that is only ever replaced cannot fall out of step. Events are for animation only.

## Clocks

One flat turn, no bank and no increment (D5). The numbers are rules, in `rules.json`'s `clock` block, and
both sides read them:

```json
"clock": { "turnMs": 30000, "lagGraceMs": 1000, "reconnectGraceMs": 60000 }
```

- Every server message carries `clock { activePlayer, turnMs, remainingMs }`. The client counts down from
  receipt and re-anchors on every message, so the rope is smooth and the server's number always wins.
- The server ends a turn at `deadline + min(rtt, lagGraceMs)` by submitting
  `EndTurnCommand(reason: timeout)`. The allowance exists so a command that left the client before the
  deadline is never refused for arriving after it — the player did everything right and the network did not.
- A bot seat never times out.
- A dropped seat is held for `reconnectGraceMs`, **with the clock still running** — pulling the cable is
  not a way to buy time — and the other player sees a countdown. No return means the server submits
  `ResignCommand(reason: disconnect)` on that seat's behalf.

**Every clock decision is a command.** A timeout and a forfeit both go through the same rules path as a
move, so replaying a match's command list reproduces it exactly, with no clock and no sockets.

## What is not here yet

Time controls with banks and increments (`timecontrols.json` stays loaded and unused; design:
`#time-controls`), a matchmaking queue and ratings (M5), accounts and any database (M5), spectating,
chat, move buffering across a reconnect, and a best-of-three wrapper or any session score around the
rematch (the room's rounds are counted, nothing else is).

One gap worth naming: `auth.ok.room` covers a resume that arrives while the seat is still held — a
second tab, or a reconnect the server has not yet seen the close for. A **full page reload** after a
result closes the socket first, and closing a socket in a waiting room frees the seat, so that player
comes back as a stranger and needs the code again. Holding a seat for a grace between matches is a
design question (how long does a room wait for you?) and is Rohan's.
