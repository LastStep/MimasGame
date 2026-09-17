---
project: mimas
owner: rohan
status: draft
updated: 2026-09-17
source: docs/design/index.html#vision
---

# Mimas: pillars and anti-pillars

> **This file is Rohan's.** No agent edits it after this first draft. It was distilled from the fifteen
> pillars in `docs/design/index.html#vision` down to the few that decide arguments — fifteen is a
> feature list, and a pillar you would not cut a feature for is not a pillar. Rohan: cut, rewrite,
> reorder. What survives here outranks everything else in the repo.

## The pillars

**1. A fight from Baldur's Gate 3, made into chess.**
Tactical positional combat with the story, the dice and the downtime removed. Readable, deterministic,
competitive, a ladder you climb. Time controls are game modes — bullet, blitz, rapid.

**2. Depth comes from position, build and knowledge — never from randomness.**
Where you stand, what you equipped, which lineage you chose, what you drafted, and what you have worked
out about the opponent. No random damage. Same inputs, same result, on both machines.

**3. What your opponent has is hidden until they use it.**
Abilities, lineage and boons are unknown until first used, then known for the rest of the session. The
game is partly about finding out what you are fighting.

**4. You build across the session, not within the round.**
Gear, lineage and every drafted boon carry forward through a best-of-three across a ladder of maps.
Losing a round changes what you draft, not what you have.

**5. It opens in a browser and it is immediately legible.**
Fast load, no UI lag, a minimal HUD, depth on demand through examine. If a player cannot tell why a
shot was refused, that is a bug in the game and not in the player.

## The anti-pillars — what Mimas is not

These exist to be quoted when something is proposed. "It would be cool if…" loses to this list.

- **No story.** No campaign, no dialogue, no lore beyond one line per item or boon. Lineages are
  flavour and mechanics, never narrative.
- **No random damage.** Ever. Variance comes from hidden information and from the draft, not from a
  die roll resolving a hit.
- **No hidden maths.** If the game knows a number that changes a decision, the player can see it. The
  attack preview shows the damage it would do even for a shot it is about to refuse.
- **No armies.** One hero per player at launch. The model is multi-unit ready; summons are a later
  boon idea, not a direction.
- **Not a strategy game.** No economy, no base, no meta-progression between sessions beyond rating.
- **No pay-to-win, and no monetisation shaping a rule.** Income is secondary to the craft.
- **Not a mobile game.** A mobile browser should not crash; it does not get to shape the UI.

## How to use this file

Before proposing a rule, a mechanic or a feature, check it against the anti-pillars first. If it
contradicts one, the answer is no and the conversation is over unless Rohan changes this file. If it
does not obviously serve a pillar, say which one it serves and how — and if the answer is laboured, it
is scope, not depth.

The design page (`docs/design/index.html`) remains the source of truth for what the rules *are*. This
file is what the rules are *for*.

## Open for Rohan

- Is **hidden information** (3) really a pillar, or is it a mechanic serving pillar 2? It is written as
  its own pillar because the whole `PlayerView` architecture exists to serve it — but you decide.
- Pillar 5 folds together "browser-based, lean" and "clean in-game UI" from the design page. Two
  pillars or one?
- "Ladder of maps" and "map symmetry" from the design page have been left out as consequences of
  pillar 1 rather than pillars in their own right. Fair?
