# Next session: the parallel verification round

_Asked for by Rohan on 24 Sep 2026, at the end of the T-0014 session: "the next session does parallel verification for
the things that have verification pending, and if they found something then they can write it into a report."
Precedent: `studio/runs/R-2026-09-23-verify-boons.md` (two verifiers in parallel, one round report)._

## What the session is

One lead session (you, reading this) runs **one `verifier` agent per task, all at once**, in fresh contexts, then
collects what they found into **one round report**. The lead builds nothing and fixes nothing. A verifier answers one
question — does this diff satisfy the one-pager and the done-when, with no regressions (`.claude/agents/verifier.md`,
`.claude/protocols/verification-ladder.md`, `reward-hacking-guards.md`) — and on a fail says exactly what would make it
pass.

## The queue

Tasks whose status is `verify` on 24 Sep 2026 (check `studio/tasks/` again first; something may have moved):

| Task | What | Run report | Evidence to hold it against | Notes for its verifier |
|---|---|---|---|---|
| **T-0014** | The lobby and the room in ink | `R-2026-09-24-T-0014.md` | `artifacts/t0014/shots/`, `seat2-*/`, `boards/`, `smoke-*` | Four files outside the spec's list, each explained in the report; the translucent-colour drift is declared in `docs/ui/lobby.md` |
| **T-0013** | The match HUD in ink (+ one wire field, ADR-039) | `R-2026-09-23-T-0013.md` | `artifacts/t0013/shots/`, `browser-hud/`, `boards/` | Judge it at its own commits (`d309c8f`…`fe866ef`). HEAD also has T-0014's change to `UI/FontSpacing.cs`: T-0013's kerning fix did not survive glyphs drawn later ("V I C TORY" came back), which T-0014 found and completed |
| **T-0011** | The examine plate | `R-2026-09-23-T-0011.md` | `artifacts/t0011/`, `MimasClient/Assets/_Shots/` | T-0013 moved the plate from paper to ink **by design** (ADR-040): judge T-0011 against its own done-when at its commits, not against today's look |
| **T-0012** | The camera | `R-2026-09-23-T-0012.md` | `artifacts/t0012/` | STATE says Rohan plays and tunes it first. Include it unless he says he is still tuning; tuning values are not done-when |
| **T-0008** | Finish M2 in the browser (closes T-0005, T-0006) | `R-2026-09-21-T-0008.md` | live measurements of 21 Sep, smoke logs | May read `https://mimas.laststep.cloud/health` (read-only). The live site is behind `main` on purpose |
| **T-0007** | Deploy | `R-2026-09-22-T-0007.md` | the live site | Never run `tools/deploy/deploy.sh` except with `--dry-run` (Rohan runs deploys) |
| **T-0002** | The online slice | `R-2026-09-17-T-0002.md` | server tests, rung 5 | — |
| **T-0001** | Install the studio | `R-2026-09-17-T-0001.md` | — | Lowest priority; include if the round has room |

**Not in the round:**

- **T-0010** failed on 23 Sep **on evidence, not code**: five captures from one Editor practice game make it pass (the
  list is in `R-2026-09-23-verify-boons.md`). That is a **builder** job and it needs the Editor, so it cannot be a
  verifier's. It *can* run at the same time as the round, because the verifiers never touch the Editor (below): one
  builder takes the five captures straight into `artifacts/` (`ScreenCapture.CaptureScreenshot`, no `.meta`), and a
  fresh verifier re-checks T-0010 afterwards.
- T-0009 already passed (23 Sep). T-0003 is blocked on Rohan; T-0004 is `todo`.

## Rules that make it safe to run in parallel

1. **The lead runs the ladder, one task at a time, before starting any verifier.**
   `node E:/Studios/Trinetra-Game-Studio/tools/ladder/ladder.mjs --project mimas --task T-NNNN` for each task in the
   queue. Several `dotnet build` / `dotnet test` runs on the same solution fight over `bin/` and `obj/`, so **verifiers
   do not run `dotnet` or the ladder themselves**; they read `studio/runs/.ladder/T-NNNN.json`. If one needs a re-run,
   it says so in its reply and the lead runs it. (A verifier that must build can be launched with
   `isolation: "worktree"`, which gives it its own `bin/`.) On `main` the ladder runs today's code, not the task's
   commit — that is the regression check; the verifier reads the task's own diff for everything else.
