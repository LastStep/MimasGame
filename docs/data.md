# Game data (JSON)

All balance/content data lives in `MimasClient/Assets/_Game/Data/**/*.json`, the single source of truth.
`Mimas.Core.Content.ContentCatalog` loads the whole folder on every peer: the Unity client through a
generated manifest, the server from a copy linked in by its csproj, the tests straight from the repo.
Numbers are integers (no floats in rules).

| File | Contents | Status |
|---|---|---|
| `terrains.json` | Terrain catalogue: `id`, `walkable`, `moveCost`, `modifiers[]` | loaded |
| `rules.json` | Match-wide rules: `damageTypes[]` (the two lanes), `globalModifiers[]` (height advantage lives here as data), `baseStats` (every hero's numbers before gear), `innateAbilities[]` (the walk) | loaded |
| `timecontrols.json` | `{ "version": 1, "timeControls": [ { "id": "3+2", "name", "baseMs", "incrementMs", "turnCapMs" } ] }` | loaded |
| `abilities/*.json` | One ability per file; `type` picks the schema (`movement`, `attack`). Every ability has an AP `cost` (default 1) | loaded |
| `items/*.json` | One item per file: `id`, `slot`, `kind`, `stats`, `abilities`, `tags` | loaded |
| `maps/*.json` | Map: name, hexes[] (`q,r,terrain,height,effect?`), spawns (`p1`,`p2`), symmetry type, ladder position | loaded |
| `modifiers/*.json` | Flat damage modifiers: `trigger`, `when` conditions, `effect.damage`, `visibility`. Attached by terrains, map hexes (`effect`), `rules.globalModifiers` and (later) boons | loaded |
| `boons/*.json` | Boon offers: tier, effects, exclusivity tags | not yet (same) |

## The content catalogue

`ContentCatalog.Load(files)` takes `(path, text)` pairs (`ContentFile`) and returns an immutable catalogue
or throws one `ContentLoadException` listing **every** problem, each tagged with its file. It fails closed:
an unrecognised file or folder is an error, never ignored.

1. **Parse.** Each file is parsed on its own by path: `terrains.json`, `rules.json`, `timecontrols.json`,
   `abilities/`, `items/`, `maps/`, `modifiers/`. Duplicate ids across files are reported with both file names.
2. **Link.** Item ability ids must exist and must not repeat an innate one; an item in the `weapon` slot
   needs at least one attack ability and may grant only `weapon`-category ones, and a `crown` may grant only
   `spell` ones (`#attacks`: the lane and the HUD section must agree); item stat keys must name a lane. `rules.innateAbilities` ids must
   exist and at least one of them must be a movement ability (the walk). A movement's `terrainCosts` keys
   must be real terrains; `rules.baseStats` keys, item stat keys, attack `damageType`s and modifier
   `damageTypes` conditions must name a type in `rules.damageTypes`; terrain `modifiers`, map hex `effect`s
   and `rules.globalModifiers` must be real modifiers; every map must pass `BuildTileMap` (symmetry, spawns,
   connectivity). So a map that would fail at match start fails at boot instead.
3. **Tables.** `Abilities`, `Items`, `Maps`, `TimeControls`, `Modifiers` are `DefinitionTable<T>`: sorted by ordinal id,
   `Get` / `TryGet` / `IndexOf` / `ByIndex`. Indices are stable small integers for wire encoding.
   `Terrains` is the existing `TerrainSet`; `Movements` is the movement subset; `GetMovement(id)`.
4. **Hash.** `Hash` is SHA-256 over path + canonical JSON (sorted keys, no whitespace) of every file, in path
   order. It ignores formatting, key order, line endings and file order, and changes when any value
   changes. Server and client compare it before a match (`/health` already reports it).

Sources: `ContentFiles.FromDirectory(root)` (server, tests) and the Unity `GameDataManifest` asset, which
lists every JSON under `Assets/_Game/Data` as TextAsset references and is regenerated automatically by
`GameDataManifestBuilder` (asset postprocessor, pre-build hook, and `Mimas > Rebuild Game Data Manifest`).
`ContentBootstrap` loads it once per scene; everything else calls `EnsureLoaded()`.

## Conventions

- `id`: lowercase kebab-case, globally unique within its folder (`"fire-bolt"`).
- Every definition has `"version": 1`; bump on breaking schema change and update the loader.
- Effects are a small expression list, e.g. `{ "type": "damage", "amount": 30, "element": "fire" }` — Core has one handler per `type`. Add new types in Core + document here.
- Maps must be symmetrical: the loader validates the declared symmetry (`"symmetry": "rotational-180"` / `"mirror-q"`) and that `p1`/`p2` spawns are at maximal hex distance.

## Editor-time schemas (`tools/schemas/`)

Every data file starts with a `"$schema"` line pointing at a JSON Schema in `tools/schemas/`, and
`.vscode/settings.json` maps the same schemas by glob. Editors then autocomplete fields and flag typos
**while you type**, instead of at boot. Core ignores the key (no loader rejects unknown top-level keys;
`Schemas_AreIgnoredByTheLoader` locks that in), but it *is* part of the content hash, like every other
byte in the file.

The schemas are deliberately stricter than the loader in two places: `additionalProperties: false`, so a
misspelled optional key (`"icons"`, `"minrange"`) is flagged rather than silently ignored, and the damage
lanes are written out as enums (`weapon`, `spell`) with a rule that an attack's `category` must equal its
`attack.damageType`. The lane list really lives in `rules.json` and the engine stays lane-agnostic, so
**a change to `rules.damageTypes` means updating `item.schema.json` and `ability.schema.json` too**.
See `tools/schemas/README.md`.

## Terrains (`terrains.json`)

Maps reference terrain by id; walkability and movement cost live here, not in the map file. Load this
before any map — `MapData.BuildTileMap` needs it to resolve `Tile.Walkable`.

```json
{
  "version": 1,
  "terrains": [
    { "id": "grass", "walkable": true,  "moveCost": 1 },
    { "id": "stone", "walkable": false, "moveCost": 0 }
  ]
}
```

`moveCost` is optional (defaults to 1 for walkable, 0 for unwalkable) and must not be negative. Ids must
be unique and non-empty; the list must not be empty. `modifiers` (optional) lists modifier ids a unit
standing on the terrain carries for combat (see *Modifiers*); the catalogue rejects unknown ids.

## Rules (`rules.json`, required)

```json
{
  "version": 1,
  "damageTypes": ["weapon", "spell"],
  "globalModifiers": ["high-ground"],
  "baseStats": { "hp": 20, "ap": 3, "power.weapon": 1, "power.spell": 1, "defense.weapon": 0, "defense.spell": 0 },
  "innateAbilities": ["move"]
}
```

`damageTypes` are the two damage lanes: every `power.<type>` / `defense.<type>` stat key, every attack's
`damageType` and every modifier `damageTypes` condition must name one, so adding a lane is one line here.
`globalModifiers` apply to every attack (height advantage is the shipped example); they are ordinary
modifier ids. `baseStats` and `innateAbilities` are both required and are described below.

## Base stats (`rules.json`, `baseStats`)

```json
{ "hp": 20, "ap": 3, "power.weapon": 1, "power.spell": 1, "defense.weapon": 0, "defense.spell": 0 }
```

Every hero starts from the same block; gear is what makes them different. `hp` (at least 1) and `ap`
(0 or more, action points granted every turn) are required; every other key must be `power.<type>` or
`defense.<type>` naming a declared lane. No value may be negative. Missing keys read as 0; unknown keys
are load errors.

A hero's numbers are `baseStats` **plus the sum of its four items' `stats`** (`StatBlock.Add`), so the
shipped bow kit is hp 28, ap 3, `power.weapon` 3, `power.spell` 4, `defense.weapon` 2, `defense.spell` 2.
Strength, Magic and Armour are the display names for `power.weapon`, `power.spell` and `defense.*`;
Core only knows the keys. Stats are public knowledge (gear is visible), so the opponent's defence appears
in your damage preview. See ADR-016 for the AP economy and ADR-017 for the damage formula.

`innateAbilities` are the ability ids every hero has whatever it wears — the walk, today `["move"]`. The
list must be non-empty, every id must exist, at least one must be a movement ability, and no item may
grant an id that is already innate.

## Items (`items/*.json`)

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

| Field | Required | Meaning |
|---|---|---|
| `slot` | yes | `weapon`, `crown`, `boots` or `armour`. One item per slot, all four mandatory. |
| `kind` | yes | Free string naming the family (`bow`, `gun`, `circlet`, `leaping`, `blink`, `leather`); boons match on it. |
| `stats` | yes (may be `{}`) | Added to the base block. Keys as in `baseStats`; **never negative** (the loader rejects it). |
| `abilities` | yes (may be `[]`) | Ability ids this item grants, in HUD order. Unique, must exist, must not be innate. A `weapon` needs at least one attack. |
| `tags` | no | Free strings modifiers match on. Unique. |
| `name` / `description` / `icon` | no | Presentation. `name` defaults to the id. |

Items are pure additions: no negative stats, no set bonuses. Every shipped weapon and crown carries
exactly two abilities, boots one movement mode and armour none — an authoring convention enforced by a
repo-data test (`ShippedItemConventionTests`), not by the loader, because in-memory test fixtures need
smaller items.

## Ability cost (every ability type)

`cost` (optional, default 1, 0 or more) is the action points one use spends. An ability can be used as
often as the unit's remaining AP allows; there is no per-turn use limit besides AP. Unspent AP is lost at
end of turn.

## Attack abilities (`abilities/*.json`, `"type": "attack"`)

```json
{
  "version": 1, "id": "fire-bolt", "name": "Fire Bolt", "type": "attack", "category": "spell",
  "icon": "fire-bolt", "cost": 2,
  "attack": { "damage": 6, "damageType": "spell", "range": 3, "minRange": 1 }
}
```

| Field | Required | Meaning |
|---|---|---|
| `category` | yes | `weapon` or `spell` (never `movement`). Picks the action-bar section, and **must equal `attack.damageType`** — the catalogue enforces it through the granting item's slot, a repo-data test checks it across the shipped catalogue, and the schema flags it as you type. |
| `attack.damage` | yes, 0 or more | Flat base damage. |
| `attack.damageType` | yes | The lane: `weapon` or `spell` (one of `rules.damageTypes`). Selects `power.<type>` and `defense.<type>` and is matched by modifiers. |
| `attack.range` | yes, 1 or more | Maximum hex distance to the target. |
| `attack.minRange` | no, default 1 | Minimum hex distance (`1..range`). |
| `tags` | no | Free strings (weapon kinds, later elements) for modifiers to match. Unique. |

Targeting (Core, `Mimas.Core.Combat.AttackTargeting`): the target hex must hold a living enemy unit inside
the range band, and the line of sight (`LineOfSight.IsClear`, the teleport rule) must be clear, always,
even at range 1. `Enumerate` and `Check` agree by construction.

Damage (`Mimas.Core.Combat.DamageCalculator`):

```
damage = max(0, base + power.<type> - defense.<type> + sum of flat modifiers)
```

The result is a `DamageBreakdown`: a fixed-order list of signed lines (base, attacker power, target
defense, then modifiers grouped attacker unit / attacker tile / globals for `dealDamage`, target unit /
target tile / globals for `takeDamage`, each group sorted by id) plus the total. The same function computes
the **preview** (`Knowledge.For(player)`: hidden enemy modifiers are dropped and counted in `UnknownCount`)
and the **actual** result (`Knowledge.Full`). When an actual result contains a hidden line, the owner's
opponent learns it (`ModifierRevealedEvent`) and it appears in every later preview.

## Modifiers (`modifiers/*.json`)

```json
{
  "version": 1, "id": "ward-of-feathers", "name": "Ward of Feathers",
  "description": "Takes 4 less damage from spell attacks.",
  "trigger": "takeDamage",
  "when": { "damageTypes": ["spell"] },
  "effect": { "damage": -4 },
  "visibility": "hidden"
}
```

| Field | Required | Meaning |
|---|---|---|
| `trigger` | yes | `dealDamage` (consulted on the attacker's side: its unit, its tile, globals) or `takeDamage` (the target's side). |
| `when` | no | All conditions must hold. `damageTypes[]` (attack type is one of), `tags[]` (attack has any of), `heightAdvantage` (attacker's tile is higher than the target's). Unknown keys are errors. |
| `effect.damage` | yes, not 0 | Flat amount added to the damage. Only `damage` exists today; other keys are errors. |
| `visibility` | no, default `public` | `hidden` modifiers are unknown to the opponent until they first change a result (boons). Tile and global modifiers should stay `public`. |
| `icon` | no | Presentation key, like abilities. |

Who owns a modifier is the attachment, never the file: a unit carries it (`Unit.ModifierIds`, granted by a
boon or, for now, `MatchSetup.WithModifier`), a terrain grants it to whoever stands on it
(`terrains.json` `modifiers`), a single map hex grants it (`maps/*.json` hex `effect`), or it applies to
every attack (`rules.globalModifiers`). The same id twice on one unit does not stack.

## Match flow (Core, `Mimas.Core.Match`)

`MatchState(catalog, MatchSetup, seed)` builds the map, spawns one hero per player from that player's
`Loadout` (four item ids, each checked against its slot) on `spawns.p1` / `p2`, then `Start()` begins turn 1. Commands: `MoveCommand`, `AttackCommand`, `EndTurnCommand` (reason
`Player` or `Timeout`; the server submits the latter when the clock runs out, so replays need no clock).
`Validate` is pure; `Apply` is the only mutator and returns `MatchEvent`s; `EnumerateLegal` lists every
legal command (bots, highlights). Each turn refreshes the active player's units to `stats.ap`; a unit at 0
hp is dead, stops occupying its tile, and a player with no living unit loses (`MatchEndedEvent`). Clients
only ever see `MatchState.ViewFor(player)` (`PlayerView`) and events passed through `EventFilter`.

## Tile height (`maps/*.json`, `height`)

Each hex may carry an integer `height` (default 0, negatives allowed). Height is a rules value, not a
visual one: walking may rise at most `maxClimb` per step, a jump is blocked by anything more than
`jumpHeight` above the tile the unit *stands on*, and line of sight is blocked by tiles taller than both
endpoints. Descending is never limited. Keep heights rotationally symmetric like terrain (the loader does
not enforce this yet; `Arena4_HeightsAreRotationallySymmetric` in the tests does for arena-4).

## Movement abilities (`abilities/*.json`, `"type": "movement"`)

```json
{
  "version": 1,
  "id": "jump",
  "name": "Jump",
  "type": "movement",
  "description": "Leap straight to a tile up to two hexes away.",
  "icon": "jump",
  "movement": {
    "mode": "jump",
    "range": 2,
    "jumpHeight": 1,
    "maxClimb": 1,
    "ignoreHeight": false,
    "requiresLineOfSight": false,
    "terrainCosts": { "mud": 1, "stone": null }
  }
}
```

Fields shared by every ability type: `id`, `type` (required); `name` (defaults to `id`), `description`,
`icon` and `category` (optional). `icon` is a presentation key the client resolves to a sprite
(`Art/UI/Icons/`); Core stores it untouched and a blank string reads as absent. The HUD falls back to the
first letter of `name` when no sprite matches, so a missing icon is never a load error. `category` is the
HUD group: `movement`, `weapon` or `spell` (`AbilityCategories`). Movement abilities are always
`movement` (declaring anything else is a load error); every other type must declare one. Rules never
read it; it only decides which section of the action bar an ability sits in.

| Field | Required | Meaning |
|---|---|---|
| `mode` | yes | Resolver key: `walk`, `jump`, `teleport`. New modes = one `IMovementResolver` + one `Register` line; unknown modes fail closed. |
| `range` | yes, ≥ 0 | `walk`: movement points spent on terrain cost. `jump` / `teleport`: maximum hex distance. |
| `maxClimb` | no, default 1 | `walk`: maximum height gained per step. |
| `jumpHeight` | no, default 1 | `jump`: how far above the origin tile the landing tile and every tile the straight line grazes may rise. |
| `ignoreHeight` | no, default false | Skip every height rule (flight, phasing). |
| `requiresLineOfSight` | no, default false | Destination must be visible from the origin (`LineOfSight.IsClear`). Balance lever for teleports. |
| `terrainCosts` | no | Per-terrain override of `terrains.json`: an integer ≥ 1 makes the terrain enterable at that cost (even unwalkable terrain); `null` makes it impassable (even walkable terrain). Terrains not listed use the catalogue. "Fly" is a `walk` whose table says everything costs 1 plus `ignoreHeight`. |

Rules shared by every mode: the destination must exist, be enterable and unoccupied; the origin is never a
destination. `walk` also refuses to cross occupied tiles. `jump` and `teleport` cross anything, including
holes in the map, and enter only their destination.

Loading: through the catalogue (`catalog.GetMovement(id)` / `catalog.Movements`). `MovementDef.FromJson`
and `AbilityDef.FromJson` exist for single files and throw `MapLoadException`. A `MovementDef` is an
`AbilityDef` with `type: "movement"`. Walking is innate: `rules.innateAbilities` lists `move`, so every
hero has it whatever it wears, and the catalogue rejects an innate list with no movement ability. Boots
add the second mode (`jump` or `teleport`).

Querying (Core, `Mimas.Core.Movement`): build a `MovementContext(map, terrains, occupancy, origin, def)`,
then `MovementResolverRegistry.CreateDefault().Enumerate(ctx)` for every legal `MovePlan` (UI highlight,
bot move generation) or `.Validate(ctx, destination)` for one (`MoveResult` with a `MoveRejectReason`).
The two always agree. A `MovePlan` carries `Path` (what to animate), `EnteredTiles` (what tile effects fire
for: every tile for a walk, only the destination for a leap or blink), `Traversal` and `Cost`.

## Loading and validation

`Mimas.Core.Data` loads both files. Every schema or invariant violation throws `MapLoadException` —
never a raw `JsonException`, never a silent default.

- `TerrainSet.FromJson(json)` → `Get(id)` (throws on unknown), `TryGet(id, out def)`, `IsWalkable(id)`, `All`.
- `MapData.FromJson(json)` parses the schema only: `version` must be `1`, `id` / `name` / `symmetry` /
  `ladderPosition` / `spawns.p1` / `spawns.p2` / `hexes` are required, `height` and `effect` are optional,
  and duplicate `q,r` pairs are rejected.
- `MapData.BuildTileMap(terrains)` resolves terrain to `Tile.Walkable` (unknown terrain id ⇒ throw) and then
  enforces the design invariants:
  1. **Symmetry.** Only `"rotational-180"` is implemented (`"mirror-q"` throws "unsupported symmetry").
     Every hex needs a 180° twin *with the same terrain*, not just the same shape.
  2. **Spawns.** Both are on the map, both are walkable, and no pair of walkable tiles is further apart
     than the spawn pair.
  3. **Connectivity.** Every walkable tile is reachable from `p1` over walkable tiles.

Validation runs on `BuildTileMap`, not on `FromJson`, so an authoring tool can parse a half-finished map.

`TileMap.FindPath(start, goal)` gives the deterministic shortest walkable route (inclusive of both ends,
neighbours expanded in `Hex.Directions` order); it returns `null` if either hex is off the map, the goal is
unwalkable, or no route exists. The start tile's own walkability is not required — a unit may be standing
on blocking terrain.

## Example map (ring of radius 3 with a hole in the middle)

```json
{
  "version": 1,
  "id": "ring-3",
  "name": "The Ring",
  "symmetry": "rotational-180",
  "spawns": { "p1": { "q": -3, "r": 0 }, "p2": { "q": 3, "r": 0 } },
  "hexes": [
    { "q": -3, "r": 0, "terrain": "stone" },
    { "q": 3, "r": 0, "terrain": "stone" }
  ]
}
```

(Generate full rings with `Hex.Ring(center, radius)` in an editor tool rather than typing hexes by hand.)

Every shipped map must pass every check, because the catalogue validates all of them at boot (the old
all-stone `ring-3.json` sample was removed for that reason). `board-3.json` and `arena-4.json` are playable. `arena-4.json` also
exercises height: the six inner pillars are height 2 (a `jumpHeight: 1` jump cannot clear them), the six
outer pillars height 1 (jumpable), and the centre row `(-1,0)…(1,0)` is a height-2 plateau with height-1
ramps at `(±2,0)`.
