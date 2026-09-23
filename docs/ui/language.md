# Mimas interface language

_Status: **built** for every token `Theme.uss` declares (T-0011, 23 Sep 2026); chosen by Rohan on 23 Sep 2026 in the examine-panel design session.
**Extended the same evening by the HUD session** (canvas page "HUD", rounds 1–3): **one surface, ink,
everywhere** — the paper surface and the serif are retired, the examine plate goes ink; new states for the
action bar; the void, the band, one new size. Rows marked `proposed` below are that session's and become
`built` with T-0013 / T-0014 (`docs/specs/2026-09-23-hud-restyle.md`). Source of the look:
`artifacts/UI Drafts/canvas_Titan Inspiration-260923_0720.png` and the Disco Elysium reference. Mock canvas:
https://claude.ai/artifact/K9qZcJQ115G5G685sdMAwy (page 2 "Examine", the "Final · the rules" sheet).
This page is the single place a colour, a face, a glyph or a spacing is defined. Every screen page under
`docs/ui/` refers to these names; the client declares them once as USS variables on `:root` and never
hard-codes a value._

## How to read the tables

- **Token** is the name in this book. **USS** is the variable the client declares. A screen page or a
  builder writes the token; the USS column is what the UXML/USS actually uses.
- **One surface: ink** (decided 23 Sep 2026, HUD round 3: Rohan, "we should go darker for consistency").
  The board HUD, the hover panel, the examine plate, the draft cards, the round band and the lobby are all
  ink. The paper values below are kept for the record and marked `retired`; no new screen uses them, and
  T-0013 moves the examine plate off them.
- Status per row: `proposed` until Rohan approves this page; then `built` when the variable exists in
  `MimasClient/Assets/_Game/UI/*.uss`, or `drift` with a note when the code does something else on purpose.

## 1. Colour

The rule: ink and bone, one signal colour per owner, four meaning colours, and nothing else carries colour
except the painting.

| Token | Meaning | Ink surface | Paper surface | USS | Status |
|---|---|---|---|---|---|
| `ink` | every plate, the void behind the HUD | `#0b0b0e` | — | `--mimas-ink` | built |
| `ink-2` | a raised plate (hover panel) | `#121217` | — | `--mimas-ink-2` | built |
| `paper` | ~~the examine plate~~ | — | `#e9e2d2` | `--mimas-paper` | **retired** 23 Sep 2026 (the plate is ink); the variable may stay until nothing reads it |
| `void` | the world behind the board: the Arena and Lobby cameras' background | `#0b0b0e` (= `ink`) | — | camera clear colour, not USS | built (Arena, T-0013); the Lobby's is its USS (T-0014) |
| `band` | the round card and results: a band across the middle, ink at 86% fading to nothing over the outer 22% each side, 300 tall | ink at 86% | — | `--mimas-band` | built (T-0013): `Ramps.BothEnds` tinted by the token |
| `fg` | all primary type | `#efe9dc` (bone) | `#16151a` | `--mimas-fg` | built |
| `fg-2` | secondary type, captions | bone at 74% | ink at 76% | `--mimas-fg-2` | built |
| `fg-3` | tertiary: labels, "/ max", unseen | bone at 50% | ink at 58% | `--mimas-fg-3` | built |
| `hair` | the only stroke: bars, tile edges | bone at 14% | ink at 16% | `--mimas-hair` | built |
| `tile` | the ground of an action tile | black at 32% | ink at 6% | `--mimas-tile` | built |
| `you` | your accent: bars, pips, section labels, tile edges (at 55%) | `#5fd3c8` | `#1f7f78` | `--mimas-you` | built |
| `them` | the enemy's accent, same uses | `#ff4b3e` | `#c8321f` | `--mimas-them` | built |
| `amount` | a damage number, anywhere | `#e9b45c` | same | `--mimas-amount` | built |
| `changed` | a number a boon changed; an action a boon added | `#a99cff` | `#5a48c8` | `--mimas-changed` | built |
| `up` | a stat above its base | `#8fdc7a` | `#2f7d3a` | `--mimas-up` | built |
| `down` | a stat below its base | `#ff6a5c` | `#c8321f` | `--mimas-down` | built |

