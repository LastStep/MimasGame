# Research: Unity 6000.4 → Web build, hosting, FMOD, hex grids, URP limits

_Researched 14 Sep 2026 against Unity 6000.4 manual pages. **[unverified]** = could not confirm against a primary source._

## 1. Web platform state in 6000.4

| Topic | State |
|---|---|
| Default graphics API | **WebGL2** |
| WebGPU | **Experimental in 6000.4** ("not recommended for production"). Left experimental only in **6000.6** (Aug 2026). 6000.4 is a frozen tech-stream release ("will not receive any fixes anymore"); **6000.3 is the LTS**. Optionally list WebGPU above WebGL2 for opportunistic use, but do not depend on it |
| WebGPU limitations | No async compute, no sync GPU readback (use `AsyncGPUReadback`), `RWBuffer` unsupported, baked GI only, 16-texture limit, no VideoClip importer |
| WebAssembly 2023 | Default on: native exceptions, SIMD, bulk memory, BigInt. Min browsers Chrome/Edge 91, Firefox 89, Safari 16.4 — no fallback |
| Multithreading | "Native C/C++ Multithreading" runs engine + Burst jobs in parallel; **managed C# threads still unsupported**; needs COOP/COEP headers. Leave **off** for a turn-based game |
| Mobile browsers | iOS Safari 15+, Android Chrome 58+. Safari has no IndexedDB in iframes (Data Caching) |
| Memory | Initial 32 MB, geometric growth, max 2048 MB (bugs above 2 GB in older browsers). Set initial to measured steady-state (128–256 MB desktop) |
| Empty build size (Brotli) | 6000.4.0f1: URP default **8.74 MB**, URP min-size 6.78 MB, Built-in 3.57 MB (JohannesDeml). Realistic target for a small URP tactics game: 10–20 MB initial download with assets in Addressables |

Size/load tips: Managed Stripping **High** + `link.xml`; API compat .NET Standard 2.1; remove unused packages (Input System + UGUI alone add a lot — but we need Input System); strip shader variants; Addressables (LZ4 only on web — no LZMA, no sync loads); textures DXT (desktop) / ASTC (mobile) — dual-build if mobile matters; audio re-encoded to AAC, `CompressedInMemory`, no streaming; no audio until user gesture.

## 2. Recommended Player Settings (Web)

| Setting | Recommended | Default |
|---|---|---|
| Compression Format | **Brotli** (needs HTTPS) | Gzip |
| Decompression Fallback | **Off** (we control nginx; fallback = bigger loader) | Off |
| Data Caching | On | Off |
| Name Files As Hashes | On (enables immutable cache headers) | Off |
| Run In Background | On (browsers still throttle background tabs) | — |
| Enable Exceptions | **None** (or Explicitly Thrown Only) | Explicitly Thrown Only |
| Target WebAssembly 2023 | On | On |
| Native C/C++ Multithreading | **Off** | Off |
| Initial / Max Memory | 32 → 128–256 MB / 2048 | 32 / 2048 |
| IL2CPP Code Generation | "Faster (smaller) builds" | |
| Code Optimization | "Disk Size with LTO" for release | |
| Managed Stripping Level | **High** | Low |
| API Compatibility | .NET Standard 2.1 | |
| Texture Compression | DXT (desktop) / ASTC (mobile build) | |

## 3. nginx

