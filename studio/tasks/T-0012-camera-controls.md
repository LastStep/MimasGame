---
id: T-0012
title: The camera — rotate, pan, zoom, recentre, and a view from behind your side
project: mimas
feature: F-camera
milestone: M4
lane: full
status: verify
owner: builder
model: opus
worktree:
depends_on: []
plan: docs/specs/2026-09-23-camera.md
allows_assets:
  # Spec §6: the design page's #camera section gains the controls Rohan decided on 23 Sep.
  - 'docs/design/index.html'
  # Spec §5: the rig goes on vcam_Tilted and is wired to the director, the board input and the session.
  - 'MimasClient/Assets/_Game/Scenes/Arena.unity'
  # Spec §4: new client files get a .meta from the Editor (never hand-made).
  - 'MimasClient/Assets/_Game/Presentation/Cameras/ArenaCameraRig.cs.meta'
  - 'MimasClient/Assets/_Game/Presentation/Cameras/CameraRigMath.cs.meta'
  - 'MimasClient/Assets/_Game/Tests/EditMode/CameraRigMathTests.cs.meta'
# Rungs by number (studio/game.yaml): 0 asset safety, 1 core build, 2 core tests, 5 server integration.
# Core and the server are untouched; the spec's §7 adds the Unity compile check, EditMode tests and captures.
ladder: [0, 1, 2, 5]
done_when:
  - "Q and E turn the tilted camera round the point it looks at while held, at an Inspector speed in degrees per second, freely (no snap) (spec §2)"
  - "W A S D pan that point in the direction the camera faces, at an Inspector speed, and it never goes further from the arena's centre than the outermost tile centres plus an Inspector number of tiles (spec §2, §3)"
  - "Space glides to the default view centred on the arena at that view's zoom; V glides to the other view; the default view is an Inspector enum with Side On and Behind You (spec §2)"
  - "Side On puts your own spawn on the left of the screen for both seats, and Behind You looks from your spawn towards the opponent's; EditMode tests assert both (spec §3, §7)"
  - "The mouse wheel zooms between an Inspector nearest and furthest distance, and does nothing while the pointer is over the HUD (spec §2)"
  - "None of the keys move the top-down camera; Tab still swaps to it; the camera works while time is stopped (spec §2)"
  - "Console clean after compile; EditMode tests up by the new ones, none softened; captures of Side On, Behind You and a rotated/panned/zoomed view in artifacts/t0012/ named in the run report"
  - "Docs: design #camera, ADR-038, Presentation README, STATE, run report"
created: 2026-09-23
started: 2026-09-23
finished: 2026-09-23
cost_usd: 0
blocked_by:
---

# The camera controls

## Context

Rohan's request of 23 Sep 2026 and his four answers are the spec's §1. The camera was two fixed Cinemachine
vcams switched by `CameraDirector` (Tab); the tilted one has no body or aim, so its transform is the shot.

## Scope

A pure `CameraRigMath` (orbit pose, view yaw per seat, pan clamp, arena extent) with EditMode tests, an
`ArenaCameraRig` on `vcam_Tilted` that reads the keys and poses it, one call from `MatchSession` once the
board is built, and `BoardInputController.PointerOverOverlay` so the wheel leaves the HUD alone.

## Out of scope

Everything in F-camera's "Not this feature". The top-down vcam and `CameraDirector`'s Tab stay as they are.

## Notes
