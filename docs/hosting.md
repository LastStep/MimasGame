# Hosting

**Mimas lives at `https://mimas.laststep.cloud`**, on the Hostinger VPS Rohan already pays for, beside
`laststep.cloud` and its subdomains. This file is the architecture; **`docs/deploy-runbook.md` is the
document you follow to actually deploy.** The decision and its alternatives are ADR-031.

The VPS is **shared**. Everything Mimas owns is listed below and nothing else on that machine is ours:
`sites-enabled/laststep.cloud`, its certificate, the sixteen Docker containers and the webhook service
belong to another product and are never opened, edited or restarted by anything in this repo.

## Layout on the VPS

| Piece | Where | How |
|---|---|---|
| Everything Mimas owns | `/home/mimas/servers/mimas.laststep.cloud/` | one tree, matching the `/home/<user>/servers/<subdomain>/` convention the other product already uses for `api.laststep.cloud` |
| Unity Web build | `/home/mimas/servers/mimas.laststep.cloud/web/` | static files; nginx serves the pre-compressed Brotli ones with the right headers |
| Game server | `Mimas.Server` on `127.0.0.1:7777` | a **systemd** unit (`mimas-server`) running as the `mimas` system user; nginx reverse-proxies `/ws` and `/health` |
| The binary | `/home/mimas/servers/mimas.laststep.cloud/server/` | a self-contained `linux-x64` publish — **there is no .NET on the VPS and nothing to install** |
| Deploy scripts | `/home/mimas/servers/mimas.laststep.cloud/deploy/` | `vps-setup.sh` and the nginx/unit files, uploaded by `deploy.sh --setup` |
| Previous release | `/home/mimas/servers/mimas.laststep.cloud/web.prev`, `/home/mimas/servers/mimas.laststep.cloud/server.prev` | exactly one generation back; `deploy.sh --rollback` swaps them in |
| TLS | Let's Encrypt, **its own certificate** for `mimas.laststep.cloud` | issued with `certbot certonly --nginx`, renewed by the `certbot.timer` already on the box. Brotli decoding needs HTTPS, and a WebSocket from an HTTPS page must be `wss://` |
| Logs | journald | `ssh hostinger journalctl -u mimas-server -f` |

**No database.** The server keeps rooms, players and matches in memory (ADR-027); there is nothing to
persist until ratings arrive at M5. **No CDN.** DNS is Hostinger's own (`dns-parking.com`), not
Cloudflare, so nothing sits in front of nginx re-compressing bodies and there is no cache to purge.

## Unity build settings that hosting depends on

| Setting | Value |
|---|---|
| Compression Format | **Brotli** |
| Decompression Fallback | **Off** (we control nginx; fallback bloats the loader) |
| Name Files As Hashes | **On** (lets `/Build/*` be cached forever) |
| Data Caching | On |
| Enable Native C/C++ Multithreading | Off (avoids COOP/COEP headers) |

These live in `MimasClient/Assets/_Game/Editor/WebBuild.cs`, **not** in the "Web Release" build
profile — the profile carries its own PlayerSettings snapshot that disagrees. Build through the
script, which is what `deploy.sh` does.

## The page: the Mimas Web template

`MimasClient/Assets/WebGLTemplates/Mimas/` — Unity's own Default template, copied from the installed
Editor and cut down (21 Sep 2026, D2/D3). The game is the page: a dark full-window canvas that resizes
with the window, a text wordmark, a thin loading bar with a percentage, an SVG favicon, and **no**
footer, fullscreen button, build title or Unity splash.

`WebBuild.ApplyIdentity` states four values on every build, so a build cannot come out wearing the
wrong name: `productName = "Mimas"`, `companyName = "Trinetra"`, `SplashScreen.show = false`,
`WebGL.template = "PROJECT:Mimas"` (`PROJECT:` means a template in this project; `APPLICATION:` would
mean one of Unity's). The build log prints them as `[WebBuild] identity: …`. Note that in batch mode
those values are **not** written back into the saved player settings, so the file in the repo still
reads the old product name — what ships is what the script sets at build time, which is why it is
stated there rather than clicked once in the Editor.

Hosting consequence: `TemplateData/` is copied next to `index.html` with **unhashed** names
(`style.css`, `favicon.svg`), so nginx serves it from the one-hour cache block rather than the
immutable `/Build/` one. No nginx change was needed for the new template — it was already serving
`/TemplateData/` that way, and `svg` comes from `mime.types`.

## nginx config

`tools/deploy/nginx-mimas.conf` — adapted from Unity's official 6000.4 nginx sample plus a WebSocket
proxy. Installed by `vps-setup.sh` to `/etc/nginx/sites-available/mimas.laststep.cloud`. Key points:

- `.wasm.br` must be served with `Content-Type: application/wasm` + `Content-Encoding: br` (enables streaming compilation).
- `.data.br` → `application/octet-stream` + `br`; `.js.br` → `application/javascript` + `br`.
- `gzip off` inside those locations so nginx never re-compresses.
- `add_header` inside a nested `location` **replaces** inherited headers, so `Cache-Control` is repeated.
- There are no `.gz` locations: the build is Brotli with fallback off, so no `.gz` file is ever produced.
- `/ws`: `proxy_http_version 1.1`, `Upgrade`/`Connection` headers, `proxy_read_timeout 3600s`, `proxy_buffering off`.
- The upgrade map writes `$mimas_connection_upgrade`, not the conventional `$connection_upgrade`: every
  file in `sites-enabled/` shares one `http` block, and two maps for one variable is a duplicate-map
  error that would take the other site down with us.
- nginx on the box is **1.24**, so `listen 443 ssl http2;` is right and `http2 on;` (1.25+) is not.
- COOP/COEP headers only if native multithreading is ever enabled.

## Deploying

One command, run by **Rohan** from his PC — PowerShell or Git Bash, the script runs itself in
`bash`. Agents only ever run it with `--dry-run` (ADR-031, decision D4).

```bash
bash tools/deploy/deploy.sh --setup     # once: DNS must already point here
bash tools/deploy/deploy.sh             # every time
bash tools/deploy/deploy.sh --rollback  # put the previous generation back
```

It builds the Web build, **hard-fails above 25 MB** of browser download (`MIMAS_SIZE_LIMIT_MB`; the
build was 12.31 MB on 18 Sep, and the ladder's 13 MB ratchet is a separate, tighter gate), publishes
the server self-contained for `linux-x64`, sends both halves as `tar` over `ssh` — **not `rsync`**,
which Git Bash on Windows does not have — into `.next` directories, and runs `vps-release.sh` on the
far end to swap and restart. Its last act is three curls against the live site: `/health`, `/` (200,
`no-cache`) and the hashed `.wasm.br` (`br` + `application/wasm`).

The VPS is reached only through the ssh alias **`hostinger`** in `~/.ssh/config`. **No address,
username or key path is committed anywhere in this repo**, and nothing should add one.

## Budget

A single small VPS (2 vCPU / 4 GB, ~€4–8/month) is more than enough for thousands of concurrent
turn-based matches; the static build is a few tens of MB. This one is already paid for and Mimas is
its second tenant.
