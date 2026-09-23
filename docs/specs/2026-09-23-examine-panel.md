# Spec E: the examine panel — the manuscript plate, the hover panel, and the first `--mimas-*` tokens

_Status: **work order for an autonomous Opus session** (task `T-0011`, lane full). Written 23 Sep 2026 by
Fable against `main` at `3fe5bbc` plus the uncommitted studio and docs files of the same day (T-0009 verified
pass, T-0010 at verify failing on evidence only; 452 Core tests, 58 server tests, 4 EditMode tests; Web build
12.38 MB against the 13 MB ratchet). The design was chosen by Rohan on 23 Sep 2026 over six rounds of options
on a design canvas; the result is written down as **`docs/ui/examine.md`** (the screen) and
**`docs/ui/language.md`** (the tokens), with the pinned render **`docs/ui/mockups/examine-manuscript-r3.png`**
(both plates side by side with the rules; the **right-hand, paper one** is the one to build). Design anchors:
`#examine`, `#hud`, `#hidden-info`, `#stats`, `#equipment`, `#abilities`, `#boons`, `#lineage`, `#presentation`.
Predecessors: `docs/specs/2026-09-22-boons-in-game.md` (§7 built the examine panel this one replaces),
`docs/specs/2026-09-17-online-slice.md` (the client drivers)._

---

## 0. How to run this session

Read this whole file, then `CLAUDE.md`, then `studio/STATE.md` ("Things the next agent must not rediscover"
is written for you), then **`docs/ui/language.md` and `docs/ui/examine.md` in full** (they are the design;
this spec is the work order that turns them into code), then look at the pinned render, then the design
sections named above (grep the anchors, never the whole page), then `docs/architecture.md` (client table),
ADR-017, ADR-018, ADR-026, ADR-034, ADR-036. Read every file you touch in full before editing it, in
particular `shared/Mimas.Core/Runtime/Units/Unit.cs`, `Units/BoonOverlay.cs`, `Data/StatBlock.cs` (or
wherever `StatBlock` lives; find it), `Data/RulesDef.cs`, `Data/ItemDef.cs`, `Data/BoonDef.cs`,
`Match/MatchState.cs` (`FromView`), `Match/PlayerView.cs`, `Grid/TileMap.cs`;
`MimasClient/Assets/_Game/Presentation/Match/MatchSession.cs` (`RefreshExamine`, `AddAbilityEntries`,
`AddBoonEntries`, `IsChangedByABoon`, `CloseExamine`, `RefreshEmphasis`), `IMatchHudSource.cs`
(`HudExamine`, `HudExamineEntry`, `HudAction`), `MimasClient/Assets/_Game/UI/MatchHudView.cs` (everything
under `examine` and `tooltip`), `MatchHud.uxml`, `MatchHud.uss`, `Lobby.uss`, the `Input/` folder under
`Presentation` (how a unit click reaches `_examinedUnitId`), `MimasClient/Assets/_Game/Tests/EditMode/*`.

Rules for the session:

1. **Autonomous.** No questions. Forks have defaults in §13.
2. **Presentation only, plus one Core query.** No rule changes, no wire changes, no server changes. The
   plate shows nothing the mirror does not already hold; §4 adds one read-only query on `Unit`. If you find
   yourself wanting a wire field, stop and take the §13 default.
3. **The design is `docs/ui/`, not your taste.** Every element in `docs/ui/examine.md` §3 exists by its
   `id` as the UXML `name`; every colour, face, size and gap is a token from `docs/ui/language.md` declared
   once in `Theme.uss`. No literal colour in `Examine.uss`. Where the language page and this spec disagree,
   this spec wins and you note it in the run report; where the pinned render and the language page
   disagree, the language page wins.
4. **Commits:** small commits to `main`, `area: what`, in the order of §12. Do not push. Before every
   commit: `dotnet build Mimas.slnx`, `dotnet test shared/Mimas.Core.Tests`, `dotnet test
   server/Mimas.Server.Tests`, all green; after every client commit, `unity command menu --path
   "Assets/Refresh"`, then `unity command recompile_status` → `completed` with no errors, then `unity
   command console` clean (pipe it through `grep -o '"groundTruth":{[^}]*}'`).
5. **Golden rules** 1, 2, 3, 4, 5, 7, 8, 9, 11 apply. Never hand-make a `.meta` or a `.asset`. Font assets
   are made by the Editor (§7.9). No new packages (rule 7): the glyphs are drawn in code, not imported as
   SVG. The Web build stays under the 13 MB ratchet; §13 says what to drop if it does not.
6. **Declare before you commit.** The task's `allows_assets` (§12) lists what the Editor will write. If the
   Editor writes something the list does not foresee (a `.meta` for a folder you had to create), add it to
   the task file in the same commit and say so in the run report.
7. **Evidence is screenshots.** §14 lists five states; each is a capture under `MimasClient/Assets/_Shots/`
   (never under a `Temp` folder), copied to `artifacts/` and named in the run report. The verifier holds them
   against the pinned render. Never write "matches the mock" without the file.
