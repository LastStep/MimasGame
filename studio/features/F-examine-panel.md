---
id: F-examine-panel
title: The examine panel — what am I fighting, what have I become
project: mimas
milestone: M4
status: approved
lane: full
design_anchor: docs/design/index.html#examine
created: 2026-09-23
approved: 2026-09-23
spec: docs/specs/2026-09-23-examine-panel.md
ui: [docs/ui/language.md, docs/ui/examine.md]
mock: docs/ui/mockups/examine-manuscript-r3.png
adrs: [ADR-037]
tasks: [T-0011]
---

# The examine panel

**One sentence:** clicking a hero opens a paper plate on the right that shows the hero's painting, health,
action points, six stats as base and net change, their boons oldest to latest, and their gear with each
action as a named tile; everything an action does waits one hover away in a single panel; a click anywhere
else closes it.

## Why now

M2 waits on a playtest, and the next thing the game needs is a look. Rohan spent 23 Sep 2026 deciding it
on the one screen that carries the most information: the interface language (ink, one signal colour per
owner, bone type, no lines, the painting carries the mood) and the examine plate as its first instance.
Building it puts the tokens, the fonts, the drawn glyphs and the hover panel into the client, so every
later screen (action bar, draft cards, banner, lobby) is a re-use rather than a design.

## What the player sees

`docs/ui/examine.md` §1–§4, and the right-hand plate in the pinned render. In one breath: a manuscript
plate slides in from the right; the painting in the lineage's hue with the name in serif over it; heart 17 / 24
with a bar, bolt 1 / 4 with eggs, height 1; six stats as "28 −4", "3 +1"; boons in the order they came;
four items with icon tiles under each, violet-edged when a boon changed or added them; on the enemy, dashed
`?` tiles, `?` boon rows, grey `?` after the lane stats. Hover anything for the panel with the numbers.

## What "correct" means

Every element id in `docs/ui/examine.md` §3 exists; every colour and face is a token from
`docs/ui/language.md`; the five acceptance screenshots of the spec's §14 match the pinned render; nothing
the plate shows is a number the rules would not let the viewer see (`#hidden-info`).

## Not this feature

Art (portrait, emblems, item squares, action icons): placeholders only. Status effects. The other screens.
