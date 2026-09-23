---
id: F-hud-restyle
title: The HUD in the ink language — the bar, the turn, the draft, the room
project: mimas
milestone: M4
status: approved
lane: full
design_anchor: docs/design/index.html#hud
created: 2026-09-23
approved: 2026-09-23   # Rohan, for T-0013; T-0014 stays at plan until he has seen T-0013 built
spec: docs/specs/2026-09-23-hud-restyle.md
ui: [docs/ui/language.md, docs/ui/hud.md, docs/ui/between-rounds.md, docs/ui/lobby.md, docs/ui/examine.md]
mock: https://claude.ai/artifact/K9qZcJQ115G5G685sdMAwy (page "HUD", rounds 1–3)
adrs: [ADR-039, ADR-040]
tasks: [T-0013, T-0014]
---

# The HUD in the ink language

**One sentence:** every screen a player meets — the action bar, the turn, End Turn, the unit tags, the
attack preview, the draft, the round card and results, the lobby and the room — moves onto the examine
plate's ink language, so the game has one look instead of two, and the examine plate itself goes ink so
nothing is left on paper.

## Why now

T-0011 put the language into the client on one screen; everything else still wears the grey panels of
15 Sep. Friends play on 10 Oct. Rohan chose this over the balance sim and the next M3 design round on
23 Sep 2026, then decided it over three rounds on the canvas the same evening.

## What the player sees

`docs/ui/hud.md`, `docs/ui/between-rounds.md`, `docs/ui/lobby.md`. In one breath: the board floats in ink;
top centre, a track with your name and series pips on the left and theirs on the right, a marker on the
half whose turn it is and that half burning towards the centre in the last seconds; bottom left, action
points as a bolt, a light numeral and eggs, then three captioned rows of 44px tiles — Movement, Weapon,
Spell — each with its cost as dots, a violet edge when a boon changed or added it, a closed eye while the
opponent has not seen it; your boons as glyph circles down the right edge; Resign above End Turn bottom
right, End Turn lit when nothing is left. Arm an attack and the one hover panel opens over the target with
the damage the rules will do. Between rounds, three ink cards over the dark board; the round card and the
results in a dark band across the middle. The room: the code on top, you on the left with four gear tiles
and three lineages, them on the right showing only their name and whether they are ready.

## What "correct" means

Every element id in the three screen pages exists by name; every colour, face, size and gap is a token of
`docs/ui/language.md`; the acceptance screenshots of the spec match the canvas boards they name; the one
new wire field tells a player only what the opponent has already seen of **their own** hero
(`#hidden-info`); nothing about a rule changes.

## Not this feature

Art (action icons stay letters, portraits stay washes); status effects; the full character-select screen
(M3-6, later); new rules for "disabled by an effect"; the time-control bank; a third map.
