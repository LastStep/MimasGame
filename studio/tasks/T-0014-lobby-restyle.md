---
id: T-0014
title: The lobby and the room in ink — two seats facing, gear as tiles, you on the left
project: mimas
feature: F-hud-restyle
milestone: M4
lane: full
status: running
owner: builder
model: opus
worktree:
depends_on: [T-0013]
plan: docs/specs/2026-09-23-hud-restyle.md
allows_assets:
  # Spec §10: the design page's #presentation Lobby line (if T-0013 did not land it).
  - 'docs/design/index.html'
  # Spec §8: new client files get a .meta from the Editor (never hand-made).
  - 'MimasClient/Assets/_Game/UI/RoomLayout.cs.meta'
  - 'MimasClient/Assets/_Game/Tests/EditMode/RoomLayoutTests.cs.meta'
  # Spec §11: captures land under Assets/_Shots; the folder .meta already exists.
  - 'MimasClient/Assets/_Shots/**'
# Rungs by number (studio/game.yaml). Core and the server are untouched; the spec's §11 adds the Unity compile
# check, EditMode tests, the Web build and the browser smoke.
ladder: [0, 1, 2, 5]
done_when:
  - "The lobby and the room are docs/ui/lobby.md: every §2–§3 id is a UXML name; Lobby.uss uses only --mimas-* tokens, radius 0, no --lobby-* variable (spec §8)"
  - "You are always on the left: seat 1 sees itself on the left too; the other seat shows only name and readiness, never gear or god; an empty seat reads WAITING FOR A PLAYER; a bot seat reads RANDOM BOT · READY (spec §8)"
  - "Gear is one tile per preset, no DropdownField; a click selects and un-readies; the tiles and the lineage rows are disabled while ready (spec §8)"
  - "Play vs bot, Create room, Join by code, Copy link, Copy code, Ready, Leave and the after-result line all still work (spec §8, §14)"
  - "Captures in artifacts/t0014/: docs/ui/lobby.md §5 items 1–5, seat 2 from the browser"
  - "EditMode up by 4 (RoomLayoutTests), none softened; console clean; Web build ≤ 13 MB; smoke green on three engines"
  - "Docs: design #presentation Lobby; the UI book status; architecture; STATE; run report"
created: 2026-09-23
started: 2026-09-24
finished:
cost_usd: 0
blocked_by:
---

# The lobby and the room in ink

## Context

The second half of F-hud-restyle, after T-0013 has put the shared tokens, `Ramps` and the ink surface in
place. Design: `docs/ui/lobby.md`; work order: `docs/specs/2026-09-23-hud-restyle.md` §8 (and §0, §9–§14
for T-0014). Canvas board: "Room · two seats facing".

## Scope

`Lobby.uxml`, `Lobby.uss`, `LobbyView.cs` (preset tiles replace the dropdown, lineage rows as elements,
you-left seats, the null guard on the other seat), `UI/RoomLayout.cs` with EditMode tests, captures.

## Out of scope

Anything in the Arena; remembering the preset across scene loads; showing the other seat's gear
(`#q-online-room-loadout` stays open); the full character select (M3-6).

## Notes

- 23 Sep 2026: Rohan approved T-0013 only; this task stays at `plan` until he has seen T-0013 built.
- 24 Sep 2026: Rohan approved it in chat ("lets execute t-0014"); running.
