---
id: T-0002
title: Execute the online slice — server rooms, guest auth, room codes, bot seat, clocks, reconnect, lobby
project: mimas
feature: F-online-slice
milestone: M2
lane: full
status: running
owner: builder
model: opus
worktree: main
depends_on: []
allows_assets:
  - 'MimasClient/Assets/_Game/Data/rules.json'
  - 'docs/design/index.html'
  - 'ProjectSettings/EditorBuildSettings.asset'
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
finished:
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
