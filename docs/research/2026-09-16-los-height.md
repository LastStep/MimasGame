# Line of sight / line of fire with height — research

Goal: inform a rule where weapon attacks aim at a fixed body-centre point, and terrain
blocks a shot only if it rises above the eye→aim-point line (Valorant-style), on an
integer hex grid with integer tile heights.

## 1. Hex wargames with integer levels

### BattleTech (tabletop, *Total Warfare*)
- LOS is the straight line between the **centre dots of the attacker's and target's hexes**. Any hex that line passes through (or clips) is checked as a potential blocker.
- **Intervening terrain rule**: a hex blocks LOS if its (terrain) level is *equal to or higher than* the level of **both** attacker and target; OR it is **adjacent to the attacker** and equal/higher than the attacker's level; OR it is **adjacent to the target** and equal/higher than the target's level. (Adjacency gets a stricter check because near obstacles subtend more angle — a proxy for the "rises above the eye line" test without doing real trig.)
- Woods add height for LOS purposes (light woods +1 level, heavy woods +2) independent of the ground level; it takes **3 hexes of light woods** or **2 hexes of heavy woods in the LOS path** to fully block sight (cumulative partial blocking, not a single hex).
- **Units never block LOS** — only terrain/buildings/woods. You can shoot through/over a hex full of other mechs.
- **Indirect fire**: a unit without direct LOS to the target can still fire (LRMs) if a *friendly spotter* has valid LOS to the target; to-hit is worse than direct fire, and a unit that *does* have direct LOS cannot choose to fire indirect instead.
- Ambiguous/edge cases (partial cover, hex-edge grazes) are resolved narratively in *Total Warfare* p.100 by the "equal to or higher than" wording above rather than a continuous geometric test — ties favour the defender (terrain blocks on equality, not just strictly-above).
- Sources: https://boardgamegeek.com/thread/842154/line-of-sight-and-elevation , https://boardgamegeek.com/thread/2425991/assessing-damage-with-intervening-terrain-partial , https://www.battletech.com/forums/index.php/topic,61243.0.html , https://www.battletech.com/forums/index.php?topic=17356.0

### Advanced Squad Leader (ASL)
- Same base idea: LOS is the line between the **centres of the two hexes**; every hex the line touches is evaluated as a potential obstacle (a graze along a hex edge is checked against both hexes it borders).
- Obstacles (hills, buildings, woods) don't just block/not-block — a tall obstacle casts a **"blind hex" zone** behind it (dead ground the shooter cannot see into), and the *number* of blind hexes is derived from a formula combining the obstacle's height level, the range to it, and the relative elevation of the shooter (a height advantage reduces the blind-hex count by roughly one hex per extra level of shooter elevation). One community-quoted worked instance: `2×2 + 14/5 − 4 − 1 = 1` blind hex for a given level/range combo — i.e. it's a small closed-form arithmetic formula on level-difference and range, not a full trace.
- Hills specifically only start creating a first blind hex once the target is **more than 5 hexes** past them — at short range a hill's blind zone hasn't "opened up" yet (mirrors how a shallow eye-to-target line clears a nearby short obstacle but not a far one).
- This is the closest tabletop analogue to "does terrain rise above the eye→aim line": ASL's blind-hex math is effectively a discretized version of the similar-triangles test, pre-baked into a lookup/formula so players don't do trig at the table.
- Sources: https://boardgamegeek.com/thread/168216/line-of-sight , https://boardgamegeek.com/thread/632640/blind-hex-calculation-help-please , https://boardgamegeek.com/thread/2555068/hills-and-los , https://asl-players.net/asl-boot-camp-5-line-of-sight-and-tem/

### PanzerBlitz / Panzer Leader
- LOS must be a clear line between attacker and defender hexes; hills, woods and towns block at specific relative elevations, and whether a shot is legal depends on the *respective elevation* of shooter vs target (higher ground sees/shoots over lower obstacles). No published closed-form trig formula surfaced — resolved via terrain tables rather than geometry.
- Source: https://grognard.com/scans/PANZERBLITZ%20CLARIFICATIONS%20AND%20ANSWERS%20(2023).pdf

