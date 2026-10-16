r"""Probe: what does the Python GOAM's yt-dlp format string actually yield, and can miniaudio decode it?

Usage: python yt_format_probe.py <youtube-url> <out_dir>
Mirrors goam.py run_youtube_download() options (single video, no postprocessors).
"""
import os
import sys
import json

import miniaudio
import yt_dlp

url, out_dir = sys.argv[1], sys.argv[2]
os.makedirs(out_dir, exist_ok=True)

fmt = "bestaudio[ext=mp3]/bestaudio[ext=ogg]/bestaudio[ext=flac]/bestaudio"
opts = {
    "format": fmt,
    "outtmpl": os.path.join(out_dir, "%(title)s.%(ext)s"),
    "quiet": True,
    "noplaylist": True,
    "postprocessors": [],
}
res = {"format_string": fmt, "yt_dlp_version": yt_dlp.version.__version__}
try:
    with yt_dlp.YoutubeDL(opts) as ydl:
        info = ydl.extract_info(url, download=True)
        path = ydl.prepare_filename(info)
    res["downloaded"] = os.path.basename(path)
    res["ext"] = info.get("ext")
    res["acodec"] = info.get("acodec")
    res["abr"] = info.get("abr")
    res["size_bytes"] = os.path.getsize(path) if os.path.exists(path) else None
    try:
        d = miniaudio.decode_file(path, output_format=miniaudio.SampleFormat.SIGNED16, nchannels=1, sample_rate=22050)
        res["miniaudio_decode"] = f"OK ({len(d.samples)} samples)"
    except Exception as e:  # miniaudio.DecodeError expected for opus/aac
        res["miniaudio_decode"] = f"FAILED: {type(e).__name__}: {e}"
except Exception as e:
    res["error"] = f"{type(e).__name__}: {e}"
print(json.dumps(res, indent=2))
