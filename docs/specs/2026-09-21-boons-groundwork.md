# Spec D, part 1: Boons groundwork (Core only — definitions, the unit overlay, elements, reveal, draft, session)

_Work order for one autonomous Claude Code session (executing model: **Claude Fable**, decided 21 Sep 2026;
Opus executes part 2). Written 21 Sep 2026 after four question rounds with Rohan and two research passes
(`studio/decisions/RESEARCH-2026-09-21-boons-precedents.md`,
`studio/decisions/RESEARCH-2026-09-21-effect-systems.md`). Design source of truth: `docs/design/index.html`
(`#boons`, `#blessing`, `#enchant`, `#sigil`, `#lineage`, `#draft`, `#session`, `#round`, `#stats`,
`#elements`, `#modifiers`, `#damage`, `#hidden-info`, `#determinism`, `#equipment`, `#abilities`,
`#attacks`, `#trajectories`, `#boots`). Names of existing code were checked on 21 Sep 2026 at commit
`73973ee`. **323 Core tests and 47 server tests are green on `main`.** Feature one-pager:
`studio/features/F-boons.md`; task `T-0009`; part 2 outline: `docs/specs/2026-09-21-boons-in-game-outline.md`._

---

## 0. How to run this session

Read this whole file, then `CLAUDE.md`, then the design sections named above (grep the anchors, never the
whole page), then `docs/data.md`, `docs/architecture.md`, `docs/decisions.md` (ADR-004, ADR-010, ADR-014,
ADR-016, ADR-017, ADR-018, ADR-020..023, ADR-026). Read every file you touch in full before editing it,
in particular `shared/Mimas.Core/Runtime/Units/Unit.cs`, `Match/MatchState.cs`, `Match/MatchSetup.cs`,
`Match/PlayerView.cs`, `Match/MatchEvent.cs`, `Match/EventFilter.cs`, `Combat/DamageCalculator.cs`,
`Combat/DamageBreakdown.cs`, `Combat/Knowledge.cs`, `Combat/AttackTargeting.cs`, `Data/AttackDef.cs`,
`Data/ModifierDef.cs`, `Data/RulesDef.cs`, `Data/StatBlock.cs`, `Data/ItemDef.cs`,
`Movement/MovementDef.cs`, `Content/ContentCatalog.cs`, `Protocol/Wire.cs`, and the test fixtures in
`shared/Mimas.Core.Tests/ContentTests.cs`, `CombatTests.cs`, `DataTests.cs`, `LoadoutTests.cs`,
`MirrorTests.cs`.

Rules for the session:

1. **Autonomous.** No questions. Forks have defaults in §13. Balance numbers are placeholders in JSON and
   are not yours to tune; ship them as written in §5 and say so.
2. **Core only.** This part touches `shared/Mimas.Core`, `shared/Mimas.Core.Tests`,
   `MimasClient/Assets/_Game/Data/**/*.json`, `tools/schemas/`, `.vscode/settings.json`, `docs/`,
   `studio/`. The one protocol touch is additive fields in `Wire.View` / `Wire.ReadView` (§6.6). **No
   server code, no wire messages, no client code, no Unity.** `server/Mimas.Server` must keep compiling
   and its 47 tests must stay green untouched; every existing Core constructor keeps working (§6.2).
3. **Commits:** small commits to `main`, `area: what`, in the order of §12. Do not push. Before every
   commit: `dotnet build Mimas.slnx`, `dotnet test shared/Mimas.Core.Tests`,
   `dotnet test server/Mimas.Server.Tests`, all green.
4. **Golden rules** 3, 4, 5 for everything under `shared/`: no `UnityEngine`, no floats, no `DateTime`, no
   `Guid`, no dictionary iteration order in anything that decides a result, C# 9, no `record`. Every
   number that could ever be tuned is in JSON.
5. **Design first, no drift.** Everything in this spec is `decided` or `proposed` on the design page or
   was decided by Rohan on 21 Sep 2026 (§2). §10 lists the design-page edits that record those decisions;
   the task declares `docs/design/index.html` in `allows_assets`. Where this spec is silent, add an open
   question to the design page and pick the smallest reading; never invent a rule.
6. **Skeletons are honest.** A capability marked *skeleton* in §3 parses from JSON, has a home in Core,
   and **fails closed with `NotSupportedException` naming this spec** the moment a match would need it.
   A test proves both halves. Nothing shipped in `Data/` may use a skeleton field.
7. **Data files and `.meta`.** New JSON under `MimasClient/Assets/_Game/Data/` needs a `.meta` that only
   Unity may write (golden rule 1). If `unity status --format json` says an Editor is `ready`: after each
   data commit's files land, run `unity command menu --path "Assets/Refresh" --timeout 180`, wait for
   `unity command recompile_status` → `completed`, and commit the generated `.meta` files and the
   regenerated `GameDataManifest` asset **with** the JSON. If no Editor is reachable, commit the JSON
   alone, and write in the run report that the `.meta` files and the manifest are the first commit of
   part 2 (the server and the tests read the folder from disk and are unaffected; the client's content
   hash will differ from the server's until then, which `/health` reports and nothing enforces).
8. **Asset guard.** It scans the whole command text, so a read-only `sed`/`awk` over
   `docs/design/index.html`, `studio/ledger.json`, `studio/game.yaml` or `.claude/**` is refused. Use the
   Read and Grep tools for those; never rephrase a command that actually writes. Note each false positive
   in the run report.
9. **No scope creep.** Out of scope: any server or client code, the draft screen, the lineage panel in the
   room, the bot picking a lineage, a third map, tiers/rarity/rerolls, enemy debuffs, statuses, per-turn
   triggers, area shapes, non-damage spells, use counters, presets, accounts.

---

## 1. Goal and result

After this session a pure C# `Session` plays a whole best-of-3 in a test: two `PlayerBuild`s (gear +
lineage) go in; each hero starts round 1 with its lineage's starting Blessing; the round ends; both players
are offered three boons drawn from their lineage's pool by the seeded `Rng`, one Blessing, one Enchant, one
Sigil where possible, filtered by what their gear can use; each picks one (or times out onto the first);
round 2 starts on the next ladder map with the loser moving first; the drafted Enchant's +1 range lets a
shot land that the base bow could not, and **that shot reveals the boon and the lineage to the opponent**;
the drafted Sigil's new spell appears under the crown; a fire immunity zeroes a fire bolt and shows as one
"Immune" line; a Health Blessing is public from round start; everything learned in round 1 is still known
in round 2; and the whole thing replays identically from the seed and the command list.

Concretely, the deliverables:

| # | Deliverable | Where |
|---|---|---|
| 1 | `boons/*.json` and `lineages/*.json` load, link and fail closed | `Data/BoonDef.cs`, `Data/LineageDef.cs`, `Content/ContentCatalog.cs` |
| 2 | Elements on attacks; `elements`, `itemKinds`, `nullify` on modifiers | `Data/AttackDef.cs`, `Data/ModifierDef.cs`, `Combat/DamageCalculator.cs` |
| 3 | A unit built from gear + lineage + boons through one overlay, with source-tagged contributions and floors | `Units/BoonOverlay.cs`, `Units/Unit.cs`, `Match/PlayerBuild.cs` |
| 4 | Every ability read through one resolver that applies the overlay | `MatchState.ResolveAbility` |
| 5 | Boon and lineage reveal, including Enchant-by-observation and stat-at-start | `MatchState`, `MatchEvent.cs`, `EventFilter.cs` |
| 6 | View, mirror and wire carry lineage and boons | `PlayerView.cs`, `Unit.FromView`, `Wire.cs` |
| 7 | Draft offers as a pure seeded function | `Session/Draft.cs` |
| 8 | `Session`: rounds, score, ladder, draft phase, session-long reveals, events, per-player view, filter | `Session/Session.cs` and friends |
| 9 | Skeletons: `hits`, trajectory swap | parse, fail closed, tested |
| 10 | Content: 3 lineages, 21 boons, 6 abilities, 9 modifiers; rules additions; schemas | `Data/`, `tools/schemas/` |
| 11 | Docs: `data.md`, ADR-034/035, design page, `architecture.md`, `networking.md` | `docs/` |

