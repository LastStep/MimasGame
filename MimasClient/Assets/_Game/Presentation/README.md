# Presentation — `Mimas.Client.Presentation`

**Charter.** Everything the player sees and clicks on the board, and nothing else. This assembly renders
Core's data, turns pointer input into board vocabulary, and animates the result. It owns no rules, no
balance numbers and no network state. If a line of code here could change the outcome of a match, it is
in the wrong assembly.

The one place hex coordinates and world space meet is `HexLayout` / `BoardView`. Everything above them
talks in `Mimas.Core.Geometry.Hex`; everything below talks in `Vector3`.

## Contents

| File | Type | Charter |
|---|---|---|
| `HexLayout.cs` | `static HexLayout` | Pointy-top axial ⇄ world maths on the XZ plane. Pure, allocation-free, size passed in. |
| `Board/HexMeshFactory.cs` | `static HexMeshFactory` | Builds a flat-shaded pointy-top hex prism mesh (top fan + side band, no bottom). Caches nothing. |
| `Board/TileHighlight.cs` | `enum TileHighlight` | `None / Reachable / PathPreview / Hovered`, ordered by painting precedence. |
| `Board/TerrainVisual.cs` | `struct TerrainVisual` | Serialized colour + prism height per terrain id. Presentation-only; no rules data. |
| `Board/TileView.cs` | `MonoBehaviour` | One tile: its `Hex`, and how it paints itself per `TileHighlight` via `MaterialPropertyBlock`. |
| `Board/BoardView.cs` | `MonoBehaviour` | Presentation owner of the board: loads JSON via Core, builds the `TileMap`, generates tile GameObjects, exposes hex⇄world and highlighting. |
| `Units/IUnitMover.cs` | `interface` | Seam for time-bounded path playback. Keeps curve maths out of the mover. |
| `Units/UnitMover.cs` | `MonoBehaviour` | Coroutine playback of a `Func<float,Vector3>` path with easing and yaw slerp. |
| `Units/UnitView.cs` | `MonoBehaviour` | One unit: `CurrentHex`, tint, and a code-built placeholder visual that a real model replaces. |
| `Input/BoardInputController.cs` | `MonoBehaviour` | Cursor raycast → `TileView`; publishes `TileClicked` / `TileHovered` / `RightClicked`. No state. |
| `Match/SkeletonMatchController.cs` | `MonoBehaviour` | **Throwaway** scaffolding: select → preview → walk, to prove the seams. Deleted when the networked match controller lands. |
| `Cameras/ICameraView.cs` | `interface` | Activate / deactivate / retarget contract, priorities as explicit ints. |
| `Cameras/CinemachineCameraView.cs` | `MonoBehaviour` | Wraps a `CinemachineCamera` behind `ICameraView`. Handles the CM3 struct traps. |
| `Cameras/CameraDirector.cs` | `MonoBehaviour` | Owns which view is live. Priority swap, never enable/disable. |

## Dependency rules

- **May reference:** `Mimas.Core`, `Mimas.Client.Content` (the catalogue bootstrap), `Unity.Cinemachine`, `Unity.InputSystem`, `UnityEngine`.
- **Never references JSON files directly.** Content comes from `ContentBootstrap.EnsureLoaded()`; ids (`_mapId`, class ids) are the only data-shaped serialized fields allowed.
- **Must not reference:** `Mimas.Client.Net`, `Mimas.Client.UI`, `Mimas.Client.Audio`, any server code.
  Those depend on Presentation, never the reverse.
- **Must not contain:** balance numbers, rules, RNG, or anything that reads/writes authoritative state.
  All game data comes from JSON under `Assets/_Game/Data/` and is parsed by Core.
- **Namespace:** `Mimas.Client.Presentation` for every file. Subfolders are organisational only — they do
  **not** get their own asmdef and do **not** add namespace segments (which also keeps the `Input/` folder
  from colliding with `UnityEngine.Input`).
- **Determinism is not our problem, and must not become our problem.** Presentation may use floats,
  `Time.deltaTime` and dictionary iteration freely; it must never feed any of that back into Core.

## Conventions

- `_camelCase` private fields, `[SerializeField] private` over public, C# 9 max.
- Compare `UnityEngine.Object` with `== null`, never `is null` / `?.`.
- Cache `GetComponent` in `Awake`; subscribe in `OnEnable`, unsubscribe in `OnDisable`.
- Colour is pushed through `MaterialPropertyBlock` on URP Lit's `_BaseColor` so all tiles share one material.
- WebGL: no threads, no compute, no VFX Graph. Meshes generated at runtime are destroyed in `OnDestroy`.

## Scene wiring (short version — see the handoff notes for numbers)

```
Board            BoardView            layer "Board"; _mapJson, _terrainsJson, _tileMaterial
Input            BoardInputController _tileMask = Board
Unit             UnitView + UnitMover
Match            SkeletonMatchController  → Board, Input, Unit
Cameras/
  vcam_Tilted    CinemachineCamera + CinemachineCameraView (_fixedPose = true)
  vcam_TopDown   CinemachineCamera + CinemachineCameraView (_fixedPose = true)
  CameraDirector CameraDirector       → both views
Main Camera      Camera + CinemachineBrain (tag MainCamera)
```
