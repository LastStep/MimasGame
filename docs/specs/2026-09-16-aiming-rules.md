# Spec A: Aiming rules (height ray sight, trajectories, circular ranges, props)

_Work order for one autonomous Claude Code session (executing model: Claude Opus). Written 16 Sep 2026
from a question round with Rohan, after a research pass (BattleTech / ASL level rules, GIS viewshed
line of sight, BG3 / Into the Breach arcs, integer parabola sampling). Design source of truth:
`docs/design/index.html` (anchors cited as `#…`). This spec never overrides the design page; if the two
disagree, the page wins and the disagreement is a bug in this file. Everything here was checked against
the code on 16 Sep 2026 (commit `53bfd60`, 203 Core tests); line numbers are approximate, names are
exact. **Spec B** (`2026-09-16-aiming-client.md`) is the Unity half and starts only after this spec's
commits are on `main`._

---

## 0. How to run this session

Read this whole file before touching anything. Then read `CLAUDE.md` (golden rules 1–6, 11) and
`docs/data.md`. Then read these sections of the design page, in this order: `#how-to-use`, `#board`,
`#line-of-sight`, `#maps`, `#attacks`, `#damage`, `#modifiers`, `#hidden-info`, `#determinism`,
`#data-model`. Open the HTML in a browser or grep it by `id="…"`.

Rules for the session:

1. **Autonomous.** Do not ask questions. Every fork you might hit has a default in §11; take it, mention
   it in the commit message, and if it is a rule (not a number) add it to the design page as a
   `proposed` bullet with an open question.
2. **Commits:** small commits straight to `main`, message format `area: what` (§10). Do not push. Run
   `dotnet build Mimas.sln` and `dotnet test shared/Mimas.Core.Tests` before every commit; both green.
3. **Prerequisites:** `dotnet test shared/Mimas.Core.Tests` is green with 203 tests at start. **No Unity
   Editor is needed for this spec.** The only Unity-side action is §7.3 (a refresh so the new JSON files
   get `.meta` files); if no Editor is `ready`, commit without the metas and say so in the report.
4. **Never** create, edit or delete `*.meta` files by hand. Do not touch `MimasClient/ProjectSettings/**`,
   `Packages/manifest.json`, `packages-lock.json`, any `.unity` / `.prefab` / `.asset`.
5. **Core stays pure:** netstandard2.1, C# 9 (no `record`, no `required`, no file-scoped namespaces), no
   `UnityEngine`, **no floats or doubles in rules** (every test in this spec is integer
   cross-multiplication with `long` intermediates), no `DateTime.Now`, no Dictionary iteration order
   dependence, no LINQ in hot paths.
6. **Fail closed:** any content problem is a `MapLoadException` at parse time or a `ContentError` at link
   time with the file name. Unknown trajectory modes throw. Never a silent default.
7. **No scope creep.** Out of scope (do not start, do not stub, do not add TODO comments for): any Unity
   code (Spec B), enchant / boon overrides of trajectory (only the seam in §4.3), area or multi-target
   shapes, owned props, rubble, units-as-cover penalties, a beam trajectory, elements, the network
   session, bot improvements, balance passes.
8. If `unity command` is used at all (§7.3): `export MSYS_NO_PATHCONV=1` first; never two Editor commands
   concurrently; on timeout wait 10 s, retry once, then give up on the refresh and report it.

---

## 1. Goal and result

