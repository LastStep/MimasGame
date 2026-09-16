# Spec B: Aiming client (range circles, path preview, projectile, facing, props on the board)

_Work order for one autonomous Claude Code session (executing model: Claude Opus). Written 16 Sep 2026
from the same question round as Spec A (`2026-09-16-aiming-rules.md`), after a research pass on BG3 /
DOS2 / XCOM 2 targeting UX and on URP-on-WebGL2 rendering options. Design source of truth:
`docs/design/index.html`. **Prerequisite: every Spec A commit is on `main`** (Core has `TargetCheck`,
`MatchState.CheckTarget` / `RangeBand`, `PlayerView.Props`, `UnitView.AimHeight`, `Ballistics`). This
spec is presentation only: nothing here changes a rule, a data file or the wire. Names of existing
client classes were checked on 16 Sep 2026 (commit `53bfd60`); Core names come from Spec A §4._

---

## 0. How to run this session

Read this whole file, then `CLAUDE.md` (golden rules 1, 2, 7, 8, 9), then the design page sections
`#presentation`, `#trajectories`, `#props`, `#attacks`, `#line-of-sight`, then Spec A §2 (the
decisions) and §4.7–4.14 (the Core API you will call). Read every client file you touch in full.

Rules for the session:

1. **Autonomous.** No questions. Forks have defaults in §13.
2. **Commits:** small commits to `main`, `area: what`. Do not push. Before every commit:
   `dotnet build Mimas.sln` and `dotnet test shared/Mimas.Core.Tests` green (Core is untouched by this
   spec, so if they go red you edited the wrong folder), the Editor compiles
   (`unity command recompile_status` → `completed`), `unity command console` has no `error CS` and no
   `Exception`.
