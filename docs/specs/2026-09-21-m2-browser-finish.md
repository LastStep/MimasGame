# Spec E: Finish M2 in the browser (rematch in the room, the Mimas Web template, Copy code, the blocker shown, origin check, multi-browser smoke)

_Work order for one or two autonomous Claude Code sessions (executing model: Claude Opus). Written
21 Sep 2026, the day the game went live at `https://mimas.laststep.cloud`, after a one-round question
round with Rohan (§1). Design source of truth: `docs/design/index.html` (`#online`, `#presentation`,
`#aiming-presentation`, `#line-of-sight`, `#hidden-info`). Names of existing code were checked on
21 Sep 2026 at commit `9f57ae1`. Three earlier specs shaped this one and are the reference for tone and
rigour: `2026-09-17-online-slice.md` (§0 rules, §12 forks), `2026-09-22-deploy.md` (Part 3, the live
smoke). **321 Core tests, 34 server tests, all green at the commit above.**_

---

## 0. How to run this session

Read this whole file, then `CLAUDE.md`, then the design sections named above (grep the anchors; never
the whole page), then `docs/decisions.md` ADR-026, ADR-028, ADR-031. Read every file you touch in full
before editing it, in particular `server/Mimas.Server/Rooms/Room.cs`, `RoomRegistry.cs`,
`Net/WsConnection.cs`, `Program.cs`, `server/Mimas.Server.Tests/RoomTests.cs`, `MatchTests.cs`,
`FakeClient.cs`, `MimasServerFactory.cs`, and on the client `Net/NetClient.cs`, `UI/LobbyView.cs`,
`UI/Lobby.uxml`, `UI/MatchHudView.cs`, `Presentation/Match/MatchSession.cs`, `IMatchHudSource.cs`,
`Presentation/Aim/AimPreview.cs`, `Presentation/Units/PropView.cs`, `Presentation/Board/TileHighlight.cs`,
`Editor/WebBuild.cs`, and `tools/smoke/browser-smoke.mjs`.

Rules for the session — the same as the online spec's §0, restated where they bite here:

1. **Autonomous.** No questions. Every fork has a default in §13. Balance numbers are not yours.
2. **Commits:** small commits to `main`, `area: what`, in the order of §11. Do not push. Before every
   commit: `dotnet build Mimas.slnx`, `dotnet test shared/Mimas.Core.Tests`, `dotnet test
   server/Mimas.Server.Tests`, all green. For client commits additionally: `unity command
   recompile_status` → `completed`, and `unity command console` has no `error CS` and no `Exception`.
3. **Order of work.** §3 and §4 (server) need no Editor and come first. Do not open §5–§8 until §4 is
   green and committed. If no Editor is `ready` (`unity status --format json`) when you reach §5, stop
   after §4's docs and say so; unverified Unity C# is not worth committing. **`unity build` needs no
   Editor** — §8's build proof can still run in batch mode.
4. **Never** create, edit or delete `*.meta`, `.unity`, `.prefab`, `.asset`, `.mat` by hand. This spec
   allows exactly one `ProjectSettings` outcome: `WebBuild.cs` writes `productName`, `companyName`,
   the splash flag and the template name **through the PlayerSettings API**, and Unity saves the file
   (§8.3) — the same route the exception-support setting already takes. Nothing in
   `Packages/manifest.json` or `packages-lock.json`.
5. **Golden rules** 3 and 4 for `shared/`: nothing here touches Core's rules. §7 adds a Core *test*
   only.
6. **Hidden information.** `room.state` carries no loadouts (there is a test); a rematch must not
   change that. `PlayerView` is still the only match bytes that leave a room.
7. **Web constraints:** WebGL2, no threads, no `Task.Delay` on the client; the clipboard goes through
   `WebClipboard`, never `GUIUtility.systemCopyBuffer`.
8. **Unity CLI gotchas** — all of the online spec's §0 rule 8 still holds (`MSYS_NO_PATHCONV=1`,
   `Assets/Refresh` then poll `recompile_status`, `capture_game_view` then `delete_asset`, stop Play
   Mode before editing C#, never two Editor commands at once, long heredocs truncate). Two more from
   the deploy session: the **asset guard scans the whole command text**, so never name a protected
   path in an `echo`, a commit message body or a grep pattern (use the Write/Edit tools for markdown);
   and the session scratchpad matches the Unity temp pattern, so write helper scripts under
   `artifacts/tmp/` instead.
9. **No scope creep.** Out of scope (§14): the best-of-three session wrapper, a session score, ratings,
   accounts, spectating, chat, a real Mimas logo, mobile layout, the WebGPU template, any change to the
   rules of sight or trajectory, real prop art, and anything on the VPS.
