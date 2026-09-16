# Targeting UI research — armed attack/spell aiming

Scope: predicted path (line/arc), min/max range rings, out-of-range indicator,
aim-point snapping priority (unit > object preset mark > tile centre), hero
facing while armed. Target platform: Unity 6 URP, WebGL2, UI Toolkit HUD,
tilted Cinemachine camera with top-down toggle, integer tile heights.

---

## 1. UX conventions across reference games

### Baldur's Gate 3 (Larian)
- Melee range: a plain **circle around the character** on the ground; step
  outside it and the attack option greys out. Ranged/spell range: game draws
  a **dashed line from caster to cursor** plus a **radius circle drawn like a
  compass** (centre + edge dot) so you can see exactly where the boundary is.
  AoE shapes (cone/circle/line) are ground-projected decals that follow the
  cursor once you're in range.
- Colour language: white/neutral while a target or tile is valid, **red**
  tint on the AoE decal / cursor ring when you're out of range or the target
  is invalid (e.g. no line of sight, needs unobstructed path for thrown
  items). Modding docs confirm range is a literal `TargetRadius` value (e.g.
  1.5 m = melee) used to drive the circle radius.
- No confirmed evidence in mod/community docs of the hero *rotating* to
  face the cursor pre-confirm (BG3 uses a fixed idle + turns into the attack
  animation itself), but the aiming-line-to-cursor is the closest analogue
  to what's being asked here.
- Sources: https://docs.baldursgate3.game/index.php?title=Making_a_Basic_Spell ,
  https://www.gamerguides.com/baldurs-gate-3/guide/gameplay/getting-started/spell-duration-range-casting-time-and-concentration-explained ,
  https://steamcommunity.com/sharedfiles/filedetails/?id=3122428771

### Divinity: Original Sin 2 (Larian, same lineage as BG3)
- Skill range/AoE is a **highlighted ground area** (filled decal, not just an
  outline) that updates live with the cursor; enemies about to be hit are
  outlined/highlighted directly on their model as extra confirmation (belt
  and braces beyond the ground shape). Community complaints center on the
  AoE shape being *inaccurate at the edges* relative to actual resolution
  logic — a cautionary tale: **the preview must match the sim's real hit
  test exactly**, or players lose trust in it.
- Modding assets literally include named decal props such as
  `LeaderLib_FX_AreaRadius_Decal_Circle` in green/red, flashing vs solid —
  i.e. their in-house pattern is "colour + animation state of a ground decal
  circle" for range/AoE, which maps directly onto a Decal Projector or a
  ring-mesh approach in Unity.
- Sources: https://steamcommunity.com/app/435150/discussions/0/1479857071264418613/ ,
  https://steamcommunity.com/app/435150/discussions/0/3223871682617510213/

### XCOM 2 (Firaxis)
- No literal range *ring* for standard shots (weapons mostly have effectively
  unlimited range, gated by tile-grid line-of-sight instead); the salient
  feedback is the **hit-% panel** built from Aim + range falloff + cover +
  angle, and a **flanking indicator** (yellow shield breaks/appears on the
  target's health bar) rather than a drawn line. Grenades/AOE abilities do
  use a **radius decal** at the target tile, plus red tinting on
  tiles/units that would be hit vs. safe.
- Cover state is shown with on-tile icons (full/half cover chevrons) rather
  than colouring the whole tile; this is a useful precedent for "small
  iconography over big tile tint" when multiple info layers compete.
- Sources: https://xcom.fandom.com/wiki/Tactical_combat_UI_(XCOM_2) ,
  https://xcom.fandom.com/wiki/Flanking_(XCOM_2) ,
  https://strategywiki.org/wiki/XCOM_2/Aim_Bonuses

### Phoenix Point (Snapshot Games)
- Uses a **free-aim reticle with two concentric circles** (inner = ~50% of
  shots land here, outer = all shots land within) directly over the body —
  this is the closest real-world analogue to "aim point snapping to a
  region of the body" but for *accuracy cone* rather than *target lock*.
  Free-aim additionally lets you point at a specific body part (head/arm/
  leg) without changing accuracy, which is conceptually close to your
  "preset hit marks" idea, just applied to creature anatomy instead of
  objects.
- Source: https://steamcommunity.com/sharedfiles/filedetails/?id=2331577913 ,
  http://wiki.phoenixpoint.com/Combat

### Wartales
- Skill/attack range is shown by **locking a range overlay** (right-click
  the ability to pin its range, then move the mouse) rather than it being
  always-on; community feedback flags that the preview can mismatch actual
  reachable targets from a hovered destination tile — again reinforcing
  "preview must equal resolved outcome."
- Source: https://steamcommunity.com/app/1527950/discussions/0/3945776779134510998/

### Gloomhaven (digital, Flaming Fowl/Asmodee)
- Line-of-sight is opt-in visual debug: hold a hotkey, hover a target hex,
  and the game draws **white lines between hex corners** that have LoS, red
  marks on corners that are blocked. This "corner-to-corner" approach is a
  direct artifact of Gloomhaven's board-game LoS rule (any corner sees any
  corner) — less relevant to Mimas's presumably centre-to-centre or
  tile-height-aware LoS, but a good example of *on-demand* LoS visualization
  that doesn't clutter the screen by default.
