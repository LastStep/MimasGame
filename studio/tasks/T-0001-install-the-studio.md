---
id: T-0001
title: Install the Trinetra studio into Mimas and seed studio/
project: mimas
feature:
milestone: M2
lane: full
status: running
owner: builder
model: opus
worktree:
depends_on: []
allows_assets:
  - '.claude/**'
  - 'studio/game.yaml'
  - 'studio/ledger.json'
done_when:
  - "An agent starting cold in this repo reads studio/STATE.md and knows where the project stands without being briefed"
  - "The ladder runs from studio/game.yaml and rungs 0-2 are green"
  - "Every M1-M3 done-when from docs/roadmap.md exists as a ledger criterion"
  - "The M2 one-pagers are in studio/features/ with status draft, ready for Rohan to approve"
ladder: [0, 1, 2]
created: 2026-09-17
started: 2026-09-17
finished:
cost_usd: 0
blocked_by:
---

# Install the studio into Mimas

## Context

`docs/PLAN.md` §9 items 1–3. The studio substrate exists in `E:\Studios\Trinetra-Game-Studio`; this
task lands it in the game repo so M2 runs through it from its first day rather than being retrofitted
afterwards.

## Scope

- `.claude/protocols/`, `.claude/skills/`, `.claude/agents/`, and the hooks merged into
  `.claude/settings.json` — written by `install-project.mjs`, which kept the existing entries and left
  `.claude/settings.json.before-trinetra` as a backup.
- `studio/game.yaml` — the ladder, the deny-list and the ratchets.
- `studio/` — `STATE.md`, `pillars.md`, `roadmap/M2-online.md`, `ledger.json`, features migrated from
  `docs/specs/`, and the M2 one-pagers.
- `CLAUDE.md` — the managed studio block, and the stale commands corrected.

## Out of scope

Ladder rungs 3–10. They are declared in `game.yaml` as `enabled: false` so the gaps are visible; each
becomes its own task. Rung 5 (server integration) belongs to M2 week 1 and is the next real piece of
work.

## Notes

`allows_assets` declares `.claude/**` and `studio/game.yaml` because installing the gate necessarily
writes the gate. Every later task will have neither, which is the point.

`docs/roadmap.md` and `docs/specs/` stay where they are for now rather than being deleted — the
migration copies their content into `studio/`, and removing the originals is a separate, reversible
step once Rohan has confirmed nothing was lost.
