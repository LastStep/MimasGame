# Mimas — guide for Claude Code

Mimas is a browser-based, 1v1 online, turn-based tactics game (hex tiles, 3D characters, hidden information, ladder of maps, TFT-style boons). Read `docs/design.md` for the vision and `docs/decisions.md` before proposing architecture changes.

## Repo layout

| Path | What | Toolchain |
|---|---|---|
| `shared/Mimas.Core/` | **Pure C# rules engine** (netstandard2.1, C# 9). No `UnityEngine`. Hex math, tile graph, units, abilities, turn resolution, RNG, boons, JSON data loading. Also a Unity *local package* (`package.json` + `.asmdef`). | Compiled by Unity **and** by `shared/Mimas.Core.Build/Mimas.Core.csproj` |
| `shared/Mimas.Core.Tests/` | xUnit tests for Core. **Run these constantly.** | `dotnet test` (~1 s) |
| `server/Mimas.Server/` | ASP.NET Core WebSocket server: matchmaking queue, rooms, clocks, hidden-info filtering, ratings. Hosts Core. | `dotnet run` |
| `MimasClient/` | Unity 6000.4.4f1 URP project → **Web (WebGL2)** build. Presentation only: rendering, input, UI Toolkit, FMOD audio, Cinemachine. | Unity CLI (`unity …`) |
| `docs/` | Design, architecture, protocol, hosting, decisions (ADRs), roadmap, research. Source of truth. | — |
| `tools/` | Build/deploy scripts, Unity templates, nginx config. | — |

## Golden rules

1. **NEVER create, edit, move or delete `*.meta` files.** Unity owns them. Commit every asset together with its `.meta`.
2. **Never hand-edit `.unity` / `.prefab`.** Drive the live Editor instead: `unity status` → `unity command <name>` (or the Unity MCP tools). If no Editor is reachable, say so and ask.
3. `shared/Mimas.Core` must stay Unity-free: no `UnityEngine`, no `Debug.Log`, no `System.Threading` timers, no `dynamic`, no reflection-emit (IL2CPP). C# 9 max (Unity 6 limit): no `record`, no `required`, no file-scoped namespaces.
4. **Determinism:** all randomness in Core goes through `Mimas.Core.Rng` seeded per match. No `DateTime.Now`, no `Guid.NewGuid()`, no `Dictionary` iteration order dependence, no floats in rules (use `int`; fixed-point if needed). Same inputs + seed ⇒ same result on server and client.
5. **Game data is JSON** under `MimasClient/Assets/_Game/Data/` (classes, abilities, boons, maps, modifiers). Core loads it; never hard-code balance numbers in C#. Schema changes ⇒ update `docs/data.md`.
6. **Hidden information lives on the server.** Client only ever receives a `PlayerView` projection. Never send full `MatchState` to a client.
7. Do not modify `MimasClient/ProjectSettings/**`, `Packages/manifest.json`, or `packages-lock.json` without saying so explicitly first.
8. Renamed serialized fields get `[FormerlySerializedAs("_old")]`. Compare `UnityEngine.Object` with `== null` (never `is null` / `?.`).
9. Web build constraints: WebGL2 only (no compute shaders → **no VFX Graph**, use Shuriken particles), no managed threads, no synchronous GPU readback, Brotli compression, Managed Stripping High (keep `link.xml` updated when adding reflection-based types).
10. Never commit `Library/`, `Temp/`, `obj/`, `bin/`, `Logs/`, `Build*/`, `UserSettings/`.

## Commands

```bash
# Fast loop (no Unity) — run before and after every Core/Server change
dotnet build Mimas.sln
dotnet test shared/Mimas.Core.Tests            # exit 0 = green
dotnet run --project server/Mimas.Server       # http://localhost:7777/health, ws://localhost:7777/ws

# Unity (needs `unity` CLI on PATH, `unity auth login` done once)
unity status --format json                     # is an Editor connected? state must be "ready"
unity command                                  # list live-editor commands
unity command get_console_logs                 # ALWAYS check after touching Unity code
unity command editor_play / editor_stop
unity test MimasClient --mode EditMode --report-format junit --output artifacts/editmode.xml --timeout 600
unity build MimasClient --profile "Web Release" --output-path Build/Web   # exit 8 from `unity test` = tests failed, do not retry
```

## Live-Editor workflow

1. `unity status` — if no instance is `ready`, check Safe Mode (`unity pipeline list`); fix compile errors first, then continue.
2. Stop Play Mode before editing C#.
3. Never run two Editor commands concurrently. On timeout: wait 10 s, retry once, then ask.
4. After scene/prefab changes via commands, `unity command save_scene`.

## Conventions

- Namespaces: `Mimas.Core.<Area>`, `Mimas.Server.<Area>`, `Mimas.Client.<Area>`. Braces-style namespaces (C# 9).
- `_camelCase` private fields; `[SerializeField] private` over public; cache `GetComponent` in `Awake`; unsubscribe in `OnDestroy`.
- Tests: one behaviour per test, name `Method_Scenario_Expected`. New rule ⇒ new test first.
- Assembly definitions per folder in `MimasClient/Assets/_Game/**` (`autoReferenced: false`, explicit refs).
- Docs: prefer `https://docs.unity3d.com/6000.4/…`. Use `/unity:*` skills (ui-uitk, optimize-web, urp-postprocessing…) when relevant.
- Commit messages: `area: what` (e.g. `core: add ring() to Hex`). Small commits.

## Workflow for a feature

1. Read the relevant `docs/*.md`. If the design is unclear, ask — do not invent rules.
2. Write/adjust JSON data + `docs/data.md` if data changes.
3. Implement in Core with tests → `dotnet test` green.
4. Server: wire message handlers; integration test with two fake clients.
5. Client: presentation only, via Unity CLI; check `get_console_logs`.
6. Update `docs/decisions.md` if you made an architectural choice.
