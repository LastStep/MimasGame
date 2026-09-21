---
id: T-0008
title: Finish M2 in the browser — rematch in the room, Mimas Web template, Copy code, blocker shown, origin check, multi-browser smoke
project: mimas
feature: F-m2-browser-finish
milestone: M2
lane: full
status: running
owner: builder
model: opus
worktree:
depends_on: [T-0007]
allows_assets:
  # Written by Unity itself during WebBuild.Build: productName, companyName, splash off, template.
  # Commit it with the WebBuild.cs change and nothing else under ProjectSettings/.
  # Both spellings, for the same reason T-0003 needed both: git sees the repo-relative path, a
  # `unity command` argument sees the Unity-relative one.
  - 'MimasClient/ProjectSettings/ProjectSettings.asset'
  - 'MimasClient/ProjectSettings/GraphicsSettings.asset'
  - 'ProjectSettings/ProjectSettings.asset'
  - 'ProjectSettings/GraphicsSettings.asset'
  # The design page: rematch becomes rule 9 of #online, plus the lobby and targeting bullets (spec §10.1).
  - 'docs/design/index.html'
  # Made by Unity itself when it imported the new Web template folder (spec §8.1, which says to commit
  # them with the assets). Golden rule 1 requires an asset and its .meta to travel together, and the
  # guard would otherwise refuse to let git see them. Same reason T-0003 declared its one .cs.meta.
  - 'MimasClient/Assets/WebGLTemplates.meta'
  - 'MimasClient/Assets/WebGLTemplates/**'
  # Two new Core tests that document arena-4's real refusals before the picture is changed to match
  # them: spec §7 step 1, plan P-T-0008 step 6, and done_when "Two arena-4 sight cases are Core tests".
  # Added by the builder on 21 Sep after the guard refused the write; nothing existing is softened or
  # deleted, and the run report says so.
  - 'shared/Mimas.Core.Tests/SightArena4Tests.cs'
done_when:
  - "After a match ends, both seats receive room.state with the same code and ready:false, and both pressing Ready starts a second match with round:2 (server tests, and a two-round match over real sockets)"
  - "A seat whose connection is gone at the result is freed and a third client can take it by code; a room with no human closes"
  - "auth.resume while seated in a waiting room returns auth.ok with the room code, followed by room.state"
  - "With Mimas:AllowedOrigins set, /ws refuses a foreign Origin with 403 and accepts the listed one; with it unset every origin connects (tests); appsettings.Production.json names https://mimas.laststep.cloud and is in the publish output"
  - "In the Editor: Play vs bot, resign, banner says Back to room, the room panel shows the result and the bot seat ready, Ready starts a second match; captured"
  - "The room panel has a Copy code button; pressing it puts exactly the code on the browser clipboard through WebClipboard (headed Chromium smoke reads it back)"
  - "Two arena-4 sight cases are Core tests that assert the reject reason and the BlockedAt hex; a refused shot tints the blocking tile while hovered; props are hex prisms filling their hex; captured"
  - "Build/Web builds through WebBuild.Build with the PROJECT:Mimas template: title Mimas, no footer, no Unity splash, full-window canvas; size within the 13 MB ratchet"
  - "browser-smoke.mjs --browser chromium|webkit|firefox runs green against the local build with a [timing] line each"
  - "Design page #online has rule 9 (rematch, decided 21 Sep 2026); ADR-032 and ADR-033 in docs/decisions.md; networking.md, hosting.md, roadmap.md updated"
  - "Core tests +2, server tests +9, EditMode green, unity command console has no error CS and no Exception"
ladder: [0, 1, 2, 5, 7, 10]
created: 2026-09-21
started: 2026-09-21
finished:
cost_usd: 0
blocked_by:
---

# Finish M2 in the browser

## Context

Deploy is live (T-0007, 21 Sep). What a friend meets in the first five minutes is what is left of M2:
the page, the code, the unexplained refusal, and a match that ends by evicting both players from the
room. Rohan answered the four questions on 21 Sep; `docs/specs/2026-09-21-m2-browser-finish.md` is
the work order and `studio/plans/P-T-0008.md` the shape of it. T-0005 and T-0006 are executed here
as written; they are closed when this task verifies.

## Scope

Spec §3–§10, in the commit order of §11. Two sessions is fine: server (§3–§4, no Editor) then client.

## Out of scope

Spec §14. In one line: session score, ratings, accounts, spectating, chat, a logo, mobile, rules
changes, real prop art, OQ-N16.

## Notes

- The room's `MatchId` is reused across rounds; `round` disambiguates. ADR-032 says why.
- The origin list is JSON in `appsettings.Production.json`, never C# and never the unit; no `--setup`
  needed after this lands. ADR-033.
- Copy Unity's Default Web template and edit it; do not write the loader boilerplate from memory.
- `PlayerSettings.SplashScreen.show` — check the 6000.4 docs first; spec §13 has the fork.