- Source: https://gloomhaven.fandom.com/wiki/Attack

### Marvel's Midnight Suns / Mario+Rabbids / Into the Breach
- Search didn't surface detailed UI-specific write-ups (card-throw arcs,
  weak-point cover targeting) beyond wiki stubs; treat as **not strongly
  evidenced** rather than absent. What is documented:
  - **Into the Breach**: enemy *intents* are shown as a **red tinted grid
    overlay** on the tiles that will be hit next enemy turn, with an arrow
    from source to target; a known usability complaint is that overlapping
    red tiles from stacked enemies become illegible — supports keeping
    your own hit-preview visually distinct per source (colour/animation)
    when multiple threats overlap.
  - **Mario+Rabbids**: shows a **trajectory line** for ranged shots and a
    hit-% derived from cover (0/50/100%), i.e. same "line + probability"
    pattern as XCOM, just simplified to three tiers.
  - Sources: https://steamcommunity.com/app/590380/discussions/0/1694914736004923146/ ,
    https://www.gamespot.com/gallery/mario-rabbids-kingdom-battle-guide-top-technique-t/2900-1432/

### Cross-cutting patterns worth carrying into Mimas
1. **Two-tier accuracy language is common** (a hard min/max range gate, plus
   a softer "quality of shot" layer like cover/angle) — Mimas's spec only
   needs the hard gate, which simplifies things: range rings + red flip is
   enough, no cover-derived tinting required for v1.
2. **Preview must equal resolution.** Every game above that got called out
   negatively (DOS2, Wartales, Into the Breach) was called out for the
   preview lying at the margins. Whatever draws the ring/arc in Mimas must
   query the *exact same* range/LoS function Core uses to resolve the
   attack — not a separate presentation-side approximation.
3. **Red is the near-universal "invalid/out of range/blocked" colour**,
   applied to whichever element is closest to the cursor (ring, decal, line,
   reticle) rather than the whole screen or the whole tile grid.
4. Nobody surveyed strongly evidences "character turns to face cursor
   while aiming" as a documented UX beat (most are top-down/isometric with
   static idle poses); this is more of a third-person-adjacent convention
   (twin-stick/cover shooters). It's a reasonable Mimas-specific choice
   since heroes are visible 3D models on a tilted camera, but there's no
   genre precedent to lean on for the "return-to-face-opponent" behaviour —
   treat it as a novel decision, not an established convention.

---

## 2. Unity implementation approaches (URP / WebGL2, no compute, no VFX Graph)

### Arc / path preview: LineRenderer vs. mesh strip
- **LineRenderer** is the pragmatic default: no compute shader dependency,
  works everywhere URP does, supports per-vertex width curves and a
  scrolling/tiled material (set `Material.mainTextureOffset` or animate a
  `_MainTex` UV offset each frame) to fake a **dashed, animated "flow"**
  along the arc — the classic "ants marching toward target" look. Sample a
  fixed number of points (8–16) along the analytic arc/line each time the
  aim point changes; recompute only then, not every frame (see §4).
- Known LineRenderer texture caveat: to tile a dashed texture correctly
  along a variable-length line, set `textureMode = LineTextureMode.Tile`
  (not `Stretch`) so dash spacing stays constant regardless of arc length;
  otherwise dashes stretch/squish as range changes.
