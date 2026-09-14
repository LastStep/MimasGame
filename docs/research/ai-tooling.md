# Research: AI-assisted tooling for Unity 6 + Claude Code (Windows)

_Researched 14 Sep 2026._

## Headline

1. **Unity's official Claude Code plugin is skills-only** (`Unity-Technologies/unity-agent-plugin`, v0.1.2-beta, 31 skills as of 11 Sep 2026). No MCP server or CLI binary inside it; its `unity-cli` skill tells the agent to install the Unity CLI if missing.
2. **The in-Editor MCP in `com.unity.ai.assistant` is deprecated.** Unity docs (2.18): "Unity MCP server is deprecated. Use the Unity command-line interface (CLI) instead." It also required a Unity AI subscription (Personal: 14-day trial then $10/mo). The CLI path is free.
3. **Use: Unity plugin (skills) + Unity CLI (`unity`) + `com.unity.pipeline` in the project + `unity mcp configure claude-code`.** Keep CoplayDev/unity-mcp as an optional supplement (Roslyn `validate_script`, `manage_ui`, `unity_docs`).

## Unity Claude Code plugin

```
claude plugin marketplace add Unity-Technologies/unity-agent-plugin
claude plugin install unity@unity-agent-plugin
```
(or `/plugin marketplace add …` / `/plugin install …` inside a session). Verify with `/unity:` in the slash menu.

Skills (31): 2d-pixel-perfect, audio-setup-mixers, build-live-game, generate-editor-search-query, implement-in-app-purchases, initialize-ai-navigation, levelplay-unity-integration, localization, manage-sprite-atlas, migrate-birp-to-urp, new-unity-project, optimize-audio, **optimize-web**, optimize-text-mesh-pro, physics-3d-collision, setup-multiplayer-services, setup-vivox-voice-chat, shader-graph-create-custom-node, sprite-editor, sprite-segment-3x3grid, tilemap-*, **ui**, ui-imgui, ui-ugui, **ui-uitk**, **unity-cli**, unity-package-management, **urp-postprocessing**, validate-urp-render-graph-renderer-feature.

Guidance baked into the `unity-cli` skill (mirrored in our CLAUDE.md): run `unity status` before touching scenes/prefabs; never hand-edit `.unity`/`.prefab`/`.asset` while an Editor is reachable; compile errors → Safe Mode → Pipeline package doesn't load.

## Unity CLI (`unity`)

Native binary, free, beta (1.0.0-beta.9, 8 Sep 2026). Docs: https://docs.unity.com/en-us/unity-cli

Install (Windows PowerShell):
```powershell
$env:UNITY_CLI_CHANNEL='beta'; irm https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1 | iex
# or: winget install Unity.CLI
```

Key commands:
```
unity auth login
unity open <project>
unity pipeline install --project-path <project>     # adds com.unity.pipeline
unity status --format json                           # connected Editors ("ready")
unity command                                        # list live-editor commands (~150)
unity command get_console_logs | editor_play | editor_stop | eval 'return Application.unityVersion;'
unity test <project> --mode EditMode --report-format junit --output artifacts/editmode.xml --timeout 600
unity test <project> --affected --since main
unity run  <project> -- -executeMethod Builder.Build -logFile build.log
unity build <project> --profile "Web Release" --output-path Build/Web
unity projects verify <project>                      # META_MISSING / GUID_DUPLICATE / CONFLICT_MARKERS
unity vcs merge-setup ; unity vcs hooks install
unity mcp configure claude-code --project-path <project> --yes
```
Exit codes: 0 ok · 2 bad args · 3 auth · 4 precondition · 6 command failure · **8 = tests ran and failed**.

`com.unity.pipeline` (0.4.0-exp.1, Unity 6.0+): local HTTP server inside the Editor, ~200–600 ms per call, no domain reload. Commands cover scenes, GameObjects/components, prefabs, scripts, materials, lighting/navmesh baking, capture, build/compile/tests, project settings, package manager, editor lifecycle, `eval`, input simulation. Custom commands via `[CliCommand]`.

Gotchas: Safe Mode hides the Editor; modal dialogs can block agents; a restricted sandbox account can't read the Editor's discovery file; Editor sometimes needs foreground focus for asset refresh.

## Community MCP servers

