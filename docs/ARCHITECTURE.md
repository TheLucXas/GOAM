# Architecture

GOAM 2 is a Windows-only C# / .NET 8 application split into a UI-free core and a thin Windows Forms shell.

```
                   +----------------------------- GOAM.App (WinForms) ---------------------------+
                   |  MainForm  OptionsForm  Dialogs  SongGrid  tray  single instance  AppServices |
                   +--------------------------------------+---------------------------------------+
                                                          | uses
+--------------------------------------------- GOAM.Core (no UI) --------------------------------------------+
|  GoamEngine ── Songs (SongLibrary, SongNames, SongImporter)                                                 |
|      │         Audio (AudioDecoder, AudioConverter, Volume, WavFile, PreviewPlayer)                         |
|      │         Game  (CfgBuilder, GameFiles, RelayParser, RelayWatcher, SteamLocator)                      |
|      │         YouTube (YouTubeUrl, YtDlpManager, YtDlpClient, ProcessRunner)                               |
|      │         Config (AppConfig, ConfigStore, LegacyConfig, GameKeys)  Localization  Platform  Diagnostics |
+--------------------------------------------------------------------------------------------------------------+
```

## Why Windows Forms (and not WPF)

| | WinForms | WPF |
|---|---|---|
| Cold start | measured: 0.2-0.35 s to a visible window (self-contained; see `PERFORMANCE.md`) | not measured here; loads the XAML/composition stack, which is heavier by design |
| Virtualised list | `DataGridView` in virtual mode, native | `ListView` + virtualisation |
| Look | classic controls, custom-painted dark theme | richer styling |

Not having a WPF build to compare against, the choice rests on the measured WinForms numbers meeting the
targets (window < 1 s, 300-row refresh without blocking) with a much smaller dependency surface.

The app has one window and a list. Start-up time and a cheap 300-row list were the explicit goals, so the
simpler toolkit wins. The song list is a `DataGridView` with `VirtualMode = true`: only the rows on screen
are asked for values and painted, so 300 songs cost the same as 10.

## Projects

| Project | Responsibility |
|---|---|
| `src/GOAM.Core` | All behaviour. Targets `net8.0-windows` only because Media Foundation and the registry are Windows APIs; it references no UI assembly. |
| `src/GOAM.App` | Windows Forms shell: composition root (`AppServices`), forms, tray, single-instance mutex, `--screenshot` / `--benchmark` helpers. |
| `tests/GOAM.Core.Tests` | xUnit. Uses `tests/assets` (tiny synthetic tones) and temporary folders; never the registry or the real AppData. |

## Runtime flow

1. `Program.Main` resolves `AppPaths` (honouring `GOAM_DATA_DIR` / `GOAM_TOOLS_DIR`), takes a per-data-folder
   mutex, builds `AppServices` and shows `MainForm`. Nothing heavy runs before the window is visible.
2. After the window is shown, `GoamEngine.Initialize()` runs on a worker thread: it lists songs, writes the
   cfg files, restores the active track from the relay file and starts the `RelayWatcher`.
3. **Relay.** The generated `goam.cfg` defines `alias N "bind <relay> N; update_cfg; echo ..."` and
   `alias update_cfg "host_writeconfig goam_relay_key"`. Typing `N` in the console makes the game write
   `userdata\<id>\4465480\local\cfg\goam_relay_key.cfg`. `RelayWatcher` (FileSystemWatcher with a 150 ms
   debounce + a 1 s poll) notices the change; `RelayParser` extracts `N`; `GoamEngine.SelectSong(N)` decodes
   the song, applies the dB gain and atomically replaces `voice_input.wav`.
4. **Close / stop.** `voice_input.wav` and the three cfg files are deleted.

### Threading

- UI thread: painting and input only. Every engine call from the UI goes through `Task.Run`.
- Engine state is guarded by one lock; the properties the UI polls (`Songs`, `SelectedIndex`, `IsRunning`)
  are lock-free snapshots, so a long decode never freezes the window.
- Engine → UI notification is a single coalesced `StateChanged` marshalled with `BeginInvoke`.

## Audio

Output is always **22050 Hz, mono, 16-bit PCM** (the only format `voice_inputfromfile` accepts).

