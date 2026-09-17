---
id: F-aiming-client
title: Aiming — the board answers "what happens if I shoot there?"
project: mimas
milestone: M3
status: done
lane: full
design_anchor: docs/design/index.html#hud
spec: docs/specs/2026-09-16-aiming-client.md
created: 2026-09-16
approved: 2026-09-16
---

# Aiming: the board answers "what happens if I shoot there?"

**One sentence:** arming an attack turns the board into a live answer — where you can reach, what is in
the way, and exactly what it would do.

> Migrated 17 Sep 2026 from `docs/specs/2026-09-16-aiming-client.md`. Built and played on 16 Sep;
> **not verified**.

## What the player sees

Two true circles for the range band, drawn per fragment in a hand-written tile shader so they bend over
steps and plateaus. One resolved hover drives everything: a dashed green path with a ring on the point
it will hit; red stopping at the blocker with an X and the reason as the tooltip's first line; grey
with "Out of range"; grey and dimmed for a wall. **A refused shot still shows the damage it would do**
— the same `DamageCalculator` the rules resolve with. On resolution the projectile flies the same curve
the preview drew and the hit lands on impact.

## Done when

- [x] Range circles, path preview with blocked reasons, out-of-range tag, projectile playback
- [x] Props on the board with hit-mark rings, hp tags, examine text, and they shrink away when killed
- [x] Hover resolves bodies before tiles, by component rather than layer, so `ProjectSettings` was not
      touched
- [x] Verified in play on arena-4 (`artifacts/aim-*.png`), console clean, Core and server untouched

## Not in this

Real art — props, the projectile and the heroes are all placeholders. A prop prefab library, tile
tinting for movement, touch input, a Web build of this slice.
