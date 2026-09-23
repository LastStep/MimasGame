# Examine · the character panel

_Status: **built** (T-0011, 23 Sep 2026; chosen by Rohan on 23 Sep 2026: the manuscript plate at its round-3 state). Design
anchors: `#examine`, `#hidden-info`, `#stats`, `#equipment`, `#boons`, `#abilities`, `#presentation`. Language:
`docs/ui/language.md`. Mock: https://claude.ai/artifact/K9qZcJQ115G5G685sdMAwy, page 2 "Examine", top row:
"Manuscript · round 3 · chosen", with round 1, the enemy, and "Final · manuscript in context" beside it.
Pinned render: `docs/ui/mockups/examine-manuscript-r3.png` (both plates with the rules; the right-hand paper
one is the chosen one). Work order: `docs/specs/2026-09-23-examine-panel.md`, task `T-0011`. Today's panel:
`MatchHud.uxml` `#examine` and `MatchSession.AddBoonEntries`, which this replaces._

## 1. What it is for

One plate, on the right, that answers "what am I fighting" and "what have I become" without leaving the
board. Pillar 5: depth on demand. Pillar 3: what the opponent has is hidden until they use it, and the
plate shows exactly the edge of what you know. It never shows a number the rules would not let you see.

## 2. Behaviour

| Rule | Detail | Source of truth |
|---|---|---|
| Opens | on a click on any unit with nothing armed (design `#hud`: "clicking a unit with nothing armed opens examine") | `MatchSession` selection |
| Stays | open across turns and events; it re-renders from every new `PlayerView` | ADR-026 mirror |
| Closes | on a click anywhere off the plate, on ✕, on Escape, or when its unit dies. The off-plate click **only closes**; it selects and arms nothing (Rohan, 23 Sep 2026) | `ex.scrim` swallows the click |
| Overflow | the plate is full window height; the painting is 210 tall at ≥ 900px windows, 120 below; the body scrolls with a 2px scroller. At 1920×1080 the round-3 state fits; at 1280×720 it scrolls | spec §3 |
| Hover | every row and tile opens the language's hover panel to the left, 120 ms after the pointer rests, closes on leave | `docs/ui/language.md` §6 |
| Position | flush to the right edge, full window height, 380 wide; a 200px dark wash bleeds onto the board so it reads as a layer, not a window | `language.md` §4 |
| Owner colour | `you` when the unit is yours, `them` when not; the plate is paper either way, the wash and panels are ink | `UnitView.IsMine` |

## 3. Element inventory

Each `id` becomes the UXML element `name`. A builder creates exactly these; a verifier greps for them.
"Data" is where the value comes from on the client: `view` = `Mimas.Core.Match.PlayerView.UnitView`,
`mirror` = the client `MatchState` rebuilt from the view (ADR-026), `catalog` = `ContentCatalog`,
`client` = presentation-only state that never reaches Core.

### 3.1 Top

| id | Shows | Data | Asset | States | Hover panel |
|---|---|---|---|---|---|
| `ex.portrait` | the painting: portrait wash in the lineage hue, emblem huge and faint behind, fading into paper over 210px (drift: the emblem is drawn in the light hue at 50%, as in the pinned render, not at 6%) | `view.LineageId` → `catalog.GetLineage(...).hueDark/hueLight` (**gap**), `.icon` | portrait art per hero/gear (**gap**, placeholder = wash + emblem) | enemy lineage unknown → neutral grey wash, no emblem | none |
| `ex.name` | player name, `serif` 32 | room seat name (`client`, from the session block) | — | — | none |
| `ex.lineage` | "Greek · your hero" / "Norse · the enemy", `serif` italic 15 | `view.LineageId`, `view.IsMine`; `null` → "Unknown lineage" | — | — | none |
| `ex.close` | ✕ | — | `glyph.close` | — | none |
| `ex.vitals.hp` | heart · **17** / 24 · bar | `view.Hp`, `view.MaxHp` | `glyph.health` | bar fill in owner colour | stat breakdown (§4) |
| `ex.vitals.ap` | bolt · **1** / 4 · eggs | `view.Ap`, `view.ApPerTurn` | `glyph.ap`, `glyph.egg` | eggs hollow when spent | stat breakdown |
| `ex.vitals.height` | arc glyph · **1** · caption "height": the level the unit stands on (it changes damage, `#damage` high ground) | `mirror` `Rules.Map[view.Position].Height` | `glyph.lobbed` | — | none |

### 3.2 Stats (caption "STATS", then a 3×2 grid)

Every cell is **base then net**: base = rules base + items (public), net = what boons changed, `up` green,
`down` red, nothing when zero, grey `?` when the unit is theirs and `view.UnrevealedBoonCount > 0`.