- **Custom mesh strip** (procedurally building a quad strip along the arc
  each recompute) buys you: independent left/right edge widths for a
  "wedge" look, easier per-segment vertex colour (e.g. green→red gradient
  as the arc nears max range), and avoids LineRenderer's occasional corner
  artifacts at sharp bends. Cost: you own the mesh-building code. For a hex
  tactics game with modest recompute frequency (§4) this is affordable and
  gives more control over the tilted-camera readability (can billboard the
  strip's width toward camera, LineRenderer can only billboard the whole
  line via `alignment = View`).
- Recommendation: LineRenderer for v1 (fast, built-in, good enough); revisit
  a mesh strip only if you need blocked/clear colour gradients along the
  arc's length (e.g. green up to the obstruction point, red past it).
- Sources: https://github.com/ForeignGods/Animated-Line-Renderer ,
  https://docs.unity3d.com/Manual/class-LineRenderer.html ,
  https://james-frowen.github.io/2017/09/28/trajectories-in-unity.html

### Range rings on uneven integer-height terrain
- **URP Decal Projector**: conforms automatically to whatever geometry is
  under it (great for stepped/height tiles), no per-tile mesh work, easy to
  fade/tint via material. **However**, there is an open Unity 6 bug
  (IN-90245, reproduced on 6000.0.24f1 and 6000.0.29f1) where **Decal
  Projectors using the Screen Space technique render correctly in-editor
  but fail to appear in WebGL2 builds**; other threads report decals not
  supported on OpenGL ES-family backends at all for some renderer-feature
  configurations, and decals are documented as **not working on terrain
  details/particles** and requiring the DBuffer/Screen Space renderer
  feature to be enabled in the URP Renderer asset (an easy thing to miss
  and non-obvious to debug on WebGL where you can't always attach a
  graphics debugger). Given Mimas ships WebGL2, **Decal Projector is a risk,
  not a safe default** — at minimum, prototype it early and keep a fallback.
  Sources: https://discussions.unity.com/t/unity-6000-0-24f1-urp-projectors-not-working-in-webgl-2-0/1563253 ,
  https://discussions.unity.com/t/urp-decals-on-webgl/881748 ,
  https://forum.unity.com/threads/webgl-urp-decal-layers-ineffective.1379325/ ,
  https://docs.unity3d.com/6000.0/Documentation/Manual/urp/renderer-feature-decal-projector-reference.html
- **Ring mesh conformed to tile tops** (procedurally generate a ring/annulus
  of short quads, one per hex on the ring's circumference, each quad's Y
  set to that tile's height + small offset): fully WebGL2-safe (it's just
  opaque/transparent geometry, no renderer feature dependency), reads well
  from a tilted camera since it's real 3D geometry sitting on the tile
  surface rather than a flat screen-space decal, and costs one small mesh
  rebuild per range change (cheap — ring vertex count is O(hexes in ring),
  tiny for realistic max ranges). This is effectively "decal projector
  behaviour, hand-rolled," and sidesteps the WebGL2 bug entirely.
- **Per-tile tinting via MaterialPropertyBlock**: cheapest and most robust
  option — iterate the tiles in range, push a `MaterialPropertyBlock` with
  a tint colour onto each tile's `MeshRenderer` (`SetPropertyBlock`), revert
  on tiles leaving the set. Works with GPU instancing/SRP batching if the
  tile shader exposes the tinted property as a per-instance property
  (`[PerRendererData]` / instancing buffer), though MaterialPropertyBlock
  usage is documented to interact carefully with SRP Batcher (can break
  batching if not declared as an instanced property). Reads clearly at a
  tilted camera since the tint is baked into the actual tile surface, and
  handles integer height differences for free (no projection math needed).
  Weakness: doesn't give you the crisp "ring outline" look by itself — best
  combined with a ring mesh or an edge-highlight shader pass for the band's
  boundary, or an emissive rim on the tinted tile's edge.
- **Projected shader on hex tiles** (world-space triplanar/decal-in-shader
  approach baked into the tile material, driven by a shader global — hero
  position + min/max range as shader uniforms, tile shader computes distance
  in its fragment stage and tints/rings itself): avoids the decal renderer
  feature dependency entirely (pure fragment math, no compute), scales to
  any number of tiles for free since it's per-fragment not per-tile-object,
  and naturally respects height because it's evaluated in world space on
  the actual tile mesh. This is the most "custom shader work" of the
  options but is the most robust/cheapest at runtime and the safest against
  WebGL2 renderer-feature quirks.