---

## 2. Decisions locked in the question rounds (21 Sep 2026)

| # | Question | Rohan's answer |
|---|---|---|
| D1 | Part split | **Part 1 = Core only** (data, loader, effects, unit, reveal, draft, session). Part 2 = server session flow, client screens, presentation, content growth |
| D2 | Where the best-of-3 lives | **A Core `Session` state machine**, replayable from one seed; the server hosts it in part 2 |
| D3 | New rules for Sigils | **Evolvable core, not every effect at once.** `hits` (multi-hit) and the trajectory swap are **skeletons** with a todo; existing-shape abilities are what launch Sigils grant |
| D4 | Part 2 contents | Online best-of-3 with the draft in the room, plus presentation of boons and reveals |
| D5 | Who builds | **Fable builds part 1**, then writes the part-2 spec against the real code; **Opus executes part 2** |
| D6 | Enchant override target | **Slot or a named ability id.** A slot means every ability the item grants, including later Sigils |
| D7 | Cost floor | **Cost never below `rules.boons.minCost` (1).** Nothing is ever free |
| D8 | Self trade-offs | **Allowed on the owner, with floors** `hp ≥ 1`, `ap ≥ 1`, power/defence `≥ 0` (all in `rules.boons.floors`) |
| D9 | Elements | **A set on the attack**: innate plus added; riders, resists, immunities match any. Element Enchants share an `exclusive` group per slot so the draft never offers two for one item |
| D10 | Comeback | **Symmetric; counts in data.** `rules.draft.offers.winner` and `.loser`, both 3 |
| D11 | Enchant reveal | **On the observation that contradicts what the opponent knows** (§6.5) |
| D12 | Stat reveal | **A Health or AP Blessing is revealed at round start** (what `#stats` rule 4 says); the boon and therefore the lineage become known |
| D13 | First mover | **Round 1 by seeded coin flip, then the loser of the previous round** (`#round` rule 2 confirmed) |
| D14 | Elements at launch | **fire, frost, lightning**; Fire Bolt gets innate `fire` |
| D15 | Starting Blessings | **Greek: Athena's Guard** (+1 Armour weapon, +1 Armour spell). **Norse: Thor's Vigour** (+4 Health). **Hindu: Vayu's Breath** (+1 walk range) |
| D16 | Content in part 1 | **Three lineages, six boons each** (2 Blessings, 2 Enchants, 2 Sigils) plus the starting Blessing, using only *works* effects |
| D17 | Ladder in round 3 | **Wrap**: round n plays ladder position `((n-1) mod length) + 1`; `rules.series.bestOf: 3`. A third map is a later session |
| D18 | Tiers, reroll, cap, exclusivity | No tiers, no reroll, no cap at best-of-3; **`exclusive[]` group tags exist** (Hades' one-boon-per-slot precedent) |

Two rules the design page already has and this spec relies on: revealing a boon reveals its **whole
definition** (like an ability: "then full definition known"), and the same modifier id twice on one unit
does not stack.

---

## 3. Capability matrix (the checklist Rohan asked for)

*works* = built and tested in this part. *skeleton* = JSON parses, Core has the field, a match that needs
it throws `NotSupportedException` naming this spec, a test proves it. *later* = a design question first.

**Blessing** (passive on the character; `requires` forbidden)

| # | Capability | Part 1 | Effect |
|---|---|---|---|
| B1 | +N / −N to hp, ap, power.*, defense.* | works | `stat` |
| B2 | Damage modifier on the unit, either trigger, conditions lane / tags / elements / itemKinds / heightAdvantage | works | `modifier` |
| B3 | Immunity to an element | works | `modifier` whose def has `effect.nullify` |
| B4 | Walk changes: +1 range, +1 climb | works | `abilityOverride` targeting the innate `move` |
| B5 | Negative self-stat trade-offs | works, floored | `stat` with a negative amount |
| B6 | Debuffs on the enemy | later (`q-stats-negative`) | — |
| B7 | Per-turn / triggered effects (heal, burn, first strike) | later (`q-elements-status`) | — |
| B8 | Conditional stats (below half health) | later | — |

**Enchant** (one item; `requires.slot` mandatory, `requires.kind` optional)

| # | Capability | Part 1 | Effect |
|---|---|---|---|
| E1 | Additive override of `range`, `minRange`, `damage`, `cost`, `apex` on the item's abilities, or on one named ability | works | `abilityOverride` |
| E2 | Add an element to every attack of the item | works | `addElement` |
| E3 | Add a tag to the item's attacks | works | `addTag` |
| E4 | Attach a modifier while the item is worn | works | `modifier` |
| E5 | Extra hits (`hits` +1) | skeleton | `abilityOverride` field `hits`; `attack.hits` on abilities |
| E6 | Trajectory or sight swap | skeleton | `abilityOverride` fields `trajectory`, `lineOfSight` (string / bool `value`) |
| E7 | Movement numbers on boots: `range`, `climb`, `jumpHeight` | works | `abilityOverride` |
| E8 | A free use once per turn | later | — |

**Sigil** (new ability on one item; `requires.slot` mandatory)

| # | Capability | Part 1 | Effect |
|---|---|---|---|
| S1 | Grant an attack of an existing shape (direct / arc / sky, single target) | works | `grantAbility` |
| S2 | Grant a movement mode | works | `grantAbility` |
| S3 | Grant a multi-hit attack | skeleton (`attack.hits`) | — |
| S4 | Cone / line / area | later (`q-attacks-multi`) | — |
| S5 | Non-damage spells | later (`q-crown-utility`) | — |
| S6 | Limited uses per round | later | — |

**Session and draft**

| # | Capability | Part 1 |
|---|---|---|
| D1 | N offers, one per kind when possible, filtered by gear, owned, stackable, exclusive | works |
| D2 | Seeded, replayable; timeout is a command that picks the first offer | works |
| D3 | Best-of-N across the ladder (wrapping); builds and revealed sets persist across rounds | works |
| D4 | Offer counts per role in data | works (both 3) |
| D5 | Tiers, rarity, reroll, cap | later |

**Reveal**

| # | Capability | Part 1 |
|---|---|---|
| R1 | Blessing modifier reveals when it changes a result; the boon and lineage follow | works |
| R2 | Health / AP Blessing revealed at round start | works |
| R3 | Strength / Magic / Armour Blessing revealed when its line changes a result | works |
| R4 | Enchant revealed on the contradicting observation (aim, cost, element, damage line, modifier) | works |
| R5 | Sigil revealed on first use of its ability; the boon and lineage follow | works |
| R6 | Lineage revealed with the first boon of it | works |
| R7 | Reveals persist for the session | works |

---

## 4. Architecture (read this before the sections that follow)

Three ideas, and everything else is a consequence of them.

**1. Definitions stay immutable; a unit carries an overlay.** `ItemDef`, `AbilityDef`, `ModifierDef`,
`BoonDef` never change after load. When a unit is built, its boons are folded once into a `BoonOverlay`:
stat contributions, attached modifiers, ability overrides resolved to concrete ability ids, added elements
and tags per ability, granted abilities per item. **Every entry in the overlay carries the id of the boon
that put it there.** Rules code never reads a raw `AbilityDef` for a unit's ability; it asks
`MatchState.ResolveAbility(unit, abilityId)`, which hands back a copy with the overlay applied (research:
Unreal GAS's spec-over-asset split; OpenRA's Info/instance split).

**2. Every contribution to a number carries its source, and the source is what reveal keys on.** A
Blessing's +2 Strength is not folded into the power line; it is its own `DamageLine` of kind `BoonStat`,
id = the boon id, hidden. That is exactly the shape hidden modifier lines already have, so the existing
"a hidden line with a non-zero amount reveals its id" loop in `MatchState.ApplyAttack` reveals boons with
no new machinery. Revealing a boon reveals its whole definition (name, god, effects), and the first boon
of a lineage reveals the lineage.

**3. The session is a pure state machine in Core.** `Session` owns the two builds, the score, the ladder
index, the session-long revealed set, the draft phase and its offers; it constructs one `MatchState` per
round with a seed drawn from its own `Rng`, imports the revealed set into it, and reads the set back at
the round's end. Commands go through `Session.Apply`; events come out as one list a filter trims per
viewer. The server hosts a `Session` the way a room hosts a `MatchState` today.

Namespaces: definitions in `Mimas.Core.Data`, the overlay in `Mimas.Core.Units`, the build in
`Mimas.Core.Match`, the draft and the session in a new `Mimas.Core.Session` folder
(`shared/Mimas.Core/Runtime/Session/`, with its `.meta` handled as §0.7 says).

---

## 5. Data changes (`MimasClient/Assets/_Game/Data/`)

### 5.1 `rules.json` (additions; everything else unchanged)

```json
"elements": ["fire", "frost", "lightning"],
"series": { "bestOf": 3 },
"draft": { "offers": { "winner": 3, "loser": 3 }, "timeoutMs": 20000 },
"boons": { "floors": { "hp": 1, "ap": 1 }, "minCost": 1 }
```

`RulesDef` gains `Elements` (list, may be empty, unique), `Series` (`SeriesDef.BestOf`, odd, ≥ 1),
`Draft` (`DraftDef.OffersWinner`, `OffersLoser` ≥ 0, `TimeoutMs` ≥ 0), `Boons` (`BoonRulesDef.HpFloor`
≥ 1, `ApFloor` ≥ 0, `MinCost` ≥ 0). All four blocks are **required** in `rules.json`; the `RulesDef`
constructor defaults them for hand-built fixtures exactly as `clock` is defaulted today. `IsElement(id)`.

### 5.2 Attacks: `attack.element`, `attack.hits`

- `element` (optional string): the innate element, must be in `rules.elements` (link phase). At most one
  innate. `fire-bolt.json` gets `"element": "fire"`. `AttackDef.Elements` is the **set** (sorted, unique)
  the rules read; at load it holds the innate one or nothing. `HasElement(id)`.
- `hits` (optional int, default 1, ≥ 1): **skeleton**. Parsed and stored as `AttackDef.Hits`; a unit
  whose resolved attack has `Hits > 1` throws `NotSupportedException` at build (§6.7). No shipped file
  sets it; a repo-data test asserts that.

### 5.3 Modifiers: `when.elements`, `when.itemKinds`, `effect.nullify`

- `when.elements[]`: the attack must carry **any** of these elements (link: each in `rules.elements`).
- `when.itemKinds[]`: the ability was granted by an item of **any** of these kinds. An innate ability
  (the walk) has no item and never matches. The calculator learns the kind from
  `attacker.AbilitySourceOf(attack.Id)` resolved through the catalogue.
- `effect.nullify: true`: immunity. When it applies, the total becomes 0 after everything else. With
  `nullify`, `effect.damage` is optional (and, if present, ignored — the loader rejects both being set to
  keep files honest: **`nullify` and `damage` are mutually exclusive**). `ModifierDef.Nullify`,
  `ModifierDef.Damage` is 0 for a nullify modifier.

### 5.4 `boons/*.json` (new folder branch; schema `tools/schemas/boon.schema.json`)

```json
{ "$schema": "../../../../../tools/schemas/boon.schema.json",
  "version": 1, "id": "agni-crown", "name": "Agni's Crown", "kind": "enchant", "lineage": "hindu",
  "description": "Every spell from your crown burns.",
  "requires": { "slot": "crown" },
  "exclusive": ["crown-element"],
  "effects": [ { "type": "addElement", "target": "crown", "element": "fire" },
               { "type": "modifier", "id": "agni-fire" } ],
  "icon": "agni-crown" }
```

| Field | Required | Meaning |
|---|---|---|
| `kind` | yes | `blessing`, `enchant` or `sigil` (`BoonKinds`) |
| `lineage` | yes | A `lineages/*.json` id (link phase) |
| `requires` | enchant, sigil: yes; blessing: **forbidden** | `slot` (one of `ItemSlots`) required; `kind` optional (an item kind string; link: at least one item of that slot has it) |
| `effects[]` | yes, ≥ 1 | Flat records, `type` picks the fields (§5.6). Allowed types per kind in §5.6 |
| `stackable` | no, default false | May be drafted again while owned. Only legal when every effect is `stat` or `abilityOverride` (the others cannot stack: same modifier id, same ability id, same element) |
| `exclusive[]` | no | Group tags; the draft never offers a boon sharing a group with an owned boon. Unique, sorted |
| `name`, `description`, `icon` | no | Presentation; `name` defaults to the id |

### 5.5 `lineages/*.json` (new folder branch; schema `tools/schemas/lineage.schema.json`)

```json
{ "$schema": "../../../../../tools/schemas/lineage.schema.json",
  "version": 1, "id": "hindu", "name": "Hindu", "description": "Pray to Agni, Vayu and Indra.",
  "startingBlessing": "vayu-breath",
  "pool": ["agni-warmth", "indra-wrath", "agni-crown", "indra-mail", "agni-spark", "vayu-wings"],
  "icon": "hindu" }
```

Link rules (each a `ContentError` naming the file): `startingBlessing` exists, is a `blessing`, and has
this lineage; every pool id exists, has this lineage, is unique in the pool; the pool contains **all three
kinds**; and **for every item in the catalogue, at least one Enchant or Sigil in the pool is applicable to
it** — the design's "no gear choice makes a lineage empty", read per item. "Applicable to item I" means:
`requires.slot == I.Slot`; `requires.kind` absent or equal to `I.Kind`; every ability-id `target` is one
of `I.AbilityIds`; and no `grantAbility` names an ability already in `I.AbilityIds` (so a Sigil that grants
`teleport` does not cover the blink boots). `Draft.IsApplicable` (§7.2) is the same predicate asked of
a whole build. The starting Blessing may or may not appear in the pool; the draft filter excludes it either way.

### 5.6 The effect vocabulary (closed; one handler each in `BoonOverlay`)

One flat `BoonEffect` class, all fields nullable, `type` decides which are required; the loader rejects
any field that does not belong to the type and any unknown type (fail closed, like trajectories).

| `type` | Fields | Allowed on | Meaning |
|---|---|---|---|
| `stat` | `key` (a stat key), `amount` (int ≠ 0) | blessing | Added to the unit's stats; negatives allowed; floors apply to the total (§6.3) |
| `modifier` | `id` (a modifier id) | blessing, enchant | The modifier is attached to the unit for the session |
| `abilityOverride` | `target` (a slot, or an ability id), `field`, `amount` (int ≠ 0) **or** `value` (string) | blessing (target must be an innate ability id), enchant (target must be `requires.slot` or an ability id) | Additive integer override of one field, resolved to every ability the target names. Numeric fields: `range`, `minRange`, `damage`, `cost`, `apex`, `hits`, `climb`, `jumpHeight`. Skeleton fields with `value`: `trajectory` (`direct`/`arc`/`sky`), `lineOfSight` (`true`/`false`) |
| `addElement` | `target` (a slot), `element` (in `rules.elements`) | enchant | Every attack the item grants gains the element |
| `addTag` | `target` (a slot), `tag` (string) | enchant | Every attack the item grants gains the tag |
| `grantAbility` | `target` (a slot), `ability` (an ability id, not innate) | sigil | The item grants one more ability, after its innate ones |

Kind rules (loader): a **blessing** has ≥ 1 effect of `stat` / `modifier` / `abilityOverride`; an
**enchant** has ≥ 1 effect of `abilityOverride` / `addElement` / `addTag` / `modifier`, every `target`
equal to `requires.slot` or an ability id; a **sigil** has exactly `grantAbility` effects (≥ 1), every
`target` equal to `requires.slot`. Link: modifier ids exist; ability ids exist; a granted attack's category
must obey the slot's lane rule the item loader already enforces (weapon → `weapon`, crown → `spell`); a
`stat` key must be `hp`, `ap` or `power.<lane>` / `defense.<lane>` for a declared lane; an ability-id
target on an enchant must be granted by at least one item of `requires.slot` (and `requires.kind`) or by a
sigil of the same lineage on that slot; a numeric field must apply to at least one ability the target can
name (`damage` on the boots is an error; `range` is fine on either type).

A field that does not apply to a particular resolved ability (an enchant on the whole weapon slot with
`apex` when one of the weapon's attacks is `direct`) is **ignored for that ability**; a test says so.

### 5.7 Schemas and editor wiring

`tools/schemas/boon.schema.json` and `lineage.schema.json` (new); `ability.schema.json` (`element` enum
fire/frost/lightning, `hits`), `modifier.schema.json` (`elements`, `itemKinds`, `nullify`; `damage` no
longer required), `rules.schema.json` (the four new blocks). `additionalProperties: false` throughout.
`.vscode/settings.json` gains the two new globs. `tools/schemas/README.md` table updated. As the README
says, the element enum is a second place that must change when `rules.elements` changes; say so there.

### 5.8 Shipped content (placeholder numbers; Rohan tunes)

Names follow `<God>'s <thing>` (closes `q-lineage-names`). Every boon hidden by default (`visibility:
hidden` on every new modifier). Descriptions are one line each; write them.

**New abilities (`abilities/`)**

| id | type | Numbers |
|---|---|---|
| `zeus-bolt` | attack, spell, cost 2 | damage 5, range 4, direct, sight, element lightning |
| `dash` | movement, walk, cost 2 | range 3, maxClimb 1 ("a sprint: three hexes for two points") |
| `hammerfall` | attack, weapon, cost 2 | damage 5, range 3, arc apex 2, no sight |
| `long-leap` | movement, jump, cost 2 | range 4, jumpHeight 2 |
| `ember-shot` | attack, weapon, cost 1 | damage 2, range 3, direct, sight, element fire |
| `wind-step` | movement, teleport, cost 1 | range 2, no sight |

**New modifiers (`modifiers/`)**, all hidden

| id | trigger | when | effect |
|---|---|---|---|
| `apollo-eye` | dealDamage | heightAdvantage | +2 |
| `athena-plating` | takeDamage | — | −1 |
| `skadi-hide` | takeDamage | elements [frost] | nullify |
| `thor-charge` | dealDamage | damageTypes [spell], elements [lightning] | +2 |
| `ymir-hide` | takeDamage | damageTypes [weapon] | −1 |
| `agni-warmth` | takeDamage | elements [fire] | nullify |
| `indra-wrath` | dealDamage | damageTypes [spell] | +2 |
| `agni-fire` | dealDamage | damageTypes [spell], elements [fire] | +2 |
| `indra-mail` | takeDamage | damageTypes [spell] | −1 |

**Boons (`boons/`)**: starting Blessing first, then 2 / 2 / 2.

| Lineage | id | kind | requires | effects |
|---|---|---|---|---|
| greek | `athena-guard` (start) | blessing | — | stat defense.weapon +1; stat defense.spell +1 |
| greek | `apollo-eye` | blessing | — | modifier apollo-eye |
| greek | `zeus-favour` | blessing | — | stat power.spell +2 |
| greek | `apollo-bowstring` | enchant | weapon | abilityOverride weapon range +1 |
| greek | `athena-plating` | enchant | armour | modifier athena-plating |
| greek | `zeus-bolt` | sigil | crown | grantAbility crown zeus-bolt |
| greek | `hermes-heels` | sigil | boots | grantAbility boots dash |
| norse | `thor-vigour` (start) | blessing | — | stat hp +4 |
| norse | `berserker-blood` | blessing | — | stat power.weapon +2; stat hp −2 |
| norse | `skadi-hide` | blessing | — | modifier skadi-hide |
| norse | `thor-charge` | enchant | crown, exclusive [crown-element] | addElement crown lightning; modifier thor-charge |
| norse | `ymir-hide` | enchant | armour | modifier ymir-hide |
| norse | `thor-hammerfall` | sigil | weapon | grantAbility weapon hammerfall |
| norse | `odin-leap` | sigil | boots | grantAbility boots long-leap |
| hindu | `vayu-breath` (start) | blessing | — | abilityOverride move range +1 |
| hindu | `agni-warmth` | blessing | — | modifier agni-warmth |
| hindu | `indra-wrath` | blessing | — | modifier indra-wrath |
| hindu | `agni-crown` | enchant | crown, exclusive [crown-element] | addElement crown fire; modifier agni-fire |
| hindu | `indra-mail` | enchant | armour | modifier indra-mail |
| hindu | `agni-spark` | sigil | weapon | grantAbility weapon ember-shot |
| hindu | `vayu-wings` | sigil | boots | grantAbility boots wind-step |

Boon ids, modifier ids and ability ids share names on purpose where one boon is one modifier; the
namespaces are separate tables, and the reveal set keys boon ids through `BoonRevealedEvent`, never
through a modifier id (§6.5), so no collision is possible. Pools list the six non-starting boons.

**Lineages (`lineages/`)**: `greek` ("Pray to Apollo, Athena and Hermes"), `norse` ("Pray to Thor, Odin
and Skadi"), `hindu` ("Pray to Agni, Vayu and Indra"), each with its starting Blessing and six-boon pool.

`docs/data.md` line 18's row for `boons/` and a new row for `lineages/` change to "loaded".

---

## 6. Core changes (`shared/Mimas.Core/Runtime`)

### 6.1 Definitions and loading

- `Data/BoonDef.cs`: `BoonKinds` (`Blessing`, `Enchant`, `Sigil`, `IsKnown`), `BoonRequirement` (`Slot`,
  `Kind` nullable), `BoonEffectTypes`, `AbilityFields` (the closed field list, with `IsNumeric(field)`,
  `AppliesTo(field, AbilityDef)`), `BoonEffect` (flat, nullable fields, validated per type in
  `FromJson`), `BoonDef : IContentDef` (`Id`, `Name`, `Description`, `Icon`, `Kind`, `LineageId`,
  `Requires`, `Effects`, `Stackable`, `ExclusiveGroups`; `IsBlessing` etc.). `FromJson` follows the
  `ModifierDef.FromJson` pattern: `MapJson` helpers, `MapLoadException` with the file's `where`.
- `Data/LineageDef.cs`: `Id`, `Name`, `Description`, `Icon`, `StartingBlessingId`, `PoolIds`.
- `Content/ContentCatalog.cs`: `BoonsFolder = "boons/"`, `LineagesFolder = "lineages/"`, tables `Boons`,
  `Lineages`; phase-1 branches; phase-2 link rules of §5.4–5.6; the "unrecognised content file" message
  lists the two new folders. `GetBoon(id)` / `GetLineage(id)` throwing helpers like `GetItemForSlot`.
- `RulesDef`, `AttackDef`, `ModifierDef` as §5.1–5.3.

### 6.2 The build and the unit

- `Match/PlayerBuild.cs`: `Loadout Loadout`, `string LineageId` (nullable), `IReadOnlyList<string>
  BoonIds` (grant order, may repeat only for stackable boons). Immutable, value-equal. `WithBoon(id)`
  returns a new build. `PlayerBuild(Loadout)` = no lineage, no boons.
- `Match/MatchSetup.cs`: new constructor `(mapId, PlayerBuild p0, PlayerBuild p1, firstPlayer = 0)`;
  the existing `(mapId, Loadout, Loadout, firstPlayer)` constructor **stays** and wraps each loadout in
  a bare build, so `Room.cs`, the bot options and every existing test compile untouched. `BuildOf(player)`.
  `WithModifier` stays (the server bot's two passives use it until part 2 moves them onto a lineage).
  New: `WithRevealed(viewer, unitId, id)` and `RevealedEntries` (what the session imports, §6.5).
- `Units/BoonOverlay.cs`: built once by `Unit`'s constructor from `(RulesDef, items, boons)`:

  | Table | Entry |
  |---|---|
  | `StatContributions` | `(boonId, key, amount)` in boon order |
  | `Modifiers` | `(boonId, modifierId)` |
  | `Overrides` | `(boonId, abilityId, field, amount / value)` — a slot target expanded to the item's innate abilities **and** the abilities sigils granted to that item, in grant order |
  | `AddedElements`, `AddedTags` | `(boonId, abilityId, element / tag)` |
  | `Grants` | `(boonId, itemId, abilityId)` |

  Queries the rules need: `BoonsFor(abilityId, predicate)` style helpers, or simply the public lists;
  keep it a plain data class with linear scans (two heroes, a handful of boons).
- `Units/Unit.cs`: new constructor `(id, owner, position, RulesDef rules, IReadOnlyList<ItemDef> items,
  string lineageId, IReadOnlyList<BoonDef> boons)`; the existing gear constructor stays and passes no
  boons. New members: `LineageId`, `BoonIds` (grant order), `Overlay`, `PublicStats` (base + items, what
  gear explains), `Stats` (base + items + boon stats, **floored** by `rules.Boons`), `BoonOfModifier(id)`,
  `BoonOfAbility(id)` (null for innate and item abilities), `AbilitySourceOf` unchanged (a sigil ability's
  source is the **item**, public, "an extra ? under the item is a fair tell"). Ability order: innate,
  then each item's abilities, then each boon's grants in boon order. `AddModifier` gains an overload
  `(modifierId, boonId)`; the parameterless one stays for `MatchSetup.WithModifier`. Multi-hit and
  trajectory-swap fields in the overlay throw `NotSupportedException` here (§6.7).
- Floors: `hp = max(rules.Boons.HpFloor, sum)`, `ap = max(ApFloor, sum)`, every other key `max(0, sum)`.
  `MaxHp` follows `Stats`.

### 6.3 One resolver for abilities

`MatchState.ResolveAbility(Unit unit, string abilityId, out AbilityDef def)` replaces the six
`Catalog.Abilities.TryGet` / `Catalog.GetMovement` reads at `MatchState.cs:299, 370, 504, 535, 548, 561,
577`. It looks the id up, then applies the unit's overlay: `AttackDef.WithOverlay(...)` (numeric deltas
**except `damage`**, which stays on the def and becomes a breakdown line so the preview-versus-actual
rule reveals it — §6.4 and §6.5 (d); added elements and tags; floors: `cost ≥ rules.Boons.MinCost`,
`range ≥ 1`, `1 ≤ minRange ≤ range`, `apex ≥ 0`, `hits ≥ 1`) and `MovementDef.WithOverlay(...)` (`range ≥ 1`, `climb ≥ 0`,
`jumpHeight ≥ 0`, `cost ≥ MinCost`). Both `With…` methods copy every other field, like `WithTrajectory`
does today, and `WithTrajectory` stays. A second form, `ResolveAbilityKnownTo(viewer, unit, abilityId)`,
applies only the overlay entries whose boon `viewer` has been shown (or all of them for the owner): it is
what the observation rule (§6.5) and a mirror's preview use. Nothing else in Core reads a unit's ability
def any other way; a test greps for it (§8, `NoRawAbilityLookupsOutsideResolver`).

### 6.4 Damage

`DamageCalculator.Compute` keeps its signature; the `attack` it receives is the **resolved** def.

- Power and defence lines come from `PublicStats`. Then, for each `StatContributions` entry on the
  attacker for `power.<lane>` and on the target for `defense.<lane>` (a target that is a prop has none),
  one `DamageLine(DamageLineKind.BoonStat, boonId, owner, unitId, ±amount, hidden: true)`, in boon
  order; and for each `Overrides` entry with field `damage` on this attack, one more such line on the
  attacker's side (the total is floored at 0 after all lines, as today, so a negative override can never
  heal). Visibility exactly like hidden modifier lines: a viewer who has not been shown the boon gets an
  unknown count instead of the line. `IBody` gains `PublicStats` and `BoonStatContributions(key)`; a
  prop returns its stats and an empty list.
- `Situation` gains the attack's elements (from the resolved def) and the source item's kind (from
  `attacker.AbilitySourceOf(attack.Id)` through the catalogue; null for innate). `Satisfies` checks
  `elements` (any) and `itemKinds` (any).
- Nullify: after the modifier groups, if any applicable modifier with `Nullify` was found, add one line
  `DamageLine(DamageLineKind.Nullify, modifierId, owner, unitId, −(sum so far, floored at 0), hidden)`
  and set `DamageBreakdown.Nullified = true`. A hidden nullify the viewer cannot see counts as an
  unknown like any hidden modifier. The tooltip shows it as "Immune (fire)" from the modifier's
  `when.elements`; the total is 0. Order never matters: all flat, floor, then nullify (`#modifiers` rule 2).
- `DamageBreakdown` gains `Nullified` and `FindBoon(boonId)`.

### 6.5 Reveal

Events, all `MatchEvent`s routed by `EventFilter` to `ToPlayer` only:
`BoonRevealedEvent(unitId, boonId, toPlayer)`, `LineageRevealedEvent(unitId, lineageId, toPlayer)`.

`MatchState.RevealBoon(unit, boonId, events)`: adds `(other, unit.Id, boonId)` to the revealed set; if
new, appends `BoonRevealedEvent`; then if the unit's lineage is not yet revealed to `other`, adds it and
appends `LineageRevealedEvent`. **Reveal events precede the event that needed them** (`#hidden-info`
rule 2), so every call site below runs before it appends its own event.

When it fires:

| Trigger | Where | Rule |
|---|---|---|
| Round start | `Start()` | For each unit and each boon with a `stat` contribution on `hp` or `ap`: reveal to the opponent (D12). Events are the first in the list `Start()` returns, before `TurnStartedEvent` |
| Hidden line changed a result | `ApplyAttack` loop over `breakdown.Lines` | A `Modifier` line whose modifier has a boon (`unit.BoonOfModifier`) reveals the modifier (existing `ModifierRevealedEvent`) **and then** the boon; a `BoonStat` line reveals the boon; a `Nullify` line behaves as a modifier line |
| Ability first used | `RevealAbility` | After the existing `AbilityRevealedEvent`, if `unit.BoonOfAbility(id)` is set, reveal that boon (a Sigil) |
| **Observation contradicts knowledge** (D11) | `ApplyAttack`, `ApplyMove`, before spending AP | Let `known = ResolveAbilityKnownTo(opponent, unit, id)` and `actual = ResolveAbility(unit, id)`. (a) **Aim / movement**: if `AttackTargeting.Check(... known ...)` refuses the target, or `_resolvers.Validate(MoveContext(unit, known), destination)` refuses it, reveal every unrevealed boon in the overlay that overrides an aiming or movement field of this ability (`range`, `minRange`, `apex`, `climb`, `jumpHeight`). (b) **Cost**: if `known.Cost != actual.Cost`, reveal the boons overriding `cost` for this ability. (c) **Element / tag**: if `actual` carries an element or tag `known` does not, reveal the boons adding them (the element is visible in flight). (d) **Damage**: nothing to do here; a `damage` override is applied inside the resolved def, so it must **also** appear in the breakdown as its own hidden `BoonStat`-style line — to keep one rule, the calculator emits the `damage` override as a `DamageLine(DamageLineKind.BoonStat, boonId, Attacker, …)` and `WithOverlay` leaves `Damage` itself untouched; that way "the actual differs from the preview" reveals it exactly like a Blessing on Strength |

Revealed-set import/export: `MatchSetup.WithRevealed` seeds `_revealed` in the constructor;
`MatchState.RevealedEntries` (an `IReadOnlyList<(int viewer, int unitId, string id)>`-shaped readonly
struct list, in insertion order) exports it. `Knows` is unchanged. Boon ids, ability ids and modifier ids
live in one set keyed by unit; they cannot collide in meaning because each event names its kind.

### 6.6 View, mirror, wire

- `UnitView` gains `LineageId` (own unit: always; enemy: the id once revealed, else null) and `Boons`
  (`List<KnownEntry>` in grant order; hidden entries have a null id, so the **count** of drafted boons is
  public, as the design says "the opponent sees only that a pick was made"). `PlayerView.Build` fills them
  from `state.Knows`.
- `Unit.FromView(view, catalog)`: build from gear + **revealed boons** (the ones with an id) through the
  boon constructor; then align the ability list with the view exactly as today, generalised: walk the
  view's abilities; a revealed entry must equal the next id of the built list (else throw, as today); a
  hidden entry becomes a `HiddenSlot` with its source item. Same for modifiers and for boons
  (`HiddenBoonSlots`, `HiddenBoonCount`). Then `Restore(hp, ap)`. Assert `MaxHp == view.MaxHp` and
  `ApPerTurn == view.ApPerTurn`: every hp/ap boon is revealed at round start (D12), so a mismatch is a
  bug, not a hidden value.
- `MatchState.FromView`: seeds `_revealed` with revealed boons and lineages too. `PreviewAgainst` on a
  mirror adds the hidden boon count to the unknowns like it adds hidden modifiers today.
- `Protocol/Wire.cs`: `View` / `ReadView` gain the unit's `lineage` (string or null) and `boons` (array
  of id-or-null, like `modifiers`). Additive; `ProtocolTests` round-trips them. `docs/networking.md`'s
  view table gets the two fields.

### 6.7 Skeletons (fail closed, tested)

- `attack.hits > 1` on any resolved ability of a unit, or an `abilityOverride` on `hits`: `Unit`'s
  constructor throws `NotSupportedException("multi-hit attacks are not implemented (spec D part 1 §6.7, E5/S3)")`.
- `abilityOverride` with field `trajectory` or `lineOfSight`: same exception, "trajectory swap".
- Both parse, both pass the catalogue, both are reachable only from test fixtures. Tests:
  `Skeleton_Hits_ParsesAndFailsClosed`, `Skeleton_TrajectorySwap_ParsesAndFailsClosed`,
  `ShippedContent_UsesNoSkeletonField`.

---

## 7. Draft (`Runtime/Session/Draft.cs`)

Pure, static, seeded.

```csharp
public static bool IsApplicable(ContentCatalog catalog, PlayerBuild build, BoonDef boon)   // 7.2
public static bool IsEligible(ContentCatalog catalog, PlayerBuild build, BoonDef boon)     // 7.3
public static void Offer(ContentCatalog catalog, PlayerBuild build, int count, Rng rng, List<string> into) // 7.4
```

7.1 The candidate pool is the build's lineage's `pool`.

7.2 **Applicable** = the gear can use it: a Blessing always; an Enchant or Sigil when the item in
`requires.slot` matches `requires.kind` (if given), every ability-id target is an ability the build's unit
would have (items plus owned boons' grants), and no `grantAbility` names an ability the unit already has.

7.3 **Eligible** = applicable, and not owned (unless `stackable`), and no `exclusive` group shared with an
owned boon (the starting Blessing counts as owned).

7.4 **Offer**: take the eligible ids in ordinal order, `rng.Shuffle` them, then pick the first Blessing,
the first Enchant, the first Sigil that appear in that shuffled order (in kind order, while `count`
allows), then fill the remaining slots from the shuffled order skipping those already taken. Fewer than
`count` eligible ⇒ fewer offers, possibly none. The result is the offer list in that order; index 0 is
what a timeout picks. Deterministic: same catalogue, build, count, seed ⇒ same list (test).

---

## 8. Session (`Runtime/Session/`)

- `SessionSetup(PlayerBuild p0, PlayerBuild p1)`: both builds need a lineage. Boons may be pre-owned
  (tests, and a part-2 reconnect path).
- `SessionPhase { Round, Draft, Over }`.
- `Session(ContentCatalog catalog, SessionSetup setup, uint seed, MovementResolverRegistry = null,
  TrajectoryRegistry = null)`. Members: `Rng` (the session's), `Phase`, `Round` (1-based, 0 before
  start), `Score(player)`, `BuildOf(player)` (grows as boons are picked; the starting Blessing is appended
  at construction), `Match` (the current `MatchState`, null in `Draft` and `Over`), `OffersOf(player)`,
  `HasPicked(player)`, `IsOver`, `Winner`, `RevealedEntries`, `Ladder` (map ids sorted by
  `ladderPosition`, then id), `RoundsToWin = bestOf / 2 + 1`, `LastRoundLoser`.
- `Start()` → events: starts round 1 (`StartRound` below).
- `StartRound()` (private; public `Start()` only for round 1): map = `Ladder[(Round-1) % Ladder.Count]`;
  first player = round 1: `Rng.Range(0, 2)`; later: the loser of the previous round (D13); seed =
  `Rng.NextUInt()`; `MatchSetup(map, build0, build1, first)` with every `RevealedEntries` imported; `Match
  = new MatchState(...)`; emit `RoundStartedEvent(round, mapId, firstPlayer)` then `Match.Start()`'s events.
- `Validate(Command)`: `Round` ⇒ `Match.Validate`; `Draft` ⇒ a `DraftPickCommand` for a player who has
  not picked, with `OfferIndex` in range (or any index when that player has zero offers, which counts as a
  pick of nothing); `Over` ⇒ `MatchOver`. `CommandRejectReason` gains `WrongPhase`, `AlreadyPicked`,
  `BadOffer`.
- `Apply(Command)` → `IReadOnlyList<MatchEvent>`: in `Round`, forward to `Match.Apply`, append; if the
  events contain `MatchEndedEvent`: score the winner, export `Match.RevealedEntries` into the session's
  set, append `RoundEndedEvent(round, winner, reason, score0, score1)`; if a score reached `RoundsToWin`
  ⇒ `Phase = Over`, `Match = null`, append `SessionEndedEvent(winner, score0, score1)`; else `Phase =
  Draft`, `Match = null`, compute offers for both players with `Draft.Offer` (`count` from
  `rules.Draft`: loser's count for the round's loser, winner's for the winner; player 0 first, then
  player 1, so the `Rng` sequence is fixed), append `DraftStartedEvent(round, offers0, offers1)`. In
  `Draft`, a `DraftPickCommand(player, offerIndex, reason)` appends the boon to that player's build,
  appends `DraftPickedEvent(player, boonId or null)`; when both have picked, `StartRound()` runs inside
  the same call and its events are appended.
- `EnumerateLegal(player, into)`: `Round` ⇒ the match's; `Draft` ⇒ one `DraftPickCommand` per offer
  (reason `Player`) for a player who has not picked; else empty.
- `DraftPickCommand : Command` (`OfferIndex`, `DraftPickReason { Player, Timeout }`). The host submits
  the timeout with index 0 when `rules.draft.timeoutMs` elapses; Core never reads a clock.
- Session events (`Session/SessionEvent.cs`, subclasses of `MatchEvent` so one list and one filter carry
  everything): `RoundStartedEvent`, `RoundEndedEvent`, `DraftStartedEvent`, `DraftPickedEvent`,
  `SessionEndedEvent`.
- `SessionEventFilter.ForPlayer(events, viewer, session, into)`: `DraftStartedEvent` ⇒ a copy with only
  the viewer's offers (the opponent's list empty); `DraftPickedEvent` for the other player ⇒ a copy with
  `BoonId = null` ("a pick was made"); match events ⇒ `EventFilter.ForPlayer` with the match that produced
  them (the filter is called before `Match` is nulled, so pass the state explicitly); everything else
  through.
- `SessionView.For(session, viewer)`: `Round`, `Phase`, `Score0/1`, `IsOver`, `Winner`, `MyBuild`
  (lineage + boon ids), `OpponentLineageId` (revealed or null), `OpponentBoons` (`KnownEntry` list),
  `MyOffers`, `IHavePicked`, `OpponentHasPicked`, `Match` (`PlayerView` or null). Part 2 encodes it.
- `Bots/IBot` gains `Command ChooseDraft(Session session, int player)`; `RandomBot` returns the pick of
  offer 0 (Rohan: "the bot picks the first offer"), or null when it is not in a draft or has picked.

---

## 9. Tests (`shared/Mimas.Core.Tests`; one behaviour each, `Method_Scenario_Expected`)

New files: `BoonContentTests.cs`, `BoonOverlayTests.cs`, `BoonRevealTests.cs`, `DraftTests.cs`,
`SessionTests.cs`; additions to `DataTests.cs`, `CombatTests.cs`, `MirrorTests.cs`, `ProtocolTests.cs`,
`ContentTests.cs`. Fixtures: extend `CombatFixtures.Files()` with one lineage and a boon of each kind and
each effect type, plus the two skeleton boons; add `ContentFixtures.BuildFor(loadout, lineage, boons)`.

Loader and content (at least):
`Boon_UnknownEffectType_FailsClosed`, `Boon_FieldNotOfItsType_IsAnError`,
`Boon_BlessingWithRequires_IsAnError`, `Boon_SigilWithoutGrant_IsAnError`,
`Boon_EnchantTargetNotItsSlot_IsAnError`, `Boon_StackableWithModifier_IsAnError`,
`Boon_GrantedSpellOnWeapon_IsAnError`, `Boon_UnknownLineage_IsAnError`,
`Lineage_PoolMissingAKind_IsAnError`, `Lineage_PoolLeavesAnItemUncovered_IsAnError`,
`Lineage_StartingBlessingOfOtherLineage_IsAnError`, `Modifier_NullifyAndDamage_AreExclusive`,
`Attack_UnknownElement_IsAnError`, `Rules_MissingBoonsBlock_IsAnError`,
`Load_ShippedDataFolder_HasNoErrors` (existing, now covering 39 new files),
`ShippedLineages_CoverEveryItem`, `ShippedContent_UsesNoSkeletonField`,
`ContentHash_ChangesWhenABoonChanges`.

Overlay and resolver:
`Unit_WithBoons_StatsAreBaseItemsPlusBoons`, `Unit_NegativeStat_IsFlooredFromRules`,
`Unit_CostOverride_NeverBelowMinCost`, `Unit_SlotOverride_AppliesToEveryAbilityOfTheItemIncludingSigils`,
`Unit_AbilityOverride_AppliesToThatAbilityOnly`, `Unit_FieldNotApplicable_IsIgnoredForThatAbility`,
`Unit_AddElement_EveryCrownSpellCarriesIt_AndInnateStays`, `Unit_Grant_OrderIsInnateItemsThenBoons`,
`Unit_GrantSourceItem_IsPublicAndBoonIsNot`, `Unit_MoveOverride_ExtendsWalkRange`,
`ResolveAbility_IsTheOnlyAbilityLookup` (a source scan of `MatchState.cs` for `Catalog.Abilities.TryGet`
/ `GetMovement` / `GetAttack` outside the resolver), `Skeleton_Hits_ParsesAndFailsClosed`,
`Skeleton_TrajectorySwap_ParsesAndFailsClosed`.

Damage:
`Damage_BoonStat_IsItsOwnHiddenLine`, `Damage_ElementRider_MatchesAddedElement`,
`Damage_ItemKindCondition_MatchesGrantingItem_NotInnate`, `Damage_Nullify_ZeroesTotalAfterEverything`,
`Damage_HiddenNullify_CountsAsUnknownInPreview`, `Damage_DamageOverride_IsALineNotABaseChange`.

Reveal:
`Start_HpBlessing_RevealsBoonAndLineageBeforeTurnStarted`, `Attack_StrengthBlessing_RevealsOnFirstHit`,
`Attack_BlessingModifier_RevealsModifierThenBoonThenLineage`,
`Attack_RangeEnchant_RevealsWhenShotBeyondKnownRange`,
`Attack_RangeEnchant_StaysHiddenWhenShotWithinKnownRange`,
`Attack_CostEnchant_RevealsOnUse`, `Attack_AddedElement_RevealsOnFirstUse`,
`Move_WalkRangeBlessing_RevealsWhenDestinationBeyondKnownRange`, `Attack_Sigil_RevealsBoonOnFirstUse`,
`Reveal_SecondBoonOfSameLineage_DoesNotRepeatLineageEvent`, `Filter_RevealEvents_GoOnlyToThatPlayer`,
`View_EnemyBoons_AreCountedButNotNamed_UntilRevealed`, `Mirror_RebuildsRevealedBoonsAndKeepsHiddenSlots`,
`Mirror_MaxHpMatchesView_BecauseHpBoonsRevealAtStart`, `Wire_View_RoundTripsLineageAndBoons`,
`Sweep_NoHiddenBoonLineReachesTheOpponentBeforeItsReveal` (extend the existing whole-match sweep).

Draft:
`Draft_OffersOneOfEachKindWhenPossible`, `Draft_ExcludesEnchantTheGearCannotUse`,
`Draft_ExcludesOwnedUnlessStackable`, `Draft_ExcludesExclusiveGroupClash`,
`Draft_ExcludesGrantOfAnAbilityAlreadyHeld`, `Draft_SameSeed_SameOffers`,
`Draft_FewerEligibleThanCount_OffersFewer`, `Draft_StartingBlessing_IsOwned`.

Session:
`Session_Start_RoundOne_CoinFlipFromSeed`, `Session_RoundTwo_LoserMovesFirst`,
`Session_RoundThree_WrapsToLadderPositionOne`, `Session_RoundEnd_ScoresAndOffersBothPlayers`,
`Session_Pick_AppliesImmediately_NextRoundHasTheBoon`, `Session_Timeout_PicksFirstOffer`,
`Session_BothPicked_NextRoundStartsInSameApply`, `Session_TwoWins_EndsSession`,
`Session_Reveals_PersistIntoNextRound`, `Session_Filter_OpponentSeesOnlyThatAPickWasMade`,
`Session_Replay_SameSeedAndCommands_SameEvents` (golden: two sessions, RandomBot on both seats plus
`ChooseDraft`, identical event `ToString()` logs), `Session_ResignInRoundOne_CountsAsRoundLoss`,
`Session_Validate_WrongPhase_IsRejected`, `RandomBot_ChooseDraft_PicksFirstOffer`.

The `Session_Replay…` golden test is the one that proves determinism end to end; keep its two seeds and
its expected event count in the test, not in a file.

---

## 10. Documentation

1. `docs/data.md`: the file table rows for `boons/` and `lineages/`; new sections **Boons**, **Lineages**,
   **Session and draft (Core, `Mimas.Core.Session`)**; additions to *Rules*, *Attack abilities*
   (`element`, `hits`), *Modifiers* (`elements`, `itemKinds`, `nullify`), *Match flow* (`PlayerBuild`,
   `ResolveAbility`, reveal rules table), *Loading and validation* (the link rules of §5). Replace the
   "Effects are a small expression list…" convention line with the real vocabulary.
2. `docs/decisions.md`: **ADR-034** "Boons are a per-unit overlay over immutable definitions; every
   contribution carries its boon id, and that id is what reveal keys on" (context: §4 idea 1 and 2;
   options: mutating copies of defs per unit / folding boon stats into the power line / a generic
   event-driven effect system; consequences: one resolver, no raw lookups, skeletons fail closed).
   **ADR-035** "The session is a Core state machine that constructs the rounds" (options: the room's
   rematch loop; a session in server code; consequences: the server hosts it in part 2, replay covers
   drafts, clocks stay outside Core). Four sentences each in the right place.
