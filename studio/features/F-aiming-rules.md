---
id: F-aiming-rules
title: Aiming — height, line of sight, trajectories and props
project: mimas
milestone: M3
status: done
lane: full
design_anchor: docs/design/index.html#line-of-sight
spec: docs/specs/2026-09-16-aiming-rules.md
created: 2026-09-16
approved: 2026-09-16
---

# Aiming: height, line of sight, trajectories and props

**One sentence:** attacks aim at a point on a body and are resolved against heights in body units, so
cover is something you stand behind rather than a rule you look up.

> Migrated 17 Sep 2026 from `docs/specs/2026-09-16-aiming-rules.md`. Built and played; **not
> verified**.

## What the player sees

A level-1 step no longer blocks a flat shot between two heroes; a level-2 plateau does, and so does any
body. A bow lobs over a wall without needing sight. A gun does not. Pillars can be shot down and the
tile walked onto.

## Done when

- [x] `rules.json` carries `heights` (1 level = 3 units, hero body 6, aim 4)
- [x] Sight is a straight ray between aim points, integer cross-multiplied; a graze blocks, endpoints
      and holes never block, unwalkable terrain is solid
- [x] Every attack declares `lineOfSight` and `trajectory` (`direct`, `arc` with an apex, or `sky`)
      behind a fail-closed registry
- [x] Range bands are circular, so the client can draw a true circle
- [x] Props are bodies: `pillar` destructible, `wall` indestructible; they occupy a tile, block sight
      and trajectories, and a destroyed one frees its tile without ending the round
- [x] 287 Core tests green

## Not in this

Enchant or boon overrides of a trajectory (only the seam exists), area and multi-target shapes, owned
props, rubble, units-as-cover penalties, a beam trajectory. `sky` exists in code and no ability uses it.
