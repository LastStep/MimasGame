# Game data (JSON)

All balance/content data lives in `MimasClient/Assets/_Game/Data/**/*.json`, the single source of truth.
`Mimas.Core.Content.ContentCatalog` loads the whole folder on every peer: the Unity client through a
generated manifest, the server from a copy linked in by its csproj, the tests straight from the repo.
Numbers are integers (no floats in rules).

| File | Contents | Status |
|---|---|---|
| `terrains.json` | Terrain catalogue: `id`, `walkable`, `moveCost`, `modifiers[]` | loaded |
| `rules.json` | Match-wide rules: `damageTypes[]` (the two lanes), `elements[]` (fire, frost, lightning), `globalModifiers[]` (height advantage lives here as data), `heights` (levels → body units), `baseStats` (every hero's numbers before gear), `innateAbilities[]` (the walk), `clock` (the turn deadline both sides time by), `series` (best of), `draft` (offer counts, timer), `boons` (floors) | loaded |
| `timecontrols.json` | `{ "version": 1, "timeControls": [ { "id": "3+2", "name", "baseMs", "incrementMs", "turnCapMs" } ] }` | loaded, unused until time controls return (design: `#time-controls`) |
| `abilities/*.json` | One ability per file; `type` picks the schema (`movement`, `attack`). Every ability has an AP `cost` (default 1); an attack may carry one innate `element` | loaded |
| `items/*.json` | One item per file: `id`, `slot`, `kind`, `stats`, `abilities`, `tags` | loaded |
| `maps/*.json` | Map: name, hexes[] (`q,r,terrain,height,effect?,prop?`), spawns (`p1`,`p2`), symmetry type, ladder position | loaded |
| `modifiers/*.json` | Flat damage modifiers: `trigger`, `when` conditions (`damageTypes`, `tags`, `elements`, `itemKinds`, `heightAdvantage`), `effect.damage` or `effect.nullify`, `visibility`. Attached by terrains, map hexes (`effect`), `rules.globalModifiers` and boons | loaded |
| `props/*.json` | Non-unit bodies a map hex can carry: `bodyHeight`, `aimHeight`, optional `stats` (a prop with `hp` can be destroyed) | loaded |
| `boons/*.json` | One boon per file: `kind` (blessing / enchant / sigil), `lineage`, `requires`, `effects[]` from the closed vocabulary, `stackable`, `exclusive[]` | loaded |
| `lineages/*.json` | One lineage per file: `startingBlessing`, `pool[]` | loaded |

## The content catalogue

`ContentCatalog.Load(files)` takes `(path, text)` pairs (`ContentFile`) and returns an immutable catalogue
or throws one `ContentLoadException` listing **every** problem, each tagged with its file. It fails closed:
an unrecognised file or folder is an error, never ignored.

1. **Parse.** Each file is parsed on its own by path: `terrains.json`, `rules.json`, `timecontrols.json`,
   `abilities/`, `items/`, `maps/`, `modifiers/`, `props/`, `boons/`, `lineages/`. Duplicate ids across files are reported with both file names.
2. **Link.** Item ability ids must exist and must not repeat an innate one; an item in the `weapon` slot
   needs at least one attack ability and may grant only `weapon`-category ones, and a `crown` may grant only
   `spell` ones (`#attacks`: the lane and the HUD section must agree); item stat keys must name a lane. `rules.innateAbilities` ids must
   exist and at least one of them must be a movement ability (the walk). A movement's `terrainCosts` keys
   must be real terrains; `rules.baseStats` keys, item stat keys, attack `damageType`s and modifier
   `damageTypes` conditions must name a type in `rules.damageTypes`; an attack's `element` and a modifier's
   `elements` must name one in `rules.elements`; terrain `modifiers`, map hex `effect`s
   and `rules.globalModifiers` must be real modifiers; a map hex's `prop` must name a real prop and sit on
   walkable terrain (`MapData.ValidateProps`); every map must pass `BuildTileMap` (symmetry, spawns,
   connectivity). So a map that would fail at match start fails at boot instead. The boon and lineage link
   rules are listed under *Boons* and *Lineages* below.
3. **Tables.** `Abilities`, `Items`, `Maps`, `TimeControls`, `Modifiers`, `Props`, `Boons`, `Lineages` are `DefinitionTable<T>`: sorted by ordinal id,
   `Get` / `TryGet` / `IndexOf` / `ByIndex`. Indices are stable small integers for wire encoding.
   `Terrains` is the existing `TerrainSet`; `Movements` is the movement subset; `GetMovement(id)`,
   `GetBoon(id)`, `GetLineage(id)`.
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
- A boon's `effects[]` is a list of flat records whose `type` is one of a **closed vocabulary** — `stat`,
  `modifier`, `abilityOverride`, `addElement`, `addTag`, `grantAbility` — with one handler each in
  `BoonOverlay`. A new type is a design decision (`#boons` rule 2) before it is code; the loader rejects
  unknown types and stray fields.
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
  "heights": { "unitsPerLevel": 3, "body": 6, "aim": 4 },
  "baseStats": { "hp": 20, "ap": 3, "power.weapon": 1, "power.spell": 1, "defense.weapon": 0, "defense.spell": 0 },
  "innateAbilities": ["move"],
  "clock": { "turnMs": 30000, "lagGraceMs": 1000, "reconnectGraceMs": 60000 },
  "elements": ["fire", "frost", "lightning"],
  "series": { "bestOf": 3 },
  "draft": { "offers": { "winner": 3, "loser": 3 }, "timeoutMs": 20000 },
  "boons": { "floors": { "hp": 1, "ap": 1 }, "minCost": 1 }
}
```

`damageTypes` are the two damage lanes: every `power.<type>` / `defense.<type>` stat key, every attack's
`damageType` and every modifier `damageTypes` condition must name one, so adding a lane is one line here.
`elements` are the modifier classes an attack may carry and a modifier may condition on (`#elements`);
an attack's `element`, a modifier's `when.elements` and a boon's `addElement` must name one. May be empty.
`globalModifiers` apply to every attack (height advantage is the shipped example); they are ordinary
modifier ids. `baseStats` and `innateAbilities` are both required and are described below.

