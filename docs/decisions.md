# Decisions (ADR log)

One entry per architectural decision. Newest at the bottom. Claude Code: append here when you make a choice that future-you would otherwise re-litigate.

| ID | Date | Decision | Why | Alternatives rejected |
|---|---|---|---|---|
| ADR-001 | 2026-09-14 | **Unity 6000.4.4f1, URP, Web (WebGL2) target** | Installed and has the Web module; URP is the only pipeline that runs well in browsers | HDRP (no web); Unity 6.6 for production WebGPU (revisit later — 6000.4 is a frozen tech-stream release; 6000.3 is LTS) |
| ADR-002 | 2026-09-14 | **Custom ASP.NET Core WebSocket server + shared C# `Mimas.Core` rules library** | Hidden information requires a server-authoritative model; self-hosting on own VPS preferred; rules unit-testable with `dotnet test` outside Unity; one process hosts many matches | Photon (no indie self-host; Quantum leaks state), Nakama/Colyseus (rules in Go/TS), NGO/Mirror/FishNet (headless Unity per match), SpacetimeDB (RLS experimental, BSL) |
| ADR-003 | 2026-09-14 | **Hex tiles** (axial storage, cube math) | Natural rings/symmetry, 6 equal neighbours, Red Blob reference | Square grid (kept possible: tile graph is shape-agnostic) |
| ADR-004 | 2026-09-14 | **All game data in JSON** (classes, abilities, boons, maps, modifiers) | Easy to change, diffable, deterministic loading, AI can author it | ScriptableObjects (binary-ish YAML, Unity-only) |
| ADR-005 | 2026-09-14 | **Git** with LFS for binaries, UnityYAMLMerge for scenes/prefabs | Works with Claude Code and the Unity CLI (`unity vcs …`) | Plastic SCM (used in the old prototype) |
| ADR-006 | 2026-09-14 | **FMOD for Unity 2.03** for audio | Rohan already uses FMOD; supports Unity Web; indie licence free under revenue threshold | Unity built-in audio (simpler on web; fallback if FMOD web proves painful) |
| ADR-007 | 2026-09-14 | **Unity CLI + `com.unity.pipeline` (+ its MCP) as the AI ↔ Editor bridge** | Unity deprecated the AI-Assistant MCP in favour of the CLI; free; exposes ~150 editor commands; `unity test`/`unity build` headless | AI Assistant package MCP (deprecated, needs Unity AI subscription); CoplayDev/unity-mcp kept as optional supplement |
| ADR-008 | 2026-09-14 | **WebGL2 only; no VFX Graph; no native threads; Brotli; no decompression fallback** | WebGPU experimental in 6.4; VFX Graph needs compute; threads need COOP/COEP; we control nginx | — |
| ADR-009 | 2026-09-14 | **Newtonsoft JSON** (`com.unity.nuget.newtonsoft-json` 3.2.2) in Core/client | Only AOT-safe JSON Unity supports; `System.Text.Json` unsupported in Unity | System.Text.Json (server-only is fine), MemoryPack (later, if size matters) |
| ADR-010 | 2026-09-14 | **Commands in, Events out**; server sends `PlayerView` projections, never full state | Hidden info by construction; client animates events | Full-state sync |
| ADR-011 | 2026-09-14 | **Core targets netstandard2.1 / C# 9, no records** | Unity 6 compiler limits; IL2CPP-safe | — |
| PIN-001 | 2026-09-14 | Alternating vs simultaneous turns | **Pinned by Rohan** — decide before M1 rules design | — |
