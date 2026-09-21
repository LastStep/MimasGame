# Roadmap

| M | Goal | Done when | Status |
|---|---|---|---|
| M0 | Setup | Empty Web build loads in browser; `dotnet test` green; Unity CLI + Claude Code connected; server `/ws` echoes ping | in progress |
| M1 | Core loop, offline | Hex map from JSON, 1 unit each, move + basic attack, turn order, win by kill; playable vs a random bot in the Editor; Core has ≥ 50 tests | **done 15 Sep 2026** |
| M2 | Online | Same match over WebSocket via `Mimas.Server`; guest auth; rooms by code; server-authoritative turn clock; resign; reconnect | **local end to end done 17 Sep 2026**; **live at `https://mimas.laststep.cloud` since 21 Sep** (Rohan's first `deploy.sh` run; three green live smokes, headers as specified — M2-7 pending verifier) |
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

## M3 progress (16 Sep 2026): the aiming slice, rules half

Attacks now aim at a point on the body and are resolved against heights in *body units*. `rules.json`
carries `heights` (1 level = 3 units, hero body 6, aim 4), so a level-1 step no longer blocks a flat shot
between two heroes but a level-2 plateau does — and so does any body. Sight is a straight ray from aim
point to aim point with a cross-multiplied integer test (a graze blocks, endpoints and holes never block,
unwalkable terrain is solid), and every attack declares two independent required fields: `lineOfSight` and
`trajectory` (`direct`, `arc` with an `apex`, or `sky`, one resolver each behind a fail-closed registry).
The bow lobs without needing sight (apex 3 and 4); the gun and both crown spells are straight and need it;
`sky` exists in code only. Range bands are circular (`minRange² ≤ q²+qr+r² ≤ range²`), so the client can
draw a true circle — from radius 7 up the diagonals reach further than hex distance would.

**Props** are the first non-unit bodies: `props/pillar.json` (body 6, 10 hp, no armour — about one full
turn of damage) and `props/wall.json` (body 6, indestructible). A map hex may carry one; they occupy their
tile, block sight and trajectories with their body, and a destructible one is a legal target that is
removed when it falls without ending the round. `arena-4.json` was reworked around them: all 12 stone hexes
are gone, replaced by a level-2 plateau with level-1 steps, four walls and four pillars — cover is bodies
now, not terrain. `board-3.json` is untouched and its stone still blocks. Core gained `IBody`, `BodySet`,
`Prop`, `PropDef`, `HeightsDef`, `Ballistics`, the trajectory resolvers and `TargetCheck`; `PlayerView`
lists props and every `UnitView` carries its body and aim heights; `/health` reports `props`. 287 Core tests.

Not done / deferred: enchant or boon overrides of a trajectory (only the seam exists); area and
multi-target shapes; owned props, rubble, units-as-cover penalties; a beam trajectory.

## M3 progress (16 Sep 2026): the aiming slice, client half

Arming an attack now turns the board into an answer to "what happens if I shoot there?". The range band is
**two true circles** computed per fragment in a hand-written tile shader (`Mimas/HexTile`), so the band is
the Euclidean rule drawn rather than approximated and it bends over steps and plateaus for free — the
Decal Projector does not render on WebGL2 (Unity IN-90245) and a ring mesh would seam at every height
change. One `CheckTarget` per resolved hover drives everything else together: a dashed green path with a
ring on the point it will hit, red stopping at the blocker with an X and the reason as the tooltip's first
line, grey with an "Out of range" tag beside the cursor, or grey with a dimmed ring for a wall. A refused
shot still lists the damage it would do — the same `DamageCalculator` the rules resolve with, no ghost on
the bar. On resolution a placeholder projectile flies the *same* curve the preview drew and the hit lands
on impact, not when the event arrives. The hero faces whatever the cursor snapped to, and the nearest
living enemy when nothing is armed; none of that leaves the client.

**Props** are on the board: code-built boxes with a hit-mark ring at their aim height, hp tags, examine
("Blocks sight and movement"), and they shrink away when destroyed. Hover resolves bodies before the tile
they stand on, by component rather than by layer, so `ProjectSettings` stayed untouched. New: `BoardHover`,
`PropView`, `UnitFacing`, `RangeCircles`, `AimPreview`, `FlightCurve`, `ProjectilePlayback`, the
`Mimas/HexTile` and `Mimas/AimLine` shaders, and the HUD's cursor tag, blocked reasons and prop tags.
`BoardView.WorldPerHeightUnit` is the single conversion from Core's body units to world units.

Verified in play on arena-4 (`artifacts/aim-*.png`): circles, a clear arc over a wall onto the bot, a
blocked direct shot at the bot and at a pillar behind a wall, out of range, a prop target, and a projectile
in flight. A pillar was shot down and its tile walked onto; the bot shot two more down by itself. Console
clean; Core and the server untouched.

Two things the play test caught: props in the HUD list broke the turn-start loop (their ids are body ids,
not unit ids), and arming an attack while the cursor already sat on a target drew nothing until the mouse
moved.

Not done / deferred: real art (models, VFX, sounds) — props, the projectile and the heroes are all
placeholders; a prop prefab library; tile tinting for movement is unchanged; the `sky` trajectory has a
placeholder drop and no ability uses it; touch input; a Web build of this slice.

## Review 17 Sep 2026: both aiming specs verified

Two independent read-only reviews (one per spec) walked every decision, data file, test name, doc update and
commit of `docs/specs/2026-09-16-aiming-rules.md` and `docs/specs/2026-09-16-aiming-client.md` against the
code. Verdict: rules half all good (287 tests green, no golden-rule violations, the two-edge sampling in
`ColumnTrajectory` is exact rather than approximate); client half good with notes (Editor compiles, console
clean, Core and the server untouched, both play-test bugs fixed in code). One nit left open: `ShowAim` in
`LocalMatchSession` calls the aim preview without the null guard the rest of the file uses. Housekeeping from
the review: the solution file is `Mimas.slnx` (CLAUDE.md said `.sln`), and the `com.unity.pipeline` editor
config under `Assets/Settings/Pipeline/` is machine-local tool config and is now git-ignored.

Next: **M2**, spec `docs/specs/2026-09-17-online-slice.md` (17 Sep 2026): guest auth, one queue, server bot,
one flat 30 s server turn with a lag grace, resign, 60 s reconnect grace, the client mirror (ADR-026), wire and
clock (ADR-027), lobby scene and the presenter / driver split (ADR-028). The aiming slice is closed; the specs are
history and the design page is the record.

## M2 progress (17 Sep 2026): the online slice

**The match is on the server.** Two browsers — or the Editor and a browser — can play the same game, and
a bot match runs through exactly the same wire so there is one client code path rather than two.

Before any code, one open question was settled. The spec locked a FIFO **queue**; the studio plan,
approved the same day, recorded **room codes** and the queue moving to M5. Both were dated 17 Sep and
`OPT-0001` put it to Rohan: **room codes**, with the loadout chosen *after* joining a room. Carried into
the spec as amendment A1. The reason is the playtest, not the engineering: four friends in two arranged
pairs pressing "Find match" get paired in the order they click, and two of them end up playing the wrong
person in the first thirty seconds of the only session that matters.

- **Core.** `rules.json` gains a `clock` block (turn 30 s, lag grace 1 s, reconnect grace 60 s) — a rule,
  not balance, because both sides read it. `ResignCommand` (reason `Player` or `Disconnect`) with
  `MatchEndReason.Resign / Forfeit`: legal off turn, never offered to a bot, and the path a server-side
  forfeit takes, so replaying a command list still reproduces the match.
  `Mimas.Core.Protocol.Wire` is a hand-written JSON codec — no attributes, no reflection, no
  `TypeNameHandling` (ADR-027). `MatchState.FromView` is the **client mirror** (ADR-026).
- **Server.** Rooms reached by a four-letter code, guest identities with resumable tokens, a bot seat, the
  turn deadline with a measured lag allowance, the reconnect grace and forfeit, and hidden information
  filtered per seat in the one method anything leaves a room through.
- **Client.** `NetClient` (one socket, alive across scenes), the Lobby scene, and `MatchSession` split
  into a presenter and an `IMatchDriver` (ADR-028) — local practice and an online match are the same
  presenter over a different driver, and opening the Arena scene directly still plays a whole local game.

**Numbers:** 287 → 321 Core tests, and a new `Mimas.Server.Tests` (34 tests, ~6 s) which is now ladder
rung 5 and `required`. Those tests play whole matches over real sockets using `MatchState.FromView` as the
client — the client's own architecture as the test double — so a match they can play is a match a browser
can play.

**What the Editor caught that no test could:** four ordering and recovery bugs — the lobby looking for the
connection before it existed, a button press that opened a socket and then waited forever, a reconnection
that never re-authenticated, and a match that was gone from the server leaving the board frozen with no way
out. All four are the kind that need a scene and a real socket, which is why section 8 of the spec exists.

**Not done:** deploy (M2-7, `F-deploy`), rematch without leaving the room (M2-8), and a Web build served to
a real browser — everything above was verified in the Editor against a real server on this machine.

## Tooling backlog

Small editor/authoring tools, in the order they are likely to be worth building.

| Tool | What | Why it is not built yet |
|---|---|---|
| JSON data Inspector (`ScriptedImporter`) | A `ScriptedImporter` for `Assets/_Game/Data/**/*.json` with a custom Inspector, so clicking `longbow.json` in the Project window shows typed fields and dropdowns and writes back to the JSON. The real answer to "gear is easier to edit as a ScriptableObject" without giving up the format the server and the content hash need (ADR-004, ADR-014). | The schemas in `tools/schemas/` cover typo-prevention for now; Inspector *editing* is the remaining gap. Maybe a day or two. |
| Content browser window | A `Mimas/Content` window over `ContentCatalog`: every item and ability with resolved numbers, plus any link errors, without entering Play Mode. | `ContentBootstrap` already logs the summary; the window is convenience. |
| Projectile and impact VFX | Shuriken emitters for a shot in flight and its impact, driven from the same `FlightCurve` evaluator the preview uses, replacing the flat sphere. | The placeholder reads well enough to play; art direction for the whole board comes first (M4). |
| Balance sim harness | Run N seeded random-bot games over a matrix of loadouts and print win rates and average turn counts. | Wanted before the first real balance pass on the shipped numbers. |

## Baselines

| Metric | Value | Date |
|---|---|---|
| Empty URP Web build (Brotli) | 12.5 MB (wasm 7.9 + data 4.5 + framework.js 0.08 + loader/template 0.04) | 14 Sep 2026 |
| Load time (desktop Chrome, cold) | **7.8 s to socket connected** on `https://mimas.laststep.cloud/?room=ZZZZ`, median of three headless runs (boot 3.7 / 5.3 / 9.5 s; connected 6.2 / 7.8 / 12.0 s), 12.3 MB transferred each time. Localhost for scale: boot 0.6 s, connected 3.1 s | 21 Sep 2026 |
| Core test count / runtime | 24 / 0.07 s | 14 Sep 2026 |
| Core test count / runtime | 110 / 0.05 s (movement: walk / jump / teleport resolvers, integer hex lines) | 15 Sep 2026 |
| Core test count / runtime | 131 / 0.05 s (+ content catalogue, classes, hash parity) | 15 Sep 2026 |
| Core test count / runtime | 137 / 0.06 s (+ ability icon and category fields) | 15 Sep 2026 |
| Core test count / runtime | 169 / 0.26 s (+ stats, attacks, modifiers, match state, bot games) | 15 Sep 2026 |
| Core test count / runtime | 197 / 0.30 s (+ items, loadouts, base stats, ability sources, shipped-data conventions) | 16 Sep 2026 |
| Core test count / runtime | 203 / 0.27 s (+ item slot vs ability category, schema tolerance, attack lane conventions) | 16 Sep 2026 |
| Core test count / runtime | 287 / 0.24 s (+ ballistics, ray sight, trajectories, props and bodies, circular ranges) | 16 Sep 2026 |
| Core test count / runtime | 321 / 6 s (+ clock, resign, the wire codec, the client mirror) | 17 Sep 2026 |
| Server test count / runtime | 34 / 6 s (auth, rooms by code, whole matches over sockets, clocks, hidden info, reconnect, forfeit) | 17 Sep 2026 |