## 2. Digital tactics games

### XCOM 2 / Long War
- Cover comes in **half (low)** and **full (high)**, granting flat defense/damage-reduction bonuses (Long War: low ≈ +30 defense/0.66 DR, high ≈ +45 defense/1.0 DR); two low-cover layers ≈ one high-cover layer for LOS purposes.
- **Elevation interacts with cover**: standing higher can *negate* an enemy's high cover (you shoot over it), and standing lower can make even low cover fully block your sight — i.e. cover height is evaluated relative to the shooter's eye height, not as an absolute flag on the tile.
- **Flanking**: moving to a position where the cover no longer sits between shooter and target (typically 90°+ around it) removes all cover bonus and adds a large crit-chance bonus — cover is directional, not omnidirectional.
- Units popping out of cover ("step out" shots) temporarily gain a sightline without counting as a move.
- **Units do not block LOS** for shooting purposes in the base LOS model (only terrain/cover objects do); the search did not surface an explicit statement either way for XCOM 2 specifically, but this matches the XCOM design lineage.
- Sources: https://www.ufopaedia.org/index.php/Cover_(Long_War) , https://www.youtube.com/watch?v=y47L183Dbb8

### Phoenix Point
- Full free-aim ballistic model: LOS is "an imaginary uninterrupted line connecting any part of one character to another" (not a single fixed point), and cover is *literal geometry* that blocks bullets rather than an abstract bonus.
- Because aiming is manual/zoomed, the player explicitly nudges the reticule to clear partial obstructions and to pick body parts (head/arm/leg/torso) for called-shot effects (disable weapon arm, cripple leg, etc.).
- Source: https://steamcommunity.com/sharedfiles/filedetails/?id=2331577913 , http://wiki.phoenixpoint.com/Combat

### Divinity: Original Sin 2 (and by extension BG3-style CRPGs)
- Height advantage gives extra range/visibility and bonus damage to the higher unit; LOS is checked as an actual 3D raycast from a point on the character up on the platform, not an abstract per-tile flag.
- Documented failure mode (a "gotcha" worth stealing as an anti-pattern): archers standing well above a melee enemy on a ledge sometimes **cannot** hit them because the ledge edge geometrically "obstructs" the ray even though a person could obviously shoot down onto someone right next to them — the fix players use is manual arc-aim (aim at the ground) to loft the shot over the edge. This is a cautionary tale about naive raycast-from-a-single-eye-point systems failing at extreme angles/adjacency.
- Sources: https://steamcommunity.com/app/435150/discussions/2/1488861734097098818/ , https://steamcommunity.com/app/435150/discussions/0/1519260397799617345/

### Jagged Alliance 3
- Cover has a **height property** (crouch-height vs full-height cover); a soldier only benefits from low cover while crouched/prone, but can stand fully behind high cover.
- Targeting UI shows **per-body-part occlusion** — the reticule tells you which specific body parts are behind cover geometry and can/can't be hit, rather than a single pass/fail LOS check to the whole body.
- Sources: https://steamcommunity.com/app/1084160/discussions/0/3807280708314694367/ , https://gamefaqs.gamespot.com/pc/921133-jagged-alliance-3/faqs/82009/tactical-view

### Into the Breach
- Deliberately **not** a general LOS game: most weapons (beams, projectiles) fire along a straight line and stop at the first blocking tile (Grid building, mountain); some units' melee/projectile attacks are explicitly said to be "blocked by obstacles" and knock back.
- **Artillery is the escape valve**: it arcs over any obstacle and can target any single tile in line (except adjacent tiles) precisely because it ignores the LOS blocking that direct-fire weapons respect. This is a clean two-tier model: direct-fire = blocked by height/obstacles, arcing/indirect = ignores them.
- Source: https://intothebreach.fandom.com/wiki/Attacks

