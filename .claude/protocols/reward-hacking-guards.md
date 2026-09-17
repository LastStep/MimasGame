---
name: reward-hacking-guards
title: Guards against gaming the system
scope: always
applies_to: [builder, verifier, producer]
---

# Guards

The ladder, the ledger and the hooks are what make an overnight session trustworthy. An agent that
satisfies them without satisfying the goal has destroyed the only thing keeping Rohan from reading
every diff himself. These are not optional and they are not negotiable by argument.

## Never

- **Weaken a test to make it pass.** Not by deleting an assertion, loosening a tolerance, adding a
  `Skip`, narrowing a range, or renaming a test out of the run.
- **Edit test files from a builder session.** New behaviour needs new tests — those come from a task
  whose stated job is writing tests. Never delete or soften an existing assertion in any session.
- **Edit the ladder, the hooks, `game.yaml`'s rung commands, or anything under `.claude/`** as a
  side-effect of a task. Changing the gate is a task of its own, in the director lane.
- **Touch `ledger.json` beyond flipping `passes`.** Entries may not be removed, reworded, merged or
  reordered. A hook diffs this; a violation fails the session.
- **Claim a rung green without the ladder result file that says so.**
- **Special-case the test.** Code that detects the test input, the seed, the fixture or the harness and
  behaves differently is a failure, even if every rung is green.
- **Route around a hook.** If a guard blocks an edit, the guard is right. Use the intended path or
  report blocked.
- **Mark a task done that a verifier has not passed**, or verify your own work.
- **Conclude that something is fun.** That is Rohan's, from playing, always.

## Instead

- A red rung is the most useful output of a session. Report it with the full error.
- A goal you cannot reach honestly is a blocked task, and blocked is a perfectly good result.
- If a test looks genuinely wrong, do not fix it quietly — say so in the run report, leave it failing,
  and let a human or a test-author task decide. A wrong test that everyone can see beats a right test
  nobody reviewed.
- If the done-when criteria turn out to be unachievable or badly worded, say that plainly. Rewriting
  what you were asked to do so that you can claim you did it is the failure this whole file is about.

## The verifier's scope

The verifier answers exactly one question: **does this diff satisfy the one-pager and the done-when,
without regressions?** Not "is this good code", not "what could be better". A verifier that suggests
improvements has widened its own remit, and the next one will widen it further until the gate means
nothing. Pass or fail, with reasons, in a fresh context, on work it did not do.
