r"""Measure time-to-visible-window and idle memory for a Windows GUI process.

Usage:
  python win_visible.py --runs 5 --title-contains GOAM --settle 5 -- <exe> [args...]

Environment of this process is inherited by the child (so env.ps1 isolation applies).
Prints one JSON object with per-run numbers and summary stats.
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import json
import statistics
import subprocess
import sys
import time

import psutil

user32 = ctypes.WinDLL("user32", use_last_error=True)
EnumWindows = user32.EnumWindows
EnumWindowsProc = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
GetWindowThreadProcessId = user32.GetWindowThreadProcessId
IsWindowVisible = user32.IsWindowVisible
GetWindowTextW = user32.GetWindowTextW
GetWindowTextLengthW = user32.GetWindowTextLengthW
GetWindowRect = user32.GetWindowRect


def visible_windows_for(pids, needle):
    found = []

    @EnumWindowsProc
    def cb(hwnd, _lp):
        pid = wt.DWORD()
        GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        if pid.value in pids and IsWindowVisible(hwnd):
            n = GetWindowTextLengthW(hwnd)
            if n > 0:
                buf = ctypes.create_unicode_buffer(n + 1)
                GetWindowTextW(hwnd, buf, n + 1)
                title = buf.value
                if needle.lower() in title.lower():
                    r = wt.RECT()
                    GetWindowRect(hwnd, ctypes.byref(r))
                    if (r.right - r.left) > 100 and (r.bottom - r.top) > 100:
                        found.append((hwnd, title))
        return True

    EnumWindows(cb, 0)
    return found


def tree_pids(proc):
    pids = {proc.pid}
    try:
        for c in proc.children(recursive=True):
            pids.add(c.pid)
    except psutil.Error:
        pass
    return pids


def one_run(cmd, needle, settle, timeout=30.0):
    t0 = time.perf_counter()
    p = subprocess.Popen(cmd)
    ps = psutil.Process(p.pid)
    visible_ms = None
    deadline = t0 + timeout
    while time.perf_counter() < deadline:
        if p.poll() is not None:
            break
        pids = tree_pids(ps)
        if visible_windows_for(pids, needle):
            visible_ms = (time.perf_counter() - t0) * 1000.0
            break
        time.sleep(0.003)
    rss = priv = None
    nproc = 0
    if visible_ms is not None:
        time.sleep(settle)
        rss = priv = 0
        for pid in tree_pids(ps):
            try:
                mi = psutil.Process(pid).memory_info()
                rss += mi.rss
                priv += getattr(mi, "private", mi.rss)
                nproc += 1
            except psutil.Error:
                pass
    # terminate the tree
    for pid in tree_pids(ps):
        try:
            psutil.Process(pid).kill()
        except psutil.Error:
            pass
    try:
        p.wait(timeout=5)
    except Exception:
        pass
    return {
        "visible_ms": None if visible_ms is None else round(visible_ms, 1),
        "working_set_mb": None if rss is None else round(rss / 1048576, 1),
        "private_mb": None if priv is None else round(priv / 1048576, 1),
        "processes": nproc,
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--runs", type=int, default=5)
    ap.add_argument("--title-contains", default="GOAM")
    ap.add_argument("--settle", type=float, default=5.0)
    ap.add_argument("--label", default="")
    ap.add_argument("cmd", nargs=argparse.REMAINDER)
    a = ap.parse_args()
    cmd = a.cmd[1:] if a.cmd and a.cmd[0] == "--" else a.cmd
    runs = []
    for i in range(a.runs):
        r = one_run(cmd, a.title_contains, a.settle)
        runs.append(r)
        print(f"run {i + 1}: {r}", file=sys.stderr, flush=True)
        time.sleep(1.0)
    vis = [r["visible_ms"] for r in runs if r["visible_ms"] is not None]
    ws = [r["working_set_mb"] for r in runs if r["working_set_mb"] is not None]
    pv = [r["private_mb"] for r in runs if r["private_mb"] is not None]
    out = {
        "label": a.label,
        "cmd": cmd,
        "runs": runs,
        "visible_ms_median": statistics.median(vis) if vis else None,
        "visible_ms_min": min(vis) if vis else None,
        "visible_ms_max": max(vis) if vis else None,
        "working_set_mb_median": statistics.median(ws) if ws else None,
        "private_mb_median": statistics.median(pv) if pv else None,
    }
    print(json.dumps(out, indent=2))


if __name__ == "__main__":
    main()
