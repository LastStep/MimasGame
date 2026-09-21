#!/usr/bin/env bash
# Deploy Mimas to https://mimas.laststep.cloud.
#
#   bash tools/deploy/deploy.sh                 build, publish, upload, release, health check
#   bash tools/deploy/deploy.sh --skip-build    reuse Build/Web (the last Web build)
#   bash tools/deploy/deploy.sh --dry-run       print every command, run none of the remote ones
#   bash tools/deploy/deploy.sh --setup         one-time: upload tools/deploy to the VPS and run vps-setup.sh
#   bash tools/deploy/deploy.sh --rollback      put the previous build and server back
#   bash tools/deploy/deploy.sh --smoke         after release, run the browser smoke against the live URL
#
# Runs on this PC, from the repo root. Invoke it as `bash tools/deploy/deploy.sh` from PowerShell,
# Git Bash or WSL — Git's bash is on the Windows PATH, and the script runs itself in bash, so the
# caller's shell does not matter. (Windows PowerShell 5.1 has no `&&`, which is why nothing here asks
# the caller to chain commands.) Rohan runs it; agents only ever run it with --dry-run (D4, 22 Sep).
#
# The VPS is reached only through the ssh alias below, which lives in ~/.ssh/config. No address, no
# username and no key path appears anywhere in this repo — that is deliberate, do not "helpfully" add
# one. The runbook is docs/deploy-runbook.md.

set -euo pipefail

# Everything overridable from the environment, so nothing has to be edited to point somewhere else.
MIMAS_SSH="${MIMAS_SSH:-hostinger}"
MIMAS_HOST="${MIMAS_HOST:-mimas.laststep.cloud}"
MIMAS_SIZE_LIMIT_MB="${MIMAS_SIZE_LIMIT_MB:-25}"
# Everything Mimas owns on the VPS lives under one folder, matching the
# /home/<user>/servers/<subdomain>/ convention the other product on that box already uses.
MIMAS_REMOTE_BASE="${MIMAS_REMOTE_BASE:-/home/mimas/servers/$MIMAS_HOST}"

# Git Bash rewrites anything that looks like a unix path — a remote /home/... path becomes
# C:/Program Files/Git/home/... — which would corrupt every remote command and the Unity build's
# output path. Off for the whole script.
export MSYS_NO_PATHCONV=1

WEB_DIR=Build/Web
SRV_DIR=Build/Server
DRY=no
SKIP_BUILD=no
SMOKE=no
MODE=deploy

usage() { sed -n '2,9p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; }

for arg in "$@"; do
    case "$arg" in
        --dry-run)    DRY=yes ;;
        --skip-build) SKIP_BUILD=yes ;;
        --setup)      MODE=setup ;;
        --rollback)   MODE=rollback ;;
        --smoke)      SMOKE=yes ;;
        --help|-h)    usage; exit 0 ;;
        *) echo "unknown option: $arg" >&2; usage >&2; exit 2 ;;
    esac
done

note() { echo; echo "[deploy] $*"; }
die()  { echo "[deploy] FAILED: $*" >&2; exit 1; }

# Prints the command, then runs it — unless this is a dry run and the command talks to the VPS or the
# live site, in which case it is only printed. The printed list is therefore exactly what a real run
# would do, in order.
remote() {
    if [ "$DRY" = yes ]; then echo "[dry-run] $1"; else echo "[deploy] \$ $1"; eval "$1"; fi
}
localcmd() {
    if [ "$DRY" = yes ]; then echo "[local]   $1"; else echo "[deploy] \$ $1"; fi
    eval "$1"
}

# The deploy's own verdict: three curls against the live site. Used by a deploy and by a rollback.
# $1 = "skip-wasm" after a rollback: the live build is then the PREVIOUS one, whose hashed file
# names do not match the local Build/Web, so that one check would 404 on a perfectly good rollback.
verdict() {
    local skip_wasm="${1:-no}"
    note "8. checking the live site"
    remote "curl -fsS \"https://$MIMAS_HOST/health\""
    if [ "$DRY" = yes ]; then
        echo "[dry-run] curl -sI \"https://$MIMAS_HOST/\"   # assert 200 and Cache-Control: no-cache"
        echo "[dry-run] curl -sI \"https://$MIMAS_HOST/Build/<hash>.wasm.br\"   # assert content-encoding: br and content-type: application/wasm"
        return 0
    fi
    local page whead wasm_path
    page=$(curl -sI "https://$MIMAS_HOST/")
    echo "$page" | grep -qi '^HTTP/.* 200'             || { echo "$page"; die "the page did not answer 200"; }
    echo "$page" | grep -qi 'cache-control:.*no-cache' || { echo "$page"; die "/ is missing Cache-Control: no-cache"; }
    echo "[deploy] / is 200 and no-cache"
    [ "$skip_wasm" = skip-wasm ] && return 0

    # The hashed wasm, read out of the loader config in index.html, is the one file that proves the
    # Brotli headers are right. Without content-type: application/wasm the browser cannot stream it.
    wasm_path=$(grep -o '/[A-Za-z0-9]\{8,\}\.wasm\.br' "$WEB_DIR/index.html" | head -1)
    [ -n "$wasm_path" ] || die "could not find the hashed .wasm.br in $WEB_DIR/index.html"
    whead=$(curl -sI "https://$MIMAS_HOST/Build${wasm_path}")
    echo "$whead" | grep -qi 'content-encoding: *br'           || { echo "$whead"; die "the wasm is not served with Content-Encoding: br"; }
    echo "$whead" | grep -qi 'content-type: *application/wasm' || { echo "$whead"; die "the wasm is not served as application/wasm"; }
    echo "[deploy] Build${wasm_path} is br + application/wasm"
}


