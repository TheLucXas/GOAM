# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [2.0.0] - 2026-10-15

Complete rewrite in C# / .NET 8. The last Python release stays available at the
`v1.x-python-final` tag.

### Added
- Native Windows Forms interface with a virtualized song list (300 songs refresh in milliseconds).
- External, verified `yt-dlp`: downloaded from the official GitHub release on first use, checked
  against the release's `SHA2-256SUMS`, refreshed automatically when a download fails and every 7 days.
- *Copy report* button: version, OS, game/Steam detection, yt-dlp version and recent log lines, with
  personal paths and names redacted.
- Single-instance behaviour (a second launch brings the first window forward).
- Rotating local log file; no telemetry.
- Automatic detection of the CS:GO install in Steam libraries.
- `GOAM_DATA_DIR`, `GOAM_TOOLS_DIR` (and `GOAM_STEAM_DIR`) environment variables for portable use and tests.
- Unit-test suite (200+ tests), UI Automation smoke tests, GitHub Actions CI and tagged-release workflow,
  Dependabot.
- Settings are migrated automatically from the 1.x `config` file.

### Changed
- Audio is decoded with Windows Media Foundation (MP3, WAV, FLAC, M4A) and NVorbis (OGG); no pygame,
  numpy, miniaudio or ffmpeg.
- YouTube audio is requested as M4A (AAC), which the new decoder reads natively. GOAM 1.x asked for
  formats YouTube never offers and then failed to decode the WebM/Opus it received.
- The relay file is watched with a file-system watcher plus a 1-second poll instead of a 200 ms loop.
- Song order is the same in the list and in the generated `.cfg` (1.x sorted them differently when names
  contained spaces).
- Selecting a song while GOAM is stopped no longer writes files into the game folder.
- Changing the volume re-applies it to the active song immediately.
- The CS:GO folder prompt can be dismissed (1.x trapped the user in the dialog).
- Renaming validates the name: path separators and reserved names are rejected.
- Song names are neutralised before being written into `.cfg` files (`;`, `"`, `//`).
- Temporary downloads go to a private folder; GOAM 1.x deleted any `.mp4/.m4a/.webm/...` file it found in
  the songs folder at start-up.

### Fixed
- `voice_input.wav` is written atomically (temp file + replace), so the game never reads a half-written file.

### Removed
- The Python sources (see the `v1.x-python-final` tag), pygame preview, UPX packaging.
