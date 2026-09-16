# JSON Schemas for `MimasClient/Assets/_Game/Data`

Editor-time validation for the hand-authored game data. These schemas mirror
`Mimas.Core`'s loaders (`ItemDef`, `AbilityDef`, `RulesDef`, …) so a typo shows up while you type
instead of as a `ContentLoadException` at boot.

They are **not** loaded by the game. The loader stays the authority: it is fail-closed, it links ids
across files and it reports every problem with its file name. A schema only catches the subset a single
file can know about on its own.

## How they are wired up

- Every data file starts with a `"$schema"` line pointing at the right schema by relative path, so any
  editor that honours it (VS Code, Rider, Visual Studio) validates the file with no extra setup.
- `.vscode/settings.json` also maps them by glob, which covers a new file whose `$schema` line has not
  been written yet.
- Core ignores the `$schema` key (no loader rejects unknown top-level keys, and
  `Schemas_AreIgnoredByTheLoader` in `ContentTests` locks that in).

## What each schema covers

| Schema | Files |
|---|---|
| `rules.schema.json` | `rules.json` |
| `terrains.schema.json` | `terrains.json` |
| `timecontrols.schema.json` | `timecontrols.json` |
| `item.schema.json` | `items/*.json` |
| `ability.schema.json` | `abilities/*.json` (movement and attack, chosen on `type`) |
| `modifier.schema.json` | `modifiers/*.json` |
| `map.schema.json` | `maps/*.json` (a hex's `prop` names a `props/*.json` id) |
| `prop.schema.json` | `props/*.json` |

Two things the schemas deliberately go **stricter** than the loader:

- `additionalProperties: false` everywhere, so a misspelled optional key (`"icons"`, `"minrange"`) is
  flagged. The loader would silently ignore it, because it only reads the keys it knows.
- The damage-lane enums list `weapon` and `spell` literally, and `ability.schema.json` requires an
  attack's `category` to equal its `attack.damageType`. The lane list really lives in `rules.json` and
  the engine is lane-agnostic (test fixtures still use `melee` / `ranged` / `magic`), so **update these
  enums when `rules.damageTypes` changes.**

The second point is deliberate belt-and-braces: the catalogue also enforces the slot-to-category rule at
link time (`item 'x' is a weapon but grants spell attack 'y'`) and a repo-data test checks
category-equals-lane across the shipped catalogue.
