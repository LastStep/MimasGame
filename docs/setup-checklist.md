# Setup checklist (M0)

Legend: **[R]** Rohan does it by hand · **[CC]** Claude Code does it · **[✓]** done.

_Status 14 Sep 2026: A1–A2, A4, B1–B4 done. Next: A3 (git), C1–C7, B5–B8, D1–D3._

## A. Repo

| # | Step | Who |
|---|---|---|
| A1 | Repo scaffold: `CLAUDE.md`, `docs/`, `shared/`, `server/`, `.gitignore`, `.gitattributes`, `.editorconfig`, `.claude/` | [✓] |
| A2 | Repo folder is `E:\Unity Projects\MimasGame`; GitHub remote https://github.com/LastStep/MimasGame.git | [✓] |
| A3 | `git init` → `git lfs install` → `git remote add origin https://github.com/LastStep/MimasGame.git` → `git add -A` → `git commit -m "chore: scaffold"` → `git push -u origin main`. Also copy `tools\claude-config\*` into `.claude\` first so it is in the first commit | [R] |
| A4 | .NET SDK 10.0.203 present; `dotnet test shared/Mimas.Core.Tests` → 24/24 green | [✓] |

## B. Unity project

| # | Step | Who |
|---|---|---|
| B1 | Unity project `MimasClient` created from Universal 3D, 6000.4.4f1 | [✓] |
| B2 | Opened once | [✓] |
| B3 | `manifest.json`: added com.mimas.core (local), Cinemachine 3.1.7, Addressables 2.9.1, Newtonsoft 3.2.2, NativeWebSocket (git); removed visualscripting, timeline, collab-proxy, multiplayer.center, ai.navigation, ide.rider (web build size). Unity resolves on next open | [✓] |
| B4 | `Assets/link.xml`, `Assets/_Game/**` tree with 6 asmdefs, `Editor/WebBuild.cs`, EditMode smoke test, starter data (`Data/maps/board-3.json`, `ring-3.json`, `timecontrols.json`). Open Unity → check Console is clean → run Window > General > Test Runner (EditMode) → 2 tests green | [✓] then [R] verify |
| B5 | Force Text serialization verified (`m_SerializationMode: 2`); visible meta files present | [✓] |
| B6 | Switch platform to **Web**. Player Settings per `hosting.md` + `research/unity-web-build.md` §2: already default: Brotli, Decompression Fallback off, Data Caching on, threads off. Still to set: **Name Files As Hashes on, Run In Background on, Managed Stripping High, IL2CPP "Faster (smaller) builds", Exceptions None, Initial Memory 128 MB** | [R] (or [CC] via `unity command set_project_settings` once CLI is connected) |
| B7 | Create a Build Profile "Web Release" | [R] |
| B8 | Import FMOD for Unity 2.03 from the Asset Store; link your FMOD Studio project (`E:\FMOD Projects\…`); set banks to load in a loading scene | [R] |

## C. AI tooling

| # | Step | Who |
|---|---|---|
| C1 | Install Unity CLI (beta): PowerShell `$env:UNITY_CLI_CHANNEL='beta'; irm https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1 \| iex` (or `winget install Unity.CLI`). New shell → `unity --version` | [R] |
| C2 | `unity auth login` | [R] |
| C3 | `com.unity.pipeline` 0.7.0-exp.1 is already in the manifest (template included it) | [✓] |
| C4 | Install the Unity Claude Code plugin: `claude plugin marketplace add Unity-Technologies/unity-agent-plugin` → `claude plugin install unity@unity-agent-plugin` | [R] |
| C5 | `unity mcp configure claude-code --project-path MimasClient --yes` | [R] |
| C6 | Open the project in the Editor, then in Claude Code: `unity status --format json` → must show state `ready`. Try `unity command get_console_logs` | [R] |
| C7 | (Optional) `unity vcs merge-setup` for UnityYAMLMerge; `unity vcs hooks install` | [R] |
| C8 | (Optional) CoplayDev/unity-mcp as supplement (Roslyn script validation, UI Toolkit tools) | later |

## D. First build baseline

| # | Step | Who |
|---|---|---|
| D1 | `unity build MimasClient --profile "Web Release" --output-path Build/Web` | [CC] |
| D2 | Record compressed size (expect ≈ 9 MB for an empty URP scene) and load time in `docs/roadmap.md` | [CC] |
| D3 | Deploy to VPS with `tools/deploy/nginx-mimas.conf`; verify `wss://…/ws` echoes `ping`→`pong` from the browser | [R] |