10. **Agents never deploy** (ADR-031 D4). §12 says what Rohan runs and what a later short session
    measures, exactly as the deploy spec's Part 2 / Part 3 did.

---

## 1. Decisions (Rohan, 21 Sep 2026, one question round)

| # | Decision | Chosen |
|---|---|---|
| D1 | **Rematch (M2-8)** | **Back to the same room.** The room outlives the match: same code, both seats kept, Ready reset, presets editable again. Pressing Ready twice is the rematch. A leaver frees the seat as today; a friend can rejoin by code. Bot rooms behave the same. No new wire message — `room.state` already carries everything. *Not* chosen: a rematch offer in the banner, a session score |
| D2 | **Web template page shape** | **Full-window canvas.** The game fills the browser window and resizes with it; dark background; title `Mimas`; own favicon; a thin loading bar with a percentage; no Unity footer, no fullscreen button |
| D3 | **Splash and identity** | **Splash off, rename.** `PlayerSettings.SplashScreen.show = false` (Unity 6 allows it on every licence); `productName = "Mimas"`, `companyName = "Trinetra"`. The HTML loading bar is the only thing before the lobby |
| D4 | **Hardening folded in** | **Yes, both.** A WebSocket origin allow-list on the server, and `--browser webkit|firefox` in the smoke script |
| D5 | T-0005 and T-0006 | Executed here, as written in their task files, unchanged in scope |
| D6 | Design page | Rematch moves from the `#online` drift-note's "Later" list into the rules as item 9, `decided 21 Sep 2026`. The task carries `allows_assets` for the page; the builder edits it (§10.1) |

---

## 2. What exists today, and what changes

| Thing | Today (`9f57ae1`) | After this spec |
|---|---|---|
| `Room.cs` match end (line ~321) | `State.IsOver` → `Phase = Over`, `Close()`; the registry forgets the room; `Player.RoomId = 0` | `Phase = Waiting` again; seats kept; `Ready` reset for humans; a seat whose socket is gone is freed; the room closes only if no human remains |
| `RoomPhase.Over` | The phase a room is in while closing | Unchanged meaning; reached only from `Close()` (all humans gone) |
| `match.start` payload | `matchId`, view, clock, … | `+ round` (1 for the first match in a room, 2 for the rematch, …). Additive; the client ignores it except in a log line |
| `auth.ok` payload | player id, name, token, … | `+ room` (the four-letter code) when the resumed player is seated in a `Waiting` room. Additive |
| `NetClient.ForgetMatch()` | Clears the match id and `MatchPref` | Unchanged; the room is not the match |
| `NetClient` | Caches `PendingMatch` for the Arena | `+ PendingRoom`: the last `room.state` payload, cleared on `room.left`; `ConsumePendingRoom()` |
| `LobbyView.Start()` | Front lobby, or "Reconnecting…" if a match id is stored | `+` if `PendingRoom` is set: straight to the room panel with the last result on its status line |
| Result banner button | "Back to lobby" always | "Back to room" when the connection still has a room; "Back to lobby" in local practice |
| `Lobby.uxml` room panel | `copy-link` | `+ copy-code` beside it |
| Refused shot | Red path, X on the blocker; HUD names the reason | `+` the blocking **tile** tinted (`TileHighlight.Blocker`) while the target is hovered; props are hex prisms the size of the hex they block |
| `PropView` placeholder | A box at 62 % of the tile | A hex prism with the tile's footprint, `bodyHeight` tall, ring kept |
| Web template | Unity's `APPLICATION:Default`: 960×600 box, footer, `Unity Web Player \| MimasClient`, Unity splash | `PROJECT:Mimas`: full-window canvas, `Mimas`, own favicon, percentage loading bar, no splash |
| `/ws` | Any `Origin` accepted | In `Production`, only `https://mimas.laststep.cloud`; elsewhere unchanged |
| `browser-smoke.mjs` | Chromium only | `--browser chromium\|webkit\|firefox` |
| Design `#online` | Rematch is in the "Later" list of the drift-note | Rule 9, decided |

---

## 3. Server: the room outlives the match (D1)

### 3.1 `Room.cs`

1. **At match end** (the block after `if (!State.IsOver) return;`): keep the log line; stop the ticker
   (`_ticker?.Cancel(); _ticker = null;`); set `_bot = null`; **do not** call `Close()`. Then, under the
   same lock:
   - for every human seat: `Ready = false`; keep `Loadout` (so Ready is one click and the preset
     dropdown shows what they played); keep `Player` and `Connection`;
   - a human seat whose `Connection == null` (the match ended by forfeit, or the socket dropped in the
     last second) is **freed**: `seat.Player.RoomId = 0; seat.Clear();` — the reconnect grace is a
     match thing, and there is no match;
   - the bot seat keeps `IsBot`, `BotName`, `Loadout`, `Ready = true`;
   - if no human seat remains, `Close()` as today; otherwise `Phase = RoomPhase.Waiting` and
     `BroadcastRoomState()`.
   - `State` may stay referenced until the next `StartIfBothReady()` replaces it; nothing reads it in
     `Waiting` (check `HandleCommand`, `HandleResync`, `Tick` — all already gate on `Playing`).
