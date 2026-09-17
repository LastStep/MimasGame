---
project: mimas
milestone: M2
updated: 2026-09-17
updated_by: builder
---

# Where Mimas stands

> **Rewritten, never appended.** One page, always current. History lives in `runs/` and in git.

## Right now

Mimas is playable offline against a bot, in the Unity Editor, on arena-4. A hero is four items rather
than a class; attacks aim at a point on a body and resolve against real heights, with line of sight and
three trajectory types; props (walls and pillars) are cover you can shoot down. 287 Core tests green.
Rohan last played it on **16 Sep** and won by elimination with a clean console.

What does not exist yet: **anything online.** `Mimas.Server` loads the same content and answers
`/health`, but no client has ever connected to it in a real match. That is all of M2, and M2 is the
only thing that matters right now, because friends are meant to play on **Sat 10 Oct 2026** — 23 days
away.

The studio (`E:\Studios\Trinetra-Game-Studio`) was installed into this repo today. Protocols, roles and
hooks are live; the ladder runs from `studio/game.yaml`.

## Current milestone: M2 — online

Target: **Sat 10 Oct 2026**, friends playing over the internet.

Scope is specified in full by **`docs/specs/2026-09-17-online-slice.md`** (17 Sep, 757 lines, twelve
locked decisions) with ADR-026 (client mirror), ADR-027 (wire and clock) and ADR-028 (lobby scene and
the presenter/driver split). Read that before touching M2 — it is the work order.

**One thing in it is unsettled.** The spec locks a FIFO matchmaking queue; `docs/PLAN.md`, approved the
same day, records Rohan choosing room codes and moving the queue to M5. See
`studio/decisions/OPT-0001-how-two-friends-meet.md`. Nothing else in M2 is open.

| Done-when | State |
|---|---|
| M2-1 Two browsers play a full match through `Mimas.Server` | not started |
| M2-2 Two players who want to play each other end up in the same match | **blocked on OPT-0001** |
| M2-3 Guest auth with a resumable token; a reload keeps your identity | not started |
| M2-4 Server-authoritative 30 s turn; timeout arrives as EndTurnCommand(Timeout) | not started |
| M2-5 Reload within the 60 s grace resyncs into the running match | not started |
| M2-6 Resign and disconnect-forfeit end the match correctly | not started |
| M2-7 Deployed on the VPS over HTTPS, reachable by a friend | not started |
| M2-8 Rematch without leaving the room | not started |

## In flight

| Task | What | Status | Who |
|---|---|---|---|
| T-0001 | Install the studio and seed `studio/` | running | builder |

## Blocked

| Task | Blocked by | Since |
|---|---|---|
| — | nothing | |

## Waiting on Rohan

| What | File | Since |
|---|---|---|
| **Decide OPT-0001** — queue or room codes. M2 waits on it | `studio/decisions/OPT-0001-how-two-friends-meet.md` | 17 Sep 2026 |
| Edit `pillars.md` — it is a draft distilled from the design page, and the pillars are yours | `studio/pillars.md` | 17 Sep 2026 |

## Last playtest

**16 Sep 2026, Rohan, the aiming slice, Editor on arena-4.** Circles, a clear arc over a wall, blocked
shots, out-of-range, a prop shot down and walked onto, a projectile in flight. Console clean. Two bugs
caught in play, both since fixed: props broke the HUD turn-start loop (their ids are body ids), and
arming an attack under a stationary cursor drew nothing until the mouse moved.

Not yet exercised and still worth a pass: the bot attacking (its flyover, an EXTRA reveal), Fire Bolt
and line of sight from the player's side, the DEFEAT banner, and a Web build of the current HUD.

## Next decision due

**OPT-0001, how two friends end up in the same match.** The online slice spec is otherwise ready to
execute, so this is the only thing standing between today and the first real M2 session.

Then, from `docs/PLAN.md` §10: if two browsers cannot play a full match through the server by
**Fri 2 Oct**, the 10 Oct playtest falls back to the local build (Rohan vs bot, shared over screen) and
online moves to 17 Oct. The Producer raises this in the 2 Oct brief either way.

## This week's intent

Week 1 (18–24 Sep): settle OPT-0001, then run the online slice spec's server half (§3–§6: data, Core,
server, server tests). It needs no Editor, and its test project becomes ladder rung 5. The client half
(§7) follows and does need a warm Editor.

Balance is not a week-1 concern, but rung 7 (bot-vs-bot batch) is, because the shipped ability numbers
have never been measured and friends will feel them on 10 Oct.
