# UI smoke tests

End-to-end checks that drive the real `GOAM.exe` window through Windows UI Automation. They need an
interactive Windows desktop, so they are run by hand (or on a desktop CI agent) rather than in the
unit-test job.

Each script creates a scratch sandbox and points `GOAM_DATA_DIR`, `GOAM_TOOLS_DIR` and `GOAM_STEAM_DIR`
into it, uses a fake game folder, and (because `GOAM_DATA_DIR` is set) the app writes "start with Windows"
to a text file instead of the registry. Nothing outside the scratch folder is modified.

```powershell
dotnet publish src/GOAM.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o out
.\tests\ui\smoke.ps1     -Exe .\out\GOAM.exe
.\tests\ui\first-run.ps1 -Exe .\out\GOAM.exe
.\tests\ui\tray.ps1      -Exe .\out\GOAM.exe
.\tests\ui\preview.ps1   -Exe .\out\GOAM.exe     # silent: volume 0
.\tests\ui\youtube.ps1   -Exe .\out\GOAM.exe     # needs internet
```

Without `-Exe`, the Release build output of `src/GOAM.App` is used.

| Script | Covers |
|---|---|
| `smoke.ps1` | song list, select -> `voice_input.wav` + cfg, Stop/Start, language toggle, Options dialog, cleanup on close |
| `first-run.ps1` | first-start questions, Steam-library detection of the game folder, config written |
| `tray.ps1` | start hidden to tray, single instance, restore from a second launch |
| `preview.ps1` | preview toggle and its 5-second limit |
| `youtube.ps1` | yt-dlp install + SHA-256 verification, download, M4A -> WAV conversion, no ffmpeg |

The scratch location is `%TEMP%\goam-ui-tests` (override with `GOAM_UI_TEST_DIR`). The tests rely on
English strings for button names (`Stop GOAM`, `Select`...) and on the system language for MessageBox
button ids (not text), so run them on any Windows UI language.