2. **`StartIfBothReady()`** already rebuilds everything a match needs (`State`, `_seq = 0`, the seed,
   the bot, the clock, the ticker). Add `Round` (`public int Round { get; private set; }`, incremented
   here) and put it in the log line and in the `match.start` payload as `round`. Confirm `_scratch` /
   `_filtered` are cleared per use (they are lists reused per tick; read the code, do not assume).
3. **`OnConnectionClosed` in `Waiting`** already frees the seat and closes the room when no human is
   left. That is the "closed the tab after the result" path; no change.
4. **`Leave` in `Waiting`** is the "Leave room" button after a match; no change.
5. **`Reattach`** in `Waiting` already rebroadcasts room state; no change.
6. **`TryJoin`** in `Waiting` fills the freed seat; so a friend who dropped can come back by code, and
   a *different* friend can take the seat. That is intended (D1: "a friend can rejoin by code").

### 3.2 `RoomRegistry.cs` / `WsConnection.cs`

- `auth.resume` → after `Reattach`, the `auth.ok` payload gains `room: <code>` when
  `RoomOf(player)` is non-null and in `Waiting`. Find where `auth.ok` is built (WsConnection's auth
  path) and add the field there; the registry exposes `string? WaitingRoomCodeOf(Player)`.
- `Busy()` is unchanged: a player seated in a `Waiting` room gets `in_room` on `room.create`,
  `bot.play` and `room.join`. The client will not offer those buttons while in a room (§5.2), and the
  `auth.ok.room` field is what stops the reload case from tripping over it.

### 3.3 Tests (`server/Mimas.Server.Tests/RoomTests.cs`, `MatchTests.cs`)

New, one behaviour each, in `Method_Scenario_Expected` form:

- `Room_MatchOver_ReturnsToWaiting_SameCodeBothSeats` — two humans, both ready, P1 resigns; both
  receive `match.events` with `MatchEndedEvent`, then `room.state` with the same code, both seats
  `present`, both `ready: false`.
- `Room_MatchOver_ReadyAgain_StartsRoundTwo` — continue: both send `room.loadout` ready; both receive
  `match.start` with `round: 2` and a **different** seed-derived first player is *not* asserted (it is
  random); assert `matchId` unchanged and the view is a fresh board (turn 1, both heroes at full hp).
- `Room_MatchOver_ForfeitedSeatIsFreed` — P2 closes its socket; wait past the grace (the test factory
  sets `ReconnectGraceMs` small already — read `MimasServerFactory`); P1 receives the forfeit result,
  then `room.state` with seat 1 `present: false`; a third client joins by code and takes seat 1.
- `Room_MatchOver_BotRoom_ReadyStartsAgain` — `bot.play`, resign, `room.state` shows the bot seat
  ready, `room.loadout` ready → `match.start` with `round: 2`.
- `Room_MatchOver_LeaveThenCreate_Works` — after the result, P1 sends `room.leave`, receives
  `room.left`, then `room.create` succeeds (proves `RoomId` was released by `Leave`, not stuck).
- `Auth_ResumeInWaitingRoom_AuthOkNamesRoom` (`AuthTests.cs`) — after a finished match, P1 reconnects
  with `auth.resume`; `auth.ok` has `room` equal to the code, and a `room.state` follows.
- `Room_LoadoutIsNotLeakedBeforeStart` stays green **and** gains a second assertion: the `room.state`
  sent after the match carries no loadout either.
- `MatchTests`: extend the existing whole-match test (the one that plays to a win condition over real
  sockets with `MirrorPlayer`) with a second round in the same room, so the ladder's rung 5 proves the
  rematch end to end. If that test is long, a new `Match_TwoRoundsInOneRoom_BothComplete` is fine.

---

## 4. Server: WebSocket origin allow-list (D4)

### 4.1 Options and configuration

- `ServerOptions.AllowedOrigins` — `string[]? AllowedOrigins { get; set; }`. **Null or empty means
  any origin is accepted**, which is today's behaviour and what every test and `dotnet run` relies on.
