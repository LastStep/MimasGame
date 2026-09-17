---
name: asset-safety
description: Asset safety (Unity projects). Use when the project's engine is unity, or the diff touches MimasClient/.
---

# Asset safety

Unity owns a set of files. An agent that edits them corrupts the project in ways that do not show up
until someone opens the Editor, and that are painful to undo. This is rung 0 of the ladder and it is
enforced by a hook, not by good intentions.

## Never edit, create, move or delete

| Pattern | Why | Do this instead |
|---|---|---|
| `*.meta` | Unity owns the GUIDs. A hand-made `.meta` silently breaks every reference to that asset | Let Unity generate it. Commit each asset **together with** its `.meta` |
| `*.unity`, `*.prefab` | YAML with file-ID cross references. Hand edits look fine and break on load | Drive the live Editor: `unity status` -> `unity command <name>` |
| `*.asset` | Serialized ScriptableObjects, same problem | `unity command` or a `[CliCommand]` you add first |
| `ProjectSettings/**` | Global, irreversible, affects every build | Ask Rohan. This is the director lane |
| `Packages/manifest.json`, `packages-lock.json` | Dependency resolution; a wrong edit costs a reimport | Ask, then use the Package Manager API |
| `Library/`, `Temp/`, `obj/`, `bin/`, `Logs/`, `Build*/`, `UserSettings/` | Generated. Never read, never commit | — |

## The one exception

**Safe Mode.** If the Editor will not start because C# does not compile, no live-editor command can
reach it — there is no Editor to reach. Check with `unity pipeline list`. In Safe Mode you may edit
`.cs` files directly to fix the compile errors, and only that. Say in the run report that you did.

## Rung 0

```
git diff --name-only   ->   no protected path, unless the task's frontmatter allows it:
                            allows_assets: [MimasClient/Assets/_Game/Data/**]
```

A task that genuinely needs to touch an asset declares it in frontmatter and explains why in the plan.
That is a lane-full decision, so Rohan has already seen it.

## If a hook blocks you

The hook is right and you are wrong. Do not look for a path around it — do not shell out to `sed`, do
not write via a script, do not disable the hook. Either use the live Editor, or mark the task blocked
and explain what you needed to change. Circumventing a guard is the reward-hacking failure in
`reward-hacking-guards.md`, and it is treated as a failed session regardless of the result.
