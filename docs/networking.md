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
is `[q, r]`. A hidden ability, modifier or boon id is `null` and stays `null`.

A unit in a view carries, beside `items`, `abilities` and `modifiers`: `lineage` (a string, or `null`
until revealed) and `boons` (an array of `{ id }` in grant order, `id` `null` while hidden). Two event
types joined the twelve: `boonRevealed { unitId, boonId, toPlayer }` and `lineageRevealed { unitId,
lineageId, toPlayer }`, routed to `toPlayer` only. Damage lines gained two kinds, `boonStat` (a Blessing's
stat or an Enchant's damage override, `id` = the boon) and `nullify` (an immunity, `id` = the modifier).
The session's own five events, its view and the draft pick joined them in part 2 (ADR-036); see
**The session on the wire** below.

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
friend can take the seat. `matchId` is the room's id and is reused across series; **`series`** in
`match.start` counts them, 1 for the first, and `round` counts the rounds inside one (ADR-036 supersedes
ADR-032's meaning of `round`). Both pressing Ready starts the next **series**; within one, the rounds
follow each other with a draft between them and the room goes back to `Waiting` only when the series
ends. Closing the tab in a waiting room frees the seat as it always did.

### Client → Server

| `t` | Payload | Reply / effect |
|---|---|---|
| `auth.guest` | `{ name }` (1..24 chars, trimmed; empty → `Guest-<4 digits>`) | `auth.ok` |
| `auth.resume` | `{ token }` | `auth.ok` (with `room` when the seat is in a room that is waiting), and a fresh `match.start` if the seat is still live, or `room.state` if the room is between matches; unknown token → `error { code: "bad_token" }` |
| `room.create` | `{}` | `room.state`; you are seat 0 |
| `room.join` | `{ code }` | `room.state` to both seats |
| `bot.play` | `{}` | `room.state` with seat 1 = `Random Bot`, already ready |
| `room.loadout` | `{ loadout: { weapon, crown, boots, armour }, lineage, ready }` | `room.state` to both; `match.start` when both seats are ready. A missing or unknown `lineage` is `bad_loadout` and nothing is stored |
| `room.leave` | `{}` | `room.left` to you, `room.state` to the other seat |
| `match.command` | `{ matchId, command }` | `match.events` to both seats, or `match.rejected` to the sender. `command` may be `{ type: "draftPick", player, offerIndex, reason }` between rounds |
| `match.resync` | `{ matchId }` | `match.view` |
| `pong` | `{ t }` | updates the connection's round-trip estimate |

### Server → Client

| `t` | Payload |
|---|---|
| `auth.ok` | `{ playerId, token, name, room? }` — `room` is the four-letter code, present only when the resumed player is seated in a room that is waiting |
| `room.state` | `{ code, youAre, seats: [ { name, ready, bot, present } … ] }`, always two entries |
| `room.left` | `{}` |
| `match.start` | `{ matchId, series, round, seq, mapId, youAre, opponentName, view, session, clock, events }` — `series` counts the best-of-3s played in the room, 1 for the first; `round` the round inside one. `events` = the filtered batch that started the round (a `roundStarted` and a `turnStarted` at least, plus the preceding `draftPicked` from round 2), empty on a reconnect. `view` is `null` in a draft; `mapId` is then the next round's map |
| `match.events` | `{ matchId, seq, events, view, session, clock }` — `view` `null` between rounds |
| `match.rejected` | `{ matchId, reason, view, session, clock }` |
| `match.view` | `{ matchId, seq, view, session, clock }` |
| `opponent.status` | `{ matchId, connected, graceMs }` |
| `ping` | `{ t }` |
| `error` | `{ code, message }` |

Error codes: `unauthenticated`, `unknown_type`, `bad_json`, `bad_token`, `in_match`, `in_room`,
`not_in_room`, `no_such_room`, `room_full`, `bad_loadout`, `unknown_match`.

`seq` increments per room per outgoing batch (`match.start` of a fresh room is `0`). It is for logs and
for dropping a batch that was in flight when a socket died — not for gap replay, because a reconnect
always brings a full view.

A seat's chosen **lineage** is never sent to the other seat at all — not in `room.state`, not in a view
until something reveals it (`#lineage` rule 3). A seat's chosen loadout is **not** sent to the other seat
before the match starts. Items are public once
it begins (they are in every `PlayerView`), but showing a preset during selection would invent a
counter-pick rule the design page does not have — open question `q-online-room-loadout`.

### The session on the wire (ADR-036)

A room hosts one **session** — a best-of-3 with a draft between its rounds — for the whole of its
`Playing` phase. It rides the channels that already existed rather than six new message types:

- **The five session events travel in `events`,** beside a round's own: `roundStarted`, `roundEnded`,
  `draftStarted`, `draftPicked`, `sessionEnded`. One filter (`SessionEventFilter.ForPlayer`), one codec.
- **A `session` block sits beside `view`** on `match.start`, `match.events`, `match.rejected` and
  `match.view`:

  ```json
  { "viewer": 0, "round": 2, "phase": "draft", "score": [1, 0], "roundsToWin": 2,
    "isOver": false, "winner": -1, "mapId": "arena-4", "nextMapId": "arena-4",
    "myBuild": { "loadout": { … }, "lineage": "hindu", "boons": ["vayu-breath", "agni-crown"] },
    "opponentLineage": null, "opponentBoons": [ { "id": null }, { "id": "thor-vigour" } ],
    "myOffers": ["agni-warmth", "agni-crown", "vayu-wings"], "iHavePicked": false, "opponentHasPicked": true }
  ```

  `phase` is `round` / `draft` / `over`. `winner` is `-1` while it runs. A view is only ever attached
  while `phase` is `round`; a session in any other phase carrying one is a malformed frame.
- **A draft pick is a command**, `{ type: "draftPick", player, offerIndex, reason }` through
  `match.command`, with `reason` `player` or `timeout`. The server submits the timeout on a seat's behalf
  when the draft deadline passes, exactly as it submits a turn timeout.
- **Every round begins with its own `match.start`.** The split rule is one line on the server: a batch
  containing a `roundStarted` goes out as `match.start`, everything else as `match.events`. The client's
  rule is as short — a `match.start` whose `round` is not the one on screen reloads the Arena, and the
  fresh scene consumes the start that is waiting for it, which is the path a first match start already
  took.
- **The draft is a state of the board, not a screen of its own.** Between rounds `view` is `null`, the
  client's mirror is null with it, and the board it already has is dimmed with the cards over it. A
  reload lands back in the draft with the seconds that are left.
- **Resign and forfeit end the series**, not the round (`#session` rule 7, P8). A resign in a draft
  scores nothing; a resign in a round scores that round first and then stops.

## Hidden information

The server holds `MatchState` (the truth) and **never serialises it**. Everything that leaves a room is
either a `PlayerView` for one seat or a list of events passed through `EventFilter.ForPlayer` for that
seat. One place in the code does both (`Room.Broadcast`), which is the guarantee as a location rather
than a promise; `TwoHumans_MirrorPlayersPlayToTheEnd` asserts every view a player ever received was
their own.

- Own units: everything, including `lineage` and every boon id. The `session` block adds your own offers
  in a draft, your build, and whether each seat has picked.
- Enemy units: position, gear, hit points, action points, heights — and **revealed** abilities,
  modifiers and boons only, plus the `lineage` once it is revealed (else `null`). A hidden entry keeps its
  slot with a `null` id, so the opponent can see *that* there is
  something and not *what*: for an ability the granting item is still named (which item you wear is
  public, what it does for you is not), and the number of boons is public while their identity is not.
- An ability is revealed the first time it is used in front of the other player; a hidden modifier the
  first time it changes a number; a boon when its stat or modifier changes a number, when its Sigil's
  ability is used, when an observation contradicts what the opponent knew, or at round start for a Health
  or AP Blessing (`boonRevealed`); the lineage with the first boon of it (`lineageRevealed`). Reveals are per
  viewer and persist for the session.
- A hidden line never appears in a damage breakdown the viewer has not earned:
  `HiddenInfo_NoHiddenLineLeaksBeforeReveal` scans a whole round to check it.
- **The draft is hidden both ways.** `session.myOffers` only ever lists that seat's three;
  `draftStarted` carries the other seat's `offers` as an explicit `null`, which is not the same as an
  empty list; the opponent's `draftPicked` says a pick was made and never which
  (`boonId: null`); and `session.opponentBoons` keeps one entry per boon they hold with a `null` id until
  a reveal names it, so the count is public and the identity is not.
  `HiddenInfo_SessionBlockNeverCarriesTheOtherSeatsOffersOrPick` sweeps every message of a whole series.

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
- **In a draft it is the same shape in a second mode:** `activePlayer: -1`, `turnMs` =
  `rules.draft.timeoutMs`, and one deadline for both seats rather than a turn each. It has no lag grace —
  nobody is racing a move — and its expiry makes the server keep offer 0 for whoever has not chosen.
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
chat, move buffering across a reconnect, tiers, rerolls, a character-select screen, and presets stored on
the server.

One gap worth naming: `auth.ok.room` covers a resume that arrives while the seat is still held — a
second tab, or a reconnect the server has not yet seen the close for. A **full page reload** after a
result closes the socket first, and closing a socket in a waiting room frees the seat, so that player
comes back as a stranger and needs the code again. Holding a seat for a grace between matches is a
design question (how long does a room wait for you?) and is Rohan's.
