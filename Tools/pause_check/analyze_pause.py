import array
import math
import sys
import wave
from pathlib import Path

WINDOW_MS = 10
MIN_RUN_MS = 30
PAUSE_MIN_S = 1.2
TONE_MIN_MS = 250

EXPECT = [
    ("post-nav dash-sector", 3.85),
    ("post-nav dot-sector", 3.45),
    ("pre-nav gap", 3.94),
]
TOL = 0.25


def load_wav(path):
    with wave.open(str(path), "rb") as w:
        channels = w.getnchannels()
        rate = w.getframerate()
        bits = w.getsampwidth() * 8
        comp = w.getcomptype()
        if comp != "NONE" or bits != 16:
            raise SystemExit(f"unsupported wav: comp={comp} bits={bits} (need PCM16)")
        raw = w.readframes(w.getnframes())
    samples = array.array("h")
    samples.frombytes(raw)
    if channels > 1:
        mono = array.array(
            "h",
            (
                int(sum(samples[i : i + channels]) / channels)
                for i in range(0, len(samples) - channels + 1, channels)
            ),
        )
    else:
        mono = samples
    return mono, rate


def envelope(mono, rate):
    win = max(1, rate * WINDOW_MS // 1000)
    n = len(mono) // win
    env = []
    for i in range(n):
        acc = 0.0
        base = i * win
        for j in range(base, base + win):
            v = mono[j]
            acc += v * v
        env.append(math.sqrt(acc / win) / 32768.0)
    return env, win


def segments(env, threshold):
    segs = []
    cur_on = env[0] >= threshold
    start = 0
    for i in range(1, len(env) + 1):
        on = i < len(env) and env[i] >= threshold
        if on != cur_on:
            segs.append([start, i - start, cur_on])
            start = i
            cur_on = on
    merged = []
    min_run = MIN_RUN_MS // WINDOW_MS
    for s in segs:
        if merged:
            last = merged[-1]
            if last[2] == s[2]:
                last[1] = s[0] + s[1] - last[0]
                continue
            if s[1] < min_run:
                last[1] = s[0] + s[1] - last[0]
                continue
        merged.append(list(s))
    out = [m for m in merged if m[1] >= min_run]
    return out


def main():
    path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).with_name("rec.wav")
    if not path.exists():
        raise SystemExit(f"no file: {path}")
    mono, rate = load_wav(path)
    env, _ = envelope(mono, rate)
    ordered = sorted(env)
    floor = ordered[len(ordered) // 5]
    peak = ordered[-1]
    thr = max(floor * 4.0, peak * 0.03)

    dur_s = len(mono) / rate
    print(f"file={path.name} rate={rate} dur={dur_s:.1f}s windows={len(env)}")
    pct = [ordered[int(len(ordered) * p)] for p in (0.05, 0.10, 0.25, 0.50, 0.75, 0.90)]
    print("envPercentiles p05..p90: " + " ".join(f"{v:.4f}" for v in pct))
    print(f"noiseFloor={floor:.5f} peak={peak:.5f} threshold={thr:.5f} ratio={thr/floor if floor else 0:.1f}x")

    for mult in (2.0, 3.0, 4.0, 6.0):
        t2 = max(floor * mult, peak * 0.03)
        s2 = segments(env, t2)
        mx = max((s[1] for s in s2 if not s[2]), default=0)
        print(f"sensitivity thr=floor*{mult:.0f} ({t2:.4f}): segments={len(s2)} maxOFF={mx * WINDOW_MS / 1000.0:.2f}s")

    segs = segments(env, thr)
    print(f"\nsegments(thr main)={len(segs)}")
    offs_all = sorted((s for s in segs if not s[2]), key=lambda s: -s[1])
    buckets = {"<300": 0, "300-600": 0, "600-900": 0, "900-1200": 0, ">=1200": 0}
    for s in offs_all:
        ms = s[1] * WINDOW_MS
        k = "<300" if ms < 300 else "300-600" if ms < 600 else "600-900" if ms < 900 else "900-1200" if ms < 1200 else ">=1200"
        buckets[k] += 1
    print("OFF histogram: " + "  ".join(f"{k}:{v}" for k, v in buckets.items()))
    print("top 15 OFF any length:")
    for s in offs_all[:15]:
        print(f"OFF start={s[0] * WINDOW_MS / 1000.0:7.2f}s dur={s[1] * WINDOW_MS / 1000.0:5.2f}s")

    pauses = sorted((s for s in segs if not s[2] and s[1] >= PAUSE_MIN_S * 1000 / WINDOW_MS), key=lambda s: -s[1])
    print(f"\n--- PAUSES >= {PAUSE_MIN_S}s ({len(pauses)}), sorted by duration ---")
    for s in pauses:
        t = s[0] * WINDOW_MS / 1000.0
        d = s[1] * WINDOW_MS / 1000.0
        best = min(EXPECT, key=lambda e: abs(e[1] - d))
        mark = "OK " if abs(d - best[1]) <= TOL else "?? "
        print(f"{mark}{best[0]:22s} start={t:7.2f}s dur={d:5.2f}s (expect {best[1]:.2f}±{TOL})")

    tones = sorted((s for s in segs if s[2] and s[1] >= TONE_MIN_MS / WINDOW_MS), key=lambda s: -s[1])
    print(f"\n--- TONES >= {TONE_MIN_MS}ms, top 20 ---")
    for s in tones[:20]:
        t = s[0] * WINDOW_MS / 1000.0
        d = s[1] * WINDOW_MS / 1000.0
        print(f"ON  start={t:7.2f}s dur={d:5.2f}s")

    print("\n--- VERDICT ---")
    matched = sum(1 for s in pauses if any(abs(s[1] * WINDOW_MS / 1000.0 - e[1]) <= TOL for e in EXPECT))
    if not pauses:
        print("no long pauses found: check recording/threshold")
    elif matched == len(pauses):
        print(f"all {len(pauses)} pauses match model: scheduler timing correct, ear was wrong")
    else:
        print(f"{matched}/{len(pauses)} pauses match model; mismatches above marked '??'")


if __name__ == "__main__":
    main()
