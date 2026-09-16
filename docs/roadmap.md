# Roadmap

| M | Goal | Done when | Status |
|---|---|---|---|
| M0 | Setup | Empty Web build loads in browser; `dotnet test` green; Unity CLI + Claude Code connected; server `/ws` echoes ping | in progress |
| M1 | Core loop, offline | Hex map from JSON, 1 unit each, move + basic attack, turn order, win by kill; playable vs a random bot in the Editor; Core has ≥ 50 tests | **done 15 Sep 2026** |
| M2 | Online | Same match over WebSocket via `Mimas.Server`; guest auth; matchmaking queue; chess clocks; reconnect | |
| M3 | Depth | Gear replaces classes (`items/`), two damage lanes + elements, three lineages with starting blessings, boons (blessing / enchant / sigil) with the between-round draft, session best-of-3 across 3 ladder maps, session-long reveals, character select and draft screens. Spec: `docs/design/index.html` | |
| M4 | Presentation | Cinemachine tilted/top-down toggle, UI Toolkit HUD + examine mode, Shuriken VFX, FMOD music/SFX, low-poly characters with animations | |
| M5 | Ship | Ratings (Glicko-2), deploy on VPS, size/load optimisation (Addressables, stripping), mobile browser check | |

## M1 progress (15 Sep 2026)

Done: movement rules (walk / jump / teleport resolvers, height, terrain cost tables, integer hex lines),
content catalogue (all JSON loaded once, linked, hashed), client board with heights and move playback,
server loads the same content, and, in the second 15 Sep session (ADR-016..019):

- Rules: `rules.json`, class `stats` (hp, ap, power/defense per damage type), ability `cost`, `attack`
  abilities (range band, line of sight always), `modifiers/*.json` (trigger + conditions + flat damage,
  public or hidden), `DamageCalculator` with one code path for the knowledge-limited preview and the full
  resolution, `MatchState` (`Validate` / `Apply` / `EnumerateLegal`), AP turns, reveal events, win by
  elimination, `PlayerView` + `EventFilter`, `RandomBot`. 169 Core tests, including random-bot games with
  invariants and a same-seed determinism check.
- Client: `LocalMatchSession` (hosts `MatchState` + bot, plays filtered events, board interaction for
  moves and attacks, fake clock with timeout-as-command) replaced `SkeletonMatchController`; HUD package D
  (ADR-019): unit hp tags that fade until relevant, AP dots with hover reservation, cost numerals, attack
  preview tooltip with a "?" row, damage flyover naming the revealed passive, passive markers, examine
  panel with hp/ap/passives, result banner. `DefaultMatchSettings.asset` gives the bot two hidden passives
  so the reveal path is visible in normal play.

Not done / deferred: tile-effect `OnEnter` hooks (no tile effects exist yet; the `effect` field now names a
combat modifier), an attack animation (flyover + hp change only), boons (M3), the network session (M2).

Playtest 16 Sep 2026 (Rohan, warrior vs bot mage, before the loadout slice): clean console, won by
elimination. Not yet exercised and worth a pass first thing: the bot attacking (its flyover, an "EXTRA"
reveal, its ability appearing in examine) since the random bot rarely picks attacks; Fire Bolt and line of
sight from the player's side (give `PlayerLoadout` the crown's spells); the DEFEAT banner; the
height-advantage preview line from the ramp; a Web build of the new HUD. Tuning thoughts: the 7 s idle penalty bites on a first turn; a refused far
click while Move is armed is silent on screen.

## M3 progress (16 Sep 2026): the loadout slice

Done, from `docs/specs/2026-09-16-loadout-slice.md` (design: `index.html#character`, `#equipment`,
`#stats`): `classes/` is gone. A hero is `rules.baseStats` plus four items (`items/*.json`: weapon, crown,
boots, armour), and its abilities are `rules.innateAbilities` (the walk) plus each item's. Damage lanes are
`weapon` and `spell`; `melee` / `ranged` / `magic` have left the shipped data. Core gained `ItemDef`,
`ItemSlots`, `Loadout`, `RulesDef.BaseStats` / `InnateAbilityIds`, `Unit.ItemIds` /
`AbilitySourceOf`, `ContentCatalog.Items` / `GetItemForSlot`, and `PlayerView` now publishes each unit's
item ids and each ability's source item (identity public, ability hidden). The launch catalogue is longbow
and flintlock, one crown, leaping and blink boots, one jerkin; `/health` reports `items`; `MatchSettings`
holds two loadouts; the examine panel lists gear with stat summaries and groups `?` ability rows under the
item that grants them (`artifacts/loadout-examine-opponent.png`). 197 Core tests.

Any kit is hp 28, ap 3, Strength 3, Magic 4, armour 2 / 2; a full turn deals about 12, so two heroes that
stand still trade for about three turns each. The numbers are hand-set placeholders.

Not done / deferred: elements and the `element` field, `nullify`, boons, lineages, the draft, the session
wrapper, character select, gear on the 3D model, the blade weapon, presets.

Next: **M2** (WebSocket rooms, guest auth, matchmaking, server-side clocks that submit
`EndTurnCommand(Timeout)`, reconnect via `PlayerView` resync), now that the wire format is built around
gear rather than classes. Before or alongside it, a short balance pass on the shipped numbers (arrow shot
1 AP 3 dmg, aimed shot 2 AP 6, quick shot 1 AP 2, heavy shot 2 AP 5, fire bolt 2 AP 6, arcane spark 1 AP 2)
by letting two random bots play a few hundred seeded games.

## Baselines

| Metric | Value | Date |
|---|---|---|
| Empty URP Web build (Brotli) | 12.5 MB (wasm 7.9 + data 4.5 + framework.js 0.08 + loader/template 0.04) | 14 Sep 2026 |
| Load time (desktop Chrome, cold) | — s (measure after D3 deploy) | |
| Core test count / runtime | 24 / 0.07 s | 14 Sep 2026 |
| Core test count / runtime | 110 / 0.05 s (movement: walk / jump / teleport resolvers, integer hex lines) | 15 Sep 2026 |
| Core test count / runtime | 131 / 0.05 s (+ content catalogue, classes, hash parity) | 15 Sep 2026 |
| Core test count / runtime | 137 / 0.06 s (+ ability icon and category fields) | 15 Sep 2026 |
| Core test count / runtime | 169 / 0.26 s (+ stats, attacks, modifiers, match state, bot games) | 15 Sep 2026 |
| Core test count / runtime | 197 / 0.30 s (+ items, loadouts, base stats, ability sources, shipped-data conventions) | 16 Sep 2026 |
