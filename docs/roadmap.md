# Roadmap

| M | Goal | Done when | Status |
|---|---|---|---|
| M0 | Setup | Empty Web build loads in browser; `dotnet test` green; Unity CLI + Claude Code connected; server `/ws` echoes ping | in progress |
| M1 | Core loop, offline | Hex map from JSON, 1 unit each, move + basic attack, turn order, win by kill; playable vs a random bot in the Editor; Core has ≥ 50 tests | |
| M2 | Online | Same match over WebSocket via `Mimas.Server`; guest auth; matchmaking queue; chess clocks; reconnect | |
| M3 | Depth | 3 classes, abilities with hidden/reveal, modifiers, tile effects (pickups, terrain), boon draft, best-of-3 series across 3 maps | |
| M4 | Presentation | Cinemachine tilted/top-down toggle, UI Toolkit HUD + examine mode, Shuriken VFX, FMOD music/SFX, low-poly characters with animations | |
| M5 | Ship | Ratings (Glicko-2), deploy on VPS, size/load optimisation (Addressables, stripping), mobile browser check | |

## M1 progress (15 Sep 2026)

Done: movement rules (walk / jump / teleport resolvers, height, terrain cost tables, integer hex lines),
content catalogue (all JSON loaded once, linked, hashed; classes with ability ids), client board with
heights and move playback, server loads the same content. The `SkeletonMatchController` still fakes the
match loop; it is deleted once these exist, in this order:

1. `MatchState` + `Command`s (`MoveCommand`, end turn) + `MatchEvent`s + a movement executor that walks
   `MovePlan.EnteredTiles` and runs tile-effect `OnEnter` hooks (interrupt ends the move).
2. Client match session seam (local `MatchState` now, WebSocket later), event player, unit view registry.
3. Board interaction controller that emits commands through the session.
4. Turn structure (needs PIN-001), HUD (UI Toolkit) for ability selection, random bot via `Enumerate`.

## Baselines

| Metric | Value | Date |
|---|---|---|
| Empty URP Web build (Brotli) | 12.5 MB (wasm 7.9 + data 4.5 + framework.js 0.08 + loader/template 0.04) | 14 Sep 2026 |
| Load time (desktop Chrome, cold) | — s (measure after D3 deploy) | |
| Core test count / runtime | 24 / 0.07 s | 14 Sep 2026 |
| Core test count / runtime | 110 / 0.05 s (movement: walk / jump / teleport resolvers, integer hex lines) | 15 Sep 2026 |
| Core test count / runtime | 131 / 0.05 s (+ content catalogue, classes, hash parity) | 15 Sep 2026 |
