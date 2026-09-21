---
id: RESEARCH-2026-09-21-board-mechanics
title: Precedents for five board-mechanic families (buildables, traps, influence, beams, guaranteed actions)
project: mimas
lane: director
status: open
author: researcher
created: 2026-09-21
feeds: docs/design/mechanics.xlsx, docs/design/2026-09-21-board-mechanics.md
---

# Precedents for five board-mechanic families

Context assumed: 1v1, one character each, 3 AP per turn, deterministic, hidden info via `PlayerView`,
best of 3, 30 s server turn. This brief informed the 21 Sep 2026 design session with Rohan. It is a
research record, not a decision; the decisions are in `docs/design/2026-09-21-board-mechanics.md`.

**On "bgs":** in the Hearthstone community "BGs" is the standard abbreviation for Battlegrounds; given
the boons-are-TFT-style framing, Hearthstone Battlegrounds is the most plausible reading. Alternatives:
"BG" as generic board game, or Baldur's Gate 3 summons.

## 1. Buildable / placeable units with their own actions

**Hearthstone Battlegrounds.** Summons ("tokens") are minions created by other cards; they fight
automatically in the combat phase (left-to-right attack order, no player input); tokens created during
combat are removed at end of combat. The owner has zero control once combat starts.
https://hearthstone.wiki.gg/wiki/Battlegrounds · https://hearthstone.wiki.gg/wiki/Token