8. Open the run report `studio/runs/R-<date>-T-0011.md` before the first edit and append as you go.

---

## 1. Goal and result

**What a player sees afterwards.** Clicking a hero with nothing armed slides a **paper plate** in from the
right edge, full window height, 380 wide: a painting in the lineage's hue at the top with the name in a
serif over it, then health and action points as large light numbers with a heart and a bolt, then six stats
each as a base number and a green or red net change, then the boons oldest to latest, then the four items
with their actions as icon tiles. Nothing on the plate is a number about an action. Resting the pointer on
any row or tile opens one hover panel to the left with everything the rules know: cost, damage, reach as
labelled tiles, one sentence, the damage line as the rules compute it, what a boon changed in violet. The
enemy's plate is the same plate in vermilion, with dashed `?` tiles for actions you have not seen, `?` rows
for boons you have not seen, and a grey `?` after a stat a hidden boon may be moving. A click anywhere off
the plate closes it and does nothing else.

**What exists afterwards in the repo.**

- `Unit.StatLines` in Core (§4), tested.
- `hueDark` / `hueLight` on the three lineages (§5), schema and `docs/data.md` updated.
- `MimasClient/Assets/_Game/UI/Theme.uss` declaring every `--mimas-*` token of `docs/ui/language.md`;
  `Examine.uxml` / `Examine.uss` / `HoverPanel.uxml` / `HoverPanel.uss` as UI Toolkit templates; three font
  assets; `ExamineView.cs` (rendering) and the presenter model `HudExamine` rebuilt to the inventory (§7).
- The old examine block removed from `MatchHud.uxml`, its rendering removed from `MatchHudView.cs`.
- EditMode tests for the presenter model (§9), screenshots of the five states (§14), docs of §8, `T-0011`
  at `verify`.

**Not in scope** (write them as follow-ups in STATE, do not build them): the action bar adopting the hover
panel and the tokens; the draft cards, round banner and lobby in the new language; portrait, emblem, item
and action **art** (placeholders only, §7.8); status effects (the design page says elements apply no status
at launch); any change to what the opponent may see.

---

## 2. Decisions locked on 23 Sep 2026 (do not reopen)

| # | Decision | Where it bites |
|---|---|---|
| E1 | The **manuscript** plate: bone paper, ink type, serif names; the portrait plate (ink) is kept on the canvas and not built | §7.3 |
| E2 | Single column in this order: painting and name · vitals · stats · boons · equipment. No lane columns | §7.3 |
| E3 | **No hairlines, no boxed sections.** A section is a caption and 18px of air | `Examine.uss` |
| E4 | Stats show **base then net**: base = rules base + items, net = known boons; green above, red below, nothing at zero; grey `?` on the enemy while `UnrevealedBoonCount > 0` (never on health or AP, which are public from round start) | §7.5 |
| E5 | Health and AP appear twice: live current/max at the top with a bar and eggs; base±net in the stats grid | §7.4, §7.5 |
| E6 | Actions are **tiles with names only** (icon, name under). No numbers on the plate. A violet edge and a diamond = changed by an Enchant; a violet edge and a triangle = added by a Sigil; dashed `?` = unseen | §7.7 |
| E7 | The boon that changed or added an action is **not named under the item**; it is listed under Boons only | §7.6 |
| E8 | Boons ordered **oldest to latest**: starting Blessing, then each draft; the enemy's in the order you learned them, unknown last. Actions in the order the item gained them: its own first, then Sigil grants | §7.6, §7.7 |
| E9 | **One hover panel** for actions, boons, items and stats, always on ink, to the left; no "seen on turn N" text anywhere | §7.10 |
| E10 | Click outside **only closes**; it does not select or arm what it hit | §7.11 |
| E11 | **Standing height** is shown in the vitals block as a third small cell | §7.4 |
| E12 | Heart for health, bolt for action points, hollow/filled eggs for AP held; circle / diamond / triangle for Blessing / Enchant / Sigil | §7.8 |

---

## 3. Architecture (read before the sections that follow)

- **Core adds one read-only query and nothing else** (§4). The mirror already has what the plate needs:
  `Unit.PublicStats` (base + items, public by design) and `Unit.Stats` (with every boon the viewer knows),
  `BoonOverlay.Overrides`, `BoonOverlay.Grants` / `Unit.BoonOfAbility`, `UnitView.Boons` in grant order with
  `KnownEntry.Revealed`, `UnitView.Abilities` with `SourceItemId`, `UnitView.UnrevealedBoonCount`,
  `TileMap[hex].Height`. Hidden enemy boons are simply absent from the mirror, so `Stats − PublicStats` on
  the mirror is exactly "the net of what you know" and can never leak.
- **The presenter (`MatchSession`) builds a model; the view renders it.** `HudExamine` is rebuilt (§7.2)
  to carry precisely the inventory of `docs/ui/examine.md` §3 with the flags of E4–E8 already resolved, so
  `ExamineView` never touches Core types. The model is built from `View` (the `PlayerView`) and `Rules`
  (the mirror `MatchState`), exactly as `RefreshExamine` does today.
