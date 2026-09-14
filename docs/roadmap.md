# Roadmap

| M | Goal | Done when | Status |
|---|---|---|---|
| M0 | Setup | Empty Web build loads in browser; `dotnet test` green; Unity CLI + Claude Code connected; server `/ws` echoes ping | in progress |
| M1 | Core loop, offline | Hex map from JSON, 1 unit each, move + basic attack, turn order, win by kill; playable vs a random bot in the Editor; Core has ≥ 50 tests | |
| M2 | Online | Same match over WebSocket via `Mimas.Server`; guest auth; matchmaking queue; chess clocks; reconnect | |
| M3 | Depth | 3 classes, abilities with hidden/reveal, modifiers, tile effects (pickups, terrain), boon draft, best-of-3 series across 3 maps | |
| M4 | Presentation | Cinemachine tilted/top-down toggle, UI Toolkit HUD + examine mode, Shuriken VFX, FMOD music/SFX, low-poly characters with animations | |
| M5 | Ship | Ratings (Glicko-2), deploy on VPS, size/load optimisation (Addressables, stripping), mobile browser check | |

## Baselines

| Metric | Value | Date |
|---|---|---|
| Empty URP Web build (Brotli) | — MB | |
| Load time (desktop Chrome, cold) | — s | |
| Core test count / runtime | — / — s | |
