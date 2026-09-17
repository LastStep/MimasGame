---
name: reporting
title: How to report to Rohan
scope: always
applies_to: [builder, verifier, researcher, producer, playtest-analyst]
---

# Reporting

**Rohan never reads code.** He is the Game Director: he decides what the game is, he plays it, he
approves plans in plain language. Every word you write for him assumes that.

## The register

- Plain sentences. No adjectives you cannot defend. No "successfully", "robust", "comprehensive",
  "significantly improved".
- Say what changed in terms of the game, then in terms of the system. "Two browsers can now join the
  same room with a code" before "added `RoomRegistry` to `Mimas.Server`".
- Numbers, not impressions. `287 -> 294 tests`, `build 12.5 -> 12.9 MB`, `win rate 51.2% over 1000
  games`.
- Short by default. A run report is a page. A brief is a screen. **Exception:** technical and design
  decisions get the full write-up, because that is what Rohan is choosing from.
- Lead with what he has to do. If nothing, say so on line one.

## What every report must contain

1. **What is different now** — one paragraph a player would understand.
2. **Evidence** — ladder rungs with real output, screenshots at the game camera, test counts, the
   before/after numbers.
3. **What did not work** — the thing you tried and abandoned, the rung that went red twice, the thing
   that is slower than it should be. A report with no negatives is not a report, it is a press release,
   and it teaches Rohan to stop reading.
4. **What it cost** — session cost in USD, wall-clock time.
5. **What is next, and what needs him.**

## Honesty rules

- Never report a rung green unless the ladder result file says so.
- Never describe intended behaviour as observed behaviour. If you did not run it, write "not run".
- If you deviated from the approved plan, that is the first line of the report, not a footnote —
  what you did instead, and why.
- If you are unsure whether something works, say "unverified" and name the rung that would settle it.
- Partial is fine and normal. Say which part, and what is left.

## The files

| Report | File | Written by | When |
|---|---|---|---|
| Run report | `studio/runs/R-<date>-T-NNNN.md` | the agent doing the work | opened at session start, appended to throughout |
| Daily brief | `<studio>/briefs/<date>.md` | producer | 08:00, pushed to Discord + email |
| Verdict | appended to the run report | verifier | after the ladder |
| Playtest | `studio/playtests/<date>-<who>.md` | playtest analyst | after Rohan plays |
| Options | `studio/decisions/` or the project's | researcher | when a director decision is reached |

## Notifications

One line per event to Discord, linking the file. Email only for something that needs a decision.
Do not notify on progress — "still working" is noise, and noise is how the inbox stops being read.