- Decoding: Windows Media Foundation through NAudio (MP3, WAV, FLAC, M4A/AAC, WebM/Opus) and NVorbis for
  OGG, which Media Foundation cannot read. Stereo is averaged to mono, then resampled (WDL resampler).
- Gain: the 0-200 % slider maps to dB exactly as in 1.x (`Volume.GetDbAdjust`: 100 % = -25 dB, above 100 %
  up to +8 dB). It is applied to the samples in float32, clamped and truncated, SIMD-vectorised
  (`Vector<T>`); a test proves the SIMD and scalar paths are bit-identical.
- Writing: temp file in the same folder + `File.Move(overwrite)`, so the game never sees a partial file.
- Measured: Media Foundation's MP3 decoder is the bottleneck (about 0.5 s for a 5-minute file); the
  managed downmix + resampler adds about 0.06 s. The native MF resampler and float-output modes were
  slower in measurements and were not adopted. See `docs/PERFORMANCE.md`.

## YouTube

GOAM never embeds yt-dlp. `YtDlpManager`:

1. Downloads `SHA2-256SUMS` and `yt-dlp.exe` from
   `github.com/yt-dlp/yt-dlp/releases/latest/download/`, streams the exe through SHA-256 and discards it on mismatch.
2. Stores the verified hash next to the exe and re-checks it before every use.
3. Updates when a download fails (once per session) and every 7 days in the background.

`YtDlpClient` builds argument lists (never strings), puts the URL after `--`, and asks for
`bestaudio[ext=m4a]/...` with `--fixup never`, so **ffmpeg is not needed**: AAC in M4A is decoded by Media
Foundation. Transient network errors are retried once.

If YouTube's JavaScript challenges ever require it, installing Deno lets yt-dlp use it (see README).

## Files and safety

- All name handling lives in `SongNames`: titles become `[letters digits _]`, renames drop path
  separators/reserved names, and `ResolveInside` is the only way to turn a name into a path (it rejects
  anything that leaves the songs folder).
- Text that ends up in `.cfg` files passes `CfgBuilder.SanitizeForCfg` (no `;`, `"`, `//`), and key names
  are validated by `GameKeys`.
- GOAM only deletes: its own generated files in the game folder, songs the user asked to delete, and the
  contents of its private `downloads` folder.
- Nothing derived from a name or URL is ever executed.

## Configuration and migration

`%APPDATA%\GOAM\config.json` (System.Text.Json, source-generated, atomic writes). If it does not exist and
the 1.x `config` file does, the latter is parsed with the same rules as `parse_config_line` and the result
is saved as JSON; the old file is left alone. Values are validated on load (volume range, key syntax, language).

## Windows start-up

`IStartupRegistration` has two implementations: the real `HKCU\...\Run` entry (value `GOAM`, command
`"<exe>" --startup`, same as 1.x) and a file-backed one used automatically when `GOAM_DATA_DIR` is set, so
automated runs can never create a real autostart entry.

## Diagnostics and logging

`FileLog` writes `%APPDATA%\GOAM\logs\goam.log` (512 KB x 3 rotated files). *Copy report* assembles version,
OS, runtime, detection results, yt-dlp version and the last 40 log lines through `Redactor`, which replaces
the user name, machine name and profile paths. Nothing leaves the machine.

## Environment variables

| Variable | Effect |
|---|---|
| `GOAM_DATA_DIR` | Replaces `%APPDATA%\GOAM`; also switches "start with Windows" to a file |
| `GOAM_TOOLS_DIR` | Replaces `%LOCALAPPDATA%\GOAM\tools` |
| `GOAM_STEAM_DIR` | Forces the Steam folder (ignores registry / default locations) |

## Packaging

Single-file, self-contained, compressed (`PublishSingleFile` + `EnableCompressionInSingleFile`), chosen from
the measurements in `docs/PERFORMANCE.md`: ReadyToRun gave no start-up gain (the framework itself ships
precompiled) and made the bundle larger; trimming is not supported for Windows Forms in .NET 8.
UPX is not used. Releases are built by `.github/workflows/release.yml` on `v*` tags.

## Command-line helpers (for development)

- `GOAM.exe --startup` is what the Run key uses.
- `GOAM.exe --screenshot out.png` renders the window's own controls to a PNG and exits.
- `GOAM.exe --benchmark out.json [--bench-mp3 file.mp3]` measures list refresh and conversion and exits.
