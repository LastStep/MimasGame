# Research: multiplayer stack for a Unity 6 WebGL, server-authoritative, 1v1 turn-based game

_Researched 14 Sep 2026. Constraints: browser client (WebSockets only), hidden information (server must be authoritative), self-host on own VPS, solo C#/.NET developer, AI-written code, JSON data._

## Comparison

| Option | WebGL support | Server-authoritative? | Self-host? | Server logic language | Matchmaking built-in | Cost | Maturity / risk | Fit (1–5) |
|---|---|---|---|---|---|---|---|---|
| Photon Fusion 2 (Server Mode) | Yes (Shared mode recommended; Server mode + WebGL clients undocumented) | Yes, but must relay via Photon Cloud | No (Photon Server = Industries Circle Pro ≈ $1,000/mo) | C# (Unity headless) | Room/property; no ladder | 100 CCU free; 500 CCU $125/mo; 1k $250; 2k $500 | Mature; vendor lock-in | 2 |
| Photon Quantum 3 | Yes (heavy CPU) | No — lockstep: every client holds full state | No | C# deterministic | Room-based | Same as Fusion | Wrong model for hidden info | 1 |
| Photon PUN 2 | Yes | No (master client) | No | C# | Room-based | 500 CCU $95/mo … | LTS/maintenance mode (v2.50, Mar 2025) | 1 |
| Nakama 3.40 | Yes (Unity 6 fix Jan 2025, SDK 3.21.1) | Yes — authoritative match handlers | Yes: Docker + Postgres, Apache-2.0 | Go / TS / Lua (no C#) | Yes — query/rank matchmaker, leaderboards, auth | Free self-host | Mature, monthly releases | 3.5 |
| Colyseus 0.17/0.18 | Yes (no `Task.Delay` on WebGL) | Yes | Yes: Node, MIT | TypeScript | Rooms + `QueueRoom` (0.17); ladder DIY | Free self-host; Cloud $15/mo | Solo maintainer; breaking releases every ~6 months | 3 |
| Unity NGO 2.13 + UTP 6.6 WebSocket + Dedicated Server | Yes (WSS needs real CA cert) | Yes | Yes (server build free); UGS optional | C# inside Unity | UGS Matchmaker/Lobby; ladder DIY | UGS free tiers (Relay 50 CCU, Lobby 10 GiB…) | One match per headless Unity process (~100 MB RAM each); Multiplay hosting shut down Apr 2026 | 2 |
| Mirror 96 + SimpleWebTransport 3.1 | Yes | Yes | Yes, MIT | C# inside Unity | None | Free | Mature; 6000.4 not explicitly listed | 2 |
| FishNet 4.7 + Bayou | Yes | Yes | Yes (free tier commercial-OK) | C# inside Unity | None | Free / Pro | Mature | 2 |
| SpacetimeDB 2.x | Yes since v2.3.0 (mid-2026) | Yes (reducers); visibility via **experimental** row-level security | Yes: Docker, BSL 1.1 (one prod instance) | C# / Rust / TS modules | No | Free self-host; Maincloud $0/$25 | Young, fast-moving, RLS unstable | 2.5 |
| **Custom ASP.NET Core WS server + NativeWebSocket** | Yes (jslib bridge) | Yes — by construction | Yes, own VPS | C# (.NET), shared rules lib, `dotnet test` | You write it (small for 1v1 queue) | $0 beyond VPS | Zero framework risk; all code yours/AI-written | **5** |

## Notes per option

**Photon.** Fusion Server Mode is server-authoritative but traffic still relays through Photon Cloud and a Fusion Cloud subscription is mandatory. Quantum is deterministic lockstep so every client simulates full state — hidden enemy abilities would sit in client memory. Photon Server (on-prem) licences are Industries-Circle only (Pro tier $1,000/mo). PUN 2 is in maintenance mode.

**Nakama.** Authoritative match handlers (`match_init/join/leave/loop/…`), tick rate configurable (1/s fine for turn-based), `dispatcher.BroadcastMessage` with presence filters for hidden info; built-in matchmaker with property queries, leaderboards, auth, storage. Server v3.40.0 (Jul 2026). Downside: server logic only in Go/TS/Lua, so C# rules would be duplicated.

**Colyseus.** Authoritative by design; `StateView` (0.16) for per-client visibility; 0.17 added `QueueRoom`, reconnection, rate limiting. TypeScript only. Unity SDK 0.17.17; unverified compatibility with server 0.18's Schema 5.0.

**Unity NGO.** Works on WebGL over UTP WebSockets (must be WSS on HTTPS pages). NGO is built around one `NetworkManager.Singleton` per process → one headless Unity process per match. UGS pricing (Sep 2026): Relay free ≤50 avg CCU then $0.16/CCU; Lobby 10 GiB/mo free; Multiplay Game Server Hosting deprecated 1 Apr 2026. Standalone Lobby/Relay/Matchmaker packages deprecated in favour of `com.unity.services.multiplayer`.

**Mirror / FishNet.** Both fine on WebGL via SimpleWebTransport / Bayou, but server = headless Unity player, rules bound to MonoBehaviours, no queue/rating/persistence.

**SpacetimeDB.** C# modules, Unity 6 WebGL compatibility since v2.3.0; row-level security documented as "experimental, unstable"; BSL 1.1 licence.

**Custom .NET.** Kestrel WebSockets behind nginx; one process = many rooms (`Dictionary<MatchId, Room>`). Client libraries that work on WebGL: **NativeWebSocket** 2.0.4 (Apache-2.0, jslib bridge, Mar 2026), UnityWebSocket (MIT), Best WebSockets (paid). `System.Net.WebSockets.ClientWebSocket` does **not** work in WebGL; the official SignalR client doesn't either — skip SignalR. JSON: `com.unity.nuget.newtonsoft-json` 3.2.2 is the AOT-safe choice (add `link.xml`); System.Text.Json is unsupported in Unity; MemoryPack/MessagePack v3 viable later for binary.

## Recommendation

Custom ASP.NET Core WebSocket server + shared `Mimas.Core` (netstandard2.1, pure C#) + NativeWebSocket client. Every requirement maps to plain server code: hidden info = serialize only the player-view projection; clocks = per-match timer; matchmaking = rating-bucketed queue; ladder/Bo-X = rows in SQLite/Postgres; thousands of rooms per process. The rules engine is testable with `dotnet test` outside Unity — the property none of the Unity-hosted netcodes provide and that Nakama/Colyseus give up by forcing Go/TS. Runner-up: Nakama if a batteries-included backend matters more than C# rules.

**Unverified:** Fusion 2 WebGL clients + Server-mode sample; Unity Matchmaker per-ticket pricing; Colyseus 0.17 SDK ↔ 0.18 server; Mirror support for 6000.4; Best WebSockets price; MemoryPack WebGL; SpacetimeDB v2.3.0 exact date.

## Sources

- Photon: https://doc.photonengine.com/photon/current/pricing · https://www.photonengine.com/fusion/pricing · https://doc.photonengine.com/fusion/current/concepts-and-patterns/dedicated-server-overview · https://doc.photonengine.com/quantum/current/manual/webgl · https://blog.photonengine.com/photon-multiplayer-webgl-for-game-jams/ · https://doc.photonengine.com/server/current/operations/licenses · https://www.photonengine.com/industries/pricing · https://doc.photonengine.com/pun/current/reference/version-history
- Nakama: https://heroiclabs.com/docs/nakama/client-libraries/unity/ · https://github.com/heroiclabs/nakama-unity/blob/master/Packages/Nakama/CHANGELOG.md · https://heroiclabs.com/docs/nakama/concepts/multiplayer/authoritative/ · https://heroiclabs.com/docs/nakama/concepts/multiplayer/matchmaker/ · https://github.com/heroiclabs/nakama
- Colyseus: https://docs.colyseus.io/getting-started/unity · https://colyseus.io/blog/colyseus-017-is-here/ · https://github.com/colyseus/colyseus · https://colyseus.io/pricing/
- Unity NGO/UGS: https://docs.unity3d.com/Packages/com.unity.transport@6.6/manual/websockets.html · https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/releases · https://docs-multiplayer.unity3d.com/netcode/current/components/networkmanager/ · https://unity.com/products/gaming-services/pricing · https://status.unity.com/info_notices/362941 · https://docs.unity3d.com/6000.4/Documentation/Manual/dedicated-server-introduction.html
- Mirror/FishNet: https://github.com/MirrorNetworking/Mirror/releases · https://github.com/James-Frowen/SimpleWebTransport · https://fish-networking.gitbook.io/docs/fishnet-building-blocks/transports/bayou · https://github.com/FirstGearGames/FishNet/blob/main/LICENSE.md
- SpacetimeDB: https://github.com/clockworklabs/SpacetimeDB/releases · https://spacetimedb.com/docs/how-to/rls/ · https://github.com/clockworklabs/spacetimedb/blob/master/LICENSE.txt · https://spacetimedb.com/blog/all-new-spacetimedb-pricing
- Custom: https://github.com/endel/NativeWebSocket · https://github.com/psygames/UnityWebSocket · https://github.com/evanlindsey/Unity-WebGL-SignalR · https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.2/manual/index.html · https://github.com/Cysharp/MemoryPack · https://github.com/MessagePack-CSharp/MessagePack-CSharp
