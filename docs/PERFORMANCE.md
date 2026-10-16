# Performance: GOAM 1.x (Python) vs GOAM 2.0 (C#)

All numbers were measured on one machine, with the scripts in [`tools/bench`](../tools/bench), and are
medians unless stated otherwise. Treat them as relative, not absolute.

| | |
|---|---|
| CPU / RAM | AMD Ryzen 7 5800XT (8 cores / 16 threads), 32 GB |
| OS | Windows 10 IoT Enterprise LTSC (build 19044) |
| Python build | CPython 3.12.10, customtkinter 6.0, miniaudio 1.71, numpy 2.5, pygame 2.6, PyInstaller 6.22, UPX 5.2.1 |
| C# build | .NET 8.0.426 SDK, win-x64, `Release` |
| Source of 1.x | tag `v1.x-python-final` (`goam.py`) |
| Test data | synthetic songs (0.4 s mono WAV) in folders of 20 / 100 / 300; one synthetic 5-minute stereo 44.1 kHz MP3 (4.6 MB) |

## Summary

| Metric | 1.x script | 1.x `.exe` (PyInstaller + UPX) | **2.0 release build** |
|---|---|---|---|
| Time until the window is visible | 0.99 s | 2.96 s | **0.34 - 0.37 s** |
| Idle working set (5 s after start) | 82 MB (20 songs), 128 MB (300) | 145 MB | **123 MB** |
| File size | needs Python | 34.1 MB (42.9 MB without UPX) | **60.3 MB** (self-contained) |
| List, build from empty - 20 songs | 0.93 s | - | **8 ms** |
| List, build from empty - 100 songs | 4.94 s | - | **8 ms** |
| List, build from empty - 300 songs | 14.04 s | - | **8 ms** |
| List, refresh after selecting a song - 20 / 100 / 300 | 0.32 s / 1.60 s / 5.01 s | - | **7 ms / 7 ms / 7 ms** |
| Convert a 5-minute MP3 to WAV | 0.34 s | not measured (same code) | **0.56 - 0.60 s** |

What each row measures:

- **Window visible**: `EnumWindows` polling from process start until a visible top-level window titled
  "GOAM" exists (5 runs, same tool for both). The Python window appears before its song list is filled; in
  the C# app the list is filled right after, off the UI thread.
- **List build / refresh**: in-process, including the final paint. For 1.x it is `update_song_list()` +
  `update()` on its customtkinter list (a first call with N songs, then a selection change). For 2.0 it is
  the virtualised `DataGridView` filled from empty / invalidated after a selection change, then `Refresh()`.
  The engine call that writes `voice_input.wav` is excluded from both.
- **Conversion**: decode + convert + write of the same MP3, three runs, median (2.0 first call in a
  process: 0.60 - 0.68 s).
- **Working set**: Windows working set of the whole process tree. 1.x also commits ~540-600 MB of private
  bytes (numpy/OpenBLAS arenas); 2.0 commits about 55 MB. Working set is the number that matters for RAM use.

### What the data says

- **UI and start-up are an order of magnitude faster**, as the diagnosis predicted. The list cost is no
  longer proportional to the number of songs because only the visible rows exist and are painted.
- **Conversion is slower**: 0.56-0.60 s vs 0.34 s. The decoder is Windows Media Foundation
  (about 0.49 s for this file on its own; the downmix + resampler add ~0.06 s), while 1.x used miniaudio's
  C decoder. I tried Media Foundation's native resampler and float output; both were slower. A
  native-library decoder would close the gap but adds a binary dependency; for a once-per-song operation
  of half a second that was judged not worth it.
- **Memory**: the compressed single file is decompressed into memory at start (see below), which is why
  the release build idles at 123 MB instead of ~59 MB for the uncompressed variant.

## Packaging variants (C#)

Same code, `dotnet publish -r win-x64`, 5 runs each for start-up (20-song folder; 300 songs gave the same
within noise).

| Variant | Flags | File | Window visible | Idle working set | Needs .NET installed |
|---|---|---|---|---|---|
| `fdd` | framework-dependent, single file | **1.55 MB** | 0.23 s | 57 MB | **yes** (.NET 8 Desktop Runtime) |
| `sc` | self-contained, single file | 131.9 MB | 0.24 s | 60 MB | no |
| **`sc-comp`** (**chosen**) | self-contained, single file, `EnableCompressionInSingleFile` | **60.3 MB** | 0.34 s | 123 MB | no |
| `sc-r2r` | `sc` + ReadyToRun | 148.7 MB | 0.23 s | 66 MB | no |
| `sc-r2r-comp` | `sc-comp` + ReadyToRun | 64.8 MB | 0.52 s | 163 MB | no |

Reasoning:

- Every variant meets the "window in < 1 s" target, so the choice is about friction and size.
- `fdd` is tiny and fast but most players do not have the .NET 8 *Desktop* runtime; the first launch would
  send them to an installer page. Not acceptable for a drag-and-run tool.
- ReadyToRun did not help: the .NET framework already ships precompiled, and GOAM's own code is small. It
  only made the file bigger (and, with compression, slower to start).
- Trimming was not tried: Windows Forms trimming is unsupported in .NET 8.
- Compression halves the download (60 MB vs 132 MB) at the cost of ~0.1 s start-up and ~60 MB of RAM.
  If idle RAM matters more than download size to you, publish with
  `-p:EnableCompressionInSingleFile=false` (variant `sc`: 0.24 s, 60 MB RAM, 132 MB file).
- UPX is not used.

The release workflow publishes `sc-comp`.

## Caveats

- One machine, one run session. The first launch after a fresh publish can be slower (cold file cache;
  e.g. one 1.2 s outlier in the `sc-r2r-comp` series).
- The Python numbers include customtkinter 6.0 / Pillow 12; older versions may differ.
- Synthetic audio was used so that the files can live in the repository's history without licensing issues.
  A real 5-minute MP3 at a similar bitrate behaves the same.
- Not measured: antivirus on-access scanning overhead, battery use, behaviour while CS:GO is running.

## Reproducing

See [`tools/bench/README.md`](../tools/bench/README.md). In short:

```powershell
.\tools\bench\run_baseline_py.ps1 -GoamPy <goam.py from the v1.x-python-final tag>
.\tools\bench\run_baseline_cs.ps1
```
