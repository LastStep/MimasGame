---
id: T-0002
title: Execute the online slice — server rooms, guest auth, room codes, bot seat, clocks, reconnect, lobby
project: mimas
feature: F-online-slice
milestone: M2
lane: full
status: verify
owner: builder
model: opus
worktree: main
depends_on: []
allows_assets:
  - 'MimasClient/Assets/_Game/Data/rules.json'
  - 'docs/design/index.html'
  - 'ProjectSettings/EditorBuildSettings.asset'
  - 'shared/Mimas.Core.Tests/**'
  - 'studio/game.yaml'
  # Both spellings: the repo-relative path git sees, and the Unity-relative one that appears inside a
  # `unity command --path Assets/...` argument, which is how every one of these is actually written.
  - '**/_Game/Scenes/Lobby.unity'
  - '**/_Game/Scenes/Lobby.unity.meta'
  - '**/_Game/Scenes/Arena.unity'
  - '**/_Game/Settings/LoadoutPresets.asset'
  - '**/_Game/Settings/LoadoutPresets.asset.meta'
done_when:
  - "dotnet build Mimas.slnx, dotnet test shared/Mimas.Core.Tests (>= 320) and dotnet test server/Mimas.Server.Tests are green"
  - "Two fake clients join the same room by code and play a full match over WebSocket through Mimas.Server"
  - "The server never serialises a MatchState; a test asserts it"
  - "Guest auth with a resumable token; auth.resume returns the same playerId and rejoins a live room"
  - "The 30 s turn is server-authoritative; a timeout arrives as EndTurnCommand(Timeout)"
  - "Resign and disconnect-forfeit both end the match through ResignCommand with the right MatchEndReason"
  - "The client half (lobby scene, NetClient, presenter/driver split) is built and verified in a warm Editor, or reported as not reachable"
ladder: [0, 1, 2, 5]
created: 2026-09-17
started: 2026-09-17
finished: 2026-09-17
cost_usd: 0
blocked_by:
---

# Execute the online slice

The work order is `docs/specs/2026-09-17-online-slice.md` plus **Amendment A1** in that file (§2a),
which replaces the FIFO queue with room codes and moves loadout choice to after the room is joined.

Amendment A1 is the resolution of `studio/decisions/OPT-0001-how-two-friends-meet.md`, decided by
Rohan on 17 Sep 2026: **option B, room codes**, with the addition that the loadout is chosen in the
room rather than in the lobby.

Everything else in the spec stands as written, including its §12 defaults for forks and its §0 rules
(autonomous, small commits to `main`, server half before client half, no Editor edits by hand).

## Why `allows_assets` names the Core test project

`shared/Mimas.Core.Tests/**` is protected because a task must not be able to weaken the gate that
judges it. This task's whole point is new rules — `ResignCommand`, `MatchState.FromView`,
`Mimas.Core.Protocol.Wire` — and §6.1 of the spec names the twenty-odd tests to add, file by file and
test by test, as part of the approved work order. The `test_count` ratchet (287) is what keeps this
honest: tests may only be added, and the ladder fails if the count drops. Existing tests are edited
only where §12's first fork applies (a `MatchOver` reject reason), and any such edit is named in its
commit.

The other paths: `rules.json` gains the `clock` block (§3.1), `docs/design/index.html` gains the status
and changelog updates §9.4 requires, and the build settings asset gains the Lobby scene via
`add_scene_to_build` (§7.9) — the one `ProjectSettings` change the spec allows, made through the live
Editor, never by hand.

## Why `allows_assets` names two scenes and an asset

Golden rule 2 says never hand-edit a `.unity` or `.asset`; drive the live Editor instead. That is what
this task does — `create_scene`, `create_gameobject`, `attach_script`, `create_asset`,
`set_serialized_field`, `save_scene` — but the asset guard matches the *path*, not how it is written,
so driving the Editor from a shell trips it too. Declaring them is the documented way to say "yes, on
purpose", and every one of these is written by Unity:

- `Scenes/Lobby.unity` (+ meta) — new, §7.5. The lobby is its own scene (D6, ADR-028).
- `Scenes/Arena.unity` — `_buildOnAwake` goes off on `/Board` (§7.3), because online the map is not
  known until `match.start` arrives and the board can no longer build itself from a serialized id.
- `Settings/LoadoutPresets.asset` (+ meta) — new, §7.7, with the four shipped kits.

No `.unity`, `.asset` or `.meta` file is edited by hand at any point.

`studio/game.yaml` is named for the rung this task builds: flipping rung 5 (`server integration`) from
`enabled: false` to `true`, and seeding its `server_test_count` ratchet at 34, because `ladder --bless`
only raises ratchet keys that already exist and a ratchet with no entry guards nothing. The spec says in
as many words that this task's test project becomes rung 5, and the ladder runner refuses a task that
requires a rung that does not exist yet. That is the gate getting
stronger, not weaker: rung 5 is `required: true`, so from here on no task can be called done while the
online game is broken. Nothing else in the file is touched by hand, and `test_count` was raised from
287 to 321 by `ladder --bless`, which is the tool's own job.
