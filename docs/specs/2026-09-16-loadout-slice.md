# Spec: Loadout slice (gear replaces classes)

_Work order for one autonomous Claude Code session (executing model: Claude Opus). Written 16 Sep 2026
from a question round with Rohan. Design source of truth: `docs/design/index.html` (anchors cited as
`#…`). This spec never overrides the design page; if the two disagree, the page wins and the
disagreement is a bug in this file. Everything in this file was checked against the code on 16 Sep 2026
(commit `bcd0847`); line numbers are approximate, names are exact._

---

## 0. How to run this session

Read this whole file before touching anything. Then read `CLAUDE.md` (golden rules 1, 2, 3, 4, 5, 11)
and `docs/data.md`. Then read these sections of the design page, in this order: `#how-to-use`,
`#character`, `#stats`, `#equipment`, `#weapon`, `#crown`, `#boots`, `#armour`, `#abilities`,
`#attacks`, `#damage`, `#hidden-info`, `#data-model`. Open the HTML in a browser or grep it by
`id="…"`; each section is a `<section class="concept" id="…">` block.

Rules for the session:

1. **Autonomous.** Do not ask questions. Every fork you might hit has a default in §11; take it, mention
   it in the commit message, and if it is a rule (not a number) add it to the design page as a
   `proposed` bullet with an open question.
2. **Commits:** small commits straight to `main`, message format `area: what` (see §10). Do not push.
   Run `dotnet build Mimas.sln` and `dotnet test shared/Mimas.Core.Tests` before every commit; both must
   be green.
3. **Prerequisites:** at start, `dotnet test shared/Mimas.Core.Tests` is green with 169 tests. For §7
   (client) a Unity Editor must be open on `MimasClient/` and `unity status --format json` must show a
   `ready` instance. If it does not, do §3–§6 and §8–§9 fully, commit them, and end with a report that
   says exactly which §7 steps remain.
4. **Never** create, edit or delete `*.meta` files, `.unity`, `.prefab` or `.asset` files by hand. Do
   not touch `MimasClient/ProjectSettings/**`, `Packages/manifest.json`, `packages-lock.json`.
5. **Core stays pure:** netstandard2.1, C# 9 (no `record`, no `required`, no file-scoped namespaces, no
   `init`-only properties are fine but avoid them for consistency, no `is not` patterns are fine), no
   `UnityEngine`, no floats in rules, no `DateTime.Now`, no Dictionary iteration order dependence, no
   LINQ in hot paths (LINQ in tests is fine and already used).
6. **Fail closed:** any content problem is a `MapLoadException` at parse time or a `ContentError` at link
   time with the file name. Never a silent default, never a `JsonException` escaping.
7. **No scope creep.** Out of scope (do not start, do not stub, do not add TODO comments for):
   elements / `element` field, `nullify`, boons, lineages, draft, session/series wrapper, character-select
   UI, gear on the 3D model, bot sims, tile effects, the blade weapon, presets, any HUD change outside the
   examine panel.
8. **Unity CLI gotchas** (from earlier sessions, all real):
   - Run `export MSYS_NO_PATHCONV=1` before any `unity command` that takes a path or hierarchy path.
   - Files written to disk are invisible to the Editor until
     `unity command menu --path "Assets/Refresh" --timeout 180`; then poll `unity command recompile_status`
     until `"status":"completed"`. `.meta` files appear only after that refresh; commit after it.
   - Console is `unity command console` (there is no `get_console_logs`). Grep the output for
     `error CS`, `Exception`, `[LocalMatchSession]`, `[ContentBootstrap]`, `[GameDataManifest]`.
   - `delete_asset --asset <path> --confirm true` deletes an asset and its meta the Unity way.
   - `capture_game_view --source screen` at native size (no `--width/--height`, or UI paints cyan);
     `--save_path` must be under `Assets/` (use `Assets/Temp/…`, then `delete_asset` it, never `rm`).
   - `eval --code` cannot add `using` directives or declare classes; use fully qualified names.
   - Stop Play Mode (`unity command editor_stop`) before editing C#.
   - Never run two Editor commands concurrently. On a timeout wait 10 s and retry once.

---

## 1. Goal and result

Replace `classes/` with **items**. A hero is: the same base stats for everyone + four equipped items
(weapon, crown, boots, armour). Abilities come from an innate walk plus the items. Damage lanes become
`weapon` and `spell`. The local match (`LocalMatchSession` vs `RandomBot`) plays exactly as before with the
new model, and the examine panel shows each unit's gear with `?` slots for unrevealed abilities.

When done: `classes/` is gone, `items/` has six files, `rules.json` carries `baseStats` and
`innateAbilities`, Core has `ItemDef`, `Loadout`, `ItemSlots`, units are built from loadouts,
`PlayerView` exposes item ids and each ability's source item, the server `/health` reports items, the
client `MatchSettings` holds two loadouts, `docs/data.md` and the design page are updated, ≥ 185 tests
pass.

---

## 2. Decisions locked in the question round (16 Sep 2026)

| # | Decision | Anchor |
|---|---|---|
| D1 | Character = base stats + weapon + crown + boots + armour. No classes. Warrior/mage are gone (presets are a later UI concern, not this session). | `#character` |
| D2 | Base stats are the same for every hero and live in `rules.json` `baseStats`. Stats remain an open string-keyed integer table (`StatBlock`), so a new stat is JSON only. | `#stats` |
| D3 | Health = `hp`. Strength = `power.weapon`, added to every weapon-lane hit. Magic = `power.spell`, added to every spell-lane hit. Armour = `defense.weapon` and `defense.spell`, subtracted per lane. Display names are a client concern; engine keys are the ones shown. | `#stats`, `#armour` |
| D4 | Lanes: `damageTypes: ["weapon", "spell"]`. `melee` / `ranged` / `magic` disappear from shipped data. The engine stays lane-agnostic (test fixtures may still use other lane names). | `#damage` |
| D5 | Walk is innate: `rules.innateAbilities: ["move"]`. Boots grant one extra movement mode. | `#boots` |
| D6 | Every **shipped** base weapon and base crown carries exactly two abilities. Enforced by a repo-data test (§6.4), not by the loader (the loader requires ≥ 1 attack on a weapon; see §11 for why). | `#weapon`, `#crown` |
| D7 | No negative stats on items at launch; the loader rejects a negative item stat value. | `#equipment` |
| D8 | Launch catalogue: weapons **bow** (range 1..5, slightly more damage) and **gun** (range 1..7); **one** crown; boots **jump** and **teleport**; **one** armour. No blade. | `#equipment` |
| D9 | Gear identity and innate item stats are public. Abilities are hidden until used, but the opponent sees **how many** abilities each item has, as `?` slots under the item. | `#hidden-info` |
| D10 | Client: loadouts are set in the `MatchSettings` inspector asset only. No picker UI. Nothing on the model. | `#presentation` |
| D11 | Numbers are hand-set placeholders (§3.7). No balance sims this session. | — |

---

## 3. Data changes (`MimasClient/Assets/_Game/Data/`)

Write files with 2-space indentation, LF line endings, trailing newline, keys in the order shown (the
content hash canonicalises, so order does not matter to the engine, but keep files diff-friendly).