- New file `server/Mimas.Server/appsettings.Production.json`:

  ```json
  { "Mimas": { "AllowedOrigins": [ "https://mimas.laststep.cloud" ] } }
  ```

  The unit already sets `ASPNETCORE_ENVIRONMENT=Production`, so the live server picks it up on the next
  deploy with **no unit or nginx change and no `--setup`**. Verify the file lands in the publish
  folder (§12 step 1); if it does not, §13 has the csproj line.
- No hostname goes into C#. The only place the name appears is this JSON and the nginx conf.

### 4.2 `Program.cs`

Before `AcceptWebSocketAsync()` on `/ws`: if `AllowedOrigins` is non-empty, read the `Origin` header;
accept only an exact, case-insensitive match against the list (scheme + host + optional port, no
trailing slash; compare the header string, do not parse). Otherwise respond `403` with a one-line body
and `LogWarning("ws: origin {Origin} refused", origin ?? "(none)")`. Browsers always send `Origin` on a
WebSocket handshake; a missing header in production is refused too.

### 4.3 Tests

- `Ws_OriginNotInAllowList_Refused403` — `MimasServerFactory.WithWebHostBuilder(b =>
  b.ConfigureAppConfiguration(c => c.AddInMemoryCollection(new[] { KeyValuePair.Create(
  "Mimas:AllowedOrigins:0", "https://good.example") })))`; `Server.CreateWebSocketClient()` with
  `ConfigureRequest = r => r.Headers["Origin"] = "https://evil.example"`; the connect throws and the
  status is 403 (assert on the exception's status or the response, whichever the test host exposes —
  read what `WebSocketClient` throws before writing the assert).
- `Ws_OriginInAllowList_Connects` — same factory, `Origin: https://good.example`, `auth.guest` works.
- `Ws_NoAllowListConfigured_AnyOriginConnects` — the default factory with `Origin:
  https://anything.example`; connects.

### 4.4 Docs

- `docs/deploy-runbook.md` §7 gets a row: `curl … /ws` returning `403` on the live box means the
  origin header is missing — add `-H "Origin: https://mimas.laststep.cloud"`. The deploy spec's §13
  fork line (the `curl -i -N` check) is amended the same way in this spec's §12.
- `docs/networking.md`: one paragraph, "Origins".
- ADR-033 (§10.3).

---

## 5. Client: rematch in the room (D1)

### 5.1 `NetClient.cs`

- `public JObject PendingRoom { get; private set; }` — set from every `room.state` received (whatever
  scene is up), cleared on `room.left`, on `Disconnect()`, and on a `bad_token` error.
  `ConsumePendingRoom()` returns and clears it, mirroring `ConsumePendingMatch()`.
- `ForgetMatch()` unchanged.
- On `auth.ok` with a `room` field: nothing to do beyond logging — the server's `room.state` follows
  and lands in `PendingRoom` / `LobbyView`.

### 5.2 `LobbyView.cs`

- **`Start()`**: after `TryBind()`, and only when `_net.CurrentMatchId == 0`: `JObject room =
  _net.ConsumePendingRoom(); if (room != null) { ShowRoom(room); … }` with `_isReady = false`,
  `_ready.text = "Ready"`, `_preset.SetEnabled(true)`, and the status line set from `_net.LastResult`:
  `"Victory · by elimination — Ready for another?"` / `"Defeat · you resigned — Ready for another?"` /
  `"Victory · opponent left — the seat is free; share the code"` when the other seat is not present.
  Use the reason strings `MatchSession.ShowResult` already builds; do not invent new ones.
- **`HandleMessage` `AuthOk`**: if the payload has `room`, drop `_pendingType` / `_pendingPayload`,
  `_busy = false`, `SetStatus("Back in room " + code + "…")`, and wait for `room.state` (which calls
  `ShowRoom`). This is the reload-after-a-match path (§3.2).
- **`ShowRoom`** must already handle a seat that is not `present` (it renders `seat-0` / `seat-1`);
  confirm the text for an empty seat says the code is still good ("empty · share the code").
- Preset dropdown: after a match the seat's `Loadout` is still on the server, but the client sends its
  own loadout with every `room.loadout`; the dropdown keeps its last selection through the scene reload
  only if `LoadoutSettings` persists it — read it; if it does not, default to the first preset and
  say so in the run report. Not a bug to fix here.

### 5.3 `MatchSession.cs` / `IMatchHudSource.cs` / `MatchHudView.cs`

- `IMatchHudSource` gains `string BackLabel { get; }`. `MatchSession` returns `"Back to room"` when
  `NetClient.Instance != null && NetClient.Instance.PendingRoom != null` (the room state arrived with
  the result), else `"Back to lobby"`. `MatchHudView` sets `_bannerButton.text` from it when the
  banner shows. `LocalMatchDriver` practice matches keep "Back to lobby" (there is no NetClient).
