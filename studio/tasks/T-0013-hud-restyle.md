---
id: T-0013
title: The match HUD in ink — the bar, the turn track, the preview, the draft, the moments, the ink examine plate, and what the opponent has seen of you
project: mimas
feature: F-hud-restyle
milestone: M4
lane: full
status: verify
owner: builder
model: opus
worktree: main
depends_on: []
plan: docs/specs/2026-09-23-hud-restyle.md
allows_assets:
  # Spec §10: the design page's #hud, #draft, #examine and #hidden-info point at the UI book, in the first commit.
  - 'docs/design/index.html'
  # Spec §9: the Core tests of the seen-by-opponent flag, one new file (no existing test file is edited).
  - 'shared/Mimas.Core.Tests/SeenByOpponentTests.cs'
  # Spec §7.9: the three Cormorant fonts, their SDF assets and licence leave the build, deleted through the Editor.
  - 'MimasClient/Assets/_Game/UI/Fonts/**'
  - '**/_Game/UI/Fonts/**'
  # Spec §7.10: the Arena camera clears to ink, changed through the Editor (both spellings: STATE).
  - 'MimasClient/Assets/_Game/Scenes/Arena.unity'
  - '**/_Game/Scenes/Arena.unity'
  # Spec §7.1: new client files get a .meta from the Editor (never hand-made).
  - 'MimasClient/Assets/_Game/UI/HoverPanelView.cs.meta'
  - 'MimasClient/Assets/_Game/UI/Ramps.cs.meta'
  - 'MimasClient/Assets/_Game/Presentation/Match/HudModel.cs.meta'
  - 'MimasClient/Assets/_Game/Editor/HudPreview.cs.meta'
  - 'MimasClient/Assets/_Game/Tests/EditMode/HudModelTests.cs.meta'
  # Added during the build (spec rule 7): the runtime repair that lets letter-spacing apply to kerned pairs.
  - 'MimasClient/Assets/_Game/UI/FontSpacing.cs.meta'
  # Spec §11: captures land under Assets/_Shots (never a Temp folder); the folder .meta already exists.
  - 'MimasClient/Assets/_Shots/**'
# Rungs by number (studio/game.yaml): 0 asset safety, 1 core build, 2 core tests, 5 server integration.
# The spec's §11 adds the Unity compile check, EditMode tests, the Web build and the browser smoke as
# commands; the verifier is step 2 of "done", not a rung.
ladder: [0, 1, 2, 5]
done_when:
  - "The owner's PlayerView flags, on its own abilities, modifiers, boons and lineage only, whether the opponent has seen them; the wire carries `seen`/`lineageSeen` for the own unit and nothing new for the enemy; the mirror reproduces the view; 12 Core and 2 server tests prove it (spec §4, §5, §9)"
  - "The HUD is docs/ui/hud.md layout A: every §3 id is a UXML name; the turn track with pips, marker and a half that burns towards the centre; AP eggs; three captioned lane rows of 44 tiles with cost dots, violet edge + diamond/triangle, and the closed eye on actions the opponent has not seen; boons down the right edge; Resign above End Turn, End Turn lit when nothing is affordable (spec §7.4, §7.5)"
  - "Resting on a tile opens the one hover panel above it with the examine plate's tile content; an armed attack opens it over the target with the total in amber, the rules' lines, one `?` row, and the reason first when refused (spec §7.4, §7.7)"
  - "The draft is ink cards with the lineage wash, HAS CHOSEN at their end of the track; the round card, round result, series result and match lost sit in the band, coloured by who won, from a HudMoment model (spec §7.8)"
  - "The examine plate is ink with capitals; no Cormorant file remains in UI/Fonts; Theme.uss has no serif face and no paper token that anything reads; the Arena clears to #0b0b0e (spec §7.9, §7.10)"
  - "MatchHud.uss contains no --hud-* variable and no literal colour; nothing under 10px at 1920×1080"
  - "Captures at 1920×1080 and 1280×720 in artifacts/t0013/, named in the run report: docs/ui/hud.md §8 items 1–6, docs/ui/between-rounds.md §5 items 1–5, the ink plate for both heroes (spec §14)"
  - "Web build ≤ 13 MB with the size before and after; smoke green on three engines; console clean; EditMode and Core tests up by the spec's counts, none softened"
  - "Docs: design #hud, #draft, #examine, #hidden-info; the UI book statuses; architecture; networking; STATE; run report"
created: 2026-09-23
started: 2026-09-23
finished: 2026-09-24
cost_usd: 0
blocked_by:
---

# The match HUD in ink

## Context

Rohan chose this over three rounds on the canvas on 23 Sep 2026 (https://claude.ai/artifact/K9qZcJQ115G5G685sdMAwy,
page "HUD"). The design is the UI book — `docs/ui/hud.md`, `between-rounds.md`, the amended `language.md` and
`examine.md` — and the work order is `docs/specs/2026-09-23-hud-restyle.md` (T-0013 is everything except §8).
ADR-039 and ADR-040 say why the one wire field and the shared models.

## Scope

Core: one flag on the owner's view entries, its wire keys and its mirror import, tested. Client: the HUD
contract, the shared hover panel and ramps, the bar, the track, End Turn, the tags, the preview, the
flyovers, the draft, the moments, the ink examine plate, the Arena's ink void, the HUD preview menu items,
EditMode tests, captures.

## Out of scope

The lobby and the room (T-0014); action icon art; status effects; any rule; a second wire field; the full
character select; remembering the preset.

## Notes