| id | Shows | Data | Asset | Hover panel |
|---|---|---|---|---|
| `ex.stat.hp` | 28 −4 | base = `Unit.PublicStats.Hp`, net = `Unit.Stats.Hp − PublicStats.Hp` on the mirror's unit; the hover rows need §5 gap 1 | `glyph.health` | rows: base 20 · Leather Jerkin +8 · Hera's Resolve −4 |
| `ex.stat.ap` | 3 +1 | breakdown for `ap` | `glyph.ap` | rows |
| `ex.stat.strength` | 3 | breakdown for `power.weapon` | `glyph.weapon` | rows |
| `ex.stat.magic` | 4 | breakdown for `power.spell` | `glyph.spell` | rows |
| `ex.stat.armour-weapon` | 2 +1 | breakdown for `defense.weapon` | `glyph.armour` | rows |
| `ex.stat.armour-spell` | 2 +1 | breakdown for `defense.spell` | `glyph.armour` | rows |

An hp or AP Blessing is public from round start (design `#stats` rule), so those two never show `?`.

### 3.3 Boons (caption "BOONS", a list)

| id | Shows | Data | Asset | States | Hover panel |
|---|---|---|---|---|---|
| `ex.boon[i]` | kind glyph · name · "Longbow" when it sits on an item (built as `ex.boon[0]`, `ex.boon[1]`, …) | `view.Boons[i]` (`KnownEntry`, grant order) → `catalog.GetBoon(id)` for name, kind, `requires.slot` → the item it landed on | `glyph.blessing/enchant/sigil` | `Revealed == false` → `state.unrevealed`, always last | boon panel: kind · lineage · on what; one sentence; flavour "God answers those who pray…" |

Order is **oldest to latest**: the starting Blessing, then each draft. `view.Boons` is already in grant
order; for the enemy, revealed entries are shown in the order they were revealed (**gap**, §5) and the
unrevealed count last as `?` rows.

### 3.4 Equipment (caption "EQUIPMENT", four groups, 12px apart)

| id | Shows | Data | Asset | States | Hover panel |
|---|---|---|---|---|---|
| `ex.item[slot]` | item square 28 · name (`serif` 17) · its stat line in `up` ("+2 Strength") | `view.ItemIds` → `catalog.GetItemForSlot(slot, id)`: name, `Stats` | item art square (**gap**, placeholder = lineage-hued square), slot glyph fallback | — | item panel: slot · kind; description; its stats as a tile; "Nike's Jab on this item" under Changes; for theirs "You have seen 1 of its 2 abilities" |
| `ex.item[slot].tile[j]` | 40px tile with the action icon, name in caps 6.5 beneath, **no numbers** (built as `ex.item[weapon].tile[0]`; the icon key rides on an `icon--<key>` class) | `view.Abilities` filtered by `SourceItemId == item` (plus innate Walk under boots) → `catalog.GetAttack(id)` / `GetMovement(id)`: name, icon | action icon (**gap**: today a letter) | `state.changed` when `BoonOverlay.OverridesFor(id)` touches cost/range/minRange/damage (the bar already computes this: `MatchSession.IsChangedByABoon`); `state.added` when `Unit.BoonOfAbility(id) != null` (a Sigil grant); `state.unseen` when `KnownEntry.Revealed == false` | attack / movement panel (§4) |

The boon that changed or added an action is **not named under the item**; the tile edge is the whole
tell. Actions are ordered as the item gained them: the item's own abilities first, then Sigil grants.
Armour, which has no abilities, shows one grey line: "turns 2 of every blow".

### 3.5 The board while the plate is open

