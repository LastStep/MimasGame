---
id: T-0009
title: Boons groundwork in Core — definitions, the unit overlay, elements, reveal, draft, session
project: mimas
feature: F-boons
milestone: M3
lane: full
status: running
owner: builder
model: fable
worktree:
depends_on: []
plan: docs/specs/2026-09-21-boons-groundwork.md
allows_assets:
  # The design page records the thirteen closed questions and the new data schema (spec §10.4).
  - 'docs/design/index.html'
  # Spec §9 requires five new test files and additions to five existing ones (new tests only; no
  # existing assertion is softened or removed). Added 2026-09-22 by the build session, as T-0008 did.
  - 'shared/Mimas.Core.Tests/BoonContentTests.cs'
  - 'shared/Mimas.Core.Tests/BoonOverlayTests.cs'
  - 'shared/Mimas.Core.Tests/BoonRevealTests.cs'
  - 'shared/Mimas.Core.Tests/DraftTests.cs'
  - 'shared/Mimas.Core.Tests/SessionTests.cs'
  - 'shared/Mimas.Core.Tests/DataTests.cs'
  - 'shared/Mimas.Core.Tests/CombatTests.cs'
  - 'shared/Mimas.Core.Tests/MirrorTests.cs'
  - 'shared/Mimas.Core.Tests/ProtocolTests.cs'
  - 'shared/Mimas.Core.Tests/ContentTests.cs'
  - 'shared/Mimas.Core.Tests/LoadoutTests.cs'
  # Spec §0.7: the Editor generates a .meta for every new Core source and golden rule 1 says an asset
  # and its .meta are committed together. Written by the Editor, never by hand.
  - 'shared/Mimas.Core/Runtime/Data/BoonDef.cs.meta'
  - 'shared/Mimas.Core/Runtime/Data/LineageDef.cs.meta'
  - 'shared/Mimas.Core/Runtime/Data/SeriesDef.cs.meta'
  - 'shared/Mimas.Core/Runtime/Data/DraftDef.cs.meta'
  - 'shared/Mimas.Core/Runtime/Data/BoonRulesDef.cs.meta'
  - 'shared/Mimas.Core/Runtime/Units/BoonOverlay.cs.meta'
  - 'shared/Mimas.Core/Runtime/Match/PlayerBuild.cs.meta'
  # New Core folder and its Unity .meta, written by the Editor if one is reachable (spec §0.7);
  # Unity owns every .meta, so they are declared, never hand-made.
  - 'shared/Mimas.Core/Runtime/Session.meta'
  - 'shared/Mimas.Core/Runtime/Session/**'
  - 'MimasClient/Assets/_Game/Data/boons/**'
  - 'MimasClient/Assets/_Game/Data/lineages/**'
  - 'MimasClient/Assets/_Game/Data/boons.meta'
  - 'MimasClient/Assets/_Game/Data/lineages.meta'
  - 'MimasClient/Assets/_Game/Data/abilities/**'
  - 'MimasClient/Assets/_Game/Data/modifiers/**'
  - 'MimasClient/Assets/_Game/Data/rules.json'
  - 'MimasClient/Assets/_Game/Content/GameDataManifest.asset'
ladder:
  - "dotnet build Mimas.slnx"
  - "dotnet test shared/Mimas.Core.Tests"
  - "dotnet test server/Mimas.Server.Tests"
  - "verifier in a fresh context agrees with spec §14"
done_when:
  - "boons/ and lineages/ load from the shipped folder; every link rule in spec §5 has a failing-case test; the shipped three lineages cover every item"
  - "A unit built from gear + lineage + boons has floored stats, attached modifiers, resolved overrides, added elements and granted abilities, and MatchState.ResolveAbility is the only ability lookup (test)"
  - "A Strength Blessing, a range Enchant, a cost Enchant, an added element, a Sigil and an hp Blessing each reveal by the rule in spec §6.5, and the whole-match sweep shows nothing reaching the opponent early"
  - "Fire immunity zeroes a fire bolt with one Nullify line and is unknown in the preview until revealed"
  - "Draft.Offer gives one of each kind when possible, respects gear, ownership, stackable and exclusive, and is seed-deterministic"
  - "Session plays a best-of-3 with a draft between rounds, wraps the ladder, seats the loser first, keeps reveals across rounds, and replays identically from a seed"
  - "hits and the trajectory swap parse and fail closed with a message naming the spec; no shipped file uses them"
  - "server/Mimas.Server compiles and its tests pass with no server file changed"
  - "data.md, ADR-034, ADR-035, the design page edits of spec §10 and STATE are done; run report complete"
---

# Boons groundwork (part 1 of F-boons)

The work order is the spec: `docs/specs/2026-09-21-boons-groundwork.md`. Read its §0 first. It is
Core-only by Rohan's decision; the server must not change and the client is not touched. Executing
model: Fable (decided 21 Sep 2026).

Open the run report `studio/runs/R-<date>-T-0009.md` before writing code, and note every asset-guard
false positive there (the guard refuses read-only `sed`/`awk` over protected paths; use Read/Grep).