- **Recommendation**: avoid Decal Projector for range rings given the
  documented WebGL2 bug; prefer either (a) MaterialPropertyBlock tile tint
  for the "in range" band + a thin ring mesh for the boundary line, or
  (b) a shared world-space "range ring" shader baked into the tile material
  if you're comfortable owning a small custom shader. Prototype whichever
  you pick against an actual WebGL2 build early, not just in-editor.

### Cursor / aim-point snapping
- **Physics raycast against tile colliders**: simplest to get right with
  Unity's existing input pipeline (`Physics.Raycast` from camera through
  the mouse ray), and trivially supports **layer masks** to prioritise hit
  order — e.g. raycast against a `Units` layer mask first (enemy aim-point
  colliders, kept small/centered on the body), then an `Interactables`
  layer for objects with preset hit-marks, then fall back to a `TileTop`
  layer for empty-tile centre-snap. This maps directly onto the requested
  priority (unit > object mark > tile centre) as three sequential
  raycasts (or one raycast with `RaycastAll` sorted by layer priority then
  distance).
- **Math pick on the hex layout** (convert mouse world position on the
  ground plane to axial hex coordinates via the standard cube/axial
  rounding formulas) is faster (no physics query) and immune to collider
  authoring mistakes, but only tells you *which tile* — it doesn't
  naturally give you unit/object hover-priority the way layered raycasts
  do, so you'd still need a secondary raycast (or a spatial lookup keyed by
  tile coordinate → occupant) to detect "is there a unit/object here worth
  snapping to instead of the tile centre."
- **Recommendation**: hybrid — raycast (layer-masked, small priority list)
  for unit/object aim-point detection since those need real 3D socket
  positions and precise hover-priority; math/axial pick for "which tile is
  the mouse over" since that's a 2D lookup you'll need anyway for movement/
  range-band checks and is cheaper per-frame than a full physics query
  against every tile's collider.
- Sources: https://forum.unity.com/threads/2017-tilemap-system-select-tile-with-mouse-ingame.506249/ ,
  https://gamelogic.co.za/grids/documentation-contents/quick-start-tutorial/gamelogics-hex-grids-for-unity-and-amit-patels-guide-for-hex-grids/

### Facing: smooth turn toward cursor, return to default
- Standard pattern: compute a target `Quaternion.LookRotation(direction,
  Vector3.up)` toward the aim point (flatten `direction.y` to keep the hero
  upright), then `transform.rotation = Quaternion.RotateTowards(current,
  target, turnSpeedDegPerSec * Time.deltaTime)` — prefer
  `RotateTowards`/an angle-based `Slerp` with a fixed max-degrees-per-second
  over a naive `Slerp(current, target, t)` with a constant `t`, since a
  constant-`t` Slerp is framerate-dependent and can feel "jittery"/snappy
  at direction changes (community threads report exactly this "choppy"
  Slerp complaint) — `RotateTowards` with `Time.deltaTime` gives a
  constant-angular-speed turn that reads as smooth regardless of frame time.
- To avoid jitter when the hovered point flickers across a tile boundary
  (mouse sitting near an edge, aim point toggling between two adjacent
  tiles/units every frame): **do not feed the raw per-frame hover target
  into the rotation every frame.** Instead, only update the *rotation
  target* when the resolved aim point actually changes tile/target (the
  same throttle you need for path recompute, §4) — small mouse jitter
  within the same resolved tile shouldn t reissue a new LookRotation target,
  it should just keep turning toward the last stable target. Optionally add
  a small dead-zone (only re-aim if the new target differs from the old by
  more than an epsilon angle) to kill single-frame flicker outright.
- Return-to-default: on disarm, set target rotation back to
  `LookRotation(opponentPosition - heroPosition)` and let the same
  `RotateTowards` easing carry it back, so arm/disarm both go through one
  facing state machine rather than two code paths.
- Sources: https://discussions.unity.com/t/rotation-with-quaternion-slerp-isnt-smooth/930928 ,
  https://docs.unity3d.com/ScriptReference/Quaternion.Slerp.html

---

## 3. Aim-point / hit-marker authoring (socket/anchor prior art)

