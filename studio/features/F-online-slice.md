---
id: F-online-slice
title: Two browsers play each other
project: mimas
milestone: M2
status: approved
lane: full
design_anchor: docs/design/index.html#online
spec: docs/specs/2026-09-17-online-slice.md
adrs: [ADR-026, ADR-027, ADR-028]
created: 2026-09-17
approved: 2026-09-17
---

# Two browsers play each other

**One sentence:** the match moves onto the server, and two people — or one person and the server's bot
— play the same game from two browsers.

> **This one-pager describes work that was already specified and approved.** The work order is
> `docs/specs/2026-09-17-online-slice.md` (757 lines, written 17 Sep from a three-round question round
> with Rohan, checked against the code at commit `15e3be0`), and its twelve locked decisions are D1–D12
> in that file's §2. ADR-026, ADR-027 and ADR-028 record the architecture.
>
> This file exists so the milestone is readable without opening the spec, and so the done-when have
> ledger entries. **Where this file and the spec disagree, the spec wins** — and that is a bug here.

## Why now

Nothing else in M2 means anything without it, and it is where every unknown lives: no client has ever
connected to `Mimas.Server` in a real match.

## What the player sees

The game opens on a **lobby**: your name, and three ways in — **Play vs bot**, **Create room** and
**Join room**. All three put you in a room with a four-letter code you can paste to a friend. In the
room you see who you are playing, pick a loadout preset and press Ready; the match starts when both
seats are ready.

Then the same match you already play offline — the same board, the same range circles, the same blocked
reasons, the same projectile arc — except the opponent is real. You get **30 seconds a turn**. A
**Resign** button sits in the HUD behind two clicks.

If your browser drops, you have **60 seconds** to come back; reload and you are returned to the match
exactly as it stands. Your opponent sees a line saying you are gone, with a countdown. If you do not
come back, you forfeit.

What you never see: anything about their hidden abilities you have not earned by watching them use it.

## Rules it introduces or changes

One new rule, and it is in Core: **`ResignCommand`**, legal at any time while the match runs, never
offered to a bot, with `MatchEndReason.Resign` and `Forfeit`. A disconnect forfeit is the server
submitting a `ResignCommand(Disconnect)` — so it goes through the same rules path as every other move
and a replay of the command list reproduces the match exactly.

A timeout is likewise `EndTurnCommand(Timeout)`, submitted by the server. **Every clock decision is a
command.** That is what keeps the online and offline games identical.

The client never holds the truth: it rebuilds a knowledge-limited `MatchState` from the `PlayerView`
attached to every message (ADR-026). It can therefore answer every preview question instantly and
locally, and it can only ever contain what the server chose to tell it — which is the hidden-
information guarantee restated as an architecture.

## Done when

From the spec's §13.

- [ ] Core and server tests green; a test asserts the server never serialises a `MatchState` (M2-1)
- [ ] Two players who want to play each other end up in the same match, by room code (M2-2)
- [ ] Guest auth with a resumable token; a page reload keeps your identity (M2-3)
- [ ] The 30 s turn is server-authoritative, with a lag grace of the measured round trip up to 1 s,
      and a timeout arrives as `EndTurnCommand(Timeout)` (M2-4)
- [ ] Reloading within the 60 s grace returns you to the running match with a full resync (M2-5)
- [ ] Resign and disconnect-forfeit both end the match with the right banner (M2-6)
- [ ] From the Editor, a bot match plays start to finish online
- [ ] A Web build in a browser joins a room the Editor created, by code, and they play a full match

## Not in this

The spec is explicit (D9): **local end-to-end only, no deploy artefacts.** Getting it onto the internet
is `F-deploy`, and that is what stands between this slice and friends playing on 10 October.

Also out: rematch without leaving the room (still open, M2-8), ratings and accounts (M5), banks,
increments and time-control choice, a database, character select and the draft (M3 — though the lobby
scene is built as its future home).

## Decided

**OPT-0001: room codes** (17 Sep, Rohan), with the loadout chosen after the room is joined. The spec
carries it as Amendment A1 (§2a); the queue moves to M5 with ratings.
`studio/decisions/OPT-0001-how-two-friends-meet.md` has the reasoning.

## Cost

The largest single piece of work in the project so far: one long session for the server half (§3–§6, no
Editor needed), then one for the client half (§7, needs a warm Editor). Ladder rungs 0, 1, 2, and it
creates rung 5.
