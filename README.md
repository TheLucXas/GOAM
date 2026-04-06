# GOAM — Global Offensive Audio Mixer

> ⚠️ This project was vibecoded with the help of AI assistants. Expect rough edges.

**GOAM** is a Windows desktop app that lets you play custom music through your microphone in CS:GO (Legacy, AppID 4465480), as a modern replacement for [SLAM](https://github.com/raphaelmacsabpf/slam).

Download songs from YouTube or import local files (MP3, WAV, FLAC, OGG), and play them in-game with a single key press.

---

## Features

- 🎵 Download audio directly from YouTube via yt-dlp
- 📁 Import local MP3, WAV, FLAC or OGG files
- 🔊 Preview songs before playing them in-game
- 🎚️ Volume control
- ⌨️ Configurable play key and relay key
- 🔁 In-game song switching without leaving CS:GO
- 🗂️ Rename and delete songs from the UI
- 🌐 Spanish / English language support
- 🖥️ Minimize to system tray
- 🚀 Optional auto-start with Windows

---

## Download

Grab the latest `GOAM.exe` from the [Releases](../../releases) page — no installation needed, just run it.

---

## How it works

GOAM converts your songs to a CS:GO-compatible WAV format and writes them to `voice_input.wav` in your CS:GO directory. It then uses CS:GO's `voice_inputfromfile` console variable to play that file through your microphone when you press your configured key.

---

## Building from source

Requirements: Python 3.11+, and all dependencies installed:

```bash
pip install customtkinter miniaudio numpy yt-dlp Pillow pystray pygame
```

To build a standalone `.exe`:

```bash
python -m PyInstaller --onefile --noconsole --icon=goam.ico --name=GOAM --hidden-import=cffi --hidden-import=_cffi_backend --collect-all=miniaudio --add-data "goam.ico;." --upx-dir=. goam.py
```

> `--add-data "goam.ico;."` embeds the icon inside the `.exe` so the system tray icon loads correctly at runtime. Without this flag the tray shows a plain green square instead of the real icon.

> `--upx-dir=.` requires [UPX](https://upx.github.io) — place `upx.exe` in the same folder before building. If you don't have it, remove that flag.

The output will be at `dist/GOAM.exe`.

---

## Credits

- [SLAM](https://github.com/raphaelmacsabpf/slam) — the original inspiration for this project
- [ChatGPT](https://chat.openai.com) — vibe coding assistance
- [Grok](https://grok.x.ai) — vibe coding assistance
- [Claude](https://claude.ai) — vibe coding assistance

---