# Deploy runbook — putting Mimas on the internet

**Address:** `https://mimas.laststep.cloud` · **Machine:** the Hostinger VPS you already pay for, next
to `laststep.cloud` · **Who runs this:** you. Agents only ever rehearse it with `--dry-run`.

You do §1 and §2 **once**. After that, deploying is §3: one command.

---

## 0. Before anything — is this PC ready?

**Which shell.** PowerShell **or** Git Bash both work for the deploy commands in §2, §3 and §5,
because Git's `bash` and `ssh` are already on your Windows PATH. Two things to know:

- Your PowerShell is **5.1**, which does **not** understand `&&`. If you paste a line containing
  `&&` into it you get `The token '&&' is not a valid statement separator in this version.` Nothing
  runs — it fails while reading the line, before doing anything. Every command below is written one
  per line so this cannot bite.
- The **appendix in §8 is Git Bash only** — it pipes a tar archive into ssh, and PowerShell pipes
  objects rather than bytes, which would corrupt the upload. The normal path (§3) is safe from either
  shell, because the whole pipeline runs inside `bash`.

Run these four, one line at a time. Each should print a version, not an error.

```
ssh -o BatchMode=yes hostinger true
dotnet --version
unity --version
node --version
```

Expected: `10.0.203`, `1.0.0-beta.9`, `v22.14.0` — and the `ssh` line prints **nothing at all**, which
is what success looks like. If it asks for a password or says `Could not resolve hostname hostinger`,
the alias in `C:\Users\droha\.ssh\config` is broken and nothing below will work.

If you want the ssh line to say so out loud:

```powershell
ssh -o BatchMode=yes hostinger true; if ($LASTEXITCODE -eq 0) { "ssh ok" } else { "ssh FAILED" }
```

```bash
ssh -o BatchMode=yes hostinger true && echo "ssh ok"    # Git Bash
```

---

## 1. The DNS record (once)

The name has to point at the VPS before a certificate can be issued for it.

1. Find the VPS's address. In Git Bash:

   ```bash
   nslookup laststep.cloud
   ```

   The `Address:` it prints (the one that is not your router) is the VPS.

2. In **Hostinger hPanel → Domains → DNS Zone** for `laststep.cloud`, add:

   | Type | Name | Points to | TTL |
   |---|---|---|---|
   | A | `mimas` | that address | default |

3. Wait, then check it took:

   ```bash
   nslookup mimas.laststep.cloud
   ```

   It must answer with the same address. This is usually minutes; it can be an hour. **Nothing else
   works until it does** — the set-up script checks and refuses rather than half-doing it.

---

## 2. Set up the VPS (once)

```bash
bash tools/deploy/deploy.sh --setup
```

It uploads `tools/deploy/` to the VPS and runs the set-up script there. What it makes:

| Thing | Where | Note |
|---|---|---|
| A user for the game | `mimas` | system user, cannot log in |
| Two folders | `/var/www/mimas`, `/opt/mimas/server` | the web files, and the server program |
| An nginx site | `/etc/nginx/sites-available/mimas.laststep.cloud` | its own file |
| A certificate | Let's Encrypt, for `mimas.laststep.cloud` only | renews itself, like your other one |
| A service | `mimas-server` | starts on boot, restarts if it crashes |

What it deliberately does **not** touch: your `laststep.cloud` site and its certificate, the Docker
containers, the webhook service, the firewall, or any other user. If installing our nginx file were
to break nginx, the script puts the old one back before it stops.

When it is done it prints the certificate's expiry date, says nginx is running, and ends with
`next: run bash tools/deploy/deploy.sh from your PC`. The game is not there yet — that is §3.

---

## 3. Deploy (every time)

From the repo root, in PowerShell or Git Bash — either is fine, the script runs itself in `bash`:

```
bash tools/deploy/deploy.sh
```

Nine steps, printed as it goes. Roughly:

| Step | What | How long |
|---|---|---|
| 2 | checks the ssh alias | instant |
| 3 | builds the Unity Web build | **~3 minutes** — the slow one |
| 4 | refuses if that build is over 25 MB | instant (it was 12.31 MB on 18 Sep) |
| 5 | builds the Linux server | ~40 s |
| 6 | sends both to the VPS | ~30 s |
| 7 | swaps them in and restarts the server | ~5 s |
| 8 | checks the live site | ~2 s |

`bash tools/deploy/deploy.sh --skip-build` reuses the last Web build — use it when you changed only
the server. `--smoke` also drives a real browser against the live site afterwards.

**The three lines at the end that mean it worked:**

```
[deploy] / is 200 and no-cache
[deploy] Build/<hash>.wasm.br is br + application/wasm
[deploy] done. https://mimas.laststep.cloud/
```

with the server's own `{"ok":true,...}` printed just above them.

---

## 4. Check it yourself

Open `https://mimas.laststep.cloud`. Loading bar, then the lobby. Press **Play vs bot** and take a
turn.

