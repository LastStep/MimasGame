---
id: T-0005
title: A Copy code button in the room, beside Copy link
project: mimas
feature: F-online-slice
milestone: M2
lane: light
status: todo
owner: builder
model: sonnet
worktree:
depends_on: []
allows_assets: []
done_when:
  - "The room panel in the Lobby scene shows a Copy code button next to Copy link"
  - "Pressing it puts exactly the four-letter code on the browser clipboard through WebClipboard, and the status line says so honestly (copied / could not copy)"
  - "tools/smoke/browser-smoke.mjs has a step that presses it and reads the clipboard back, and it passes in a headed Chromium"
  - "unity command console has no error CS and no Exception after the change"
ladder: [0, 1, 7, 10]
created: 2026-09-21
started:
finished:
cost_usd: 0
blocked_by:
---

# A Copy code button in the room

## Context

From Rohan's playtest on 21 Sep 2026 (`studio/playtests/2026-09-21-rohan.md`, finding 1): "Like copy
link we need copy code button." The code is what people paste into Discord; the link is what they
click. Both should be one press.

## Scope

`MimasClient/Assets/_Game/UI/LobbyView.cs` already has `HandleCopyLink` going through
`WebClipboard.Copy` (the `GUIUtility.systemCopyBuffer` route never reaches the browser clipboard, see
STATE). Add the sibling for the code. The button lives in `Lobby.uxml`, which is a text asset and not a
guarded path; the scene is not touched.

## Out of scope

Any change to how codes are generated or validated. Any change to the join field.

## Notes

**Executed as part of T-0008** (`docs/specs/2026-09-21-m2-browser-finish.md` §6, commit 6 of §11),
unchanged in scope. Close this task when T-0008 verifies.

The 18 Sep harness (`browser-smoke.mjs`) can seed and read the real clipboard (`clip:` step, the
context requests clipboard permissions); reuse that for the check rather than trusting the status line.
