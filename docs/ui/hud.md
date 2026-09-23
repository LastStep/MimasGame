# The match HUD · the board in play

_Status: **proposed** (chosen by Rohan on 23 Sep 2026 in the HUD session, canvas rounds 1–3). Design anchors:
`#hud`, `#turns`, `#time-controls`, `#attacks`, `#damage`, `#hidden-info`, `#aiming-presentation`, `#camera`
(your end is always on the left), `#presentation`. Language: `docs/ui/language.md`. Mock:
https://claude.ai/artifact/K9qZcJQ115G5G685sdMAwy, page "HUD": round 1 **"A · as drawn"** (the layout) and
**"The parts"**; round 2 **"A · Arrow Shot armed on them"**, **"A · their turn"**, **"A · your last seconds,
no AP left"**, **"The attack preview · three shapes"** (shape 1 chosen); round 3 **"A · examine open on them ·
the ink plate"**. Rohan's own sketches: `artifacts/UI Drafts/Mimas InGame UI Draft 1.png` / `Draft 2.png`.
Work order: `docs/specs/2026-09-23-hud-restyle.md`, task `T-0013`. Today's HUD: `MatchHud.uxml` /
`MatchHud.uss` / `MatchHudView.cs` (package D, ADR-019), which this restyles; the behaviour of package D stays._

## 1. What it is for

Everything a player needs to take a turn, and nothing they do not: whose turn and how long, what they can
still spend, what each action costs, what a shot will do before they take it, and what they have not yet
shown the opponent. Pillar 5 (immediately legible, minimal HUD, depth on demand through examine) and the
anti-pillar "no hidden maths": a number the game knows that changes a decision is on screen.

## 2. The layout (Draft 1, in ink)

```
┌──────────────────────── ROHAN ●○ ───── ROUND 3 ───── ○● GUEST-2869 ────────────────────────┐
│                          ──●───────────────┃─────────────────                              │
│                                                                                    (●)     │
│                    17 / 28                         22 / 32                          (◆)    │
│                    ───────                         ───────                          (▲)    │
│                     (hero)                          ● ? ?                                  │
│                                                    (hero)                                  │
│ ACTION POINTS    MOVEMENT   [↦][⤴]                                               RESIGN   │
│ ϟ 1 / 3          WEAPON     [↝][◎][⚔]                                        [ END TURN ] │
│ ● ○ ○            SPELL      [✹][✦]                                                         │
└────────────────────────────────────────────────────────────────────────────────────────────┘
```

| Region | What sits there | Edge |
|---|---|---|
| top centre | the **turn track** (§3.1) | 38 from the top |
| bottom left | action points, then the three **lane rows** (§3.2) | `edge` 40 left, 40 bottom |
| right edge, from 380 down | your **boons** as glyph circles (§3.3) | `edge` 40 right |
| bottom right | Resign above End Turn (§3.4) | `edge` 40 right, 40 bottom |
| over each hero | the **unit tag** (§3.5) | anchored to the world each frame, as today |
| over the target, while an attack is armed | the **attack preview**, the one hover panel (§5) | above the target's tag |
| right, when a unit is examined | the examine plate, a layer over the boons and End Turn (`docs/ui/examine.md`) | flush right |

The **void** around the board is ink (`language.md` `void`). The floor of the screen carries a soft ink
gradient under the bar (black 86% → 50% → nothing over 300px) and a lighter one under the track (62% over
220px) so bone type reads over any tile. No panel, no box, no divider anywhere (`language.md` §4).

Draft 1's left-edge column ("action effects … like burning") is **not built**: elements apply no status at
launch (`#q-elements-status`). The space stays empty; adding it is a design-page change first. Draft 1's
fourth row ("Actions") has no abilities behind it yet (board mechanics are M3-or-later) and is not built.

## 3. Element inventory

Each `id` is the UXML element `name`, dotted as in `examine.md`. "Data" is where the value comes from:
`src` = `IMatchHudSource` (the presenter, `MatchSession`), `view` = the `PlayerView`, `session` = the
`SessionView` block. **New** marks a field the contract does not have today (§6).

### 3.1 The turn track (`hud.track`)

