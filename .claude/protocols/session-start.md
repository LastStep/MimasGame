---
name: session-start
title: Session start checklist
scope: always
applies_to: [builder, verifier, researcher, producer, playtest-analyst]
---

# Session start

You are starting cold. Everything you need is in files. Spend the first two minutes here and you will
not need to ask Rohan anything he has already answered.

## 1. Work out what you are

Three facts, in this order:

| Fact | Where it comes from |
|---|---|
| **Project** | The repo you are in. Its identity is `studio/game.yaml`; the studio's list is `<studio>/registry.yaml` |
| **Role** | The agent definition that launched you (builder, verifier, researcher, producer, playtest analyst). If nothing says, you are a **builder** |
| **Task** | `TRINETRA_TASK` in the environment, or the single task file with `status: running`, or what Rohan just typed |

If you cannot name all three, stop and say so. Do not guess a task.

## 2. Read, in this order

1. `studio/STATE.md` — where the project stands right now. One page. Always current.
2. Your task file, `studio/tasks/T-NNNN-*.md`. The frontmatter is the contract: `lane`, `done_when`,
   `ladder`, `depends_on`, `worktree`.
3. The plan, `studio/plans/P-T-NNNN.md`, if the task's status is `approved` or later. If the lane is
   `full` and there is no approved plan, your job this session is to **write the plan**, not code.
4. The one-pager the task belongs to, `studio/features/F-*.md`. That is what "correct" means.
5. Only the design section the one-pager cites. Grep the anchor — never read the whole design page.

Stop reading there. Do not read the roadmap, old run reports, or unrelated specs "for context". If a
file you did not read turns out to matter, that is a bug in `STATE.md` — fix it at the end.

## 3. Check the lane before you touch anything

- **light** — build it, climb the ladder, merge. No approval gate.
- **full** — an approved plan must exist before code. Write one if it does not.
- **director** — **stop**. Write an options write-up (`templates/options.md`) and nothing else. See
  `lanes.md` for what lands in this lane. Writing code in the director lane is the single most
  expensive mistake available to you.

If the task as written pushes you into the director lane and it is not marked that way, say so in the
run report, downgrade yourself, and write the options instead.

## 4. Confirm the ground

```bash
git status --short          # clean? if not, whose changes are these?
git branch --show-current   # matches the task's `worktree`?
```

Uncommitted work you did not make is not yours to commit. Report it and leave it alone.

## 5. Open the run report before you write code

Create `studio/runs/R-<YYYY-MM-DD>-T-NNNN.md` from `templates/run-report.md` **now**, and append to it
as you go. It is a log, not a summary written at the end. If your session dies halfway, this file is
the only thing that survives — make it worth finding.

## 6. Then work

Everything from here is in `lanes.md` (what you are allowed to decide), `verification-ladder.md` (how
you prove it), `reporting.md` (how you tell Rohan) and `reward-hacking-guards.md` (what counts as
cheating).

## Never

- Ask Rohan something a file answers.
- Start over because reading felt slow.
- Carry knowledge only in your context. If you learned something the next agent needs, it goes in a
  file before you stop.
