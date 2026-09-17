---
name: lanes
title: Lanes and authority
scope: always
applies_to: [builder, verifier, researcher, producer]
---

# Lanes and authority

Every task runs in one of three lanes. The **Producer** assigns the lane; Rohan can override it; a
builder who finds themselves in the wrong lane says so and stops.

## The three lanes

| Lane | What it is | Gate before code | Gate before merge |
|---|---|---|---|
| **light** | Bug fixes, tweaks, refactors, test additions, doc updates. Roughly: under ~200 changed lines, no new file in the wire format or data schema, no player-visible behaviour change | none | ladder green + verifier |
| **full** | A feature. Anything a player will notice, anything that changes the wire format, the data schema or the server's contract | one-pager approved by Rohan, then a plan approved by Rohan | ladder green + verifier, then Rohan plays it |
| **director** | A decision, not a task. See below | — | Rohan chooses from the write-up |

When in doubt between light and full, it is full. When in doubt between full and director, it is
director.

## The director lane: what stops and becomes a write-up

Rohan owns these and nobody else touches them:

- **The pillars and the anti-pillars** — what the game is, and what it refuses to be.
- **Game design and feel** — a new rule, a mechanic, a number that changes how it plays, UX, art
  direction, pacing.
- **Scope and priorities** — what gets built next, what gets cut.
- **Any irreversible technical choice** — engine, service, dependency, hosting, data format, anything
  architecture-level, anything that would cost a week to undo.
- **The fun verdict.** Only Rohan plays and says whether it is good. No agent may conclude this from a
  sim, a metric or its own judgement.

The output is `templates/options.md`: two to four options, honest trade-offs, a recommendation, and
what it would cost to reverse each one. Not a recommendation dressed as the only option.

## What an agent decides alone

- How to implement an approved plan.
- Small technical choices inside a task — a helper's shape, a data structure, a test's name. Log an ADR
  only if a future agent would otherwise re-litigate it (see `adr.md`).
- Whether to merge: automatic, when the ladder is green and the verifier passed.
- What to do when blocked: stop, report, mark the task `blocked`. Never improvise around a blocker in
  the director lane.

## How a decision reaches Rohan

Not by asking in chat. By landing in the inbox as a file:

| Kind | File | Rohan sees |
|---|---|---|
| New feature | `studio/features/F-*.md` with `status: draft` | one-pager to approve |
| How to build it | `studio/plans/P-T-NNNN.md` with `status: draft` | plan to approve |
| A choice | `studio/decisions/` or the project's options file | options write-up |
| Blocked | task `status: blocked` | the blocker, with the full error |

Keep the inbox small. Only lane-full plans and director decisions belong there. Everything else is a
report, not a request — Rohan reading fewer things is what keeps him reading them properly.
