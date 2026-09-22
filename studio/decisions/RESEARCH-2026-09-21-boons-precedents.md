---
id: RESEARCH-2026-09-21-boons-precedents
title: Boon and draft precedents — Hades, TFT, Slay the Spire, Monster Train, Rounds, hidden picks
project: mimas
written: 2026-09-21
by: Claude (Sonnet research subagent), for the boons spec session
for: docs/design/index.html#boons, #draft, #lineage, #hidden-info
---

# Boon / draft design precedents

Facts with sources. Anything the subagent could not verify is marked **unverified**.

## 1. Hades (Supergiant)

- Rarity tiers: Common, Rare, Epic, Heroic. Legendary and Duo boons sit outside the tier ladder.
  [Hades Wiki: Boons](https://hades.fandom.com/wiki/Boons),
  [DBLTap rarity guide](https://www.dbltap.com/posts/hades-boon-rarity-guide-to-standard-and-special-boons-01ek30qqebzt)
- Prerequisites: Duo boons need specific boons from *both* gods already held; Legendary needs a tier-1 plus a
  specific tier-2 boon of one god. [Prima Games](https://primagames.com/gaming/hades-guide-legendary-heroic-duo-boons)
- Exclusivity: Attack, Special, Cast, Dash, Call each hold exactly one boon. Replacing one via Sacrifice
  inherits the old level and gains a rarity tier, so replacing is never a pure downgrade.
  [Steam discussion](https://steamcommunity.com/app/1145360/discussions/0/3034851664982152155/)
- Offers: three choices; a god keepsake guarantees the next boon offer is from that god. The slot pattern
  of the three choices is fixed per encounter, rarity varies.
  [Steam discussion](https://steamcommunity.com/app/1145360/discussions/0/2932364348237093214/)
- Reroll (Dice): one of the previously offered boons is marked ineligible so the reroll always changes at
  least one option. [Steam discussion](https://steamcommunity.com/app/1145360/discussions/0/3969311271670813573)
- Pool sizes: no official per-god total (**unverified**). Hades 1: 28 Duo across 8 gods, 12 Legendary.
  Hades 2: 37 Duo across 9 gods; Infusions (threshold on accumulated elemental essence) are new;
  Zeus/Poseidon ~14 standard boons each, Athena ~6 (**unverified**, secondary sources).
  [Duo Boons](https://hades.fandom.com/wiki/Duo_Boons), [Hades II boons](https://hades.fandom.com/wiki/Boons/Hades_II),
  [Infusion](https://hades.fandom.com/wiki/Infusion)

## 2. Teamfight Tactics augments

- Tiers Silver / Gold / Prismatic, offered at fixed stages 2-1, 3-2, 4-2.
  [League Wiki: Augment](https://wiki.leagueoflegends.com/en-us/TFT:Augment)
- Three offers; each can be rerolled once. Rule: never three economy augments at once
  (anti-degenerate-offer rule). [Fandom: Augment](https://leagueoflegends.fandom.com/wiki/Augment_(Teamfight_Tactics))
- Hero augments (Set 8, 118 of them) were removed in Set 9.
  [Fandom: Set 9](https://leagueoflegends.fandom.com/wiki/Augments_(Teamfight_Tactics_-_Set_9))
- Rationale: "variation in small ways that lead to more potential outcomes"; champion-specific augments
  are best when they change how a champion plays but are narrow. No dev blog on pool sizing found
  (**unverified**). [Medium](https://medium.com/@briansamadam/tft-champion-augments-are-fun-e5365197cc0d)

## 3. Draft precedents and comeback drafts

- Slay the Spire: rarity rolled per encounter type, then a card of that rarity; a pity counter raises rare
  odds after droughts; no repeats within one 3-card offer.
  [Fandom: Card Rewards](https://slay-the-spire.fandom.com/wiki/Card_Rewards)
- Monster Train: 42 cards per clan (2 champion, 10 common, 18 uncommon, 12 rare).
  [Trainworks guide](https://github.com/Monster-Train-2-Modding-Group/Trainworks-Reloaded/wiki/Custom-Clan-Design-Guide)
- Dota 2 Ability Draft: shared pool from 10 random heroes plus 2 spares; 3 regular + 1 ultimate each.
  [Fandom](https://dota2.fandom.com/wiki/Ability_Draft)
- Dota Underlords shared bag 30/20/15/12/10 by tier.
  [Esports Tales](https://www.esportstales.com/dota-underlords/hero-pool-size-and-unit-odds-by-level)
- **Rounds (Landfall)** is the clearest comeback-draft precedent: the round **loser** picks 1 of 5 cards;
  the winner gets nothing. Whether the opponent sees the exact pick is **unverified**; effects are
  largely visible on the character model.
  [Rounds Fandom](https://rounds.fandom.com/wiki/Rounds), [Bits & Pieces](https://bitsandpieces.games/2024/02/23/rounds/)
- Into the Breach pilot skills: nothing usable found (**unverified**).

## 4. Hidden picks in competitive PvP

- Hearthstone Battlegrounds shows every player's hero on purpose; boards and hands stay hidden until
  combat. [PCGamesN](https://www.pcgamesn.com/hearthstone/battlegrounds-heroes)
- Kriegspiel: a neutral umpire enforces legality and leaks only narrow signals (check, capture attempt).
  The precedent for a trusted server with deliberately structured leak channels.
  [Wikipedia](https://en.wikipedia.org/wiki/Kriegspiel_(chess))
- Pitfalls: unregulated information leak rates cause imbalance; players misremember and misread hidden
  information, so the client must never present incomplete state as certainty.
  [Ludology](https://ludology.co/asymmetric-information/), [BGDF thread](https://www.bgdf.com/forum/game-creation/mechanics/help-hidden-information)

## 5. Numbers

| Source | Pool per source | Picks per run |
|---|---|---|
| Hades 2 | ~14 per major god, ~6 minor (**unverified**) | 10-14 rooms (**unverified**) |
| TFT | augment pool total not found | 3 fixed picks |
| Monster Train | 42 per clan | one per node |
| Rounds | 5 offered per loss | one per lost round |

No designer-stated "healthy pool-to-picks ratio" was found. **Not found**, rather than inferred.

## What this suggests for Mimas (agent reading, not a decision)

- With best-of-3 there are at most two drafts plus one starting Blessing. A pool of 9-12 per lineage
  (three of each kind, covering every item kind) gives real variety across sessions without a
  rarity system.
- Rounds is the precedent for `q-draft-comeback`; TFT's "never three of one category" is the precedent
  for the "one of each kind" offer rule already proposed in `#draft`.
- Hades' one-boon-per-slot is the precedent for `q-boons-exclusive` (one element per crown).
