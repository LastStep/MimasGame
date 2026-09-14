# Hosting (self-hosted on Rohan's VPS + website)

## Layout on the VPS

| Piece | Where | How |
|---|---|---|
| Unity Web build | `/var/www/mimas/` (static files) | nginx serves pre-compressed Brotli files with correct headers |
| Game server | `mimas-server` process on `127.0.0.1:7777` | Docker container (see `server/Mimas.Server/Dockerfile`) or systemd; nginx reverse-proxies `/ws` |
| TLS | Let's Encrypt (certbot) | Brotli decoding in browsers requires HTTPS; WebSockets from an HTTPS page must be `wss://` |
| DB | SQLite file on a volume (start) | Move to Postgres only if needed |
| Optional CDN | Cloudflare (orange cloud) | Add `Cache-Control: no-transform`, a Cache Rule for `/Build/*`, disable Rocket Loader; purge cache on deploy |

## Unity build settings that hosting depends on

| Setting | Value |
|---|---|
| Compression Format | **Brotli** |
| Decompression Fallback | **Off** (we control nginx; fallback bloats the loader) |
| Name Files As Hashes | **On** (lets `/Build/*` be cached forever) |
| Data Caching | On |
| Enable Native C/C++ Multithreading | Off (avoids COOP/COEP headers) |

## nginx config

`tools/deploy/nginx-mimas.conf` — adapted from Unity's official 6000.4 nginx sample plus a WebSocket proxy. Key points:

- `.wasm.br` must be served with `Content-Type: application/wasm` + `Content-Encoding: br` (enables streaming compilation).
- `.data.br` → `application/octet-stream` + `br`; `.js.br` → `application/javascript` + `br`.
- `gzip off` inside those locations so nginx never re-compresses.
- `add_header` inside a nested `location` **replaces** inherited headers, so `Cache-Control` is repeated.
- `/ws`: `proxy_http_version 1.1`, `Upgrade`/`Connection` headers, `proxy_read_timeout 3600s`, `proxy_buffering off`.
- COOP/COEP headers only if native multithreading is ever enabled.

## Deploy steps (manual for now; script later)

1. `unity build MimasClient --profile "Web Release" --output-path Build/Web`
2. `rsync -av --delete Build/Web/ user@vps:/var/www/mimas/`
3. `docker build -t mimas-server server/ && docker run -d --restart unless-stopped -p 127.0.0.1:7777:7777 -v /srv/mimas/data:/data mimas-server`
4. Purge CDN cache if used.

## Budget

A single small VPS (2 vCPU / 4 GB, ~€4–8/month) is more than enough for thousands of concurrent turn-based matches; the static build is a few tens of MB.
