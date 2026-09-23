# Between rounds · the draft and the round moments

_Status: **proposed** (chosen by Rohan on 23 Sep 2026 in the HUD session, canvas round 2). Design anchors:
`#draft`, `#session`, `#round`, `#win-conditions`, `#hidden-info`, `#presentation` (HUD: series line, round
card, draft overlay). Language: `docs/ui/language.md`. Mock: https://claude.ai/artifact/K9qZcJQ115G5G685sdMAwy,
page "HUD", round 2 row 2: **"Draft · ink cards with the lineage wash"** (chosen; the paper cards and
the no-cards board beside it were not), **"Round card"**, **"Round result"**, **"Series result"** with the
tweak on **band** (chosen; "type only" was not). Work order: `docs/specs/2026-09-23-hud-restyle.md`, task
`T-0013`. Today's screens: `MatchHud.uxml` `#draft-panel` and `#banner-panel` (the round card reuses the
banner), from T-0010 (ADR-036); their behaviour stays, their look changes._

## 1. What it is for

The two seconds between a round and the next are where the series is felt: who took the round, what the
score is, what you are becoming. The draft must read in three seconds (STATE: "the only thing that will say
whether the draft reads in three seconds" is the playtest); the moments must never be mistaken for the end
of the series, and only the series result offers a way out.

## 2. The draft (`dr.*`)

Over the board, dimmed; the turn track (`docs/ui/hud.md` §3.1) stays on top, reading "DRAFT" in the middle.

| id | Shows | Data | States |
|---|---|---|---|
| `dr.scrim` | the board under ink at 80% | — | fades in over 250 ms, as today |
| `dr.headline` | "ROUND 2 TO GUEST-2869 · 1 – 1", caps 20 `fg` at 0.28em, 170 from the top | `src.Draft.Headline` (composed by the presenter: the round, its winner's name, the score yours first) | — |
| `dr.next` | "CHOOSE ONE BOON · ROUND 3 ON OPEN FIELD", caps 11 `fg-3` | `src.Draft.NextRoundLine` | — |
| `dr.card[i]` | a `card` 360 × 470 on `ink-2` with `shadow.panel`, 44 apart, centred, top 300: | `src.Draft.Cards[i]` (`HudDraftCard`) | `selected`: lifts 14px, a 2px `you` left edge, the kind glyph filled; after the pick, the others at 45% and none clickable |
| `dr.card[i].wash` | the top 130: the lineage's painting wash (`hueDark` / `hueLight`, the examine plate's recipe) fading to `ink-2` | **new** `HudDraftCard.HueDark`, `HueLight` | — |
| `dr.card[i].kind` | on the wash, bottom left: the kind glyph 16 in `you` and "BLESSING" / "ENCHANT" / "SIGIL" caps 12 `you` | `Kind` | — |
| `dr.card[i].name` | "NIKE'S JAB", caps 28 `fg` at 0.12em | `Name` | — |
| `dr.card[i].god` | "NIKE · GREEK", caps 11 `fg-3` | `God`, `Lineage` | — |
| `dr.card[i].text` | the boon's sentence, `body` 15 `fg-2` | `Effect` | — |
| `dr.card[i].on` | at the foot: the slot glyph 14 and "ON YOUR LONGBOW" / "ON YOU", caps 11 `fg-2` | `Attach`, **new** `Slot` (null for a Blessing → the Blessing circle) | — |
| `dr.timer` | a 520 × 2 `hair` line under the cards, filled `you` from the left, an 8px `fg` ember at the end: the rope, burning the whole 20 s | `SecondsRemaining`, `SecondsTotal` | — |
| `dr.confirm` | `button`: "CONFIRM", 1px `you` edge, `you` at 14%; "KEPT" and disabled after the pick | `Selected`, `Picked` | disabled until a card is selected |
| `dr.status` | under Confirm after your pick: "Waiting for Guest-2869…", `body` 12 `fg-3` | `Status` | only after your pick |
| `hud.track.picked` | their end of the track: "HAS CHOSEN" and a filled `them` dot | `OpponentPicked` | replaces today's green `#draft-dot` |

Behaviour is T-0010's, unchanged: a click selects, Confirm keeps it, the timer keeps the first; a reload
comes back into the draft with the seconds that are left; the pick is private (`#hidden-info`).

## 3. The round moments (`mo.*`)

One component in three kinds, always inside the **band** (`language.md` `band`: ink at 86% across the
middle, 300 tall, fading over the outer 22% each side). The track stays above it.

| id | Round card (start) | Round result | Series result | Match lost |
|---|---|---|---|---|
| `mo.band` | the band | the band | the band | the band |
| `mo.kicker` | "ROUND 3", caps 15 `fg-2` at 0.3em | "ROUND 2 TO GUEST-2869", caps 20 in the **winner's** colour | — | — |
| `mo.title` | "OPEN FIELD", display 38 at 0.3em caps `fg` | — | "VICTORY" in `you` / "DEFEAT" in `them`, display 38 at 0.34em | "MATCH LOST", display 38 `fg` |
| `mo.score` | — | "1 – 1": your number in `you` at display 38, a `fg-3` dash, theirs in `them` | — | — |
| `mo.sub` | "YOU MOVE FIRST" in `you` / "THEY MOVE FIRST" in `them`, caps 13 | "by elimination · the draft opens in a moment", `body` 12 `fg-3` | "SERIES 2 – 1 · BY ELIMINATION", caps 13 `fg-2` | the reason, `body` 12 `fg-3` |
| `mo.back` | — | — (the series goes on; no way out) | `button` "BACK TO ROOM" | `button` "BACK TO LOBBY" |
| timing | clears itself after 1.5 s (today) | holds until the draft opens 2 s later (today) | holds until Back | holds until Back |
| data | `src.Moment` **new** (`HudMoment`, §4) | same | same | same |

This replaces the banner's strings and fixes its colour: today every banner that is not "VICTORY" draws in
the opponent's colour, the round card included.

## 4. Data gaps

| # | Gap | Proposed shape |
|---|---|---|
| 1 | The moments as data | `HudMoment { Kind (RoundStart, RoundResult, SeriesResult, MatchLost), int Round, string MapName, bool IMoveFirst, bool IWon, string WinnerName, int ScoreMine, int ScoreTheirs, string Reason, bool ShowBack, string BackLabel }` on `IMatchHudSource`, replacing `Banner` / `BannerDetail` |
| 2 | The card's wash and slot | `HudDraftCard.HueDark`, `HueLight` (from the lineage), `Slot` (from the boon's `requires.slot`, null for a Blessing) |
| 3 | The track in the draft | `src.Phase == Draft` → `hud.track.round` "DRAFT", no fill, no marker |

## 5. Acceptance

Captures at 1920×1080 and 1280×720 against the canvas boards:

1. The draft with a card selected and the opponent's "HAS CHOSEN" on the track.
2. The draft after your pick: "KEPT", the other two cards at 45%, the status line.
3. The round card at the start of round 2 or 3.
4. A round result with the score in both colours and no button.
5. The series result with "BACK TO ROOM".

## Change log

- 2026-09-23 · written from the HUD session (canvas round 2); status proposed.