| | CoplayDev/unity-mcp | IvanMurzak/Unity-MCP | Official CLI + Pipeline |
|---|---|---|---|
| Version | v10 (Jun 2026), active | 0.90.0, active | CLI beta.9, pipeline 0.4.0-exp.1 |
| Install | `openupm add com.coplaydev.unity-mcp` → Window → MCP for Unity → Configure | `openupm add com.ivanmurzak.unity.mcp`; project path must not contain spaces; cloud-pinned by default | `unity pipeline install`; `unity mcp configure claude-code` |
| Deps | Python 3.10 + uv | .NET server binary, Node for CLI | none |
| Tools | 47: scene/GO/prefab/asset/material/packages/build, read_console, validate_script (Roslyn), run_tests, UI Toolkit, VFX, profiling, docs | 70+: CRUD, screenshots, console, playmode, Roslyn `script-execute`, reflection, tests, profiler | ~150 pipeline commands |
| Licence | MIT | Apache-2.0 | Unity Companion |

No neutral 2026 benchmark found; official CLI first (it is what the official skills are written against), Coplay as supplement.

## CLAUDE.md best practices (synthesised)

Never edit `.meta`; never hand-edit scenes/prefabs (drive the Editor); asmdefs with `autoReferenced:false`; `[FormerlySerializedAs]`; `== null` for UnityEngine.Object; stop Play Mode before editing C#; ProjectSettings changes need human approval; run tests via CLI/batchmode, never two Editor tools in parallel; ignore `Library/ Temp/ obj/ Logs/ Build*/ UserSettings/`; put coding rules in `.editorconfig`.

## Claude Code hooks

Verified schema: events `PreToolUse`/`PostToolUse`/`Stop`; `matcher` like `Edit|Write`; hook `type: "command"`; stdin JSON with `tool_input.file_path`; **exit 2 blocks**; `${CLAUDE_PROJECT_DIR}` available. Our `.claude/settings.json` + `.claude/hooks/guard-unity-files.ps1` implement the `.meta` / ProjectSettings / scene guard. Optional: `csharp-lsp@claude-plugins-official` plugin for C# navigation.

Raw batchmode test line (if the CLI is unavailable):
```
"C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Unity.exe" -batchmode -nographics -projectPath MimasClient -runTests -testPlatform EditMode -testResults artifacts\editmode.xml -logFile artifacts\editmode.log
```
(no `-quit` with `-runTests`; don't run batchmode while the GUI Editor has the project open.)

## Git

- `.gitignore`: canonical github/gitignore `Unity.gitignore` (root-anchored → prefixed with `MimasClient/` in our root `.gitignore`).
- Force Text serialization + Visible Meta Files (Unity 6 defaults).
- UnityYAMLMerge: `unity vcs merge-setup` or manual `git config merge.unityyamlmerge.driver "'C:/Program Files/Unity/Hub/Editor/6000.4.4f1/Editor/Data/Tools/UnityYAMLMerge.exe' merge --fallback none -h -p --force %O %B %A %A"`.
- LFS extension list in `.gitattributes` is a community-standard draft, not an official Unity list.

## Sources

- https://github.com/Unity-Technologies/unity-agent-plugin · https://unity.com/blog/unity-plugin-for-claude-code · https://unity.com/blog/meet-the-unity-cli
- https://docs.unity.com/en-us/unity-cli · https://docs.unity.com/en-us/unity-cli/use-unity-cli · https://docs.unity.com/en-us/unity-cli/replace-mcp-server-unity-cli · https://docs.unity.com/en-us/unity-cli/release-notes
- https://docs.unity.com/en-us/unity-production-pipeline/local-tools-cli/unity-pipeline-package · https://docs.unity3d.com/Packages/com.unity.pipeline@0.4/manual/index.html
- https://unity.com/blog/unity-ai-mcp-how-to-get-started · https://docs.unity3d.com/Packages/com.unity.ai.assistant@2.18/manual/integration/unity-mcp-get-started.html · https://unity.com/resources/what-is-unity-ai
- https://github.com/CoplayDev/unity-mcp · https://github.com/IvanMurzak/Unity-MCP · https://vindler.solutions/blog/unity-cli-agent-automation · https://coplay.dev/blog/coplay-vs-coplay-mcp-vs-unity-mcp
- https://github.com/nowsprinting/claude-code-settings-for-unity · https://github.com/zaffre001/unity-claude-template · https://github.com/XeldarAlz/everything-claude-unity
- https://code.claude.com/docs/en/hooks · https://docs.unity3d.com/Packages/com.unity.test-framework@1.5/manual/reference-command-line.html · https://docs.unity3d.com/6000.4/Documentation/Manual/EditorCommandLineArguments.html
- https://raw.githubusercontent.com/github/gitignore/main/Unity.gitignore · https://docs.unity3d.com/6000.4/Documentation/Manual/SmartMerge.html · https://discussions.unity.com/t/configuring-unityyamlmerge-for-git-the-correct-instructions/1661546