- **The view is a UI Toolkit template of its own**, `Examine.uxml` instantiated into the HUD root by
  `MatchHudView` (which keeps owning the root and the panel settings) and driven by a new `ExamineView`
  class so `MatchHudView.cs` gets smaller, not larger. `HoverPanel.uxml` is a second template, one instance,
  moved and filled on pointer enter.
- **Tokens live in `Theme.uss`**, imported first by `MatchHud.uss` and `Lobby.uss`. Today's `--hud-*`
  variables stay this session (the bar and lobby keep using them); `Theme.uss` adds the `--mimas-*` set.
  Only the new files use `--mimas-*`. Migrating the bar is a follow-up.
- **Glyphs are drawn, not imported.** Heart, bolt, egg, the three kind shapes, the four slot glyphs, the lane
  glyphs, the trajectory and sight glyphs, the three lineage emblems: each is a small `VisualElement`
  subclass painting with `generateVisualContent` and `Painter2D` (UI Toolkit's vector API; it works on
  WebGL2). No package, no `.meta` for icons. The action icons keep today's placeholder (a letter on the
  tile) until art arrives.
- **Fonts are Unity font assets** made by an Editor script (§7.9), the only new binary assets.
- **Overflow scrolls.** The plate is full window height; the painting is 210 tall when the window is at
  least 900 tall and 120 otherwise; the body below the painting is a `ScrollView` with a 2px scroller in
  `--mimas-hair`. At 1920×1080 the round-3 state fits without scrolling; at 1280×720 it scrolls.

---

## 4. Core changes (`shared/Mimas.Core/Runtime`)

### 4.1 `Unit.StatLines`

```csharp
public readonly struct StatLine
{
    public string SourceKind { get; }   // "base" | "item" | "boon"
    public string SourceId { get; }     // null for base; the item id; the boon id
    public int Amount { get; }
}

/// <summary>
/// How a stat is made, in a fixed order: the rules base, then each equipped item in slot order that
/// contributes to the key, then each boon on the overlay in grant order with a stat effect on the key.
/// The amounts sum to <see cref="Stats"/>[key]; the base and item lines sum to <see cref="PublicStats"/>[key].
/// Read-only; a presenter's question (design: #stats rule 3, docs/ui/examine.md §5 gap 1).
/// </summary>
public void StatLines(string statKey, List<StatLine> into)
```

- Order is deterministic: base, items in the loadout's slot order (weapon, crown, boots, armour), boons in
  the overlay's grant order. Zero contributions are omitted (an item without the key adds no line).
- Uses whatever the overlay already keeps for stat effects; if the overlay applies stat effects without
  keeping per-boon amounts, add the minimal record (boon id, key, amount) to it in grant order. Floors
  (`rules.boons.floors`) are applied by `Stats` already; `StatLines` reports the raw lines, and the test
  in §9 asserts `sum == Stats[key]` only for fixtures that do not hit a floor, plus one fixture that does,
  asserting the lines still list the raw amounts and `Stats` shows the floored value.
- No `UnityEngine`, no floats, no LINQ in the hot path (it is not hot, but keep it plain), C# 9.

### 4.2 Nothing else

`KnownEntry`, `PlayerView`, `Wire`, `Session`: untouched. The enemy's boon reveal order is remembered by
the client (§7.6); a `RevealedAt` on the wire is a design question for later, written in STATE.

---

## 5. Data changes (`MimasClient/Assets/_Game/Data/`)

- `lineages/greek.json`, `norse.json`, `hindu.json` gain two presentation keys, hex strings:
  `"hueDark"`, `"hueLight"` — Greek `#1d4f5c` / `#7fb7b0`, Norse `#3a3f52` / `#8e98b8`, Hindu `#6a3a12` /
  `#e0a35a` (from `docs/ui/language.md` §1).
- `tools/schemas/lineage.schema.json`: both keys optional strings matching `^#[0-9a-fA-F]{6}$`.
- `LineageDef` reads them (nullable; a missing hue falls back to `--mimas-ink-2` and a lighter grey in the
  view, never a throw). `DataTests` cover the read.
- `docs/data.md` Lineages table: two rows.
- The Editor regenerates `GameDataManifest.asset` on Refresh; commit it with the JSON, as T-0010 did.

---

## 6. Server

None. Not one file under `server/` changes. If a server test goes red, the cause is in Core §4 and it is
yours to fix without touching the server.

---

## 7. Client (`MimasClient/Assets/_Game`, presentation only)

### 7.1 Files