### 3.1 `rules.json` (replace whole file)

```json
{
  "version": 1,
  "damageTypes": ["weapon", "spell"],
  "globalModifiers": ["high-ground"],
  "baseStats": {
    "hp": 20,
    "ap": 3,
    "power.weapon": 1,
    "power.spell": 1,
    "defense.weapon": 0,
    "defense.spell": 0
  },
  "innateAbilities": ["move"]
}
```

Parse rules (`RulesDef.FromJson`): `baseStats` **required**, parsed with `StatBlock.FromJson` (so
`hp` ≥ 1 and `ap` ≥ 0 are required, other keys must be `power.*` / `defense.*`); every value must be ≥ 0.
`innateAbilities` **required**, non-empty list of unique non-empty strings. Link rules (catalogue):
every `baseStats` lane key must name a declared lane; every innate id must exist; at least one innate
ability must be a `MovementDef`.

### 3.2 `items/` (new folder, six files, one item per file)

Common schema: `version` (1), `id`, `name`, `slot`, `kind`, `description`, `stats` (object, may be
`{}`), `abilities` (array, may be `[]`), `tags` (array, may be omitted), `icon` (optional string).

`items/longbow.json`
```json
{
  "version": 1,
  "id": "longbow",
  "name": "Longbow",
  "slot": "weapon",
  "kind": "bow",
  "description": "A tall yew bow. Reaches five hexes and hits harder than a gun.",
  "stats": { "power.weapon": 2 },
  "abilities": ["arrow-shot", "aimed-shot"],
  "tags": ["bow", "ranged"],
  "icon": "longbow"
}
```

`items/flintlock.json`
```json
{
  "version": 1,
  "id": "flintlock",
  "name": "Flintlock",
  "slot": "weapon",
  "kind": "gun",
  "description": "A long-barrelled gun. Reaches the whole field with lighter hits.",
  "stats": { "power.weapon": 2 },
  "abilities": ["quick-shot", "heavy-shot"],
  "tags": ["gun", "ranged"],
  "icon": "flintlock"
}
```

`items/ember-circlet.json`
```json
{
  "version": 1,
  "id": "ember-circlet",
  "name": "Ember Circlet",
  "slot": "crown",
  "kind": "circlet",
  "description": "A thin band of warm bronze. Holds two spells.",
  "stats": { "power.spell": 3 },
  "abilities": ["fire-bolt", "arcane-spark"],
  "tags": ["crown"],
  "icon": "ember-circlet"
}
```

`items/leaping-boots.json`
```json
{
  "version": 1,
  "id": "leaping-boots",
  "name": "Leaping Boots",
  "slot": "boots",
  "kind": "leaping",
  "description": "Sprung soles. Lets you leap two hexes in a straight line.",
  "stats": {},
  "abilities": ["jump"],
  "tags": ["boots"],
  "icon": "leaping-boots"
}
```

`items/blink-boots.json`
```json
{
  "version": 1,
  "id": "blink-boots",
  "name": "Blink Boots",
  "slot": "boots",
  "kind": "blink",
  "description": "Soft slippers stitched with silver. Blink to any tile you can see within three hexes.",
  "stats": {},
  "abilities": ["teleport"],
  "tags": ["boots"],
  "icon": "blink-boots"
}
```

`items/leather-jerkin.json`
```json
{
  "version": 1,
  "id": "leather-jerkin",
  "name": "Leather Jerkin",
  "slot": "armour",
  "kind": "leather",
  "description": "Boiled leather over padding. Turns a little of everything.",
  "stats": { "hp": 8, "defense.weapon": 2, "defense.spell": 2 },
  "abilities": [],
  "tags": ["armour"],
  "icon": "leather-jerkin"
}
```

Parse rules (`ItemDef.FromJson`, all violations are `MapLoadException` with the item id in the message):
`version` must be 1; `id`, `slot`, `kind` required non-empty; `slot` must be one of
`weapon`, `crown`, `boots`, `armour`; `name` defaults to `id`; `description`, `icon` optional;
`stats` required (may be empty object), each value an integer ≥ 0, keys are `hp`, `ap`, or
`power.*` / `defense.*` (use a new `StatBlock.FromJsonLenient`-style parser that does **not** require
`hp` and `ap`: see §4.1); `abilities` required array of unique non-empty strings (may be empty);
`tags` optional, unique.

Link rules (catalogue, each a `ContentError` naming the item file): every ability id exists; no item
ability is in `rules.innateAbilities`; item stat lane keys name declared lanes; a `weapon` item has at
least one ability that is an `AttackDef`. Ids unique within `items/` (use `RegisterId` like the others).

### 3.3 `abilities/` (five new files, three edited)

New files, all `"type": "attack"`, `"category": "weapon"` unless stated, no `tags`:

`abilities/arrow-shot.json`
```json
{
  "version": 1,
  "id": "arrow-shot",
  "name": "Arrow Shot",
  "type": "attack",
  "category": "weapon",
  "icon": "arrow-shot",
  "description": "A quick arrow at any enemy you can see within five hexes.",
  "cost": 1,
  "attack": { "damage": 3, "damageType": "weapon", "range": 5 }
}
```

`abilities/aimed-shot.json`
```json
{
  "version": 1,
  "id": "aimed-shot",
  "name": "Aimed Shot",
  "type": "attack",
  "category": "weapon",
  "icon": "aimed-shot",
  "description": "A drawn, steady shot. Twice the cost, twice the arrow.",
  "cost": 2,
  "attack": { "damage": 6, "damageType": "weapon", "range": 5 }
}
```

`abilities/quick-shot.json`
```json
{
  "version": 1,
  "id": "quick-shot",
  "name": "Quick Shot",
  "type": "attack",
  "category": "weapon",
  "icon": "quick-shot",
  "description": "A snap shot from the hip. Long reach, light hit.",
  "cost": 1,
  "attack": { "damage": 2, "damageType": "weapon", "range": 7 }
}
```

`abilities/heavy-shot.json`
```json
{
  "version": 1,
  "id": "heavy-shot",
  "name": "Heavy Shot",
  "type": "attack",
  "category": "weapon",
  "icon": "heavy-shot",
  "description": "A braced, full-charge shot across the whole field.",
  "cost": 2,
  "attack": { "damage": 5, "damageType": "weapon", "range": 7 }
}
```

`abilities/arcane-spark.json`
```json
{
  "version": 1,
  "id": "arcane-spark",
  "name": "Arcane Spark",
  "type": "attack",
  "category": "spell",
  "icon": "arcane-spark",
  "description": "A cheap flick of raw magic at a nearby foe.",
  "cost": 1,
  "attack": { "damage": 2, "damageType": "spell", "range": 2 }
}
```

Edits to existing files (change only the listed keys, keep everything else byte-identical):

- `abilities/fire-bolt.json`: `"damageType": "magic"` → `"damageType": "spell"`; **remove** the
  `"tags": ["fire"]` line (elements are out of scope; the `tags` field itself stays supported).
- `abilities/jab.json`: `"damageType": "melee"` → `"damageType": "weapon"`.
- `abilities/strike.json`: `"damageType": "melee"` → `"damageType": "weapon"`.
- `move.json`, `jump.json`, `teleport.json`: unchanged.

