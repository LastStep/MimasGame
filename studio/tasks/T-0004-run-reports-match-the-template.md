---
id: T-0004
title: Mimas run reports use the template's field names, so the Inbox can see them
project: mimas
feature:
milestone: M2
lane: light
status: todo
owner: builder
model: sonnet
worktree:
depends_on: []
allows_assets: []
done_when:
  - "Every file in studio/runs/ uses id, role and outcome, matching studio/templates/run-report.md"
  - "A run report with outcome: needs-rohan appears in the Trinetra app's Inbox"
ladder: [0]
created: 2026-09-17
started:
finished:
cost_usd: 0
blocked_by:
---

# Run reports match the template

## Context

Found on 17 Sep 2026 by the Trinetra app's reader, which walks both repos.

`studio/templates/run-report.md` specifies `id:`, `role:` and `outcome:`. Mimas's two newest run
reports use `run:`, `agent:` and `status:` instead:

| File | Has | Should have |
|---|---|---|
| `R-2026-09-17-T-0002.md` | `run:`, `agent:`, `status: done` | `id:`, `role:`, `outcome: merged` |
| `R-2026-09-17-T-0003.md` | `run:`, `agent:`, `status: running` | `id:`, `role:`, `outcome: running` |

`R-2026-09-17-T-0001.md` is correct and can be used as the reference.

## Why this is worth a task

It is not cosmetic. The Trinetra Inbox surfaces a run report by exactly one rule: `outcome:
needs-rohan`. A Mimas run that stops and asks for Rohan by name, written in the current shape, will
**never appear in his inbox**. It will sit in `studio/runs/` looking finished.

The reader tolerated the drift — it falls back to the filename for the id, which is why nothing has
broken visibly. That tolerance is what let it go unnoticed for a day.

## Scope

The two files above. Map `status` to the template's vocabulary: `done` becomes `merged` where the work
landed, `running` stays `running`.

## Out of scope

Rewriting the prose in either report. They are a record of what happened and are not edited after the
fact — only the frontmatter keys change.

## Notes

Worth checking whether the agent that wrote these was reading Mimas's own older reports rather than
`studio/templates/run-report.md`. If so the template is being bypassed, and that is the thing to fix
rather than these two files.