3. **Prerequisites:** a Unity Editor open on `MimasClient/` with `unity status --format json` showing a
   `ready` instance. If none is ready at the start: run `unity pipeline list`, report, and stop; there is
   nothing in this spec that can be done without the Editor except §3 (C# only), and unverified Unity
   C# is not worth committing.
4. **Never** create, edit or delete `*.meta`, `.unity`, `.prefab`, `.asset`, `.mat` files by hand. New
   assets (a shader, a material, a prefab-less placeholder) are created through `unity command` /
   the Unity MCP tools or by writing text assets (`.shader`, `.uss`, `.uxml`, `.cs`) and letting the
   Editor import them. Do not touch `ProjectSettings/**`, `Packages/manifest.json`, `packages-lock.json`.
5. **Web constraints:** WebGL2 only. No compute, no VFX Graph, no Decal Projector (open Unity 6 bug
   IN-90245: renders in the Editor, not in WebGL2 builds), no world-space UI Toolkit (UUM-149277).
   The circles are a fragment-shader effect on the tile material; the path is a `LineRenderer`; the
   projectile is a primitive moved by an evaluator; the cursor tag is screen-space UI Toolkit.
6. **Unity CLI gotchas** (all real, from earlier sessions):
   - `export MSYS_NO_PATHCONV=1` before any `unity command` that takes a path.
   - New files on disk are invisible until `unity command menu --path "Assets/Refresh" --timeout 180`;
     then poll `unity command recompile_status` until `completed`.
   - Console: `unity command console`. Grep for `error CS`, `Exception`, `[LocalMatchSession]`,
     `[BoardView]`, `[AimPreview]`, `[ContentBootstrap]`.
   - `capture_game_view --source screen` at native size; `--save_path` under `Assets/Temp/…`, then
     `delete_asset --asset <path> --confirm true` (never `rm`). Copy captures into `artifacts/` with the
     names in §11 before deleting.
   - `eval --code` cannot add `using` or declare classes; use fully qualified names.
   - Stop Play Mode (`unity command editor_stop`) before editing C#. Never two Editor commands at once;
     on a timeout wait 10 s, retry once, then treat the Editor as unreachable and report.
   - Long heredocs get truncated: write scripts to the scratchpad directory and run them.
7. **No scope creep.** Out of scope: real art (models, VFX, sounds), a prop prefab library, the
   character-select screen, camera changes, any Core / server / data change, tile tinting for movement
   (keep what exists), touch input, the `sky` visual beyond the placeholder in §7, a Web build (offer it
   in the report; do not start it).

---

## 1. Goal and result

When an attack is armed the board shows **two true circles** (min and max range) drawn on the tiles,
tiles outside the band dimmed; the hero **turns toward the cursor**; a **path preview** runs from the
hero's aim point to the snapped aim point (enemy body centre > prop hit mark > tile centre) and shows
clear / blocked (red up to the blocking tile, with an X there, and the reason in the tooltip) / out of
range (grey path plus an "Out of range" tag at the cursor). On resolution a **placeholder projectile**
flies the same curve and the flyover fires on impact. **Props** appear on the board with hp tags, can be
targeted, and vanish when destroyed. With nothing armed the hero faces the nearest living enemy.

When done: `AimPreview`, `RangeCircles`, `UnitFacing`, `ProjectilePlayback`, `PropView`,
`BoardHover` exist; `BoardInputController` resolves units and props before tiles; `LocalMatchSession`
drives them from `MatchState.CheckTarget` / `RangeBand`; the tile shader draws the circles; the HUD
shows blocked reasons, the cursor tag and prop hp tags; six screenshots in `artifacts/`; console clean;
design page `#presentation` updated.

---

## 2. Decisions locked in the question round (16 Sep 2026)

| # | Decision | Anchor |
|---|---|---|
| U1 | Range is drawn as **true circles** (the rule is Euclidean, Spec A D9, so the circle is exact). Radii in world units: `minRange · spacing` and `range · spacing` where `spacing = HexLayout.TileWidth(tileSize)` (= √3 · size, the centre-to-centre distance). | `#presentation` |
| U2 | Circles are **distance math in the tile shader**: the tile material receives a centre and two radii; each fragment draws the two ring lines and dims itself outside the band by its world XZ distance. Conforms to plateaus by construction, WebGL2-safe. | `#presentation` |
| U3 | Path preview **state machine**: `Clear` (dashed line/arc, aim marker), `Blocked` (path red up to the blocking hex, X marker there, tooltip names sight or trajectory), `OutOfRange` (grey path to the tile centre, cursor tag "Out of range"), `NotTargetable` (path grey to the snapped point, tag "Cannot be hit" for walls, "Empty" for empty tiles is **not** shown; empty tiles just get the grey path). | `#presentation` |
| U4 | **Snap priority**: living enemy unit's aim point > present prop's hit mark > hovered tile centre (at the tile top + aim height, so the line reads as "chest height"). Hovering any part of a unit's or prop's body selects it. | `#presentation` |
| U5 | **Projectile** placeholder flies the exact previewed curve over a fixed duration; `sky` drops vertically from above the target. Preview and projectile share one evaluator built from `Ballistics`. | `#presentation` |
| U6 | **Facing** is local presentation only: face the snapped aim point while an attack is armed; face the nearest living enemy unit otherwise; keep the last facing when there is none. Constant angular speed (`RotateTowards`), target updated only when the resolved hover changes. Never on the wire. | `#presentation` |
| U7 | **Aim points** are child transforms named `AimPoint` placed at `AimHeight · worldPerUnit` above the unit's/prop's feet, where `worldPerUnit = BoardView._heightUnit / rules.heights.unitsPerLevel` (0.45 / 3 = 0.15 today, so a hero's aim point is 0.6 above the tile top and its 0.9 placeholder body is exactly 6 units). One number, derived, never typed twice. | `#presentation` |
| U8 | Prop visuals are code-built placeholders like the unit capsule: a box `bodyHeight · worldPerUnit` tall; wall dark grey, pillar sandstone; a small ring marker at the hit mark for destructible props. | `#props` |

---

## 3. World scale and aim points (`Presentation/`)

### 3.1 `Presentation/Board/BoardView.cs`

- `public float WorldPerHeightUnit => _heightUnit / Mathf.Max(1, UnitsPerLevel)` where `UnitsPerLevel`
  is read from the catalogue's `Rules.Heights.UnitsPerLevel` after `Build()` (store it in a field; log a
  warning and use 1 if the catalogue is missing, which cannot happen after a successful build).
- `public Vector3 HexToAimPoint(Hex hex, int aimHeight)` = `HexToSurface(hex) + Vector3.up * aimHeight * WorldPerHeightUnit`.
- `public float Spacing => HexLayout.TileWidth(_tileSize)`.
- Expose the tile material's runtime instance for §5: `public Material TileMaterial` (the shared
  material the tiles were created with; the circle uniforms are set on it, tints keep going through
  each tile's `MaterialPropertyBlock`).

### 3.2 `Presentation/Units/UnitView.cs`

- `[SerializeField] private Transform _aimPoint;` plus `public Transform AimPoint`. If null at `Awake`,
  look for a child named `AimPoint`; if none, create one. After `SnapTo` / on `Configure(int aimHeight, int bodyHeight, float worldPerUnit)`
  (new method, called by the session after the board is built and the `PlayerView` is known) set the
  child's local position to `(0, aimHeight · worldPerUnit, 0)` and scale the placeholder body to
  `bodyHeight · worldPerUnit` tall (replace the `_bodyHeight` default only when the placeholder was built).
- Put a `CapsuleCollider` (trigger off) on the placeholder body so the hover raycast can hit it; set the
  layer to `_bodyLayer` (§4). Real models later carry their own collider.
- Add `public UnitFacing Facing` (component on the same object, §8).

### 3.3 `Presentation/Units/PropView.cs` (new)

```csharp
[DisallowMultipleComponent]
public sealed class PropView : MonoBehaviour
{
    public int Id { get; private set; }            // body id from PlayerView.PropView
    public string DefId { get; private set; }
    public Hex CurrentHex { get; private set; }
    public Transform AimPoint { get; private set; }
    public bool IsDamageable { get; private set; }
    public void Configure(PropView view /*Core*/, BoardView board, float worldPerUnit, Color color);   // builds the placeholder box + collider + AimPoint
    public void PlayDestroyed(Action onDone);     // 0.25 s shrink to zero, then Destroy(gameObject)
}
```

The session instantiates one per `PlayerView.Props` entry on `HandleBoardBuilt` (a plain
`new GameObject("Prop " + defId)` under a `Props` parent; no prefab). Colour: wall `#4A4A50`, pillar
`#C8B48A`. Destructible props get a thin torus/ring marker (a scaled cylinder is fine) at the aim point.

---

## 4. Hover resolution (`Presentation/Input/BoardInputController.cs`, new `Presentation/Input/BoardHover.cs`)

```csharp
public readonly struct BoardHover
{
    public readonly TileView Tile;      // tile under the resolved point (never null when Any)
    public readonly UnitView Unit;      // non-null when a unit body was hit first
    public readonly PropView Prop;      // non-null when a prop body was hit first
    public bool Any => Tile != null;
    public Hex Hex => Tile.Coord;
}
```

- `BoardInputController` gains `[SerializeField] private LayerMask _bodyMask;` and raycasts **bodies
  first** (units and props share the "Bodies" layer; use an existing unused layer, read
  `get_tags_layers` first, and set it through `set_tags_layers` only if no free named layer exists —
  say so in the report). If a body is hit, the hover's tile is the body's `CurrentHex` tile view; else
  the existing tile raycast. Publish `event Action<BoardHover> HoverChanged` (fires when the resolved
  unit / prop / tile changes, not per frame) and `event Action<BoardHover> Clicked`. Keep the old
  `TileHovered` / `TileClicked` events firing with the resolved tile so existing consumers keep working.
- Also publish the **cursor screen position** each frame through a `public Vector2 PointerPosition`
  for the cursor tag.

---

## 5. Range circles (`Assets/_Game/Art/Shaders/HexTile.shader`, `Presentation/Board/RangeCircles.cs`)

### 5.1 Shader

Write a hand-written URP shader (text asset; no Shader Graph, its file format is not hand-editable).
Base it on a minimal URP Lit-style forward pass: `_BaseColor` (per-tile through the property block, as
today), simple Lambert + ambient from URP's `Lighting.hlsl` (`MainLight`), shadows off, SRP-batcher
`CBUFFER_START(UnityPerMaterial)` with:

```
float4 _RangeCenter;   // xz world centre, w = 1 when active
float  _RangeMin;      // world radius of the inner circle (0 = no inner circle)
float  _RangeMax;      // world radius of the outer circle
float  _RangeLine;     // line half-width in world units (default 0.05)
float4 _RangeColor;    // ring colour
float  _RangeDim;      // multiplier applied outside the band (default 0.55)
```

Fragment: `d = length(worldPos.xz - _RangeCenter.xz)`; when `_RangeCenter.w > 0.5`:
`inBand = d >= _RangeMin - _RangeLine && d <= _RangeMax + _RangeLine`; colour `*= inBand ? 1 : _RangeDim`;
ring where `abs(d - _RangeMax) < _RangeLine` or (`_RangeMin > 0` and `abs(d - _RangeMin) < _RangeLine`)
→ lerp to `_RangeColor` with a 1-texel `fwidth` smoothstep. Make sure it compiles for GLES3 (`#pragma target 3.0`,
no `SV_` tricks beyond the basics, `#pragma multi_compile_instancing`).

Create `Assets/_Game/Art/Shaders/HexTile.shader` on disk, refresh, then switch `Assets/_Game/Art/Tile.mat`
to it with `unity command` (`set_material_properties` / `eval` setting `material.shader = Shader.Find("Mimas/HexTile")`),
then `save_all`. Check the tiles still show their terrain colours and height tints.

### 5.2 `RangeCircles` component (on the board object)

`Show(Vector3 worldCentre, float minRadius, float maxRadius)` and `Hide()` set the uniforms on
`BoardView.TileMaterial` (`SetVector`, `SetFloat`). Inner radius for `minRange 1`: draw it (a small
ring around the hero reads as "not point blank"); for `minRange 1` and hex layout this is one spacing.
Ring colour: the existing `_targetableTint` desaturated (`#FFB0A8`), alpha 1. Hide on disarm and on
`OnDisable`.

---

## 6. Path preview (`Presentation/Aim/AimPreview.cs`, `Presentation/Aim/FlightCurve.cs`)

### 6.1 `FlightCurve` (static, presentation maths)

Builds a `Func<float, Vector3>` evaluator from Core's integer model so the drawn curve and the rule
agree:

```csharp
public static Func<float, Vector3> Build(string trajectory, Vector3 from, Vector3 to, int fromHeightUnits, int toHeightUnits, int apex, float worldPerUnit)
```

- `direct`: linear `from → to`.
- `arc`: `xz` linear; `y(t) = from.y + (to.y - from.y) * t + (4 * apex + 2 * |Δ|) * t * (1 - t) * worldPerUnit`
  where `Δ = toHeightUnits - fromHeightUnits` (this is `Ballistics.ArcHeightScaled` divided by `b²`,
  written in floats for drawing only; a comment must say so and point at Spec A §4.7).
- `sky`: for `t < 0.15` hold at `from` (a "cast" pause); then a vertical drop from
  `to + Vector3.up * dropHeight` (`dropHeight = 3 * bodyHeight * worldPerUnit`) to `to`.

### 6.2 `AimPreview` component (on the board object)

Fields: a `LineRenderer` (created in `Awake`, `useWorldSpace`, `textureMode = Tile`, width 0.06,
`alignment = View`, material: a small unlit URP material with a 2-texel dashed texture generated in
code (`Texture2D` 8×1, alternating alpha) so no art asset is needed; scroll `mainTextureOffset` for a
gentle flow toward the target), an X marker (two thin quads, red) and an aim marker (small ring, white)
built in code, and a `sampleCount` of 24 (arc) / 2 (direct) / 12 (sky).

API used by the session:

```csharp
public void ShowClear(Func<float, Vector3> curve, Vector3 aimPoint);
public void ShowBlocked(Func<float, Vector3> curve, Vector3 blockedWorld);   // draws red up to the sample nearest blockedWorld (xz distance), X there
public void ShowOutOfRange(Func<float, Vector3> curve);                       // grey, no markers
public void ShowNotTargetable(Func<float, Vector3> curve);                    // grey, aim marker dimmed
public void Hide();
```

Colours: clear `#7CFF9A`, blocked `#FF5A4A`, grey `#9A9A9A` at 60 % alpha. Recompute only when the
resolved hover changes (the session calls once per `HoverChanged`), never per frame except the texture
scroll.

---

## 7. Projectile (`Presentation/Aim/ProjectilePlayback.cs`)

`Play(Func<float, Vector3> curve, float duration, Action onImpact)`: spawns (or reuses) a small sphere
(radius 0.08, unlit, colour by ability category: weapon `#F2E8C8`, spell `#8CC8FF`), moves it along the
curve with the same coroutine pattern as `UnitMover.MoveAlong`, calls `onImpact`, hides the sphere.
Duration: `direct` 0.18 s + 0.03 s per tile of hex distance; `arc` 0.35 s + 0.06 s per tile; `sky` 0.6 s.
The session marks `IsPlaying` true during the flight (so hover previews stay off, as during moves).

---

## 8. Facing (`Presentation/Units/UnitFacing.cs`)

```csharp
public sealed class UnitFacing : MonoBehaviour
{
    [SerializeField] private float _degreesPerSecond = 540f;
    public void FaceWorldPoint(Vector3 point);      // sets the target yaw (y flattened); no-op if within 0.5°
    public void FaceTransform(Transform target);    // follows a transform (the enemy) until replaced
    public void ClearTarget();                      // keep current yaw
    // Update: Quaternion.RotateTowards(current, target, _degreesPerSecond * Time.deltaTime)
}
```

`UnitMover` already rotates units along paths when moving? Read it: if it sets rotation during
`MoveAlong`, `UnitFacing` must not fight it (skip `Update` while `Mover.IsMoving`, then resume).

---

## 9. HUD (`UI/MatchHudView.cs`, `UI/MatchHud.uxml`, `UI/MatchHud.uss`, `Presentation/Match/IMatchHudSource.cs`)

- `HudPreview` gains `public string BlockedReason` (null when clear), `public bool TargetIsProp`,
  `public int TargetPropId`. The tooltip shows the reason as its first body line in the blocked colour:
  "No line of sight" / "Trajectory blocked" / "Out of range" / "Cannot be hit". When blocked or out of
  range the damage lines are still listed (the number the shot *would* do), ghost damage is not applied.
- New `HudCursorTag`: `public string CursorTag` on `IMatchHudSource` (null = hidden) and
  `public Vector2 CursorScreenPosition`; a small label `#cursor-tag` (uss class `cursor-tag`,
  `cursor-tag--visible`) positioned next to the pointer (offset +16, +16 px, converted with the panel's
  scale like the flyovers).
- Props get hp tags: `HudUnit` grows `public bool IsProp;` and the tag builder treats a prop like a unit
  with no AP row and a shorter bar; walls (not damageable) get no tag. The session adds a `HudUnit` per
  destructible prop with its `PropView.transform` as anchor.
- Examine: clicking a prop with nothing armed opens the examine panel with name, description, hp and
  "Blocks sight and movement" as a modifier-like row (`HudExamine` fields already cover it: use
  `Title`, `Description`, `Hp`, `MaxHp`; leave `Items` / `Abilities` empty).
- The action-bar tooltip for an attack appends one line: "Straight shot · needs sight" /
  "Lobbed · no sight needed" / "From above · needs sight" from `AttackDef.Trajectory` and `LineOfSight`,
  and shows the range as "range 1–5" (already there? read `MatchHudView` line ~775; add if not).

---

## 10. Session wiring (`Presentation/Match/LocalMatchSession.cs`)

Read the file in full first (967 lines). Changes, in the order they are hit at runtime:

1. **Board built**: after units are placed, `Configure` each `UnitView` with its `UnitView.AimHeight` /
   `BodyHeight` from the `PlayerView` and `_board.WorldPerHeightUnit`; spawn `PropView`s for
   `view.Props`; register destructible props as `HudUnit`s; set `UnitFacing.FaceTransform(enemy)` on the
   local hero (and the bot's hero faces the local hero, so both read as "in the fight").
2. **Arm an attack** (`SelectAction` → `PaintOptions`): besides `HighlightTargets`, call
   `_state.RangeBand(_localUnitId, def.Id, _bandScratch)`; `RangeCircles.Show(heroAimPointWorld, minRange·spacing, range·spacing)`;
   tint the band tiles with the existing `Targetable`-style highlight only for legal targets (keep) and
   leave the dimming to the shader. Facing: switch the local hero to `FaceWorldPoint` mode on the first
   hover.
3. **Hover** (`HoverChanged` with an attack armed, not `IsPlaying`): resolve the snapped aim point:
   - unit → `unit.AimPoint.position`; prop → `prop.AimPoint.position`; else
     `_board.HexToAimPoint(hex, rules.Heights.Aim)`.
   - `TargetCheck check = _state.CheckTarget(_localUnitId, def.Id, hex)`.
   - Curve: `FlightCurve.Build(attack.Trajectory, heroAim, snappedAim, fromUnits, toUnits, attack.Apex, worldPerUnit)`
     where `fromUnits = tileTop(hero) + hero.AimHeight` and `toUnits = tileTop(hex) + (victim?.AimHeight ?? rules.Heights.Aim)`,
     tile tops from `Map[hex].Height * UnitsPerLevel`.
   - Dispatch on `check.Reason`: `None` → `ShowClear` + damage preview (existing `ShowPreview`, now
     accepting an `IBody`); `NoLineOfSight` / `TrajectoryBlocked` → `ShowBlocked(curve, _board.HexToAimPoint(check.BlockedAt, 0))`
     + preview with `BlockedReason`; `OutOfRange` → `ShowOutOfRange` + `CursorTag = "Out of range"`;
     `NotDamageable` → `ShowNotTargetable` + tag "Cannot be hit"; `NoBody` → `ShowOutOfRange`-style grey
     path with no tag; `OwnUnit` / `TargetDead` → hide the path.
   - `UnitFacing.FaceWorldPoint(snappedAim)` on the local hero.
4. **Click** with an attack armed: submit exactly as today (`AttackCommand` with the hex). A refused
   click (`Submit` false) keeps the armed state; the preview already showed why.
5. **Attack resolved** (`PlayAttack`): build the same curve from the event (attacker aim point →
   victim aim point via the `PlayerView` heights; `TargetIsProp` picks the `PropView`), set `IsPlaying`,
   `ProjectilePlayback.Play(curve, duration, onImpact: existing flyover + hp change)`, then clear
   `IsPlaying`. The bot's attacks go through the same path (its hero faces the local hero already).
6. **Prop destroyed** (`PropDestroyedEvent`): `PropView.PlayDestroyed`, remove its `HudUnit`, drop the
   view from the lookup. Occupancy and sight are Core's business; nothing else to do.
7. **Disarm** (`Disarm()`, right click, end of turn, ability no longer affordable): `AimPreview.Hide()`,
   `RangeCircles.Hide()`, `CursorTag = null`, `UnitFacing.FaceTransform(nearest living enemy unit)`.
8. **Examine** with nothing armed: a click that resolves to a prop opens the prop examine (§9).

Keep the existing movement flow untouched (path preview for moves stays the tile tinting it is today).

---

## 11. Verification (screenshots go to `artifacts/`, captured at native size)

Play a local match vs the bot on `arena-4` (`DefaultMatchSettings.asset` map id; check it is arena-4 and
switch it with `set_serialized_field` if not, then `save_all`) and capture:

| File | What must be visible |
|---|---|
| `aim-circles-bow.png` | Arrow Shot armed: two circles, band lit, outside dimmed, hero turned toward the cursor |
| `aim-arc-clear.png` | Arc preview in green over a wall to the bot's hero, aim marker on its chest, tooltip with damage |
| `aim-direct-blocked.png` | Quick Shot at the bot behind a wall: red line to the wall, X on it, tooltip "No line of sight" |
| `aim-out-of-range.png` | Cursor outside the outer circle: grey path, "Out of range" tag next to the cursor |
| `aim-prop-target.png` | Hovering a pillar: hit-mark snap, hp tag under the pillar, tooltip with damage |
| `aim-projectile.png` | A frame mid-flight of the placeholder projectile (capture right after clicking; if the capture misses, a frame of the X/impact flyover is acceptable and must be reported) |

Also confirm in the console log: `[LocalMatchSession]` lines for one bot attack resolving with a
projectile; `PropDestroyedEvent` handled at least once (shoot a pillar down: two Aimed Shots), and the
tile becomes walkable (move onto it) with no exception.

Commands:

```bash
unity status --format json                              # ready
unity command recompile_status                          # completed
unity command console                                   # no error CS / Exception
dotnet build Mimas.sln && dotnet test shared/Mimas.Core.Tests   # unchanged, green
git status --short                                      # every new asset has its .meta; no Temp/ leftovers
```

---

## 12. Commit plan

1. `client: aim points, prop views and body-first hover resolution` (§3, §4).
2. `client: hex tile shader with range circles` (§5, including the material switch and metas).
3. `client: aim preview, flight curve and projectile playback` (§6, §7).
4. `client: unit facing follows the cursor while aiming` (§8).
5. `client: hud shows blocked reasons, cursor tag and prop tags` (§9, §10, screenshots).
6. `docs: presentation section and roadmap for the aiming client` (§14).

Commit body: one or two lines plus every §13 default taken, then the attribution line the session's
system reminder requires.

---

## 13. Defaults for forks the session may hit

| If… | Then… |
|---|---|
| No free named layer exists for bodies | Reuse the tile layer for bodies and give body colliders a `BodyCollider` tag instead; resolve by `GetComponentInParent<UnitView>()` / `PropView` first, `TileView` second. Say so. |
| The URP `Lighting.hlsl` include path differs in 6000.4 | Use `Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl` (check with `find`); if lighting is a fight, ship the shader unlit with a fixed 0.85 brightness and note it. |
| Switching `Tile.mat` to the new shader loses the tile colours | The property name must stay `_BaseColor`; the tint code in `TileView` is untouched. |
| SRP batcher warnings appear after the shader change | Acceptable (the property blocks already break batching); note it in the report. |
| `LineRenderer` dashes stretch | `textureMode = LineTextureMode.Tile` and a `mainTextureScale` of (1/dashLength, 1). |
| The projectile capture misses the frame | Capture the impact instead and say so. |
| `UnitMover` writes rotation during moves | Pause `UnitFacing` while `IsMoving`. |
| The bot's hero should face the cursor too | No: only the local hero aims; the bot's hero faces the local hero always. |
| A `TargetCheck.Reason` value has no UI mapping | Treat it like `NoBody` (grey path, no tag) and list it in the report. |
| `DefaultMatchSettings.asset` is on `board-3` | Switch it to `arena-4` for the screenshots and switch it back before the last commit if it was different, saying so. |
| `unity command` times out twice | Stop, commit what compiles and is verified, report the rest as unverified. |

---

## 14. Documentation updates

- `docs/design/index.html` `#presentation`: replace the `proposed` bullet from Spec A with a decided
  "Targeting" paragraph (U1–U8, the state machine, the snap priority, facing local-only), `data-impl`
  implemented; decision log and changelog entries.
- `docs/roadmap.md`: "M3 progress: the aiming slice, client half" paragraph with the artifact names and
  the placeholder-visual caveats; tooling backlog row "projectile / impact VFX" if useful.
- `docs/decisions.md`: **ADR-025** (2026-09-16): *Range circles in the tile shader, LineRenderer path
  preview, code-built placeholders for props and projectiles.* Rejected: URP Decal Projector (WebGL2
  bug IN-90245), ring meshes conformed to tiles (seams at steps), world-space UI Toolkit (UUM-149277).

---

## 15. Definition of done (copy into the final report with ticks)

- [ ] Editor compiles, console clean; Core tests unchanged and green.
- [ ] New: `BoardHover`, `PropView`, `UnitFacing`, `RangeCircles`, `AimPreview`, `FlightCurve`, `ProjectilePlayback`, `HexTile.shader` (+ `Tile.mat` on it), HUD cursor tag and blocked reasons, prop hp tags and prop examine.
- [ ] `BoardInputController` resolves bodies before tiles; `UnitView.AimPoint` placed from `UnitView.AimHeight`; `BoardView.WorldPerHeightUnit` derived from rules.
- [ ] The six screenshots in `artifacts/`; a pillar was destroyed in play and its tile walked onto.
- [ ] Six commits on `main`; nothing pushed; every new asset committed with its `.meta`.
- [ ] Design page, roadmap, ADR-025 updated.
- [ ] Final report: every §13 default taken, anything unverified, and the offer to run a Web build.
