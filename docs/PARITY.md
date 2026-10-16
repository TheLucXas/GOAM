# Feature parity with GOAM 1.x (Python)

Checklist built from reading every function of `goam.py` at tag `v1.x-python-final`.
`[x]` = present in 2.0. "Changed" lists deliberate differences.

## Interface

- [x] Main window: title, subtitle, dark theme, status line, song list (980x820 default, resizable)
- [x] "GOAM ACTIVE / STOPPED" indicator with Start/Stop toggle
- [x] "Track: N — name" label
- [x] *Launch CS:GO* button (`steam.exe -applaunch 4465480 +exec goam.cfg`; Steam found via registry)
- [x] Volume slider 0-200 % with dB readout
- [x] YouTube link box, *Download & Convert*, *Import local file(s)*, *Cancel*
- [x] Status messages (analyzing, downloading %, converting, importing i/n, processing i/n, cancelling, success)
- [x] Song list with per-song **Select/Active**, **Preview**, **Rename**, **Delete** (confirmation on delete)
- [x] Header buttons: play key, options, language
- [x] Language toggle ES/EN, system language detected on first start, persisted
- [x] Minimize to tray (tray menu *Show / Quit*), restore, tray created only when minimizing

## Songs and audio

- [x] Convert MP3 / WAV / FLAC / OGG to WAV 22050 Hz, mono, PCM 16-bit
- [x] Import multiple local files; originals are never deleted
- [x] File name from title: non-alphanumerics become `_`, edges trimmed, max 100 chars
- [x] Newly added song is auto-selected
- [x] Rename (extension kept as `.wav`) and delete
- [x] Preview: first 5 s, volume follows the slider (linear up to 100 %, dB gain above), click again to stop,
      another song switches
- [x] Volume curve identical: 0 % = -100 dB, 1-79 % = -49.75 … -30.25 dB, 80-100 % = -30 … -25 dB,
      101-200 % = +0.08 … +8 dB (note the jump from -25 dB at 100 % to +0.08 dB at 101 % exists in 1.x too)
- [x] Fallback: if decoding for `voice_input.wav` fails, the raw song file is copied

## YouTube

- [x] Link validation (`is_valid_youtube_url`) and cleaning to `watch?v=<id>` (`clean_youtube_url`)
- [x] Playlist detection with *Single video / Full playlist / Cancel* dialog
- [x] Download with progress and cancellation; playlist items processed one by one
- [x] No ffmpeg required

## Game integration

- [x] `voice_input.wav` in the CS:GO folder
- [x] `goam.cfg` (aliases, `bind <play key> goam_play`, banner, per-song aliases), `goam_tracks.cfg`,
      `current_track.cfg` with the same commands, CRLF line endings
- [x] `voice_inputfromfile` / `voice_loopback` / `+voicerecord` play toggle
- [x] Relay: `bind "<relay>" "<N>"` file observed; change copies the selected song; initial track restored
      from it at start-up
- [x] Cleanup of `voice_input.wav` and the cfgs on *Stop GOAM* and on exit
- [x] Play-key capture dialog with the same Windows-key → Source-key names
- [x] Relay key typed manually (`=`, `kp_ins`, `mouse4` ...), lower-cased

## Options and persistence

- [x] CS:GO folder (must contain `csgo.exe`), songs folder, relay key
- [x] Start with Windows (HKCU Run value `GOAM`, `--startup`)
- [x] Start minimized / start minimized only with Windows (mutually exclusive) / minimize to tray
- [x] Reload songs, reset configuration (closes the app)
- [x] First-run setup: custom songs folder question, CS:GO folder
- [x] Settings persisted on every change; 1.x `config` file migrated automatically

## Changed on purpose

| Area | 1.x | 2.0 | Why |
|---|---|---|---|
| YouTube format | `bestaudio[ext=mp3]/ogg/flac/bestaudio` → WebM/Opus, then "unsupported format" | `bestaudio[ext=m4a]/…` | miniaudio cannot read Opus/AAC; verified with yt-dlp 2026.08.19, so downloads in 1.x failed |
| yt-dlp | embedded in the exe | external, SHA-256 verified, auto-updated | YouTube breaks old builds every few weeks |
| Preview engine | pygame (winsound fallback) | NAudio | one audio stack |
| Song list | ~6 widgets per song, full repack on each change | virtual `DataGridView` | 300 songs: 14 s → 8 ms |
| Relay polling | `stat()` every 200 ms | FileSystemWatcher + 1 s poll | less wake-ups, same latency |
| Sorting | list by file name, cfg by stem (differed with spaces) | one order everywhere | list numbers always match in-game numbers |
| Temp cleanup | deleted `.mp4/.m4a/.webm/.aac/.part/.ytdl/.tmp` in the **songs** folder at start | private downloads folder only | could delete the user's own files |
| Selecting while stopped | rewrote files in the game folder | selection only | "stopped" means nothing in the game folder |
| Volume slider | applied on the next song change | re-applied immediately | what you hear matches the slider |
| Rename | unvalidated (path traversal possible) | sanitised, confined to songs folder | safety |
| Names in cfg | raw | `;` `"` `//` neutralised | a song name could inject console commands |
| CS:GO dialog | could not be closed at first run | can be dismissed | users without the game were trapped |
| Empty title | file named `.wav` | `song.wav` | |
| Steam location | hard-coded `C:\Program Files (x86)\Steam` | registry + libraries | non-default installs |
| Logging | `print()` | rotating log file | |
| Startup flag (`--startup`) | same | same | |

## Not carried over

- pygame / winsound preview fallback (replaced, see above)
- PyInstaller / UPX packaging