`series`, `draft` and `boons` are the session's numbers (all required; the constructor defaults them
for hand-built fixtures like `clock`): `series.bestOf` (odd, ≥ 1; first to `bestOf / 2 + 1` wins),
`draft.offers.winner` / `.loser` (how many boons each role is offered between rounds, ≥ 0 — symmetric 3 / 3
at launch, so a comeback draft is a number change) and `draft.timeoutMs` (the host's timer before it
submits a timeout pick; Core never reads a clock), `boons.floors.hp` (≥ 1) / `.ap` (≥ 0) (what a self
trade-off can never take a unit below) and `boons.minCost` (an ability's cost never drops below it through
an Enchant; "nothing is ever free").

`clock` is required and all three values must be positive. It is a **rule**, not balance, because both
sides read it: the server times the turn by `turnMs` and the client draws the same rope from the same
number. `lagGraceMs` caps the measured round-trip allowance the server adds before it ends a turn, so a
command that left the client before the deadline is never refused for arriving after it.
`reconnectGraceMs` is how long a dropped seat is held before the server forfeits it. Banks, increments
and a choice of time control are a later design (`#time-controls`); `timecontrols.json` stays loaded and
unused until then.

## Heights (`rules.json`, `heights`)

```json
{ "unitsPerLevel": 3, "body": 6, "aim": 4 }
```

Required. A map's `height` is an integer **level**; sight and trajectories are measured in **body units**,
and this block is the conversion. One level is `unitsPerLevel` units tall (3), a hero's body is `body`
units above the tile top (6), and every attack leaves from and lands at `aim` units above the tile top (4).
Constraints: `unitsPerLevel` ≥ 1, `body` ≥ 1, `1 ≤ aim ≤ body`. The numbers are balance placeholders.

