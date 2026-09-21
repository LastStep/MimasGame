---
id: F-deploy
title: A link a friend can open
project: mimas
milestone: M2
status: approved
lane: full
design_anchor: docs/hosting.md
created: 2026-09-17
depends_on: F-online-slice
approved: 2026-09-22
spec: docs/specs/2026-09-22-deploy.md
task: T-0007
---

# A link a friend can open

**One sentence:** the game lives at `https://mimas.laststep.cloud`, over HTTPS, and someone who is not
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

If something is broken server-side, they see a sentence saying so, not a spinner forever. (The lobby
already says "Server unreachable · retrying"; the spec verifies it on the real origin.)

## Rules it introduces or changes

None. But it forces one irreversible-ish choice — **where it is hosted and under what name** — which is
a director-lane decision and is the main reason this is a one-pager rather than a task. Rohan made it
on 22 Sep 2026; see the answers below.

## Done when

- [ ] The Web build is served over HTTPS from the VPS with Brotli, and `wss://` works from it (M2-7)
- [ ] Someone who is not Rohan has opened the link and played a match
- [ ] Cold load time on a normal desktop connection is measured and written into
      `docs/roadmap.md` baselines — the row has been blank since 14 Sep
- [ ] The build size gate (ladder rung 9) fails above 25 MB. Baseline is 12.5 MB Brotli for an empty
      project; the real figure was 12.31 MB on 18 Sep
- [ ] Chrome and Safari both load it
- [ ] A Playwright smoke test (rung 10) passes: no `pageerror`, no `console.error`, a match starts
- [ ] Deploying again is one command, so the 9 Oct freeze build is not a manual adventure

## Not in this

A CDN, autoscaling, monitoring, uptime alerting, a custom domain with a nice name if that costs a
decision, mobile browsers (should not crash; does not get to shape anything), and any analytics.

## Open questions for Rohan — answered 22 Sep 2026

1. **Where does it live, and at what address?** **The existing Hostinger VPS, as
   `mimas.laststep.cloud`**, beside `laststep.cloud` and its subdomains, with its own nginx file and its
   own certificate. Not a new domain, not Cloudflare Pages, not a path under the main site.
2. **Does the link need to still work after 10 October?** **Yes, permanent.** The name follows the
   pattern of the existing subdomains, so it stays right if a second game or a hub ever appears.
3. **Who deploys?** (Asked in the session.) **Rohan, from `tools/deploy/deploy.sh` on this PC**, with a
   runbook for the one-time VPS setup. Agents rehearse (`--dry-run`) and never touch the VPS; automation
   is a later decision, and the ssh alias is already in place for it.
4. **Process on the VPS?** **systemd + a self-contained Linux publish.** Nothing to install; Docker is
   on the box but would be one more container to operate beside sixteen.

## Cost

Roughly one to two sessions, week 3. The risk is not the work, it is discovering something about
WebGL2-over-HTTPS that takes a day. Which is why it is not scheduled for the 9th.
