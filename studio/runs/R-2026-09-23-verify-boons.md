---
id: R-2026-09-23-verify-boons
task: [T-0009, T-0010]
role: verifier
model: fable (coordinating two opus verifiers in fresh contexts)
date: 2026-09-23
outcome: needs-rohan
---

# Boons verification round, 23 Sep 2026

Two verifiers ran in parallel, each cold, each appending its own **Verifier** section:

| Task | Verdict | Where the reasons are |
|---|---|---|
| T-0009 boons groundwork in Core | **PASS** | `studio/runs/R-2026-09-22-T-0009.md` |
| T-0010 boons in the game | **FAIL, on evidence not code** | `studio/runs/R-2026-09-22-T-0010-build.md` |

Numbers both verifiers observed at `3fe5bbc`: Core 452 passed, server 58 passed, EditMode 4, ladder
green 4/4 (T-0010's re-run today; T-0009's file left at `73aa09c` on purpose, since HEAD now carries
T-0010's Core work). Web build 12.38 MiB against the 13 MB ratchet. Three-engine smoke re-run, all OK.
No test skipped or deleted without a stronger replacement. No hidden-info leak found. Ledger untouched
by either builder; nothing flipped by either verifier.

## Why T-0010 fails

`done_when` 6 and 7 are true only by the builder's assertion. The builder's own §14 table says so
("written, not looked at"). Nobody has watched:

1. a **reveal flyover** (`Revealed: <boon>` / `<lineage> · <kind>`) over a unit;
2. an action-bar button whose number a boon changed (the `action--modified` mark) and an attack detail
   line leading with its element word;
3. a **round card** (`ROUND 2 · <map> · you move first`);
4. the **series banner with Back to room** visible;
5. a `boonStat` / `nullify` preview line label.

`done_when` 7 names four screenshots; the reveal is the missing one. What passes it: five captures, or
four plus a sentence saying the fifth is left unproven and why. One practice game in the Editor reaches
all five. Nothing else needs rework.

## Issues to act on

Ordered by who owns them.

### Rohan

- **M3-5 cannot be ticked as written.** It says "best-of-three across **three** ladder maps"; two maps
  ship (`board-3`, `arena-4`) and the ladder wraps (D17). Spec §0.10 put a third map out of scope.
  Reword the ledger line or ship a map. A verifier may not change ledger wording.
- **Two browsers have never played a series.** Spec §7.8 asked for it; not in T-0010's `done_when`;
  already in STATE as the M2 playtest. Still the biggest unproven claim in the project.

### A builder, small (one light task)

- **Vacuous assertion** in `server/Mimas.Server.Tests/SessionTests.cs`,
  `Draft_Timeout_ServerPicksFirstOffer`: `Assert.Equal(-1, a.Session.Round == 0 ? -1 : -1)` is always
  true and its comment claims to check `clock.activePlayer`. Replace with the real assertion (the
  reconnect test shows the shape).
- **`_Shots/examine-boons.png` is mislabelled**: no examine panel is open in it, so the own-hero boon
  list has no capture. Re-take it when taking the five above.
- `docs/architecture.md` lists `Presentation/MatchSession` twice.
- `hermes-sandals.json` omits `exclusive: []` (harmless; default).
- T-0009's run report `commits:` stops at `73aa09c`; `fc19ee9` and `967b64c` are also T-0009.

### Known and deliberate, for the next reader

- `HiddenInfo_NoHiddenLineLeaksBeforeReveal` now skips lines owned by your own units (both seats carry
  a Blessing since P6). `hiddenLinesSeen > 0` still guards it.
- `Session_Filter_OpponentSeesOnlyThatAPickWasMade` lost `Assert.Equal(3, for0.Count)` when the helper
  moved from resign to elimination; the surviving assertions got stronger.
- `Wire.ReadView` now requires `lineage` and `boons` on every unit and throws otherwise. An old client
  against a new server fails at the first view, which is why the live site must be redeployed before
  anyone plays.
- Round 1 online is `board-3` (`ladderPosition: 1`), spec §6.6 had it backwards.
- T-0009 `done_when` 4 ("immune to fire") is proven end to end with the fixture's frost pair; the
  shipped fire pair is asserted at the data level only. Element matching is name-agnostic.

### Tooling

- The asset guard refuses read-only shell commands that mention `docs/design/index.html`, `.claude/`,
  or any path containing `Temp` (so the session scratchpad too). Both verifiers hit it; Read/Grep are the
  route. Worth a look at the guard's read/write distinction.
- `unity status` reported no Editor this session, and the Unity MCP server refused its connection.
  Nothing that needs the Editor was checked live.
