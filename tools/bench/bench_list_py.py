r"""In-process benchmark of the Python GOAM song list (update_song_list).

Runs goam.py with its final mainloop replaced by a benchmark routine:
  cold_build_ms : first update_song_list() with N songs (creates ~6 widgets per song) + layout/paint
  warm_refresh_ms: update_song_list() after a selection change (repack + reconfigure all rows) + paint
Also measures decode+convert of five_min.mp3 with the app's own functions.

Usage: python bench_list_py.py <path-to-goam.py> <songs_dir_N> <mp3_for_conversion>
Must be run with APPDATA pointing at a config that sets songs_dir/csgo_dir (no first-run dialogs).
"""
import json
import os
import statistics
import sys
import time
from pathlib import Path

goam_py = Path(sys.argv[1])
mp3 = sys.argv[3]
src = goam_py.read_text(encoding="utf-8")

bench = r'''
import time as _t, json as _j, statistics as _s, os as _os
def _bench():
    out = {}
    app.update()
    wav_files[:] = load_wav_files()
    out["songs"] = len(wav_files)
    t0 = _t.perf_counter()
    update_song_list()
    app.update()
    out["cold_build_ms"] = round((_t.perf_counter() - t0) * 1000, 1)
    times = []
    n = max(1, len(wav_files))
    global selected_index
    for k in range(1, 9):
        selected_index = (k * 7) % n + 1
        t0 = _t.perf_counter()
        update_song_list()
        app.update()
        times.append((_t.perf_counter() - t0) * 1000)
    out["warm_refresh_ms_median"] = round(_s.median(times), 1)
    out["warm_refresh_ms_max"] = round(max(times), 1)
    # conversion benchmark: decode + convert + write wav with app functions
    outwav = _os.path.join(_os.environ["TEMP"], "bench_conv.wav")
    ctimes = []
    for _ in range(3):
        t0 = _t.perf_counter()
        ok = convert_to_csgo_wav(MP3_PATH, outwav)
        ctimes.append((_t.perf_counter() - t0) * 1000)
    out["convert_5min_mp3_ok"] = ok
    out["convert_5min_mp3_ms_median"] = round(_s.median(ctimes), 1)
    print("BENCH_RESULT " + _j.dumps(out), flush=True)
    app.after(50, app.destroy)
app.after(300, _bench)
app.mainloop()
'''
marker = "app.after(50, on_app_ready)\napp.mainloop()"
assert marker in src, "marker not found in goam.py"
src = src.replace(marker, f"MP3_PATH = {mp3!r}\n" + bench)
g = {"__name__": "__main__", "__file__": str(goam_py)}
sys.argv = [str(goam_py)]
exec(compile(src, str(goam_py), "exec"), g)
