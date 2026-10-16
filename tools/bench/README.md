# Benchmark scripts

These scripts produced the numbers in [docs/PERFORMANCE.md](../../docs/PERFORMANCE.md). They are
development tools, not part of the product.

They assume a scratch workspace whose location is in the `GOAM_PORT_ROOT` environment variable, laid out as:

```
%GOAM_PORT_ROOT%\
  bench\        generated assets and results (created by gen_assets.py)
  venv\         Python venv for the 1.x baseline
  repo\         this repository (the C# build is published from here)
  out\publish\  publish output per variant
```

## Python baseline (GOAM 1.x)

```powershell
# venv with the 1.x dependencies + helpers used by the scripts
python -m venv $env:GOAM_PORT_ROOT\venv
& $env:GOAM_PORT_ROOT\venv\Scripts\pip install customtkinter miniaudio numpy yt-dlp Pillow pystray pygame pyinstaller lameenc soundfile psutil

# synthetic assets: a 5-minute MP3 and folders with 20/100/300 tiny songs
& $env:GOAM_PORT_ROOT\venv\Scripts\python tools\bench\gen_assets.py

# goam.py from the frozen tag
git show v1.x-python-final:goam.py > $env:GOAM_PORT_ROOT\goam.py

.\tools\bench\run_baseline_py.ps1 -GoamPy $env:GOAM_PORT_ROOT\goam.py
```

## C# build

```powershell
.\tools\bench\run_baseline_cs.ps1                 # all four publish variants
.\tools\bench\run_baseline_cs.ps1 -Variants sc-r2r-comp -Runs 10
```

## Tools

| Script | What it does |
|---|---|
| `win_visible.py` | Launches a program, polls its windows with `EnumWindows` and reports the time until the first visible window with a given title, then the working set / private bytes after a settle time. Used for both Python and C#. |
| `bench_list_py.py` | Runs `goam.py` with its main loop replaced to time `update_song_list()` and the MP3 conversion. |
| `gen_assets.py` | Generates synthetic, copyright-free test audio. |
| `yt_format_probe.py` | Shows which format the 1.x yt-dlp options really download and whether miniaudio can decode it. |

`GOAM.exe --benchmark <out.json> [--bench-mp3 <file>]` is the C# counterpart of `bench_list_py.py`.
Always run with `GOAM_DATA_DIR`, `GOAM_TOOLS_DIR` and `GOAM_STEAM_DIR` pointing at scratch folders.