### Battle Brothers (hex grid, integer height levels — closest analogue to Mimas)
- Tiles have discrete **elevation levels**; standing higher increases view range and lets you see/shoot **over** obstacles that would block LOS from lower ground. Ranged weapons also gain **extra range per level of height advantage** when shooting downhill.
- Ranged "line of fire" can be **fully blocked** (can't select that target at all) or **partially blocked**, in which case there's a flat **75% miss-chance penalty to reach the blocked target** (75% → 50% with the Bullseye perk); on a miss caused by this penalty there's an additional −15% modifier and a **re-roll against whichever unit was actually in the way**.
- **Units do block ranged line of fire** — but with an exception: **your own allies within 2 tiles do not block your shots** (short-range "shooting over/around" your own front line is allowed), while an ally standing further out still blocks and eats the "in the way" resolution described above.
- Sources: https://battlebrothersgame.com/tactical-combat-mechanics/ , https://battlebrothers.fandom.com/wiki/Hit_Chance , https://battlebrothers.fandom.com/wiki/Combat_Mechanics

### Gloomhaven (digital, for contrast — no height, but a clean discrete corner rule worth noting)
- LOS = **any corner of the attacker's hex to any corner of the defender's hex**, unobstructed by a wall (an "exists a clear line among the 6×6 corner pairs" test, not a single centre-to-centre ray). Obstacles and characters never block LOS — only walls do.
- Useful as a contrast case: a discrete "any-corner-pair" existence test is a materially different (more permissive) rule than BattleTech/ASL's single centre-to-centre ray, and is worth naming explicitly if the designer wants a more forgiving LOS.
- Source: https://havenhints.com/rules/line-of-sight/ , https://rules.dized.com/game/I7lEsCGOS2-zgol-ZRNf3g/mEjqu7I1RSSB7fHkFqGs0Q/what-are-the-line-of-sight-rules

## 3. Cover-height taxonomies: exact ray vs threshold table

Two families, seen repeatedly above:
- **Threshold/table family** (BattleTech, ASL, XCOM abstract cover, Gloomhaven): cover height is bucketed into a small enum (none / low / high, or blocks / doesn't), and whether a shot is blocked is decided by comparing enum levels or a hex-adjacency rule — cheap, deterministic, easy to explain to players, but coarse at boundaries (XCOM's "elevation can flip a high-cover tile to not-blocking" is already the game bolting a continuous correction onto the table because pure buckets felt wrong).
- **Exact ray/geometry family** (Phoenix Point, DOS2, Jagged Alliance 3 per-body-part): cover is real 3D geometry and LOS/LOF is a literal raycast from an eye point to an aim point (or per body part) — more "correct" and satisfying (this is exactly the Valorant-style ask), but more prone to edge-case jank at grazing angles/adjacency (see the DOS2 ledge bug) and harder to reason about for players without a strong visual indicator.

## 4. Integer "does the segment clear the column" test (no floats)

This exact problem is solved routinely in GIS **viewshed analysis** (line-of-sight against a digital elevation model): compare the elevation of the straight sightline at each sampled distance against the terrain's elevation at that distance; the line is blocked at the first point where terrain elevation exceeds the interpolated line elevation. The standard interpolation is a **linear (similar-triangles) interpolation between the two endpoint heights**, i.e. exactly the primitive the design wants:

Given shooter at distance 0 with eye height `h0`, target at distance `D` with aim height `h1`, and an intervening tile at distance `d` (0 < d < D) with top height `H`:

```
line_height_at(d) = h0 + (h1 - h0) * d / D   -- the float version

-- integer-only, no division, cross-multiplied form:
blocked  iff  H * D  >  h0 * (D - d) + h1 * d
```

(multiply both sides by `D` to clear the division; all terms are integers if `h0, h1, H, d, D` are integers — exactly the "similar triangles cross-multiplication" the designer described). This is the same core operation as Bresenham-style integer line algorithms (used for 2D grid LOS/FOV — e.g. the classic *Moria*/roguelike LOS did fast integer center-to-center line tracing) generalized to one extra "height" axis. GIS literature calls the naive per-tile version of this the standard viewshed line-of-sight test, with the caveat that production viewshed algorithms optimize the *sequence* of these tests (propagating a running max/min "reference line" instead of recomputing from scratch per tile) rather than changing the underlying math.

- **Endpoint handling**: the shooter's own tile and the target's own tile are excluded from the blocking test (d strictly between 0 and D) — every game above does this implicitly (nobody blocks LOS out of their own hex, and nobody's own tile occludes the shot into them). This also sidesteps the "shooter standing on a tall tile" self-block case.
- **Ties (`H*D == h0*(D-d)+h1*d`, i.e. terrain exactly grazes the line)**: BattleTech resolves this in the defender's favor (blocks on "equal to or higher than"); a Valorant-style FPS treats an exact graze as blocking too (conservative/deterministic default). Recommend blocking on `>=` rather than `>` for a deterministic multiplayer rules engine — avoids float-adjacent "just barely visible" arguments and replay desyncs are impossible either way since it's pure integers.
- Sources: https://en.wikipedia.org/wiki/Viewshed (general concept, confirmed via multiple viewshed-algorithm papers e.g. https://www.sciencedirect.com/science/article/abs/pii/S0098300422001625 , https://link.springer.com/article/10.1007/s12145-020-00545-7 ), https://www.redblobgames.com/articles/visibility/ (2D grid visibility background), https://groups.google.com/g/rec.games.programmer/c/bWVneTfLQ3E (Moria's fast integer center-to-center LOS)

## 5. Do units block LOS?

| Game | Units block LOS/LOF? |
|---|---|
| BattleTech | No — only terrain/woods/buildings. |
| ASL | Only terrain generates blind hexes; units are not obstacles for LOS purposes (they can still be targeted/hit by area effects etc., a separate system). |
| XCOM 2 / Long War | No explicit unit-blocking found; cover objects block, units don't (consistent with the wider XCOM lineage). |
| Gloomhaven | Explicitly no — "obstacles and characters do not block line of sight; only walls do." |
| Battle Brothers | **Yes** — any unit (including allies beyond 2 tiles) in the path applies a 75%/50% miss penalty and can absorb the shot instead of the intended target; allies within 2 tiles are exempted. |
| Into the Breach | Ambiguous/weapon-dependent — beams stop at Grid buildings/mountains, some projectiles are blocked by "obstacles," artillery ignores it entirely. |

## Options for a hex game with integer heights

**A — Ray from eye height to aim height, integer cross-multiplied test (viewshed-style).**
Shooter's muzzle/eye at some fixed height on their tile, target's fixed aim point (e.g. 4 of 6 units), test every intervening tile with `H*D > h0*(D-d)+h1*d` (or `>=` to block on graze). This is exactly what the designer described.
- Pros: matches the stated design 1:1; deterministic and cheap (pure integer multiply/compare per tile, no trig); scales naturally to "does a 2-height obstacle block, does a 4-height obstacle block" tuning by moving `h0`/`h1`, not by touching the algorithm; generalizes cleanly to any shooter/target height combo (uphill/downhill) with no special-casing.
- Cons: needs a decision on which tiles to sample (every hex the line crosses, like BattleTech/ASL centre-to-centre, or a corner-pair test like Gloomhaven); grazing/tie cases need an explicit `>=`-vs-`>` ruling or you get "technically blocks" edge cases that feel arbitrary to players without a good visual/preview.

**B — ASL-style blind-hex table (precomputed level-difference × range formula).**
Instead of a per-shot geometric test, precompute (or formula-derive) a small table of "how many tiles behind a height-H obstacle are dead ground" as a function of obstacle height and shooter height/range, and consult it.
- Pros: very cheap at runtime (lookup, not even a multiply); easy to hand-tune per weapon range band; historically proven at the tabletop for exactly this kind of integer-height problem.
- Cons: indirect — designers/players reason about "blind hexes behind an obstacle" rather than "does this specific shot connect," which is a worse fit for a system built around one continuous aim-point-to-aim-point ray; harder to extend to asymmetric heights (target standing on a raised tile) without expanding the table; effectively reinvents option A's math but hides it in a table, so no accuracy win, only a (probably unneeded, since option A is already O(path length) integer ops) performance win.

**C — BattleTech-style threshold/bucket rule ("blocks if obstacle level >= both endpoints, or adjacent and >= that endpoint's level").**
Bucket heights into levels and use the adjacency-aware inequality instead of true interpolation.
- Pros: extremely simple mental model for players ("is it taller than me/them"); no need to define an exact aim-point height at all; forgiving of weird geometry since it never needs a real interpolated line height.
- Cons: actively contradicts the stated goal — it cannot express "a 2-unit obstacle doesn't block a 4-unit-high shot but does block if the target is closer to it," because it ignores *distance* entirely (only adjacency is distance-sensitive, and only as a binary near/far flag); would need heavy retrofitting to reproduce the Valorant-style behaviour the designer explicitly asked for. Include mainly as the "why not" baseline.

**D — Per-body-part / free-aim raycast (Phoenix Point / Jagged Alliance 3 style).**
Extend option A from one fixed aim point to multiple sampled points (e.g. test against head/torso/legs heights) and report partial cover / called-shot eligibility.
- Pros: richest tactical depth (partial exposure, called shots); best visual/UX legibility since the game can literally draw the ray.
- Cons: multiplies the option-A test by however many sample points, and now needs per-body-part damage/targeting design the docs don't currently call for; DOS2's ledge-adjacency bug shows this family is more exposed to "obviously should hit but geometrically doesn't" complaints at extreme angles — needs a deliberate near-field exception (see gotchas) if adopted. Overkill unless body-part targeting is already a planned feature.

**Recommendation shape**: Option A is the direct implementation of what's already been decided (fixed aim point + eye point + integer cross-multiply test); B and C are useful only as fallback/optimization or as the "simple bucket" alternative to hold up against A when the designer wants to compare complexity; D is a stretch goal if called shots/body-part damage ever enter scope.

## Gotchas

- **Edge grazing (`H*D == numerator`)**: decide once, globally, whether an exact graze blocks or not, and make it `>=` (blocks) for a deterministic multiplayer engine — matches BattleTech's "equal to or higher than" precedent and avoids float-adjacent near-miss arguments since everything stays integer.
- **Endpoint tiles**: exclude `d = 0` (shooter's own tile) and `d = D` (target's own tile) from the blocking test — otherwise a shooter standing on a tall tile, or a tall target tile itself, would incorrectly self-block the shot. Every game surveyed does this implicitly.
- **Unit standing on a raised tile**: the eye/muzzle height must be `tile_height + fixed_offset`, not a flat world-space number — otherwise a unit on a level-2 tile with a "6-unit-tall eye" ends up physically inside the terrain of any level-3+ neighbour, or LOS math silently uses the wrong base height. Needs the same treatment for the aim point on the target's tile.
- **Target on higher ground than the shooter**: the interpolation must work symmetrically for `h1 > h0` (shooting uphill) and `h1 < h0` (downhill) — a naive implementation that assumes "the line only goes down" will mishandle uphill shots where an obstacle *between* shooter and target can still clear the line even though it's taller than the shooter, because the line is climbing toward the target. Test both directions explicitly; Battle Brothers' "extra range per level shooting downhill" and DOS2's ledge bug are both symptoms of getting this asymmetry wrong.
- **Very long ranges**: integer overflow is a real risk once `H`, `D` get multiplied (`H*D` and `h0*(D-d)` etc.) — use 64-bit intermediate math even if heights/ranges individually fit in 16/32 bits, especially if map radius or height scale ever grows. Also, at long range with fine height granularity, precision loss from truncating division (if you ever do divide instead of cross-multiplying) can flip a should-block/shouldn't-block decision near the tie boundary — this is exactly why the cross-multiplied form (never dividing) is worth keeping even though it looks less readable.
- **Units as blockers**: decide this independently from terrain height blocking. Precedent splits: BattleTech/XCOM/Gloomhaven = units never block; Battle Brothers = units block with a miss-chance/redirect penalty and a short-range exemption for allies. If Mimas wants "shoot over your own front line at close range" (Battle Brothers' 2-tile exemption), that's a separate rule layered on top of the height test, not a special case inside it.
- **Woods/soft cover accumulation** (BattleTech): if Mimas ever adds partial-height "soft" obstacles (bushes, low walls) that don't fully block but degrade the shot, decide whether their heights stack additively along the path (BattleTech's 3 light-woods-hexes-to-block model) or whether the integer ray test only cares about the single tallest obstacle on the path (simpler, matches the Valorant framing better).
