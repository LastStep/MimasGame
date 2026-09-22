---
id: F-boons
title: Boons — lineages, blessings, enchants, sigils, the draft between rounds, and the best-of-3 session
project: mimas
milestone: M3
status: approved
lane: full
design_anchor: docs/design/index.html#boons
created: 2026-09-21
approved: 2026-09-21
spec: docs/specs/2026-09-21-boons-groundwork.md
spec_part2: docs/specs/2026-09-22-boons-in-game.md
adrs: [ADR-034, ADR-035]
tasks: [T-0009, T-0010]
research:
  - studio/decisions/RESEARCH-2026-09-21-boons-precedents.md
  - studio/decisions/RESEARCH-2026-09-21-effect-systems.md
---

# Boons

**One sentence:** a player prays to a lineage, starts with its Blessing, and between the rounds of a
best-of-3 drafts one boon from three — a passive, a change to one item, or a new ability on one item —
that the opponent only learns about when it changes something they can see.

## Why now

M2 is on the internet and everything left in it waits on a playtest. M3's first line is "depth", and
pillars 2, 3 and 4 (build, hidden knowledge, building across the session) all run through boons: without
them the session is one round repeated, and nothing is hidden but two dev passives. The design page has
had the whole system as `decided` since 16 Sep; the code has an empty `boons/` folder and a modifier list
on the unit. This is the largest system the game still lacks, and it is the one that most rewards being
built as a proper core rather than a pile of special cases — which is why it is two parts.

## What the player sees

**Part 1 (Core only) shows nothing yet**; it is the rules engine growing the whole system with tests:
definitions, a unit built from gear + lineage + boons, elements and immunities, the reveal rules, the
draft, and a `Session` that plays a best-of-3 in a test. **Part 2** is what a player sees: a lineage row
in the room, a draft screen between rounds with three cards and a timer, round 2 on the next map, the
examine panel listing boons with their god, a "Revealed: Agni's Crown (Hindu)" flyover, a Sigil's
ability under its item, an Enchant's changed number on the button.

## Rules it introduces or changes

Everything is on the design page as `decided` or `proposed`; the 21 Sep question rounds closed thirteen
open questions there (part-1 spec §2 and §10). The additions in one line each: the effect vocabulary is
closed (`stat`, `modifier`, `abilityOverride`, `addElement`, `addTag`, `grantAbility`); an Enchant targets
a slot or one ability; cost never drops below 1; self trade-offs are allowed with floors; elements are a
set on an attack; offer counts per role are data (3 / 3); an Enchant reveals on the observation that
contradicts what the opponent knows; an hp / AP Blessing is public from round start; round 1 by coin
flip, then the loser moves first; elements fire / frost / lightning; the ladder wraps; no tiers, rerolls
or cap at launch; `exclusive[]` groups exist.

## Answered by Rohan on 21 Sep 2026

See the part-1 spec §2 (D1–D18). The one on strategy: **Fable builds part 1, then writes the part-2 spec
against the real code; Opus executes part 2.**

## Capability checklist

Part-1 spec §3: what each boon kind can do, marked *works*, *skeleton* (parses, fails closed, tested) or
*later* (design question first). Rohan asked for the system to be a core that evolves; the skeleton
column is the todo list.

## Done when

- Part 1: the part-1 spec §14, verified in a fresh context; ledger rows stay unticked (they are player-
  visible outcomes and need part 2).
- Part 2: its own spec's done-when; the verifier ticks **M3-3, M3-4, M3-5** and the draft half of
  **M3-6**; Rohan plays a best-of-3 against a friend with a draft between rounds.

## Not in this feature

Enemy debuffs, statuses and per-turn triggers, area shapes, non-damage spells, use counters, tiers and
rarity, rerolls, a comeback draft (the counts are data, so it is a number change later), a third map,
presets stored on the server, a balance simulation (part 3 if wanted).
