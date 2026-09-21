#!/usr/bin/env bash
# Swap a freshly uploaded build into place, on the VPS, as root.
#
# Runs ON THE VPS. `deploy.sh` pipes it over ssh (`ssh hostinger 'bash -s' < vps-release.sh`), so it
# reads no files beside itself and takes everything from these two directories, which deploy.sh has
# just filled:
#
#   /var/www/mimas.next     the Unity Web build
#   /opt/mimas/server.next  the self-contained linux-x64 publish
#
# It keeps exactly one generation back — /var/www/mimas.prev and /opt/mimas/server.prev — which is
# what `deploy.sh --rollback` (this script with --rollback) puts back.
#
# A release stops the server. Any match in flight is lost and both players see "Match lost", so the
# runbook says to read `rooms` from /health first.

set -euo pipefail

HEALTH="http://127.0.0.1:7777/health"
WEB=/var/www/mimas
SRV=/opt/mimas/server

say() { echo "[release] $*"; }
die() { echo "[release] FAILED: $*" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || die "run this as root (deploy.sh does that for you)"

# Move $1.<from> into place at $1, keeping the current one as $1.prev. One generation, no more.
swap_in() {
    local base="$1" from="$2"
    [ -d "$base.$from" ] || die "$base.$from does not exist"
    rm -rf "$base.prev"
    if [ -d "$base" ]; then
        mv "$base" "$base.prev"
    fi
    mv "$base.$from" "$base"
}

wait_for_health() {
    local i
    for i in $(seq 1 15); do
        if out="$(curl -fsS --max-time 2 "$HEALTH" 2>/dev/null)"; then
            say "healthy after ${i}s:"
            echo "    $out"
            return 0
        fi
        sleep 1
    done
    say "the server never answered $HEALTH after 15s. Its last words:"
    journalctl -u mimas-server -n 40 --no-pager || true
    return 1
}

if [ "${1:-}" = "--rollback" ]; then
    say "rolling back to the previous generation"
    [ -d "$WEB.prev" ] || die "there is no $WEB.prev to roll back to"
    [ -d "$SRV.prev" ] || die "there is no $SRV.prev to roll back to"
    systemctl stop mimas-server || true
    swap_in "$WEB" prev
    swap_in "$SRV" prev
    chmod +x "$SRV/Mimas.Server"
    systemctl start mimas-server
    wait_for_health || die "the previous server does not come up either; ssh in and look at journalctl"
    say "rolled back. The build you just deployed is now in $WEB.prev / $SRV.prev."
    exit 0
fi

# ---------------------------------------------------------------- 1. sanity on the upload
say "1/4 checking the upload"
[ -d "$WEB.next" ] || die "$WEB.next does not exist — did the upload run?"
[ -f "$SRV.next/Mimas.Server" ] || die "$SRV.next/Mimas.Server is missing"
[ -d "$SRV.next/Data" ] || die "$SRV.next/Data is missing — the server refuses to start without content"
[ -f "$WEB.next/index.html" ] || die "$WEB.next/index.html is missing"
chmod +x "$SRV.next/Mimas.Server"
chown -R root:root "$WEB.next" "$SRV.next"
say "  ok"

# ---------------------------------------------------------------- 2. static files
say "2/4 swapping the Web build into $WEB"
swap_in "$WEB" next
say "  done (previous build kept at $WEB.prev)"

# ---------------------------------------------------------------- 3. the server
say "3/4 restarting the game server"
systemctl stop mimas-server || true     # first release: the unit has never run
swap_in "$SRV" next
systemctl start mimas-server
say "  started"

# ---------------------------------------------------------------- 4. does it answer
say "4/4 health check"
if ! wait_for_health; then
    die "the new server does not answer. The static files are already swapped; 'deploy.sh --rollback' undoes both."
fi

say "released."