Jab and Strike stay in the folder although no item references them (a deferred blade weapon will). The
catalogue allows unreferenced abilities.

### 3.4 `modifiers/` (two edits)

- `modifiers/stone-skin.json`: `"damageTypes": ["melee"]` → `"damageTypes": ["weapon"]`;
  description → `"Takes 2 less damage from weapon attacks. Hidden until it first changes a hit."`
- `modifiers/ward-of-feathers.json`: `"damageTypes": ["ranged", "magic"]` → `"damageTypes": ["spell"]`;
  description → `"Takes 4 less damage from spell attacks. Hidden until it first changes a hit."`
- `modifiers/high-ground.json`: unchanged.

### 3.5 `classes/` (delete)

Delete `classes/warrior.json`, `classes/mage.json`, `classes/.gitkeep` and the folder. Do it the Unity
way so the `.meta` files go with them (§7.1 step 2). If the Editor is unreachable, delete the three files
and the folder with `git rm -r MimasClient/Assets/_Game/Data/classes` **including** their `.meta` files
in the same `git rm` (this is the one case where removing metas is correct: the assets are gone and
Unity would delete them on next refresh anyway); say so in the commit message.

### 3.6 `terrains.json`, `timecontrols.json`, `maps/`

Unchanged.

### 3.7 Resulting numbers (for the commit message and the docs; not tuned)

Any kit: hp 28, ap 3, Strength 3, Magic 4, armour 2 / 2. Damage per hit against that armour:
arrow-shot 4, aimed-shot 7, quick-shot 3, heavy-shot 6, fire-bolt 8, arcane-spark 4; high ground +2.
A full turn deals about 12 with any kit, so two heroes that stand still trade for about three turns each.

---

## 4. Core changes (`shared/Mimas.Core/Runtime`)

Write each class in the style of the existing ones: XML doc comment on the type, `MapLoadException` for
parse errors, `MapJson` helpers (`Require`, `RequireString`, `RequireInt`, `OptionalString`,
`OptionalInt`, `RequireStringList`, `OptionalStringList` in `Data/MapData.cs`, `internal static class
MapJson`), `IContentDef` for anything that goes in a `DefinitionTable<T>`.

### 4.1 `Data/StatBlock.cs`

Add, keeping everything that exists:

```csharp
/// <summary>An empty block: every stat reads as 0.</summary>
public static readonly StatBlock Empty = new StatBlock(new List<KeyValuePair<string, int>>());

/// <summary>Key-wise sum. Keys present in either block appear in the result.</summary>
public StatBlock Add(StatBlock other)
{
    if (other == null) throw new ArgumentNullException(nameof(other));
    var merged = new List<KeyValuePair<string, int>>(_entries);
    for (int i = 0; i < other._entries.Count; i++)
    {
        string key = other._entries[i].Key;
        int value = other._entries[i].Value;
        int at = -1;
        for (int j = 0; j < merged.Count; j++) if (merged[j].Key == key) { at = j; break; }
        if (at < 0) merged.Add(new KeyValuePair<string, int>(key, value));
        else merged[at] = new KeyValuePair<string, int>(key, merged[at].Value + value);
    }
    return new StatBlock(merged);
}

/// <summary>
/// Parses a partial stat object (an item's stats): same key rules as <see cref="FromJson"/> but
/// <c>hp</c> and <c>ap</c> are optional, and every value must be ≥ 0 when <paramref name="nonNegative"/>.
/// </summary>
internal static StatBlock FromJsonPartial(JObject stats, string where, bool nonNegative)
```

Refactor so `FromJson` (required hp ≥ 1, ap ≥ 0) and `FromJsonPartial` share one private key-validating
loop. Update the type's doc comment: it is now "a stat table authored in `rules.json` `baseStats` and
`items/*.json` `stats`", not "classes".

### 4.2 `Data/RulesDef.cs`

Add properties and constructor parameters:

```csharp
/// <summary>Stats every hero starts with before gear (rules.json baseStats).</summary>
public StatBlock BaseStats { get; }

/// <summary>Ability ids every hero has regardless of gear (rules.json innateAbilities), authored order.</summary>
public IReadOnlyList<string> InnateAbilityIds { get; }

public RulesDef(List<string> damageTypes, List<string> globalModifierIds, StatBlock baseStats, List<string> innateAbilityIds)
```

`FromJson`: `baseStats` via `MapJson.Require(root, "baseStats", "rules") is JObject` then
`StatBlock.FromJson(obj, "rules.baseStats")`, then reject any negative value with
`MapLoadException("rules.baseStats['<key>'] must not be negative.")`. `innateAbilities` via
`MapJson.RequireStringList`; reject empty, empty strings, and duplicates.

### 4.3 `Data/ItemSlots.cs` (new)

```csharp
namespace Mimas.Core.Data
{
    /// <summary>The four equipment slots, in the order a loadout lists them (design: #equipment).</summary>
    public static class ItemSlots
    {
        public const string Weapon = "weapon";
        public const string Crown = "crown";
        public const string Boots = "boots";
        public const string Armour = "armour";

        /// <summary>Slot order used everywhere a loadout is enumerated.</summary>
        public static readonly string[] All = { Weapon, Crown, Boots, Armour };

        public static bool IsKnown(string slot) => slot == Weapon || slot == Crown || slot == Boots || slot == Armour;

        /// <summary>Index into <see cref="All"/>, or -1.</summary>
        public static int IndexOf(string slot) { for (int i = 0; i < All.Length; i++) if (All[i] == slot) return i; return -1; }
    }
}
```

### 4.4 `Data/ItemDef.cs` (new)

```csharp
public sealed class ItemDef : IContentDef
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string Slot { get; }          // one of ItemSlots
    public string Kind { get; }          // free string: bow, gun, circlet, leaping, blink, leather
    public StatBlock Stats { get; }      // never null; may be empty
    public IReadOnlyList<string> AbilityIds { get; }   // authored order, unique
    public IReadOnlyList<string> Tags { get; }         // sorted, unique (same treatment as AttackDef.Tags)
    public string Icon { get; }
    public ItemDef(string id, string name, string description, string slot, string kind, StatBlock stats,
                   List<string> abilityIds, List<string> tags, string icon)
    public static ItemDef FromJson(string json)
    public bool HasTag(string tag)
}
```

`FromJson` mirrors `ClassDef.FromJson` (which you are deleting: copy its ability-array loop and error
wording, replacing "class" with "item"). Error message examples the tests will assert on (use these
strings): `item 'x' has unknown slot 'hat' (expected weapon, crown, boots or armour).`,
`item 'x'.stats['hp'] must not be negative.`, `item 'x' lists ability 'move' twice.`

### 4.5 `Data/ClassDef.cs`

Delete the file. Grep `shared/`, `server/`, `MimasClient/Assets/_Game` for `ClassDef`, `Classes`,
`ClassId`, `ClassesFolder`, `classFiles` and fix every hit (the list at the time of writing: `ContentCatalog.cs`,
`Unit.cs`, `MatchSetup.cs`, `MatchState.cs`, `PlayerView.cs`, `Program.cs`, `ContentBootstrap.cs`,
`LocalMatchSession.cs`, `MatchSettings.cs`, tests `CombatTests.cs`, `ContentTests.cs`).

