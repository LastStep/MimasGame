---
id: T-0011
title: The examine panel — the manuscript plate, the hover panel, and the first mimas tokens
project: mimas
feature: F-examine-panel
milestone: M4
lane: full
status: running
owner: builder
model: opus
worktree:
depends_on: []
plan: docs/specs/2026-09-23-examine-panel.md
allows_assets:
  # Spec §8: the design page's examine section points at the UI book, through the declared allowance.
  - 'docs/design/index.html'
  # Spec §9: new Core tests, and DataTests gains the lineage-hue read (new tests only).
  - 'shared/Mimas.Core.Tests/StatLinesTests.cs'
  - 'shared/Mimas.Core.Tests/DataTests.cs'
  # Spec §5: two presentation keys per lineage, with the .meta the Editor keeps and the manifest it regenerates.
  - 'MimasClient/Assets/_Game/Data/lineages/**'
  - 'MimasClient/Assets/_Game/Content/GameDataManifest.asset'
  # Spec §7.9: the fonts and the font assets the Editor makes from them, with their .meta files.
  - 'MimasClient/Assets/_Game/UI/Fonts/**'
  - 'MimasClient/Assets/_Game/UI/Fonts.meta'
  # Spec §7.1: new client files get a .meta from the Editor (never hand-made).
  - 'MimasClient/Assets/_Game/UI/Theme.uss.meta'
  - 'MimasClient/Assets/_Game/UI/Examine.uxml.meta'
  - 'MimasClient/Assets/_Game/UI/Examine.uss.meta'
  - 'MimasClient/Assets/_Game/UI/HoverPanel.uxml.meta'
  - 'MimasClient/Assets/_Game/UI/HoverPanel.uss.meta'
  - 'MimasClient/Assets/_Game/UI/ExamineView.cs.meta'
  - 'MimasClient/Assets/_Game/UI/Glyphs.cs.meta'
  - 'MimasClient/Assets/_Game/Presentation/Match/ExamineModelBuilder.cs.meta'
  - 'MimasClient/Assets/_Game/Tests/EditMode/ExamineModelTests.cs.meta'
  - 'MimasClient/Assets/_Game/Editor/Fonts.cs.meta'
  # Spec §11: captures land under Assets/_Shots (never a Temp folder); the folder .meta already exists.
  - 'MimasClient/Assets/_Shots/**'
# Rungs by number (studio/game.yaml): 0 asset safety, 1 core build, 2 core tests, 5 server integration.
# The spec's §10 adds the Unity compile check, EditMode tests, the Web build and the browser smoke as
# commands; the verifier is step 2 of "done", not a rung.
ladder: [0, 1, 2, 5]
done_when:
  - "Unit.StatLines lists base, items in slot order, boons in grant order, sums to Stats, and on the mirror never names a hidden boon (spec §4, §9)"
  - "The examine plate is the manuscript of docs/ui/examine.md: every §3 id is a UXML name, no literal colour or font outside Theme.uss, no hairline or bordered section, no boon name under an item, boons oldest to latest, no seen-on text (spec §7, §14)"
  - "Actions are icon tiles with names only; changed and added tiles carry the violet edge and mark; unseen enemy actions are dashed ?; the one hover panel opens to the left with the numbers, the sentence, the damage line, the changes and the conditions (spec §7.7, §7.10)"
  - "Stats show base then net in green or red, ? on the enemy's lane stats while a boon is unrevealed and never on health or AP; standing height sits in the vitals (spec §7.4, §7.5)"
  - "A click anywhere off the plate closes it and selects nothing; Escape and ✕ close it; it stays open across a turn; End Turn still works with it open (spec §7.11, §13)"
  - "Screenshots of the five states of spec §14, at 1920×1080 and 1280×720, in artifacts/ and named in the run report, against docs/ui/mockups/examine-manuscript-r3.png"
  - "Web build ≤ 13 MB with fonts; smoke green on three engines; console clean; EditMode and Core tests up by the spec's counts, none softened"
  - "Docs: design page #examine points at the UI book; docs/ui statuses flipped; architecture, data, ADR-037, STATE, run report"
created: 2026-09-23
started: 2026-09-23
finished:
cost_usd: 0
blocked_by:
---

# The examine panel

## Context

Six rounds of options on 23 Sep 2026 (canvas https://claude.ai/artifact/K9qZcJQ115G5G685sdMAwy, page 2)
ended with Rohan choosing the manuscript plate at its round-3 state. The design is written as the first two
pages of the UI book, `docs/ui/language.md` and `docs/ui/examine.md`, with the render pinned at
`docs/ui/mockups/examine-manuscript-r3.png`. The spec turns those pages into a work order: one read-only
Core query, two data keys, a token stylesheet, two UI Toolkit templates, a rebuilt presenter model, drawn
glyphs, three fonts, tests and screenshots.

## Not in scope

The action bar, draft cards, round banner and lobby in the new language; art; status effects; anything the
opponent may see.
