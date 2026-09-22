# Spec D, part 2 (outline): Boons in the game — the online best-of-3 with a draft in the room, and the presentation of boons and reveals

_Status: **outline, not a work order.** Written 21 Sep 2026 in the same session as part 1
(`docs/specs/2026-09-21-boons-groundwork.md`). Fable turns it into a full spec **after part 1 lands**,
checked against the real Core names at that commit; Opus executes that spec unattended (task `T-0010`,
lane full, depends on `T-0009`). Nothing below is to be built before then. Design anchors: `#session`,
`#draft`, `#character-select`, `#lineage`, `#online`, `#presentation` (lobby, HUD, examine),
`#hidden-info`, `#bots`._

## What Rohan decided about part 2 (21 Sep 2026)

- **In:** the online best-of-3 with the draft in the room, and the presentation of boons and reveals.
- **Also cheap enough to fold in:** the Editor practice mode reading lineage + boons from
  `DefaultMatchSettings` (how the two hidden passives are exercised today).
- **Not in:** a headless balance simulation (a part 3 if wanted), a third map, tiers, rerolls, presets.

## 1. Server (`server/Mimas.Server`)

- `Room` hosts a `Mimas.Core.Session.Session` instead of a bare `MatchState`. `Room.Round` becomes the
  session's round; `MatchId` stays the room's id (ADR-032). The rematch-in-the-room flow of T-0008
  becomes: after `SessionEndedEvent` both seats are back in the room, Ready reset, gear and lineage
  editable; a new Ready pair starts a new session.
- `room.loadout` gains `lineage` (validated against `Lineages`; `bad_loadout` if unknown). Gear first,
  lineage second, as `#character-select` orders it; in M2's room there is no separate step, one panel.
- New messages: `session.state` (the `SessionView` for that seat, attached like `view` is today),
  `draft.pick` (client → server: `offerIndex`), `draft.state` (offers to the seat, the opponent's
  "picked" flag, remaining ms), `round.start` / `round.end` / `session.end` (or carried as events in
  `match.events`; decide in the full spec — the Core events already exist, `Wire` needs codecs for the
  five session events and for `SessionView`).
- Clock: a draft deadline from `rules.draft.timeoutMs`; on expiry the server submits
  `DraftPickCommand(seat, 0, Timeout)` exactly as it submits `EndTurnCommand(Timeout)`.
- Resync: `match.resync` returns the `SessionView` (with the `PlayerView` inside when a round runs).
- Bot seat: a random lineage from the seeded server `Rng` at room creation; `RandomBot.ChooseDraft` for
  picks; the two hidden passives (`ward-of-feathers`, `stone-skin`) retire in favour of the lineage's
  starting Blessing (`ServerOptions.BotModifierIds` goes; `MatchSetup.WithModifier` may go with it).
- Origin, auth, reconnect grace: unchanged.
- Tests (`server/Mimas.Server.Tests`): two fake clients play a whole best-of-3 over sockets with a draft
  between rounds; a draft timeout; a reconnect during a draft; the opponent's `draft.state` never
  carries the other seat's offers or pick id; the session hash / replay from the journal.

## 2. Client (`MimasClient/Assets/_Game`, presentation only, through the live Editor)

- **Room panel:** a lineage row under the preset (three cards: name, one-line flavour, the starting
  Blessing named, three example boons from the pool). Persists in browser storage with the preset.
- **Draft screen** (in the lobby scene, between rounds): three cards (name, kind badge, god, one-line
  effect text generated from the effects), a timer bar from the remaining ms, the opponent's "picked"
  dot, the series score. Pick = one click + confirm, or the timeout. Follow the research-then-options
  workflow for the layout (`q-pres-select` is still open) — mock, then ask Rohan, before building.
- **Series flow:** `round.start` loads the Arena for the next map with a "Round 2 — Arena" line;
  `round.end` shows the round banner with the score; `session.end` shows the series result and returns
  to the room (ADR-032).
- **Presentation of boons and reveals** (the other half Rohan asked for):
  - Examine: own boons listed under a "Boons" header with kind and god; enemy boons as "?" rows until
    revealed, then name + god; revealed lineage shown beside the enemy name.
  - Reveal flyover: `BoonRevealedEvent` → "Revealed: Agni's Crown (Hindu)"; `LineageRevealedEvent` →
    the lineage tag appears on the enemy's nameplate.
  - Action bar: a Sigil's ability appears in its category with the item's icon mark; an Enchant-changed
    number (range, cost, damage) shows on the button with a small mark, and the range circle uses the
    resolved def (it already asks the mirror, so this is free once the mirror resolves).
  - Damage tooltip: `BoonStat` lines named after the boon; the `Nullify` line as "Immune (fire)";
    element icon on the ability button and in the tooltip.
  - Practice mode: `DefaultMatchSettings` gains `lineage` + `boonIds` per side and `LocalMatchDriver`
    builds `PlayerBuild`s; `LocalMatchSession` may host a `Session` or stay one round — decide in the
    full spec (one round with pre-owned boons is the cheap version).
- Web build after the client half; smoke: a two-browser session with a draft over the live server.

## 3. Content growth

- Each lineage from six to nine pool boons (three per kind), still from the *works* vocabulary, so every
  draft has a real choice in every kind. Names as `<God>'s <thing>`. Numbers stay placeholders; Rohan
  tunes after playing.
- `.meta` files and the regenerated `GameDataManifest` for every JSON part 1 added, if part 1 ran with no
  Editor (part 1 §0.7).

## 4. Docs and design

- Design page: `#online` gains the session rules (a room hosts a session; draft in the lobby scene), the
  lobby section gains the lineage row and the draft screen, `#bots` closes `q-bots-draft`,
  `#presentation` closes `q-pres-select` once the mock is chosen. `#session`, `#draft`, `#lineage`,
  `#boons` move to `data-impl="implemented"`.
- ADR for the wire shape of the session messages (one ADR, alongside ADR-027).
- Ledger rows **M3-3**, **M3-4**, **M3-5** are what the verifier ticks after part 2; **M3-6** partly
  (the draft screen; character select's gear half is the room's preset today).

## 5. Open questions to settle in the full spec (ask Rohan, 3–4 options each)

1. Does a seat that reloads during a draft come back to the draft (grace) or forfeit the pick to the timeout?
2. Session messages as new message types or as events inside `match.events`?
3. Draft screen layout (after a mock), and where the series score lives in the HUD.
4. Should `Play vs bot` be a full best-of-3 or a single round by default?