See `tools/deploy/nginx-mimas.conf` (adapted from Unity's official sample). Essentials: `.wasm.br` → `application/wasm` + `Content-Encoding: br`; `.data.br` → `application/octet-stream`; `.js.br` → `application/javascript`; `gzip off` in those locations; nested `add_header` replaces inherited headers; COOP/COEP only for native threads; `/ws` proxy with `proxy_http_version 1.1`, Upgrade headers, `proxy_read_timeout 3600s`, `proxy_buffering off`.

Cloudflare: passes origin `Content-Encoding: br` through **if** Rocket Loader / Email Obfuscation / Auto HTTPS Rewrites / Polish are off for the path; add `Cache-Control: no-transform`. Default edge cache excludes `.wasm`, `.data`, `.br` → add a Cache Rule for `/Build/*`. Purge cache on deploy (or use hashed filenames). WebSockets work through the proxy; ~100 s idle timeout → keep heartbeats.

## 4. FMOD for Unity on Web

| Item | Finding |
|---|---|
| Version | **FMOD for Unity 2.03.14** (Asset Store, Jun 2026), compatible with 6000.4 |
| HTML5 support | Yes (Emscripten/Web Audio). FMOD maintains `fmod/Unity-HTML5-Demo`. [unverified] that HTML5 binaries ship in the 2.03.14 package itself (they did in 2.02) |
| Bank loading | Async on web: load banks in a loading scene, wait for `RuntimeManager.HaveAllBanksLoaded`, then change scene |
| Autoplay | Audio blocked until user gesture: on first interaction call `RuntimeManager.CoreSystem.mixerSuspend()` then `mixerResume()`; or init FMOD after a Start button |
| Licensing [unverified — fmod.com blocked] | Indie: free if dev budget < US$600k and revenue < US$200k/yr; requires project registration + FMOD logo/credit |
| Caveat | Adds async plumbing and package weight; Unity built-in audio is the simpler fallback if FMOD web is painful |

## 5. Hex grids

- **Red Blob Games — Hexagonal Grids** (canonical): store axial (q,r), compute in cube (q,r,s); distance, neighbours, rings, lines, rounding. `Mimas.Core.Geometry.Hex` implements this.
- **Catlike Coding — Hex Map** (URP, A*, units, fog of war) — reference to cherry-pick.
- No widely adopted, maintained open-source Unity hex *package* in 2026; tactics projects roll ~300 lines from Red Blob in pure C# with a `Dictionary<Hex, Tile>` model and MonoBehaviour views — which is what we do.
- Unity's built-in `Grid` component (`CellLayout.Hexagon`) can help with cell↔world conversion in the client.

## 6. URP on Web — use / avoid

| Feature | WebGL2 (6.4) |
|---|---|
| URP Forward renderer | OK (not Forward+/Deferred) |
| Shader Graph | OK |
| **VFX Graph** | **No** — needs compute shaders → use Shuriken Particle System |
| Post-processing | Works; bloom/DoF/SSAO/motion blur are expensive; tonemapping + vignette + colour grading are cheap; HDR off on mobile; MSAA 2× or off |
| Realtime GI / APV | No — baked lightmaps + light probes |
| GPU Resident Drawer / occlusion / STP | WebGPU only |
| Sync GPU readback | Avoid; `AsyncGPUReadback` |
| Cinemachine | **3.1.7** verified for 6000.4 (`CinemachineCamera` API) |
| Fonts / UI Toolkit | Include fonts in project; UI Toolkit runtime works on web |

## 7. Package versions for 6000.4

| Package | Version |
|---|---|
| `com.unity.render-pipelines.universal` | 17.4.x (pinned to editor) |
| `com.unity.cinemachine` | 3.1.7 |
| `com.unity.inputsystem` | 1.19.0 |
| `com.unity.addressables` | 2.9.1 (3.1.0 also compatible) |
| `com.unity.nuget.newtonsoft-json` | 3.2.2 |
| `com.unity.test-framework` | 1.6.0 |
| UI Toolkit | built-in module |
| `com.unity.ai.assistant` | 2.12.0-pre.2 for 6000.4 — its MCP server is **deprecated**; use Unity CLI + `com.unity.pipeline` instead |

## Sources

- https://docs.unity3d.com/6000.4/Documentation/Manual/WebGPU.html · https://docs.unity3d.com/6000.4/Documentation/Manual/WebGPU-limitations.html · https://discussions.unity.com/t/webgpu-out-of-experimental-in-unity-6-6/1734694
- https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-graphics.html · https://docs.unity3d.com/6000.4/Documentation/Manual/web-optimization-player.html · https://docs.unity3d.com/6000.4/Documentation/Manual/class-PlayerSettingsWebGL.html · https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-deploying.html · https://docs.unity3d.com/6000.4/Documentation/Manual/web-server-config-nginx.html · https://docs.unity3d.com/6000.3/Documentation/Manual/webgl-memory.html · https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-browsercompatibility.html · https://docs.unity3d.com/6000.4/Documentation/Manual/wasm-2023-features.html · https://docs.unity3d.com/6000.4/Documentation/Manual/web-multithreading-intro.html · https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-texture-compression.html · https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-audio.html · https://docs.unity3d.com/6000.4/Documentation/Manual/webgl-caching.html
- Build sizes: https://github.com/JohannesDeml/UnityWebGL-LoadingTest · https://gist.github.com/aras-p/740c2d4f9977ce92b7de72b1394dd365
- Cloudflare: https://developers.cloudflare.com/speed/optimization/content/compression/ · https://developers.cloudflare.com/cache/concepts/default-cache-behavior/
- nginx WebSocket proxy: https://websocket.org/guides/infrastructure/nginx/
- FMOD: https://assetstore.unity.com/packages/package/1082237 · https://github.com/fmod/Unity-HTML5-Demo · https://alessandrofama.com/tutorials/fmod/unity/fix-blocked-audio-browsers · https://en.wikipedia.org/wiki/FMOD
- Hex: https://www.redblobgames.com/grids/hexagons/ · https://www.redblobgames.com/grids/hexagons/implementation.html · https://catlikecoding.com/unity/hex-map/
- VFX Graph / URP requirements: https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.4/manual/System-Requirements.html · https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.4/manual/requirements.html
- Packages: https://docs.unity3d.com/6000.4/Documentation/Manual/com.unity.cinemachine.html · https://docs.unity3d.com/6000.4/Documentation/Manual/com.unity.inputsystem.html · https://docs.unity3d.com/6000.4/Documentation/Manual/com.unity.addressables.html · https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.2/manual/index.html · https://docs.unity3d.com/6000.4/Documentation/Manual/com.unity.ai.assistant.html
