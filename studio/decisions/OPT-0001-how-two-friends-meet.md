---
id: OPT-0001
title: How do two friends end up in the same match?
project: mimas
lane: director
status: open
author: builder
created: 2026-09-17
decided:
chosen:
---

# How do two friends end up in the same match?

## Why this is being asked now

Two documents, both dated 17 September 2026, both recording a decision with you, say opposite things:

| Source | Says |
|---|---|
| `docs/specs/2026-09-17-online-slice.md` §2 D-list, ADR-027, ADR-028 | **A FIFO queue.** The lobby has a "Find match" button; the server holds one queue and pairs the first two waiting players. Written after a three-round question round, checked against the code at `15e3be0`, and already a 757-line work order |
| `docs/PLAN.md` §8 and its decision log | **Room codes.** "M2 uses room codes instead of a matchmaking queue" and "the queue moves to M5", listed as a decision by you on 17 Sep |

One of them is stale and I cannot tell which from the files. Nothing else in M2 is unclear — this is
the only open question in the milestone, and it is small in code and large in consequence.

It blocks: the lobby screen, the `queue.join` / room-open message in the wire, and the first thing a
friend does on 10 October.

## What matters here

1. **Does it work on 10 October?** Four friends, in two pairs, probably in a Discord call. That is the
   only real test this decision faces before it is too late to change.
2. **How much work is it?** Three weeks to the playtest, and the online slice is already the largest
   piece of work in the project.
3. **What does it cost later?** M5 wants a real queue with ratings. Whatever is built now should not
   have to be thrown away.

## Options

### A. Queue only, as the spec already specifies

The lobby has "Find match". The server keeps one FIFO list; the first two players waiting are paired.

- **Gets us:** exactly what the 757-line spec describes, with nothing to rewrite. It is the shortest
  path to two browsers playing, and it is the thing M5 eventually needs anyway.
- **Costs:** on 10 October, four friends pressing "Find match" get paired **in the order they pressed
  it**, not into the pairs they arranged. Two of them will end up playing the wrong person, and the fix
  in the moment is "everyone stop, now you two press it". With a population of four, a queue is a
  coordination problem wearing a matchmaking hat.
- **Reversible?** Yes, cheaply. Adding room codes later is a new message type and a text field.

### B. Room codes only, as PLAN.md records

The lobby has "Create a room" (gives a code and a link) and "Join a room" (a text field).

- **Gets us:** four friends split into two pairs with no coordination beyond pasting a link — which is
  what will actually happen in a Discord call. It is also the only option that produces a **link**, and
  a link is how anyone who is not in the call ever tries the game.
- **Costs:** the spec's queue, `Matchmaker`, and part of the lobby get rewritten before they are built.
  Perhaps half a session. M5 then has to build the queue after all.
- **Reversible?** Yes. The queue is additive later.

### C. Both, queue second

Build rooms with codes and links now; keep "Find match" in the lobby as a button that opens a room and
waits for anyone — which is a one-entry queue and is roughly ten lines on top of rooms.

- **Gets us:** friends get links, and the bot path and the "just play someone" path still exist for
  testing. The room abstraction is the thing M5's real matchmaker will sit on top of anyway, so nothing
  is wasted.
- **Costs:** slightly more than B, meaningfully more than A. Two ways in means two lobby states to get
  right, and two paths through the rung 5 harness.
- **Reversible?** It is the superset; there is nothing to reverse.

## Comparison

| | A. Queue | B. Rooms | C. Both |
|---|---|---|---|
| Works for 4 friends in 2 pairs on 10 Oct | poorly | yes | yes |
| Produces a link you can paste | no | yes | yes |
| Work relative to the written spec | none | −half a session, +half a session | +half to one session |
| Thrown away at M5 | nothing | the room-only lobby | nothing |
| Risk to the 10 Oct date | lowest | low | low-moderate |

## Recommendation

**B — room codes**, matching `docs/PLAN.md`.

The single reason: **on 10 October you are not testing matchmaking, you are testing whether the game is
any good**, and a queue introduces a coordination failure in the first thirty seconds of the only
playtest that matters. A room code and a link remove that entirely.

C is the better engineering answer and I would take it if there were four weeks rather than three. If
you would rather not spend the extra half-session, B alone is enough for the playtest.

What would change this recommendation: if you intend to put Mimas in front of strangers before M5, the
queue stops being premature and A or C wins.

## What happens next, either way

- **A:** nothing changes. The online slice spec executes as written.
- **B:** the spec's §5 (`Matchmaker`), the `queue.join` message and §7.5 (lobby) need an amendment
  before the session starts — one task, half a session, before the server half begins.
- **C:** the same amendment, plus "Find match" kept as a one-entry room.

Either way, `studio/ledger.json` M2-2 is currently worded neutrally — "two players who want to play
each other end up in the same match" — so it does not have to be rewritten once you decide. It will be
sharpened to the mechanism you pick.