Lineage hues are **painting colours, not UI colours**: they tint the portrait wash and the item squares
and never a bar or a label. Greek `#1d4f5c` / `#7fb7b0`, Norse `#3a3f52` / `#8e98b8`, Hindu `#6a3a12` /
`#e0a35a` (dark, light). Data: `lineages/*.json` gets two presentation keys `hueDark`, `hueLight`
(**data gap**, see §7).

Never pure white. Never a grey-on-grey card. Never a colour that means two things.

## 2. Type

Two faces on ink, three on paper. Nothing else. All numbers that carry weight are set light.

| Token | Face, weight | Where | Size (px) | USS | Status |
|---|---|---|---|---|---|
| `display` | Josefin Sans 300 | every large number (health, AP, stat base) | 34 / 24 | `--mimas-font-display` | built: 300 (unmodified file: the font has a Reserved Font Name) |
| `display-caps` | Josefin Sans 400, letter-spacing 0.14–0.28em, uppercase | names and captions on ink; tile labels everywhere | 26 / 13.5 / 9 / 7.5 / 6.5 | `--mimas-font-caps` | built: 400 |
| `serif` | ~~Cormorant Garamond 700 / 600 / italic~~ | ~~names, item names, section labels, the lineage line, on paper only~~ | — | `--mimas-font-serif`, `-serif-semibold`, `-serif-italic` | **retired** 23 Sep 2026 with the paper surface; T-0013 removes the three font assets from the build |
| `body` | Sora 400 / 600 | every sentence: descriptions, hover text, notes | 12 / 11.5 / 10.5 / 10 | `--mimas-font-body` | built: 400 only, Latin subset (600 not needed yet; italic is synthesised for the flavour line) |

Scale, top to bottom: **56 · 38 · 36 · 28 · 20 · 19 · 17 · 15 · 14 · 13 · 12 · 11 · 10** (USS `--mimas-text-<size>`).
A new size is a decision, not a tweak. **56 was added on 23 Sep 2026 for the room code only** (HUD round 3,
`docs/ui/lobby.md`); nothing else uses it. Raised on 23 Sep 2026 after Rohan played the plate: the HUD scales
with the window, so 1280×720 draws every size at two thirds, and the old 6.5 and 7.5 became unreadable.
The sizes in the rows below are the 23 Sep originals; each moved up one step of this scale
(34→38, 32→36, 24→28, 17→20, 16→19, 15→17, 13.5→15, 12→14, 11.5→13, 10.5 and 10→12, 7.5→11, 6.5→10).

**Letter-spacing is written in hundredths of an em** (drift, found by T-0013 on 23 Sep 2026): UI Toolkit's text
generator in 6000.4 reads `letter-spacing` as em × 100 whatever its unit says — a 15px label with
`letter-spacing: 100px` gains 15px per gap. So `--mimas-caps-tracking: 8px` is 0.08em, `-wide: 14px` 0.14em, and
the HUD's `--mimas-tracking-11/-15/-20/-38` are 0.28 / 0.24 / 0.28 / 0.30em. Until T-0013 they were written as
pixels (1px, 2px) and drew as almost nothing, on the plate too. The fonts' kerning pairs also carried TextCore's
"ignore spacing adjustments" flag (1422 of 1439 in Josefin Sans Light), which dropped the tracking on pairs such
as T-O and Y-O ("V I C TORY"); `UI/FontSpacing` clears it in memory when the HUD binds.

Licence: all three are SIL OFL on Google Fonts. **Asset gap**: they must ship as Unity font assets under
`MimasClient/Assets/_Game/UI/Fonts/` (UI Toolkit uses TextCore font assets; generate with the Editor, never
hand-make the `.asset`). Fallback stack in USS: `"Josefin Sans", "Sora", sans-serif`.

