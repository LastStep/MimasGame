---
project: mimas
milestone: M2
updated: 2026-09-17
updated_by: builder
---

# Where Mimas stands

> **Rewritten, never appended.** One page, always current. History lives in `runs/` and in git.

## Right now

**The match is on the server.** From the Editor you open a lobby, press **Play vs bot** or **Create
room**, get a four-letter code, pick your gear in the room, press Ready, and play a real match whose
truth lives in `Mimas.Server` — with a 30 s server-authoritative turn, resign, and a reconnect grace.
The client holds a *mirror* of the match rebuilt from its own `PlayerView` (ADR-026), so every preview
it already had works unchanged and it can only ever know what the server chose to send.

**321 Core tests and 34 server tests**, the latter playing whole matches over real sockets using the
client's own mirror as the test double. Both are ladder rungs, both required. Local practice — opening
`Arena.unity` directly — still plays a whole game against the bot, unchanged.

What does not exist yet: **anything on the internet**, and **any of it in a browser**. Everything above
was verified in the Editor against a server on this machine. The friends playtest is **Sat 10 Oct
2026** — 23 days away.

## Current milestone: M2 — online

Target: **Sat 10 Oct 2026**, friends playing over the internet.

The work order was `docs/specs/2026-09-17-online-slice.md` plus its **amendment A1** (room codes). It is
executed; the spec is now history and `docs/networking.md` is the wire reference.

| Done-when | State |
|---|---|
| M2-1 Two browsers play a full match through `Mimas.Server` | **done in tests and in the Editor**; not yet two browsers |
| M2-2 Two players who want to play each other end up in the same match | **done** — by a four-letter room code (OPT-0001, ADR-029) |
| M2-3 Guest auth with a resumable token; a reload keeps your identity | **done** (tested; browser reload untested) |
| M2-4 Server-authoritative 30 s turn; timeout arrives as EndTurnCommand(Timeout) | **done**, seen firing live |
| M2-5 Reload within the 60 s grace resyncs into the running match | **done** (tested server-side and in the Editor) |
| M2-6 Resign and disconnect-forfeit end the match correctly | **done**, both paths seen |
| M2-7 Deployed on the VPS over HTTPS, reachable by a friend | **not started** — this is the gap to 10 Oct |
| M2-8 Rematch without leaving the room | not started (out of scope by D9) |

**Nothing in the ledger is ticked.** `pass` belongs to a verifier, not to the builder who wrote the
code (`reward-hacking-guards.md`). M2-1 and M2-3..M2-6 are ready for one; M2-2's wording still says
"mechanism open" and wants updating to room codes.

## In flight

| Task | What | Status | Who |
|---|---|---|---|
| T-0002 | Execute the online slice | verify | builder |

## Blocked

| Task | Blocked by | Since |
|---|---|---|
| — | nothing | |

## Waiting on Rohan

| What | File | Since |
|---|---|---|
| Edit `pillars.md` — it is a draft distilled from the design page, and the pillars are yours | `studio/pillars.md` | 17 Sep 2026 |
| Optional: should a room show the other seat's chosen preset before the match starts? Hidden today | design page `#q-online-room-loadout` | 17 Sep 2026 |

## The two things that matter next

1. **A Web build, in a browser.** The WebGL-specific paths are written and *unexercised*: the jslib
   socket, `PlayerPrefs` reaching IndexedDB, `?room=` and `?ws=` read from the page URL, Brotli. This is
   the largest remaining unknown in M2 and it is cheap to find out — `unity build MimasClient --profile
   "Web Release"`, then `MIMAS_WEB_PATH=Build/Web dotnet run --project server/Mimas.Server` serves the
   page and the socket from one process (already implemented, inert without the variable).
2. **Deploy** (`F-deploy`, M2-7). Nothing about 10 October works without it.

Then: a verifier over T-0002, and M2-8 (rematch) if there is room.

From `E:\Studios\Trinetra-Game-Studio\docs\PLAN.md` §10: if two browsers cannot play a full match
through the server by **Fri 2 Oct**, the 10 Oct playtest falls back to the local build and online moves
to 17 Oct. On today's evidence that date is not at risk from the game code; it is at risk from deploy.

## Last playtest

**16 Sep 2026, Rohan, the aiming slice, Editor on arena-4.** Console clean. Two bugs caught in play,
both since fixed.

Nothing has been played by a human since the online slice landed. **The next playtest should be the
first one online**, even just Rohan against the server bot in a browser, because that is the thing no
test can tell us about.

## Next decision due

None open. OPT-0001 (room codes) was decided and executed on 17 Sep. The one live question is the
optional design nit above, which does not block anything.