- `BackToLobby()` unchanged: it still records `LastResult`, `ForgetMatch()`, loads `Lobby`.
- The 3 s `BackToLobbyDelaySeconds` stays.

### 5.4 Proof in the Editor (rung 7) and in a browser (rung 10)

- Editor: `unity command editor_play`, `Play vs bot` against a local `dotnet run` server, resign
  through the HUD (two clicks), wait for the banner, press **Back to room**, see the room panel with
  the bot seat ready and the status line naming the result; press Ready; the Arena loads again; console
  shows `[LobbyView] loading the Arena for match N` twice and `[MatchSession] match over` once before.
  Capture the room panel after the first match as `artifacts/rematch-room.png`.
- Browser: `browser-smoke.mjs --do` script that drives the same sequence against a locally served
  `Build/Web` — clicks by coordinates are fragile, so prefer `--expect` on the console lines above; the
  harness's `--do` supports `wait:`, `click:`, `type:`, `shot:` (read the script header). If a headless
  bot match is not drivable in reasonable time, the Editor proof plus the server tests stand, and the
  run report says so (§13).

---

## 6. Client: Copy code (T-0005, D5)

Exactly `studio/tasks/T-0005-copy-code-button.md`:

- `Lobby.uxml`: `<ui:Button name="copy-code" class="button button--quiet" text="Copy code" />` beside
  `copy-link`, in a horizontal row (a `VisualElement` with a `row` class if `Lobby.uss` has one;
  otherwise add `.room-actions { flex-direction: row; }` to `Lobby.uss`).
- `LobbyView.cs`: bind `_copyCode`, `HandleCopyCode` → `WebClipboard.Copy(_code)`,
  `SetRoomStatus(copied ? "Code copied." : "Could not copy — read them the code instead.")`, a
  `[LobbyView] copy code XXXX -> ok|refused` log line, unsubscribe in `OnDestroy`.
- Smoke: a `--do` step that clicks it and reads the clipboard back (`clip:` exists — read the header),
  run **headed** in Chromium (the task says so; headless Chromium has no clipboard). Record the command
  and its output in the run report.

---

## 7. Client: refused shots show the blocker (T-0006, D5)

Exactly `studio/tasks/T-0006-refused-shots-show-the-blocker.md`, in its order:

1. **Reproduce first**, as a Core test that documents rather than changes:
   `shared/Mimas.Core.Tests/…/SightArena4Tests.cs` → `IsClear_Arena4_GrazingPillar_NamesFlankingHex`
   and one more case, each asserting `AttackTargeting` returns `TargetRejectReason.NoLineOfSight` /
   `TrajectoryBlocked` **and** the `BlockedAt` hex, for two (attacker, target) pairs on `arena-4`
   picked by reading the map JSON: one where the ray grazes a pillar corner (the "either flanking hex
   blocks" rule, `#line-of-sight` rule 5) and one where a hero body is the blocker. Write both cases
   in the run report with a sentence each on whether the rule or the picture is wrong. **Default:
   the picture.** The rule changes only if the design page is wrong, and then the page changes too
   (§13).
2. **Show the blocker.** `TileHighlight` gains `Blocker`; the tile shader / `TileView` renders it as a
   red-tinted tile (use the existing `_blockedColor` from `AimPreview` for consistency). Where
   `AimPreview.ShowBlocked` is called with the blocked hex, also set the highlight on that tile; clear it
   whenever the preview clears or the hover moves. The X on the curve stays.
3. **Props fill their hex.** `PropView.BuildPlaceholder`: replace the box with a hex prism built from
   the tile's outline (`HexMeshFactory` has the footprint; extrude to `bodyHeight`; flat-shaded, same
   material and tint), keep the aim-height ring at the prop's `AimHeight`. Drop `_footprint` (it is a
   private serialized field on a code-built object; nothing in a scene references it — confirm with
   `unity command` `find_objects`/`grep` before deleting; if something does, keep the field and set it
   to 1.0 with `[FormerlySerializedAs]` untouched).
4. Capture `artifacts/blocker-highlight.png` (a refused shot hovered) and
   `artifacts/prop-prism.png`.

---

## 8. The Mimas Web template (D2, D3)

### 8.1 Files

`MimasClient/Assets/WebGLTemplates/Mimas/` — **copy Unity's own Default template first**, from the
installed Editor (`<Editor>/Editor/Data/PlaybackEngines/WebGLSupport/BuildTools/WebGLTemplates/Default/`;
`unity` CLI can tell you the Editor path, or `unity command eval --code
"return UnityEditor.EditorApplication.applicationPath;"`), then edit. Copying keeps the loader
boilerplate (`createUnityInstance`, the `{{{ }}}` variables, `unityShowBanner`) exactly as Unity
expects. Then:

