---
id: T-0010
title: Boons in the game — the room hosts the session, the lineage row, the draft over the board, the presentation of boons and reveals
project: mimas
feature: F-boons
milestone: M3
lane: full
status: running
owner: builder
model: opus
worktree:
depends_on: [T-0009]
plan: docs/specs/2026-09-22-boons-in-game.md
allows_assets:
  # Spec §8.5: the design page records P1–P8, closes q-bots-draft and half of q-pres-select.
  - 'docs/design/index.html'
  # Spec §9: Core test additions (new tests only; no existing assertion softened or removed).
  - 'shared/Mimas.Core.Tests/SessionTests.cs'
  - 'shared/Mimas.Core.Tests/ProtocolTests.cs'
  - 'shared/Mimas.Core.Tests/ContentTests.cs'
  - 'shared/Mimas.Core.Tests/DataTests.cs'
  - 'shared/Mimas.Core.Tests/BoonContentTests.cs'
  - 'shared/Mimas.Core.Tests/DraftTests.cs'
  # Spec §5: nine boons and one ability, with the .meta files the Editor writes and the manifest it
  # regenerates (golden rule 1: committed with the JSON, never hand-made).
  - 'MimasClient/Assets/_Game/Data/boons/**'
  - 'MimasClient/Assets/_Game/Data/lineages/**'
  - 'MimasClient/Assets/_Game/Data/abilities/**'
  - 'MimasClient/Assets/_Game/Content/GameDataManifest.asset'
  # Spec §7: new client files and the Editor's .meta for each.
  - 'MimasClient/Assets/_Game/Presentation/Match/LocalSessionHost.cs.meta'
  - 'MimasClient/Assets/_Game/Tests/EditMode/MirrorResolverTests.cs.meta'
  # Spec §7.7: the one scene edit, through the live Editor — LobbyView gets its ContentBootstrap.
  - 'MimasClient/Assets/_Game/Scenes/Lobby.unity'
  # Spec §7.8: game-view captures land under Assets/_Shots (never under a Temp folder). The folder's own
  # .meta sits beside it rather than inside it, so it needs naming too (spec §0.6: declare what the spec
  # did not foresee before committing it).
  - 'MimasClient/Assets/_Shots/**'
  - 'MimasClient/Assets/_Shots.meta'
# Rungs by number, as the runner wants them (studio/game.yaml): 0 asset safety, 1 core build, 2 core
# tests, 5 server integration. The spec's §10 adds the Unity compile check, EditMode tests, the Web
# build and the browser smoke as commands; the verifier is step 2 of "done", not a rung.
ladder: [0, 1, 2, 5]
done_when:
  - "Two fake clients play a whole best-of-3 over sockets with a draft between rounds, on two maps in ladder order; every view and session block each receives is its own (spec §6.6)"
  - "A draft timeout, a reconnect into a draft, a forfeit from a draft, and a resign in a round and in a draft each end as spec §6.6 says; the bot room plays a best-of-3 and drafts"
  - "room.loadout needs a lineage (bad_loadout otherwise) and the lineage never appears in room.state"
  - "Core: SessionEndReason, SessionView.NextMapId and Create, draftPick and the five session events and the session view round-trip on the wire"
  - "Nine boons and storm-bolt load with their .meta and the manifest; every lineage has nine pool boons"
  - "Client: lineage row in the room, one match.start per round reloads the Arena, the draft overlay over the dimmed board, the series line, round cards, series banner and Back to room, examine boons and lineage, reveal flyovers and nameplate tag, resolved numbers on the action bar, practice mode runs a session, no _catalog.Abilities in MatchSession.cs (EditMode test)"
  - "unity command console clean; EditMode tests green; Web build under 13 MB; smoke green on three engines; screenshots of the overlay, the lineage row, the examine panel and a reveal in artifacts/"
  - "networking.md, data.md, architecture.md, ADR-036, the design page edits of spec §8.5, the runbook line and STATE are done; run report complete"
---

# Boons in the game (part 2 of F-boons)

The work order is the spec: `docs/specs/2026-09-22-boons-in-game.md`. Read its §0 first. It touches Core,
server, client, data and docs, each in a named and bounded way, in the ten commits of its §12. The eight
questions the outline left open were answered by Rohan on 22 Sep 2026 (spec §2, P1–P8); do not re-ask
them. Executing model: Opus, unattended (decided 21 Sep 2026).

Two things the part-1 build found, both written into the spec: the client's `MatchSession.cs` reads
`_catalog.Abilities.TryGet` in three places and every ability number must instead come from the mirror's
`ResolveAbility` (spec §7.5, §7.9), and the class is `Mimas.Core.Session.Session`, which the server aliases
with `using Session = Mimas.Core.Session.Session;` after its namespace line (spec §6.2).

Set this task to `running` before the first edit (the asset guard's active task is the single `running`
one), open the run report `studio/runs/R-<date>-T-0010.md` from the studio template, and note every
asset-guard false positive there. Flip to `verify` only after the last commit.