### 4.6 `Content/ContentCatalog.cs`

- Constants: remove `ClassesFolder`; add `public const string ItemsFolder = "items/";`.
- Property: remove `Classes`; add `public DefinitionTable<ItemDef> Items { get; }`. Keep constructor
  parameter order otherwise; update the private constructor and the final `new ContentCatalog(...)` call.
- Phase 1: replace the `classes` branch with an `items` branch (`ItemDef.FromJson`, `RegisterId(itemFiles,
  def.Id, path, "item", errors)`). Update the "unrecognised content file" message to list
  `abilities/, items/, maps/ or modifiers/`.
- Phase 2, under `if (rules != null)`:
  - replace the per-class stat-lane check with the same loop over `items` (message:
    `item '{id}' stat '{key}' uses undeclared damage type '{type}'.`) **and** over `rules.BaseStats`
    (message: `rules.baseStats '{key}' uses undeclared damage type '{type}'.`, file `rules.json`).
  - after `abilityIds` / `movementIds` are built: check `rules.InnateAbilityIds` (unknown id →
    `rules.json: rules reference unknown innate ability '{id}'.`; none is a movement →
    `rules.json: rules.innateAbilities must include at least one movement ability (usually 'move').`).
- Phase 2, replace the per-class ability loop with the per-item loop:
  - unknown ability → `item '{id}' references unknown ability '{abilityId}'.`
  - ability is innate → `item '{id}' grants '{abilityId}', which is already innate (rules.innateAbilities).`
    (only when `rules != null`)
  - `Slot == weapon` and no `AttackDef` among its abilities → `item '{id}' is a weapon but grants no attack ability.`
- Items are optional: a catalogue with zero items loads (the `Minimal()` fixture has none).
- Add a public helper used by `MatchState` and the client:

```csharp
/// <summary>The item, or throws ArgumentException naming the slot when the id is unknown or in the wrong slot.</summary>
public ItemDef GetItemForSlot(string slot, string itemId)
```

### 4.7 `Match/Loadout.cs` (new)

```csharp
namespace Mimas.Core.Match
{
    /// <summary>One item id per slot, the whole of a player's gear for a session (design: #character, #equipment). Immutable, value-equal.</summary>
    public sealed class Loadout : IEquatable<Loadout>
    {
        public string WeaponId { get; }
        public string CrownId { get; }
        public string BootsId { get; }
        public string ArmourId { get; }

        public Loadout(string weaponId, string crownId, string bootsId, string armourId)   // each non-empty, else ArgumentException naming the slot

        /// <summary>Ids in <see cref="ItemSlots.All"/> order.</summary>
        public IReadOnlyList<string> ItemIds { get; }

        public string IdForSlot(string slot)   // ArgumentException on unknown slot
        public bool Equals(Loadout other) / override Equals / GetHashCode (ordinal string hashes combined) / ToString() => "longbow / ember-circlet / leaping-boots / leather-jerkin"
    }
}
```

### 4.8 `Match/MatchSetup.cs`

Replace the class-id storage with loadouts:

```csharp
private readonly Loadout[] _loadouts = new Loadout[PlayerCount];
public MatchSetup(string mapId, Loadout player0, Loadout player1, int firstPlayer = 0)
public Loadout LoadoutOf(int player)
```

Keep `MapId`, `FirstPlayer`, `ModifierIdsOf`, `WithModifier`, `CheckPlayer`. Update the doc comment
("each player's loadout").

### 4.9 `Units/Unit.cs`

- Remove `ClassId`. Add:

```csharp
/// <summary>Equipped item ids in slot order (weapon, crown, boots, armour); empty for a bare test unit.</summary>
public IReadOnlyList<string> ItemIds { get; }

/// <summary>The item that granted an ability, or null for an innate ability or an unknown id.</summary>
public string AbilitySourceOf(string abilityId)
```

- Rename `ClasslessStats` → `BareStats` (still 10 hp / 3 ap). Keep the bare constructor
  `Unit(int id, int owner, Hex position)`.
- Replace the `ClassDef` constructor with:

```csharp
/// <summary>A hero built from the rules' base stats and innate abilities plus four items in slot order.</summary>
public Unit(int id, int owner, Hex position, RulesDef rules, IReadOnlyList<ItemDef> items)
```

  Behaviour: `Stats = rules.BaseStats` summed with every `item.Stats` via `StatBlock.Add`;
  `_abilityIds` = innate ids first, then each item's abilities in the given item order; a duplicate id
  anywhere in that sequence is `ArgumentException($"Ability '{id}' is granted twice.")`; keep a private
  `List<string> _abilitySources` parallel to `_abilityIds` (null for innate, item id otherwise);
  `AddAbility` appends a null source; `RemoveAbility` removes both. `Hp = Stats.Hp`, `Ap = 0`.
- Update the class doc comment (no "class").

### 4.10 `Match/MatchState.cs`

In the constructor loop, replace the class lookup with:

```csharp
Loadout loadout = setup.LoadoutOf(player);
var items = new List<ItemDef>(ItemSlots.All.Length);
for (int s = 0; s < ItemSlots.All.Length; s++)
    items.Add(catalog.GetItemForSlot(ItemSlots.All[s], loadout.IdForSlot(ItemSlots.All[s])));
var unit = new Unit(player, player, player == 0 ? MapData.SpawnP1 : MapData.SpawnP2, catalog.Rules, items);
```

`GetItemForSlot` throws `ArgumentException` for unknown id or slot mismatch; the client already catches
`ArgumentException` at setup. Nothing else in `MatchState` references classes.

### 4.11 `Match/PlayerView.cs`

- `KnownEntry`: add `public string SourceItemId { get; }` and constructor `KnownEntry(string id, string
  sourceItemId)`; keep the single-argument constructor for modifiers (source null). The source is set
  even when `Id` is null (hidden) — that is D9.
- `UnitView`: replace `string ClassId` with `IReadOnlyList<string> ItemIds` (constructor parameter in the
  same position). Update the doc comment: "Position, gear and numbers are public; abilities and hidden
  modifiers are not."
- `ViewFor`: build ability entries with `new KnownEntry(known ? id : null, unit.AbilitySourceOf(id))`
  and pass `unit.ItemIds`.
- `EventFilter.cs`: check it does not reference `ClassId` (it should not); no change expected.

### 4.12 Untouched

`Combat/*`, `Movement/*`, `Bots/RandomBot.cs`, `Grid`, `Geometry`, `Rng`, `Content/ContentHash.cs`,
`DefinitionTable.cs`, `TimeControlDef.cs`, `MapData.cs`, `TerrainSet.cs`, `ModifierDef.cs`,
`AbilityDef.cs`, `AttackDef.cs`, `MovementDef.cs`. If you find yourself editing one of these, stop and
re-read §1.

---

## 5. Test fixtures (`shared/Mimas.Core.Tests`) — rewrite, do not delete

The tests build their own content in memory. Keep their arithmetic intact by keeping their lane names
(`melee`, `ranged`, `magic`): the engine is lane-agnostic, only shipped data moves to `weapon`/`spell`.