Then open a second browser — an incognito window, or your phone on mobile data, which is the honest
test because it is a different network. In the first, **Create room** and **Copy link**; paste the
link into the second. Both seats press Ready.

If the game server is down but the page is up, the lobby's status line says
**"Server unreachable · retrying"** — a sentence, not a spinner. That is what a friend sees; it is
already in the client, it is not something the deploy adds.

---

## 5. Rolling back

```bash
bash tools/deploy/deploy.sh --rollback
```

Puts back the previous Web build **and** the previous server — the VPS keeps exactly one generation.
Run it twice and you are back where you started, not two versions back.

---

## 6. Looking at the server

```bash
ssh hostinger journalctl -u mimas-server -f     # live log, Ctrl-C to stop
ssh hostinger systemctl status mimas-server     # is it running?
curl https://mimas.laststep.cloud/health        # what it thinks about itself
```

`/health` prints `rooms` — **how many matches are being played right now**. A deploy stops the
server, so any match in flight is lost and both players see "Match lost". If `rooms` is not 0 and you
are not in the match yourself, wait.

---

## 7. When something goes wrong

| What you see | What it is | What to do |
|---|---|---|
| `DNS for mimas.laststep.cloud does not point here yet` | §1 has not propagated | Wait, re-check `nslookup`, run `--setup` again. Safe to repeat |
| certbot fails to issue | Same cause, nearly always | As above |
| `nginx -t` failed, previous file restored | Our nginx file is bad | Your other site is fine — the script put the old file back. Send the output to the next Claude session |
| `502 Bad Gateway` on the page | The page is served, the game server is not running | `ssh hostinger systemctl status mimas-server`, then `journalctl -u mimas-server -n 40` |
| Page loads, lobby says "Server unreachable · retrying" | The `/ws` proxy or the server | Same two commands. If the server is healthy, it is nginx not forwarding the upgrade |
| Units are magenta, props are pink | A stale Web build | Deploy again **without** `--skip-build` |
| `rung 9 gate: build is X MB, limit 25 MB` | The build grew past the limit | Nothing to do on the VPS. Tell the next session; it is a real regression |
| Certificate warning in the browser | The address bar must say exactly `mimas.laststep.cloud` | The certificate covers that name only, not `www.` and not the bare domain |
| `the ssh alias hostinger does not work` | §0 | Fix `~/.ssh/config` before anything else |

---

## 8. Appendix — doing it by hand

For the day the script dies halfway. On the VPS, as root:

```bash
# set-up, in order (this is vps-setup.sh)
getent hosts mimas.laststep.cloud                     # must be this machine
useradd --system --home-dir /opt/mimas --shell /usr/sbin/nologin mimas
mkdir -p /opt/mimas/server /opt/mimas/deploy /var/www/mimas
install -m 644 /opt/mimas/deploy/nginx-mimas-bootstrap.conf /etc/nginx/sites-available/mimas.laststep.cloud
ln -sfn /etc/nginx/sites-available/mimas.laststep.cloud /etc/nginx/sites-enabled/mimas.laststep.cloud
nginx -t && systemctl reload nginx
certbot certonly --nginx -d mimas.laststep.cloud --cert-name mimas.laststep.cloud \
        --non-interactive --agree-tos --keep-until-expiring
install -m 644 /opt/mimas/deploy/nginx-mimas.conf /etc/nginx/sites-available/mimas.laststep.cloud
nginx -t && systemctl reload nginx
install -m 644 /opt/mimas/deploy/mimas-server.service /etc/systemd/system/
systemctl daemon-reload && systemctl enable mimas-server

# a release, in order (this is vps-release.sh)
chmod +x /opt/mimas/server.next/Mimas.Server
rm -rf /var/www/mimas.prev && mv /var/www/mimas /var/www/mimas.prev && mv /var/www/mimas.next /var/www/mimas
systemctl stop mimas-server
rm -rf /opt/mimas/server.prev && mv /opt/mimas/server /opt/mimas/server.prev && mv /opt/mimas/server.next /opt/mimas/server
systemctl start mimas-server
curl -fsS http://127.0.0.1:7777/health
```

And from this PC, the two uploads the script does — **in Git Bash, not PowerShell**: these pipe a tar
archive into `ssh`, and PowerShell's pipeline carries objects rather than raw bytes, so it would
arrive corrupted.

```bash
tar -C Build/Web --format=ustar -czf - . | ssh hostinger 'rm -rf /var/www/mimas.next && mkdir -p /var/www/mimas.next && tar -C /var/www/mimas.next -xzf -'
tar -C Build/Server --format=ustar -czf - . | ssh hostinger 'rm -rf /opt/mimas/server.next && mkdir -p /opt/mimas/server.next && tar -C /opt/mimas/server.next -xzf -'
```

---

## 9. What to send friends

The bare link:

```
https://mimas.laststep.cloud
```

Or, from inside a room, press **Copy link** — that carries the room code, so they land in your room
instead of having to type four letters.
