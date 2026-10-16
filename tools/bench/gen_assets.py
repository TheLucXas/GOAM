"""Generate synthetic benchmark/test assets (all original, no third-party audio).

Outputs (under GOAM-port\bench\):
  assets\five_min.mp3            5 min stereo 44.1 kHz 128 kbps MP3 (conversion benchmark, NOT for git)
  songs20|songs100|songs300\*.wav  tiny synthetic songs for list refresh benchmark
  fake-csgo\csgo.exe             empty placeholder so the app accepts the folder
  small\*                        tiny tone files destined for tests\assets in the repo
"""
import math
import os
import sys
import wave
from pathlib import Path

import numpy as np
import lameenc
import soundfile as sf

ROOT = Path(os.environ.get("GOAM_PORT_ROOT", ".")) / "bench"
(ROOT / "assets").mkdir(parents=True, exist_ok=True)
(ROOT / "small").mkdir(parents=True, exist_ok=True)
(ROOT / "fake-csgo").mkdir(parents=True, exist_ok=True)
(ROOT / "fake-csgo" / "csgo.exe").write_bytes(b"")


def tone(seconds, rate, freq=440.0, channels=1, amp=0.4):
    t = np.arange(int(seconds * rate)) / rate
    base = amp * np.sin(2 * np.pi * freq * t)
    if channels == 1:
        return (base * 32767).astype(np.int16)
    right = amp * np.sin(2 * np.pi * freq * 1.5 * t)
    return (np.stack([base, right], axis=1) * 32767).astype(np.int16)


def music_like(seconds, rate):
    t = np.arange(int(seconds * rate)) / rate
    env = 0.5 + 0.5 * np.sin(2 * np.pi * t / 7.0)
    l = sum(np.sin(2 * np.pi * f * t) / (i + 1) for i, f in enumerate([220, 330, 440, 660, 880]))
    r = sum(np.sin(2 * np.pi * f * t + 0.7) / (i + 1) for i, f in enumerate([247, 370, 494, 740, 988]))
    rng = np.random.default_rng(12345)
    n = rng.normal(0, 0.02, size=(t.size, 2))
    st = np.stack([l * env, r * env], axis=1) * 0.25 + n
    return (np.clip(st, -1, 1) * 32767).astype(np.int16)


def write_mp3(path, pcm, rate, channels):
    enc = lameenc.Encoder()
    enc.set_bit_rate(128)
    enc.set_in_sample_rate(rate)
    enc.set_channels(channels)
    enc.set_quality(2)
    data = enc.encode(pcm.tobytes()) + enc.flush()
    Path(path).write_bytes(data)


def write_wav(path, pcm, rate, channels):
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(channels)
        wf.setsampwidth(2)
        wf.setframerate(rate)
        wf.writeframes(pcm.tobytes())


# 5-minute MP3 for the conversion benchmark
write_mp3(ROOT / "assets" / "five_min.mp3", music_like(300, 44100), 44100, 2)

# Small tone files for tests/assets
small = ROOT / "small"
write_mp3(small / "tone-440hz-stereo-44100.mp3", tone(1.0, 44100, channels=2), 44100, 2)
sf.write(str(small / "tone-440hz-stereo-44100.ogg"), tone(1.0, 44100, channels=2), 44100, format="OGG", subtype="VORBIS")
sf.write(str(small / "tone-440hz-mono-48000.flac"), tone(1.0, 48000), 48000, format="FLAC", subtype="PCM_16")
write_wav(small / "tone-440hz-mono-22050.wav", tone(1.0, 22050), 22050, 1)

# Synthetic songs for list benchmarks (0.4 s each, already CS:GO format)
for n in (20, 100, 300):
    d = ROOT / f"songs{n}"
    d.mkdir(parents=True, exist_ok=True)
    for f in d.glob("*.wav"):
        f.unlink()
    for i in range(1, n + 1):
        name = f"Synthetic Song {i:03d} - Test Artist {(i * 7) % 97:02d}.wav"
        write_wav(d / name, tone(0.4, 22050, freq=200 + (i % 40) * 20), 22050, 1)

for p in sorted(ROOT.rglob("*")):
    if p.is_file() and p.parent.name not in ("songs20", "songs100", "songs300"):
        print(f"{p.relative_to(ROOT)}  {p.stat().st_size} bytes")
print("songs20/100/300 created")
