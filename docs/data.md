# Game data (JSON)

All balance/content data lives in `MimasClient/Assets/_Game/Data/**/*.json` and is loaded by `Mimas.Core.Data`. The server loads the same folder (path configurable). Numbers are integers (no floats in rules).

| File | Contents |
|---|---|
| `classes/*.json` | One class per file: id, base stats, movement type, starting abilities |
| `abilities/*.json` | id, targeting (range, shape, LOS), cost, effects[], `hidden: true/false` |
| `modifiers/*.json` | Status effects / passives: duration, stat deltas, triggers |
| `boons/*.json` | Boon offers: tier, effects, exclusivity tags |
| `maps/*.json` | Map: name, hexes[] (`q,r,terrain,height,effect?`), spawns (`p1`,`p2`), symmetry type, ladder position |
| `timecontrols.json` | `[ { "id": "3+2", "baseMs": 180000, "incrementMs": 2000, "turnCapMs": 60000 } ]` |

## Conventions

- `id`: lowercase kebab-case, globally unique within its folder (`"fire-bolt"`).
- Every definition has `"version": 1`; bump on breaking schema change and update the loader.
- Effects are a small expression list, e.g. `{ "type": "damage", "amount": 30, "element": "fire" }` — Core has one handler per `type`. Add new types in Core + document here.
- Maps must be symmetrical: the loader validates the declared symmetry (`"symmetry": "rotational-180"` / `"mirror-q"`) and that `p1`/`p2` spawns are at maximal hex distance.

## Example map (ring of radius 3 with a hole in the middle)

```json
{
  "version": 1,
  "id": "ring-3",
  "name": "The Ring",
  "symmetry": "rotational-180",
  "spawns": { "p1": { "q": -3, "r": 0 }, "p2": { "q": 3, "r": 0 } },
  "hexes": [
    { "q": -3, "r": 0, "terrain": "stone" },
    { "q": 3, "r": 0, "terrain": "stone" }
  ]
}
```

(Generate full rings with `Hex.Ring(center, radius)` in an editor tool rather than typing hexes by hand.)