What they buy: a flat shot between two heroes standing on the ground runs at 4, so a level-1 step (top 3)
does not block it but a level-2 plateau (top 6) does, and so does any body — hero, wall or pillar — because
a body's top is the tile top plus 6. `HeightsDef.TileTop(tile)` is the one conversion; nothing else
multiplies a level.

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
  "attack": { "damage": 6, "damageType": "spell", "range": 3, "minRange": 1,
              "trajectory": "direct", "lineOfSight": true, "element": "fire" }
}
```

| Field | Required | Meaning |
|---|---|---|
| `category` | yes | `weapon` or `spell` (never `movement`). Picks the action-bar section, and **must equal `attack.damageType`** — the catalogue enforces it through the granting item's slot, a repo-data test checks it across the shipped catalogue, and the schema flags it as you type. |
| `attack.damage` | yes, 0 or more | Flat base damage. |
| `attack.damageType` | yes | The lane: `weapon` or `spell` (one of `rules.damageTypes`). Selects `power.<type>` and `defense.<type>` and is matched by modifiers. |
| `attack.range` | yes, 1 or more | Maximum **Euclidean centre distance in tile spacings** (see below). |
| `attack.minRange` | no, default 1 | Minimum Euclidean centre distance in tile spacings (`1..range`). |
| `attack.trajectory` | yes | How the attack travels: `direct`, `arc` or `sky`. See *Line of sight and trajectories*. |
| `attack.apex` | required iff `arc`, forbidden otherwise | How far above the higher endpoint the arc peaks, 0 or more. |
| `attack.lineOfSight` | yes | Must the attacker see the target? Independent of `trajectory`; both must pass. |
| `attack.element` | no | The innate element, one of `rules.elements`. At most one; an Enchant can add more. `AttackDef.Elements` is the **set** (innate plus added, once a unit's overlay is applied); riders, resistances and immunities match on any of them. |
| `attack.hits` | no, default 1 | **Skeleton** (spec D part 1 §6.7): parsed and stored, and a unit whose resolved attack has more than one throws `NotSupportedException`. No shipped file sets it; a repo-data test checks that. |
| `tags` | no | Free strings (weapon kinds) for modifiers to match. Unique. |

**Range is a circle.** A target is in the band when `minRange² ≤ q² + qr + r² ≤ range²` for the offset
`target − attacker` (`Hex.EuclideanSquared`, `AttackDef.InRangeSquared`). That integer is exactly the
squared distance between the two hex centres in units of the centre-to-centre spacing, so a client can draw
a true circle and be right. Up to 6 it equals hex distance; from 7 up the diagonals reach further — the
gun's range 7 covers `(4,4)`-shaped offsets at hex distance 8 (48 ≤ 49) but not 8 straight along an axis
(64 > 49). Movement is unchanged: it still counts hexes.

Targeting (Core, `Mimas.Core.Combat.AttackTargeting.Check`) returns a `TargetCheck` and refuses in this
order: nothing alive on the hex (`NoBody` — ground targeting is not legal), the body has no hit points
(`NotDamageable`, a wall), it is the attacker's own hero (`OwnUnit`), `TargetDead`, `OutOfRange`, then
`NoLineOfSight` when `lineOfSight` is set and the ray is blocked, then `TrajectoryBlocked`. The last two
carry `BlockedAt`, the hex that stopped the shot. Anything with health can be hit and nothing else, so an
enemy hero and a destructible prop are targets and a wall is not. `Enumerate` and `Check` agree by
construction; `MatchState.CheckTarget` is the non-throwing wrapper a HUD polls, and `MatchState.RangeBand`
lists the tiles inside the circle.

Damage (`Mimas.Core.Combat.DamageCalculator`):

```
damage = max(0, base + power.<type> - defense.<type> + sum of boon stat lines + sum of flat modifiers)
         then 0 if an immunity applied
```

The result is a `DamageBreakdown`: a fixed-order list of signed lines (base, attacker power, target
defense, then modifiers grouped attacker unit / attacker tile / globals for `dealDamage`, target unit /
target tile / globals for `takeDamage`, each group sorted by id) plus the total. Power and defence come
from the bodies' **public** stats (what gear explains); every boon's contribution to those keys, and
every Enchant `damage` override on the attack, is its own hidden `BoonStat` line carrying the boon id, in
boon order. An immunity (`effect.nullify`) never adds a flat line: the first that applies ends the
breakdown with one `Nullify` line that takes the total to 0 after every flat line and the floor
(`DamageBreakdown.Nullified`). The same function computes
the **preview** (`Knowledge.For(player)`: hidden enemy modifiers and boons are dropped and counted in
`UnknownCount` — one unknown per hidden modifier and one per hidden boon, whether or not it applies, so a
client mirror can reproduce the count) and the **actual** result (`Knowledge.Full`). When an actual result
contains a hidden line that changed the number, the owner's opponent learns it (`ModifierRevealedEvent`,
and `BoonRevealedEvent` for a boon line or for the boon behind a modifier) and it appears in every later
preview. The `attack` the calculator receives is the **resolved** definition (see *Match flow*).

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
| `when` | no | All conditions must hold. `damageTypes[]` (attack type is one of), `tags[]` (attack has any of), `elements[]` (the resolved attack carries any of; each in `rules.elements`), `itemKinds[]` (the attack was granted by an item of any of these kinds — an innate ability never matches), `heightAdvantage` (attacker's tile is higher than the target's). Unknown keys are errors. |
| `effect.damage` | one of the two, not 0 | Flat amount added to the damage. |
| `effect.nullify` | one of the two, `true` | Immunity (`#damage` rule 3): when it applies, the total becomes 0 after everything else, as one `Nullify` line. **Mutually exclusive with `damage`**; the loader rejects a file that sets both. |
| `visibility` | no, default `public` | `hidden` modifiers are unknown to the opponent until they first change a result (boons). Tile and global modifiers should stay `public`. |
| `icon` | no | Presentation key, like abilities. |

