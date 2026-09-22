---
id: T-0010
title: Boons in the game — the online best-of-3 with the draft in the room, and the presentation of boons and reveals
project: mimas
feature: F-boons
milestone: M3
lane: full
status: draft
owner: builder
model: opus
worktree:
depends_on: [T-0009]
plan: docs/specs/2026-09-21-boons-in-game-outline.md
allows_assets: []
ladder:
  - "dotnet build Mimas.slnx"
  - "dotnet test shared/Mimas.Core.Tests"
  - "dotnet test server/Mimas.Server.Tests"
  - "unity command console clean; EditMode tests"
  - "browser smoke: a two-browser best-of-3 with a draft over the live server"
  - "verifier in a fresh context"
done_when:
  - "Written into the full spec after T-0009 lands (the outline's §5 questions answered by Rohan first)"
---

# Boons in the game (part 2 of F-boons)

**Not ready to build.** The plan file is an outline. Two things the T-0009 build found for the spec
session (22 Sep 2026): the client's `MatchSession.cs` still reads `_catalog.Abilities.TryGet` in three
places, so button costs and range bands must come from the mirror's `ResolveAbility` or an Enchant will
show the base number; and the class is `Mimas.Core.Session.Session`, which the server aliases
(`using Session = Mimas.Core.Session.Session;` inside its namespace) or names in full. The sequence Rohan chose on 21 Sep 2026: T-0009 is
built by Fable; then Fable writes the full part-2 spec against the real Core at that commit, asking the
outline's §5 questions with 3–4 options each; then this task moves to `approved` and Opus executes it
unattended, in the shape of `docs/specs/2026-09-17-online-slice.md`.
