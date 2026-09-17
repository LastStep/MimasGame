---
id: M2
title: Online
project: mimas
target: 2026-10-10
status: in progress
done_when: [M2-1, M2-2, M2-3, M2-4, M2-5, M2-6, M2-7, M2-8]
---

# M2 — online

**Two friends, two browsers, one match, over the internet, on Saturday 10 October 2026.**

That date is the anchor of the whole plan. Everything here is scoped to reach it, and Trinetra's own
work slips before this does.

## The work order

**`docs/specs/2026-09-17-online-slice.md`** is the spec for the bulk of this milestone — 757 lines,
written 17 Sep from a three-round question round with Rohan, with twelve locked decisions and three
ADRs (026 client mirror, 027 wire and clock, 028 lobby scene and the presenter/driver split). It is
checked against the code at commit `15e3be0`. Read it before touching M2.

It deliberately stops at **local end-to-end** (D9: no deploy artefacts). Deployment, and therefore the
10 October playtest, is `F-deploy` on top of it.

## The one question, now settled

The spec locked a **FIFO queue**; the studio plan (`E:\Studios\Trinetra-Game-Studio\docs\PLAN.md`),
approved the same day, recorded Rohan choosing **room codes** and moving the queue to M5. Put to him on
17 Sep as OPT-0001: **room codes**, with the loadout chosen after the room is joined. The spec carries
it as Amendment A1 (§2a). Nothing else in M2 is undecided.

## Done when

In `studio/ledger.json` as M2-1 … M2-8.

1. **M2-1** Two browsers play a full match through `Mimas.Server` — the real thing, end to end.
2. **M2-2** Two players who want to play each other end up in the same match, by a four-letter room
   code they can paste as a link.
3. **M2-3** Guest auth with a resumable token; a page reload keeps your identity.
4. **M2-4** The 30 s turn is server-authoritative, with a lag grace of the measured round trip up to
   1 s, and a timeout arrives as `EndTurnCommand(Timeout)`.
5. **M2-5** Reloading within the 60 s grace resyncs into the running match.
6. **M2-6** Resign and disconnect-forfeit both end the match correctly, through `ResignCommand`.
7. **M2-7** Deployed on the VPS over HTTPS and reachable by someone who is not Rohan.
8. **M2-8** Rematch without leaving the room.

## Explicitly not in M2

- Ratings, Glicko-2, accounts and a database → M5.
- Reconnect with move buffering, spectators, chat → later, if ever.
- Anything from M3 (boons, lineages, the draft, session best-of-three). M2 ships the current single
  round over the wire; depth comes after friends have played it once.
- New art. Placeholders go in front of friends. That is fine and it is deliberate.

## The shape of the work

Server first, then client, then deploy. The client already plays a full match against a local bot
through `LocalMatchSession`; M2 replaces that bot and that local state with a socket, and the wire
format was already built around gear rather than classes, which was the hard part.

| Week | What |
|---|---|
| 1 (18–24 Sep) | OPT-0001 settled. The spec's server half (§3–§6): data, Core mirror, wire, server, server tests — no Editor needed, and its test project becomes ladder rung 5. Rung 7 (bot batch) so the numbers get one tuning pass. |
| 2 (25 Sep–1 Oct) | The spec's client half (§7): lobby scene, persistent NetClient, the presenter/driver split. Two browsers play locally. Nightly sim tunes the shipped numbers. |
| 3 (2–8 Oct) | VPS deploy with HTTPS (the nginx config already exists). Playwright smoke (rung 10). First remote match, Rohan vs Rohan. Lobby text, error states, rematch. |
| Fri 9 Oct | Build freeze. Rohan plays the deployed build for 30 minutes and writes notes **before** reading any report. |
| Sat 10 Oct | Friends play. Silent observation, notes via the template and the interview. |

## The fallback, decided in advance

If two browsers cannot play a full match through the server by **Fri 2 Oct**, the 10 Oct session
becomes a playtest of the local build — Rohan vs bot, shared over screen — and online play moves to
17 Oct. The Producer raises this in the 2 Oct brief either way, so the decision is made with a week in
hand rather than on the Friday night.

## Risks

- **Nobody has ever connected a client to this server.** The first integration is where the unknowns
  are, which is why rung 5 is week 1 and not week 2.
- **The balance numbers have never been measured.** Arrow shot 1 AP 3 dmg, aimed shot 2 AP 6, fire bolt
  2 AP 6 and the rest are hand-set placeholders. Friends will feel them. Rung 7 gives them one pass.
- **Unity Web build on a real connection** is untested beyond an empty project (12.5 MB, 14 Sep).
- **23 days.** Everything above assumes no week is lost. The fallback exists because one might be.
