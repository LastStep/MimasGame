---
project: mimas
milestone: M2
updated: 2026-09-17
updated_by: builder (T-0003)
---

# Where Mimas stands

> **Rewritten, never appended.** One page, always current. History lives in `runs/` and in git.

## Right now

**The match is on the server.** From the Editor you open a lobby, press **Play vs bot** or **Create
room**, get a four-letter code, pick your gear in the room, press Ready, and play a real match whose
truth lives in `Mimas.Server` — with a 30 s server-authoritative turn, resign, and a reconnect grace.
The client holds a *mirror* of the match rebuilt from its own `PlayerView` (ADR-026), so every preview
it already had works unchanged and it can only ever know what the server chose to send.

**321 Core tests and 34 server tests**, the latter playing whole matches over real sockets using the
client's own mirror as the test double. Both are ladder rungs, both required. Local practice — opening
`Arena.unity` directly — still plays a whole game against the bot, unchanged.

**It runs in a browser.** As of 17 Sep evening (T-0003) a Web build is served by `Mimas.Server` itself
and plays: the lobby renders, a room code is issued, the socket connects, and a bot match reaches the
Arena with the board and HUD. Every WebGL-specific path — the NativeWebSocket jslib socket,
`PlayerPrefs` reaching IndexedDB, `?ws=`/`?room=` off the page URL, Brotli — has now run at least once.
Release build **12.38 MB** against a 13 MB ratchet.

What does not exist yet: **anything on the internet**. The friends playtest is **Sat 10 Oct 2026** —
23 days away, and deploy is the whole of what stands in the way.

## Current milestone: M2 — online

Target: **Sat 10 Oct 2026**, friends playing over the internet.

The work order was `docs/specs/2026-09-17-online-slice.md` plus its **amendment A1** (room codes). It is
executed; the spec is now history and `docs/networking.md` is the wire reference.

| Done-when | State |
|---|---|
| M2-1 Two browsers play a full match through `Mimas.Server` | **one browser plays the server** (17 Sep, T-0003). Two seats across a browser and an incognito window is the remaining half |
| M2-2 Two players who want to play each other end up in the same match | **done** — by a four-letter room code (OPT-0001, ADR-029) |
| M2-3 Guest auth with a resumable token; a reload keeps your identity | **done** (tested; browser reload untested) |
| M2-4 Server-authoritative 30 s turn; timeout arrives as EndTurnCommand(Timeout) | **done**, seen firing live |
| M2-5 Reload within the 60 s grace resyncs into the running match | **done** (tested server-side and in the Editor) |
| M2-6 Resign and disconnect-forfeit end the match correctly | **done**, both paths seen |
| M2-7 Deployed on the VPS over HTTPS, reachable by a friend | **not started** — this is the gap to 10 Oct |
| M2-8 Rematch without leaving the room | not started (out of scope by D9) |

**Nothing in the ledger is ticked.** `pass` belongs to a verifier, not to the builder who wrote the
code (`reward-hacking-guards.md`). M2-1 and M2-3..M2-6 are ready for one; M2-2's wording still says
"mechanism open" and wants updating to room codes.

## In flight

| Task | What | Status | Who |
|---|---|---|---|
| T-0002 | Execute the online slice | verify | builder |
| T-0003 | The Web build, in a browser | running — see below | builder |

**T-0003 is mid-flight.** The machine ran out of memory and the harness killed the server and a
rebuild together. Everything is committed (through `b6ad293`) and `studio/runs/R-2026-09-17-T-0003.md`
has the whole log. What is **proven**: the serving path, the Release build, the browser run, the socket,
a bot match starting, and four bugs found and fixed. What is **not yet proven**: the last rebuild (it
never logged `result=`), the magenta-placeholder fix, the player-settings persistence fix, the ladder on
final code, and everything needing Rohan's hands — two seats by incognito, `?room=`, a mid-match
refresh, resign.

## Blocked

| Task | Blocked by | Since |
|---|---|---|
| — | nothing | |

## Waiting on Rohan

| What | File | Since |
|---|---|---|
| Edit `pillars.md` — it is a draft distilled from the design page, and the pillars are yours | `studio/pillars.md` | 17 Sep 2026 |
| Optional: should a room show the other seat's chosen preset before the match starts? Hidden today | design page `#q-online-room-loadout` | 17 Sep 2026 |

## The two things that matter next

1. **Finish T-0003** — one rebuild, the ladder, and Rohan playing it in a browser. Small.
2. **Deploy** (`F-deploy`, M2-7). Nothing about 10 October works without it.

### Two things T-0004 must not rediscover

- **Build through the script, not the profile.** `unity build MimasClient --target WebGL
  --execute-method Mimas.Client.Editor.WebBuild.Build --output-path Build/Web` states every setting
  that matters in code and needs no Editor. The `Web Release` **profile** still carries its own
  PlayerSettings snapshot with `webGLExceptionSupport: 0`, so ladder rung 9's `--profile "Web Release"`
  would build with `try`/`catch` disabled. Fixing the profile needs a live Editor.
- **A Development Build does not link** on 6000.4.4f1: `wasm-ld: undefined symbol:
  unitytls_ssl_set_client_transport_id` from Unity's own `modules_development` TLS archive. No public
  report of it exists anywhere. Release is unaffected. Do not "fix" it with
  `ERROR_ON_UNDEFINED_SYMBOLS=0` — that only moves the failure to runtime.

Then: a verifier over T-0002, and M2-8 (rematch) if there is room.

From `E:\Studios\Trinetra-Game-Studio\docs\PLAN.md` §10: if two browsers cannot play a full match
through the server by **Fri 2 Oct**, the 10 Oct playtest falls back to the local build and online moves
to 17 Oct. On today's evidence that date is not at risk from the game code; it is at risk from deploy.

## Last playtest

**17 Sep 2026, Rohan, the online slice.** Ran the server with `dotnet run` and played through it;
looked good, no notes raised. That closes "nobody has ever connected a client to this server".

**Still unplayed by a human: the browser build.** An agent drove it through headless Chromium and a bot
match started clean, but nobody has touched it. That is the next playtest, and it is short.

**16 Sep 2026, Rohan, the aiming slice, Editor on arena-4.** Console clean. Two bugs caught in play,
both since fixed.

Still unplayed: **anything in a browser.** That should be the next session and the next playtest.

## Next decision due

None open. OPT-0001 (room codes) was decided and executed on 17 Sep. The one live question is the
optional design nit above, which does not block anything.