### 5.1 `CombatTests.cs` → `CombatFixtures.Files()`

Replace `rules.json` with:

```json
{ "version": 1, "damageTypes": [ "melee", "ranged", "magic" ], "globalModifiers": [ "high-ground" ],
  "baseStats": { "hp": 2, "ap": 3 }, "innateAbilities": [ "move" ] }
```

Replace the two `classes/*.json` entries with these items (old class numbers: archer hp 12,
`power.ranged` 2, `defense.ranged` 1, `defense.melee` 0; brute hp 20, `power.melee` 3, `defense.melee` 2,
`defense.ranged` 3; the sums below reproduce them exactly):

```
items/archer-bow.json     { "version": 1, "id": "archer-bow", "slot": "weapon", "kind": "bow",
                            "stats": { "power.ranged": 2, "defense.ranged": 1, "defense.melee": 0 }, "abilities": [ "bow" ] }
items/brute-club.json     { "version": 1, "id": "brute-club", "slot": "weapon", "kind": "club",
                            "stats": { "power.melee": 3, "defense.melee": 2, "defense.ranged": 3 }, "abilities": [ "jab", "strike" ] }
items/bare-crown.json     { "version": 1, "id": "bare-crown", "slot": "crown", "kind": "bare", "stats": {}, "abilities": [] }
items/bare-boots.json     { "version": 1, "id": "bare-boots", "slot": "boots", "kind": "bare", "stats": {}, "abilities": [] }
items/archer-vest.json    { "version": 1, "id": "archer-vest", "slot": "armour", "kind": "vest", "stats": { "hp": 10 }, "abilities": [] }
items/brute-hide.json     { "version": 1, "id": "brute-hide", "slot": "armour", "kind": "hide", "stats": { "hp": 18 }, "abilities": [] }
```

Add to the fixture class:

```csharp
internal static readonly Loadout Archer = new Loadout("archer-bow", "bare-crown", "bare-boots", "archer-vest");
internal static readonly Loadout Brute = new Loadout("brute-club", "bare-crown", "bare-boots", "brute-hide");
internal static MatchSetup Setup() => new MatchSetup("field-3", Archer, Brute);
```

and update the one inline `new MatchSetup("field-3", "archer", "brute", firstPlayer: 1)` (≈ line 232)
to `new MatchSetup("field-3", Archer, Brute, firstPlayer: 1)`. The archer's ability list is
`move, bow` and the brute's is `move, jab, strike`, exactly as before, so `MatchTests` (which asserts 3
enemy abilities at ≈ line 223 and counts legal moves) keep passing. Because the bare boots grant no
movement, the fixture deliberately does not satisfy the *shipped-data* convention; that is fine (D6).

`DataSchemaTests` in the same file:

- `StatBlock_RequiresHpAndAp_AndRejectsUnknownKeys`: port the four `Assert.Throws` and the positive case to
  `RulesDef.FromJson` with a `baseStats` object (`innateAbilities: ["move"]`), e.g.
  `RulesDef.FromJson(@"{ ""version"": 1, ""damageTypes"": [""melee""], ""baseStats"": { ""ap"": 3 }, ""innateAbilities"": [""move""] }")` must throw.
- `Catalogue_RejectsStatKeysAndAttacksWithUndeclaredDamageTypes`: `classes/odd.json` → `items/odd.json`
  (`{ "version": 1, "id": "odd", "slot": "armour", "kind": "odd", "stats": { "power.psychic": 1 }, "abilities": [] }`);
  expected string → `items/odd.json: item 'odd' stat 'power.psychic' uses undeclared damage type 'psychic'`.
- ≈ lines 175–176 (`catalog.Classes.Get("warrior").Stats.Hp` etc.): replace with
  `Assert.Equal(20, catalog.Rules.BaseStats.Hp)` and `Assert.Equal(8, catalog.Items.Get("leather-jerkin").Stats.Hp)`.

### 5.2 `ContentTests.cs`

- `ContentFixtures.Minimal()`: `rules.json` → `{ "version": 1, "damageTypes": [ "melee", "ranged", "magic" ], "baseStats": { "hp": 10, "ap": 3 }, "innateAbilities": [ "move" ] }`; drop `classes/scout.json`.
- `Load_ShippedDataFolder_HasNoErrors`: abilities →
  `aimed-shot, arcane-spark, arrow-shot, fire-bolt, heavy-shot, jab, jump, move, quick-shot, strike, teleport`;
  lanes → `weapon, spell`; replace the classes assertion with items →
  `blink-boots, ember-circlet, flintlock, leaping-boots, leather-jerkin, longbow`; keep the rest.
- `Load_Minimal_Succeeds`: `Assert.Equal(1, catalog.Classes.Count)` → `Assert.Equal(0, catalog.Items.Count)`.
- ≈ line 164 `catalog.Classes.Get("missing")` → `catalog.Items.Get("missing")`.
- `ClassAndUnitTests` → rename `ItemAndUnitTests`:
  - `ClassDef_FromJson_DuplicateAbility_Throws` → `ItemDef_FromJson_DuplicateAbility_Throws` using an item JSON.
  - `Unit_FromClass_StartsWithClassAbilities_AndCanGainMore` → `Unit_FromLoadout_StartsWithInnateThenItemAbilities_AndCanGainMore`:
    build the repo catalogue, the items for `("longbow", "ember-circlet", "leaping-boots", "leather-jerkin")`,
    `new Unit(0, 0, Hex.Zero, catalog.Rules, items)`; assert `ItemIds` equals that list, `AbilityIds` equals
    `move, arrow-shot, aimed-shot, fire-bolt, arcane-spark, jump`, `AbilitySourceOf("move") == null`,
    `AbilitySourceOf("jump") == "leaping-boots"`, then the existing add/remove assertions with `"teleport"`.
  - `Unit_MovementAbilities_ResolveThroughCatalogue`: build the unit from the blink kit
    (`flintlock, ember-circlet, blink-boots, leather-jerkin`); expected movements `move, teleport`.
- Add a helper `internal static Unit HeroFrom(ContentCatalog catalog, Loadout loadout, Hex at)` in
  `ContentFixtures` that resolves items through `GetItemForSlot`; reuse it in the tests above.

### 5.3 `DataTests.cs`, `MatchTests.cs`, `MovementTests.cs`, `HexTests.cs`, `TileMapAndRngTests.cs`

No class references. `MatchTests` uses `CombatFixtures.Setup()` and must pass unchanged once §5.1 is done.
If a `MatchTests` assertion fails, the fixture mapping is wrong; fix the fixture, not the assertion.

---

## 6. New tests (one behaviour each, names exact)

Put content/loader tests in `ContentTests.cs`, unit/state/view tests in `MatchTests.cs` or a new
`LoadoutTests.cs` (preferred: new file, `namespace Mimas.Core.Tests`, `#nullable disable` like the others).

### 6.1 Parsing

