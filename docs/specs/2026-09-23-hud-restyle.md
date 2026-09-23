# Spec H: the HUD in the ink language — the bar, the turn, the draft, the moments, the room

_Status: **work order for autonomous Opus sessions**, two tasks in order: **`T-0013`** (the match HUD, the
between-rounds screens, the examine plate in ink, and one wire field) and **`T-0014`** (the lobby and the
room). Lane full. Written 23 Sep 2026 against `main` at `a0c5a68` plus the uncommitted docs of the same
evening (461 Core tests, 58 server tests, 24 EditMode tests; Web build 12.57 MiB against the 13 MB ratchet;
T-0010, T-0011, T-0012 at verify). The design was chosen by Rohan on 23 Sep 2026 over three rounds on the
design canvas (https://claude.ai/artifact/K9qZcJQ115G5G685sdMAwy, page "HUD", newest round at the top) and is
written down as the UI book: **`docs/ui/hud.md`**, **`docs/ui/between-rounds.md`**, **`docs/ui/lobby.md`**, the
amended **`docs/ui/language.md`** and **`docs/ui/examine.md`** (rows marked *ink*). ADR-039 (seen by the
opponent), ADR-040 (one surface; shared models). Design anchors: `#hud`, `#draft`, `#examine`, `#hidden-info`,
`#presentation`, `#online`, `#turns`, `#time-controls`, `#aiming-presentation`, `#camera`. Predecessors:
`docs/specs/2026-09-23-examine-panel.md` (the tokens, glyphs, fonts and hover panel this builds on),
`docs/specs/2026-09-22-boons-in-game.md` (the draft and the moments this restyles)._

---

## 0. How to run a session

Read this whole file, then `CLAUDE.md`, then `studio/STATE.md` ("Things the next agent must not rediscover"
is written for you), then **the five UI book pages in full** (they are the design; this spec is the work
order), then look at the canvas boards each page names (read them with the Artifact tool, `action: "read"`
with a board's `path`, e.g. `project/L-A-Armed.dc.html`; they are HTML), then the design sections named above
(grep the anchors, never the whole page), then ADR-017, ADR-018, ADR-019, ADR-026, ADR-036, ADR-037, ADR-039,
ADR-040. Read every file you touch in full before editing it — in particular, for T-0013:
`shared/Mimas.Core/Runtime/Match/PlayerView.cs`, `Match/MatchState.cs` (`_revealed`, `Knows`, `KnowsBoon`,
`KnowsLineage`, `FromView`, `ViewFor`), `Match/EventFilter.cs`, `Session/Session.cs`, `Session/SessionView.cs`,
`Protocol/Wire.cs` (`View`, `ReadView`, `Session`, `ReadSession`); `MimasClient/Assets/_Game/UI/MatchHud.uxml`,
`MatchHud.uss`, `MatchHudView.cs`, `ExamineView.cs`, `Examine.uss`, `Examine.uxml`, `HoverPanel.uxml`,
`HoverPanel.uss`, `Theme.uss`, `Glyphs.cs`; `Presentation/Match/IMatchHudSource.cs`, `MatchSession.cs`,
`ExamineModelBuilder.cs`, `IMatchDriver.cs`, `OnlineMatchDriver.cs`, `LocalMatchDriver.cs`; for T-0014:
`UI/Lobby.uxml`, `Lobby.uss`, `LobbyView.cs`, `WebClipboard.cs`, `Presentation/Match/LoadoutPresets.cs`.

Rules for the session:

1. **Autonomous.** No questions. Forks have defaults in §13.
2. **One wire field, no rule.** T-0013 adds exactly one piece of knowledge to the owner's view (§4, ADR-039)
   and nothing else to Core, the wire or the server. No rule changes. If you find yourself wanting another
   wire field, stop and take the §13 default. T-0014 touches no Core, wire or server file at all.
3. **The design is `docs/ui/`, not your taste.** Every element in the book's inventories exists by its `id`
   as the UXML `name`; every colour, face, size and gap is a `--mimas-*` token declared once in `Theme.uss`.
   No literal colour, no `--hud-*` variable and no `--lobby-*` variable survives in `MatchHud.uss` or
   `Lobby.uss`. Where the book and this spec disagree, this spec wins and you note it in the run report;
   where a canvas board and the book disagree, the book wins.
4. **Behaviour is kept.** Package D's click rules, the examine plate's open/close rules, the draft's timer and
   pick rules, the resign's two clicks, the lobby's state machine: all unchanged. This is a restyle plus the
   data the new look needs.
5. **Commits:** small commits to `main`, `area: what`, in the order of §12. Do not push. Before every commit:
   `dotnet build Mimas.slnx`, `dotnet test shared/Mimas.Core.Tests`, `dotnet test server/Mimas.Server.Tests`,
   all green; after every client commit: `unity command menu --path "Assets/Refresh"`, then `unity command
   recompile_status` → `completed` with no errors, then `unity command console` clean (pipe it through
   `grep -o '"groundTruth":{[^}]*}'`).
6. **Golden rules** 1, 2, 3, 4, 6, 7, 8, 9, 11 apply. Never hand-make, hand-edit or hand-delete a `.meta`, a
   `.asset` or a `.unity`: fonts are deleted and the Arena camera is changed **through the live Editor**
   (§11). No new packages. The Web build stays under the 13 MB ratchet (it should shrink: §7.9).
7. **Declare before you commit.** The task's `allows_assets` lists what the Editor will write or delete. If
   the Editor touches something the list does not foresee, add it to the task file in the same commit and say
   so in the run report. Keep the task `running` for any rework after it reaches `verify` (STATE).
8. **Evidence is screenshots.** §14 lists the states; each is a capture under `MimasClient/Assets/_Shots/`
   (never a `Temp` folder), copied to `artifacts/t0013/` (or `t0014/`) and named in the run report. The
   verifier holds them against the canvas boards named in the book. Never write "matches the mock" without
   the file.
9. Open the run report `studio/runs/R-<date>-T-0013.md` (or `-T-0014`) before the first edit and append as
   you go.

---

## 1. Goal and result

**What a player sees afterwards (T-0013).** The board floats in ink. Top centre, a turn track: your name and
two series pips on the left, theirs on the right, a marker on the half whose turn it is; in the last ten
seconds that half burns towards the centre. Bottom left, action points as a bolt, a light numeral and eggs,
then three captioned rows of 44px tiles — Movement, Weapon, Spell — each tile with its cost as dots, a
violet edge and diamond when an Enchant changed it, a violet edge and triangle when a Sigil added it, and a
small closed eye while the opponent has not yet seen it. Your boons are glyph circles down the right edge.
Resign sits above End Turn bottom right; End Turn lights when nothing is left to spend. Resting on a tile
opens the one hover panel above it; arming an attack opens it over the target with the damage the rules will
do, the reason first if the shot is refused. Unit tags are a light number over a 2px bar, theirs carrying a
mark per boon. Between rounds, three ink cards with the lineage's painted wash; the round card and the
results sit in a dark band across the middle. The examine plate is ink, in capitals, like everything else.

**What a player sees afterwards (T-0014).** The lobby is one ink column: MIMAS, the last result, a name,
Play vs bot, Create room, a code and Join. The room puts you on the left in teal — your name, four gear tiles,
three lineages — and them on the right in vermilion with only their name and whether they are ready, the
code large between you at the top, Ready at the foot.

**What exists afterwards in the repo.**

- `KnownEntry.SeenByOpponent` and `UnitView.LineageSeenByOpponent` in Core, on the wire, in the mirror, tested
  in Core and over sockets (§4, §5, §9).
- `UI/HoverPanelView.cs` (the one hover panel, shared, with three placements) and `UI/Ramps.cs` (the
  generated gradient textures USS cannot draw); `ExamineView` using both.
- `MatchHud.uxml` / `MatchHud.uss` rebuilt to `docs/ui/hud.md` and `between-rounds.md`; `MatchHudView.cs`
  rendering them; `IMatchHudSource` carrying the data of §7.2; `Examine.uss` on ink; the three Cormorant font
  assets gone; the Arena camera clearing to ink (T-0013).
- `Lobby.uxml` / `Lobby.uss` rebuilt to `docs/ui/lobby.md`; `LobbyView.cs` with preset tiles and you-left
  seats (T-0014).
- EditMode tests for the new presenter logic (§9), screenshots (§14), docs (§10).

**Not in scope** (write them as follow-ups in STATE, do not build them): action icon art (tiles keep the
letter), portraits, a status-effect column, a "disabled by an effect" tile state, remembering the preset
across scene loads, the full character select, `RevealedAt` for the enemy's reveal order, any rule.

---

## 2. Decisions locked on 23 Sep 2026 (do not reopen)

| # | Decision | Where it bites |
|---|---|---|
| H1 | **Layout A, "as drawn"** (Rohan's Draft 1): track top centre; AP and three captioned lane rows bottom left; your boons as glyph circles down the right edge; Resign above End Turn bottom right | §7.4 |
| H2 | **The turn track**: your end left, theirs right, series pips at each end, a marker on the active half, the active half burning towards the centre in the rope seconds | §7.5 |
| H3 | **Your own actions the opponent has not seen carry a mark** (a closed eye; the struck eye already means "no sight needed") | §4, §7.4 |
| H4 | **Cost is dots inside the tile**, never a digit | §7.4 |
| H5 | **The attack preview is the one hover panel over the target**: total in `amount`, the rules' lines, one `?` row; a refused shot leads with the reason | §7.7 |
| H6 | **Draft = ink cards** with the lineage wash; the selected one lifts and takes the `you` edge; "HAS CHOSEN" at their end of the track replaces the green dot | §7.8 |
| H7 | **Round card, round result, series result and match lost sit in the band** | §7.8 |
| H8 | **The void is ink**: the Arena camera clears to `#0b0b0e` | §7.10 |
| H9 | **One surface: ink.** The examine plate goes ink; the serif leaves the build | §7.9 |
| H10 | **The room: two seats facing**, you always left; **gear as preset tiles**, no dropdown | §8 |
| H11 | **Two tasks**: T-0013 (everything a friend plays in) first, T-0014 (lobby, room) second | §12 |
| H12 | Small calls made in the session and not questioned by Rohan: End Turn lights at no affordable action; your bar at 45% on their turn; the draft timer is the same rope; the disconnect line sits under their end of the track; the turn number ("YOUR TURN · 1") goes | §7.4, §7.5 |

---

## 3. Architecture (read before the sections that follow)

- **Knowledge, not a rule.** The server already knows what each seat has learned about the other
  (`MatchState._revealed`, carried across rounds by `Session`, ADR-018/035). T-0013 copies the opponent's
  knowledge of *your own* unit onto *your own* view entries. The owner cannot compute it (reveal events about
  their unit go only to the opponent, `EventFilter`; the observation rule needs the opponent's knowledge; a
  boon reveal cascades silently), so it must come from the view. The local practice driver reads
  `ViewFor(Local)` on the real state and gets it for free; the online driver gets it through `Wire.ReadView`.
- **One hover panel, three placements.** `HoverContent` and `HoverPanel` move out of `ExamineView.cs` into
  `UI/HoverPanelView.cs` unchanged in behaviour, gaining a placement: `Left` (today: left of an anchor, for the
  plate and the boons column), `Above` (over an action-bar tile), `Over` (over a unit tag, for the preview).
  One instance still lives in `hover-mount`; the HUD and the plate share it. The content builders
  (`TileContent`, `BoonContent`, `StatContent`, `ItemContent`) move with it as statics so the bar builds a
  tile's panel exactly as the plate does.
- **One tile model.** The bar's `HudAction` carries the examine builder's `HudTile` for the same ability
  (`HudAction.Hover`), so an action cannot say one thing on the bar and another on the plate (ADR-040).
- **The presenter composes; the view formats.** `MatchSession` builds every model (names, scores, moments,
  marks); `MatchHudView` never touches Core. Pure composition helpers go in a static `HudModel` class under
  `Presentation/Match/` so EditMode tests can reach them without a scene.
- **Gradients are generated textures.** USS has no gradients. `UI/Ramps.cs` generates small white-alpha
  textures once (cached, destroyed with their owner) — a vertical ramp for the floors, a horizontal
  both-ends ramp for the band, the lineage painting (moved from `ExamineView`) for the plate and the draft
  cards — and the USS tints them with `-unity-background-image-tint-color: var(--mimas-…)`, the way the
  plate's wash already works.
- **Scaling** is the panel settings' 1920×1080 scale-with-screen-size (match 0.5), shared by the HUD and the
  lobby. Every px in the book is a 1920×1080 px; 1280×720 draws it at two thirds.

---

## 4. Core changes (`shared/Mimas.Core/Runtime`) · T-0013 only

### 4.1 `KnownEntry.SeenByOpponent`, `UnitView.LineageSeenByOpponent`

```csharp
// Match/PlayerView.cs
public sealed class KnownEntry   // (or struct — keep its current kind)
{
    // … existing members …
    /// <summary>
    /// On the viewer's own unit only: whether the opponent has already seen this entry (design #hidden-info,
    /// ADR-039). Always false on an enemy unit's entries.
    /// </summary>
    public bool SeenByOpponent { get; }
}
public sealed class UnitView
{
    // … existing members …
    /// <summary>On the viewer's own unit only: whether the opponent knows its lineage. False on an enemy unit.</summary>
    public bool LineageSeenByOpponent { get; }
}
```

- Add constructor overloads; existing call sites keep compiling with `false`.
- `PlayerView.Build`, **only when the unit is the viewer's own** (`mine`), with `opp = 1 - viewer`:
  abilities `state.Knows(opp, unit.Id, id)`; modifiers `!hidden || state.Knows(opp, unit.Id, id)` (a public
  modifier is seen by construction); boons `state.KnowsBoon(opp, unit.Id, id)`; lineage
  `state.KnowsLineage(opp, unit.Id)`. Use the exact query methods `MatchState` exposes through
  `IRevealedKnowledge`; do not add new ones.
- `MatchState.FromView`: for the viewer's own unit, import one revealed entry `(opp, unit.Id, id)` for every
  entry flagged seen — with the same `"boon:"` / `"lineage:"` id prefixes the store uses internally — so a
  mirror's `ViewFor(viewer)` reproduces the view byte for byte (the existing
  `Mirror_ViewFor_ReproducesSourceView` and the boon-reveal mirror asserts must stay green untouched).
  This does not change a preview: `Knowledge.CanSee` is already true for an owner, and a mirror never applies
  commands.

### 4.2 Nothing else

No new rule, no new event, no change to `EventFilter`, `Session`, `SessionView` or the bot.

---

## 5. Wire and server · T-0013 only

- `Wire.View`: on the viewer's own unit, each ability, modifier and boon entry gains `"seen": true|false`
  and the unit gains `"lineageSeen": true|false`. On an enemy unit, **no key is written** (the enemy's JSON is
  byte-identical to today's).
- `Wire.ReadView`: for a unit with `mine: true`, read both as required booleans (a missing key throws
  `WireException`, the codec's rule); for an enemy unit, `false` without reading.
- Old clients ignore unknown keys (`Wire.cs` reads by name), so a pre-T-0013 client keeps working against a
  new server; a new client against an old server throws on the first view, which is acceptable because both
  ship together (the Web build and the server deploy in one `deploy.sh`).
- **Server code: none.** `Room` builds views through `SessionView.For` → `PlayerView.Build`, so the flag
  arrives by itself. Server **tests** are added (§9).
- `docs/networking.md`: the two keys in the view's unit table, with ADR-039.

---

## 6. Data

None. The draft card's wash reads `hueDark` / `hueLight` from `lineages/*.json`, which T-0011 added.

---

## 7. Client · T-0013 (`MimasClient/Assets/_Game`, presentation only)

### 7.1 Files

| File | New / changed | Holds |
|---|---|---|
| `UI/Theme.uss` | changed | the new tokens of `language.md` (§7.3); the three serif faces removed; the paper block removed once nothing reads it |
| `UI/HoverPanelView.cs` | new | `HoverContent`, `HoverPanel` (moved from `ExamineView.cs`), `HoverPlacement { Left, Above, Over }`, the static content builders |
| `UI/Ramps.cs` | new | `Ramps.Vertical`, `Ramps.BothEnds`, `Ramps.Painting` (moved from `ExamineView`), all cached |
| `UI/ExamineView.cs` | changed | uses `HoverPanelView` and `Ramps`; the ink fade (§7.9) |
| `UI/Examine.uss` | changed | ink tokens and caps faces (§7.9) |
| `UI/Fonts/CormorantGaramond-*` (3 `.ttf`, 3 SDF assets), `UI/Fonts/OFL-CormorantGaramond.txt`, their `.meta` | **deleted through the Editor** | §7.9 |
| `UI/MatchHud.uxml` | rewritten | every id of `hud.md` §3 and `between-rounds.md` §2–§3; `examine-mount` and `hover-mount` stay last |
| `UI/MatchHud.uss` | rewritten | `@import url("Theme.uss")` first; only `--mimas-*`; the `--hud-*` block deleted |
| `UI/MatchHudView.cs` | changed | renders the new tree (§7.4–§7.8); the bar-centring code goes (the bar is left-anchored) |
| `UI/Glyphs.cs` | changed | one path, `eye-closed` (§7.6) |
| `Presentation/Match/IMatchHudSource.cs` | changed | the contract of §7.2 |
| `Presentation/Match/MatchSession.cs` | changed | composes it |
| `Presentation/Match/HudModel.cs` | new | pure helpers of §7.2, for EditMode tests |
| `Presentation/Match/ExamineModelBuilder.cs` | changed | `Tile` becomes `internal`; `HudTile.UnseenByThem` on own tiles |
| `Presentation/Match/IMatchDriver.cs`, `OnlineMatchDriver.cs`, `LocalMatchDriver.cs` | changed | `MyName` beside `OpponentName` |
| `Editor/HudPreview.cs` | new | the `Mimas/HUD/Preview …` menu items of §11 |
| `Scenes/Arena.unity` | changed **through the Editor** | `Main Camera`: Solid Color, `#0b0b0e` (§7.10) |
| `Tests/EditMode/HudModelTests.cs` | new | §9 |

### 7.2 The contract (`IMatchHudSource`)

Add:

```csharp
string MyName { get; }                         // online: the seat name (NetClient.PlayerName); practice: "You"
int ScoreMine { get; }  int ScoreTheirs { get; } // from SessionView.Score0/Score1 by seat
int RoundsToWin { get; }                       // SessionView.RoundsToWin (2 at best of 3)
int RoundNumber { get; }                       // the round being played or just finished
HudPhase Phase { get; }                        // Round, Draft, Over
HudMoment Moment { get; }                      // null when no moment is showing
IReadOnlyList<HudBoon> MyBoons { get; }        // own boons, grant order, the examine builder's HudBoon
```

```csharp
public enum HudPhase { Round, Draft, Over }
public enum HudMomentKind { RoundStart, RoundResult, SeriesResult, MatchLost }
public sealed class HudMoment
{
    public HudMomentKind Kind;
    public int Round; public string MapName; public bool IMoveFirst;        // RoundStart
    public bool IWon; public string WinnerName; public int ScoreMine, ScoreTheirs; public string Reason; // results
    public bool ShowBack; public string BackLabel;                           // SeriesResult, MatchLost
}
public sealed class HudBoonMark { public string Kind; }                     // null Kind = unrevealed
```

Change:

- `HudAction` gains `bool Added` (a Sigil grant: `Unit.BoonOfAbility(id) != null` on the mirror),
  `bool UnseenByThem` (the own `KnownEntry.SeenByOpponent == false`), `HudTile Hover` (the examine builder's
  tile for that ability, with `UnseenByThem` set). Keep `Modified` (the violet edge; it means "a boon changed
  a number", as on the plate).
- `HudPreview` gains `string AbilityIcon`, `string TargetName`, `string TrajectoryWord` ("lobbed",
  "straight", "from the sky"). The hidden line's label becomes **"A boon of theirs you have not seen"** (one
  row, `Unknown = true`, whatever the count).
- `HudUnit` gains `List<HudBoonMark> BoonMarks` (enemy units only: one per boon in grant order, revealed kind
  or null).
- `HudDraftCard` gains `string HueDark, HueLight, Slot` (`Slot` from the boon's `requires.slot`, null for a
  Blessing).
- `HudDraft.Headline` is composed as "ROUND 2 TO GUEST-2869 · 1 – 1" (the round, its winner, your score first).
- `HudTile` gains `bool UnseenByThem`.

Remove (nothing else reads them after §7.4–§7.8): `SeriesLine`, `Banner`, `BannerDetail`, `ShowBackToLobby`,
`BackLabel`, `TurnNumber` (keep `BackToLobby()`).

`HudModel` (static, pure, tested): `LitFraction(float remaining, float ropeSeconds)` (1 outside the rope,
`remaining / ropeSeconds` inside, clamped 0–1); `IsDone(bool isMyTurn, IReadOnlyList<HudAction>)` (your turn
and no action affordable); `Moment…` builders from plain values (round, map, first mover, winner seat, local
seat, scores, reason); `ScoresBySeat(score0, score1, localSeat)`.

### 7.3 Tokens (`Theme.uss`)

Add, from `language.md`: `--mimas-text-56: 56px`; `--mimas-band: rgba(11, 11, 14, 0.86)`;
`--mimas-scrim: rgba(11, 11, 14, 0.80)`; `--mimas-floor: rgba(11, 11, 14, 0.86)`;
`--mimas-floor-top: rgba(11, 11, 14, 0.62)`; `--mimas-button-w: 230px`; `--mimas-button-h: 58px`;
`--mimas-track-w: 620px`; `--mimas-card-w: 360px`; `--mimas-card-h: 470px`; `--mimas-edge: 40px`;
`--mimas-you-10`, `--mimas-you-14`, `--mimas-you-18`, `--mimas-you-28` (the `you` colour at those alphas, for
`state.armed`, the primary buttons, `state.done`, `state.spends`); `--mimas-ink-55: rgba(11, 11, 14, 0.55)`
(the ground under End Turn and the boon circles); `--mimas-ink-82: rgba(11, 11, 14, 0.82)` (the cursor tag). Remove `--mimas-font-serif*`. Remove the `--mimas-paper*`
block and `--mimas-paper-hover` after §7.9 (grep first: nothing may read them). Update the header comment:
one surface.

### 7.4 The bar, AP, boons, End Turn, Resign (`hud.md` §3.2–§3.4)

- `hud.ap` and `hud.lanes` sit in one row at `left: --mimas-edge; bottom: --mimas-edge`, 44 apart; the AP
  block is `hud.ap.caption`, `hud.ap.value` (a `Glyph` bolt, two labels), `hud.ap.eggs` (`Glyph` eggs, as the
  plate draws them). `hud.lane.movement`, `.weapon`, `.spell` are rows 12 apart, each a 120-wide caption
  ("MOVEMENT", "WEAPON", "SPELL") and a row of tiles 12 apart. The row order and the unknown-category rule
  are today's. The bar keeps its rebuild-on-signature behaviour.
- `hud.tile[<id>]`: 44 square, `tile` ground, 1px `hair` edge; `.hud-tile__letter` (today's letter, keeps
  the `icon--<key>` class), `.hud-tile__cost` (one 5px `you` dot per AP, 3 apart, 5 above the bottom edge),
  `.hud-tile__mark` (a 10px filled `Glyph` diamond or triangle in `changed` at the top-right, 5 outside),
  `.hud-tile__eye` (an 11px `Glyph` `eye-closed` in `fg-3` at 4, 4). Classes: `hud-tile--armed`,
  `--unaffordable` (the whole tile at 0.36, dots `fg-3`), `--changed`, `--added`, `--unseen`. Fix the
  specificity trap: the armed edge wins over the changed edge.
- `state.spends`: while a tile is hovered (affordable) or armed, the last `cost` held eggs take the
  `hud-egg--spends` class (`you` edge, `--mimas-you-28` fill).
- Hover: `HoverPanelView` with `Above`, 120 ms delay as on the plate, content from `HudAction.Hover`, plus the
  row "They have not seen this yet." with the closed eye when `UnseenByThem`. The old `#tooltip` is deleted.
- `hud.boons`: at `right: --mimas-edge; top: 380px`, one `hud.boon[i]` per `MyBoons` entry: 40 circle,
  1px `hair` edge, `--mimas-ink-55` ground, the kind `Glyph` 14 in `you` (filled when `Starting`), 12 apart;
  hover `Left`, `BoonContent`.
- `hud.end`: `--mimas-button-w` × `--mimas-button-h` at `right/bottom: --mimas-edge`; states per `hud.md`
  §3.4 (`hud-end--done` when `HudModel.IsDone`; "THEIR TURN" when not your turn). `hud.resign` 16 above it,
  the two-click rule and its 3 s window unchanged, the armed text "CLICK AGAIN TO RESIGN" in `them`.
- The whole bar takes `hud-bar--theirs` (opacity 0.45) when `!IsMyTurn` during a round.
- The floors: two elements under everything but the unit layer, `hud.floor` (bottom, 300 tall,
  `Ramps.Vertical` tinted `--mimas-floor`) and `hud.floor.top` (220 tall, tinted `--mimas-floor-top`).

### 7.5 The turn track (`hud.md` §3.1)

`hud.track` at `top: 38px`, centred, `--mimas-track-w`. Names and pips per the book; the pips are
`RoundsToWin` 7px circles per side. The line is three elements: the `hair` line, `hud.track.fill` (a child
positioned on the active half) and the centre tick. Outside the rope, the fill covers the active half and
`hud.track.marker` sits at its middle. Inside the rope, the fill **touches the centre tick and shortens from
its outer end** — width `50% × HudModel.LitFraction(...)`, anchored at the centre — and `hud.track.ember` sits
at its outer end, travelling inward; the marker hides. (The canvas board drew the lit part from the outer end;
the book's "towards the centre" wins: the fire reaching the centre is the turn crossing over.) Between rounds
(`Phase == Draft`) nothing is lit and `hud.track.round` reads "DRAFT"; on the series result, "SERIES".
`hud.track.status` and `hud.track.picked` sit under their end, right-aligned.

### 7.6 Glyph

`Glyphs.cs` gains `eye-closed`: a lid arc and three short lashes on the 24 grid, round caps, e.g.
`M3 10c2.5 3 5.5 4.5 9 4.5s6.5-1.5 9-4.5M7 13.5l-1.5 2.5M12 15v3M17 13.5l1.5 2.5`. It is never used for "no
sight needed" (that stays `eye-struck`).

### 7.7 Tags, preview, flyovers, cursor (`hud.md` §3.5, §3.6, §5)

- `hud.tag[u]`: `.hud-tag__number` (display 19 · "/ max" 11 `fg-3`), `.hud-tag__bar` (84 × 2 on ink at 50%,
  fill in the owner colour, `ghost` at the fill's right end in `amount`), `.hud-tag__marks` (enemy: a 9px
  filled kind `Glyph` in `them` per revealed mark, a 10px dashed `?` `Glyph` per unrevealed), `.hud-tag__lineage`
  (enemy, once revealed), the ring as built. The 4-HP segments go. World anchoring in `LateUpdate` is kept.
- `hud.preview`: the shared hover panel with `Over`: anchored to the target tag's `worldBound`, 18 above it,
  centred on it, flipped below the tag when it would leave the window. Content per `hud.md` §5 from
  `HudPreview`; the struck reason mark is the existing `close` path in `them`.
- `hud.fly`: headline display 36 in `amount` for damage; a reveal is the kind `Glyph` in `them`, caps 13
  headline, caps 10 `fg-3` detail. The animation is today's.
- `hud.cursor`: caps 12 `fg` on `--mimas-ink-82`, 9 × 14 padding.

### 7.8 The draft and the moments (`between-rounds.md`)

- `dr.*` per the book. The card is a `Button` (keyboard focus) with a `.dr-card__wash` child filled with
  `Ramps.Painting(hueDark, hueLight, ink-2)` 130 tall; the lift is `translate: 0 -14px` on
  `dr-card--selected`. After the pick the other cards take `dr-card--dim` (opacity 0.45) and all are
  disabled. `dr.timer` is the rope language on a 520 line. `#draft-dot` is deleted; `hud.track.picked`
  replaces it.
- `mo.*`: one `mo.root` with `mo.band` (full width, 300 tall at the vertical centre, `Ramps.BothEnds` tinted
  `--mimas-band`, fading over the outer 22%), `mo.kicker`, `mo.title`, `mo.score` (three labels),
  `mo.sub`, `mo.back`. Kinds per the book, colours by `IWon` / `IMoveFirst`, never by the text. Timing is
  today's (the round card clears after 1.5 s; the result holds until the draft opens 2 s later).
  `#banner-panel` is deleted.

### 7.9 The examine plate in ink (`examine.md`, rows marked *ink*)

- `Examine.uss`: every `--mimas-paper-X` becomes `--mimas-X` (`paper` → `ink`, `paper-fg` → `fg`, `paper-fg-2`
  → `fg-2`, `paper-fg-3` → `fg-3`, `paper-hair` → `hair`, `paper-hover` → `hover`, `paper-tile` → `tile`,
  `paper-you` → `you`, `paper-them` → `them`, `paper-up` → `up`, `paper-down` → `down`, `paper-changed` →
  `changed`); the slot glyph on the item square keeps a dark colour (`--mimas-ink`).
- Faces: `.ex-name` caps 28 at `--mimas-caps-tracking-wide` (was serif 36); `.ex-lineage` caps 12 in the
  accent (was serif italic 17; the text becomes upper case: "GREEK · YOUR HERO"); `.ex-caption` caps 11 in the
  accent (was serif semibold 17); `.ex-boon-name` caps 14 (was serif 19); `.ex-item-name` caps 15 (was
  serif 20).
- `ExamineView`: the painting fades into the plate's resolved background (already); its fallback literal
  becomes ink `#0b0b0e`; the neutral (unknown-lineage) wash blends towards bone at 0.10 and 0.05 instead of
  towards ink.
- The hover panel on `ink-2` over the ink plate stands apart by its 2px edge and the shadow layer; nothing
  to change.
- **Fonts:** with the Editor open, delete through the Editor (`AssetDatabase.DeleteAsset` from `unity
  command eval`, or the MCP `delete_asset`) the three `CormorantGaramond-*.ttf`, their three `SDF` assets and
  `OFL-CormorantGaramond.txt`; the Editor removes their `.meta`. Remove the three `--mimas-font-serif*` lines
  from `Theme.uss` in the same commit. Report the Web build size before and after.

### 7.10 The void

Through the live Editor, in `Arena.unity`: `Main Camera` → `Camera.clearFlags = CameraClearFlags.SolidColor`,
`backgroundColor = new Color32(11, 11, 14, 255)`; save the scene. `RenderSettings` (the ambient light from the
default skybox) is untouched, so the board's lighting does not change. The Lobby scene needs nothing (its
background is the USS in T-0014).

---

## 8. Client · T-0014 (`docs/ui/lobby.md`)

| File | New / changed | Holds |
|---|---|---|
| `UI/Lobby.uxml` | rewritten | every id of `lobby.md` §2–§3 |
| `UI/Lobby.uss` | rewritten | `@import url("Theme.uss")` first; only `--mimas-*` (the `--lobby-*` block deleted); radius 0 everywhere |
| `UI/LobbyView.cs` | changed | preset tiles, you-left seats, lineage rows with child labels |
| `UI/RoomLayout.cs` (beside `LobbyView`, same assembly; no new folder) | new | pure helpers for §9 |
| `Tests/EditMode/RoomLayoutTests.cs` | new | §9 |

- **Preset tiles.** `room.gear.tiles` is a container; `LobbyView` builds one `Button` `room.preset[i]` per
  `LoadoutPresets` entry (weapon slot glyph + boots slot glyph over the name) and keeps an `int _presetIndex`.
  Every use of the `DropdownField` (`_preset.index`, `SetEnabled`, the value callback, the required-element
  check) moves to the index and the tile array, following the `_lineageButtons` pattern. A click selects and
  un-readies, as the dropdown's change did. While ready, the tiles are disabled.
- **Lineage rows.** `room.lineage[i]` is a `Button` with child elements (square with the hue painting and the
  emblem `Glyph`, name label, line label, the Blessing row) instead of one `\n` string.
- **Seats.** `room.you.*` always shows `_mySeat`; `room.them.*` shows `1 - _mySeat`. Guard the other seat's
  JSON for null (today's line casts and dereferences without a check). The bot seat reads "RANDOM BOT",
  "READY".
- **Background.** `.lobby-root` is `--mimas-ink` with `lb.wash`, a full-screen element filled with a generated
  texture of the three lineage washes (`Ramps.Painting`-style radial washes at 28–35%, cached).
- Strings are today's, in the book's case (caps labels upper case, sentences as today).

---

## 9. Tests (one behaviour each, `Method_Scenario_Expected`)

**Core, T-0013** — one new file, `shared/Mimas.Core.Tests/SeenByOpponentTests.cs` (declared in
`allows_assets`; no existing test file is edited):

1. `PlayerView_OwnAbility_NotSeenByOpponentBeforeUse`
2. `PlayerView_OwnAbility_SeenByOpponentAfterUse`
3. `PlayerView_OwnSigil_UseMarksBoonAbilityAndLineageSeen`
4. `PlayerView_OwnStatBlessing_SeenByOpponentAtRoundStart`
5. `PlayerView_OwnPublicModifier_CountsAsSeen`
6. `PlayerView_EnemyEntries_NeverFlaggedSeen`
7. `PlayerView_SeenFlag_EqualsOpponentKnowledge` (a seeded random-bot sweep, every own entry, both seats)
8. `Session_SeenByOpponent_CarriesIntoNextRound`
9. `Wire_OwnSeenFlags_RoundTrip`
10. `Wire_EnemyUnit_CarriesNoSeenKeys`
11. `Wire_OwnEntryWithoutSeen_Throws`
12. `Mirror_FromView_ReproducesSeenFlags`

**Server, T-0013** — `server/Mimas.Server.Tests` (not protected), a new file `SeenByOpponentTests.cs`:
`SeenFlags_OverSockets_OnlyOnOwnUnit`; `SeenFlags_OverSockets_FlipAfterOpponentSeesAbility` (two
`MirrorPlayer`s; seat 0 uses an ability; seat 0's next view flags it seen, seat 1's view of seat 0 carries no
key).

**EditMode, T-0013** — `Tests/EditMode/HudModelTests.cs`:
`LitFraction_OutsideRope_IsOne`; `LitFraction_InsideRope_ShrinksToZero`; `IsDone_NoAffordableAction_True`;
`IsDone_TheirTurn_False`; `ScoresBySeat_Seat1_YoursFirst`; `Moment_RoundResult_WinnerIsMe_IWonTrue`;
`Moment_RoundStart_OpponentFirst_IMoveFirstFalse`; `Action_OwnUnusedAbility_UnseenByThem` (through
`ExamineModelBuilder` with a `PlayerView` fixture, as `ExamineModelTests` builds them);
`Action_SigilGrant_Added`; `Preview_HiddenLine_OneRowWithTheLanguageWording`.

**EditMode, T-0014** — `Tests/EditMode/RoomLayoutTests.cs`: `Sides_Seat0_YouLeft`; `Sides_Seat1_YouLeft`;
`PresetTiles_IndexSurvivesUnready`; `OtherSeat_Missing_ShowsWaiting`.

---

## 10. Docs and design

T-0013, in its **first** commit (before any code, rule 11), through the declared allowance on
`docs/design/index.html`:

- `#hud`: the layout bullets ("Action bar bottom-centre … Turn owner and rope top-centre …") point at
  `docs/ui/hud.md` with a `proposed` badge, as `#examine` points at its page; the "Series line in the turn
  panel" bullet becomes the track's series pips; the rest of package D's behaviour stays.
- `#draft` rule 8: "A green dot says the opponent has picked" → "their end of the turn track says HAS CHOSEN";
  the cards → `docs/ui/between-rounds.md`.
- `#examine`: the plate is ink (the paper plate was the 23 Sep morning's choice).
- `#hidden-info`: one proposed rule: "You see which of your own abilities and boons the opponent has already
  seen — a mark on your own action tiles, never text. It tells you nothing about them." ADR-039.
- `#presentation` → Lobby: two seats facing, gear as preset tiles (this line can land with T-0014's docs
  commit instead; either way before T-0014's code).

At the end of each task: the UI book pages' statuses `proposed` → `built` (rows that drifted get a drift note);
`language.md` rows of this session → `built`; `docs/architecture.md` client table (`HoverPanelView`,
`Ramps`, `HudModel`, `RoomLayout`); `docs/networking.md` (§5); STATE rewritten; the run report.

---

## 11. Editor workflow, in order

1. `unity status --format json` → `ready`; if no Editor, `unity open MimasClient` and wait (~1 min).
2. After Core §4 and the wire: Refresh, recompile_status, console.
3. After each client commit: Refresh, recompile_status, console. **Never `run_tests` in Play Mode** (STATE).
4. The fonts (§7.9) and the camera (§7.10) through `unity command eval` or the MCP tools, with the paths
   declared in `allows_assets` in both spellings (STATE: the guard matches the path as written).
5. Captures. Practice mode (Lobby → Play Mode → `SceneManager.LoadScene("Arena")` twice, `artifacts/t0011/start.sh`
   does it) reaches most states by playing. The round-3 states come from `Editor/HudPreview.cs`: menu items
   `Mimas/HUD/Preview round 3 (armed)`, `(their turn)`, `(last seconds)`, `(blocked)`, `(draft)`,
   `(round card)`, `(round result)`, `(series result)`, each feeding the HUD a fixture built from the same
   round-3 hero as the examine preview (Greek; Athena's Guard, Apollo's Bowstring, Nike's Jab; 1 of 3 AP; the
   enemy Norse with Thor's Vigour revealed and two boons unseen) and stopping time, `End preview` restarting
   it. Schedule captures from inside the Editor (`ScreenCapture.CaptureScreenshot` in an
   `EditorApplication.update` closure) into `Assets/_Shots/`, then copy to `artifacts/t0013/`. For T-0014,
   seat 2: run the local server, create a room in the Editor, join it from the browser smoke
   (`--url http://localhost:7777/?room=<code> --shot artifacts/t0014/seat2`), and capture both sides.
6. Web build and smoke last.

---

## 12. Commit plan (each step green before committing; `git add` only the files you touched)

**T-0013**

1. `docs: design #hud, #draft, #examine, #hidden-info point at the UI book` (proposed).
2. `core: the owner's view says what the opponent has seen` (§4, §5, the Core tests).
3. `server: tests for the seen flags over sockets`.
4. `client: one hover panel with three placements; generated ramps` (`HoverPanelView`, `Ramps`, `ExamineView`
   uses them; no visual change).
5. `client: the examine plate in ink; the serif leaves the build` (Examine.uss, ExamineView fades, Theme,
   fonts deleted through the Editor).
6. `client: the HUD contract` (`IMatchHudSource`, `MatchSession`, `HudModel`, `ExamineModelBuilder`, drivers,
   EditMode tests).
7. `client: the bar, the track, End Turn and the tags in ink` (`MatchHud.uxml` / `.uss`, `MatchHudView`,
   `Glyphs`).
8. `client: the preview, the flyovers, the draft and the moments in ink`.
9. `client: the Arena clears to ink` (the scene, through the Editor) + `Editor/HudPreview.cs`.
10. `docs: UI book statuses, language, architecture, networking`.
11. `studio: run report, STATE, T-0013 to verify`.

**T-0014**

1. `docs: design #presentation — the room, two seats facing` (if T-0013 did not land it).
2. `client: the lobby and the room in ink; gear as tiles; you on the left` (+ `RoomLayout`, EditMode tests).
3. `docs: UI book status, architecture`.
4. `studio: run report, STATE, T-0014 to verify`.

---

## 13. Defaults for forks the session may hit

| Fork | Default |
|---|---|
| `KnownEntry` is a struct with positional construction everywhere | Add the field with a default of `false` and one new constructor; never reorder existing parameters |
| `FromView` has no path to import revealed entries for the viewer's own unit | Add one: the same `RevealedEntry` import it uses for the enemy, filtered to `Viewer == 1 - viewer` |
| A mirror test goes red after §4 | The `FromView` import is missing or uses the wrong prefix; fix the import, never the test |
| You want a second wire field (e.g. the enemy's reveal order) | Do not; write it in STATE as a follow-up |
| `Wire.ReadView` treats all unit fields the same way | Read `seen` / `lineageSeen` only when `mine` is true; otherwise `false` |
| The bar's hover content needs data the examine builder does not compute for your own unit | Compute it in `ExamineModelBuilder.Tile` for both sides; never a second tile model |
| The hover panel `Above` would leave the window on the left | Clamp its left to the window's left + 8 |
| The preview `Over` would leave the window at the top | Flip it below the tag |
| A preview line kind is unknown (a new modifier) | Show it by its label, as today |
| Deleting a font asset through `eval` fails | Use the MCP `delete_asset`; if both fail, stop and report (never `git rm` an asset) |
| The Web build is over 13 MB | It should shrink by the three serif atlases; if not, report the size and stop — never move the ratchet |
| URP ignores `Camera.clearFlags` | Set the camera's background type through `UniversalAdditionalCameraData` if the component exists; otherwise report |
| The ramp textures look banded | 64 px of ramp is enough; use `FilterMode.Bilinear`, `TextureWrapMode.Clamp` |
| At 1280×720 the boons column meets the examine plate | It is under the plate by design (a layer); nothing to do |
| The bar and the tags overlap at 1280×720 | Keep the bar; the tags are world-anchored and move with the camera |
| `MyName` is empty online | Fall back to "You" |
| A category other than movement/weapon/spell appears | Append its row with its name upper-cased, as today |
| `LoadoutPresets` has one preset | One tile, 520 wide |
| The room's other seat JSON is missing | "WAITING FOR A PLAYER" |
| `docs/ui` and the canvas disagree | The book wins; note it |

---

## 14. Definition of done (copy into the final report with ticks)

**T-0013**

- [ ] `dotnet build Mimas.slnx` clean; Core ≥ 461 + 12; server ≥ 58 + 2; EditMode ≥ 24 + 10; no assertion
      softened or removed; the mirror tests untouched and green.
- [ ] `KnownEntry.SeenByOpponent` and `UnitView.LineageSeenByOpponent` set only on own entries; the wire
      writes `seen` / `lineageSeen` only for the own unit; the enemy JSON unchanged.
- [ ] Every id of `docs/ui/hud.md` §3 and `docs/ui/between-rounds.md` §2–§3 exists as a UXML `name` (list them
      with a grep in the report); `MatchHud.uss` has no `--hud-*` and no literal colour; nothing under 10px at
      1920×1080.
- [ ] `Examine.uss` reads no `--mimas-paper*`; no Cormorant file in `UI/Fonts/`; `Theme.uss` has no serif face.
- [ ] The Arena clears to ink.
- [ ] Captures at 1920×1080 and 1280×720 in `artifacts/t0013/`, named in the run report, held against the
      canvas boards: `hud.md` §8 items 1–6 and `between-rounds.md` §5 items 1–5; plus the examine plate on ink
      for your hero and for theirs.
- [ ] `unity command console` clean; Web build ≤ 13 MB with the size before and after in the report; smoke
      green on three engines.
- [ ] Docs of §10; STATE with the follow-ups; run report complete; T-0013 at `verify`.

**T-0014**

- [ ] EditMode ≥ (T-0013's count) + 4; Core and server unchanged.
- [ ] Every id of `docs/ui/lobby.md` §2–§3 exists as a UXML `name`; `Lobby.uss` has only `--mimas-*`; radius 0.
- [ ] No `DropdownField` in the lobby; one tile per preset; the lineage rows are elements, not one string.
- [ ] Captures in `artifacts/t0014/`: `lobby.md` §5 items 1–5, seat 2 from the browser.
- [ ] Console clean; Web build ≤ 13 MB; smoke green on three engines; the Play vs bot and the room-code paths
      still start a match.
- [ ] Docs of §10; STATE; run report; T-0014 at `verify`.