| File | New / changed | Holds |
|---|---|---|
| `UI/Theme.uss` | new | every `--mimas-*` token of `docs/ui/language.md` §1, §2, §4 on `:root`, both surfaces (`--mimas-fg` etc. are the ink values; the paper values are `--mimas-paper-fg` etc.) |
| `UI/Examine.uxml`, `UI/Examine.uss` | new | the plate: every element of `docs/ui/examine.md` §3 by `name` |
| `UI/HoverPanel.uxml`, `UI/HoverPanel.uss` | new | the one hover panel, `docs/ui/language.md` §6 |
| `UI/ExamineView.cs` | new | binds `Examine.uxml` to a `HudExamine`, owns the hover panel, the scrim, the open/close animation |
| `UI/Glyphs.cs` | new | the `Painter2D` glyph elements (§7.8), one small class per glyph, all `[UxmlElement]` so they can sit in UXML |
| `UI/Fonts/` | new | three `.ttf` and the font assets the Editor makes (§7.9) |
| `UI/MatchHud.uxml`, `UI/MatchHud.uss` | changed | the old `#examine` block and its rules removed; `@import url("Theme.uss")` first; an empty `#examine-mount` where `ExamineView` instantiates the template |
| `UI/MatchHudView.cs` | changed | drops the examine fields and `Render` code for it; creates `ExamineView` and forwards `Examine` and `CloseExamine` |
| `Presentation/Match/IMatchHudSource.cs` | changed | `HudExamine` rebuilt (§7.2); `HudExamineEntry` removed if nothing else uses it |
| `Presentation/Match/MatchSession.cs` | changed | `RefreshExamine` builds the new model; the reveal-order memory (§7.6); `CloseExamine` unchanged in meaning |
| `Editor/Fonts.cs` (beside `Editor/WebBuild.cs`, assembly `Mimas.Client.Editor`) | new script | `Fonts.Generate` (§7.9) |
| `Tests/EditMode/ExamineModelTests.cs` | new | §9 |

### 7.2 The model (`HudExamine`, rebuilt)

```csharp
public sealed class HudExamine
{
    public string Name;                 // seat name; "You" / "Random Bot" in practice (§13)
    public string LineageName;          // null = unknown
    public string LineageIcon;          // catalogue icon key, null when unknown
    public string HueDark, HueLight;    // null when unknown → view falls back
    public bool IsMine;
    public int Hp, MaxHp, Ap, ApPerTurn, Height;
    public List<HudStat> Stats = new List<HudStat>();      // six, in inventory order
    public List<HudBoon> Boons = new List<HudBoon>();      // ordered per E8, unknown last
    public List<HudItem> Items = new List<HudItem>();      // four, slot order
}
public sealed class HudStat { public string Id, Label, Icon; public int Base, Net; public bool Hidden; public List<HudStatLine> Lines; }
public sealed class HudStatLine { public string Label; public int Amount; }   // "base" / item name / boon name
public sealed class HudBoon { public string Id, Name, Kind, God, LineageName, OnItemName, Description, Badge; public bool Revealed, Starting; }
public sealed class HudItem { public string Id, Name, Slot, Kind, Quick, Description, StatLine; public List<HudTile> Tiles; public List<string> BoonNames; }
public sealed class HudTile { public string Id, Name, Icon, TypeLine, Description; public bool Revealed, Changed, Added; public HudTileNumbers Numbers; public List<string> Changes, Conditions; public string DamageLine; }
public sealed class HudTileNumbers { public int Cost; public int? Damage, Apex; public int? RangeMin, RangeMax; public string Element; public bool RangeChanged, DamageChanged; }
```

Every string the view shows is composed in the presenter (it has the catalogue); the view formats numbers
and colours only. `HudStat.Id` values: `hp`, `ap`, `power.weapon`, `power.spell`, `defense.weapon`,
`defense.spell`, with the labels of the inventory (Health, Actions, Strength, Magic, Armour · weapon,
Armour · spell).

### 7.3 The plate (`Examine.uxml`)

Root `ex.plate` (absolute, right 0, top 0, bottom 0, width `--mimas-plate-w`, background `--mimas-paper`,
padding `--mimas-plate-pad`), with class `ex--mine` or `ex--theirs` setting `--ex-accent` to `--mimas-you`
or `--mimas-them` (paper values). Order inside, exactly:

1. `ex.painting` (negative margins to bleed, height per §3): three layered radial gradients in `HueDark` /
   `HueLight` fading to paper over the lower 60%, the lineage emblem glyph at 220px, opacity 0.06, top right.
   Over it: `ex.name` (serif 32), `ex.lineage` (serif italic 15: "Greek · your hero" / "Norse · the enemy" /
   "Unknown lineage"), `ex.close` (✕, `--mimas-paper-fg-3`).
2. `ex.body`, a `ScrollView` (vertical, scroller 2px, thumb `--mimas-hair`):
   - `ex.vitals`: `ex.vitals.hp` (heart · `Hp` at display 34 · "/ MaxHp" · 2px bar filled `Hp/MaxHp` in the
     accent), `ex.vitals.ap` (bolt · `Ap` · "/ ApPerTurn" · one egg per `ApPerTurn`, filled for the first
     `Ap`), `ex.vitals.height` (a small cell: the `arc` glyph · `Height` · caption "height").
   - caption `stats` (serif 15, accent) then `ex.stats`, a 3×2 grid of `ex.stat.<id>` cells: base at display
     24, net at 12 in `--mimas-up` / `--mimas-down`, or a `?` in `--mimas-paper-fg-3` when `Hidden`, then a
     glyph and a caption at 7.5.
   - caption `boons` then `ex.boons`: one `ex.boon` row per `HudBoon`: kind glyph in the accent (filled for
     `Starting`), name (serif 16), `OnItemName` in fg-3 at 10 on the right; an unrevealed one is the
     dashed `?` glyph and "Unrevealed" in fg-3.
   - caption `equipment` then `ex.equipment`: one `ex.item.<slot>` group per item, 12px apart: a 28px
     square in the lineage hues, the name (serif 17), the `StatLine` in `--mimas-paper-up` at 10 on the
     right; under it, indented 38px, the tiles (`ex.item.<slot>.tile.<abilityId>`) 8px apart, wrapping;
     the armour group shows the grey line "turns 2 of every blow" instead of tiles. **No boon names here** (E7).
