# Game data (JSON)

All balance/content data lives in `MimasClient/Assets/_Game/Data/**/*.json`, the single source of truth.
`Mimas.Core.Content.ContentCatalog` loads the whole folder on every peer: the Unity client through a
generated manifest, the server from a copy linked in by its csproj, the tests straight from the repo.
Numbers are integers (no floats in rules).

| File | Contents | Status |
|---|---|---|
| `terrains.json` | Terrain catalogue: `id`, `walkable`, `moveCost` | loaded |
| `timecontrols.json` | `{ "version": 1, "timeControls": [ { "id": "3+2", "name", "baseMs", "incrementMs", "turnCapMs" } ] }` | loaded |
| `abilities/*.json` | One ability per file; `type` picks the schema. `movement` is implemented; attacks etc. will add `targeting`, `cost`, `effects[]`, `hidden` | loaded |
| `classes/*.json` | One class per file: `id`, `name`, `description`, `abilities` (ids). Base stats join with the unit model | loaded |
| `maps/*.json` | Map: name, hexes[] (`q,r,terrain,height,effect?`), spawns (`p1`,`p2`), symmetry type, ladder position | loaded |
| `modifiers/*.json` | Status effects / passives: duration, stat deltas, triggers | not yet (folder must stay empty or the catalogue rejects it) |
| `boons/*.json` | Boon offers: tier, effects, exclusivity tags | not yet (same) |

## The content catalogue

`ContentCatalog.Load(files)` takes `(path, text)` pairs (`ContentFile`) and returns an immutable catalogue
or throws one `ContentLoadException` listing **every** problem, each tagged with its file. It fails closed:
an unrecognised file or folder is an error, never ignored.

1. **Parse.** Each file is parsed on its own by path: `terrains.json`, `timecontrols.json`, `abilities/`,
   `classes/`, `maps/`. Duplicate ids across files are reported with both file names.
2. **Link.** Class ability ids must exist; a class needs at least one movement ability; a movement's
   `terrainCosts` keys must be real terrains; every map must pass `BuildTileMap` (symmetry, spawns,
   connectivity). So a map that would fail at match start fails at boot instead.
3. **Tables.** `Abilities`, `Classes`, `Maps`, `TimeControls` are `DefinitionTable<T>`: sorted by ordinal id,
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
be unique and non-empty; the list must not be empty.

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
`AbilityDef` with `type: "movement"`. Units get their movement ids from their class (`classes/*.json`
`abilities`), so "every unit can move one tile" is simply every class listing `move`; the catalogue
rejects a class with no movement ability.

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