## 3. Glyphs

Shapes carry meaning so it survives any colour, the art, and colour-blindness.

| Token | Shape | Means | Source | Status |
|---|---|---|---|---|
| `glyph.health` | heart, stroke | health | `Icons/heart` | built (`Glyphs.cs`) |
| `glyph.ap` | bolt, stroke | action points | `Icons/bolt` | built (`Glyphs.cs`) |
| `glyph.egg` | egg, hollow / filled | one AP, spent / held | drawn in USS (border-radius), no asset | drift: drawn by `Glyphs.cs` (Painter2D), not USS |
| `glyph.cost` | 5px dot ×n, 3 apart, centred 5px above the tile's bottom edge | an action's AP cost, on its action-bar tile (never a digit; HUD round 1) | drawn in USS | built (T-0013) |
| `glyph.unseen-by-them` | closed eye: a lid arc with three short lashes, 11px, at the tile's top-left | an action of **yours** the opponent has not seen yet (HUD round 1). Not the struck eye: that is `glyph.nosight` | `Glyphs.cs` path `eye-closed` | built (T-0013): on the bar's tiles; the plate's own tiles do not draw it, their hover panel says it |
| `glyph.blessing` | circle | Blessing (filled when it is the starting one) | `Icons/kind-blessing` | built (`Glyphs.cs`) |
| `glyph.enchant` | diamond | Enchant | `Icons/kind-enchant` | built (`Glyphs.cs`) |
| `glyph.sigil` | triangle | Sigil | `Icons/kind-sigil` | built (`Glyphs.cs`) |
| `glyph.weapon` | sword | weapon lane, weapon slot | `Icons/lane-weapon` | built (`Glyphs.cs`) |
| `glyph.spell` | spark | spell lane | `Icons/lane-spell` | built (`Glyphs.cs`) |
| `glyph.crown` `glyph.boots` `glyph.armour` | crown, boot, shield | the other three slots (fallback under item art) | `Icons/slot-*` | built (`Glyphs.cs`) |
| `glyph.lobbed` `glyph.straight` | arc, arrow | trajectory | `Icons/traj-*` | built (`Glyphs.cs`) |
| `glyph.sight` `glyph.nosight` | eye, eye struck | needs sight / no sight needed | `Icons/sight-*` | built (`Glyphs.cs`) |
| `glyph.unknown` | dashed circle with `?` | a thing of theirs you have not seen | drawn in USS | drift: drawn by `Glyphs.cs` (Painter2D), not USS |
| `glyph.emblem.<lineage>` | laurel, hammer, lotus (placeholders) | the lineage | `lineages/*.json` `icon` key, already in data | built (`Glyphs.cs`) |
| action icons | one per ability | the action, identical on the bar, in examine and in the hover | `abilities/*.json` `icon` key, already in data | built (bar) |

All stroke icons: 24-unit grid, 1.4–1.8 stroke, round caps. **Asset gap**: the Icons folder does not exist;
today the action bar draws a letter. Vector (SVG import) preferred so one file serves 12px and 22px.

## 4. Space and shape

