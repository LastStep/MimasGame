---
title: Board mechanics — builds, traps, aura and fog, lasers, guaranteed actions
project: mimas
status: proposed
author: Claude, from a question round with Rohan
created: 2026-09-21
workbook: docs/design/mechanics.xlsx
research: studio/decisions/RESEARCH-2026-09-21-board-mechanics.md
---

# Board mechanics: builds, traps, aura and fog, lasers, guaranteed actions

**Status: proposed, concept level.** Rohan decided on 21 Sep 2026 that these concepts live here and in
the mechanics workbook, and that the design page (`docs/design/index.html`) stays untouched until they
are picked up for implementation. Nothing here is a work order. Names and numbers are deliberately
absent; the workbook carries them as TBD.

The workbook is the source of truth for detail: `docs/design/mechanics.xlsx`, sheets *Mechanics*
(M-060 to M-092), *Buildables*, *Triggers*, *Tunables* (T-060 onward), *Open questions* (OQ-N01 to
N15) and *Decisions* (D-2026-09-21-01 to 20, with Rohan's own words where he typed them).

## What Rohan asked for, and what it is underneath

Five ideas came in: buildable units on tiles with their own actions, prediction plays through hidden
tile spells and traps, an area of influence, a laser-sight prop, and a "guaranteed action" that fires at
turn start. Read as mechanics rather than features they collapse into three systems and one vocabulary:

1. **Vision.** A hero sees a circle around itself (the *aura*), cut by the existing sight ray. Fog hides
   everything outside your vision. Builds add vision. Attacks may or may not need vision, per attack.
2. **Building.** Three innate actions, move, build, destroy, each with a range. A catalogue of
   buildables, common plus lineage-unique. Structures are owned bodies with health; traps are hidden
   things under a tile. The laser sight is one structure.
3. **Guaranteed actions.** Autonomous structures act at the start of their owner's turn, in placement
   order, before AP and before the clock. The opponent always has one full turn to react.
4. **A trigger vocabulary** (events, conditions, effects) that traps, structures, modifiers and
   guaranteed actions share. This is the real design object; everything else is rows in it.

## The rules, as decided

### Aura and fog (M-060 to M-065)

- **Aura.** A circle of tiles around the hero, radius measured the same Euclidean way as attack ranges,
  cut by the sight ray from the hero's aim point. Walls, plateaus and bodies cast shadows. The aura is
  strictly hero-based; Blessings and similar may change its radius. Builds do not extend it.
- **The hero's vision is its aura.** Innate actions (build, destroy) reach only inside it.
- **Fog of war.** A player sees only the tiles inside their vision: aura plus the vision of every build
  they own. The enemy hero, enemy builds and anything on unseen tiles are absent from the `PlayerView`.
  Terrain, height and neutral props stay known; the map is not a secret, the pieces on it are.
- **Build vision.** Every build grants its owner vision in a per-kind radius, same shape rule. Builds are
  the scouting layer that a one-hero game otherwise lacks.
- **`needsVision`.** A third required attack field beside `lineOfSight` and `trajectory`. Weapon
  attacks default to `false`; spells decide per spell. Rohan wants the flag on weapon attacks too, for
  later ideas.
- **Blind shots.** An attack with `needsVision: false` may target a tile outside vision. If a body with
  health stands there it takes the hit (the sight ray and trajectory still apply); otherwise the AP is
  spent for nothing. This makes tile targeting legal, which the attacks page currently refuses.
- **Attack reveal.** Whenever anything performs an attacking action, a hero attack, a structure firing,
  a trap or laser going off, the attacker and its tile are revealed to the enemy until the start of the
  attacker's next turn. Fog protects only the silent.

### Building (M-070 to M-083)

- **Innate actions.** Move (walk), build, destroy. Every hero has all three; gear and boons never
  remove them. Build and destroy are a third ability type beside movement and attack.
- **Build.** Pick a buildable from your catalogue and a tile inside your aura that is walkable and
  empty. Directional builds pick one of six hex directions at build time. Flat AP cost per buildable.
  Building is not an attacking action, so it does not reveal you; being watched does.
- **Destroy.** Remove one of your own builds inside your aura, freeing its cap slot and tile. Flat AP.
  Never targets enemy builds. No refund, no detonation.
- **Catalogue.** Common buildables every hero has, plus buildables unique to the lineage. Base
  buildables are public knowledge; upgrades from boons are hidden until observed.
- **Cap per kind.** Each buildable has a maximum on the board per owner. At the cap, Build is refused
  with a reason. Destroy frees a slot.
- **Structures** are owned bodies: hp, per-kind body and aim height, they occupy the tile and block like
  a prop. Anything with health can be hit. They vanish at round end. Once seen, identity, direction and
  health are public for good. This answers the design page's open question on owned props.
- **Structure control** is per kind, by data: *autonomous* (acts only through guaranteed actions) or
  *hero-directed* (its action appears on the hero's bar, costs hero AP, and works only while the
  structure is inside the hero's aura). Structures never have AP of their own.
- **Traps** sit under a tile and are not bodies. Trigger: any body enters the tile, the owner included.
  No dud or feint builds. On firing, the trap is revealed fully and (proposed) consumed.
- **Trap visibility.** If the enemy had vision of the tile at the moment of placement, the tile carries
  a marker, "something is set here", from then on; content stays hidden. If the tile was unseen at
  placement, the trap is fully invisible, even when the enemy's aura later covers the tile. A scout
  build's vision shows traps as markers. Rohan chose *not* to let the aura alone reveal traps.
- **Laser sight.** A common directional structure. It projects a beam along a straight hex line in the
  direction chosen at build, stopping at the first body it meets. The beam is a zone: a body that enters
  or crosses a beam tile during a move takes the beam's damage then; a body standing on the beam at the
  owner's turn start takes it as a guaranteed action. The line is drawn on the tiles for anyone who can
  see the laser. Bodies are the counterplay. **The beam is symmetric: it hurts any body, its builder
  included** (Rohan, 21 Sep wrap-up), the same rule as traps.
- **Scout.** A common structure whose only job is vision. With blind shots, it is the intended counter
  to hidden traps.
- **Blind shot clears a trap.** A blind attack on a trapped tile destroys or triggers it (which of the
  two is open).
- **Boons on buildables.** An Enchant may target a buildable kind and change its numbers; a Sigil may
  add an action to a structure kind or unlock a new buildable. Hidden until observed, like gear.
- **Lineage builds reveal the lineage** once seen (derived from the existing lineage reveal rule, not
  asked; listed to confirm).

### Guaranteed actions and the trigger vocabulary (M-090 to M-092)

- **When.** At the start of the owner's turn, before AP is granted and before the clock starts.
- **Order.** Placement order (build sequence), each action fully resolved before the next reads the
  board. The order is shown as numbers on the board. Deterministic; the opponent changes it only by
  destroying things.
- **Vocabulary.** Events: `onDealDamage`, `onTakeDamage` (in code), `onEnterTile`, `onCrossTile`,
  `onOwnerTurnStart`, `onAttack`, `onBuilt`, `onDestroyed`, `onSeen` (new), `onEndTurn` (reserved).
  Conditions add `actor`, `bodyKind`, `inAura`, `inVision`, `direction` to the existing list. Effects
  add `dealDamage`, `spawnBody`, `placeTrap`, `removeBody`, `grantVision`, `reveal`, `mark`,
  `buildableOverride`, `grantStructureAction`. The full table is the *Triggers* sheet. Adding an entry
  is a design decision and one handler in Core.

## What this changes on the design page, when it gets there

Listed so the later task knows where to write. All would land as `data-design="proposed"`.

| Anchor | Change |
|---|---|
| `#vision` non-goals | "One hero per player" stands; structures never move and never spend AP of their own. Note the anti-pillar "No armies" is respected by construction. |
| `#hidden-info` table | Position is no longer always public (fog). New row: attack reveal. New row: builds. |
| `#tile-effects` | `q-tiles-visibility` answered: no, traps are hidden board state. Traps are the first tile effect. |
| `#props` | `q-props-owned` answered: structures. Body height per kind. |
| `#attacks` | New required field `needsVision`. "Targeting the ground is not legal" becomes "…unless the tile is out of vision (blind shot)". |
| `#abilities` | A third type, build / destroy, innate. |
| `#character` | Three innate actions. Aura as a hero property (possibly a stat, OQ-N01). |
| `#lineage` | Lineage-unique buildables. |
| `#boons` | Enchant and Sigil may target a buildable kind. |
| `#turns` | Guaranteed actions resolve at turn start, before AP and clock. |
| `#modifiers` | Generalised into the trigger vocabulary. |
| new `#aura`, `#fog`, `#build`, `#buildables`, `#structures`, `#traps`, `#laser`, `#guaranteed`, `#triggers` | New sections. |

## Risks the research flagged, and Rohan's calls

- **Fog plus a 30 s clock** is the documented frustration risk (Frozen Synapse postmortem: "I should have
  known" losses). Rohan chose fog with builds as scouts, which is the one configuration the research
  found workable for a single-hero game. Playtest it before anything else in this brief.
- **Symmetric traps** (hurting their owner) were flagged as a feel-bad. Rohan chose symmetric, no duds.
  Watch for players fencing themselves in.
- **Invisible traps set out of vision, from a public pool**, is the Yu-Gi-Oh end of the spectrum. The
  marker rule for watched placements and the scout build are the reads the opponent gets. If
  playtests show "backrow paralysis", the lever is the scout's cost and vision, not the trap rule.
- **The laser hurts its builder** (decided in the wrap-up). Together with symmetric traps, a player can
  fence themselves in. Watch for it.

## Open questions

Sixteen new ones, OQ-N01 to N16 on the *Open questions* sheet (N08 answered: the laser is symmetric;
N16 is the auto-end-turn question from the 21 Sep playtest). The three that block a first slice:

1. OQ-N03: does a jump or teleport cross a beam? Movement enters only its destination today.
2. OQ-N07: is a trap consumed when it fires?
3. OQ-N11: which lane and element do trap and laser damage use, and does the owner's power stat add?

## Precedents used

Into the Breach (telegraphed attacks, placement-order firing), Hearthstone Secrets (marker, not
content), Techies mines (invisible with a leak), Wesnoth zones of control and chess lines (a beam stops
at the first body), Wildermyth Interfuse and Dota wards (builds as vision). Details and sources in the
research record.