- **Unity convention**: an empty **child `Transform` named `AimPoint`**
  (or `HitPoint`/`Muzzle`/`Socket_AimPoint`) parented under the hero/object
  prefab, positioned by the artist in the prefab editor. Presentation code
  reads `unit.transform.Find("AimPoint")` (cached at spawn, not per-frame)
  or, better, a `[SerializeField] private Transform _aimPoint;` wired once
  in the prefab inspector, so no runtime string lookup, and no
  ambiguity if two children could match a name. For humanoid-rigged heroes,
  an alternative is binding to a **Humanoid rig bone** (e.g. `Chest` or
  `Spine1` via `Animator.GetBoneTransform(HumanBodyBones.Chest)`), which
  auto-adjusts if the hero animates/leans, but couples presentation to
  animation state in a way a static child Transform does not — for a
  turn-based game where the hero is idle when being targeted, a plain
  child Transform is simpler and sufficiently accurate, with the bone
  approach as a nice-to-have if you want the aim point to track an
  in-progress hit-react animation.
- **Rules-side vs. presentation-side aim height**: Core's `Mimas.Core`
  rules engine should own a single **integer aim height** per unit (per
  the design's fixed aim-point/body-centre rule) used for all *deterministic*
  math (LoS blocking checks, range-through-height calculations) — this
  must not know about `UnityEngine.Transform`. The **presentation-side
  socket** (`AimPoint` Transform) is purely cosmetic: it's where the
  visual arc/line terminates and where the muzzle/impact VFX plays. The
  two must be kept in sync by construction, not by coincidence — e.g. the
  prefab's `AimPoint` local Y should be authored to equal
  `(coreAimHeightUnits * tileHeightToWorldScale)` for that unit's rig, and
  ideally a small editor validation (or a unit test comparing the
  hard-coded rules constant against the prefab's socket height at
  import/CI time) should catch drift if an artist moves the socket without
  updating the constant, or vice versa.
- **Prior art for "preset hit marks on objects" (vs. BG3/DOS2-style
  "any part is hittable")**:
  - **Company of Heroes**: doesn't expose a clickable hit-mark, but has a
    closely related concept — vehicles have **armor-facing zones**
    (front/side/rear) and a "Target Weak Point" ability that fires at a
    *fixed point* (the target's position at ability-trigger time, not a
    tracked socket) — i.e. weak-point targeting is itself a discrete,
    author-defined interaction rather than freeform aiming.
  - **Helldivers 2**: enemy weak points are fixed anatomical regions (head,
    vents, legs) with distinct hit-marker colours/feedback (white = armor
    reduced damage, red = weak-point full damage, shield icon = deflected)
    — a clean example of *hit-marker-as-feedback-language* your damage
    preview vs. actual-hit reveal (per your M1 combat decisions memory)
    could borrow: colour-code the marker/beam by outcome, not just show/
    hide it.
  - **Zelda (Z-targeting)**: when locking onto certain mini-bosses (Talus,
    Hinox), the lock-on **snaps specifically to the boss's weak point**
    (ore deposit / eye) rather than body centre — i.e. the "snap priority"
    concept already exists in a shipped game: default lock is body centre,
    but an author-placed override (the weak point) takes priority when
    present, exactly analogous to your object "preset hit marker" > "tile
    centre" priority, just applied to creatures instead of destructibles.
  - **Monster Hunter**: breakable parts are per-hitbox-mesh regions rather
    than a single point-socket (any hit registers against whichever part's
    collider was hit), which is the opposite end of the spectrum from
    "preset marks only" — useful to cite as the alternative you're
    explicitly *not* doing (confirms "preset marks, unlike BG3" framing is
    also unlike Monster Hunter's granular hitboxes).
  - **Sniper Elite**: not really a "preset socket" system — the x-ray
    killcam tracks the actual bullet path through a ragdoll/skeleton after
    the fact; the pre-shot UI is a simple reticle, not a snapping aim
    point. Least relevant of the cited titles for this feature.
  - Sources: https://companyofheroes.fandom.com/wiki/Penetration ,
    https://steamcommunity.com/app/231430/discussions/0/412448792355400269/ ,
    https://u.gg/hd2/guides/armor-pen-helldivers2 ,
    https://gameplay.tips/guides/helldivers-2-enemy-weak-spot-guide.html ,
    https://zelda.fandom.com/wiki/Targeting , https://zeldawiki.wiki/wiki/Targeting

---

## 4. Performance notes: recomputing the preview on every mouse move

- The universal pattern across engine forums/devlogs is: **don't recompute
  on every raw mouse-move event** — resolve the mouse to a discrete game
  concept (hex tile / unit / object) first, and only redo the expensive
  work (pathfinding, LoS trace, arc sampling, rotation retarget) **when
  that resolved target actually changes**, not on every pixel of mouse
  movement. This is the same principle turn-based-strategy pathfinding
  devlogs describe for move-range previews ("recompute path only when the
  hovered tile changes"), and it applies identically to an attack/spell aim
  preview.
- Concretely for Mimas:
  1. Every `OnMouseMove` (or `EventSystem` pointer-move), do the *cheap*
     resolve only: axial math pick → current hovered tile id; layered
     raycast (small, 1–3 candidate colliders near the cursor ray, not the
     whole board) → hovered unit/object aim-point, if any.
  2. Compare the resolved aim target (tile id, or unit id, or object+mark
     id) against the last-resolved target. If unchanged, do nothing further
     this frame — the arc/ring/facing state is already correct.
  3. If changed: run the one real computation needed — ask Core for the
     same range/LoS/blocked result it would use to resolve the attack
     (§1's "preview must equal resolution" lesson), rebuild the arc sample
     points (8–16 points is enough for a smooth-looking LineRenderer/mesh
     strip at typical hex-tactics ranges), update ring/tint state only if
     the *band membership* changed (in-range ↔ out-of-range), and set a new
     rotation target for the facing turn (§2).
  4. **Cache per target tile/unit** within a single "armed" session: since
     hero position and ability range/LoS blockers don't change while the
     player is just moving the mouse around before confirming, you can
     precompute a lookup (tile id → in-range bool, tile id → blocked bool)
     once when the ability is armed (covering the full max-range hex ring,
     which is a small bounded set — a handful to a few dozen tiles for
     realistic ranges) rather than re-deriving range/LoS per hovered tile
     on the fly. Mouse-move then becomes a dictionary lookup plus (only on
     tile change) an arc rebuild — cheap enough to run at full mouse-event
     rate on WebGL2 with no throttling needed beyond the "only on tile
     change" gate.
  5. Invalidate that cache only when something that can change range/LoS
     actually happens (hero moves, a blocking unit/object moves or is
     destroyed, height data changes) — rare events compared to mouse moves.
  6. The facing turn itself should keep running every frame *toward the
     last stable target* (for the smooth `RotateTowards` motion, §2), even
     though the target itself only updates on tile-change — i.e. throttle
     *what* you retarget to, not the per-frame interpolation that animates
     toward it.
- Net effect: the only per-mouse-move cost is a plane-math tile pick + a
  short-range raycast + a dictionary lookup + an equality check — all O(1)
  or near enough — with the "real" work (arc mesh rebuild, ring/tint
  updates) amortized to "once per tile the cursor actually crosses," which
  even on a large hex board is a small, bounded number of events per
  second during normal mouse movement.
- Sources: https://yairm210.medium.com/multi-turn-pathfinding-7136bd0bdaf0
  (general "don't recompute per-tile in the hot loop, recompute on real
  state change" principle, applied here to per-frame aim rather than
  per-turn pathing).

---

## 5. Options — concrete UI packages

### Package A — "Decal + LineRenderer" (fastest to prototype, WebGL2-risky)
- Range band: URP **Decal Projector**, radius-shaped via material mask, one
  decal for min-range "dead zone" ring + one for max-range boundary.
- Path: **LineRenderer** with a tiled dashed material, scrolling UV for a
  subtle "energy flow" animation toward the aim point; swap material/tint
  to red when out of band.
- Snapping: layered raycast (unit > object > tile), tile hover via same
  raycast against tile colliders.
- Pros: least new shader/mesh code, decals auto-conform to height, fast to
  iterate in-editor.
- Cons: **the Decal Projector WebGL2 rendering bug (IN-90245) is a real,
  currently-open risk** — could work in editor and silently vanish in the
  actual shipped build; also decals need the Screen Space/DBuffer renderer
  feature enabled and are known-unsupported on some GL-family paths, adding
  a platform-specific validation burden right before ship. Readability at a
  tilted camera is good for flat-ish terrain but decal "swimming"/aliasing
  at grazing angles on steep height steps is a known decal weakness.
- Verdict: attractive on paper, but given Mimas targets WebGL2 exclusively,
  this package carries the highest ship risk of the three.

### Package B — "Tile-tint band + ring mesh + mesh-strip arc" (robust, more shader/mesh work)
- Range band: **MaterialPropertyBlock tint** on every in-range tile
  (green-ish "in band," neutral outside), computed once at arm-time from
  the cached range/LoS lookup (§4); a thin **ring mesh** conformed to tile
  tops at the min-range and max-range boundaries for a crisp edge on top of
  the soft tint.
- Path: procedurally built **mesh strip** along the analytic line/arc, with
  a scrolling dashed texture and a vertex-colour gradient that goes red past
  the point where Core reports the path becomes blocked (if applicable to
  spells with obstruction) — gives strictly more information than a flat
  LineRenderer at modest extra cost.
- Snapping: same hybrid raycast/math-pick as Package A.
- Facing: `RotateTowards` state machine as in §2, shared by both packages.
- Pros: fully WebGL2-safe (no renderer-feature/decal dependency at all —
  everything is ordinary opaque/transparent mesh + vertex colours), height
  is handled for free since tint lives on the real tile mesh and the ring
  mesh samples real tile-top height, best readability at a tilted camera
  because both the band and the ring are real 3D surfaces rather than
  screen-space projections, and both range and arc can share exactly the
  Core-computed blocked/clear data with no separate "does this look right"
  approximation.
- Cons: more one-time engineering (tile tint plumbing that doesn't break
  SRP batching, a small ring-mesh generator, a small strip-mesh generator)
  than dropping in a Decal Projector + LineRenderer; tile tint needs the
  tile shader to expose an instanced tint property up front.
- Verdict: **recommended** — most engineering-safe under the project's
  "WebGL2 only, no compute/VFX Graph" constraint, and the mesh-based
  approach gives the best long-term flexibility (blocked-segment colour
  gradients, per-tile iconography for things like cover later).

### Package C — "World-space shader ring/band + LineRenderer arc" (middle ground)
- Range band + rings: a **shared shader driving fragment-level range math**
  baked into (or overlaid via a second material pass on) the tile material
  — hero world position and min/max range pushed as shader globals each
  arm/move event; each tile's fragment shader computes its own distance and
  renders the band tint and ring lines itself. No per-tile
  MaterialPropertyBlock bookkeeping needed (it's automatic for every tile in
  view), no decal renderer feature dependency.
- Path: plain **LineRenderer** (as Package A) since the arc doesn't need the
  gradient sophistication of Package B for a v1.
  Snapping/facing: same as A/B.
- Pros: scales to board size for free (no per-tile CPU work to turn tint on/
  off, it's all GPU per-fragment), no decal WebGL2 risk, height-aware by
  construction (evaluated on the real tile surface).
- Cons: requires writing/maintaining a small custom shader touched by every
  tile material (higher blast radius if something regresses — a bug in this
  shader affects the whole board's rendering, not just a decorative overlay);
  less flexible than mesh tint for later per-tile special-casing (e.g.
  marking one tile specifically without also recoloring by distance) since
  the logic is purely distance-based unless you add more shader inputs.
- Verdict: reasonable alternative to B if the team is comfortable owning
  shader code and wants to avoid any per-tile CPU bookkeeping; slightly
  less flexible than B for future iconography/exception cases (a single
  odd tile that needs a non-distance-based highlight).

### Summary recommendation
Avoid Decal Projector for range rings on WebGL2 given the open Unity 6 bug;
build Package B (tile-tint via MaterialPropertyBlock + a small conformed
ring mesh + a mesh-strip or LineRenderer arc) as the safe default, with
Package C as the fallback if tile-tint bookkeeping turns out to be more
plumbing than the team wants. Whichever is chosen, wire both range-band
membership and the arc's blocked/clear state directly off the same Core
query used to resolve the actual attack, and gate all recompute (arc,
ring/tint refresh, rotation retarget) on "resolved aim target changed
tile/unit," not on raw mouse-move — that single decision is what every
negative example in §1 (DOS2, Wartales, Into the Breach) was missing.