| Token | Value | Used for | USS | Status |
|---|---|---|---|---|
| `plate.width` | 420 (was 380; widened with the type) | the examine plate; the hover panel is 340 | `--mimas-plate-w`, `--mimas-panel-w` | built |
| `plate.pad` | 22 top/bottom, 20 sides | plate padding | `--mimas-plate-pad` | built |
| `gap.section` | 18 | between sections; a section is a caption plus this air, **no line** | `--mimas-gap-section` | built |
| `gap.item` | 12 | between items in equipment | `--mimas-gap-item` | built |
| `gap.row` | 4–5 | between rows in a list | `--mimas-gap-row` | built |
| `tile` | 44 square (was 40), 1px edge, label beneath | an action tile | `--mimas-tile-size` | built |
| `bar` | 2px tall | health bar; the fill is the owner's colour | `--mimas-bar` | built |
| `radius` | 0 | nothing is rounded except eggs, dots and the `?` | — | built |
| `shadow.panel` | `0 14px 34px` black at 60% | the hover panel, the draft cards | `--mimas-shadow-panel` | drift: USS has no `box-shadow`; an offset layer in this colour stands in |
| `button` | 230 × 58, 1px edge, caps 15 at 0.28em | End Turn, Confirm, Back to room, Ready (Ready is 320 wide) | `--mimas-button-w`, `--mimas-button-h` | built (T-0013; Ready with T-0014). The edge at 55% is `--mimas-you-55` |
| `track` | 620 wide, a 2px line, 14px marker | the turn track (`docs/ui/hud.md`) | `--mimas-track-w` | built (T-0013) |
| `card` | 360 × 470; the selected one lifts 14px | a draft card (`docs/ui/between-rounds.md`) | `--mimas-card-w`, `--mimas-card-h` | built (T-0013) |
| `edge` | 40 | the HUD's distance from the window edges (bar, End Turn, boons column) | `--mimas-edge` | built (T-0013) |

No hairline dividers, no boxed sections, no borders around groups. If two things need separating, use
space or a caption. The one edge allowed is the hover panel's 2px left edge in the owner's colour.

## 5. States

| Token | Looks like | Means |
|---|---|---|
| `state.changed` | tile edge in `changed`, a small diamond at the corner | an Enchant changed a number of this action |
| `state.added` | tile edge in `changed`, a small triangle at the corner | a Sigil granted this action |
| `state.unseen` | dashed tile with `?` and the label "unseen" | an action of theirs you have not seen |
| `state.unrevealed` | dashed `?` row | a boon they hold that you have not seen |
| `state.hidden-stat` | grey `?` after a stat | an unrevealed boon may be moving this stat |
| `state.hover` | row ground at fg 4.5%; the hover panel opens to the left | the pointer rests on a thing |
| `state.selected` | a 1px ring in the owner's colour around the unit on the board | the unit examine is open for |
| `state.ready` | tile ground `tile`, 1px `hair` edge, icon in `fg`, cost dots in `you` | an action you can take now |
| `state.armed` | 2px edge in `you`, ground `you` at 10% | the action waiting for a target |
| `state.spends` | the eggs the hovered or armed action would spend turn hollow-bright (`you` edge, `you` at 28%), from the right of the held ones | what this costs, before you commit (Divinity: Original Sin 2) |
| `state.unaffordable` | the whole tile at 36%, cost dots in `fg-3`; the tile stays on the bar | not enough AP. Never removed from the bar |
| `state.unseen-by-them` | `glyph.unseen-by-them` at the tile's top-left in `fg-3` | an action of yours they have not seen; it goes the first time you use it (HUD round 1, Rohan) |
| `state.their-turn` | the whole bar at 45%; End Turn reads "Their turn" in `fg-3` | not yours to act |
| `state.done` | End Turn with a 1px `you` edge, ground `you` at 18%, label in `you` | nothing left to spend: one click ends it (Hearthstone) |

