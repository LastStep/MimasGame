#!/usr/bin/env bash
# One-time (and safely repeatable) setup of the Mimas half of the VPS.
#
# Runs ON THE VPS, as root, from the deploy/ folder under the path below — `deploy.sh --setup`
# uploads that folder and then runs this file. Do not run it from your PC.
#
# Everything Mimas owns lives under /home/mimas/servers/mimas.laststep.cloud/, matching the
# /home/<user>/servers/<subdomain>/ convention the other product on this box already uses.
#
# What it touches, and nothing else:
#   user       mimas (system user, no login shell, home /home/mimas)
#   dirs       /home/mimas/servers/mimas.laststep.cloud/{web,server,deploy}
#   nginx      /etc/nginx/sites-available/mimas.laststep.cloud  + symlink in sites-enabled
#   cert       /etc/letsencrypt/live/mimas.laststep.cloud (certbot certonly --nginx)
#   systemd    /etc/systemd/system/mimas-server.service (enabled, NOT started)
#
# What it never touches: sites-enabled/laststep.cloud and its certificate, the docker containers,
# the webhook service, ufw, or any other user. See ADR-031.
#
# It does not start the server: there is no binary until the first `deploy.sh` run.

set -euo pipefail

HOST="${MIMAS_HOST:-mimas.laststep.cloud}"
BASE="/home/mimas/servers/$HOST"
SITE_AVAILABLE="/etc/nginx/sites-available/$HOST"
SITE_ENABLED="/etc/nginx/sites-enabled/$HOST"
UNIT="/etc/systemd/system/mimas-server.service"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

say() { echo "[setup] $*"; }
die() { echo "[setup] FAILED: $*" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || die "run this as root on the VPS (deploy.sh --setup does that for you)"
for f in nginx-mimas.conf nginx-mimas-bootstrap.conf mimas-server.service; do
    [ -f "$HERE/$f" ] || die "$f is not next to this script; re-run deploy.sh --setup"
done

# ---------------------------------------------------------------- 1. DNS points here
say "1/7 checking that $HOST resolves to this machine"
resolved="$(getent hosts "$HOST" | awk '{print $1}' | sort -u || true)"
mine="$(hostname -I || true)"
if [ -z "$resolved" ]; then
    die "DNS for $HOST does not point here yet; add the A record and wait for it"
fi
match=no
for ip in $resolved; do
    for own in $mine; do
        [ "$ip" = "$own" ] && match=yes
    done
done
if [ "$match" != yes ]; then
    say "  $HOST resolves to: $resolved"
    say "  this machine has:  $mine"
    die "DNS for $HOST does not point here yet; add the A record and wait for it"
fi
say "  ok — $HOST resolves to this machine ($resolved)"

# ---------------------------------------------------------------- 2. the service user
say "2/7 the mimas system user"
if id -u mimas >/dev/null 2>&1; then
    say "  already exists"
else
    useradd --system --home-dir /home/mimas --shell /usr/sbin/nologin mimas
    say "  created"
fi

# ---------------------------------------------------------------- 3. the directories
say "3/7 directories"
mkdir -p "$BASE/web" "$BASE/server" "$BASE/deploy"
# /home/mimas is 755, not the 750 a home directory usually gets. nginx serves the Web build straight
# off disk as www-data, and www-data must be able to traverse every directory on the way to it. The
# other subdomains on this box are all reverse proxies, so this has never had to be true before.
# Nothing under here is secret: it is a public web build and a game server binary.
chown mimas:mimas /home/mimas /home/mimas/servers
chmod 755 /home/mimas /home/mimas/servers
chown root:root "$BASE" "$BASE/web" "$BASE/server" "$BASE/deploy"
chmod 755 "$BASE" "$BASE/web" "$BASE/server" "$BASE/deploy"
say "  $BASE/{web,server,deploy} — root:root 755, under mimas:mimas 755"

# ---------------------------------------------------------------- 4. the certificate
say "4/7 TLS certificate"
if [ -f "/etc/letsencrypt/live/$HOST/fullchain.pem" ]; then
    say "  already issued — leaving it alone (certbot.timer renews it)"
else
    say "  none yet; installing the port-80 bootstrap block so certbot can answer the challenge"
    install -m 644 "$HERE/nginx-mimas-bootstrap.conf" "$SITE_AVAILABLE"
    ln -sfn "$SITE_AVAILABLE" "$SITE_ENABLED"
    nginx -t || die "nginx -t rejected the bootstrap block; nothing else was changed"
    systemctl reload nginx
    say "  requesting the certificate (certonly --nginx; this file is never edited by certbot)"
    certbot certonly --nginx -d "$HOST" --cert-name "$HOST" \
        --non-interactive --agree-tos --keep-until-expiring
    [ -f "/etc/letsencrypt/live/$HOST/fullchain.pem" ] || die "certbot finished but there is no certificate at /etc/letsencrypt/live/$HOST"
    say "  issued"
fi

# ---------------------------------------------------------------- 5. the real nginx block
say "5/7 installing the real nginx block"
backup=""
if [ -f "$SITE_AVAILABLE" ]; then
    backup="$SITE_AVAILABLE.bak"
    cp -a "$SITE_AVAILABLE" "$backup"
    say "  previous file backed up to $backup"
fi
install -m 644 "$HERE/nginx-mimas.conf" "$SITE_AVAILABLE"
ln -sfn "$SITE_AVAILABLE" "$SITE_ENABLED"
if nginx -t; then
    systemctl reload nginx
    say "  installed and nginx reloaded"
else
    say "  nginx -t FAILED — putting the previous file back so the other site stays up"
    if [ -n "$backup" ]; then
        cp -a "$backup" "$SITE_AVAILABLE"
    else
        rm -f "$SITE_ENABLED" "$SITE_AVAILABLE"
    fi
    nginx -t && systemctl reload nginx
    die "the new nginx block is bad; the previous configuration is restored. Send the nginx -t output to the next session"
fi

# ---------------------------------------------------------------- 6. the systemd unit
say "6/7 systemd unit"
install -m 644 "$HERE/mimas-server.service" "$UNIT"
systemctl daemon-reload
systemctl enable mimas-server >/dev/null
say "  installed and enabled (not started — there is no binary until the first deploy)"

# ---------------------------------------------------------------- 7. summary
say "7/7 done. Summary:"
echo
certbot certificates --cert-name "$HOST" 2>/dev/null | sed 's/^/    /' || echo "    (certbot certificates said nothing)"
echo
systemctl is-active nginx | sed 's/^/    nginx: /'
systemctl is-enabled mimas-server | sed 's/^/    mimas-server: /'
echo
say "next: run  bash tools/deploy/deploy.sh  from your PC to put the game here."