Who owns a modifier is the attachment, never the file: a unit carries it (`Unit.ModifierIds`, attached by
a boon's `modifier` effect — the unit remembers which, `Unit.BoonOfModifier` — or by
`MatchSetup.WithModifier`), a terrain grants it to whoever stands on it
(`terrains.json` `modifiers`), a single map hex grants it (`maps/*.json` hex `effect`), or it applies to
every attack (`rules.globalModifiers`). The same id twice on one unit does not stack; when two boons attach
one modifier, the first in grant order owns it.

## Boons (`boons/*.json`)

```json
{
  "version": 1, "id": "agni-crown", "name": "Agni's Crown", "kind": "enchant", "lineage": "hindu",
  "description": "Every spell from your crown burns, and fire spells hit 2 harder.",
  "requires": { "slot": "crown" },
  "exclusive": ["crown-element"],
  "effects": [ { "type": "addElement", "target": "crown", "element": "fire" },
               { "type": "modifier", "id": "agni-fire" } ],
  "icon": "agni-crown"
}
```

| Field | Required | Meaning |
|---|---|---|
| `kind` | yes | `blessing` (a passive on the character), `enchant` (a change to one item), `sigil` (a new ability on one item). `BoonKinds`. |
| `lineage` | yes | A `lineages/*.json` id (link). |
| `requires` | enchant, sigil: yes; blessing: **forbidden** | `slot` (one of the four) required; `kind` optional (an item kind; link: at least one item of that slot has it). |
| `effects[]` | yes, ≥ 1 | Flat records; `type` picks the fields (table below). A field that does not belong to the type, or an unknown type, is a load error. |
| `stackable` | no, default false | May be drafted again while owned. Only legal when every effect is `stat` or `abilityOverride`. |
| `exclusive[]` | no | Group tags: the draft never offers a boon sharing a group with an owned one (`crown-element` keeps two element Enchants off one crown). Unique, sorted. |
| `name`, `description`, `icon` | no | Presentation; `name` defaults to the id. Names follow `<God>'s <thing>`, so a reveal also names the god. |

The effect vocabulary (closed; one handler each in `Units/BoonOverlay.cs`):

| `type` | Fields | Allowed on | Meaning |
|---|---|---|---|
| `stat` | `key` (`hp`, `ap`, `power.<lane>`, `defense.<lane>`), `amount` (≠ 0) | blessing | Added to the unit's stats; negatives are self trade-offs, floored on the total by `rules.boons`. A `stackable` boon of this kind may be drafted again — shipped: `thor-might`, `hanuman-heart` |
| `modifier` | `id` | blessing, enchant | The modifier is attached to the unit for the session |
| `abilityOverride` | `target` (an enchant's slot, or one ability id; a blessing's target must be an innate ability id), `field`, `amount` (≠ 0) or `value` | blessing, enchant | Additive integer override of one field. Numeric fields: `range`, `minRange`, `damage`, `cost`, `apex`, `hits`, `climb`, `jumpHeight`. Skeleton fields with a string `value`: `trajectory` (`direct` / `arc` / `sky`), `lineOfSight` (`true` / `false`). A slot target means every ability the item grants, Sigils included whichever was drafted first; a field with no meaning on one of them (`apex` on a direct attack) is ignored for that one |
| `addElement` | `target` (the slot), `element` | enchant | Every attack the item grants gains the element |
| `addTag` | `target` (the slot), `tag` | enchant | Every attack the item grants gains the tag |
| `grantAbility` | `target` (the slot), `ability` (not innate) | sigil | The item grants one more ability, after its own; the item is the ability's public source, the boon is not |