**Duelyst.** Minions can only be summoned on an empty tile adjacent to one of your units; they cannot
act the turn they arrive (Rush aside). Board tiles carry modifiers (Shadow Creep: enemy on it takes 1 at
end of owner's turn). Placement adjacency is the whole tempo game. https://duelyst.fandom.com/wiki/Game_Rules

**Faeria.** One free "power wheel" pick per turn: place 1 land (or 2 prairie), draw, or +1 faeria. New
land only adjacent to your land or a tile your creature occupies. Land is the map itself: building is
territory, not a unit. https://faeria.fandom.com/wiki/Lands_and_land_placement

**Gloomhaven.** Summons act *before* the summoner each round, using monster AI (focus nearest enemy).
The player cannot steer them. https://gloomtactics.blogspot.com/2021/06/gloomhaven-common-rules-you-may-not.html

**Into the Breach** is the inverse (buildings are the loss condition, mechs are the actors).
**Mechanicus**: servo-skulls are free scanners, not combatants. **Wildermyth**: Mystics "Interfuse" with
scenery; the object becomes the attack origin and gives vision. https://wildermyth.com/wiki/Interfuse
**Dota**: wards are pure vision, no actions; Techies mines are family 2.

*What it does well:* extends the single character's footprint; creates tempo (spend now, act later);
positional puzzles (adjacency rules). *Failure modes:* autonomous summons that act wrongly feel worse
than doing nothing; summons that need AP to act compete with the character for 3 AP and rarely win;
board clutter and turn length; the "one character" identity dilutes if summons are strong.
*AP/clock note:* the cleanest precedent (Gloomhaven, BGs) is free autonomous action at a fixed phase.
Prefer "build costs AP, firing is automatic" (family 5).

## 2. Hidden prediction plays (face-down traps)

**Yu-Gi-Oh!** Trap set face-down; cannot activate the turn it is set; activates in response to a game
event on either player's turn and can be chained. A face-down card has no properties, so any card can
be set as a bluff; opponents pay with backrow removal or by "testing" with a low-value play. The pool is
the entire card game, so play-around is by category, not exact card.
https://yugipedia.com/wiki/Trap_Card · https://yugipedia.com/wiki/Face-down

**Hearthstone Secrets.** Max 5 active, no duplicates, trigger only on the opponent's turn, fire in the
order played, and the opponent sees the class colour; all secrets in a class share one mana cost so cost
leaks nothing. Counterplay is deduction over a small public pool plus test plays and a few removal cards.
https://hearthstone.wiki.gg/wiki/Secret. No primary Blizzard post on *why* the pool is small was found;
Ben Brode's on-record framing: Secret Paladin "doesn't just kill you in one turn — you see it coming,
and there are opportunities to play around."
https://www.pcgamer.com/hearthstones-ben-brode-on-rng-esports-and-the-rise-of-secret-paladin/

**Dota 2 Techies mines.** Invisible but emit a warning sound audible through fog on entering the
trigger radius; sentry wards reveal. Counter-scouting is an economy. https://dota2.fandom.com/wiki/Techies

**Duelyst / Faeria:** no face-down tile mechanic exists in either.

*Known pool vs unknown pool:* a known pool makes traps a deduction game and keeps bluffs cheap to read;
an unknown pool makes bluffing deep but produces "backrow paralysis" where a novice cannot reason at
all. *Failure modes:* feel-bad when a trap decides a round with no scouting available; "set the same
trap every game"; every move becomes a risk read, brutal under a 30 s clock. *AP/clock note:* a trap
costs AP to set and forces the opponent to spend *time* reasoning; keep the pool small, public,
non-duplicable, and give a cheap test action.

## 3. Area of influence / vision / control zones

**Battle for Wesnoth ZoC.** Every unit exerts a zone on all 6 adjacent hexes; an enemy entering one
must stop. Two units two hexes apart form a wall. Fully public. https://wiki.wesnoth.org/BasicStrategy

**Advance Wars fog.** Radius vision per unit; moving into an unseen unit ambushes: movement stops and the
unit loses its action. https://awbw.fandom.com/wiki/Fog_of_War

**Fire Emblem fog.** Per-unit vision; Torch extends vision, decaying per turn. https://fireemblemwiki.org/wiki/Fog_of_war

**XCOM 2.** Enemy *detection radius* is a visible tile set that moves with the enemy, drawn only for
enemies you can already see. https://xcom.fandom.com/wiki/Concealment

**Frozen Synapse Dark mode.** Enemies visible only in LOS; last known position greys out.

**Fog-of-war chess** is the closest 1v1 analogue: "poker plus chess", tactics matter less.
https://www.chess.com/terms/fog-of-war-chess

*The one-unit problem:* with one character, vision equals your own position, so fog has no scouting
layer. Every fog game above has multiple units or wards. A single-unit fog game degrades to guessing
unless structures or traps *are* the scouts. *Failure modes:* ambush stops feel bad without a warning;
fog plus a clock punishes thinking time; ZoC without fog is the safest, most readable control mechanic.

## 4. Persistent directional hazards

**Hoplite.** Demon Wizard fires a range-5 beam in one of 6 hex directions, hits everything in line,
fully telegraphed, cannot fire two turns running. https://github.com/ychalier/hoplite/blob/master/RULES.md

**Portal 2 Thermal Discouragement Beam.** Persistent laser, redirected by cubes and portals; the beam
is a resource both sides can redirect. https://theportalwiki.com/wiki/Thermal_Discouragement_Beam

**Into the Breach line attacks** hit the first thing in the line, so a body blocks it. **Chess
rook/bishop**: control lines are public, unlimited, stopped by the first piece, threat rather than damage.

*What works:* a persistent line is a public map edit that shapes movement with no per-turn decisions.
*Failure modes:* crossing lasers cage the map; a hazard that damages the owner's own summons; ambiguity
between *ending* on a beam and *passing through* it. Chess's rule (line stops at the first body) is the
cleanest and gives blocking as counterplay. Render lines on tiles explicitly.

## 5. Guaranteed actions at start of owner's turn

**Into the Breach (the model).** Each Vek moves, then *declares* an attack (direction and target tiles
shown). On the player turn all of them are visible; environmental effects resolve before Vek attacks;
attack order is shown on demand (Alt) and is the order the Vek moved. Attacks target *tiles*, so the
player alters them by killing the Vek, pushing it (the attack line moves with it), pushing another body
into the line, standing in the line to absorb it, freezing or smoking the Vek, or shielding the target.
https://intothebreach.fandom.com/wiki/How_To_Play_Guide_For_Into_The_Breach ·
https://gamefaqs.gamespot.com/pc/205477-into-the-breach/faqs/76363/game-mechanics ·
Matthew Davis: "our vision… is a push to be more deterministic in the moment-to-moment gameplay"
https://www.pcgamer.com/into-the-breach-preview/ · GDC 2019 talk (members only) https://gdcvault.com/play/1026333/-Into-the-Breach-Design

**Slay the Spire intents.** Show type and a damage tier, not the exact number or hit count.
https://slaythespire.wiki.gg/wiki/Intent

**Magic upkeep triggers.** Rule 603.3b: the active player orders their triggers, then the non-active
player; a visible stack. https://mtg.fandom.com/wiki/Triggered_ability

**Hearthstone.** Simultaneous triggers resolve in order of play, each fully before the next. No choice.
https://hearthstone.wiki.gg/wiki/Advanced_rulebook

*Ordering rules seen:* placement order (Hearthstone, Dota, Gloomhaven), move order (ITB), owner choice
(Magic). For a deterministic server under a clock, placement order shown as a numbered list is the
strongest precedent: deterministic, visible, and manipulable by killing or moving things.
*Failure modes:* hidden order ("I didn't know that would fire first"); owner choice means analysis
paralysis; too many simultaneous firings are unreadable.

## 6. Composing hidden traps, face-down structures and fog in a one-unit 1v1

- The literature converges on **"telegraph the shape, hide the content"**. Hearthstone reveals that a
  secret exists and its class; ITB reveals everything but spawn positions; StS reveals type but not the
  number; Techies mines are invisible but audible; XCOM draws detection tiles once the enemy is seen.
  Every successful case leaks *some* structure so the defender has something to reason about.
- **Small, public, non-duplicable pools** turn hidden info into deduction; open pools turn it into
  bluff-reading, which is deep but slow and novice-hostile.
- **Frozen Synapse postmortem:** when everything is technically predictable, losses feel like "I should
  have known", which "just leads to significant frustration" and "a constant sense of pressure… to make
  a perfect decision." Under a 30 s clock this is the main risk of stacking three hidden systems.
  https://www.gamedeveloper.com/audio/postmortem-mode-7-games-i-frozen-synapse-i-
- **One unit each removes counter-scouting.** Structures and traps must double as scouts, or fog should
  be dropped and only *content* hidden (position public, identity hidden).
- **Composition rule that falls out:** at most one *unknown* per tile interaction. A face-down thing on
  a known tile inside a public laser line, firing in a displayed order, is readable; a face-down thing
  on an unseen tile in fog is not, and no precedent was found that makes that work in a 1v1.

**Not verified:** a primary Blizzard blog on secret-pool size; the ITB GDC slide text; Faeria/Duelyst
hidden tiles (none exist as far as could be found).