# ---------------------------------------------------------------- 0. the tools this needs
# Checked before anything slow or remote happens, so a missing tool costs a second rather than an
# ssh round trip and a three-minute build.
UNITY_BIN=unity
have() { command -v "$1" >/dev/null 2>&1; }

for t in tar curl ssh; do
    have "$t" || die "'$t' is not on PATH. In Git Bash and in PowerShell it should be; if it is not, reopen the terminal."
done
have dotnet || die "'dotnet' is not on PATH (expected 10.0.203). Reopen the terminal, or see docs/deploy-runbook.md §0."

if [ "$SKIP_BUILD" != yes ]; then
    if ! have unity; then
        # Unity Hub installs the CLI here and adds it to the USER PATH. A terminal opened before that
        # happened carries a stale copy of the environment and cannot see it, which looks exactly like
        # "Unity is not installed". Use it anyway and say so, rather than failing on a technicality.
        #
        # $HOME is used rather than $LOCALAPPDATA on purpose: under Git Bash, $HOME is already a unix
        # path (/c/Users/name) while $LOCALAPPDATA is a Windows one (C:\Users\name\AppData\Local), and
        # converting the latter means backslash-escaping that is easy to get subtly wrong.
        unity_fallback="$HOME/AppData/Local/Unity/bin/unity.exe"
        if [ -x "$unity_fallback" ]; then
            UNITY_BIN="$unity_fallback"
            echo "[deploy] note: 'unity' is not on this shell's PATH, but the CLI is installed."
            echo "[deploy]       Using $UNITY_BIN"
            echo "[deploy]       Open a new terminal and it will be found normally — the PATH entry"
            echo "[deploy]       Unity Hub added is newer than this shell."
        else
            die "'unity' is not on PATH and the CLI is not at $unity_fallback. Open a new terminal (the PATH entry may be newer than this shell), or run with --skip-build to reuse $WEB_DIR."
        fi
    fi
fi

if [ "$SMOKE" = yes ]; then
    have node || die "'node' is not on PATH, and --smoke needs it. Reopen the terminal, or drop --smoke."
fi

# ---------------------------------------------------------------- 1. where are we
[ -f Mimas.slnx ] || die "run this from the repo root (Mimas.slnx is not here)"
if [ -n "$(git status --porcelain 2>/dev/null)" ]; then
    note "WARNING: the working tree is dirty. The build's provenance file will say dirty: true."
    git status --short | sed 's/^/    /'
fi

# ---------------------------------------------------------------- 2. can we reach the VPS
note "2. checking the ssh alias '$MIMAS_SSH'"
ssh -o BatchMode=yes -o ConnectTimeout=10 "$MIMAS_SSH" true \
    || die "the ssh alias $MIMAS_SSH does not work; see docs/deploy-runbook.md §0"
echo "[deploy] ok"

# ---------------------------------------------------------------- --setup
if [ "$MODE" = setup ]; then
    note "one-time VPS setup"
    remote "ssh \"$MIMAS_SSH\" 'mkdir -p $MIMAS_REMOTE_BASE/deploy'"
    remote "tar -C tools/deploy --format=ustar -czf - nginx-mimas.conf nginx-mimas-bootstrap.conf mimas-server.service vps-setup.sh | ssh \"$MIMAS_SSH\" 'tar -C $MIMAS_REMOTE_BASE/deploy -xzf -'"
    remote "ssh \"$MIMAS_SSH\" 'bash $MIMAS_REMOTE_BASE/deploy/vps-setup.sh'"
    note "setup finished. Next: bash tools/deploy/deploy.sh"
    exit 0
fi

# ---------------------------------------------------------------- --rollback
if [ "$MODE" = rollback ]; then
    note "rolling the VPS back one generation"
    remote "ssh \"$MIMAS_SSH\" 'bash -s' -- --rollback < tools/deploy/vps-release.sh"
    verdict skip-wasm
    exit 0
fi

# ---------------------------------------------------------------- 3. the Unity Web build
if [ "$SKIP_BUILD" = yes ]; then
    note "3. skipping the Unity build (--skip-build); reusing $WEB_DIR"