3. `ex.scrim` is **not** inside the plate: it is a full-window transparent element `ExamineView` inserts
   under the plate in the HUD root while the plate is open, `picking-mode: Position`, whose `PointerDown`
   calls `CloseExamine()` and **stops propagation** so the board's input never sees the click (E10). It is
   removed when the plate closes. The HUD elements outside the plate (bar, End Turn) sit above the scrim in
   the tree so they still work; verify End Turn still ends the turn with the plate open.
4. `ex.wash`: a 200px gradient to `--mimas-ink` at 45% along the plate's left edge, `picking-mode: Ignore`.
5. `ex.ring`: not in this template; the existing `RefreshEmphasis` already emphasises the examined unit's
   nameplate. Add a 1px ring in the accent to that emphasis for the examined unit only (in the unit tag, not
   on the board mesh).

Open and close: the plate translates in from `translate(100%, 0)` over 160 ms, out the same; no fade.

### 7.4 Vitals

`Height` is `Rules.Map[unit.Position].Height` from the mirror; 0 on the ground. It has no hover panel.
Health and AP rows open the stat panel of their `HudStat` (§7.10) on hover.

### 7.5 Stats

For each of the six keys: `Base = unit.PublicStats[key]`, `Net = unit.Stats[key] − unit.PublicStats[key]`
on the **mirror's** `Unit` (`Rules.Units.TryGet`), `Hidden = !unit.IsMine && view.UnrevealedBoonCount > 0`
for the four lane stats and never for `hp` and `ap` (E4). `Lines` from `Unit.StatLines(key)` mapped to
names through the catalogue (`"base"` → "base"; an item → its name; a boon → its name).

### 7.6 Boons

`view.Boons` is in grant order. Your own list is that order (the starting Blessing is index 0). For the
enemy: `MatchSession` keeps a `List<string> _revealOrder` per enemy unit id, appended when a reveal event
names a boon (the events the reveal flyover already listens to), and orders revealed boons by their index
in it, falling back to grant order for any revealed boon it did not see (a reload), with unrevealed
entries last. `HudBoon.Badge` is `"starting Blessing"` for index 0 of your own list, else `"drafted"`
(the round is not in the view; do not invent it). `OnItemName` is the equipped item whose slot matches the
boon's `requires.slot` (null for a Blessing). `God` is the name before the apostrophe, whole name if none
(the rule T-0010 used).

### 7.7 Tiles

For each item, in `unit.ItemIds` slot order, the tiles are `view.Abilities` with `SourceItemId == itemId`
in the view's order (the item's own first, then grants: assert this in the EditMode test, and if the view
orders them otherwise, stable-sort by `Unit.BoonOfAbility(id) != null`). Under boots, the innate walk
(`rules.innateAbilityIds`) comes first. Per tile:

- `Revealed` from `KnownEntry.Revealed`; an unrevealed one renders as the dashed `?` tile with "unseen".
- `Changed` = the bar's `IsChangedByABoon` (make it non-static-private → internal, or copy; do not
  duplicate the field list).
- `Added` = `unit.BoonOfAbility(id) != null`.
- `Numbers`: cost from the resolved def (`Rules.ResolveAbility` gives the boon-adjusted def, as the bar
  uses); damage, range, apex, element from `AttackDef` when it is an attack; `RangeChanged` /
  `DamageChanged` by comparing the resolved def with the catalogue's base def.
- `TypeLine`: `"weapon attack · Longbow · action"`, `"spell · Ember Circlet · action"`,
  `"movement · innate"`, `"movement · Leaping Boots"`.
- `Changes`: one string per override or grant: `"reach 5 → 6 · Apollo's Bowstring"`, `"damage 2 → 3 ·
  Tyr's Edge"`, `"granted by Nike's Jab"`. The boon name comes from `BoonOverlay.Overrides[i].BoonId` (or
  whatever field names the boon; `IsChangedByABoon` shows where to look) and `BoonOfAbility`.
- `Conditions`: `"lobbed, clears cover"` / `"straight line"`, `"needs line of sight"` / `"no sight needed"`,
  `"one target"`; for movement the mode's own words (`"2 hexes"`, `"line"`, `"clears 1"`).
- `DamageLine` for attacks: `"<damage> base + <power> <Strength|Magic> − their armour"` for yours,
  `"… − your armour"` for theirs, where `<power>` is the attacker's `Stats.power.<lane>`. This is the
  rules' formula from `#damage` written out, **not** a target-specific preview (there is no target).

