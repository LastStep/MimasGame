---
name: adr
description: Architecture decision records. Use when a technical choice is being made that a future agent could re-litigate.
---

# ADRs

An ADR exists to stop the same argument happening twice. That is its entire purpose, and it is the test
for whether to write one.

## Write one when

- A future agent, reading the code cold, would reasonably propose the opposite and be wrong.
- The choice was between real alternatives and the loser is still tempting.
- It constrains future work (a data format, a protocol shape, a dependency, a layering rule).
- Rohan chose from an options write-up. The ADR records what he chose and why the others lost.

## Do not write one when

- There was no real alternative.
- It is a naming or style choice.
- It is already stated in the design page or in `CLAUDE.md`.
- You are documenting that you did the obvious thing.

Ceremony is the failure mode here. Ten ADRs nobody reads are worse than three that everybody does.

## Where

| Kind | Location |
|---|---|
| About a game's code, data, protocol or architecture | that project's own decision log (Mimas: `docs/decisions.md`) |
| About game design — a rule, a mechanic, a number | the design page's decision log, **not** an ADR |
| About the studio — tooling, process, the app, agent behaviour | `<studio>/decisions/ADR-NNNN-slug.md` |

Numbers are sequential per log and never reused. A superseded ADR is not deleted or edited: it gets
`status: superseded by ADR-NNNN` and stays.

## Shape

Context (what forced the choice) — Options (what was really considered, with the trade-off that
decided it) — Decision — Consequences, including what this now makes hard. Use
`<studio>/templates/adr.md`.

Four sentences in the right place beat two pages in the wrong one.
