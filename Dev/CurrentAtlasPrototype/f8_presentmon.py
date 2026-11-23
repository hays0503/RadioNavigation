"""Extract an uninterrupted F8 window from PresentMon --v2_metrics CSV.

Intervals are present-to-present (CPUStartTime), so a frame that was
presented but not displayed still counts as a long interval. Display
tracking is reported only as a transparency metric.
"""
import argparse
import csv
import hashlib
import json
from math import isfinite
from pathlib import Path

from f8_frames import window


COLUMNS = {"ProcessID", "SwapChainAddress", "SyncInterval",
           "CPUStartTime", "DisplayedTime"}
WARMUP_MS = 30_000
WINDOW_MS = 120_000


def extract(rows, pid, swapchain, start_ms, stress=False):
    if not isfinite(start_ms) or start_ms < 0:
        raise ValueError("invalid predeclared start")
    presents = []
    displayed = 0
    for row in rows:
        if int(row["ProcessID"]) != pid or row["SwapChainAddress"].lower() != swapchain.lower():
            continue
        if int(row["SyncInterval"]) != 0:
            raise ValueError("target swapchain submitted with VSync")
        at = float(row["CPUStartTime"])
        if not isfinite(at):
            raise ValueError("invalid present time")
        if row["DisplayedTime"].strip().upper() not in ("", "NA", "N/A"):
            displayed += 1
        presents.append(at)
    if len(presents) < 2:
        raise ValueError("no presented frames on selected PID/swapchain")
    if any(b < a for a, b in zip(presents, presents[1:])):
        raise ValueError("non-monotonic present timestamps")
    if start_ms - presents[0] < WARMUP_MS:
        raise ValueError("capture does not document 30 s of pre-window gameplay")

    intervals = []
    begin = last = None
    for before, after in zip(presents, presents[1:]):
        if after <= start_ms:
            continue
        if begin is None:
            begin = before
        intervals.append(after - before)
        last = after
        if (last - begin) + 1e-8 >= WINDOW_MS:
            break
    if not intervals:
        raise ValueError("measurement window not found")
    metrics = window(intervals, stress=stress)
    return dict(presented_frame_ms=intervals, warmup_seconds=(begin - presents[0]) / 1000,
                start_ms=begin, end_ms=last, displayed_fraction=displayed / len(presents),
                metrics=metrics)


def self_check():
    rows = [dict(ProcessID="1", SwapChainAddress="abc", SyncInterval="0",
                 CPUStartTime=str(at), DisplayedTime="10") for at in range(0, 150_020, 10)]
    good = extract(rows, 1, "ABC", 30_000)
    assert good["metrics"]["passed"] and good["metrics"]["frames"] == 12_000
    assert good["displayed_fraction"] == 1.0
    hitch = [row for row in rows if not 80_000 < int(row["CPUStartTime"]) < 80_060]
    assert not extract(hitch, 1, "abc", 30_000, stress=True)["metrics"]["passed"]
    vsync = [dict(row, SyncInterval="1") for row in rows]
    for table, start in ((rows[:-4], 30_000), (rows, 29_000), (vsync, 30_000)):
        try:
            extract(table, 1, "abc", start)
        except ValueError:
            pass
        else:
            raise AssertionError("invalid frame receipt accepted")
    # A presented frame with no display record still contributes its interval.
    unshown = list(rows)
    unshown[7000]["DisplayedTime"] = "NA"
    assert extract(unshown, 1, "abc", 30_000)["metrics"]["passed"]
    assert extract(unshown, 1, "abc", 30_000)["displayed_fraction"] < 1
    print("PASS PresentMon CSV window checks; synthetic only")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("csv", type=Path, nargs="?")
    parser.add_argument("--pid", type=int)
    parser.add_argument("--swapchain")
    parser.add_argument("--start-ms", type=float, help="predeclared start on the capture's time axis")
    parser.add_argument("--configuration-id")
    parser.add_argument("--stress", action="store_true")
    args = parser.parse_args()
    if args.csv is None:
        return self_check()
    if not (args.pid and args.swapchain and args.configuration_id) or args.start_ms is None:
        parser.error("capture requires pid, swapchain, start-ms and configuration-id")
    with args.csv.open(encoding="utf-8-sig", newline="") as source:
        rows = csv.DictReader(source)
        if not COLUMNS.issubset(rows.fieldnames or []):
            parser.error("expected PresentMon --v2_metrics with display tracking")
        result = extract(rows, args.pid, args.swapchain, args.start_ms, args.stress)
    result.update(configuration_id=args.configuration_id, pid=args.pid, swapchain=args.swapchain,
                  csv_sha256=hashlib.sha256(args.csv.read_bytes()).hexdigest())
    print(json.dumps(result))
    return 0 if result["metrics"]["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
