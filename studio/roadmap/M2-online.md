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
| 1 (18–24 Sep) | ~~OPT-0001, server half, client half~~ — **all done on 17 Sep in one session (T-0002)**, a week early. What week 1 is now actually for: the Web build in a browser, then deploy. |
| 2 (25 Sep–1 Oct) | VPS deploy with HTTPS (the nginx config already exists). Playwright smoke (rung 10). First remote match, Rohan vs Rohan. Rematch (M2-8). |
| 3 (2–8 Oct) | Slack. Rung 7 (bot batch) so the shipped numbers get their first measured pass. Lobby text and error states from whatever the remote match turns up. |
| Fri 9 Oct | Build freeze. Rohan plays the deployed build for 30 minutes and writes notes **before** reading any report. |
| Sat 10 Oct | Friends play. Silent observation, notes via the template and the interview. |

## The fallback, decided in advance

If two browsers cannot play a full match through the server by **Fri 2 Oct**, the 10 Oct session
becomes a playtest of the local build — Rohan vs bot, shared over screen — and online play moves to
17 Oct. The Producer raises this in the 2 Oct brief either way, so the decision is made with a week in
hand rather than on the Friday night.

## Risks

- ~~Nobody has ever connected a client to this server.~~ **Closed 17 Sep.** The Editor plays full
  matches through it, 34 server tests play more, and Rohan ran it against a local `dotnet run` server
  the same day and it looked good.
- **Nothing has ever run in a browser.** This is now the top risk, and it replaces the one above. Every
  WebGL-specific path is written and unexercised: the NativeWebSocket jslib socket, `PlayerPrefs`
  reaching IndexedDB (which is what makes a refresh rejoin a match), `?room=` and `?ws=` read from the
  page URL, and Brotli. A Web build is ~20 minutes and answers all of it.
- **Deploy has never been attempted.** `docs/hosting.md` and the nginx config exist and have never been
  run. TLS, `wss://` through a proxy, and the 3600 s `proxy_read_timeout` are all untried. This is the
  whole of M2-7 and the only thing between today and friends playing.
- **The balance numbers have never been measured.** Arrow shot 1 AP 3 dmg, aimed shot 2 AP 6, fire bolt
  2 AP 6 and the rest are hand-set placeholders. Friends will feel them. Rung 7 gives them one pass.
- **The server has never held more than two rooms.** On 10 October it holds two at once, for four
  people. Nothing suggests it will not — a room is a lock and a few KB — but nobody has looked.
- **23 days.** The fallback below still stands, and is now much less likely to be needed.

## What T-0002 did not do, and what that means for the plan

The online slice was executed in one session on 17 Sep (see `studio/runs/R-2026-09-17-T-0002.md` for
the evidence). These are the honest gaps, in the order they should shape the next plan.

**1. No Web build, so no browser.** Spec §7.11's static-file serving is implemented and inert without
`MIMAS_WEB_PATH`; `unity build MimasClient --profile "Web Release"` was never run. Everything in the
browser risk above follows from this one omission. **This is the next task and it is small.**

**2. No repeatable test covers the client at all.** The server half has 34 tests; the client half was
verified by one agent driving the Editor by hand for an hour. There are no EditMode tests for
`NetClient`, `OnlineMatchDriver` or `LobbyView`, and rung 6 is still disabled. Four ordering and
recovery bugs were found that way — which is evidence the method works, and also evidence that nothing
will catch the fifth. A handful of EditMode tests over the drivers would be cheap.

**3. Rematch (M2-8) matters more than its position suggests.** Friends will play three to five games in
a sitting. Without it, every game ends with both players back in the lobby re-sharing a code. That is
small friction repeated at exactly the moment they are deciding whether they want another go. It was
out of scope by D9 and should probably be promoted ahead of polish.

**4. Rung 3 (determinism sweep) is still not built, and it now carries more weight.** The client mirror
rests entirely on "same inputs, same result"; determinism is currently checked at two seeds. It was
listed as a gap before M2 and is a bigger one after it.

**5. Nobody has verified T-0002.** The ledger is untouched on purpose — `pass` is the verifier's, not
the builder's. M2-1 and M2-3..M2-6 look ready for one. **M2-2's wording still says "mechanism open:
OPT-0001" and needs rewording to room codes** before it can be ticked honestly.

**6. One design question was opened and not answered:** should a room show the other seat's chosen
preset before the match starts? Hidden today, because showing it would invent a counter-pick rule the
design page does not have — but gear is public the moment the match begins, so hiding it for the last
ten seconds may be theatre. Design page `#q-online-room-loadout`. It blocks nothing.

**7. Two small things worth knowing before the playtest.** Two browser tabs at the same origin share
one guest token (`PlayerPrefs` is per-origin), so they are the same player — testing two seats needs
the Editor plus a browser, or an incognito window. And the asset guard blocks several harmless commands,
including `unity command get_scene_hierarchy`; the false positives and the route through are in the run
report and in the agent's memory.

## Suggested next tasks

| Task | What | Size | Why this order |
|---|---|---|---|
| T-0003 | Web build, served by `MIMAS_WEB_PATH`, played in a browser. Refresh mid-match, `?room=` link, two seats across Editor + browser | half a session | Answers the single largest unknown, and every later task assumes it works |
| T-0004 | Deploy on the VPS over HTTPS (`F-deploy`, M2-7) | one session | The only thing between this and 10 October |
| T-0005 | Rematch without leaving the room (M2-8) | small | Disproportionate effect on a playtest |
| T-0006 | Verifier over T-0002; reword M2-2 and tick what passes | short | Nothing is done because the builder says so |
| T-0007 | Rung 3 determinism sweep, and rung 7 bot-vs-bot batch for the first measured balance pass | one session | Both were already overdue; the mirror makes the first one load-bearing |