3. `docs/architecture.md`: the `Session` box above `MatchState`; `docs/networking.md`: the two view fields.
4. `docs/design/index.html` (declared in `allows_assets`; use the Edit tool):
   - `#boons`: `data-impl="partial"`; the data block replaced by §5.4's; rules gain: effect vocabulary as
     §5.6 (decided), `stackable` and `exclusive` (decided), "revealing a boon reveals its whole
     definition". Close `q-boons-cap` (none at best-of-3) and `q-boons-exclusive` (yes, `exclusive[]`).
   - `#blessing`, `#enchant`, `#sigil`: `data-impl="partial"`; `#enchant` gains the target rule (D6) and
     the cost floor (D7); `#blessing` gains the trade-off floor rule (D8).
   - `#lineage`: `data-impl="partial"`; the three starting Blessings (D15) close `q-lineage-starting`;
     `q-lineage-names` closed ("<God>'s <thing>"); the pool coverage rule reads "per item".
   - `#draft`: `data-impl="partial"`; offer counts in `rules.draft` (D10) close `q-draft-comeback`;
     `q-draft-tiers` and `q-draft-reroll` marked "later, not at launch"; timeout in `rules.draft.timeoutMs`.
   - `#session`: `data-impl="partial"`, drift note replaced ("`Mimas.Core.Session.Session` since 21 Sep
     2026; the server hosts it in part 2"); `q-session-length` (best of 3, `rules.series.bestOf`) and
     `q-session-map-order` (strict, wrapping) closed.
   - `#round`: `q-round-first` closed (D13).
   - `#stats`: rule 3 gains the floors (D8) and `q-stats-negative` answered "owner only; enemy debuffs
     later".
   - `#elements`: `data-impl="partial"`; `q-elements-list` closed (D14); elements are a set on an attack (D9).
   - `#modifiers`, `#damage`: drift notes updated (elements, itemKinds, nullify exist).
   - `#hidden-info`: `q-hidden-enchant-obs` closed with §6.5's table; the drift note updated.
   - `#attacks`: `hits` skeleton noted; `#trajectories`: `q-trajectory-enchant` gains "skeleton exists;
     the rule is still open".
   - `#data-model`: `lineages/` and `boons/` rows → "in code"; `rules.json` row gains the four blocks.
   - Decision log: one row per D6–D18 dated 2026-09-21; changelog entry.
5. `studio/STATE.md` rewritten at the end; run report `studio/runs/R-<date>-T-0009.md` appended to as
   you go (§0.8 false positives included).

---

## 11. Verification commands (run all before the final commit)

```bash
dotnet build Mimas.slnx
dotnet test shared/Mimas.Core.Tests
dotnet test server/Mimas.Server.Tests
node E:/Studios/Trinetra-Game-Studio/tools/ladder/ladder.mjs --project mimas --task T-0009
git status --short        # only files you touched; no Library/, no bin/, no obj/
```

The ladder rungs for T-0009 are the two `dotnet test` runs and the verifier; no Unity rung.

---

## 12. Commit plan (each step green before committing; `git add` only the files you touched)

1. `core: elements, hits, itemKinds and nullify in data` — §5.1–5.3 loaders, `RulesDef` blocks, schema
   updates, `rules.json` and `fire-bolt.json`, calculator conditions (not yet nullify lines). Existing
   tests green.
2. `core: boon and lineage definitions load and link` — §6.1, `boon.schema.json`, `lineage.schema.json`,
   `.vscode/settings.json`, loader tests.
3. `core: a unit is gear plus lineage plus boons through one overlay` — §6.2, §6.3, floors, skeletons,
   overlay tests.
4. `core: boon stat lines, element riders, immunity` — §6.4, damage tests.
5. `core: boons and lineages reveal` — §6.5, §6.6, reveal / mirror / wire tests.
6. `core: draft offers` — §7, draft tests.
7. `core: the session plays a best-of-3 with a draft between rounds` — §8, session tests, the golden replay.
8. `data: three lineages, twenty-one boons, six abilities, nine modifiers` — §5.8, repo-data tests
   (with `.meta` and the manifest if an Editor was reachable, §0.7).
9. `docs: boons groundwork — data.md, ADR-034/035, design page, STATE` — §10; the run report.

---

## 13. Defaults for forks the session may hit

| Fork | Default |
|---|---|
| Newtonsoft `JObject` vs a hand parser for `effects[]` | `JObject`, like every loader; one `switch` on `type` |
| Where floors are applied | Once, on the unit's total `Stats`; never on individual contributions |
| A `stat` on a key the hero does not have (`power.melee` in a fixture) | Legal if the lane is declared; adds the key |
| Two boons override the same field of the same ability | Both apply, additively, in boon order; floors on the total |
| The same modifier attached by two boons | One entry on the unit (set semantics as today); the first boon in grant order owns it for reveal |
| A Blessing `abilityOverride` on `move` and a sigil-granted `dash` | Only `move` changes; a Blessing's target is one innate id |
| `hits` on a movement, `climb` on an attack | Loader error for an ability-id target; ignored for that ability under a slot target |
| Draft when `offers.loser` is 0 | Empty offer list; the player's pick is a no-op command and `HasPicked` is true immediately |
| Both players zero offers | The next round starts in the same `Apply` that ended the previous one |
| A resign or forfeit in a round | The round is lost like an elimination; `RoundEndedEvent.Reason` carries it |
| `Session` constructed with a build whose lineage's starting Blessing is already in `BoonIds` | Not appended twice |
| Where `RoundEndedEvent` sits relative to `MatchEndedEvent` | Immediately after it |
| Wire encoding of a null lineage | JSON `null`, like a hidden modifier id |
| An Editor is `ready` but `Assets/Refresh` times out | Retry once after 10 s, then treat the Editor as unreachable (§0.7) and say so |

---

## 14. Definition of done (copy into the final report with ticks)

- [ ] `dotnet build Mimas.slnx` clean; `dotnet test shared/Mimas.Core.Tests` green with at least 60 new
      tests; `dotnet test server/Mimas.Server.Tests` green with **no server file changed**.
- [ ] `boons/` and `lineages/` load from the shipped folder; every link rule in §5 has a failing-case test.
- [ ] The three starting Blessings, 18 pool boons, 6 abilities and 9 modifiers of §5.8 exist and pass
      `ShippedLineages_CoverEveryItem`.
- [ ] `MatchState.ResolveAbility` is the only ability lookup (test).
- [ ] A Strength Blessing, a range Enchant, a cost Enchant, an added element, a Sigil and an hp Blessing
      each reveal by exactly the rule in §6.5 (six tests), and nothing reaches the opponent early (sweep).
- [ ] Fire immunity zeroes a fire bolt with one `Nullify` line and is unknown in the preview until revealed.
- [ ] `Draft.Offer` gives one of each kind when possible, respects gear, ownership, `stackable` and
      `exclusive`, and is seed-deterministic.
- [ ] `Session` plays a best-of-3 with a draft between rounds, wraps the ladder, seats the loser first,
      keeps reveals across rounds, and replays identically from a seed (golden test).
- [ ] `hits` and the trajectory swap parse and fail closed with a message naming this spec.
- [ ] Docs of §10 done; design page edited through the declared allowance; ADR-034 and ADR-035 written.
- [ ] Run report complete; STATE rewritten; task `T-0009` at `verify`.