else
    note "3. building the Unity Web build (about 3 minutes)"
    # Exactly the invocation that worked on 18 Sep (R-2026-09-18-T-0003). The "Web Release" build
    # PROFILE is not used: it carries its own PlayerSettings snapshot (exception support, the
    # template) that disagrees with WebBuild.cs. The relative output path is what worked; if a future
    # Unity CLI lands the build elsewhere, pass "$(pwd -W 2>/dev/null || pwd)/Build/Web" instead.
    localcmd "\"$UNITY_BIN\" build MimasClient --target WebGL --execute-method Mimas.Client.Editor.WebBuild.Build --output-path $WEB_DIR"
fi
[ -f "$WEB_DIR/index.html" ] || die "$WEB_DIR/index.html is missing — the Web build did not land here"
wasm_count=$(find "$WEB_DIR/Build" -maxdepth 1 -name '*.wasm.br' | wc -l)
[ "$wasm_count" -eq 1 ] || die "expected exactly one *.wasm.br under $WEB_DIR/Build, found $wasm_count. WebBuild.Build cleans stale hashes, so more than one means something is wrong — clear $WEB_DIR and build again"

# ---------------------------------------------------------------- 4. the size gate (ladder rung 9)
note "4. size gate"
bytes=$(find "$WEB_DIR/Build" -maxdepth 1 -type f -printf '%s\n' | awk '{t+=$1} END {print t+0}')
mb=$(awk -v b="$bytes" 'BEGIN { printf "%.2f", b/1048576 }')
echo "[deploy] $WEB_DIR/Build is $mb MB ($bytes bytes), limit ${MIMAS_SIZE_LIMIT_MB} MB"
over=$(awk -v m="$mb" -v l="$MIMAS_SIZE_LIMIT_MB" 'BEGIN { print (m > l) ? 1 : 0 }')
[ "$over" -eq 0 ] || die "rung 9 gate: build is $mb MB, limit ${MIMAS_SIZE_LIMIT_MB} MB"

# ---------------------------------------------------------------- 5. the Linux server
note "5. publishing the server for linux-x64, self-contained"
localcmd "dotnet publish server/Mimas.Server -c Release -r linux-x64 --self-contained true -o $SRV_DIR"
[ -f "$SRV_DIR/Mimas.Server" ]    || die "$SRV_DIR/Mimas.Server is missing after publish"
[ -f "$SRV_DIR/Data/rules.json" ] || die "$SRV_DIR/Data/rules.json is missing — the server will refuse to start"
srv_mb=$(du -sm "$SRV_DIR" | awk '{print $1}')
echo "[deploy] $SRV_DIR is ${srv_mb} MB (not gated; only the browser download is)"

# ---------------------------------------------------------------- 6. upload
note "6. uploading"
started=$(date +%s)
# --format=ustar because Git Bash's tar otherwise writes pax headers Ubuntu's tar is noisy about.
remote "tar -C $WEB_DIR --format=ustar -czf - . | ssh \"$MIMAS_SSH\" 'rm -rf $MIMAS_REMOTE_BASE/web.next && mkdir -p $MIMAS_REMOTE_BASE/web.next && tar -C $MIMAS_REMOTE_BASE/web.next -xzf -'"
remote "tar -C $SRV_DIR --format=ustar -czf - . | ssh \"$MIMAS_SSH\" 'rm -rf $MIMAS_REMOTE_BASE/server.next && mkdir -p $MIMAS_REMOTE_BASE/server.next && tar -C $MIMAS_REMOTE_BASE/server.next -xzf -'"
[ "$DRY" = yes ] || echo "[deploy] uploaded $mb MB of build and ${srv_mb} MB of server in $(( $(date +%s) - started )) s"

# ---------------------------------------------------------------- 7. release
note "7. swapping it in on the VPS"
remote "ssh \"$MIMAS_SSH\" 'bash -s' < tools/deploy/vps-release.sh"

# ---------------------------------------------------------------- 8. the deploy's own verdict
verdict

# ---------------------------------------------------------------- 9. optional browser smoke
if [ "$SMOKE" = yes ]; then
    note "9. browser smoke against the live site"
    # ?room=ZZZZ on purpose: the client opens no socket until the player does something, so a bare
    # page load can never print "[NetClient] connected". An invite link auto-joins, which drives the
    # whole round trip over the real origin — connect, authenticate, and the server's no_such_room
    # answer for a code nobody owns. That last one is a console WARNING, so the smoke stays green.
    remote "node tools/smoke/browser-smoke.mjs --url \"https://$MIMAS_HOST/?room=ZZZZ\" --expect \"\\[NetClient\\] connected\" --timing --shot artifacts/smoke-live"
elif [ "$DRY" = yes ]; then
    echo "[dry-run] (--smoke would run: node tools/smoke/browser-smoke.mjs --url \"https://$MIMAS_HOST/?room=ZZZZ\" --expect \"\\[NetClient\\] connected\" --timing --shot artifacts/smoke-live)"
fi

note "done. https://$MIMAS_HOST/"
if [ "$DRY" = yes ]; then
    echo "[deploy] that was a dry run: nothing on the VPS was touched."
fi
exit 0
