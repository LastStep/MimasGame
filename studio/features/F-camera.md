---
id: F-camera
title: The camera — look around the arena, from your side of it
project: mimas
milestone: M4
status: approved
lane: full
design_anchor: docs/design/index.html#camera
created: 2026-09-23
approved: 2026-09-23
spec: docs/specs/2026-09-23-camera.md
adrs: [ADR-038]
tasks: [T-0012]
---

# The camera

**One sentence:** the tilted camera becomes the player's: Q and E turn it round the arena, W A S D pan it
without leaving the arena, the wheel zooms, Space sends it home to the centre, and V flips between the
side-on view (your end on the left) and a view from behind your end looking at the opponent.

## Why now

Rohan asked for it on 23 Sep 2026, in one message with its own list. The camera has been two fixed poses
since the first slice; the arena-4 plateau, the pillars and the walls already hide things from a fixed
angle, and the examine plate now covers the right third of the screen.

## What the player sees

The match opens on the default view (a setting): **side-on** — your hero's end of the arena on the left,
the opponent's on the right, whichever seat you hold — or **behind you** — the camera past your end, looking
across at the opponent's. Holding Q or E turns the camera round the point it looks at, freely, stopping where
you let go. W A S D slide that point across the board in the direction the camera faces, and stop at a set
distance past the arena's edge. The wheel zooms between a nearest and a furthest distance. Space glides back
to the default view, centred, at its own zoom. V glides to the other view, centred. Tab (development) still
swaps to the fixed top-down camera, which none of these keys move.

## What "correct" means

Rohan's list of 23 Sep, plus his four answers (spec §1). Every speed, limit and framing is an Inspector value
on one component that takes effect live in Play Mode. The side-on view puts your own spawn on the left of the
screen for seat 1 and for seat 2 (an EditMode test says so). Nothing about the camera reaches Core or the wire.

## Not this feature

Top-down on the same rig, middle-drag pan, snapping to hex faces, remembering the view across rounds, a
follow-your-hero camera, rebinding keys, touch.