| id | Shows | Data | States |
|---|---|---|---|
| `hud.track.you.name` | your name, caps 15 at 0.24em | `src.MyName` **new** (online: the seat name; practice: "You") | `you` on your turn, `fg-3` on theirs |
| `hud.track.you.pips` | one 7px circle per round to win: filled `you` for each round you have won, hollow `fg-3` otherwise | `src.ScoreMine`, `src.RoundsToWin` **new** (from `session.Score0/Score1/RoundsToWin` by seat) | — |
| `hud.track.round` | "ROUND 3", caps 11 `fg-3`; "DRAFT" between rounds; "SERIES" on the series result | `src.RoundNumber` **new**, `src.Phase` **new** | — |
| `hud.track.them.pips` | the same for them, `them` | `src.ScoreTheirs` **new** | — |
| `hud.track.them.name` | their name | `src.OpponentName` | `them` on their turn, `fg-3` on yours |
| `hud.track.line` | a 2px `hair` line, 620 wide, a 2px × 14 `fg-3` tick at its centre | — | — |
| `hud.track.fill` | the active half of the line in the owner's colour | `src.IsMyTurn` | your half lit on your turn, theirs on theirs; neither between rounds |
| `hud.track.marker` | a 14px disc in the owner's colour at the middle of the active half | `src.IsMyTurn` | hidden while the rope burns |
| `hud.track.ember` | an 8px `fg` dot with a glow in the owner's colour, at the burning end of the active half | `src.TurnSecondsRemaining`, `src.RopeSeconds` | shown only while `remaining ≤ RopeSeconds`; the lit part of the half shrinks **towards the centre** as it burns |
| `hud.track.status` | under the line on **their** side: "Guest-2869 disconnected · 47 s" in caps 11 `fg-2`, a 7px hollow `them` dot | `src.OpponentStatus` | only when there is something to say |
| `hud.track.picked` | between rounds, under their end: "HAS CHOSEN" caps 10 `fg-2` and a 7px filled `them` dot | `src.Draft.OpponentPicked` | replaces today's green dot (green means "above base" in the language) |

