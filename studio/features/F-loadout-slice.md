---
id: F-loadout-slice
title: Gear replaces classes
project: mimas
milestone: M3
status: done
lane: full
design_anchor: docs/design/index.html#character
spec: docs/specs/2026-09-16-loadout-slice.md
created: 2026-09-16
approved: 2026-09-16
---

# Gear replaces classes

**One sentence:** a hero is no longer a class, it is base stats plus four items, and what it can do
comes from what it is wearing.

> Migrated 17 Sep 2026 from `docs/specs/2026-09-16-loadout-slice.md`, which remains the full work
> order. Built and played; **not verified** — no verifier has been through it, so the M3 ledger entry
> is still false.

## What the player sees

You pick a weapon, a crown, boots and armour. Your abilities are the walk, plus whatever each item
grants. Damage is `weapon` or `spell`; `melee` / `ranged` / `magic` are gone. The examine panel lists
your gear with stat summaries and groups each `?` ability row under the item that grants it.

## Done when

- [x] `classes/` is gone; a hero is `rules.baseStats` plus four `items/*.json`
- [x] `PlayerView` publishes item ids and each ability's source item (identity public, ability hidden)
- [x] The launch catalogue exists: longbow, flintlock, one crown, leaping and blink boots, one jerkin
- [x] 197 Core tests green at the time of the slice

## Not in this

Elements and the `element` field, `nullify`, boons, lineages, the draft, the session wrapper, character
select, gear on the 3D model, the blade weapon, presets.

## Note

Every shipped number is a hand-set placeholder: any kit is hp 28, ap 3, Strength 3, Magic 4, armour
2/2, and a full turn deals about 12. Nothing has ever measured them. That is what ladder rung 7 is for.