- `ItemDef_FromJson_ParsesAllFields` (id, name, slot, kind, description, stats, abilities order, tags sorted, icon).
- `ItemDef_FromJson_UnknownSlot_Throws` (`slot: "hat"` → `MapLoadException` containing `unknown slot 'hat'`).
- `ItemDef_FromJson_NegativeStat_Throws` (`stats: { "hp": -1 }`).
- `ItemDef_FromJson_MissingStats_Throws` and `ItemDef_FromJson_EmptyStats_IsAllowed`.
- `RulesDef_FromJson_RequiresBaseStatsAndInnateAbilities` (missing either → throws; negative base value → throws; duplicate innate → throws).
- `StatBlock_Add_SumsByKey` (`{hp 2} + {hp 8, defense.weapon 2}` = `{hp 10, defense.weapon 2}`; result has no negative floor logic).

### 6.2 Linking

- `Catalogue_ItemWithUnknownAbility_ReportsItemFile` (expect `items/x.json: item 'x' references unknown ability 'nope'`).
- `Catalogue_ItemGrantingInnateAbility_IsAnError` (item lists `move`).
- `Catalogue_WeaponWithoutAttack_IsAnError` (weapon with `abilities: []`).
- `Catalogue_UnknownInnateAbility_IsAnError` and `Catalogue_InnateWithoutMovement_IsAnError` (innate = `["jab"]` only, using the combat fixture's jab).
- `Catalogue_BaseStatsWithUndeclaredLane_IsAnError`.
- `Catalogue_DuplicateItemId_ReportsBothFiles` (mirror the existing duplicate-id test for abilities).

### 6.3 Units, state, view

- `Unit_FromLoadout_StatsAreBasePlusItems` (repo catalogue, bow kit: hp 28, ap 3, `power.weapon` 3, `power.spell` 4, `defense.weapon` 2, `defense.spell` 2).
- `Unit_FromLoadout_DuplicateAbilityAcrossItems_Throws` (two fixture items both granting `bow`).
- `MatchState_LoadoutSlotMismatch_Throws` (`new Loadout("bare-crown", "bare-crown", "bare-boots", "archer-vest")` → `ArgumentException` mentioning `weapon`).
- `MatchState_UnknownItem_Throws`.
- `Loadout_Equality_IsByValue` and `Loadout_EmptyId_Throws`.
- `PlayerView_EnemyItems_AreVisible` (view for player 0 lists the brute's four item ids).
- `PlayerView_EnemyHiddenAbility_KeepsSourceItem` (before any use: the brute's `jab` entry has `Id == null`
  and `SourceItemId == "brute-club"`; the `move` entry has `SourceItemId == null`).
- `PlayerView_OwnAbilities_AreRevealedWithSources`.
- `DamageCalculator_WeaponLane_UsesStrengthAndWeaponDefense`: repo catalogue, bow kit vs gun kit on
  `arena-4`, `arrow-shot` preview = base 3 + power 3 − defense 2 = 4 (use the same call pattern as the
  existing `DamageCalculator` tests in `CombatTests.cs`).
- `DamageCalculator_SpellLane_UsesMagicAndSpellDefense` (`fire-bolt` = 6 + 4 − 2 = 8).
- `RandomBot_TwoDifferentKits_PlaysToCompletion_Deterministically`: extend or copy the existing seeded
  bot-game test to use the bow kit vs the gun kit on the repo data; same seed twice ⇒ identical event
  log (there is an existing same-seed test to copy from).

### 6.4 Shipped-data conventions (this is where D5/D6 live)

- `ShippedItems_WeaponsAndCrownsHaveExactlyTwoAbilities`.
- `ShippedItems_BootsGrantExactlyOneMovementAbility`.
- `ShippedItems_EveryAbilityLaneIsWeaponOrSpell` (every `AttackDef` in the repo catalogue has `damageType` in `{weapon, spell}`; every item and base stat lane key too).
- `ShippedItems_NoItemHasNegativeStats` (belt and braces; the loader already forbids it).

Target: every existing test green or ported as above, **≥ 185 tests** in total (169 now, ≥ 16 new).

---

## 7. Server and client

### 7.1 Order of operations for the Unity part

1. `unity status --format json` → confirm `ready`. `unity command editor_stop` if playing.
2. Delete the classes folder the Unity way, one command at a time:
   `unity command delete_asset --asset "Assets/_Game/Data/classes/warrior.json" --confirm true`, same for
   `mage.json`, `.gitkeep`, then the folder `Assets/_Game/Data/classes`. (§3.5 has the no-Editor fallback.)
3. Write every data file from §3 to disk. Write every C# change from §4 and §7.3–7.5.
4. `unity command menu --path "Assets/Refresh" --timeout 180`; poll `unity command recompile_status` until
   `completed`; then `unity command console` and grep for `error CS`. Fix until clean.
5. Verify the manifest: `unity command console` should show a `[GameDataManifest]` line, or run
   `unity command menu --path "Mimas/Rebuild Game Data Manifest"` and confirm
   `Assets/_Game/Content/GameDataManifest.asset` now lists the six `items/*.json` and no `classes/`
   (`git diff --stat` on the asset is the check; do not edit it).
6. Confirm `Assets/_Game/Settings/DefaultMatchSettings.asset` picked up the new fields: run
   `unity command eval --code "var s = UnityEditor.AssetDatabase.LoadAssetAtPath<Mimas.Client.Presentation.MatchSettings>(\"Assets/_Game/Settings/DefaultMatchSettings.asset\"); return s.PlayerLoadout.Weapon + \" / \" + s.OpponentLoadout.Weapon;"`
   and expect `longbow / flintlock`. If either is empty, set it with `set_serialized_field` (field paths
   `PlayerLoadout.Weapon` etc.) and `save_all`; never edit the `.asset` text.
7. `unity command editor_play`; wait ~5 s; `unity command console` must contain
   `[ContentBootstrap] Content loaded: … 6 items` and `[LocalMatchSession] longbow vs flintlock on arena-4`
   and no `Exception`. Play at least until one attack lands: use `eval` reflection the way earlier
   sessions did (the memory file `unity-cli-quirks` describes it), or simply let the bot act and end
   turns via the HUD's End Turn through `eval` (`UnityEngine.UIElements.UQueryExtensions.Q(root, "end-turn")`).
8. Capture the opponent examine panel: open examine on the bot unit through the session's private
   handler via reflection (as in the HUD session), then
   `unity command capture_game_view --source screen --save_path "Assets/Temp/examine-opponent.png"`;
   copy the PNG to `artifacts/loadout-examine-opponent.png` in the repo, then
   `unity command delete_asset --asset "Assets/Temp/examine-opponent.png" --confirm true` (and the
   `Assets/Temp` folder if you created it). Read the PNG with the Read tool to confirm four item rows and
   `?` ability rows are visible.
9. `unity command editor_stop`. `unity command save_all`. Commit (metas included, `git status` must
   show no stray `.meta` for deleted assets).

### 7.2 `server/Mimas.Server/Program.cs`

`/health` content block: `classes = catalog.Classes.Count` → `items = catalog.Items.Count`. Nothing
else. The csproj already links `Data/**/*.json` recursively, so `items/` is picked up. Run
`dotnet build server/Mimas.Server` (do not need to run it).

### 7.3 `MimasClient/Assets/_Game/Content/ContentBootstrap.cs`

Log line: `Catalog.Classes.Count + " classes, "` → `Catalog.Items.Count + " items, "`.

### 7.4 `MimasClient/Assets/_Game/Presentation/Match/MatchSettings.cs`

Replace the two class strings with:

```csharp
[Serializable]
public sealed class LoadoutSettings
{
    [Tooltip("items/*.json id with slot weapon.")] public string Weapon = "longbow";
    [Tooltip("items/*.json id with slot crown.")] public string Crown = "ember-circlet";
    [Tooltip("items/*.json id with slot boots.")] public string Boots = "leaping-boots";
    [Tooltip("items/*.json id with slot armour.")] public string Armour = "leather-jerkin";

    public Loadout ToLoadout() => new Loadout(Weapon, Crown, Boots, Armour);
    public override string ToString() => Weapon + " / " + Crown + " / " + Boots + " / " + Armour;
}

[Header("Sides (items/*.json ids)")]
public LoadoutSettings PlayerLoadout = new LoadoutSettings();
public LoadoutSettings OpponentLoadout = new LoadoutSettings { Weapon = "flintlock", Boots = "blink-boots" };
```

Put `LoadoutSettings` in its own file `LoadoutSettings.cs` next to `MatchSettings.cs` (same namespace,
same asmdef). `using System;` and `using Mimas.Core.Match;` are needed. Update the class doc comment
("which gear each side wears"). Do not add `[FormerlySerializedAs]` (the old fields are strings, the new
ones are objects; there is nothing to migrate).

### 7.5 `MimasClient/Assets/_Game/Presentation/Match/LocalMatchSession.cs`

- Setup (≈ line 267): `new MatchSetup(_board.MapData.Id, _settings.PlayerLoadout.ToLoadout(),
  _settings.OpponentLoadout.ToLoadout(), _settings.FirstPlayer)`. The `catch` already covers
  `ArgumentException`; `Loadout`'s constructor throws `ArgumentException` for empty ids, so keep the
  `new Loadout` calls inside the `try`.
- Start-up log (≈ line 316): `"[LocalMatchSession] " + _settings.PlayerLoadout.Weapon + " vs " +
  _settings.OpponentLoadout.Weapon + " on " + …`.
- `RefreshExamine` (≈ line 601): remove the `ClassDef` lookup. Set `Title = "Hero"`, `Subtitle`
  unchanged, `Description = null`. Fill a new `examine.Items` list (see 7.6) in slot order: for each
  `unit.ItemIds[i]`, `_catalog.Items.TryGet(id, out def)`; `Name = def.Name`, `Description =
  DescribeItem(def)`, `Icon = def.Icon`, `Hidden = false`. `DescribeItem` returns the item's description
  plus a stat summary on a second line, e.g. `"+8 hp, +2 weapon armour, +2 spell armour"`, built from
  `def.Stats.Entries` with these labels: `hp` → `hp`, `ap` → `ap`, `power.weapon` → `strength`,
  `power.spell` → `magic`, `defense.weapon` → `weapon armour`, `defense.spell` → `spell armour`, anything
  else → the raw key. Skip zero entries.
- Abilities in examine: keep the existing loop but group by source. Build the list in this order: for
  each item in slot order, that item's abilities (using `KnownEntry.SourceItemId`), then innate ones
  (`SourceItemId == null`). Set a new `HudExamineEntry.Group` string to the item name (or `"Innate"`).
  Hidden entries keep `Name = "Unknown ability"` and `Hidden = true`, so an enemy longbow shows two `?`
  rows under "Longbow" (D9).

### 7.6 `IMatchHudSource.cs`, `MatchHudView.cs`, `MatchHud.uxml`, `MatchHud.uss`

- `HudExamineEntry`: add `public string Group;`.
- `HudExamine`: add `public List<HudExamineEntry> Items = new List<HudExamineEntry>();`.
- `MatchHud.uxml`: inside `examine`, after `examine-description` and before the ABILITIES caption, add
  `<ui:Label name="examine-items-caption" class="examine-caption" text="GEAR" />` and
  `<ui:VisualElement name="examine-items" class="examine-abilities" />`. Change the title label's
  placeholder text from `Warrior` to `Hero` and the description placeholder to an empty string.
- `MatchHudView.cs`: query the two new elements in the same place as the others (add them to the null
  check at ≈ line 206), and in `RefreshExamine` call `FillEntries(_examineItems, examine.Items)` before
  the abilities. In `FillEntries`, when `entry.Group` differs from the previous entry's group, insert a
  small caption row (`Label` with class `examine-group`) before the entry. Add to `MatchHud.uss`:

```css
.examine-group { margin: 6px 0 2px 0; font-size: 10px; letter-spacing: 1px; -unity-font-style: bold; color: rgba(255, 255, 255, 0.45); }
```

  Use the same colour variable/value the existing `.examine-caption` uses if it differs; match the
  existing style, do not invent new colours.
- No other HUD change. The action bar, AP dots, preview, flyover and markers are untouched.

---

## 8. Documentation updates

### 8.1 `docs/data.md`

- Folder table: replace the `classes/*.json` row with `items/*.json` ("One item per file: `id`, `slot`,
  `kind`, `stats`, `abilities`, `tags`" / loaded) and add `rules.json` fields `baseStats`,
  `innateAbilities` to its row.
- "The content catalogue" step 2 (Link): replace the class sentences with the item rules from §3.2 and
  the innate rules from §3.1. Step 3 (Tables): `Classes` → `Items`.
- Replace the whole "Class stats (`classes/*.json`, `stats`)" section with two sections:
  "Base stats (`rules.json` `baseStats`)" and "Items (`items/*.json`)", each with one JSON example from
  §3 and the field table. State that a hero's stats are `baseStats` plus the sum of its four items'
  stats, that item stats are never negative, and that Strength / Magic / Armour are display names for
  `power.weapon` / `power.spell` / `defense.*`.
- "Rules (`rules.json`, required)": update the example and text (two lanes, `baseStats`, `innateAbilities`).
- "Attack abilities": example uses `"damageType": "weapon"`, no `tags`; lane list is `weapon, spell`.
- "Modifiers": example `when.damageTypes` → `["spell"]`.
- "Match flow": `MatchState(catalog, MatchSetup, seed)` now "spawns one hero per player from the player's
  `Loadout` (four item ids, slot-checked) on `spawns.p1` / `p2`".
- Every remaining mention of "class", "warrior", "mage", "melee", "ranged", "magic" in this file: grep and
  fix.

### 8.2 `docs/design/index.html`

Edit the HTML directly (it is hand-authored; no build step). For each of these sections set the
`data-impl` attribute on the `<section>` tag and replace or delete the `<div class="drift-note">` block:

| id | new `data-impl` | drift note |
|---|---|---|
| `character` | `implemented` | delete |
| `classes` | `implemented` | replace with `<div class="note"><b>Removed 16 Sep 2026.</b> <code>classes/</code>, <code>ClassDef</code> and <code>MatchSettings.PlayerClassId</code> are gone; loadouts live in <code>MatchSetup</code>.</div>` |
| `stats` | `implemented` | delete |
| `equipment` | `implemented` | (none) |
| `weapon` | `implemented` | delete (blade stays deferred; the table row already says so) |
| `crown` | `implemented` | delete |
| `boots` | `implemented` | delete |
| `armour` | `implemented` | delete |
| `attacks` | `partial` | replace with `<div class="drift-note"><b>Partial.</b> Lanes are weapon / spell; the <code>element</code> field does not exist yet.</div>` |
| `damage` | `partial` | replace with `<div class="drift-note"><b>Partial.</b> Two lanes in code; <code>nullify</code> (immunity) does not exist yet.</div>` |
| `data-model` | `partial` | delete; in its table set the `rules.json` row Code cell to "partial (no elements, no series)", `items/` to "in code", `classes/` to "removed", `abilities/` unchanged |
| `abilities` | `partial` | replace the note text: "Abilities load and resolve and know their source item; enchant overrides do not exist." |

Also: in `#hidden-info` the drift note stays. Add a changelog row
`2026-09-16 · Loadout slice shipped: items replace classes, two lanes, base stats, innate walk, examine shows gear.`
and a decision-log row only if a §11 default became a rule. Do not change any `data-design` value.
After editing, open the file with a quick sanity check: `python -c "import re,sys;s=open('docs/design/index.html',encoding='utf-8').read();ids=set(re.findall(r'id=\"([^\"]+)\"',s));print([h for h in re.findall(r'href=\"#([^\"]+)\"',s) if h not in ids and '$' not in h])"` must print `[]`.

### 8.3 `docs/roadmap.md`

Add an "M3 progress (date)" paragraph modelled on the M1 one (what shipped, test count), and a
"Core test count / runtime" baseline row. Change the "Next" paragraph to point at M2 again.

### 8.4 `docs/decisions.md`

Only if you deviated from §4 in a structural way (then ADR-023, same table format). Otherwise nothing:
ADR-020 already covers this slice.

### 8.5 `CLAUDE.md`

Golden rule 5 lists `(classes, abilities, boons, maps, modifiers)`: change to
`(items, abilities, boons, maps, modifiers, rules)`. Nothing else.

---

## 9. Verification commands (run all before the final commit)

```bash
dotnet build Mimas.sln
dotnet test shared/Mimas.Core.Tests                       # expect >= 185 passed, 0 failed
dotnet build server/Mimas.Server
grep -rn "ClassDef\|ClassId\|Classes\.\|classes/" shared server MimasClient/Assets/_Game --include=*.cs   # expect no hits
grep -rn "melee\|ranged\|magic" MimasClient/Assets/_Game/Data                                     # expect no hits
ls MimasClient/Assets/_Game/Data/items | wc -l           # 12 (6 json + 6 meta) after the Editor refresh
git status --short                                       # no untracked .meta orphans, no Library/ etc.
```

---

## 10. Commit plan (each step green before committing; `git add` only the files you touched)

1. `data: items replace classes; lanes weapon/spell; base stats and innate walk in rules`
   (§3 + the Core parse and link changes needed for the catalogue to load: §4.1–4.6 + §5 fixtures +
   §6.1, §6.2, §6.4 tests). If the Editor is up, do §7.1 steps 1–5 first so the metas land in this commit.
2. `core: loadouts in MatchSetup, heroes built from items, item ids and ability sources in PlayerView`
   (§4.7–4.11 + §6.3 tests).
3. `server: health reports items` (§7.2).
4. `client: match settings hold loadouts; examine shows gear` (§7.3–7.6 + the screenshot in `artifacts/`).
5. `docs: data.md, design page and roadmap for the loadout slice` (§8).

Commit message body: one or two lines on what changed and which §11 defaults were taken, then the
attribution line required by the session's system reminder.

---

## 11. Defaults for forks the session may hit

| If… | Then… |
|---|---|
| The loader "exactly two abilities" rule tempts you | Do not add it to the loader: the in-memory test fixtures need one-ability weapons and empty crowns, and the shipped-data test in §6.4 enforces the convention. This is a deliberate spec choice; mention it in commit 1. |
| A `MatchTests` count assertion fails after the fixture change | The fixture mapping in §5.1 is wrong (ability order or count). Fix the fixture so archer = `move, bow` and brute = `move, jab, strike`. Never edit the assertion. |
| `DefaultMatchSettings.asset` shows empty loadout strings after recompile | Set them with `unity command set_serialized_field` (paths `PlayerLoadout.Weapon`, …) then `save_all`. Never edit the `.asset` text. |
| The Editor is not reachable at all | Do §3 (with the `git rm` fallback for classes and their metas), §4, §5, §6, §7.2, §8; commit 1, 2, 3 and 5 (docs say "client pending"); write the §7.3–7.6 C# anyway (it compiles against Core; you just cannot verify it in the Editor) but keep it uncommitted only if it fails `dotnet build MimasClient`… it will not build outside Unity, so commit it with the note "client: unverified, Editor unavailable". Report precisely what is unverified. |
| A duplicate ability id appears from innate + item at match start | `ArgumentException` from `Unit`; the catalogue already prevents it for shipped data. Do not dedupe silently. |
| You need a rule the design page does not have | Pick the smallest option, add it as a `proposed` bullet in the relevant section with an `<li id="q-…">` open question, continue. |
| `stone-skin` / `ward-of-feathers` on the bot look odd with the new lanes | Keep them; they exist only to exercise the reveal path. |
| The `high-ground` preview line or flyover looks unchanged | Correct; nothing about modifiers changed. |
| Unity reports the `.gitkeep` cannot be deleted or the folder is not empty | Delete the two JSON files first, then the `.gitkeep`, then the folder; on failure fall back to `git rm -r` for the folder including metas and say so. |
| `unity command` times out | Wait 10 s, retry once, then treat the Editor as unreachable (row above). |

---

## 12. Definition of done (copy this list into the final report with ticks)

- [ ] `dotnet build Mimas.sln` and `dotnet test` green, ≥ 185 tests, the content-hash and same-seed determinism tests pass.
- [ ] `classes/` gone (no orphan `.meta`); `items/` has six JSON files (+ metas); `rules.json` has `baseStats` and `innateAbilities`; no `melee`/`ranged`/`magic` anywhere under `Data/`.
- [ ] Core: `ItemDef`, `ItemSlots`, `Loadout`, `RulesDef.BaseStats/InnateAbilityIds`, `Unit.ItemIds/AbilitySourceOf`, `UnitView.ItemIds`, `KnownEntry.SourceItemId`, `ContentCatalog.Items/GetItemForSlot`; no `ClassDef`/`ClassId` left.
- [ ] Server `/health` reports `items`.
- [ ] Local match vs bot plays start to finish with the default loadouts; `unity command console` clean.
- [ ] Opponent examine shows "Hero", four gear rows with stat summaries, and `?` ability rows grouped under their item; screenshot at `artifacts/loadout-examine-opponent.png`.
- [ ] Five commits on `main` as in §10; nothing pushed.
- [ ] `docs/data.md` accurate; design page statuses flipped per §8.2 and link check prints `[]`; roadmap baseline row added; CLAUDE.md rule 5 wording updated.
- [ ] Final report: test count, every §11 default taken, anything unverified or left undone.
