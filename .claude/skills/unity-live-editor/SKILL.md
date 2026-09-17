---
name: unity-live-editor
description: Driving the Unity Editor. Use when the project's engine is unity and the work touches the client.
---

# Driving the Editor

The rule is **drive the Editor, do not edit its files** (`asset-safety.md`). This is how.

## Before anything

```bash
unity status --format json      # an instance must report state "ready"
unity command                   # list the live-editor commands this project exposes
```

If nothing is ready:

1. `unity pipeline list` — is it in **Safe Mode**? Then C# does not compile. Fix the compile errors in
   `.cs` directly (the one allowed exception) and say so in the run report.
2. Otherwise the Editor is not running or not reachable. Say so and stop — do not start hand-editing
   scenes because the Editor was inconvenient.

The Unity MCP server is an alternative front door for the same thing. When it is not connected, the
CLI is the fallback, not a reason to stop; when neither is reachable, that is a blocked task.

## The loop

1. **Stop Play Mode before editing C#.** `unity command editor_stop`.
2. New or changed files on disk are invisible to the Editor until it refreshes:
   `unity command menu --path "Assets/Refresh"`, then poll `recompile_status` until `completed`.
3. **Always check the console after touching Unity code**: `unity command console`. There is no separate
   log-fetch command; compile errors are silent otherwise.
4. Never run two Editor commands at once. On a timeout: wait 10s, retry **once**, then stop and ask.
5. After changing a scene or prefab through commands: `unity command save_scene`.

## Evidence

A claim about the client is worth nothing without a picture. Capture the game view through a
`[CliCommand]` and put the path in the run report:

```
artifacts/<task>-<what>.png
```

The verifier reads the screenshot. "It looks right" from the agent that made it is not evidence.

## Tests and builds

```bash
unity test MimasClient --mode EditMode --report-format junit --output artifacts/editmode.xml --timeout 600
unity build MimasClient --profile "Web Release" --output-path Build/Web
```

`unity test` exiting **8** means tests failed. That is a result, not a flake — do not retry it.

## Keep the Editor warm

A cold Editor start costs minutes on every rung. Leave one running with domain reload disabled and
**Application.runInBackground** on, so a ladder run is seconds. If the ladder is slow, that is usually
why — say so in the report rather than skipping rungs.