There is no "seen by your opponent" **text** anywhere. On your own action tiles, what they have not seen
is a mark (`state.unseen-by-them`), never a sentence (23 Sep 2026: this replaces "the mirror of `unseen` on
your own side is not shown"). The hover panel of such a tile says it in one line: "They have not seen this
yet."

## 6. The hover panel (one object, everywhere)

Anything that can be rested on opens the same panel, 340 wide, on `ink-2`, with a 2px left edge in the
owner's colour (grey `fg-3` for an unknown). **Where it opens** (HUD round 1): to the left of a thing on the
right edge (the examine plate, the boons column); above a thing on the bottom edge (an action-bar tile),
its left edge 8px left of the tile's; and, as the **attack preview**, over the target's tag while an attack
is armed (`docs/ui/hud.md` §5). Order inside, top to bottom:

1. Icon tile (42, 1px edge in the accent) beside the **name** in `display-caps` 17 and a **type line** in
   caps 8: what it is · what it belongs to · action / movement / Blessing / Enchant / Sigil / stat / item.
2. **Numbers as tiles**: glyph over value over label, on a 4% ground: cost · damage (in `amount`) · reach
   (in `changed` when a boon changed it) · apex · element.
3. **One sentence** (`body` 12).
4. **The damage line**, as the rules compute it: `3 base + 3 Strength − their armour` (only for attacks).
5. **Changes** from boons, one per line, in `changed`, with a diamond.
6. **Conditions** in `fg-3` 10: lobbed and clears cover / straight line · needs sight / no sight needed · one target.
7. One italic line of flavour (`body` 10.5, `fg-3`), never more. Anti-pillar: no lore beyond one line.

The same panel serves a boon (kind glyph instead of icon, flavour = "Athena answers those who pray to the
Greek gods."), an item (slot glyph, its stats as a tile, the boons on it under Changes), a stat (the
breakdown as rows: base, each item, each boon), and the unknown (grey edge, one sentence about when it
reveals). Nothing in the list repeats what the panel says: **names in the list, numbers in the panel**.

### Tokens T-0013 added (built)

`you` at 10 / 14 / 18 / 28 / 55% (`--mimas-you-10` … `-55`: armed, primary buttons, done, spends, the button
edge); ink at 50 / 55 / 82% (`--mimas-ink-50` a tag's bar ground, `-55` End Turn and the boon circles, `-82` the
cursor tag); `--mimas-scrim` (the draft's 80%), `--mimas-floor` / `--mimas-floor-top` (the floors' 86% and 62%,
tints for `Ramps.Vertical`); the 56 size (`--mimas-text-56`, the room code's, used by T-0014); the tracking
tokens above.

## 7. Data and asset gaps this page creates

| Gap | Kind | Where it lands |
|---|---|---|
| `hueDark`, `hueLight` per lineage | data | `lineages/*.json`, `docs/data.md`, `tools/schemas/lineage.schema.json` |
| Font assets for Josefin Sans, Cormorant Garamond, Sora | asset | `MimasClient/Assets/_Game/UI/Fonts/` via the Editor |
| Stroke icon set (§3) | asset | `MimasClient/Assets/_Game/UI/Icons/` |
| USS variables (`--mimas-*`) replacing today's `--hud-*` in `MatchHud.uss` / `Lobby.uss` | code | one commit, no visual change until a screen adopts them |

## Change log

- 2026-09-23 · written from the examine session; status proposed.
- 2026-09-23 · built by T-0011: every §1, §2, §4 token in `MimasClient/Assets/_Game/UI/Theme.uss`; glyphs drawn by `Glyphs.cs`; fonts as dynamic TextCore assets under `UI/Fonts/`. Two drifts noted in the rows: no USS box-shadow, and the egg and unknown glyphs drawn in code rather than USS.
- 2026-09-23 · readability pass after Rohan played it: the type scale raised one step throughout, `fg-2` / `fg-3` stronger on both surfaces, plate 420 and panel 340 wide, tiles 44.
- 2026-09-23 · built by T-0013: the HUD session's rows (void, band, cost dots, closed eye, button, track, card,
  edge) and the new alpha and tracking tokens; the paper block and the serif faces gone from `Theme.uss` and the
  three Cormorant assets from the build; the letter-spacing unit and the kerning flag found and fixed (§2).
- 2026-09-23 · HUD session (canvas page "HUD", rounds 1–3, Rohan): one surface, ink — paper and the serif retired; `void`, `band`, the 56 size for the room code; `glyph.cost` placed, `glyph.unseen-by-them` (closed eye, because the struck eye already means "no sight needed"); the action-bar states; where the hover panel opens; button, track, card and edge sizes. Screens: `docs/ui/hud.md`, `docs/ui/between-rounds.md`, `docs/ui/lobby.md`.
