---
id: T-0006
title: Refused shots show the blocking hex, and prop art matches the hex it blocks
project: mimas
feature: F-aiming-client
milestone: M2
lane: light
status: verify
owner: builder
model: sonnet
worktree:
depends_on: []
allows_assets: []
done_when:
  - "A reproduction is written down first: at least two concrete (attacker hex, target hex, map) cases from arena-4 where the shot looks open and is refused, with the rules' named blocking hex for each"
  - "For each case, the run report says whether the rule or the picture is wrong. The rule is changed only if the design page is wrong, and then the design page changes too; otherwise the rule stands"
  - "When a shot is refused for sight or trajectory, the client highlights the hex the rules named as the blocker, for as long as the target is hovered"
  - "The placeholder prop box fills the hex footprint it blocks (or the run report argues why not), so the picture no longer undersells the blocking volume"
  - "Core tests unchanged and green; EditMode tests green; unity command console has no error CS and no Exception"
ladder: [0, 1, 2, 7]
created: 2026-09-21
started:
finished:
cost_usd: 0
blocked_by:
---

# Refused shots show the blocking hex

## Context

From Rohan's playtest on 21 Sep 2026 (`studio/playtests/2026-09-21-rohan.md`, finding 2): "some line of
sight detection issues, maybe its the props or walls which have bigger collider than their visual."

The rules (`docs/design/index.html#line-of-sight`, ADR-023) treat every hex as a full prism: a pillar's
`bodyHeight` covers the whole hex, and a grazing ray is blocked if either flanking hex blocks. The
placeholder art (`PropView.BuildPlaceholder`) draws a box narrower than the hex. So the player sees a
clear corner where the rules see a wall. Pillar 5 (`studio/pillars.md`): if a player cannot tell why a
shot was refused, that is a bug in the game.

## Scope

1. Reproduce before touching anything. `Mimas.Core` refusals already name the blocking hex; log it.
2. Decide per case: rule wrong (rare, needs the design page) or picture wrong (expected).
3. Show the blocker: the targeting presentation (`#aiming-presentation`, ADR-025) already draws range
   circles in the tile shader; add a blocker highlight on the named hex while the refused target is
   hovered.
4. Make the placeholder box fill the hex footprint, so the volume the rules block is the volume drawn.

## Out of scope

Changing the supercover rule, heights, aim points or prop data. Real prop art.

## Notes

**Executed as part of T-0008** (`docs/specs/2026-09-21-m2-browser-finish.md` §7, commits 7–8 of
§11), unchanged in scope. Close this task when T-0008 verifies.

Heroes are also whole-hex bodies of height 6. If a case turns out to be a hero body blocking a shot
that looks open past its shoulder, the fix is the same highlight, not a thinner hero.

**Done 21 Sep 2026 inside T-0008** (commits "core-tests: two arena-4 sight cases that name their
blocker", "client: refused shots tint the blocking tile; props are hex prisms that fill their hex",
and "client: the blocking body wears the mark too, not just the hex under it").

The two documented cases, both of which say the rule is right and the picture was wrong:
a grazing ray refused by the pillar at (-2,-2) that the board draws *beside* the line, and a shot at
a pillar refused by the opponent standing in front of it. Pictures: `artifacts/blocker-highlight.png`
(the wall in front of the hero is red, the X is on it, the HUD says No line of sight) and
`artifacts/prop-prism.png`. Status `verify`; it is the T-0008 verifier who closes it.