Link rules (each a `ContentError` naming the boon file): the lineage exists; `requires.kind` is some item's
kind; modifier ids and ability ids exist; a granted ability is not innate and obeys the slot's lane rule
(a weapon Sigil grants `weapon` attacks, a crown Sigil `spell` ones); a blessing's override names an innate
ability; an enchant's ability-id target is granted by an item of its slot (and kind) **or by a Sigil of the
same lineage on that slot**, so an Enchant on a Sigil's spell is legal content; a numeric field must apply
to at least one ability the target can name (`damage` on the boots is an error, `range` is fine on either
type); a `stat` lane is declared; an `addElement` element is declared.

`hits`, `trajectory` and `lineOfSight` are **skeletons** (spec D part 1 §6.7): they parse and link, and a
unit built with one throws `NotSupportedException` naming the spec. Nothing shipped uses them.

Shipped: **thirty boons** — a starting Blessing and nine pool boons for each of the three lineages. Four
Sigils grant an ability no item does: `dash`, `hammerfall`, `wind-step`, and part 2's `storm-bolt`
(Indra's Storm, a lightning spell on the crown); two more grant the shipped `jab` and `strike`.

## Lineages (`lineages/*.json`)

```json
{ "version": 1, "id": "hindu", "name": "Hindu", "description": "Pray to Agni, Vayu and Indra.",
  "startingBlessing": "vayu-breath",
  "pool": ["agni-warmth", "indra-wrath", "agni-crown", "indra-mail", "agni-spark", "vayu-wings"],
  "icon": "hindu" }
```

`startingBlessing` (a `blessing` of this lineage; granted at select, appended to the build by the session)
and `pool[]` (unique boon ids of this lineage the draft draws from). Link rules: the starting Blessing
exists, is a blessing, belongs here; every pool id exists and belongs here; the pool holds **all three
kinds**; and **for every item in the catalogue at least one Enchant or Sigil in the pool is applicable to
it** — `#lineage` rule 2 read per item, through `BoonDef.IsApplicableTo(slot, kind, abilityIds)`: the
slot and kind match, every ability-id target is one of the item's abilities, and no granted ability already
is. Shipped: `greek` (Athena's Guard), `norse` (Thor's Vigour), `hindu` (Vayu's Breath), **nine boons
each** since part 2 — three of every kind, so every draft offers a real choice in every kind.

## Session and draft (Core, `Mimas.Core.Session`)

`Session(catalog, SessionSetup, seed)` is the best-of-N as a pure state machine (ADR-035): it owns the two
`PlayerBuild`s (the lineage's starting Blessing appended once), the score, the ladder (map ids by
`ladderPosition` then id; round *n* plays position `((n − 1) mod count) + 1`, so it wraps), the revealed
knowledge that outlives a round, and the draft. `Start()` begins round 1; each round is a `MatchState` the
session constructs with a seed from its own `Rng` — round 1's first mover by coin flip, then the loser of
the previous round — with every `RevealedEntry` learned so far imported through `MatchSetup.WithRevealed`.
`Apply(command)` forwards to the round; a `MatchEndedEvent` scores it (`RoundEndedEvent`) and either ends
the session at `rules.series` (`SessionEndedEvent`) or opens a draft: `Draft.Offer` for player 0 then
player 1 with the counts `rules.draft` gives each role (`DraftStartedEvent`); a player with no offers is
picked at once; a `DraftPickCommand(player, offerIndex, reason)` — a timeout is the same command with
`reason: timeout`, submitted by the host, so a replay needs no clock — appends the boon to the build
(`DraftPickedEvent`), and the second pick starts the next round in the same `Apply`. `Phase` is `Round`,
`Draft` or `Over`; `CommandRejectReason` gained `WrongPhase`, `AlreadyPicked`, `BadOffer`.

**Conceding concedes the series** (`#session` rule 7; decided 22 Sep 2026, P8). A `ResignCommand` inside a
round scores that round first — it did end, and the `RoundEndedEvent` says how — and then stops the
session whatever the score; the same command is legal in a draft, where it scores nothing because a draft
is not a round. Either way the last event is one `SessionEndedEvent(winner, score0, score1, reason)`, whose
`SessionEndReason` is `Score`, `Resign` or `Forfeit` (a disconnect past the grace is a resign with
`ResignReason.Disconnect`). `SessionView` carries `NextMapId` (the map the next round plays, null once the
session is over), `MapId` (the running round's, or the next one's between rounds) and `RoundsToWin`; its
`Create` factory is the twin of `PlayerView.Create` and the only way a client builds one.

`Draft` is pure and seeded: `IsApplicable` (the gear can use it), `IsEligible` (applicable, not owned unless
stackable, no exclusive clash; the starting Blessing counts as owned), `Offer` (the eligible pool in ordinal
order, shuffled, then the first Blessing, Enchant and Sigil in kind order while the count allows, then the
rest of the shuffle; index 0 is what a timeout picks). Session events subclass `MatchEvent` so one list
carries everything; `SessionEventFilter.ForPlayer` leaves a viewer their own offers and only "a pick was
made" for the opponent's, and filters round events with the state that produced them; `SessionView.For`
is the per-player projection, which part 2 put on the wire (ADR-036, `docs/networking.md`).
`IBot.ChooseDraft`: the `RandomBot` keeps offer 0.

## Match flow (Core, `Mimas.Core.Match`)

`MatchState(catalog, MatchSetup, seed)` builds the map, spawns one hero per player from that player's
`PlayerBuild` — a `Loadout` (four item ids, each checked against its slot), a lineage and the boons owned
so far in grant order; the loadout-only constructor wraps a bare build — on `spawns.p1` / `p2`, then `Start()` begins turn 1. Commands: `MoveCommand`, `AttackCommand`, `EndTurnCommand` (reason
`Player` or `Timeout`; the server submits the latter when the clock runs out, so replays need no clock)
and `ResignCommand` (reason `Player` or `Disconnect`; the server submits the latter when a dropped seat
does not come back, so a forfeit goes through the same rules path as a move). A resign is legal for either
player at any moment while the match runs, **including off turn**, and is never offered to a bot;
`MatchEndReason` is `Elimination`, `Resign` or `Forfeit`.
`Validate` is pure; `Apply` is the only mutator and returns `MatchEvent`s; `EnumerateLegal` lists every
legal command (bots, highlights). Each turn refreshes the active player's units to `stats.ap`; a unit at 0
hp is dead, stops occupying its tile, and a player with no living unit loses (`MatchEndedEvent`). Clients
only ever see `MatchState.ViewFor(player)` (`PlayerView`) and events passed through `EventFilter`.

**A unit is gear + lineage + boons through one overlay** (ADR-034). `Unit` folds its boons once into a
`BoonOverlay` — stat contributions, attached modifiers, overrides resolved to ability ids, added elements
and tags, Sigil grants — with every entry tagged by the boon that put it there. `Unit.PublicStats` is base
+ items (what gear explains, public); `Unit.Stats` adds the boon stats and floors them by `rules.boons`.
Abilities are innate, then each item's, then each Sigil's grant in boon order. **`MatchState.ResolveAbility`
is the only ability lookup**: it hands back the catalogue's definition with the unit's overlay applied
(numbers floored: cost at `minCost`, range at 1, `minRange` inside the band, apex / climb / jump at 0;
elements and tags added; `damage` left on the def, because it becomes a breakdown line), and a test scans
`MatchState.cs` to keep it so. `ResolveAbilityKnownTo(viewer, …)` applies only the boons that viewer has
been shown: what the observation rule compares against and what a mirror previews with.

**Reveal** (design `#hidden-info`; spec D part 1 §6.5). `BoonRevealedEvent(unitId, boonId, toPlayer)` and
`LineageRevealedEvent(unitId, lineageId, toPlayer)`, routed by `EventFilter` to that player only. Revealing a
boon reveals its whole definition (the modifiers it attaches and the abilities it grants become known too),
and the first boon of a lineage reveals the lineage. Reveal events precede the event that needed them.

| Trigger | Rule |
|---|---|
| Round start | A Health or AP Blessing is revealed to the opponent before `TurnStartedEvent` (D12: the bar shows it). |
| A hidden line changed a result | A `Modifier` line reveals the modifier and then the boon behind it (`Unit.BoonOfModifier`); a `BoonStat` line reveals the boon; a `Nullify` line behaves as a modifier line. |
| An ability's first use | After `AbilityRevealedEvent`, a Sigil's ability reveals the Sigil (`Unit.BoonOfAbility`). |
| An observation contradicts what the opponent knows (D11) | Before AP is spent, the ability as the opponent knows it (`ResolveAbilityKnownTo`) is compared with the ability as it is: (a) a target or destination the known version could not reach reveals every boon overriding an aiming or movement field of this ability (`range`, `minRange`, `apex`, `climb`, `jumpHeight`); (b) a different cost reveals the boons overriding `cost`; (c) an element or tag the known version lacks reveals the boons that added it; (d) a `damage` override is a breakdown line and reveals itself like a Strength Blessing. |

Boon and lineage reveals are keyed apart from ability and modifier reveals inside the revealed set
(`KnowsBoon`, `KnowsLineage` beside `Knows`), because shipped content gives a boon and its modifier one
name. `MatchState.RevealedEntries` exports the set in insertion order; `MatchSetup.WithRevealed` imports it
into the next round. The `PlayerView` carries each unit's `LineageId` (own always; enemy once revealed) and
`Boons` as id-or-null in grant order, so the count of picks is public and their identity is not.

**The client's copy is a mirror** (`MatchState.FromView`, ADR-026): the same class rebuilt from one
player's `PlayerView` and nothing else, so every preview question is answered by the same rules code
online and offline, and it can only ever know what the server chose to send. A mirror answers questions
and refuses to be advanced — `Start`, `Apply` and `TryApply` all throw on one. A mirror's unit is built
from gear plus the **revealed** boons and keeps hidden boon slots (`HiddenBoonCount`), and it refuses a view
whose max hp or AP disagree with what the revealed boons explain, because every hp and AP Blessing is
revealed at round start.

Everything that crosses a socket is encoded by `Mimas.Core.Protocol.Wire`: a hand-written JSON codec with
no attributes, no reflection and no `TypeNameHandling` (ADR-027). See `docs/networking.md`, which is the
wire reference.

## Tile height (`maps/*.json`, `height`)

Each hex may carry an integer `height` (default 0, negatives allowed). Height is a rules value, not a
visual one, and it is a **level**: movement reads levels directly (walking may rise at most `maxClimb` per
step; a jump is blocked by anything more than `jumpHeight` above the tile the unit *stands on*), while
sight and trajectories convert through `rules.heights` (see below). Descending is never limited. Keep
heights rotationally symmetric like terrain (the loader does not enforce this yet;
`Arena4_HeightsAreRotationallySymmetric` in the tests does for arena-4).

## Line of sight and trajectories

An attack declares two independent things, and both must pass: whether the attacker must **see** the
target (`lineOfSight`) and how the attack **travels** (`trajectory`). They are separate because a lobbed
arrow should be able to drop behind cover you cannot see past, while a straight shot needs both.

**The ray.** Sight is a straight line from the attacker's aim point to the target's aim point, in absolute
units: `heights.TileTop(tile) + body.AimHeight` at each end. Every column strictly between them is tested,
and a column blocks when its top *reaches* the line — a graze blocks. The test is integer, cross-multiplied
and never divides: for a sample at parameter `a / b`,

```
column blocks  ⟺  top · b  ≥  h0 · (b − a) + h1 · a
```

A column's top is the tile top raised by any **living body** standing there (a hero, a wall or a pillar:
`tileTop + BodyHeight`). The attacker's and the target's own bodies never count. **Endpoint tiles never
block**, holes in the map (no tile) never block, and **unwalkable terrain is solid**: it stops everything
but `sky` whatever its height. Sight is symmetric between two heroes — if you can see me, I can see you —
because both sides use the same aim height and the sample set mirrors.

**Which columns.** `HexLine.Trace` gives the integer-exact line; where a sample sits on an edge both
flanking hexes are tested, so a shot is never legal in one direction and illegal in the other. A tile is a
prism, so each tile is tested at **both** of its ends: for the `k`-th tile of an `n`-tile flight, at
`a = 2k − 1` and `a = 2k + 1` over `b = 2n`. The lowest point of a monotone flight over a tile is at one of
its edges, so two samples are exact, not an approximation.

**The three trajectories** (`Mimas.Core.Combat`, one resolver each, registered in `TrajectoryRegistry`):

| Mode | Rule |
|---|---|
| `direct` | The same ray as sight, by construction. What a gun or a bolt does. |
| `arc` | An integer parabola through the same endpoints, peaking `max(h0,h1) + apex` at mid-flight. What a bow does. |
| `sky` | Never blocked. Exists in code and test fixtures; no shipped ability uses it. |

The arc's height at `t`, with `d = h1 − h0`, is `h0 + d·t + (4·apex + 2·|d|)·t·(1 − t)`; scaled by `b²` it
is all integers, and a column blocks when `top · b² ≥` that value. A bow arrow (`apex` 3) therefore peaks
three units above the higher of the two aim points: it clears a 6-unit wall two tiles away but not the wall
right next to it, which is the whole point of the field. An unknown trajectory throws — content that gets
this far has already been parsed.

Movement's `requiresLineOfSight` (the teleport) uses the same ray, from the mover's aim point to the aim
height of the destination tile, ignoring the mover's own body (`Movement.LineOfSight.IsClear`). A context
built without `heights` refuses such a movement loudly rather than skipping the check.

## Props (`props/*.json`)

```json
{
  "version": 1, "id": "pillar", "name": "Stone Pillar", "icon": "pillar",
  "description": "A cracked pillar the height of a hero. It blocks shots and can be brought down.",
  "bodyHeight": 6, "aimHeight": 3,
  "stats": { "hp": 10, "defense.weapon": 0, "defense.spell": 0 }
}
```

A prop is a **body** that is not a hero: it stands on a hex, occupies it, and blocks movement, sight and
trajectories with its height, exactly as a hero does (`IBody`, ADR-024).

| Field | Required | Meaning |
|---|---|---|
| `bodyHeight` | yes, 1 or more | Height in units above the tile top: what it blocks with. |
| `aimHeight` | yes, `1..bodyHeight` | Where attacks land on it. Required even for a wall, so a client can place a marker. |
| `stats` | no | `hp` (1 or more) and `defense.<lane>` only. `ap` and `power.*` are errors. Lanes are link-checked against `rules.damageTypes`. |
| `name` / `description` / `icon` | no | Presentation. `name` defaults to the id. |

Rules (design: `#props`):

- A prop **with** `stats.hp` is destructible and a legal target; **without**, it is a permanent wall that
  cannot be hit at all (`NotDamageable`).
- Props are **neutral** (`Owner == -1`) and entirely **public**: both `PlayerView`s list them.
- A destroyed prop is **removed**: nothing reports it any more and its tile is enterable again. Killing one
  never ends the round.
- Body ids continue after the units: with two heroes (0 and 1) the map's props are 2, 3, … in the map's
  authored hex order. `BodySet.All` is units in unit-set order, then props in that order.
- `PropDestroyedEvent(propId)` is public; `AttackResolvedEvent.TargetIsProp` says which kind of body the
  `TargetId` names.

**Placement** is a map field: a hex may carry `"prop": "<id>"`. The id must exist, the hex's terrain must be
walkable, the hex's 180° twin must carry the same prop, and a spawn may carry none. The shipped catalogue
has `pillar` (10 hp, no armour — about one full turn of damage) and `wall` (indestructible).

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
  `ladderPosition` / `spawns.p1` / `spawns.p2` / `hexes` are required, `height`, `effect` and `prop` are
  optional, and duplicate `q,r` pairs are rejected.
- `PropDef.FromJson(json)` parses one prop file (see *Props*).
- `MapData.BuildTileMap(terrains)` resolves terrain to `Tile.Walkable` (unknown terrain id ⇒ throw) and then
  enforces the design invariants:
  1. **Symmetry.** Only `"rotational-180"` is implemented (`"mirror-q"` throws "unsupported symmetry").
     Every hex needs a 180° twin *with the same terrain and the same prop*, not just the same shape.
  2. **Spawns.** Both are on the map, both are walkable, neither carries a prop, and no pair of walkable
     tiles is further apart than the spawn pair.
  3. **Connectivity.** Every walkable tile is reachable from `p1` over walkable tiles.

  `MapData.ValidateProps(props, terrains)` is separate, because it needs the prop catalogue: it runs in the
  catalogue's link phase and rejects an unknown prop id or a prop on unwalkable terrain.

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
all-stone `ring-3.json` sample was removed for that reason). `board-3.json` and `arena-4.json` are playable.
`board-3.json` is a flat radius-3 field with one stone hex at the centre, which is still solid and still
blocks sight. `arena-4.json` has no stone at all: it is 61 grass tiles with a level-2 plateau across
`(-1,0)…(1,0)`, level-1 steps at `(±2,0)`, `(-1,4)` and `(1,-4)`, four `wall` props at `(0,±2)`, `(1,1)`,
`(-1,-1)` and four `pillar` props at `(±2,∓1)`, `(2,2)`, `(-2,-2)` — cover is bodies now, not terrain. The
plateau is what stops a spawn-to-spawn shot.
