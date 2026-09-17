---
id: F-deploy
title: A link a friend can open
project: mimas
milestone: M2
status: draft
lane: full
design_anchor: docs/hosting.md
created: 2026-09-17
depends_on: F-online-slice
approved:
---

# A link a friend can open

**One sentence:** the game lives at an address on the internet, over HTTPS, and someone who is not
Rohan can play it there.

## Why now

The online slice stops deliberately at local end-to-end (spec D9: "no deploy artefacts"). This is the
step between two browsers on Rohan's desk and four friends on 10 October, and everything else in M2 is
worthless if it only runs on one laptop. This is also the feature most likely
to be underestimated and then to eat the last week — browser builds meet reality here: compression,
CORS, secure websockets, certificates, and the first time anyone measures how long Mimas takes to load
on a connection that is not localhost.

`tools/deploy` and the nginx config already exist, and `docs/hosting.md` has the plan. This is
finishing it, not starting it.

## What the player sees

They click a link. A loading bar, then the game. Not a certificate warning, not a blank page, not a
console full of red. It works on Chrome and on Safari, because one of the four friends will have a Mac.

If something is broken server-side, they see a sentence saying so, not a spinner forever.

## Rules it introduces or changes

None. But it forces one irreversible-ish choice — **where it is hosted and under what name** — which is
a director-lane decision and is the main reason this is a one-pager rather than a task.

## Done when

- [ ] The Web build is served over HTTPS from the VPS with Brotli, and `wss://` works from it (M2-7)
- [ ] Someone who is not Rohan has opened the link and played a match
- [ ] Cold load time on a normal desktop connection is measured and written into
      `docs/roadmap.md` baselines — the row has been blank since 14 Sep
- [ ] The build size gate (ladder rung 9) fails above 25 MB. Baseline is 12.5 MB Brotli for an empty
      project; the real figure is unknown
- [ ] Chrome and Safari both load it
- [ ] A Playwright smoke test (rung 10) passes: no `pageerror`, no `console.error`, a match starts
- [ ] Deploying again is one command, so the 9 Oct freeze build is not a manual adventure

## Not in this

A CDN, autoscaling, monitoring, uptime alerting, a custom domain with a nice name if that costs a
decision, mobile browsers (should not crash; does not get to shape anything), and any analytics.

## Open questions for Rohan

1. **Where does it live, and at what address?** This is the irreversible bit. Options: the existing VPS
   with a subdomain of a domain you already own; a new cheap domain; or a free host (Cloudflare Pages
   for the build) with the VPS carrying only the websocket. Recommendation: **existing VPS, subdomain
   you already own** — one moving part, one certificate, and `docs/hosting.md` already assumes it.
   A researcher can write this up properly if you would rather see the trade-offs in full.
2. **Does the link need to still work after 10 October?** If it is a permanent address, it wants a
   permanent name. If it is for one evening, `mimas.<yourdomain>` is fine forever. Recommendation:
   treat it as permanent — renaming later is cheap, but telling friends a new link is not.

## Cost

Roughly one to two sessions, week 3. The risk is not the work, it is discovering something about
WebGL2-over-HTTPS that takes a day. Which is why it is not scheduled for the 9th.