- `index.html`: `<title>{{{ PRODUCT_NAME }}}</title>`; `<link rel="icon" href="TemplateData/favicon.svg">`;
  remove `#unity-footer`, the fullscreen button and the build title; the canvas has no `width`/`height`
  attributes — CSS sizes it; keep `#unity-loading-bar` and add `<div id="unity-progress-text">0%</div>`
  updated from the `createUnityInstance` progress callback (`Math.round(progress * 100) + "%"`); keep
  `unityShowBanner` (the smoke does not depend on it, but the loader calls it); `config` keeps
  `matchWebGLToCanvasSize` at its default (true) so Unity renders at the canvas's CSS size × DPR — the
  18 Sep measurement said there is nothing to cap, so do not set `devicePixelRatio`.
- `TemplateData/style.css`: `html, body { margin: 0; height: 100%; background: #0b0d12; overflow:
  hidden; }`, `#unity-container { position: fixed; inset: 0; }`, `#unity-canvas { width: 100%;
  height: 100%; display: block; }`, the loading bar centred with a `Mimas` wordmark in text (no logo
  asset exists; a text wordmark in the system UI font is the decided placeholder), `#unity-warning`
  kept. Delete the Unity logo PNGs the Default template ships and the references to them.
- `TemplateData/favicon.svg`: a single hexagon in the accent colour the HUD uses (read `MatchHud.uss`
  for the token; if there is none, `#e8b45c`). SVG is fine in every browser the smoke runs.
- After adding the folder: `unity command menu --path "Assets/Refresh"`, wait for `recompile_status`,
  and commit the new `.meta` files Unity made with the assets (never write one).

### 8.2 nginx

`tools/deploy/nginx-mimas.conf` already serves `/TemplateData/` with a one-hour cache and inherits the
`svg` type from `mime.types`. No change expected; confirm by reading the conf and say so.

### 8.3 `WebBuild.cs`

In `Run()`, beside the existing PlayerSettings lines, as **decided values** (like
`ShippedExceptionSupport`), so the committed `ProjectSettings.asset` records them after any build:

```csharp
PlayerSettings.productName = "Mimas";
PlayerSettings.companyName = "Trinetra";
PlayerSettings.SplashScreen.show = false;          // Unity 6: allowed on every licence
PlayerSettings.WebGL.template = "PROJECT:Mimas";
```

`SaveProjectSettings()` already persists them. The task's `allows_assets` lists
`ProjectSettings/ProjectSettings.asset` for this reason; **commit it with the WebBuild change**, and
nothing else in `ProjectSettings/`. Rohan agreed to these four on 21 Sep (D3).

Docs: https://docs.unity3d.com/6000.4/Documentation/Manual/web-templates-introduction.html and
https://docs.unity3d.com/6000.4/Documentation/ScriptReference/PlayerSettings.SplashScreen-show.html —
check both before writing; if the template variable names differ from the copied Default, the copied
file is right.

### 8.4 Proof

- `unity build MimasClient --target WebGL --execute-method Mimas.Client.Editor.WebBuild.Build
  --output-path Build/Web` — exit 0; `Build/Web/index.html` has `<title>Mimas</title>` and no
  `unity-footer`; the `[WebBuild]` size line is within the 13 MB ratchet (the template is kilobytes).
- Serve it (`MIMAS_WEB_PATH=Build/Web dotnet run --project server/Mimas.Server`) and run the smoke
  with `--timing` in all three browsers (§9). Capture `artifacts/template-chromium.png` at 1920×1080
  and at 1280×720 (`--viewport` if the script has it; add it if not — two lines) to show the canvas
  filling both.
- Confirm in the console log that no `Made with Unity` splash frame appears: the first `[ContentBootstrap]`
  line should come well under a second after `booted` locally (the 18 Sep number with the splash was
  ~3 s; write both in the run report).

---

## 9. Smoke: `--browser` (D4)

`tools/smoke/browser-smoke.mjs`: `import { chromium, webkit, firefox } from 'playwright'`;
`const engines = { chromium, webkit, firefox }; const engine = engines[args.browser ?? 'chromium']`
(unknown → exit 2 with the list); launch `engine`. The `clip:` step and the clipboard permissions are
Chromium-only — skip them with a printed `[skip] clipboard step: <browser>` rather than failing. Update
the header comment and `deploy.sh --smoke`'s usage line. `npx playwright install webkit firefox` once
(document the command in the header; it is not run by `deploy.sh`).

WebKit-on-Windows is not Safari (the deploy spec said so); it catches WebGL2 and Brotli-decoding
differences, not Apple's own quirks. Record all three `[timing]` lines against the local build.

---

## 10. Design page, docs, ADRs

