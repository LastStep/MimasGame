# Claude Code config

Copy this folder's contents into `<repo>/.claude/` (the remote bridge cannot write into `.claude/` directly):

    mkdir .claude\hooks
    copy tools\claude-config\settings.json .claude\settings.json
    copy tools\claude-config\hooks\guard-unity-files.ps1 .claude\hooks\guard-unity-files.ps1

`settings.json` = allow-list for dotnet/unity/git commands, deny-list for `.meta` / ProjectSettings edits, and a PreToolUse hook that blocks hand-edits to `.meta`, `.unity`, `.prefab` and ProjectSettings. Requires `pwsh` (PowerShell 7) on PATH; if you only have Windows PowerShell 5, change `pwsh` to `powershell` in settings.json.
