"""Evaluate F8 frame-time receipts; inputs must be presented-frame intervals."""
import argparse
import json
from math import ceil, isfinite
from pathlib import Path


def window(frame_ms, stress=False):
    if not frame_ms or any(not isfinite(value) or value <= 0 for value in frame_ms):
        raise ValueError("invalid frame interval")
    ordered = sorted(frame_ms)
    seconds = sum(frame_ms) / 1000
    if not 120 <= seconds <= 120 + ordered[-1] / 1000 + 1e-9:
        raise ValueError("intervals do not cover the prespecified 120 s window")
    fps = len(frame_ms) / seconds
    p99 = ordered[ceil(len(ordered) * .99) - 1]
    p999 = ordered[ceil(len(ordered) * .999) - 1]
    maximum = ordered[-1]
    return dict(seconds=seconds, frames=len(frame_ms), fps=fps, p99_ms=p99, p999_ms=p999,
                max_ms=maximum, passed=fps >= 60 and p99 <= 16.667 and p999 <= 25
                and (not stress or maximum <= 50))


def evaluate(receipt):
    results = {}
    for name in ("open-sea", "dense-convoy", "port", "dock-skip", "transition-batch"):
        pairs = receipt[name]
        if len(pairs) != 3:
            raise ValueError(f"{name}: three A/B pairs required")
        stress = name in ("dock-skip", "transition-batch")
        measured = []
        for pair in pairs:
            conditions = {"A": pair["A"], "B": pair["B"]}
            if len({side["configuration_id"] for side in conditions.values()}) != 1:
                raise ValueError(f"{name}: mismatched A/B settings")
            record = {}
            for label, side in conditions.items():
                metrics = window(side["presented_frame_ms"], stress)
                if side["warmup_seconds"] < 30:
                    raise ValueError(f"{name}: warm-up shorter than 30 s")
                if stress and (not side["completed_in_window"] or side["ending_debt_ticks"] != 0):
                    metrics["passed"] = False
                record[label] = metrics
            measured.append(record)
        results[name] = measured
    return dict(phase="frame receipt only, not an attestation of inputs, peak memory or events",
                passed=all(side["passed"] for pairs in results.values() for pair in pairs for side in pair.values()),
                scenarios=results)


def self_check():
    good = [15] * 8000
    assert window(good)["passed"]
    assert not window([20] * 6000)["passed"]
    assert not window([10] * 11994 + [60], stress=True)["passed"]
    assert window([10] * 11994 + [60])["passed"]
    case = dict(configuration_id="test", warmup_seconds=30, presented_frame_ms=good,
                completed_in_window=True, ending_debt_ticks=0)
    receipt = {name: [{"A": dict(case), "B": dict(case)} for _ in range(3)]
               for name in ("open-sea", "dense-convoy", "port", "dock-skip", "transition-batch")}
    assert evaluate(receipt)["passed"]
    receipt["dock-skip"][2]["B"]["ending_debt_ticks"] = 1
    assert not evaluate(receipt)["passed"]
    receipt["dock-skip"][2]["B"]["configuration_id"] = "different"
    try:
        evaluate(receipt)
    except ValueError:
        pass
    else:
        raise AssertionError("mismatched configurations accepted")
    print("PASS 7 synthetic F8 receipt checks; no Unity results")


def synthetic_receipt():
    """A passing receipt for harness dry-runs. Synthetic, not a measurement."""
    base = [12.0] * 10000          # exactly 120 s, comfortably inside 60 FPS
    stress = [12.0] * 9998 + [40.0, 12.0]
    receipt = {}
    for name in ("open-sea", "dense-convoy", "port", "dock-skip", "transition-batch"):
        frames = stress if name in ("dock-skip", "transition-batch") else base
        side = dict(configuration_id="synthetic", warmup_seconds=30,
                    presented_frame_ms=frames, completed_in_window=True,
                    ending_debt_ticks=0)
        receipt[name] = [{"A": dict(side), "B": dict(side)} for _ in range(3)]
    return receipt


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path, nargs="?")
    parser.add_argument("--write-synthetic", type=Path,
                        help="write a passing synthetic receipt for a harness dry-run")
    args = parser.parse_args()
    if args.write_synthetic is not None:
        payload = synthetic_receipt()
        result = evaluate(payload)
        if not result["passed"]:
            raise SystemExit("synthetic receipt failed its own validator")
        args.write_synthetic.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
        print(f"wrote synthetic passing receipt to {args.write_synthetic}")
    elif args.input is None:
        self_check()
    else:
        result = evaluate(json.loads(args.input.read_text(encoding="utf-8-sig")))
        print(json.dumps(result, indent=2))
        raise SystemExit(0 if result["passed"] else 1)
