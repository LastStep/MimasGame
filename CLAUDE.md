# Mimas — guide for Claude Code

Mimas is a browser-based, 1v1 online, turn-based tactics game (hex tiles, 3D characters, hidden
information, ladder of maps, TFT-style boons).

**Where the truth lives:**

| Question | File |
|---|---|
| Where does the project stand? | `studio/STATE.md` — read this first, every session |
| What is the game for? | `studio/pillars.md` — Rohan's, and it outranks everything here |
| What are the rules? | `docs/design/index.html` — the design source of truth. Grep it by `id="…"` anchors; never read it whole |
| Why is it built this way? | `docs/decisions.md` (ADRs), `docs/architecture.md`, `docs/data.md` |
| What am I building? | your task in `studio/tasks/`, its one-pager in `studio/features/` |

## Repo layout

| Path | What | Toolchain |
|---|---|---|
| `shared/Mimas.Core/` | **Pure C# rules engine** (netstandard2.1, C# 9). No `UnityEngine`. Hex math, tiles, units, abilities, turn resolution, RNG, boons, JSON data. Also a Unity local package. | `dotnet`, and compiled by Unity |
| `shared/Mimas.Core.Tests/` | xUnit tests. **Run constantly.** | `dotnet test` (~1 s) |
| `server/Mimas.Server/` | ASP.NET Core WebSocket server: rooms by code, guest auth, clocks, bot seat, hidden-info filtering. Hosts Core. | `dotnet run` |
| `server/Mimas.Server.Tests/` | xUnit. Two fake clients play whole matches over real sockets. Ladder rung 5. | `dotnet test` (~6 s) |
| `MimasClient/` | Unity 6000.4.4f1 URP → **Web (WebGL2)**. Presentation only. | Unity CLI (`unity …`) |
| `studio/` | Production state: STATE, pillars, roadmap, features, tasks, plans, playtests, runs, ledger | markdown + yaml |
| `tools/` | Build/deploy scripts, JSON schemas, nginx config | — |

## Golden rules

1. **Never create, edit, move or delete `*.meta`.** Unity owns the GUIDs. Commit each asset with its `.meta`.
2. **Never hand-edit `.unity` / `.prefab` / `.asset`.** Drive the live Editor: `unity status` → `unity command <name>`. No Editor reachable? Say so and stop.
3. `shared/Mimas.Core` stays Unity-free: no `UnityEngine`, no threads, no `dynamic`, no reflection-emit (IL2CPP). C# 9 max — no `record`, no `required`, no file-scoped namespaces.
4. **Determinism.** All randomness through `Mimas.Core.Rng`, seeded per match. No `DateTime.Now`, no `Guid.NewGuid()`, no dictionary-order dependence, no floats in rules. Same inputs + seed ⇒ same result on server and client.
5. **Game data is JSON** under `MimasClient/Assets/_Game/Data/`. Never hard-code balance numbers in C#. Schema change ⇒ update `docs/data.md` **and** `tools/schemas/`.
6. **Hidden information lives on the server.** A client only ever receives a `PlayerView`. Never send full `MatchState`. (The client's `MatchState` is a *mirror* built from a `PlayerView` — ADR-026 — which answers questions and refuses to be advanced.)
7. Do not touch `ProjectSettings/**`, `Packages/manifest.json` or `packages-lock.json` without asking first.
8. Renamed serialized fields get `[FormerlySerializedAs("_old")]`. Compare `UnityEngine.Object` with `== null`, never `is null` / `?.`.
9. Web build: WebGL2 only — no compute shaders, so **no VFX Graph** (use Shuriken), no managed threads, no sync GPU readback, Brotli, Managed Stripping High (keep `link.xml` current).
10. Never commit `Library/`, `Temp/`, `obj/`, `bin/`, `Logs/`, `Build*/`, `UserSettings/`.
11. **Design first, no drift.** Build nothing that is not in `docs/design/index.html` as `decided` or `proposed`. Cite the anchor before implementing and use its vocabulary (Boon = Blessing / Enchant / Sigil; lanes = weapon / spell; gear = weapon / crown / boots / armour; no classes). If the design is silent, add a `proposed` section or an open question there and **ask** — never invent a rule in code. When code and design disagree, set `data-impl="drift"` with a `.drift-note`, or fix the code.

## Commands

```bash
# Fast loop (no Unity) — before and after every Core/Server change
dotnet build Mimas.slnx
dotnet test shared/Mimas.Core.Tests              # exit 0 = green
dotnet test server/Mimas.Server.Tests            # the online game, end to end over sockets
dotnet run --project server/Mimas.Server         # http://localhost:7777/health, ws://localhost:7777/ws
MIMAS_WEB_PATH=Build/Web dotnet run --project server/Mimas.Server   # also serves the Web build at /

# The ladder — what turns "it works" into a fact
node E:/Studios/Trinetra-Game-Studio/tools/ladder/ladder.mjs --project mimas --task T-NNNN

# Unity (needs `unity` on PATH, `unity auth login` done once)
unity status --format json                       # state must be "ready"
unity command console                            # ALWAYS after touching Unity code
unity test MimasClient --mode EditMode --report-format junit --output artifacts/editmode.xml --timeout 600
unity build MimasClient --profile "Web Release" --output-path Build/Web
```

`unity test` exit **8** = tests failed. That is a result, not a flake — do not retry.

## Conventions

- Namespaces `Mimas.Core.<Area>` etc., braces style (C# 9). `_camelCase` private fields;
  `[SerializeField] private` over public; cache `GetComponent` in `Awake`; unsubscribe in `OnDestroy`.
- Tests: one behaviour each, named `Method_Scenario_Expected`. New rule ⇒ new test first.
- Assembly definitions per folder under `MimasClient/Assets/_Game/**` (`autoReferenced: false`).
- Commits: `area: what` (e.g. `core: add ring() to Hex`). Small commits.
- Prefer `https://docs.unity3d.com/6000.4/…`. Use the `/unity:*` skills when relevant.

<!-- trinetra:begin -->
## Studio protocol

This project runs inside the Trinetra studio. Before anything else, follow the session start
checklist; it tells you what to read and in what order, and points at the rest of the protocols in
`.claude/protocols/`.

@.claude/protocols/session-start.md

State lives in `studio/` — `STATE.md` first, then your task. Nothing is done because an agent says
so: the ladder must be green and a verifier in a fresh context must agree.
<!-- trinetra:end -->
