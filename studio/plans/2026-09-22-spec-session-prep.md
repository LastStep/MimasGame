---
id: PREP-2026-09-22
title: Prep for the next session: write one concrete spec for Opus builders
project: mimas
written: 2026-09-21
by: Claude, at the end of the 21 Sep design session
for: the next session (Rohan + Claude), a spec-writing session in the shape of docs/specs/2026-09-17-online-slice.md
---

# Next session: write one spec that Opus agents can execute unattended

**The outcome Rohan asked for:** "another concrete plan/spec which can then be worked on by opus
agents." The shape is known and has worked three times: a question round with 3-4 option questions,
then a spec in `docs/specs/<date>-<slice>.md` with a "how to run this session" preamble, numbered
sections in commit order, a forks-with-defaults section, and a done-when list that maps to ladder
rungs (see `docs/specs/2026-09-17-online-slice.md` §0 and §12 for the template, and the memory note
"spec then autonomous session").

## First decision of the session: which slice

Two candidates. The recommendation is the first, because it is the only thing that can slip 10 Oct.

### A. Deploy (M2-7, `studio/features/F-deploy.md`) — recommended

| | |
|---|---|
| Why now | 19 days to 10 Oct; the 2 Oct fallback date needs two browsers through the server *on the internet*. Everything else in M2 is done or a small task |
| Lane | full, and F-deploy is still `status: draft` with two director questions unanswered |
| Inputs to read | `studio/features/F-deploy.md`, `docs/hosting.md` (layout, nginx, manual steps, budget), `tools/deploy/nginx-mimas.conf`, `docs/roadmap.md` rows M2 and the blank load-time baseline, `tools/smoke/browser-smoke.mjs` (rung 10), `MimasClient/Assets/_Game/Editor/WebBuild.cs` (build through the script, not the profile; deletes stale `Build/` first) |
| Director questions to ask first | 1. Where does it live and at what address (existing VPS + owned subdomain is the one-pager's recommendation)? 2. Permanent link or one evening? 3. Who holds the VPS credentials and how does an agent deploy without them in the repo? 4. Is the 25 MB size gate still the number? |
| What the spec must cover | HTTPS + `wss://` behind nginx with Brotli and the right `Content-Encoding` for `.br` files; a one-command deploy (`tools/deploy/…`) that builds through `WebBuild.Build`, ships `Build/Web` and restarts the server; the health check; cold-load measurement written into `docs/roadmap.md`; Chrome and Safari; the Playwright smoke against the live URL; what "someone who is not Rohan played" means as evidence |
| Known traps to write in | Release builds only (a Development Build does not link on this stream); headless Chromium is a software rasteriser; "booted" is not "playable"; `com.unity.pipeline` must resolve after any upgrade; the asset guard's false positives |
| Rough size | one to two Opus sessions |

### B. Board mechanics, first slice (`docs/design/2026-09-21-board-mechanics.md`)

| | |
|---|---|
| Why not first | M3-or-later design; nothing on 10 Oct depends on it; the design page is untouched by Rohan's choice, and golden rule 11 says build only what the page has as decided or proposed |
| If Rohan chooses it anyway | The spec session must (1) answer OQ-N03, N07, N11 on `docs/design/mechanics.xlsx`; (2) pick a slice that is testable in Core without the client: the aura + fog in `PlayerView` and the `needsVision` flag + blind shots is the smallest one that changes play; builds and traps are a second slice; the laser a third; (3) decide how the design page gets its `proposed` sections, since the guard blocks the file (a task with `allows_assets: ['docs/design/index.html']` or Rohan's own edit); (4) put every number from the Tunables sheet's yellow cells into the spec as data, not code |
| Inputs to read | the brief, the workbook's Mechanics (M-060..M-092), Triggers and Open questions sheets, `#hidden-info`, `#attacks`, `#line-of-sight`, ADR-026 (mirror) |

## Small things that do not need a spec

Already written as light-lane tasks from the 21 Sep playtest; any builder can take them, and they are
good warm-ups for a fresh Opus session before the deploy spec lands:

- `T-0005` Copy code button beside Copy link.
- `T-0006` Refused shots highlight the blocking hex; prop art fills the hex it blocks.
- `T-0004` Run report frontmatter matches the template (from 17 Sep, still todo).

## Still waiting on Rohan (carried over)

- The `?perf=1` frame meter reading from a bot match. His 21 Sep notes did not mention frames, which
  is not the same as "no jank".
- `pillars.md` is still the agent's draft.
- OQ-N03, N07, N11 if slice B is ever chosen.

## How to open the next session

1. Read `studio/STATE.md`, then this file, then only the inputs of the slice chosen.
2. Ask the director questions of that slice, 3-4 options each, one round at a time.
3. Write the spec. Name every existing file and function it touches (check them at the current commit
   and say so in the preamble, as the online spec did at `15e3be0`).
4. Update `F-deploy.md` (`status`, `approved`) or open the feature one-pager for slice B.
5. Update STATE and stop. The Opus session that executes the spec is a different session.