2. **Verifiers never touch Unity.** There is one Editor, and batch `unity test` / `unity build` refuse while it is open.
   The evidence is the captures already in `artifacts/`. If a claim can only be settled by a new capture or a live
   Editor, that is a **fail on evidence** with the exact capture named — as T-0010's was — not something the verifier
   takes itself. If an EditMode count is needed, the lead runs the batch tests once with the Editor closed and hands
   `artifacts/editmode.xml` to everyone.
3. **The ledger is written one verifier at a time.** `ledger.mjs pass` rewrites one JSON file; two at once can lose
   an update. A verifier that passes **lists the ledger rows it would flip, with evidence, in its Verifier section and
   does not run the tool**. When every verdict is in, the lead messages each passing verifier in turn (SendMessage) to
   run its own flips — the flip stays the verifier's (`verifier.md`) — then runs
   `node E:/Studios/Trinetra-Game-Studio/tools/ledger.mjs check --project mimas`.
4. **Protected paths are read with the Read / Grep tools, never named in a shell command** — the ledger, the design
   page, `.claude/`, the Core tests folder, any `.meta` / `.asset` / `.unity`, anything under a folder called `Temp`.
   The asset guard scans the command text and refuses even a read (STATE).
5. **Nothing is fixed in this session.** Verifiers may not edit code, tests, data or configuration. Everything they find
   goes into a report (below), and Rohan or a later builder decides.

## Where the findings go

- **Each verifier appends a `## Verifier` section to its task's run report** (one verifier per task, so no two write the
  same file; T-0005 and T-0006 are judged inside T-0008's). Verdict first (PASS / FAIL / FAIL on evidence), then reasons,
  each pointing at a file and line or a done-when; on a fail, one list of exactly what would make it pass; on a pass, the
  ledger rows to flip.
- **The lead writes one round report, `studio/runs/R-<date>-verify-round.md`**, in the shape of
  `R-2026-09-23-verify-boons.md`: frontmatter (`task: [...]`, `role: verifier`, `outcome`), a verdict table pointing at
  each run report, the numbers every verifier saw (ladder files, test counts, build size), then **Issues to act on,
  ordered by who owns them**: Rohan · a builder, small · known and deliberate · tooling. "Something found" means a real
  defect or a claim the evidence does not support — a bug, a leak of hidden information, a weakened test, a done-when
  that is only asserted, a doc that says something the code does not do. Not taste, not style, not "could be better".
- **STATE**: the In flight table gets each verdict and the date; "The one thing to do next" gets what the round leaves
  for Rohan. A task that passed stays at `verify` until Rohan has played it (verification-ladder "What done means",
  step 4); a task that failed stays at `verify` with its fix list.

## A prompt for each verifier (fill in the task)

> You are verifying **T-NNNN** for Mimas (`E:\Unity Projects\MimasGame`), in a parallel round with other verifiers.
> Read `.claude/agents/verifier.md` and do exactly that job. Inputs: the task file `studio/tasks/T-NNNN-*.md`, its
> one-pager in `studio/features/`, its plan or spec, its run report `studio/runs/<report>`, its diff (the commits the run
> report lists), the ladder result `studio/runs/.ladder/T-NNNN.json`, and the evidence in `<artifacts path>`.
> Context the run report may not give you: <notes from the table>. Rules for this round: do not run `dotnet`, the ladder
> or any `unity` command (ask the lead if you need a re-run); do not write the ledger — list the rows you would flip;
> read protected paths (ledger, design page, `.claude/`, Core tests, `.meta`/`.asset`/`.unity`) with the Read/Grep tools
> only; edit nothing but your own `## Verifier` section at the end of the run report. Reply with the verdict, the reasons,
> any defect you found outside the done-when (file and line), and the ledger rows you would flip.

Launch them all in one message (one Agent call each, `subagent_type: "verifier"`). Eight is inside the session's
agent guideline; T-0001 is the one to drop if it has to be seven.
