# PreToolUse hook: block edits Unity must own. Exit 2 = block (message goes back to Claude), 0 = allow.
$in = [Console]::In.ReadToEnd() | ConvertFrom-Json
$p = $in.tool_input.file_path
if (-not $p) { exit 0 }
if ($p -match '\.meta$') {
  [Console]::Error.WriteLine("Blocked: Unity owns .meta files (GUIDs). Never create or edit them.")
  exit 2
}
if ($p -match '[\\/]MimasClient[\\/]ProjectSettings[\\/]') {
  [Console]::Error.WriteLine("Blocked: ProjectSettings changes need explicit approval from Rohan. Use `unity command set_project_settings` after asking.")
  exit 2
}
if ($p -match '\.(unity|prefab)$') {
  [Console]::Error.WriteLine("Blocked: do not hand-edit scenes/prefabs. Use the live Editor (unity status / unity command ...).")
  exit 2
}
exit 0
