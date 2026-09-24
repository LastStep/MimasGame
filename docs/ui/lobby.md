# The lobby and the room

_Status: **built** (T-0014, 24 Sep 2026; chosen by Rohan on 23 Sep 2026 in the HUD session, canvas round 3). Design anchors:
`#online` (rules 1, 2, 3, 9), `#presentation` (Lobby), `#character-select` (the launch shape: a preset and a
lineage in the room), `#lineage`, `#hidden-info`. Language: `docs/ui/language.md`. Mock:
https://claude.ai/artifact/K9qZcJQ115G5G685sdMAwy, page "HUD", round 3: **"Lobby · the way in"** and
**"Room · two seats facing"** (chosen; "one column" and "the god first" were not). Work order:
`docs/specs/2026-09-23-hud-restyle.md`, task `T-0014`. Today's screens: `Lobby.uxml` / `Lobby.uss` /
`LobbyView.cs` (ADR-028, ADR-032, T-0010's lineage row); their behaviour stays, their look and the gear
picker change._

## 1. What it is for

The first thing a friend sees, and the last thing between two people and a match: a name, a way in, a code
to send, who is here, what you are wearing and who you pray to. It must say "you are in the right room with
the right person" at a glance. The full character select (M3-6) is later; this is the launch shape restyled.

## 2. The lobby (`lb.*`)

Ink, with three soft radial washes in the lineage dark hues (Greek, Hindu, Norse at 28–35%) behind
everything (`lb.wash`). One centred column, 420 wide.

| id | Shows | Data |
|---|---|---|
| `lb.title` | "MIMAS", display 38 at 0.6em, `fg` | — |
| `lb.last` | the last result: "VICTORY VS GUEST-2869 · SERIES 2 – 1", caps 11, `you` for a win, `them` for a loss; hidden when there is none. When the score does not explain the result (a resignation before the rounds decide it) the reason follows: "… · SERIES 0 – 0 · OPPONENT RESIGNED" | today's `last-result`; the series score is carried in `MatchResult` since T-0014 |
| `lb.name.caption`, `lb.name` | "NAME", caps 10 `fg-3`; a 420 × 52 field on `tile` with a 1px `hair` edge, `body` 15 | today's `name` |
| `lb.play-bot` | 420 × 52 primary: 1px `you` edge, `you` at 14%, "PLAY VS BOT" caps 14 | today's `play-bot` |
| `lb.create` | 420 × 52 plain: 1px bone at 22%, ink at 25%, "CREATE ROOM" | today's `create-room` |
| `lb.join.code`, `lb.join` | a 204 × 52 code field (display 20 at 0.5em, centred, "CODE" as its placeholder) and a 204 plain "JOIN ROOM" | today's `join-code`, `join-room` |
| `lb.status` | `body` 12 `fg-3`: "Type the code your friend sent you." and today's other strings | today's `status` |
| `lb.server` | the socket URL, `body` 10, bone at 30%, 28 from the bottom | today's `server` |

## 3. The room (`room.*`) · two seats facing

You are always on the **left** in `you`; they are always on the right in `them` — the same left and right as
the camera and the turn track, whichever seat you hold (the view knows `_mySeat`).

| id | Shows | Data | States |
|---|---|---|---|
| `room.code.caption` | "ROOM CODE", caps 10 `fg-3`, centred, 64 from the top | — | — |
| `room.code` | the four letters, display **56** at 0.5em (the only 56 in the game) | today's `room-code` | — |
| `room.copy-link`, `room.copy-code` | "COPY LINK", "COPY CODE": quiet buttons, caps 12 `fg-2`, 26 apart | today's handlers and `WebClipboard` | the confirmation goes to `room.status` as today |
| `room.you.caption`, `room.you.name` | "YOU", caps 11 `you`; your name, caps 28 `fg` | your seat | — |
| `room.gear.caption`, `room.preset[i]` | "GEAR", caps 10 `fg-3`; **one tile per preset**, 74 tall, sharing the 520 width, 8 apart: the weapon's and the boots' slot glyphs (18) over the preset's name (caps 10). Built as written: every tile shows the sword and the boot, the names tell them apart (the canvas drew per-item placeholder glyphs; item icons are an art slot) | `LoadoutPresets` (four today; the row takes however many there are) | selected: 1px `you` edge, `you` at 10%, text `fg`; others `hair` edge, `tile` ground, text `fg-3`; disabled while ready |
| `room.god.caption`, `room.lineage[i]` | "PRAY TO", caps 10 `fg-3`; three rows 520 wide, 6 apart: a 52 square in the lineage's hues with its emblem · the name caps 15 · its line (`body` 12) · a Blessing circle and "STARTS WITH ATHENA'S GUARD" caps 10 | the catalogue's first three lineages by id, as today | selected: 2px `you` left edge, `you` at 7%; the name and the Blessing line brighten; disabled while ready |
| `room.divider` | a 2px vertical `hair` line at the centre, fading at both ends, 420 tall | — | — |
| `room.them.caption`, `room.them.name` | "THEM", caps 11 `them`; their name, caps 28 `fg-2`; "WAITING FOR A PLAYER" in `fg-3` when the seat is empty | the other seat | — |
| `room.them.state` | a 9px `them` dot (filled when ready, hollow otherwise) and "READY" / "CHOOSING", caps 13 | the other seat's `ready` | — |
| `room.them.note` | "Their gear shows when the round starts. Their god shows itself.", `body` 12 `fg-3`, 300 wide | — | hidden while the seat is empty |
| `room.ready` | 320 × 58 primary, centred at the foot: "READY" / "NOT READY" | today's toggle | — |
| `room.leave` | quiet "LEAVE ROOM", caps 12 | today's handler | — |
| `room.status` | `body` 12 `fg-3`: "Pick your gear and your god.", "Send the code to a friend.", "Waiting for your opponent…", the after-result line, the copy confirmations | today's strings | — |

The other seat never shows their gear or their god (`#q-online-room-loadout` stays open, and hidden).
A bot's seat reads "RANDOM BOT" and "READY".

**Built (T-0014).** Rows that repeat are built by `LobbyView` under named containers with the ids above
(`room.preset[0]`… under `room.gear.tiles`, `room.lineage[0]`… under `room.god.rows`, each row's parts
`room.lineage[i].swatch` / `.emblem` / `.name` / `.line` / `.blessing`); the two sides are `room.you` and `room.them`.
The pure rules (which seat is drawn where, what the other seat says, the preset choice, the result line) are
`UI/RoomLayout`.

## 4. Behaviour kept from today

Everything in `LobbyView.cs`'s state machine: the three ways in, reconnecting, "Back in room X…", the
after-result line ("Victory · by elimination — Ready for another?"), Ready as a toggle that sends
`room.loadout`, a change of gear or god un-readying you, the lineage remembered in `PlayerPrefs`, Copy link
and Copy code through `WebClipboard`, "Match found · loading…". The preset choice still resets to the first
on a scene load, as today (remembering it is a follow-up, not decided).

## 5. Acceptance

Captures at 1920×1080 and 1280×720 against the canvas boards:

1. The lobby with a last result.
2. The room waiting for someone (their side: "WAITING FOR A PLAYER").
3. The room with both present, you choosing and them ready, a preset and a lineage selected.
4. The room as seat 2 (you still on the left).
5. The room after a result, with the after-result line.

## Drift (what the build does that this page does not say)

Both measured against the canvas on 24 Sep 2026 (T-0014).

- **Translucent fills draw brighter than the canvas.** The project renders in linear
  space, so a token such as `you` at 14% blends half as bright again as a browser blends it: Ready's fill is
  rgb(42, 90, 88) where the board has (31, 56, 60), the selected gear tile (34, 77, 74) against (22, 41, 44). The lobby's
  wash is mixed onto the ink in gamma values and drawn opaque, so it matches; the fills do not. The same holds for
  every translucent token in the HUD. Not decided: a follow-up in STATE.
- **No balancing padding on the wide-tracked words.** The canvas pads "MIMAS" and the
  room code on the left by their tracking; UI Toolkit adds no tracking after the last letter, so the padding would
  push them right. None is used.

## Change log

- 2026-09-23 · written from the HUD session (canvas round 3); status proposed.
- 2026-09-24 · built by T-0014 (`Lobby.uxml` / `.uss`, `LobbyView`, `RoomLayout`, `Ramps.LobbyWash` / `Swatch` /
  `BothEndsVertical`). Captures in `artifacts/t0014/shots/` (items 1–5 at 1920×1080 and 1280×720, from the Editor)
  and `artifacts/t0014/seat2-*/` (item 4 from the browser). Drifts above.
