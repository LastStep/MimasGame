---
id: T-0007
title: Deploy — mimas.laststep.cloud over HTTPS, one-command redeploy, a runbook Rohan follows
project: mimas
feature: F-deploy
milestone: M2
lane: full
status: verify
owner: builder
model: opus
worktree:
depends_on: [T-0002, T-0003]
allows_assets:
  # Written by Unity itself during WebBuild.Build (exception support restored, always-included
  # shaders). Expected unchanged; if they show as modified, leave them uncommitted and say so.
  - 'MimasClient/ProjectSettings/ProjectSettings.asset'
  - 'MimasClient/ProjectSettings/GraphicsSettings.asset'
done_when:
  - "server/Mimas.Server honours MIMAS_BIND=loopback and logs its bind at startup; Core and server tests stay green"
  - "tools/deploy/ holds nginx-mimas.conf for mimas.laststep.cloud, nginx-mimas-bootstrap.conf, mimas-server.service, vps-setup.sh, vps-release.sh and deploy.sh; every script passes bash -n and none contains an IP, a username or a key path"
  - "bash tools/deploy/deploy.sh --dry-run exits 0 and prints the command list in docs/specs/2026-09-22-deploy.md §5.6; MIMAS_SIZE_LIMIT_MB=1 makes it exit 1"
  - "The linux-x64 self-contained publish answers /health inside WSL with the same content hash the Windows server logs"
  - "node tools/smoke/browser-smoke.mjs --timing prints a [timing] line and exits 0 against a locally served Build/Web"
  - "docs/deploy-runbook.md exists with the nine sections in spec §7; docs/hosting.md no longer describes Docker, rsync or mimas.example.com; ADR-031 is in docs/decisions.md"
  - "Part 3, after Rohan deploys: the live smoke exits 0 three times, headers match spec §1, and docs/roadmap.md has a cold-load baseline"
ladder: [0, 1, 2, 5]
created: 2026-09-22
started: 2026-09-22
finished: 2026-09-21
cost_usd: 0
blocked_by:
---

# Deploy — a link a friend can open

## Context

Everything else in M2 is done or a small task; deploy is the only thing that can slip 10 Oct
(`studio/STATE.md`). The spec is `docs/specs/2026-09-22-deploy.md`: read it whole, it is the work
order. Rohan decided on 22 Sep that he runs the deploy himself from a script, with a runbook for the VPS
side, and that agents only ever `--dry-run`; the spec's §0 rule 2 is that decision. The VPS survey in
spec §3 is what an agent would otherwise spend the first hour rediscovering.

## Scope

Three parts, one task. Part 1 (this builder, unattended): the server bind flag, the six files in
`tools/deploy/`, `--timing` in the smoke harness, the runbook, the doc rewrites, and the local proof in
spec §8. Part 2: Rohan, from the runbook. Part 3 (a short builder session): the live smoke, the
cold-load baseline, runbook lessons, hand to the verifier.

## Out of scope

The custom Web template, Docker, a CDN, monitoring, GitHub Actions, a WebSocket origin check, any
change to gameplay or the client, and anything on the VPS that is not Mimas's own nginx file, unit,
user and directories. The `laststep.cloud` nginx file is never edited.

## Notes

- Git Bash on this PC has no `rsync`; the script uses `tar | ssh tar`.
- The lobby already says "Server unreachable · retrying" when the socket fails (`LobbyView.cs` 430);
  the one-pager's "a sentence, not a spinner" is verified, not built.
- nginx on the VPS is 1.24: `listen 443 ssl http2;` is the right syntax; `http2 on;` is not.