### 10.1 `docs/design/index.html` (the task allows it)

- `#online` rules list: append **item 9**, `<li><b>Rematch</b> (decided 21 Sep 2026): the room
  outlives the match. After the result both seats are back in the same room, same code, Ready reset,
  gear editable; both pressing Ready starts the next match. A seat whose connection is gone at the
  result is freed, so a friend can rejoin by code; a room with no human closes. No session score, no
  offer/accept: the room is the offer.</li>`.
- The drift-note: remove "rematch without leaving the room" from the **Later** list; add
  "rematch in the room" to the implemented list with the date and this spec's path.
- `#presentation` Lobby list: the room bullet gains "a Copy code button beside Copy link"; the result
  bullet gains "and the lobby returns to the *room* after the result banner".
- `#aiming-presentation`: the *blocked* state bullet gains "and the blocking tile is tinted"; the
  props line (`<p class="small">Props render as code-built placeholder boxes…`) becomes "hex prisms
  the size of the hex they block".
- Section `data-impl` values stay honest: `#online` stays `implemented`; the aiming section stays as
  it is.

### 10.2 Other docs

- `docs/networking.md`: room lifecycle paragraph (Waiting → Playing → Waiting …; Over only when
  empty), the `round` and `auth.ok.room` fields, the origins paragraph.
- `docs/hosting.md`: a "Template" section: where it lives, what `WebBuild.cs` sets, that
  `TemplateData/` is unhashed and one-hour cached.
- `docs/roadmap.md`: M2-8 row → done pending verifier; the M2 row mentions the template.
- `docs/deploy-runbook.md` §7: the origin row (§4.4).
- `CLAUDE.md`: nothing, unless a command changed.

### 10.3 ADRs (`docs/decisions.md`)

- **ADR-032 — The room outlives the match.** Context: M2-8. Decision: D1 verbatim, with the two
  consequences a future agent will question — `MatchId` is the room's id and is reused across rounds
  (`round` disambiguates in logs), and a forfeited seat is freed rather than held. Alternatives
  rejected: a `rematch.offer` message pair (a timer and two more states for the same outcome), a
  fresh room per match (the code would change, which is the one thing friends have already shared).
- **ADR-033 — WebSocket origin allow-list lives in `appsettings.Production.json`.** Decision:
  empty list = allow all, so tests and `dotnet run` never need configuration; production names its one
  origin in JSON, not in C# and not in the unit, so a hostname change is a redeploy and not a
  `--setup`. Rejected: an `Origin` check in nginx (it would hide the refusal from the server's log and
  from the tests).

---

## 11. Commit plan (each step green before committing; `git add` only the files you touched)

1. `server: the room outlives the match — Waiting again after the result, forfeited seats freed, round in match.start`
2. `server: auth.ok names the waiting room on resume`
3. `server: WebSocket origin allow-list from Mimas:AllowedOrigins; production names mimas.laststep.cloud`
4. `docs: ADR-032 room outlives the match, ADR-033 origin allow-list; networking.md`
5. `client: NetClient keeps the room; the lobby opens on the room panel after a match; Back to room`
6. `client: Copy code beside Copy link` (T-0005)
7. `core-tests: two arena-4 sight cases that name their blocker` (T-0006 step 1)
8. `client: refused shots tint the blocking tile; props are hex prisms that fill their hex` (T-0006)
9. `client: the Mimas Web template — full-window canvas, no splash, product name Mimas` (with the
   `.meta` files Unity made and `ProjectSettings.asset` as written by WebBuild)
10. `smoke: --browser chromium|webkit|firefox; clipboard step skipped outside Chromium`
11. `design: rematch is rule 9 of #online; lobby and targeting bullets; hosting.md template section`
12. `studio: T-0008 run report, STATE, T-0005/T-0006 closed into T-0008`

Two sessions is fine: 1–4 (server, no Editor) then 5–12.

---

## 12. Done-when, and who proves what

