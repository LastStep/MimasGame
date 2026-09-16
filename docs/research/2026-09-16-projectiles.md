# Research: delivery / "way of aiming" systems for a hex tactics game

Context: Mimas needs a data-driven `delivery` mode per ability — direct line, lobbed arc,
or "falls from the sky" — with integer-only, deterministic resolution and boons able to
swap one delivery for another.

## 1. Tabletop precedent: what actually blocks a lob

The consistent pattern across tabletop wargames: **almost none of them trace the physical
arc against terrain height.** They gate indirect fire on *visibility*, not geometry.

- **BattleTech (LRM indirect fire).** A spotter unit needs line of sight to the target; the
  firing unit does **not**. The spotter cannot fire on the turn it spots. The shot uses
  range measured from firer to target, applies the spotter's terrain/movement modifiers as
  if the spotter had fired, plus a flat +1 to-hit for firing indirectly. Nothing in the rule
  checks whether the LRM's ballistic arc physically clears an intervening hill — the
  spotter's LOS *is* the "did it clear terrain" check.
  [Sarna forums](https://www.sarna.net/forums/showflat.php/Cat/0/Number/175338/Main/175298),
  [BattleTech forums](https://battletech.com/forums/index.php?topic=17356.0)
  BattleTech also has a well-known **minimum range** rule for LRMs (accuracy penalty when
  firing at a target closer than the weapon's minimum range band) — a distinct guard against
  "can't drop a shell on top of yourself," worth flagging as an open question for Mimas' arc
  mode.
- **Warhammer 40k 10th ed., `[INDIRECT FIRE]` keyword.** Weapons with this keyword can
  target units with zero visible models. Penalty: -1 to hit, an unmodified 1-3 always fails,
  and the target gets Benefit of Cover. `[TORRENT]` weapons (auto-hit, no roll) cannot use
  `[INDIRECT FIRE]` — an explicit tag-conflict rule. Again: it's a *keyword tag* on the
  weapon profile, purely a to-hit modifier, no path/height tracing.
  [New Recruit wiki](https://www.newrecruit.eu/wiki/wh40k-10e/warhammer-40,000-10th-edition/rules/indirect-fire)
- **Kill Team.** `[INDIRECT]` changes how the *cover/visibility state* of the target is
  computed at the targeting step (it stops treating the target as "not in cover" the normal
  way) rather than exempting the shot from geometry checks outright — same family of
  solution: modify the visibility/cover resolution, not the flight path.
  [Kill Team FAQ notes](https://www.offmetamusings.com/2021/09/killteam-2021-frequently-asked-questions.html)
- **ASL mortars / OBA.** Two firing modes: direct (Hit Probability Table, like a tank gun,
  requires LOS) or indirect via a spotter (Call For Fire → d100 roll → Fire For Effect or
  adjust next turn). Indirect fire must be declared in a dedicated Indirect Fire Segment.
  Conceptually identical to BattleTech: a human/unit acts as the LOS proxy.
  [ASL tutorial lesson 4](https://asl-players.net/downloads/ASLTUTE_4.pdf)
- **Battle Brothers.** No explicit "lob" keyword found, but its ranged-blocking model is
  instructive: a blocked shot doesn't fail outright, it gets a heavy (75%) chance to miss
  and *scatter* onto the obstruction or an adjacent tile instead of a hard block. Elevation
  extends sight over obstacles (a height-vs-height check at the *shooter*, not along the
  path). [Hit Chance wiki](https://battlebrothers.fandom.com/wiki/Hit_Chance)
- **Wikipedia, "Indirect fire" / "Plunging fire."** Formal definition: indirect fire is
  fire "delivered at a target which cannot be seen by the aimer"; the gun crew never needs
  LOS, only some observer does. Real-world artillery computes a genuine ballistic parabola,
  but every tabletop abstraction above throws that away in favor of a binary
  visibility/spotter test. [Indirect fire](https://en.wikipedia.org/wiki/Indirect_fire)

**Takeaway:** tabletop design overwhelmingly answers "does the lob clear the terrain?" with
"can *someone* (spotter, or the target's own exposure) see the target?" — not with real
arc-vs-height collision. Only BattleTech's minimum-range rule and the general concept of
"apex" gesture at real ballistics, and even those are flat modifiers, not geometry.

## 2. Digital games: preview + resolution of arcs

- **Baldur's Gate 3 (Throw).** The throw arc is tall/pronounced, and critically, **the game
  does check the physical path**: the thrown object's hitbox must clear the geometry along
  the whole arc, so low ceilings, doorways, and trees block throws even when the target is
  reachable and lower than the thrower — a frequent player complaint ("Path is
  interrupted"). Bow/arrow trajectories are flatter and far more forgiving than the throw
  arc, i.e. **BG3 encodes at least two different delivery curves** (throw vs. shoot) with
  different collision strictness. Popular mods flatten the throw arc to match the bow's.
  [BG3 wiki gameplay](https://bg3.wiki/wiki/Gameplay_mechanics),
  [Nexus mod: Throw Trajectory fix](https://www.nexusmods.com/baldursgate3/mods/13724)
- **Divinity: Original Sin 2.** Grenades render a visible arc plus an AoE circle at
  cast-time; they are 100% accurate (no deviation, unlike DOS1's grenades). Line-of-sight
  is inconsistent across abilities — "some abilities need direct line of sight, some shoot
  in an arc" — suggesting delivery/LOS is effectively hardcoded per-skill rather than one
  clean shared system, which produced confused player reports.
  [Steam LOS discussion](https://steamcommunity.com/app/435150/discussions/0/1483232961035710434/)
- **XCOM 2.** Grenade throws can be foiled by low cover directly ahead at a flat trajectory,
  but throwing at a steeper angle clears the same obstruction — informally corroborating an
  arc-height-vs-obstacle-height model, though no official document spells out an exact
  formula or max apex.
- **Into the Breach.** The purest "sky" example: artillery mechs fire in an arc that flies
  **over everything** — no LOS check, no path collision — the only restriction is that
  artillery cannot target the tile immediately adjacent to the shooter (a minimum-range
  rule again). This matches the tabletop "spotter model" taken to its logical extreme:
  delivery = "ignore the whole board, only the destination tile and min-range matter."
  [Into the Breach wiki, Attacks](https://intothebreach.fandom.com/wiki/Attacks)
- **Phoenix Point.** Explicitly distinguishes "direct fire weapons" from indirect ones
  (grenade launchers, mortars); a defensive "Return Fire" ability only triggers against
  attacks made with a *direct* weapon, i.e. delivery type is a first-class attribute of the
  weapon that other systems key off of, not just a flavor label.

**Takeaway:** digital tactics games split into the same two camps as tabletop —
"trace the real path and let it get blocked" (BG3 throws) vs. "abstract it into an
endpoint/visibility check and ignore the middle" (Into the Breach, most of DOS2). The
former feels more physical but produces exactly the false-positive frustration
("blocked?! by that?") that BG3 players complain about; the latter is cheap, predictable,
and easy to keep deterministic across client/server.

## 3. Integer math for a deterministic sampled arc

**Model.** Distance `D` hexes, start height `h0` at sample `i=0`, end height `h1` at
`i=D`, designer-set apex clearance `A` (height above the *higher* endpoint at the
horizontal midpoint of flight). Sample at every hex boundary `i = 0..D`.

Build the arc as *linear interpolation between endpoints* + a *symmetric parabolic bump*
that is zero at both endpoints and equal to `B` at the midpoint, where `B` is chosen so the
peak equals `max(h0,h1) + A`:

```
linInterp(i) = h0 + i*(h1-h0)/D
bump(i)      = B * 4*i*(D-i) / D^2         // 0 at i=0 and i=D, peak B at i=D/2
B            = A + |h1-h0| / 2             // makes linInterp(D/2)+B == max(h0,h1)+A
height(i)    = linInterp(i) + bump(i)
```

Multiply through by `D^2` and simplify (the factor of 2 needed for the `|h1-h0|/2` term
cancels cleanly) to get an **all-integer** formula — no floats anywhere, division deferred
to a single final cross-multiplied comparison:

```
height(i) * D^2  =  D^2*h0  +  D*i*(h1-h0)  +  4*A*i*(D-i)  +  2*i*(D-i)*|h1-h0|
```

To test "does sample `i` clear a tile of height `H`" without ever dividing:

```
clears(i, H)  ⟺  D^2*h0 + D*i*(h1-h0) + 4*A*i*(D-i) + 2*i*(D-i)*|h1-h0|  ≥  H * D^2
```

All terms (`D, i, h0, h1, A, H`) are plain `int`s. This is the same value on server and
client bit-for-bit, satisfies rule 4 (no floats), and costs `O(D)` integer multiplications
per resolved shot — trivial for hex ranges in the 1-12 tile range Mimas will use.

### Worked example

`D = 5`, `h0 = 0`, `h1 = 2`, `A = 3` (apex should reach `max(0,2)+3 = 5` at the flight's
horizontal midpoint, i.e. between samples 2 and 3).

`D^2 = 25`. Plugging into `height(i)*25 = 25*0 + 5*i*2 + 4*3*i*(5-i) + 2*i*(5-i)*2`
`= 10*i + 16*i*(5-i)`:

| i (hex boundary) | 10i + 16i(5-i) | ÷25 = height(i) |
|---|---|---|
| 0 | 0 | **0.00** (= h0) |
| 1 | 10 + 64 = 74 | 2.96 |
| 2 | 20 + 96 = 116 | 4.64 |
| 3 | 30 + 96 = 126 | 5.04 |
| 4 | 40 + 64 = 104 | 4.16 |
| 5 | 50 + 0 = 50 | **2.00** (= h1) |

The continuous peak (checked at the true midpoint `i=2.5`) evaluates to exactly `5.00`,
matching `max(h0,h1)+A = 5` — the formula is correct, and samples 2 and 3 (4.64 and 5.04)
correctly bracket it. To decide if the arc clears, say, a height-4 tile at boundary `i=2`:
`116 ≥ 4*25=100` → true, clears. A height-5 tile at `i=2` would fail (`116 < 125`) even
though the *true* peak (5.0) technically reaches it, because the sample grid only checks
hex boundaries — a resolution trade-off to note explicitly in the ability data (denser
sampling, e.g. per half-hex, tightens this at 2x the integer ops).

### Alternative, cheaper models

- **Threshold/no-curve model:** "an arc clears any tile whose height is
  `< max(h0,h1) + A`" for every intervening tile, with no real curve at all. Zero
  per-sample math, matches how most tabletop games *effectively* behave (see §1), but is
  geometrically wrong very close to a low endpoint (a tall spike one tile from the archer
  would be incorrectly ruled "clear" even though a real arc couldn't have gained height
  that fast).
- **"Only the target/shooter tile matters" model:** ignore the path entirely; block only
  on the target's own visibility/cover (this is what Into the Breach and most of DOS2 do
  in practice, per §1-2). Cheapest of all, and arguably the most player-predictable, since
  players reason about "can I see them," not "does the arc clear that tower."

## 4. Naming survey

Terms found in the wild for this concept: **"trajectory"** (BG3 community, ballistics
articles), **"indirect fire"** / **"direct fire"** (all the wargames, Phoenix Point),
**"delivery"** (not a standard industry term, but matches the designer's own phrase "way
of aiming"), **"targeting type"** (Dota2 — but that's about *what you select*: unit/point/
area/no-target, an orthogonal axis, see §5), **"attack pattern"**, **"aim mode"**, and in
shooters specifically **"hitscan" vs "projectile" vs "grenade/lob"** (Overwatch/Valorant-
style vocabulary: hitscan = instant straight line blocked by geometry, projectile = travels
and can be dodged, lob/grenade = arced).

Three candidates for the ability data field:

1. **`delivery: "direct" | "arc" | "sky"`** — matches the designer's own vocabulary
   ("delivery"/"way of aiming") verbatim, and doesn't collide with a likely existing
   `targetShape`/`targetingMode` field (unit vs. tile vs. area) the way "targeting" would.
   Recommended.
2. **`trajectory: "direct" | "arc" | "sky"`** — the term the industry actually uses in
   docs/forums (BG3, XCOM, general ballistics), most discoverable to a new engineer. Slight
   mismatch: "sky" (no LOS check at all) isn't really a *trajectory shape*, it's a targeting
   rule, so the enum values sit a little awkwardly under this name.
3. **`aimMode`** — echoes "way of aiming" directly, but risks being read as an *input/UI*
   concept (how the player aims the cursor) rather than a *resolution* rule (how LOS/height
   is checked), which is the part that actually matters for determinism.

## 5. Data-driven precedent for override-able fields

- **Battle for Wesnoth (WML).** Attack `range` is a free-form string (conventionally
  `melee`/`ranged`) on the attack definition, kept deliberately separate from damage
  `type`. Modifiers (`[effect] apply_to=attack set_range=...`) can **overwrite** it outright
  — a clean precedent for "a boon replaces the field's value," not "a boon adds a flag on
  top of it." [EffectWML](https://wiki.wesnoth.org/Effectwml)
- **Dota 2.** Keeps *targeting type* (`AbilityBehavior`: unit target / point target / area
  target / no target — i.e. **what you aim at**) completely separate from things like cast
  range. This is the orthogonal-axis lesson for Mimas: `targetShape` (what tiles/units are
  selected) and `delivery` (how the effect physically reaches them / what can block it)
  should be two independent enums on the ability, not one combined field.
- **Warhammer 40k / Kill Team.** `[INDIRECT FIRE]` is a **tag**, not a replacement of a
  single delivery field, and the ruleset explicitly forbids some tag combinations
  (`[TORRENT]` + `[INDIRECT FIRE]`) — the "add tag" approach needs its own conflict-
  resolution rules once two tags interact.
- **DOS2 (cautionary).** LOS/arc behavior reads as bespoke per-skill rather than driven by
  one shared field, which is exactly the "invented rule in code, not in data" failure mode
  Mimas' CLAUDE.md rule 11 warns against.

**Override vs. add-tag:** for a *mutually exclusive* concept like "how does this attack
travel" (it can't simultaneously be direct and sky), a single **override-able enum**
(Wesnoth's `set_range` pattern) is simpler, always has exactly one deterministic answer
for the height-clearance check, and is trivial for a boon to swap (`delivery: "arc"` on
the modified ability). Reserve a **tag list** for secondary, stackable, non-exclusive
effects layered on top of whichever delivery is active (e.g. `ignoresCover`,
`piercesLine`) — mirroring how Dota2 keeps targeting-type and cast-range as separate knobs,
and how 40k's tag system still needs explicit conflict rules the moment two tags overlap.

## Recommended model for Mimas

**Option A — Endpoint/visibility-only (tabletop model).** `direct` = existing hex-LOS ray
check shooter→target; `arc` = ignore all intervening tile heights, only check target-tile
(and maybe shooter-tile) exposure, i.e. §1's "spotter" abstraction; `sky` = always clears,
optionally gated by a ceiling/indoor flag on the target tile. Cheapest, fully matches
tabletop precedent, zero new math. Trade-off: an arc shot can never be blocked by a tower
standing directly between shooter and target, which some players will find unintuitive
given the design explicitly wants "arrow lobbed over terrain" to feel physical.

**Option B — Sampled integer parabola for all non-direct deliveries** (§3's formula).
Most faithful to the "BG3 throw arc" feel the designer referenced, deterministic and cheap
(`O(D)` int ops), but adds a per-ability `apexClearance` data field and more surface area
to test (sample resolution, min-range edge cases per BattleTech's LRM rule).

**Option C — Hybrid (recommended).** Use Option A's cheap check for `direct` (real hex-LOS)
and `sky` (always-clear, or ceiling-only), and reserve Option B's sampled-parabola math
*only* for `arc`, since that's the single delivery mode where "does it clear the terrain
between" is actually the interesting, physically-flavored question the designer cares
about. This keeps two of three modes nearly free, concentrates the new integer math in one
well-tested function (`ArcHeightAt(D, h0, h1, A, i)`), and lets a boon retarget an ability
from `direct` to `arc` (or vice versa) by swapping one enum plus supplying/omitting
`apexClearance` — consistent with Wesnoth's override pattern and Dota2's separation of
targeting shape from delivery.

Open question to log in `docs/design/index.html` before implementing: does Mimas want a
BattleTech-style **minimum range** for `arc`/`sky` deliveries (so you can't lob a spell on
your own head), and should sample resolution be per-hex-boundary (as worked above) or
finer (e.g. half-hex) for narrow gaps between tall tiles?
