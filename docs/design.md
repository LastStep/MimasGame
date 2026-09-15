# Mimas — Game Design (v0.1, from Rohan's notebook, 14 Sep 2026)

> Source of truth for *what the game is*. Rules details go in `rules.md` once designed; data schemas in `data.md`.

## One-line pitch

A browser-based 1v1 turn-based tactics duel on hex maps — think a BG3 fight distilled into a competitive, chess-like ladder game with hidden information and TFT-style power drafting.

## Pillars

| # | Pillar | Notes from the design notebook |
|---|---|---|
| 1 | **PvP turn-based fighting** | 1v1, online matchmaking |
| 2 | **Strategy through positioning, character class, modifiers** | Depth comes from where you stand, what you are, and what you've picked up |
| 3 | **Ladder of maps** | Each map provides different challenges and effects. Player progresses through maps in a session |
| 4 | **Tile-based maps** (hex) | Controlled movement; tile effects: bonus pickups, terrain modifiers, etc. |
| 5 | **Game modes around time controls** | Like chess: bullet / blitz / rapid / classical equivalents |
| 6 | **Carry-forward character build** | Your build persists across maps within a session |
| 7 | **Boon system** | Between rounds, choose one power from offered options — like TFT augments |
| 8 | **Hidden information** | Enemy abilities/actions/effects are hidden until first used. Once revealed, the opponent has full information on that thing for the rest of the game. Adds surprise |
| 9 | **Browser-based, lean and responsive** | Fast load, no lag in UI |
| 10 | **Clean in-game UI for immersion** | Minimal HUD; depth via selectable "examine" controls: inspect opponent character, tile, structure, etc. |
| 11 | **Audio** | Nice music, not overbearing; VFX and SFX for feedback (FMOD) |
| 12 | **Movement variety** | Jumping, flying, teleport, etc. |
| 13 | **Camera** | 3D tilted view, with a toggle to full top-down (like pressing O in BG3) |
| 14 | **Map symmetry** | Maps are overall symmetrical; players spawn opposite each other at maximum distance, for easier initial strategising and balance |
| 15 | **No story** | Pure gameplay |

## Win conditions

| Level | Condition |
|---|---|
| Map round | Kill the opponent — by damage or by environment |
| Game (session) | Best of X map rounds |

## Open design questions

| Question | Options | Status |
|---|---|---|
| Alternating turns vs simultaneous turns | (a) alternating, chess-like; (b) simultaneous planning + resolution; (c) initiative-based | **Decided: alternating** (ADR-015). Per-turn cap from the time control; reaching it with no action = skip; End Turn passes early |
| Map shapes | Ring of polygons; game board of polygons | Hex chosen; shapes are data (any set of hex coordinates), so both are possible |
| How many units per player? | 1 hero; or hero + summons | **Decided: one hero**, model stays multi-unit ready (`UnitSet`, per-unit ids and AP) so summons can come via boons later |
| Action economy | Move + action per turn (BG3-style); action points | **Decided: action points** (ADR-016): 3 AP per turn, flat cost per ability use, unlimited reuse, no carry-over |
| Number of classes at launch | 3 recommended for M3 | Undecided |
| Damage model | flat; dice; hit chance | **Decided: deterministic** (ADR-017): base + power − defense + flat modifiers (stats, weapons, tiles, boons). The attacker sees a preview built from what they know; the server resolves the truth; any hidden modifier that changed the result is revealed |
| Enemy HP visibility | visible; bar only; hidden until hit | **Decided: fully visible** (both players see exact HP; surprise lives in abilities and modifiers) |
| Attack targeting | line of sight always / per ability / never | **Decided: always requires line of sight**, even melee; range is a `min..max` band per attack |
| Time controls | e.g. 1+0, 3+2, 10+0 per player, plus per-turn cap | Undecided |
| Rating system | Glicko-2 recommended | Undecided |

## Reference games

Baldur's Gate 3 (tactical feel, camera), chess (time controls, ladder), Teamfight Tactics (augment/boon drafting).
