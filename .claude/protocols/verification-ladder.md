---
name: verification-ladder
title: The verification ladder
scope: always
applies_to: [builder, verifier, producer]
---

# The verification ladder

Generation is cheap. Verification is the bottleneck, and games have the worst verification signal in
software. The ladder is how a claim becomes a fact.

## How it works

Rungs are numbered, cheapest first. **Each project defines its own rungs** in `studio/game.yaml` — the
command, whether it is required, and what its failure means. Each task's frontmatter names the rungs it
must pass:

```yaml
ladder: [0,1,2,3,5]
```

Run them:

```bash
node <trinetra>/tools/ladder/ladder.mjs --project mimas --rungs 0,1,2,3,5
```

The runner writes `studio/runs/.ladder/<task>.json` — the machine-readable result the `Stop` hook reads
before it will let you call anything done. **You do not write that file by hand.** A run report whose
green rungs do not match a real result file is a failed session, not a finished one.

## The rules

1. **Climb from the bottom, every time.** Rung 1 failing makes rung 6's result meaningless. The runner
   stops at the first required failure.
2. **Run the ladder before you believe you are finished, not after you have said so.**
3. **Paste the evidence.** Test counts, exit codes, the error text, the screenshot path. "Tests pass"
   is not evidence; `287 passed, 0 failed` is.
4. **A red rung is information, not an obstacle.** Report it. Never route around it.
5. **Never-worse ratchet.** Test count, simulation invariants and build size may not regress. If your
   change drops the test count, that is a failure even if everything is green — say why, or fix it.

## The circuit breaker

Three failed attempts at the **same** error and you stop. Not a fourth idea, not a workaround.

1. Write the full error into the run report — the real text, not your summary of it.
2. Set the task's `status: blocked` and fill `blocked_by` with one plain sentence.
3. Stop the session.

A blocked task with a good error report is a useful night's work. Eleven attempts that end in a
disabled test are not.

## What "done" means

All four, in order:

1. Every rung the task names is green, in one run, on the final code.
2. A **verifier in a fresh context** read the one-pager, the plan and the diff, and passed it.
3. The ledger entry for each `done_when` was flipped to `passes: true` by the verifier — not by you.
4. For anything a player can feel: **Rohan played it.** No sim, metric or agent opinion substitutes.

Steps 1–3 let a task merge. Only step 4 closes it.