| Rung | Proof | Who |
|---|---|---|
| 0–1 | `dotnet build Mimas.slnx` green; `bash -n` on nothing new (no shell scripts change) | builder |
| 2 | Core tests green, **+2** (§7.1); count in the run report | builder |
| 5 | Server tests green, **+9** (§3.3, §4.3); the two-round match over real sockets | builder |
| 7 | EditMode tests green; `unity command console` clean; the Editor rematch sequence captured (§5.4) | builder |
| 10 | `Build/Web` builds through `WebBuild.Build`; smoke green in chromium, webkit, firefox against the local build with `[timing]` lines; the Copy code clipboard check headed; screenshots §8.4 | builder |
| — | **Rohan deploys**: `bash tools/deploy/deploy.sh` (no `--setup`: no nginx or unit change). Then `--smoke`. Then one bot match at the live URL, a resign, **Back to room**, Ready, a second match. Then a friend, by code, and a rematch — that single evening is M2-1, M2-7 step 4 and M2-8 in one sitting; write it as `studio/playtests/<date>-<name>.md` | Rohan |
| — | **Short session after the deploy** (same task): live smoke ×3 in Chromium with `--timing` (compare with the 21 Sep baseline `7.8 s / 12.3 MB`; the splash removal should show in *expect*, not in *boot*); `curl -sI https://mimas.laststep.cloud/` shows `<title>Mimas</title>`; `curl -i -N` with the upgrade headers and **no** `Origin` on `/ws` is `403`, and with `-H "Origin: https://mimas.laststep.cloud"` is `101`; `journalctl -u mimas-server -n 50` (read-only ssh) shows `round 2` and an `origin … refused` line only if something probed it; `docs/roadmap.md` baseline row updated; task → `verify` | builder |
| — | Verifier ticks M2-8 (and M2-1, M2-7 if the playtest entry exists) through `node tools/ledger.mjs pass` | verifier |

Publish check for §4.1: after `dotnet publish -r linux-x64 --self-contained -c Release` (the exact
command is in `deploy.sh`), `appsettings.Production.json` is in the output folder. Do this in the
server half, before commit 3 is called done.

---

## 13. Defaults for forks the session may hit

| If… | Then… |
|---|---|
| `appsettings.Production.json` is not in the publish output | Add `<Content Include="appsettings.Production.json" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />` to `Mimas.Server.csproj`; say so in the run report |
| `WebSocketClient` in the test host does not surface a 403 as a status | Assert on the exception type and message containing `403`; if the test host bypasses the middleware order so the check never runs, move the check into the `/ws` endpoint delegate itself (it is already inline in `Program.cs`) |
| Keeping `State` referenced in `Waiting` trips a test or a `HandleResync` path | Set `State = null` at match end and add the null guard where the compiler asks; do not restructure |
| The Arena receives `room.state` before `match.events` with the result (ordering on one socket is preserved, so this should not happen) | Log it; `PendingRoom` is set either way and `BackLabel` reads it lazily |
| `LoadoutSettings` does not persist the chosen preset across the scene reload | Default to preset 0; write it in the run report as a small follow-up task, not a fix here |
| A `--do` browser drive of the rematch is not achievable headless in an hour | The Editor sequence (§5.4) and the server tests stand; say so; leave a `--do` recipe that gets as far as it gets |
| Copying the Default template: the Editor path is not where §8.1 says | `unity command eval --code "return UnityEditor.EditorApplication.applicationContentsPath;"` then `PlaybackEngines/WebGLSupport/BuildTools/WebGLTemplates/Default` under it |
| `PlayerSettings.SplashScreen.show = false` throws or is ignored in this Editor | Read the 6000.4 docs page in §8.3; if the API is licence-gated after all, leave the splash on, keep the rename, record the API's message verbatim, and mark D3 partially done |
| `PlayerSettings.WebGL.template = "PROJECT:Mimas"` is not honoured by the batch build | Check the template folder name matches exactly and that `Assets/Refresh` ran; the value must be `PROJECT:<folder>` |
| The sight reproduction shows the *rule* is wrong (a refusal the design page does not describe) | Do not change Core. Add an open question to `#line-of-sight` (`q-los-…`) with the case, and note `data-impl="drift"` is **not** needed since code matches the page; the picture fix still ships |
| WebKit refuses to launch on Windows (`npx playwright install webkit` incomplete) | Run `npx playwright install-deps` is Linux-only; on Windows re-run the install; if it still fails, record the error, ship `--browser` with Chromium and Firefox proven, and leave WebKit for the Part-3-style session |
| Firefox headless has no WebGL2 (software GL) | Pass `--headed` for Firefox and say so; the point is Brotli and the socket, not the GPU |
| `HexMeshFactory` has no extrude helper | Build the prism in `PropView`: the six outline vertices at y=0 and y=bodyHeight, side quads, a top cap; ~40 lines, flat normals |
| The design page edit is blocked by the guard even with `allows_assets` | Use the Edit tool (the guard is a Bash hook); if Edit is refused too, stop, write the exact `<li>` text in the run report for Rohan to paste |

---

## 14. Out of scope

The best-of-three session wrapper and any score in the room; ratings; accounts; spectating; chat; a
Mimas logo (text wordmark only); a mobile layout; the WebGPU or 6.7 template work; rules of sight and
trajectory; real prop art; a rematch *offer* flow; a nginx-level origin check; anything on the VPS
beyond what `deploy.sh` already does; OQ-N16 (auto end turn at 0 AP) — still Rohan's.