Attacks aim at a fixed point on the target's body and are resolved against **tile heights in body
units**. An attack declares whether it needs **line of sight** (a straight ray from the attacker's aim
point to the target's aim point, blocked by tall terrain and by bodies) and how it **travels**
(`direct`: the same ray; `arc`: an integer parabola with a per-ability apex; `sky`: nothing in between
matters). Ranges become **circular** (Euclidean centre distance, integer-exact) so the client can draw
true circles. **Props** (a destructible pillar, an indestructible wall) are the first non-unit bodies:
they block movement, sight and trajectories with their height, and a prop with health is a legal target
with its own aim point. Arena-4 loses its stone hexes and gets props and steps instead.

When done: `rules.json` carries `heights`; every attack file carries `trajectory` and `lineOfSight`
(and `apex` when `arc`); `props/` has two files; `arena-4.json` has no stone; Core has `HeightsDef`,
`PropDef`, `IBody`, `Prop`, `BodySet`, `Ballistics`, the trajectory resolvers and registry,
`TargetCheck`; `PlayerView` lists props; the server `/health` reports `props`; docs, schemas and the
design page are updated; ≥ 240 tests pass.

---

## 2. Decisions locked in the question round (16 Sep 2026)

| # | Decision | Anchor |
|---|---|---|
| D1 | **Height units.** Map `height` stays an integer *level*. `rules.json` `heights.unitsPerLevel` = 3 converts levels to body units. A hero is `heights.body` = 6 units tall and every attack aims at (and leaves from) `heights.aim` = 4 units above the tile top. So a level-1 step (3) does not block a flat shot between two ground units, a level-2 plateau (6) does. Numbers are placeholders for playtesting. | `#board`, `#line-of-sight` |
| D2 | **Muzzle = aim point.** The ray runs from `attackerTile·L + attacker.AimHeight` to `targetTile·L + target.AimHeight`. Sight stays symmetric between two heroes (if I can see you, you can see me). | `#line-of-sight` |
| D3 | **Sight rule (viewshed).** An intervening column blocks when its top reaches the ray: `top·b ≥ h0·(b−a) + h1·a` for a sample at parameter `a/b`. A graze (`=`) blocks. Endpoint tiles never block. Unwalkable terrain is solid (blocks everything but `sky`). Holes (no tile) never block. | `#line-of-sight` |
| D4 | **Bodies block.** Any living unit or present prop standing on an intervening tile adds a column of `tile·L + BodyHeight`. The attacker and the target never block themselves. | `#line-of-sight` |
| D5 | **Two independent fields.** `attack.lineOfSight` (bool, required): must the attacker *see* the target (D3 ray). `attack.trajectory` (`direct` \| `arc` \| `sky`, required): how the attack *travels*, each with its own blocking rule. Legal iff both pass. | `#attacks`, `#trajectories` |
| D6 | **Trajectory rules.** `direct`: the D3 ray. `arc`: integer parabola through the same endpoints whose peak is `max(h0,h1) + apex` at mid-flight, sampled per intervening tile, all-integer (§4.7). `sky`: never blocked. `apex` (int ≥ 0) is required iff `arc`. | `#trajectories` |
| D7 | **Shipped values.** Bow (`arrow-shot` apex 3, `aimed-shot` apex 4): `arc`, `lineOfSight` false. Gun (`quick-shot`, `heavy-shot`) and crown spells (`fire-bolt`, `arcane-spark`): `direct`, `lineOfSight` true. `sky` exists in code and test fixtures only. | `#weapon`, `#crown` |
| D8 | **Override seam only.** Resolution reads trajectory, apex, sight and the range band from `AttackDef` through one place (`AttackTargeting.Check`); a test proves that an `AttackDef` cloned with a different trajectory resolves differently. No enchant JSON. | `#abilities` |
| D9 | **Ranges are Euclidean.** In range iff `minRange² ≤ q²+qr+r² ≤ range²` for the offset target − attacker (this is the squared centre distance in units of the centre spacing, integer-exact). Movement stays hex-distance. For ranges ≤ 6 this equals hex distance exactly; from 7 up diagonals reach further. | `#attacks` |
| D10 | **Targets.** Anything with health can be hit; nothing else. Only living enemy units and present, damageable props are legal targets. Ground targeting is not legal (the client only previews it). | `#attacks`, `#props` |
| D11 | **Props.** `props/*.json`: `bodyHeight`, `aimHeight`, optional `stats` (`hp`, `defense.weapon`, `defense.spell`). A prop with `stats.hp` is destructible; without, it is a permanent wall that cannot be hit. Props are neutral, public, occupy their tile while present, block sight and trajectories with their body, and are **removed** when destroyed (tile enterable again). Killing a prop never ends the round. | `#props` |
| D12 | **Map placement.** A map hex may carry `"prop": "<id>"`. The hex terrain must be walkable, the twin under the map symmetry must carry the same prop, spawns carry none. | `#maps` |
| D13 | **High ground** applies to every trajectory (the global modifier is unchanged: attacker tile higher than target tile). | `#damage` |
| D14 | **Arena-4 rework.** All 12 stone hexes go; four wall props, four pillar props, two extra height-1 steps (§3.4). Board-3 is untouched (its stone hex stays and still blocks, D3). | `#maps` |
| D15 | Facing, previews, circles and projectiles are presentation (Spec B). Nothing about the cursor or the armed ability ever enters Core or the wire. | `#presentation` |
| D16 | Teleport's `requiresLineOfSight` uses the same D3/D4 ray, from the mover's aim point to the destination's aim height, ignoring the mover's own body. | `#movement` |

---

## 3. Data changes (`MimasClient/Assets/_Game/Data/`)

### 3.1 `rules.json` (add one block; everything else unchanged)

```json
{
  "$schema": "../../../../tools/schemas/rules.schema.json",
  "version": 1,
  "damageTypes": ["weapon", "spell"],
  "globalModifiers": ["high-ground"],
  "heights": { "unitsPerLevel": 3, "body": 6, "aim": 4 },
  "baseStats": { "hp": 20, "ap": 3, "power.weapon": 1, "power.spell": 1, "defense.weapon": 0, "defense.spell": 0 },
  "innateAbilities": ["move"]
}
```

`heights` is required. Constraints: `unitsPerLevel ≥ 1`, `body ≥ 1`, `1 ≤ aim ≤ body`.

### 3.2 `abilities/*.json` (every `"type": "attack"` file; movement files untouched)

Add to the `attack` object. `trajectory` and `lineOfSight` are **required** (the schema and the loader
both reject an attack without them; no default). `apex` is required when `trajectory` is `arc` and
forbidden otherwise.

| File | `trajectory` | `apex` | `lineOfSight` | New `description` |
|---|---|---|---|---|
| `arrow-shot.json` | `arc` | 3 | `false` | "A quick arrow lobbed at an enemy within five tiles. It can drop behind cover you cannot see past." |
| `aimed-shot.json` | `arc` | 4 | `false` | "A high, slow arrow that clears taller cover. Two action points." |
| `quick-shot.json` | `direct` | — | `true` | "A straight shot at any enemy you can see within seven tiles." |
| `heavy-shot.json` | `direct` | — | `true` | "A heavy straight shot at any enemy you can see within seven tiles. Two action points." |
| `fire-bolt.json` | `direct` | — | `true` | "A bolt of fire at any enemy you can see within three tiles." |
| `arcane-spark.json` | `direct` | — | `true` | keep the current description |
| `jab.json`, `strike.json` (unreferenced blade files) | `direct` | — | `true` | unchanged |

Example (`arrow-shot.json`):

```json
{
  "$schema": "../../../../../tools/schemas/ability.schema.json",
  "version": 1,
  "id": "arrow-shot",
  "name": "Arrow Shot",
  "type": "attack",
  "category": "weapon",
  "icon": "arrow-shot",
  "description": "A quick arrow lobbed at an enemy within five tiles. It can drop behind cover you cannot see past.",
  "cost": 1,
  "attack": { "damage": 3, "damageType": "weapon", "range": 5, "trajectory": "arc", "apex": 3, "lineOfSight": false }
}
```

Ranges and damage stay as they are (`range` is now a Euclidean radius, D9; for 5 and 3 nothing changes
in practice, for the gun's 7 the diagonal (4,4)-type tiles at hex distance 8 come into range).

### 3.3 `props/` (new folder, two files)

`props/pillar.json`:

```json
{
  "$schema": "../../../../../tools/schemas/prop.schema.json",
  "version": 1,
  "id": "pillar",
  "name": "Stone Pillar",
  "description": "A cracked pillar the height of a hero. It blocks shots and can be brought down.",
  "icon": "pillar",
  "bodyHeight": 6,
  "aimHeight": 3,
  "stats": { "hp": 10, "defense.weapon": 0, "defense.spell": 0 }
}
```

`props/wall.json`:

```json
{
  "$schema": "../../../../../tools/schemas/prop.schema.json",
  "version": 1,
  "id": "wall",
  "name": "Wall",
  "description": "A solid wall the height of a hero. Nothing goes through it and nothing brings it down.",
  "icon": "wall",
  "bodyHeight": 6,
  "aimHeight": 3
}
```

Field rules: `bodyHeight ≥ 1`; `1 ≤ aimHeight ≤ bodyHeight` (required even for walls, so a client can
place a marker); `stats` optional, keys as in `baseStats` minus `ap` (`hp ≥ 1` when present, defenses
≥ 0, unknown keys are errors); no `power.*` on a prop.

### 3.4 `maps/arena-4.json` (rewrite the `hexes` array; header unchanged except `description`)

Keep `id`, `name`, `symmetry: rotational-180`, `ladderPosition: 3`, spawns `p1 (-4,0)`, `p2 (4,0)`.
New description: "Radius-4 hex arena, 61 tiles: a raised central plateau with a step on each side, four
walls and four destructible pillars for cover. Ladder map 3."

All 61 hexes with `|q| ≤ 4, |r| ≤ 4, |q + r| ≤ 4` are `"terrain": "grass"`. Height and prop are the
defaults (0, none) **except**:

| Hexes (q, r) | Height | Prop |
|---|---|---|
| (−1, 0), (0, 0), (1, 0) | 2 | — |
| (−2, 0), (2, 0), (−1, 4), (1, −4) | 1 | — |
| (0, 2), (1, 1), (0, −2), (−1, −1) | 0 | `wall` |
| (2, −1), (−2, 1), (2, 2), (−2, −2) | 0 | `pillar` |

Write the hexes in a stable order: rows `r = −4 … 4`, within a row `q` ascending. Picture (rows by
`r`, `.` ground, `,` height 1, `^` height 2, `W` wall, `P` pillar, `S` spawn):

```
r=-4        . , . . .
r=-3       . . . . . .
r=-2      P . W . . . .
r=-1     . . W . . P . .
r= 0    S . , ^ ^ ^ , . S
r=+1     . . P . . W . .
r=+2      . . . . W . P
r=+3       . . . . . .
r=+4        . . . , .
```

Checked before writing this spec (integer model, D1–D6): rotationally symmetric including props; all
53 standable tiles reachable from p1 with `maxClimb 1`; spawns 8 apart and still at maximal distance;
spawn-to-spawn direct sight is blocked by the plateau; a pillar adjacent to a ground target shields it
from the plateau; with bow apex 3 an arc clears 1536 of the 1980 attacker/target pairs inside the bow
band versus 1064 for a direct shot.

### 3.5 `terrains.json`, `timecontrols.json`, `maps/board-3.json`, `items/`, `modifiers/`

Untouched. `stone` stays in `terrains.json` (board-3 uses it; unwalkable terrain is solid, D3).

### 3.6 `tools/schemas/`

- `ability.schema.json`, attack branch: add to `attack.properties`
  `trajectory: { enum: ["direct","arc","sky"] }`, `apex: { type: integer, minimum: 0 }`,
  `lineOfSight: { type: boolean }`; `required` becomes
  `["damage","damageType","range","trajectory","lineOfSight"]`; add an `allOf` pair: if `trajectory`
  is `arc` then `apex` required, else `apex` must be absent (`"not": { "required": ["apex"] }`).
  Update the `range` / `minRange` descriptions to "Euclidean centre distance in tile spacings (see
  docs/data.md)".
- `map.schema.json`: hex gets `prop: { type: string, pattern: "^[a-z0-9]+(-[a-z0-9]+)*$" }`.
- `rules.schema.json`: add `heights` (required) with the three integer fields and the D1 minimums.
- New `prop.schema.json` mirroring §3.3 (`additionalProperties: false`, `stats` with `hp`,
  `defense.weapon`, `defense.spell` only).
- `tools/schemas/README.md`: add the prop row and the `props/` folder to the mapping table.

### 3.7 Resulting numbers (for the commit message and the docs; not tuned)

Hero body 6, aim 4, level 3. Flat shots between ground units pass over level 1, are stopped by level 2,
walls, pillars and bodies. A bow arc with apex 3 peaks at 7 above the higher tile top… i.e. peak
`max(h0,h1) + 3` above the *aim* heights, so it clears a 6-unit wall from two tiles away but not from
the adjacent tile. Pillar: 10 hp, armour 0/0, so a full turn (≈12 damage) brings one down.

---

## 4. Core changes (`shared/Mimas.Core/Runtime`)

Read every file you touch in full first. Keep the braces namespace style and the `_camelCase` fields.

### 4.1 `Geometry/Hex.cs`

Add:

```csharp
/// <summary>
/// Squared Euclidean distance between two hex centres, in units of the centre-to-centre spacing.
/// For a pointy-top layout the world offset of (dq, dr) has |v|^2 = 3 s^2 (dq^2 + dq dr + dr^2), so this
/// integer is exact and layout-independent. Used for circular range bands (design: #attacks).
/// </summary>
public static int EuclideanSquared(Hex a, Hex b)
{
    int dq = b.Q - a.Q, dr = b.R - a.R;
    return dq * dq + dq * dr + dr * dr;
}
```

### 4.2 `Data/HeightsDef.cs` (new) and `Data/RulesDef.cs`

```csharp
public sealed class HeightsDef
{
    public int UnitsPerLevel { get; }   // >= 1
    public int Body { get; }            // >= 1, hero body height in units above the tile top
    public int Aim { get; }             // 1..Body, where attacks leave from and land
    public HeightsDef(int unitsPerLevel, int body, int aim) { /* validate, throw ArgumentOutOfRangeException */ }
    /// <summary>Top of a tile's terrain column in height units.</summary>
    public int TileTop(Tile tile) => tile.Height * UnitsPerLevel;
    internal static HeightsDef FromJsonAt(JObject obj, string where) { /* MapJson.RequireInt x3, D1 constraints -> MapLoadException */ }
}
```

`RulesDef` gains `public HeightsDef Heights { get; }`, a constructor parameter (after `baseStats`), and
`FromJson` requires the `heights` object. Tests that build a `RulesDef` directly must pass one.

### 4.3 `Data/AttackDef.cs`

- Add `public static class Trajectories { public const string Direct = "direct", Arc = "arc", Sky = "sky"; public static bool IsKnown(string) }`
  (same file or `Data/Trajectories.cs`).
- New properties: `public string Trajectory { get; }`, `public int Apex { get; }` (0 unless `arc`),
  `public bool LineOfSight { get; }`.
- Constructor: add `string trajectory = Trajectories.Direct, bool lineOfSight = true, int apex = 0`
  **after** `minRange` and before `tags` (structural defaults for in-memory fixtures; JSON has none).
  Validate: known trajectory; `apex ≥ 0`; `apex > 0` only when `arc`.
- `FromJson`: `trajectory` via `MapJson.RequireString`, `lineOfSight` via `MapJson.RequireBool`, `apex`
  via `OptionalInt` then: `arc` without `apex` → `MapLoadException("… attack.apex is required for trajectory 'arc'")`;
  `apex` present with another trajectory → `MapLoadException`. Unknown trajectory → `MapLoadException`.
- Replace `InRange(int distance)` with `public bool InRangeSquared(int distanceSquared)`:
  `distanceSquared >= MinRange * MinRange && distanceSquared <= Range * Range`. Fix the doc comments:
  "Euclidean centre distance in tile spacings".
- `public AttackDef WithTrajectory(string trajectory, int apex, bool lineOfSight)` returning a copy with
  the same id, name, numbers and tags: this is the D8 seam and is exercised only by tests.

### 4.4 `Data/PropDef.cs` (new)

```csharp
public sealed class PropDef : IContentDef
{
    public string Id { get; } public string Name { get; } public string Description { get; } public string Icon { get; }
    public int BodyHeight { get; }   // >= 1
    public int AimHeight { get; }    // 1..BodyHeight
    public StatBlock Stats { get; }  // hp 0 when absent
    public bool IsDestructible => Stats.Hp > 0;
    public static PropDef FromJson(string json) { /* version 1, id, name?, description?, icon?, bodyHeight, aimHeight, stats? */ }
}
```

`stats`: parse with `StatBlock.FromJsonAt(obj, where, false)` (check the third parameter's meaning in
`StatBlock.cs` before use: it must *not* require `hp`/`ap`); then reject `ap` and any `power.*` key
with a `MapLoadException`; `hp` when present must be ≥ 1; defenses ≥ 0.

### 4.5 `Data/MapData.cs`

- `MapHex` gains `public string PropId { get; }` (null = none); constructor parameter after `effectId`.
- `FromJson` reads `MapJson.OptionalString(entry, "prop", where)`.
- `ValidateSymmetry` also checks the twin's `PropId` equals (ordinal, both null allowed):
  `"map '{Id}' breaks rotational symmetry: {pos} has prop '{a}' but its twin {twin} has '{b}'"`.
- `ValidateSpawns`: a spawn hex with a prop → `MapLoadException("map '{Id}' spawn p1 {hex} carries a prop")`.
- `BuildTileMap` does **not** need the prop catalogue; walkability is terrain only. Add
  `public void ValidateProps(DefinitionTable<PropDef> props, TerrainSet terrains)`: unknown prop id →
  throw; prop on unwalkable terrain → throw. Called by the catalogue link phase (§4.6).
- `Tile` (Grid/TileMap.cs) is untouched: a prop is a body (§4.8), not a tile attribute.

### 4.6 `Content/ContentCatalog.cs`

- `public const string PropsFolder = "props/";` and `public DefinitionTable<PropDef> Props { get; }`.
- Parse phase: files under `props/` → `PropDef.FromJson`, same error collection as items.
- Link phase: for every map call `ValidateProps` (collect as `ContentError` with the map file name).
- The hash already covers every file; verify the `Files` list includes `props/*` (the existing
  "unknown file is an error" rule means the new folder must be registered in whatever table lists
  known folders; find it by grepping `ItemsFolder`).

### 4.7 `Combat/Ballistics.cs` (new, pure integer math)

```csharp
public static class Ballistics
{
    /// <summary>Height of the straight line from h0 (t=0) to h1 (t=1) at t = a/b, scaled by b.</summary>
    public static long StraightHeightScaled(long h0, long h1, long a, long b) => h0 * (b - a) + h1 * a;

    /// <summary>True when a column of height top does NOT reach the straight line at t = a/b (a graze blocks).</summary>
    public static bool StraightClears(long h0, long h1, long top, long a, long b) => top * b < StraightHeightScaled(h0, h1, a, b);

    /// <summary>
    /// Height of the arc at t = a/b, scaled by b². The arc is the straight line plus a symmetric bump that is
    /// zero at both ends and peaks so the flight reaches max(h0,h1) + apex at t = 1/2:
    ///   height(t) = h0 + (h1-h0) t + (4 apex + 2 |h1-h0|) t (1-t).
    /// </summary>
    public static long ArcHeightScaled(long h0, long h1, long apex, long a, long b)
        => h0 * b * b + (h1 - h0) * a * b + (4 * apex + 2 * Abs(h1 - h0)) * a * (b - a);

    public static bool ArcClears(long h0, long h1, long apex, long top, long a, long b) => top * b * b < ArcHeightScaled(h0, h1, apex, a, b);
}
```

Worked check (put it in a test): `h0 = 4, h1 = 4, apex = 3, b = 6` (three tiles apart, samples at
`a = 1, 3, 5`): heights ×36 = 144 + 12·a·(6−a) → a=1: 204 (5.67), a=3: 252 (7.0), a=5: 204. A wall of 6
at the middle tile (a = 3) clears (216 < 252); at the first tile (a = 1) it blocks (216 ≥ 204).

### 4.8 Bodies: `Units/IBody.cs`, `Units/IBodyLookup.cs`, `Units/Prop.cs`, `Units/BodySet.cs` (new), `Units/Unit.cs`

```csharp
/// <summary>Anything that stands on a tile with a height: blocks movement and sight, may be hit.</summary>
public interface IBody
{
    int Id { get; }                    // unique among all bodies of a match
    int Owner { get; }                 // player index, or -1 for neutral props
    Hex Position { get; }
    int BodyHeight { get; }            // units above the tile top
    int AimHeight { get; }             // where attacks land, units above the tile top
    bool IsAlive { get; }              // unit: hp > 0; prop: not destroyed (walls are always alive)
    bool IsDamageable { get; }         // unit: true; prop: def.IsDestructible
    int Hp { get; } int MaxHp { get; }
    StatBlock Stats { get; }
    IReadOnlyList<string> ModifierIds { get; }   // empty for props
    int TakeDamage(int amount);        // throws InvalidOperationException when !IsDamageable
}

public interface IBodyLookup
{
    IReadOnlyList<IBody> All { get; }                    // units in unit-set order, then props in map order
    bool TryGetBodyAt(Hex hex, out IBody body);          // alive bodies only
    bool TryGetBody(int id, out IBody body);
}
```

- `Unit : IBody`. Constructors: `Unit(int id, int owner, Hex position, HeightsDef heights)` (replaces the
  three-argument one; **every** test call site changes, see §5) and
  `Unit(int id, int owner, Hex position, RulesDef rules, IReadOnlyList<ItemDef> items)` reads
  `rules.Heights`. `BodyHeight => _heights.Body`, `AimHeight => _heights.Aim`, `IsDamageable => true`.
- `Prop : IBody`: `Prop(int id, PropDef def, Hex position)`; `Def`, `Hp` (= `def.Stats.Hp` at start),
  `MaxHp`, `IsDestroyed`, `IsAlive => !IsDestroyed`, `IsDamageable => Def.IsDestructible`,
  `Owner => -1`, `Stats => Def.Stats`, `ModifierIds` → a shared empty list, `TakeDamage` as
  `Unit.TakeDamage` (returns hp actually lost; sets `IsDestroyed` at 0).
- `BodySet : IOccupancy, IBodyLookup`: wraps a `UnitSet` and a `List<Prop>`; `Units`, `Props`
  (read-only), `AddProp`; `IsOccupied(hex)` = an alive body stands there; `TryGetBodyAt` alive only;
  `All` rebuilt lazily or maintained on add (units never leave the set; keep it simple).
- `UnitSet` is untouched (it stays the unit index; `BodySet` is what movement and targeting receive).

### 4.9 Trajectories: `Combat/TrajectoryContext.cs`, `Combat/ITrajectoryResolver.cs`, `Combat/DirectTrajectory.cs`, `Combat/ArcTrajectory.cs`, `Combat/SkyTrajectory.cs`, `Combat/TrajectoryRegistry.cs` (new)

```csharp
public readonly struct TrajectoryContext
{
    public readonly TileMap Map; public readonly IBodyLookup Bodies; public readonly HeightsDef Heights;
    public readonly Hex From; public readonly int FromHeight;   // absolute units: tileTop + aim
    public readonly Hex To;   public readonly int ToHeight;
    public readonly int Apex;
    public readonly int IgnoreBodyA, IgnoreBodyB;               // ids never counted as blockers (attacker, target); -1 = none
    /// <summary>Column top at a hex in absolute units; solid = unwalkable terrain (blocks everything but sky). Holes: false.</summary>
    public bool TryColumnTop(Hex hex, out long top, out bool solid) { … max(tile top, tile top + body.BodyHeight for an alive body not ignored) … }
}

public interface ITrajectoryResolver
{
    string Mode { get; }
    /// <summary>False when something between From and To stops the flight; blockedAt is that hex.</summary>
    bool IsClear(in TrajectoryContext ctx, out Hex blockedAt);
}
```

Shared walker (an `abstract class ColumnTrajectory : ITrajectoryResolver` with
`protected abstract bool Clears(long h0, long h1, int apex, long top, long a, long b)`), used by
`DirectTrajectory` (→ `Ballistics.StraightClears`) and `ArcTrajectory` (→ `Ballistics.ArcClears`):

1. `n = Hex.Distance(From, To)`; `n ≤ 1` → clear (adjacent or same).
2. `samples = HexLine.Trace(From, To)`; for `k = 1 … n−1`, for each candidate hex `samples[k][c]`
   (`c < Count`), skipping `From` and `To`:
   - `TryColumnTop` false → continue. `solid` → blocked at that hex.
   - test the column at **both** `a = 2k − 1` and `a = 2k + 1` over `b = 2n` (the tile is a prism; the
     lowest point of a monotone flight over it is at one of its ends). If `!Clears` for either →
     blocked at that hex.
3. Clear.

`SkyTrajectory.IsClear` is always true. `TrajectoryRegistry`: `Register(ITrajectoryResolver)`,
`bool TryGet(string mode, out ITrajectoryResolver)`, `bool IsClear(string mode, in ctx, out Hex blockedAt)`
throwing `InvalidOperationException("Unknown trajectory '…'")` (fail closed), and `static TrajectoryRegistry Default()`
registering the three. Mirror `MovementResolverRegistry`'s shape.

### 4.10 `Combat/Sight.cs` (new) and `Movement/LineOfSight.cs`

`Sight.IsClear(TileMap map, IBodyLookup bodies, HeightsDef heights, Hex from, int fromHeight, Hex to, int toHeight, int ignoreA, int ignoreB, out Hex blockedAt)`
builds a `TrajectoryContext` and runs `DirectTrajectory` (one static instance). Sight and `direct` are
the same test by construction (D2/D3).

`Movement/LineOfSight.cs` keeps its name and namespace but its only method becomes
`IsClear(TileMap map, IBodyLookup bodies, HeightsDef heights, Hex from, Hex to, int ignoreBodyId)` =
`Sight.IsClear` with `fromHeight = tileTop(from) + heights.Aim`, `toHeight = tileTop(to) + heights.Aim`
(D16). Delete the old two-argument overload. `MovementContext` gains `IBodyLookup Bodies` and
`HeightsDef Heights` and `int MoverId` (constructor and `For(...)` overloads; `For(map, terrains, occupancy, unit, def)`
becomes `For(map, terrains, bodies, unit, def, heights)` where `bodies` is a `BodySet` serving as both).
`TeleportResolver` with `RequiresLineOfSight` and `ctx.Heights == null` throws
`InvalidOperationException` (never a silent skip).

### 4.11 `Combat/AttackTargeting.cs`

```csharp
public enum TargetRejectReason { None = 0, NoBody, NotDamageable, OwnUnit, TargetDead, OutOfRange, NoLineOfSight, TrajectoryBlocked }

public readonly struct TargetCheck
{
    public readonly TargetRejectReason Reason; public readonly IBody Victim;
    public readonly Hex BlockedAt; public readonly bool HasBlockedAt;   // set for NoLineOfSight / TrajectoryBlocked
    public bool Ok => Reason == TargetRejectReason.None;
}

public static TargetCheck Check(TileMap map, BodySet bodies, HeightsDef heights, TrajectoryRegistry trajectories, Unit attacker, AttackDef attack, Hex target);
public static void Enumerate(…same…, List<IBody> into);   // bodies.All order
```

Order inside `Check` (first failure wins): no alive body at `target` → `NoBody`; `!IsDamageable` →
`NotDamageable`; `body.Owner == attacker.Owner` → `OwnUnit`; `!IsAlive` → `TargetDead`;
`!attack.InRangeSquared(Hex.EuclideanSquared(attacker.Position, target))` → `OutOfRange`;
`attack.LineOfSight && !Sight.IsClear(…)` → `NoLineOfSight` with `BlockedAt`;
`!trajectories.IsClear(attack.Trajectory, ctx, out blockedAt)` → `TrajectoryBlocked` with `BlockedAt`.
Endpoints: `fromHeight = heights.TileTop(attackerTile) + attacker.AimHeight`,
`toHeight = heights.TileTop(targetTile) + victim.AimHeight`, `IgnoreBodyA = attacker.Id`,
`IgnoreBodyB = victim.Id`, `Apex = attack.Apex`. Rename the old `NoUnit`/`NotEnemy` everywhere
(`grep -rn "TargetRejectReason\." shared server MimasClient`).

### 4.12 `Combat/DamageCalculator.cs`

`Compute(TileMap map, Unit attacker, IBody target, AttackDef attack, Knowledge knowledge)`. Everything
that read `target.Stats`, `target.Position`, `target.Id`, `target.ModifierIds`, `target.Owner` works
on the interface; `AddUnitModifiers` takes an `IBody`. `DamageLine.OwnerUnitId` keeps its name (it now
holds a body id; add a doc comment). `Applies(...)` unchanged (tiles only).

### 4.13 `Match/MatchState.cs`

- New members: `public BodySet Bodies { get; }`, `public IReadOnlyList<Prop> Props => Bodies.Props`,
  `public TrajectoryRegistry Trajectories { get; }` (constructor parameter after `resolvers`, default
  `TrajectoryRegistry.Default()`).
- Constructor: after the units exist, walk `MapData.Hexes` in authored order and add a `Prop` for every
  `PropId`, ids continuing after the highest unit id (`Units.All` max + 1, then +1 each).
- Replace every `Units` argument to movement contexts and targeting with `Bodies`
  (`MovementContext.For(Map, Catalog.Terrains, Bodies, unit, movement, Catalog.Rules.Heights)`).
- `ValidateAttack` uses `AttackTargeting.Check(Map, Bodies, Catalog.Rules.Heights, Trajectories, attacker, def, attack.Target)`;
  map `TargetCheck.Reason` to `CommandResult.RejectTarget`.
- `ApplyAttack`: `victim` is an `IBody`. Hidden-line reveal loop: look the owner up with
  `Bodies.TryGetBody` and skip lines whose owner is not a `Unit` (props have no hidden modifiers; the
  guard keeps it honest). After damage: a dead unit → `UnitDiedEvent` + `CheckElimination` as today; a
  destroyed prop → `PropDestroyedEvent(prop.Id)` (no elimination check, D11).
- `AttackResolvedEvent`: add `public bool TargetIsProp { get; }` (constructor parameter after `targetId`).
- New public queries:
  - `public TargetCheck CheckTarget(int unitId, string abilityId, Hex target)` (unknown unit/ability →
    a `TargetCheck` with `NoBody`; keep it non-throwing for UI use).
  - `public void AttackTargets(int unitId, string abilityId, List<IBody> into)` (replaces the `List<Unit>` one).
  - `public void RangeBand(int unitId, string abilityId, List<Hex> into)`: every map tile whose
    `EuclideanSquared` from the unit's position is inside the band, in `MapData.Hexes` authored order.
  - `PreviewAttack` / `ResolveAttackFully`: unchanged names; they now accept a prop target and return
    null when `CheckTarget` fails.
- `EnumerateLegal`: attack candidates come from the new `Enumerate` (props included).

### 4.14 `Match/MatchEvent.cs`, `Match/EventFilter.cs`, `Match/PlayerView.cs`

- `PropDestroyedEvent(int PropId)`; `EventFilter` passes it to both viewers (public).
- `PlayerView`: `public IReadOnlyList<PropView> Props`, `public PropView FindProp(int id)`.
  `PropView(int id, string defId, Hex position, int hp, int maxHp, bool isAlive, int bodyHeight, int aimHeight, bool isDamageable)`.
  `Build` appends every prop (props are public, D11).
- `UnitView` gains `BodyHeight` and `AimHeight` (so a client never reads rules for a unit's socket).

### 4.15 `Bots/RandomBot.cs`, `Match/Command.cs`, `Match/MatchSetup.cs`, `Match/Loadout.cs`, `Data/ItemDef.cs`, `Data/ModifierDef.cs`, `Data/StatBlock.cs` (read-only use), `Geometry/HexLine.cs`

Untouched (verify with `git diff --stat` before the final commit).

---

## 5. Test fixtures (`shared/Mimas.Core.Tests`) — adapt, do not delete

- `CombatFixtures.Files()` (in `CombatTests.cs`): add `"heights": { "unitsPerLevel": 3, "body": 6, "aim": 4 }`
  to the rules file; add `trajectory` / `lineOfSight` to every attack fixture (`direct` / `true` unless
  a test says otherwise); add a `props/pillar.json` and `props/wall.json` fixture (copy §3.3) so
  catalogue tests see the folder; add a small fixture map `props-3` (radius 2 or 3, rotational-180) with
  one pillar and one wall pair for the body-blocking and prop-targeting tests.
- Every `new Unit(id, owner, hex)` → `new Unit(id, owner, hex, TestHeights.Default)` where
  `TestHeights.Default = new HeightsDef(3, 6, 4)` lives in a new `TestFixtures.cs`.
- Every `AttackTargeting.Check(...)` / `Enumerate(...)` / `MovementContext.For(...)` /
  `LineOfSight.IsClear(...)` call updates to the new signatures (§4.10, §4.11). Where a test passed a
  `UnitSet`, build a `BodySet` around it.
- `MovementTests` that relied on the old "taller than both endpoints" sight rule for teleport: keep the
  behaviour they assert where D3 gives the same answer; where it does not, rewrite the arrangement so
  the blocker is a level-2 tile (6 ≥ 4 blocks) and add a sibling test showing level 1 no longer blocks.
- `MatchTests` / bot games: the repo-data games run on `arena-4` (now with props) and `board-3`; the
  invariants (hp never below 0, AP bounds, same seed same log) must keep holding. Add the invariant
  "a destroyed prop's tile is enterable and no body reports it".
- Never edit an assertion to make a test pass; fix the fixture or the code.

---

## 6. New tests (one behaviour each, names exact)

### 6.1 Geometry and ballistics (`HexTests.cs`, new `BallisticsTests.cs`)

- `EuclideanSquared_StraightFive_Is25`, `EuclideanSquared_Diagonal33_Is27`,
  `EuclideanSquared_IsSymmetric`.
- `StraightClears_FlatRayOverLowerColumn_Clears`, `StraightClears_GrazeBlocks`,
  `StraightClears_UphillRay_ClearsColumnTallerThanShooter`,
  `ArcHeightScaled_WorkedExample_MatchesTable` (§4.7 numbers), `ArcClears_PeakIsMaxEndpointPlusApex`,
  `ArcClears_WallAdjacentToShooter_Blocks`, `ArcClears_SameWallMidway_Clears`,
  `Ballistics_UsesLongArithmetic_NoOverflowAtRange20Height1000`.

### 6.2 Parsing and linking (`DataTests.cs`, `ContentTests.cs`)

- `Rules_MissingHeights_Throws`, `Rules_AimAboveBody_Throws`.
- `Attack_MissingTrajectory_Throws`, `Attack_MissingLineOfSight_Throws`, `Attack_ArcWithoutApex_Throws`,
  `Attack_ApexOnDirect_Throws`, `Attack_UnknownTrajectory_Throws`, `Attack_ArcParsesApex`.
- `Prop_ParsesWallWithoutStats_NotDestructible`, `Prop_PillarWithHp_Destructible`,
  `Prop_AimAboveBody_Throws`, `Prop_PowerStat_Throws`, `Prop_ApStat_Throws`.
- `Map_PropOnUnwalkableTerrain_LinkError`, `Map_UnknownProp_LinkError`, `Map_PropTwinMismatch_Throws`,
  `Map_PropOnSpawn_Throws`.
- `Catalog_LoadsProps`, `Catalog_HashChangesWhenPropChanges`.
- Shipped data: `ShippedAttacks_AllDeclareTrajectoryAndSight`, `ShippedBow_IsArcWithoutSight`,
  `ShippedGunAndSpells_AreDirectWithSight`, `Arena4_HasNoStone`, `Arena4_PropsAreRotationallySymmetric`,
  `Arena4_SpawnsCannotSeeEachOther`, `Board3_Unchanged_StoneStillBlocksSight`.

### 6.3 Sight, trajectories, targeting (`CombatTests.cs`, new `TrajectoryTests.cs`)

- `Sight_LevelOneStepBetweenGroundUnits_Clear`, `Sight_LevelTwoBetweenGroundUnits_Blocked_ReportsHex`,
  `Sight_FromLevelOne_SeesOverWallAtShortRange`, `Sight_IsSymmetricBetweenHeroes`,
  `Sight_EndpointTilesNeverBlock`, `Sight_UnwalkableTerrainIsSolid`, `Sight_HoleNeverBlocks`,
  `Sight_LivingUnitBetween_Blocks`, `Sight_DeadUnitBetween_DoesNotBlock`, `Sight_WallProp_Blocks`,
  `Sight_DestroyedPillar_DoesNotBlock`, `Sight_AttackerAndTargetBodiesIgnored`.
- `Direct_EqualsSight`, `Arc_ClearsWallTwoTilesAway`, `Arc_BlockedByAdjacentWall`,
  `Arc_HigherApexClearsWhatLowerDoesNot`, `Sky_IgnoresEverythingBetween`,
  `Sky_StillNeedsSightWhenFlagged`, `Registry_UnknownMode_Throws`, `Registry_Default_HasThreeModes`.
- `Check_ArcWithoutSight_HitsUnseenTarget`, `Check_DirectWithSight_BlockedReportsNoLineOfSight`,
  `Check_OutOfRange_UsesEuclidean_Diagonal44InGunRange`, `Check_WallProp_NotDamageable`,
  `Check_PillarProp_IsLegalTarget`, `Check_OwnUnit_Rejected`, `Check_EmptyTile_NoBody`,
  `WithTrajectory_CloneOfGunAsArc_ClearsOverPlateau` (D8 seam).

### 6.4 Match, view, events (`MatchTests.cs`)

- `Match_PropsCreatedFromMap_IdsAfterUnits`, `Match_PropOccupiesTile_MoveRejected`,
  `Match_DestroyedPillar_TileEnterable_EventEmitted`, `Match_DestroyingPillar_DoesNotEndMatch`,
  `Match_AttackOnPillar_ResolvedEventFlagsProp`, `Match_TeleportSight_UsesRay_IgnoresMover`,
  `PlayerView_ListsPropsForBothViewers`, `PlayerView_UnitViewCarriesHeights`,
  `RangeBand_ReturnsTilesInsideCircle_OrderStable`, `EnumerateLegal_IncludesPillarTargets`,
  `Bot_GamesOnArena4WithProps_InvariantsHold_SameSeedSameLog`.

---

## 7. Server and the data refresh

### 7.1 `server/Mimas.Server/Program.cs`

`/health` adds `props = catalog.Props.Count`. Nothing else.

### 7.2 `MimasClient/Assets/_Game/Content/ContentBootstrap.cs`, `GameDataManifestBuilder.cs`

Read them: if the manifest builder enumerates folders explicitly, add `props/`; the summary log should
count props. This is C# in the Unity project, compiled by Unity only; keep the change minimal and
compile-safe by inspection (no `dotnet build` covers it). Say in the report that it is unverified if no
Editor was available.

### 7.3 Data refresh (only if an Editor is `ready`)

`export MSYS_NO_PATHCONV=1; unity command menu --path "Assets/Refresh" --timeout 180`, poll
`unity command recompile_status` until `completed`, then `unity command console` and grep for
`error CS`, `Exception`, `[ContentBootstrap]`, `[GameDataManifest]`. Commit the new `.meta` files
together with their JSON. If the Editor is not `ready`, skip this step and commit the JSON without
metas; Spec B's session refreshes first thing.

---

## 8. Documentation updates

### 8.1 `docs/data.md`

- Folder table: add `props/*.json` (loaded) and update `rules.json` (`heights`) and `maps/*.json`
  (`prop?`) rows.
- Rules section: a **Heights** subsection (D1, the three fields, "1 level = 3 units, hero 6, aim 4").
- Attack section: rows for `trajectory`, `apex`, `lineOfSight`; `range` / `minRange` now "Euclidean
  centre distance in tile spacings (`q²+qr+r²` of the offset compared with the squares); equals hex
  distance up to 6".
- New **Line of sight and trajectories** section replacing the sentence in "Tile height": the D3/D4
  ray rule with the cross-multiplied test, the per-tile two-sample rule (§4.9 step 2), the arc formula,
  `sky`, what is solid, what is ignored, and that movement's `requiresLineOfSight` uses the same ray.
- New **Props** section (§3.3 fields, D11 rules, map `prop` field, D12 validation, ids after units,
  `PropDestroyedEvent`, `PlayerView.Props`).
- Map section: the `prop` field and the new invariants.
- Schemas section: the prop schema row.

### 8.2 `docs/design/index.html`

Follow the page's own conventions (`<section class="concept" id="…">`, `data-status`, `data-impl`,
`.drift-note`, decision log, changelog). Changes:

- `#board`: heights paragraph rewritten for D1 ("height is a level; rules convert levels to body units
  through `rules.heights`"); note that the level-1 / level-2 examples now read in units.
- `#line-of-sight`: rewrite to D2–D4 (ray from aim point to aim point, integer viewshed test, graze
  blocks, bodies block, endpoints never block, unwalkable is solid). The open question "should units
  block sight" is **resolved: yes** (log it). `data-impl` → implemented.
- New `#trajectories` section under the attacks area (status `decided`, impl `implemented`): the two
  fields, the three modes and their rules, the apex definition, the override seam (D8) and an open
  question `q-trajectory-enchant` ("which enchant first swaps a trajectory?"). Add `sky` as "exists in
  code, no shipped ability".
- `#attacks`: fields `trajectory`, `apex`, `lineOfSight`; range band is Euclidean (D9) with the "equals
  hex distance up to 6" note; targets are "any damageable body: enemy hero or destructible prop";
  ground targeting is not legal. Flip `data-impl`.
- New `#props` section under the board area (status `decided`, impl `implemented`): D11, D12, the two
  shipped props, an open question `q-props-owned` (owned props / shield generators) and
  `q-props-rubble`.
- `#maps`: arena-4 description and the layout table from §3.4; board-3 unchanged.
- `#weapon`: bow row "lobbed (arc), no sight needed"; gun row "straight, needs sight".
- `#hidden-info`: props are public.
- `#movement`: teleport sight follows the ray rule (D16).
- `#presentation`: one `proposed` bullet pointing at Spec B ("range circles, path preview, projectile,
  facing").
- Glossary: **body**, **aim point**, **trajectory**, **prop**.
- Decision log entries for D1–D14 dated 2026-09-16; changelog entry; drift table row cleared for
  line-of-sight. Run the page's link check if it has one (grep `#how-to-use` for the instruction).

### 8.3 `docs/roadmap.md`

"M3 progress (16 Sep 2026): the aiming slice, rules half" paragraph (what landed, numbers from §3.7,
"client half pending: Spec B"); a baselines row for the new test count.

### 8.4 `docs/decisions.md`

- **ADR-023** (2026-09-16): *Height-aware line of sight and trajectories.* Ray from aim point to aim
  point over integer heights (viewshed test, cross-multiplied, graze blocks, bodies block), two
  independent attack fields (`lineOfSight`, `trajectory`) with a resolver registry (direct / arc /
  sky) and an integer parabola for arcs; ranges Euclidean via `q²+qr+r²`. Alternatives rejected:
  BattleTech level buckets (cannot express distance-sensitive cover), ASL blind-hex tables (same maths
  hidden in a table), "arc ignores everything" (nothing for an apex boon to change), hex-distance bands
  drawn as circles (the circle would lie).
- **ADR-024** (2026-09-16): *Bodies and props.* `IBody` unifies heroes and props for occupancy, sight
  blocking and targeting; anything with hp can be hit; props are neutral and public, removed on death,
  placed by map hexes. Alternatives: props as terrain (cannot be damaged, no aim point), owned props
  now (premature).

### 8.5 `CLAUDE.md`

Rule 5's folder list: add `props`. Nothing else.

---

## 9. Verification commands (run all before the final commit)

```bash
dotnet build Mimas.sln
dotnet test shared/Mimas.Core.Tests                        # expect >= 240 passed, 0 failed
dotnet build server/Mimas.Server
grep -rn "InRange(" shared server --include=*.cs           # expect only InRangeSquared
grep -rn "NoUnit\|NotEnemy" shared server MimasClient/Assets/_Game --include=*.cs   # expect no hits
grep -rn "\"stone\"" MimasClient/Assets/_Game/Data/maps/arena-4.json   # expect no hits
grep -rln "trajectory" MimasClient/Assets/_Game/Data/abilities | wc -l  # 8 (six shipped attacks + jab + strike)
grep -rn "float\|double" shared/Mimas.Core/Runtime/Combat shared/Mimas.Core/Runtime/Units   # expect no hits
git status --short                                         # no Library/, no orphan metas
```

---

## 10. Commit plan (each step green before committing; `git add` only the files you touched)

1. `core: euclidean hex distance, heights in rules, trajectory and sight fields on attacks`
   (§4.1–4.3, §3.1–3.2 data, schema edits for rules/ability, fixture updates for rules/attacks, §6.1
   ballistics tests, §6.2 rules/attack parsing tests).
2. `core: bodies, props and the body set; props in maps and the catalogue`
   (§3.3, §3.4, §3.6 prop/map schemas, §4.4–4.6, §4.8, fixtures §5, tests §6.2 prop/map/catalog).
3. `core: ray line of sight, trajectory resolvers, target checks against bodies`
   (§4.7, §4.9–4.13 targeting and damage, §4.10 movement sight, tests §6.3).
4. `core: props in match state, events and player view; range band query`
   (§4.13 rest, §4.14, tests §6.4).
5. `server: health reports props` (§7.1) and `client: manifest and bootstrap know props/` (§7.2, plus
   the metas from §7.3 if the Editor was up; one commit or two, say which).
6. `docs: data.md, design page, ADR-023/024 and roadmap for the aiming rules` (§8).

Commit body: one or two lines on what changed and which §11 defaults were taken, then the attribution
line the session's system reminder requires.

---

## 11. Defaults for forks the session may hit

| If… | Then… |
|---|---|
| `HexLine.Trace` returns candidate hexes that are `From` or `To` at `k = 1` or `k = n−1` (a graze next to an endpoint) | Skip them (endpoints never block, D3). |
| `StatBlock.FromJsonAt`'s third parameter does not mean "require hp/ap" | Parse the prop `stats` object by hand with `MapJson.OptionalInt` for the three allowed keys and reject any other key. |
| A `MovementTests` teleport-sight test breaks because level 1 no longer blocks | That is D3 working; rearrange the fixture to level 2 and add the sibling test (§5). |
| The random-bot games on arena-4 now take far longer because the bot shoots pillars | Acceptable; if a test's step cap is hit, raise the cap in that test only and say so. |
| Line-of-sight tests need a map with heights beyond what `props-3` offers | Add a second in-memory fixture map; never change `arena-4`'s layout from §3.4. |
| The order of `BodySet.All` is unclear for a determinism test | Units in `UnitSet.All` order, then props in map authored order. Document it in the class summary. |
| `PreviewAttack` is asked about a prop and the calculator finds `OwnerUnitId` lines for a prop's defense | Fine: the line's owner is the prop id; the reveal loop skips non-unit owners. |
| The ability schema's `apex`-forbidden-unless-arc rule is awkward in draft-07 | Use `"if": { "properties": { "trajectory": { "const": "arc" } }, "required": ["trajectory"] }, "then": { "required": ["apex"] }, "else": { "not": { "required": ["apex"] } }` inside the attack object. |
| The design page has no `#trajectories` or `#props` section slot | Create them next to `#attacks` and `#tile-effects` respectively, copying an existing section's markup, and add them to the nav list. |
| `unity command` times out during §7.3 | Wait 10 s, retry once, then skip the refresh and report it. |
| You need a rule the design page does not have | Pick the smallest option, add it as a `proposed` bullet with an `<li id="q-…">` open question, continue. |

---

## 12. Definition of done (copy this list into the final report with ticks)

- [ ] `dotnet build Mimas.sln` and `dotnet test` green, ≥ 240 tests; content-hash and same-seed determinism tests pass; no `float`/`double` in `Combat/` or `Units/`.
- [ ] Data: `rules.json` has `heights`; all eight attack files declare `trajectory` + `lineOfSight` (bow `arc` with `apex`, no sight); `props/pillar.json`, `props/wall.json`; `arena-4.json` matches §3.4 and has no stone; schemas updated; `board-3.json` untouched.
- [ ] Core: `Hex.EuclideanSquared`, `HeightsDef`, `RulesDef.Heights`, `AttackDef.Trajectory/Apex/LineOfSight/InRangeSquared/WithTrajectory`, `PropDef`, `MapHex.PropId`, `ContentCatalog.Props`, `IBody`, `IBodyLookup`, `Prop`, `BodySet`, `Ballistics`, `TrajectoryContext`, `ITrajectoryResolver` + three resolvers, `TrajectoryRegistry`, `Sight`, `TargetCheck`, `TargetRejectReason` (new names), `DamageCalculator.Compute(IBody)`, `MatchState.Bodies/Props/Trajectories/CheckTarget/RangeBand`, `PropDestroyedEvent`, `AttackResolvedEvent.TargetIsProp`, `PlayerView.Props`, `UnitView.BodyHeight/AimHeight`.
- [ ] Server `/health` reports `props`; bootstrap/manifest know `props/` (verified or reported as unverified).
- [ ] Six (or seven) commits on `main` as in §10; nothing pushed.
- [ ] `docs/data.md` accurate; design page sections flipped/added per §8.2 with decision log and changelog entries; ADR-023 and ADR-024 written; roadmap paragraph and baseline row; CLAUDE.md rule 5 lists `props`.
- [ ] Final report: test count, every §11 default taken, whether §7.3 ran, anything unverified or left undone, and the one-line hand-off "Spec B may start".
