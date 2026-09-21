---
id: F-m2-browser-finish
title: Finish M2 in the browser — rematch in the room, a page that looks like Mimas, the two playtest fixes
project: mimas
milestone: M2
status: approved
lane: full
design_anchor: docs/design/index.html#online
created: 2026-09-21
depends_on: F-deploy
approved: 2026-09-21
spec: docs/specs/2026-09-21-m2-browser-finish.md
task: T-0008
---

# Finish M2 in the browser

**One sentence:** on 10 October a friend opens `https://mimas.laststep.cloud`, sees a page called
Mimas that fills their window, joins by a code they were sent, plays, loses, and presses Ready again
without anyone typing a code twice.

## Why now

Deploy went live on 21 Sep. Everything M2 still lacks is what a friend meets in the first five minutes:
a page titled `Unity Web Player | MimasClient` in a 960×600 box with a Unity footer and a splash; no
way to copy the code by itself; a refused shot the picture does not explain; and a match that ends by
dumping both players back at the front of the lobby, where the code they shared no longer opens
anything. None of it is hard, all of it is known, and none of it needs a design question the page has
not already answered — except rematch, which Rohan decided on 21 Sep.

## What the player sees

The page is dark and the game is the page. A thin bar with a percentage, then the lobby. In the room:
**Copy link**, **Copy code**. In a match, a refused shot shows the tile that blocked it, tinted, and
the pillar in the way is the size of the hex it fills. After the result, **Back to room** — same code,
same seats, Ready reset — and the next match starts when both press Ready. A friend who dropped can
come back by code, because the seat is free and the room is still there.

## Rules it introduces or changes

- **Rematch: the room outlives the match** — rule 9 of `#online`, decided 21 Sep 2026 (ADR-032).
- Nothing else. Sight and trajectory rules are untouched; the T-0006 work makes the picture match the
  rule, not the other way round.

## Answered by Rohan on 21 Sep 2026

| Question | Answer |
|---|---|
| What happens after the result banner? | Both players are back in the same room; Ready twice is the rematch. Not an offer, not a score |
| Page shape? | Full-window canvas, dark, `Mimas`, own favicon, percentage loading bar, no footer |
| Unity splash and product identity? | Splash off; `productName` Mimas, `companyName` Trinetra, through `WebBuild.cs` |
| Fold in the hardening? | Yes: WebSocket origin allow-list, `--browser webkit\|firefox` in the smoke |

## Done when

See the task's `done_when` and the spec §12. The verifier ticks **M2-8**; **M2-1** and **M2-7** are
ticked from the playtest entry Rohan writes after the first evening with a friend on the live site.

## Not in this feature

Session score / best-of-three, ratings, accounts, spectating, chat, a logo, mobile layout, any rules
change, real prop art, OQ-N16.
