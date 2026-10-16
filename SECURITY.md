# Security policy

## Supported versions

Only the latest 2.x release receives fixes. The 1.x Python version is frozen (tag `v1.x-python-final`).

## Reporting a vulnerability

Please **do not open a public issue** for security problems.

Use GitHub's private reporting: **Security → Report a vulnerability** on the
[repository page](https://github.com/TheLucXas/GOAM/security/advisories/new).

Include what you found, how to reproduce it and the GOAM version (*Options → Copy report*).
You can expect an acknowledgement within a few days. This is a spare-time project, so fixes are
best-effort, and credit is given in the changelog unless you prefer otherwise.

## Scope and design notes

GOAM runs with your normal user rights and has no network service. Things that matter for security review:

- **Downloads:** only `yt-dlp.exe`, from `github.com/yt-dlp/yt-dlp/releases/latest/download/`, accepted only
  if its SHA-256 equals the entry in the release's `SHA2-256SUMS`. The verified hash is stored next to it
  and re-checked before each run.
- **Processes:** the only program GOAM starts is `yt-dlp.exe` (and Steam for *Launch CS:GO*), always with
  an argument list (never through a shell). Links are validated against YouTube hosts and passed after `--`.
- **Files:** song names are sanitised; rename/delete cannot leave the songs folder; text written into
  `.cfg` files cannot start extra console commands.
- **Privacy:** no telemetry; the diagnostic report redacts user name, machine name and profile paths.
- **Out of scope:** the behaviour of CS:GO itself, yt-dlp's own vulnerabilities (report those upstream),
  and issues that require an attacker who can already write to your profile.