### 7.8 Glyphs (`Glyphs.cs`)

One `VisualElement` subclass per glyph, painting a 24-unit path with `Painter2D` in `generateVisualContent`,
stroke 1.5, round caps, colour from `--mimas-*` via a `--glyph-color` custom property read in
`CustomStyleResolved`, size from the element's layout. Set: heart, bolt, egg (hollow / filled by a class),
circle / diamond / triangle (hollow / filled), sword, spark, crown, boot, shield, arc, straight, eye,
eye-struck, laurel, hammer, lotus, close, unknown (dashed circle with a `?` Label inside). The paths are
in `docs/ui/language.md` §3 by name; copy the shapes from the mock's SVG paths in
`artifacts/UI Drafts/hud-mock/gen3.py` and `gen4.py` (`ICON` dictionaries) if the folder exists, else draw
the simplest recognisable version. Action icons: the tile shows the existing letter placeholder from the
bar (`HudAction`'s icon handling) until art exists; put the icon key in the tile's `name` so a later
sprite can bind to it.

### 7.9 Fonts

Download SIL-OFL TTFs into `UI/Fonts/`: Josefin Sans Light (300) and Regular (400), Cormorant Garamond
SemiBold (600), Bold (700) and Italic (500), Sora Regular (400). Add an Editor script
`Mimas.Client.Editor.Fonts.Generate` that creates a **dynamic** TextCore font asset per file (sampling 90,
atlas 1024, Latin only) and saves it beside the TTF; run it with `unity command menu --path
"Mimas/Fonts/Generate"` (register the menu item) and commit the `.ttf`, the generated `.asset` files and
every `.meta` the Editor wrote. `Theme.uss` declares `--mimas-font-display`, `--mimas-font-serif`,
`--mimas-font-body` with `resource()` / `url()` to the assets; `Examine.uss` uses only the variables. Check
`link.xml` keeps TextCore; check the Web build size (§10 and §13).

### 7.10 The hover panel (`HoverPanel.uxml`, one instance)

`ExamineView` owns one `HoverPanel` element in the HUD root, hidden by default. Every `ex.*` row and tile
registers `PointerEnter` / `PointerLeave`; on enter, after 120 ms without leaving, the panel is filled from
the row's model and shown at `x = plate.left − 22 − panel.width`, `y = row.top − 12`, clamped inside the
window; on leave it hides. Contents in the order of `docs/ui/language.md` §6, on `--mimas-ink-2`, with a
2px left edge in the accent (fg-3 for an unknown): `hp.tile` (glyph or letter), `hp.name`, `hp.type`,
`hp.numbers` (a row of tiles: glyph over value over caption; damage in `--mimas-amount`, reach in
`--mimas-changed` when `RangeChanged`), `hp.text`, `hp.damageline`, `hp.changes` (each with a small filled
diamond in `--mimas-changed`), `hp.conditions`, `hp.flavour`. Sections absent when their data is null.
Kinds: tile → attack or movement; boon → kind glyph as the tile, `"Blessing · Greek · on you"`,
description, `Badge` as the one condition, flavour `"<God> answers those who pray to the <lineage> gods."`;
item → slot glyph, `"weapon · bow · lobbed · no sight needed"`, description, `StatLine` as one number
tile, `BoonNames` under changes, for theirs the extra sentence "You have seen k of n abilities"; stat →
shield glyph, `"stat · base, then every change"`, the `Lines` as label/amount rows, plus a fg-3 row
"unrevealed boons · ?" when `Hidden`; unknown tile → `"ability · <item>"`, "One more ability on this item.
You will see it the first time it is used."; unknown boon → "A Blessing shows itself when it changes a
result; an Enchant when a number contradicts what you know; a Sigil on first use."

### 7.11 Click outside

§7.3 item 3. Also: `Escape` closes (the HUD already has a key path for resign; reuse it), and the plate
closes itself when its unit dies (`RefreshExamine` returns null → view hides). The plate stays open across
turns, events, and the round card; it is closed by the round-end banner and the draft overlay (they take
the screen), and it does not reopen after them.

### 7.12 What is removed

`MatchHud.uxml` `#examine` and everything under it; the `examine-*` rules in `MatchHud.uss`; the
`_examine*` fields and the examine branch of `Render` in `MatchHudView.cs`; `HudExamineEntry`;
`AddAbilityEntries`, `AddBoonEntries`, `LineageSubtitle`, `DescribeItem` in `MatchSession.cs` if nothing
else uses them (grep first; `ExamineProp` for props stays and is rendered by `ExamineView` as a reduced
plate: name, height, hp bar, no stats/boons/equipment — §13).

---

## 8. Docs and design

- `docs/design/index.html` `#examine`: replace the paragraph with a `proposed` sentence pointing at
  `docs/ui/examine.md` and `docs/ui/language.md`, and one line of what changed on 23 Sep 2026 (single
  column, base±net stats, tiles, one hover panel, click outside closes, standing height). The `#hud` list
  item "Examine panel left" becomes "right". Through the declared allowance only.
- `docs/ui/examine.md`: status `built` at the end, with a change-log line; any deliberate difference from
  the page written as `drift` in the row it concerns.
- `docs/ui/language.md`: status `built` per token that `Theme.uss` declares; the fonts row says which
  weights shipped.
- `docs/architecture.md` client table: `UI/Theme.uss`, `UI/Examine.*`, `UI/HoverPanel.*`, `UI/ExamineView`,
  `UI/Glyphs`; remove the duplicate `Presentation/MatchSession` row the verifier found.
- `docs/data.md`: the lineage hue keys (§5); `Unit.StatLines` under the stats section.
- ADR-037: "The UI book: `docs/ui/` is the design of each screen, tokens are declared once in `Theme.uss`,
  element ids in the book are UXML names". Short.
- `studio/STATE.md`: rewritten (what a player sees, numbers, follow-ups: bar migration to tokens and the
  hover panel, draft/banner/lobby in the language, art slots, `RevealedAt` question).
- Run report complete; task at `verify`.

---

## 9. Tests (one behaviour each, `Method_Scenario_Expected`)

Core, `shared/Mimas.Core.Tests/StatLinesTests.cs` (new):

- `StatLines_BaseAndItems_SumToPublicStats` — for each of the six keys on a fixture unit.
- `StatLines_WithStatBlessing_SumsToStats` — a `+4 hp` Blessing appears as a `boon` line after the items.
- `StatLines_ItemWithoutKey_AddsNoLine` — boots contribute no Strength line.
- `StatLines_Order_BaseThenSlotOrderThenGrantOrder` — two stat boons in grant order.
- `StatLines_FlooredStat_ListsRawLines` — a trade-off Blessing pushing under a floor.
- `StatLines_OnMirror_KnowsOnlyRevealedBoons` — build a `PlayerView` for the opponent, `MatchState.FromView`,
  assert the enemy unit's lines hold base and items only while the Blessing is hidden.

Client EditMode, `Tests/EditMode/ExamineModelTests.cs` (new; build the model through whatever seam
`MatchSession` exposes, or extract the builder into a static `ExamineModelBuilder` in `Presentation/Match`
so it is testable without a scene — preferred):

- `Build_OwnUnit_SixStatsInInventoryOrder`.
- `Build_OwnUnit_NetIsStatsMinusPublic` (green and red cases).
- `Build_EnemyWithUnrevealedBoon_LaneStatsHiddenHealthNot`.
- `Build_Tiles_OwnAbilitiesBeforeSigilGrants`.
- `Build_Tiles_EnchantOverride_MarksChangedAndChangeLine`.
- `Build_Tiles_SigilGrant_MarksAddedNoBoonNameOnItem`.
- `Build_EnemyTiles_UnrevealedAreUnseen`.
- `Build_Boons_StartingFirstThenGrantOrder_UnknownLast`.
- `Build_EnemyBoons_RevealOrderWins`.
- `Build_Height_FromMapTile`.

Existing tests: none softened, none removed. Test counts may only go up.

---

## 10. Verification commands (run all before the final commit)

```
dotnet build Mimas.slnx
dotnet test shared/Mimas.Core.Tests          # ≥ 452 + §9
dotnet test server/Mimas.Server.Tests        # ≥ 58, unchanged
unity command menu --path "Assets/Refresh" ; unity command recompile_status ; unity command console
unity test MimasClient --mode EditMode --report-format junit --output artifacts/editmode.xml --timeout 600   # ≥ 4 + §9; exit 8 = failed, not a flake
unity build MimasClient --target WebGL --execute-method Mimas.Client.Editor.WebBuild.Build --output-path Build/Web   # ≤ 13 MB; report the size
node tools/smoke/browser-smoke.mjs           # three engines, as T-0010 did
node E:/Studios/Trinetra-Game-Studio/tools/ladder/ladder.mjs --project mimas --task T-0011   # before the final commit: rung 0 checks the working tree
```

---

## 11. Editor workflow, in order

1. `unity status --format json` → `ready`; if no Editor, `unity open MimasClient` and wait (~1 min).
2. After Core §4 and data §5: Refresh, recompile_status, console; confirm `[GameDataManifest] Rebuilt`.
3. Fonts §7.9 through the menu item; confirm the assets exist and the console is clean.
4. After each client commit: Refresh, recompile_status, console.
5. Screenshots: practice mode (`LocalSessionHost`, Arena scene) reaches the five states of §14 by playing:
   round 1 before the first move (state 1); play to round 3 drafting an Enchant and a Sigil where offered
   (state 2; if the offers never include one of each, take the §13 default); click the bot (state 3, after
   it has used one action and one boon has revealed — play until a reveal flyover appears); hover an attack
   tile (state 4, capture scheduled from inside the Editor as STATE describes); click the board with the
   plate open (state 5: two captures, before and after). Captures go to `Assets/_Shots/examine-*.png`,
   then copy to `artifacts/`.
6. Web build and smoke last.

---

## 12. Commit plan (each step green before committing; `git add` only the files you touched)

1. `core: Unit.StatLines` (+ tests).
2. `data: lineage hues` (+ schema, `docs/data.md`, `DataTests`, manifest).
3. `client: Theme.uss with the mimas tokens; fonts` (+ Editor script, assets, metas).
4. `client: Glyphs drawn with Painter2D`.
5. `client: the examine model rebuilt to the UI book` (+ `ExamineModelBuilder`, EditMode tests).
6. `client: the manuscript plate and the hover panel` (templates, `ExamineView`, old block removed).
7. `client: click outside closes; scroll; reveal order; height`.
8. `docs: design page, UI book statuses, architecture, data, ADR-037`.
9. `studio: run report, STATE, T-0011 to verify`.

Task file `studio/tasks/T-0011-examine-panel.md` is written by this spec's author with `allows_assets`
covering: `docs/design/index.html`; `shared/Mimas.Core.Tests/StatLinesTests.cs`, `DataTests.cs`;
`MimasClient/Assets/_Game/Data/lineages/**`; `MimasClient/Assets/_Game/Content/GameDataManifest.asset`;
`MimasClient/Assets/_Game/UI/Fonts/**`; new `.cs.meta`, `.uxml.meta`, `.uss.meta` under `UI/` and
`Tests/EditMode/` and `Editor/`; `MimasClient/Assets/_Shots/**`. Anything else the Editor writes: rule 0.6.

---

## 13. Defaults for forks the session may hit

| Fork | Default |
|---|---|
| `StatBlock` has no enumerable keys | Add `StatKeys.All` (the six) next to it; `StatLines` iterates nothing else |
| The overlay applies stat effects without keeping per-boon amounts | Add a `List<(string boonId, string key, int amount)>` to `BoonOverlay` in grant order, filled where the effect is applied |
| `Painter2D` unavailable on the shipped UI Toolkit version | It is in Unity 6; if a glyph will not draw on WebGL2, fall back to a `Label` with a Unicode shape for that glyph only and report it |
| Web build over 13 MB after fonts | Drop Sora (body uses Josefin Sans 400), rebuild; still over → drop Cormorant Italic; still over → stop and report. Never move the ratchet |
| A font asset generation API differs (`FontAsset.CreateFontAsset` signature) | Use whatever the installed `com.unity.textcore` offers; a static (non-dynamic) atlas at 1024 is acceptable |
| The practice offers never include both an Enchant and a Sigil by round 3 | Capture state 2 with whatever was drafted, and add a second capture from an EditMode-built model rendered in the Editor (a menu item `Mimas/Examine/Preview round 3` that feeds `ExamineView` a fixture) |
| The bot reveals no boon before the series ends | State 3 shows the `?` rows and `?` stats without a revealed boon; say so |
| Seat name in practice | `"You"` and `"Random Bot"`; online, the room's seat names from the session block |
| `ScrollView` shows a mouse-wheel scroller chrome | Style the vertical scroller: 2px, no arrow buttons, thumb `--mimas-hair` |
| Escape is already bound | Leave it; ✕ and the scrim are enough |
| A prop is examined | The reduced plate: name, height, hp bar; no stats, boons, equipment; same close rules |
| Hover on a touch device | Not handled; the plate reads without hover |
| `IsChangedByABoon` is private static | Make it `internal static` and use it; no copy |
| The examined unit is mid-animation when the plate opens | Open anyway |
| 1280×720: the plate hides the End Turn button | Move End Turn 380px left while the plate is open (a class on the HUD root), and back on close; capture it |

---

## 14. Definition of done (copy into the final report with ticks)

- [ ] `dotnet build Mimas.slnx` clean; Core tests ≥ 452 + 6; server tests ≥ 58 unchanged; EditMode ≥ 4 + 10;
      no existing assertion softened or removed.
- [ ] `Unit.StatLines` sums to `Stats` and, on the mirror, never lists a hidden boon.
- [ ] Three lineages carry hues; schema and `docs/data.md` say so.
- [ ] `Theme.uss` declares every token in `docs/ui/language.md` §1, §2, §4; `Examine.uss` and
      `HoverPanel.uss` contain no literal colour or font.
- [ ] Every `id` of `docs/ui/examine.md` §3 exists as a UXML `name` (list them in the report with a grep).
- [ ] Screenshots, in `artifacts/` and named in the run report, held against
      `docs/ui/mockups/examine-manuscript-r3.png`: (1) your hero at round 1, (2) your hero at round 3 with a
      green and a red net, a changed tile and an added tile, (3) the enemy with `?` tiles, a `?` boon row and
      `?` stats, (4) a hover panel open on an attack showing the damage line, (5) the plate before and after
      an off-plate click, with the click having selected nothing. Plus one at 1920×1080 showing no scroll
      and one at 1280×720 showing the scroller.
- [ ] No hairline, no bordered section on the plate; no boon name under an item; boons oldest to latest;
      no "seen on turn" text.
- [ ] `unity command console` clean; Web build ≤ 13 MB with the size in the report; smoke green on three engines.
- [ ] Docs of §8; ADR-037; STATE rewritten with the follow-ups; run report complete; `T-0011` at `verify`.