Numbers never appear: no clock digits (ADR-015), no turn number (today's "YOUR TURN · 1" counter goes).

### 3.2 Action points and the lane rows (`hud.ap`, `hud.lanes`)

| id | Shows | Data | States |
|---|---|---|---|
| `hud.ap.caption` | "ACTION POINTS", caps 10 `fg-3` | — | — |
| `hud.ap.value` | bolt glyph 20 in `you` · current AP at display 38 · "/ 3" at 14 `fg-3` | `src.ApCurrent`, `src.ApPerTurn` | — |
| `hud.ap.eggs` | one egg per AP per turn, 12px, filled `you` while held, hollow `fg-3` when spent | same | `state.spends` on the eggs a hovered or armed action would spend |
| `hud.lane.<category>` | a row: the caption (caps 11 `fg-3` at 0.28em, 120 wide: "MOVEMENT", "WEAPON", "SPELL") then its tiles 12 apart | `src.Actions` grouped by `Category` in the order movement, weapon, spell (an unknown category is appended with its name upper-cased, as today) | rows 12 apart |
| `hud.tile[<abilityId>]` | a 44 tile: the action's icon (today its letter, §7), its cost as `glyph.cost` dots inside the bottom edge | `HudAction` | `state.ready`, `.armed`, `.unaffordable`, `.changed` (violet edge, diamond at top-right), `.added` (violet edge, triangle), `.unseen-by-them` (closed eye top-left), `.their-turn` (via the bar) |

The tile's hover opens the one hover panel **above** it with everything the rules know about the action:
the examine plate's tile panel (`examine.md` §4 "attack" and "movement" rows) — cost · damage · reach · apex ·
element as number tiles, the sentence, the damage line, the changes in violet, the conditions — plus, when
`unseen-by-them`, one `fg-3` line with the closed eye: "They have not seen this yet." The old tooltip
(`#tooltip`) goes.

Click semantics are package D's, unchanged: nothing is armed by default; a click arms; a click on the armed
tile disarms; an unaffordable tile does nothing and stays on the bar (research: removing unaffordable
actions disorients players).

### 3.3 Your boons (`hud.boons`)

| id | Shows | Data | Hover |
|---|---|---|---|
| `hud.boon[i]` | a 40 circle with a 1px `hair` edge on ink at 55%, the kind glyph 14 in `you` inside it (filled for the starting Blessing); 12 apart, top to bottom oldest to latest | your boons in grant order: `view` own `UnitView.Boons` → `catalog.GetBoon` (the examine builder's `HudBoon`) | the boon panel, **to the left** (`examine.md` §4 "boon") |

Only yours. The opponent's boons live on their tag (§3.5) and on their examine plate.

### 3.4 End Turn and Resign (`hud.end`, `hud.resign`)

| id | Shows | Data | States |
|---|---|---|---|
| `hud.end` | `button` 230 × 58: "END TURN", caps 15 at 0.28em, 1px `you` edge at 55%, ground ink 55% | `src.CanEndTurn` | `state.done` when it is your turn and no action is affordable; "THEIR TURN" in `fg-3` with a `hair` edge on theirs; disabled between rounds |
| `hud.resign` | "RESIGN", caps 11 `fg-3`, no edge, 16 above End Turn | `src.CanResign` | first click: "CLICK AGAIN TO RESIGN" in `them` for 3 s (today's two-click rule) |

### 3.5 Unit tags (`hud.tag[<unitId>]`)

| id | Shows | Data | States |
|---|---|---|---|
| `hud.tag[u].number` | "17" at display 19 and "/ 28" at 11 `fg-3`, text-shadow for legibility | `HudUnit.Hp`, `MaxHp` | — |
| `hud.tag[u].bar` | an 84 × 2 bar, filled in the owner's colour over ink at 50% | same | `ghost`: the damage an armed attack will do, in `amount`, at the right end of the fill |
| `hud.tag[u].marks` | theirs only: one mark per boon they hold, in grant order — the kind glyph 9 filled in `them` when revealed, a 10px dashed `?` when not | `view` enemy `UnitView.Boons` (`KnownEntry.Revealed`) → kind; `UnrevealedBoonCount` **new on `HudUnit`** as a list of marks | — |
| `hud.tag[u].lineage` | theirs only, once revealed: the lineage, caps 10 `fg-3`, under the marks (kept from T-0010) | `HudUnit.LineageTag` | — |
| `hud.tag[u].ring` | the 1px ring in the owner's colour while examined | `HudUnit.Examined` | as built by T-0011 |

The segmented bar (4 HP a segment) goes; the language's bar is one 2px line. Props keep their tags in the
same shape with the fill in `fg-2`.

### 3.6 Flyovers and the cursor tag (`hud.fly`, `hud.cursor`)

| id | Shows | Data |
|---|---|---|
| `hud.fly` | damage: "−6" at display 36 in `amount`; a reveal: the kind glyph in `them` · "REVEALED · TYR'S EDGE" caps 13 `fg` · "NORSE · ENCHANT" caps 10 `fg-3`; a lineage reveal the same with the emblem | `src.Flyover` (`HudFlyover.Headline/Detail`, today's composition) |
| `hud.cursor` | beside the pointer while aiming at nothing hittable: "OUT OF RANGE", caps 12 `fg` on ink at 82% | `src.CursorTag` |

## 4. States in one table

| State | Looks like | When |
|---|---|---|
| your turn | your half of the track lit `you` with the marker; bar at full; End Turn ready | `IsMyTurn` |
| their turn | their half lit `them`; your bar at 45%; End Turn "THEIR TURN" | `!IsMyTurn` |
| last seconds | the marker gives way to the ember, the lit half shrinks to the centre | `remaining ≤ RopeSeconds` |
| nothing left | End Turn `state.done` | your turn and no action affordable |
| armed | the tile `state.armed`, the eggs `state.spends`, the preview over the target (§5), the ghost on its bar | `ActiveActionIndex ≥ 0` and a target hovered |
| opponent gone | `hud.track.status` under their end | `OpponentStatus` non-empty |
| examine open | the plate covers the boons column and End Turn; a click anywhere off it closes it (T-0011 rules) | `src.Examine` |

## 5. The attack preview (shape 1: the panel)

While an attack is armed and the pointer rests on something it can target, the one hover panel opens **over
the target's tag**, 18px above it, 340 wide:

1. Head: icon tile (42, 1px `you` edge) · the action's name caps 17 · type line caps 10 `fg-3`:
   "on Guest-2869 · lobbed" (the target's name, or the prop's; the trajectory word).
2. The total: display 38 in `amount`; followed by "+ ?" in `fg-3` when `!IsExact`; caption "DAMAGE".
3. The rules' own lines, one per row, label left `fg-2`, value right `fg`: `Base 3`, `Strength +3`,
   `Their armour · weapon −2`, then each known modifier by name; **one** `?` row for what you have not
   seen: a dashed `?` glyph and "A boon of theirs you have not seen" (replacing "N unrevealed passives").
4. A refused shot keeps every line and **leads** with the reason: a 13px struck mark in `them` and the
   reason in caps 13 `fg` ("TRAJECTORY BLOCKED", "NO LINE OF SIGHT", "OUT OF RANGE"); the total goes
   `fg-3` and there is no ghost on the bar (design `#aiming-presentation`: a refused shot still shows its
   damage).

Data: `src.Preview` (`HudPreview`: `AbilityName`, `Total`, `IsExact`, `BlockedReason`, `Lines`, `TargetIsProp`)
plus **new** `AbilityIcon`, `TargetName`, `TrajectoryWord`; the board's path, tint and blocker marks are
unchanged (`AimPreview`).

## 6. Data gaps (the contract must add these)

| # | Gap | Proposed shape | Where |
|---|---|---|---|
| 1 | **Seen by the opponent**, per own ability and boon | `KnownEntry.SeenByOpponent` on the owner's entries, `UnitView.LineageSeenByOpponent`; the wire's `"seen"` / `"lineageSeen"` on own entries only; the mirror re-imports them (ADR-039) | Core + wire + tests |
| 2 | Your own name | `IMatchHudSource.MyName` | client |
| 3 | Series score as numbers | `ScoreMine`, `ScoreTheirs`, `RoundsToWin`, `RoundNumber`, `Phase` (round / draft / over) | client, from `SessionView` |
| 4 | Per-action flags for the bar | `HudAction` gains `Added` (a Sigil grant: `Unit.BoonOfAbility(id) != null`), `UnseenByThem` (gap 1), and `Hover` (the examine builder's `HudTile` for that ability, so the panel is one code path) | client |
| 5 | The preview's head | `HudPreview.AbilityIcon`, `TargetName`, `TrajectoryWord`; the hidden row's wording | client |
| 6 | Boon marks on their tag | `HudUnit.BoonMarks`: kind or null per boon in grant order | client |
| 7 | The round moments as data | `HudMoment` (`docs/ui/between-rounds.md` §3) replacing `Banner`/`BannerDetail` strings; fixes the colour bug (every non-"VICTORY" banner drew in the opponent's colour) | client |

Nothing here changes a rule. Gap 1 tells a player only what the opponent already knows about **their own**
hero, which their own actions revealed (`#hidden-info`).

## 7. Asset gaps

| Asset | Placeholder |
|---|---|
| Action icons (18 abilities) | the letter on the tile, as today and as on the examine plate; each tile carries its `icon--<key>` class for a sprite to bind to |
| The closed-eye glyph | a `Painter2D` path in `Glyphs.cs` (`eye-closed`), no asset |

## 8. Acceptance (what "built" means)

Captures at 1920×1080 and 1280×720 against the canvas boards named in the status line:

1. Your turn at round 3, Arrow Shot armed on the enemy: the armed tile, one egg `state.spends`, the preview
   panel over their tag with the `?` row, 4 in `amount` on their bar; the closed eye on at least two tiles;
   the violet edge and diamond on the bow's two tiles and the triangle on Jab.
2. Their turn: their half lit, your bar at 45%, "THEIR TURN".
3. Your last seconds with no AP: the ember burning your half, End Turn `state.done`.
4. A refused shot: the reason first, the total grey, no ghost.
5. Examine open on them: the ink plate over the boons column and End Turn.
6. The hover panel above a tile the opponent has not seen, with "They have not seen this yet."

Plus: every id of §3 present by `name`; no `--hud-*` variable left in `MatchHud.uss`; no literal colour in
it; nothing under 10px at 1920×1080.

## 9. Open questions

1. "Disabled by some other effect in game" (Draft 1's third tile state) has no rule behind it; no state is
   built until one exists.
2. The status column on the left (Draft 1) waits for status effects.

## Change log

- 2026-09-23 · written from the HUD session (canvas rounds 1–3); status proposed; spec and T-0013 written.