| id | Shows | Data |
|---|---|---|
| `ex.ring` | 1px ring in the owner colour around the examined unit | `client` selection (drawn around the unit's nameplate, not on the board mesh) |
| `ex.scrim` | invisible full-window catcher behind the plate that closes it | `client` |
| `ex.wash` | 200px gradient to ink at 45% on the board's edge next to the plate | none |

## 4. Hover panel contents, per thing (the language §6 shape)

| Thing | Type line | Number tiles | Sentence | Damage line | Changes | Conditions |
|---|---|---|---|---|---|---|
| attack | `weapon attack · Longbow · action` or `spell · Ember Circlet · action` | cost · damage · reach · apex? · element? | `abilities/*.json` description | `<damage> base + <Strength or Magic> − their armour` (from the mirror's damage lines, ADR-017, with hidden enemy lines as `?`) | `reach 5 → 6, Apollo's Bowstring` / `granted by Nike's Jab` | trajectory · sight · one target |
| movement | `movement · innate` / `movement · Leaping Boots` | cost · hexes / per AP | description | — | — | `2 hexes · line · clears 1` |
| unknown action | `ability · Flintlock` | — | "One more ability on this item. You will see it the first time it is used." | — | — | — |
| boon | `Blessing · Greek · on you` / `Enchant · Greek · on Longbow` | — | boon description with values coloured `up`/`down`/`amount` | — | — | `starting Blessing` / `drafted · round 2` |
| unknown boon | `drafted · not yet revealed` | — | the reveal rule in one sentence | — | — | — |
| item | `weapon · bow · lobbed · no sight needed` | kind · its stats | item description (+ "You have seen k of n abilities" for theirs) | — | boons on it | — |
| stat | `stat · base, then every change` | — | rows: base · each item · each boon (`?` row for hidden) | — | — | — |

## 5. Data gaps (what the code must add before this can be built)

| # | Gap | Proposed shape | Where |
|---|---|---|---|
| 1 | **Stat breakdown rows for the hover panel.** The two numbers on the plate need nothing new: base is `Unit.PublicStats` (rules base + items, public by design) and net is `Unit.Stats − Unit.PublicStats`, both on the mirror's unit. The hover panel's rows (base · each item · each known boon) need a lines query | `MatchState.StatLinesKnownTo(unitId, statKey, viewer)` returning ordered `(sourceId, amount)` lines the way damage lines already are (ADR-017); hidden Blessing lines omitted, their existence surfacing only through `UnrevealedBoonCount` | `shared/Mimas.Core` + a Core test |
| 2 | **"Added by a Sigil" per ability**: already there. `Unit.BoonOfAbility(abilityId)` returns the granting boon (from `BoonOverlay.Grants`, ADR-034); `BoonOverlay.OverridesFor(abilityId, …)` is what the bar already uses for `changed` | no change; the client reads both from the mirror's unit | — |
| 3 | **Enemy boon reveal order** | `KnownEntry` for boons gains a `RevealedAt` turn (server knows it; ADR-018 reveal sets are per viewer) or the client remembers the order it saw reveals in (client-only, lost on reload) | Core or client; proposed Core |
| 4 | **Lineage hues** | `hueDark`, `hueLight` in `lineages/*.json` | data + `docs/data.md` + schema |
| 5 | **Player name on the plate** | the session block already carries seat names online; the practice mode needs "You" / "Random Bot" | client |
| 6 | Standing height in the top section (**open question 2**) | `view.Position` → map hex height; shown as a small "height 1" cell if Rohan wants it | client, no gap |

Nothing here changes a rule. Everything the plate shows is already legal to show under `#hidden-info`.

## 6. Asset gaps

| Asset | Placeholder until it exists |
|---|---|
| Portrait per hero (one per lineage at launch is enough) | lineage-hued wash + faint emblem |
| Lineage emblems (Greek, Norse, Hindu), vector | laurel / hammer / lotus strokes from the mock |
| Item squares (6 items) | lineage-hued square |
| Action icons (18 abilities) | today's letter on a tile |
| Fonts as Unity font assets | system sans |

## 7. Acceptance (what "built" means)

Screenshots in the run report, at 1280×720 and 1920×1080, compared by a verifier against the pinned mock:

1. Your hero at round 1: starting gear, one boon, no `changed` tiles, no `?`.
2. Your hero at round 3 with a Blessing that moved two stats, an Enchant that changed a reach, and a Sigil
   that added a tile: green and red deltas, one violet-edged tile with a diamond, one with a triangle.
3. The enemy at round 3: lineage revealed, one boon revealed, one `?` row, at least two `unseen` tiles,
   grey `?` after the four hidden-able stats.
4. One hover panel open on an attack, showing the damage line.
5. The plate closing on an off-plate click, and staying open across an End Turn.

Plus: no hairline anywhere on the plate; every element from §3 present by `name`; the three `--mimas-*`
colour tokens for you/them/changed used, not literals.

## 8. Open questions

1. ~~Does the click that closes the plate also arm what it hit?~~ **No** (23 Sep 2026): it only closes.
2. ~~Standing height in the top section?~~ **Yes** (23 Sep 2026): `ex.vitals.height`.
3. Status effects ("burning"): the design page says elements apply no status at launch. No row is reserved
   for them; adding one is a design-page change first.
4. Should the wire carry a `RevealedAt` turn for boons so the enemy's reveal order survives a reload? Today
   the client remembers the order it saw and falls back to grant order after a reload.

## Change log

- 2026-09-23 · written from the examine session (rounds 1–6 on the canvas); status proposed.
- 2026-09-23 · open questions 1 and 2 answered by Rohan; `ex.vitals.height` added; render pinned; spec and
  T-0011 written.
- 2026-09-23 · built by T-0011: `Examine.uxml` / `Examine.uss`, `HoverPanel.uxml` / `HoverPanel.uss`, `ExamineView`, `ExamineModelBuilder`; gap 1 closed by `Unit.StatLines`, gap 3 by client memory (open question 4 stands), gap 4 by the lineage hues, gap 5 by seat names, gap 6 by `ex.vitals.height`. Drifts are noted in their rows.
