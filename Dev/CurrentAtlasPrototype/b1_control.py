"""B1.2 runner: stage a save outside the live directory and compare read-only captures."""
import argparse
from datetime import date
import hashlib
import json
import math
from pathlib import Path
import re
import shutil
import sys
import tempfile
import time
from urllib.request import ProxyHandler, build_opener


TICKS = 10_000_000
SECONDS = 600
MAX_GAP = 7 * TICKS // 4
COPY_NAME = "RadioNav_B1_Control.save"
STATE = "http://127.0.0.1:28777/state?fields=loading,position,time,ship"
LOG = Path.home() / "AppData/LocalLow/Deep Water Studio/UBOAT/output_log.txt"
CLOCK = re.compile(r"(\d{4}-\d\d-\d\d)T(\d\d):(\d\d):(\d\d)\.(\d{7})")


def file_hash(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def inventory(root):
    if not root.is_dir():
        raise ValueError("Saves directory is unavailable")
    return {path.relative_to(root).as_posix(): file_hash(path)
            for path in sorted(root.rglob("*")) if path.is_file()}


def write_new(path, value):
    with path.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2, allow_nan=False)
        stream.write("\n")


def verify_inventory(before, after):
    changed = [name for name in sorted(before.keys() | after.keys())
               if before.get(name) != after.get(name)]
    if changed:
        raise ValueError("protected save inventory changed: " + ", ".join(changed))


def prepare(saves, name, work):
    saves, work = saves.resolve(strict=True), work.resolve()
    source = (saves / name).resolve(strict=True)
    if source.parent != saves or source.suffix.lower() != ".save":
        raise ValueError("select one .save directly under the protected Saves directory")
    if work == saves or saves in work.parents:
        raise ValueError("stage the copy outside the protected Saves directory")
    if (saves / COPY_NAME).exists():
        raise ValueError("reserved campaign copy name already exists in Saves")
    before = inventory(saves)
    work.mkdir(parents=True, exist_ok=False)
    staged = work / COPY_NAME
    shutil.copyfile(source, staged)
    digest = before[source.name]
    with tempfile.TemporaryDirectory(dir=work) as directory:
        # Restore from the preserved input to a distinct disposable target.
        restored = Path(directory) / COPY_NAME
        shutil.copyfile(staged, restored)
        if file_hash(restored) != digest:
            raise ValueError("restore rehearsal did not reproduce the source bytes")
    verify_inventory(before, inventory(saves))
    return save_manifest(work, saves, source, digest, before)


def save_manifest(work, saves, source, digest, before):
    manifest = dict(saves=str(saves), source=source.name, save_sha256=digest,
                    copy=str(work / COPY_NAME), restore_rehearsal=True,
                    protected_file_count=len(before), protected=before)
    write_new(work / "manifest.json", manifest)
    return manifest


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def verified_stage(work):
    manifest = read_json(work / "manifest.json")
    if file_hash(work / COPY_NAME) != manifest["save_sha256"]:
        raise ValueError("staged campaign copy hash mismatch")
    return manifest


def verify(work):
    manifest = verified_stage(work)
    verify_inventory(manifest["protected"], inventory(Path(manifest["saves"])))
    return dict(unchanged=True, protected_file_count=manifest["protected_file_count"])


def clock_tick(value):
    match = CLOCK.fullmatch(value)
    if not match:
        raise ValueError("expected game DateTime with exactly seven fractional digits and no timezone")
    day, hour, minute, second, fraction = match.groups()
    h, m, s = int(hour), int(minute), int(second)
    if h >= 24 or m >= 60 or s >= 60:
        raise ValueError("invalid game clock")
    return ((date.fromisoformat(day).toordinal() - 1) * 86400 + h * 3600 + m * 60 + s) * TICKS + int(fraction)


def finite(value):
    return type(value) in (int, float) and math.isfinite(value)


def validate_row(row):
    if (len(row) != 7 or type(row[0]) is not int or type(row[5]) is not bool
            or not all(finite(row[i]) for i in (1, 2, 3, 4, 6))):
        raise ValueError("invalid sample: expected ticks, encounter x/z, reported speed/depth, docked, target depth")


def snapshot(response):
    pos, stamp, ship = (response[k] for k in ("position", "time", "ship"))
    if (response.get("ok") is not True or response.get("sandbox") is not True
            or response.get("loading", {}).get("ready") is not True
            or not all(part.get("ok") is True for part in (pos, stamp, ship))):
        raise ValueError("bridge does not report a ready campaign snapshot")
    row = [clock_tick(stamp["current"]), pos["x"], pos["z"], ship["velocity"],
           ship["depth"], ship["docked"], ship["targetDepth"]]
    validate_row(row)
    return row


def check_log(path, mode):
    lines = path.read_text(encoding="utf-8-sig", errors="replace").splitlines()
    current = "\n".join(line for line in lines if "[RadioNav][CURRENT]" in line)
    disabled = "disabled (RADIONAV_CURRENT_FIELD unset)" in current
    zero = ("requested field east=0 north=0 m/s" in current
            and "no ambient flow requested; no water drag transpilers installed" in current
            and "[CURRENT] water drag transpilers installed" not in current)
    requested = [line for line in current.splitlines() if "requested field" in line]
    matches = disabled and not requested if mode == "off" else zero and not disabled and len(requested) == 1
    if not matches or any(re.search(r"\berror CS\d+", line, re.IGNORECASE) for line in lines):
        raise ValueError("game log does not corroborate the requested current-adapter mode")


def capture(work, mode, start, output, log, cadence=0.5, verify_clock=None):
    manifest = verified_stage(work)
    # "load the named copy and position at the agreed clock" is a manual precondition.
    # /state does not attest loaded save identity or the controls/settings.
    saves = Path(manifest["saves"])
    output = output.resolve()
    if output == saves or saves in output.parents:
        raise ValueError("write receipts outside the protected Saves directory")
    deployed = saves / COPY_NAME
    if file_hash(deployed) != manifest["save_sha256"]:
        raise ValueError("test save in Saves does not match the staged copy")
    if output.exists():
        raise FileExistsError(output)
    check_log(log, mode)
    start_tick = clock_tick(start)
    opener = build_opener(ProxyHandler({}))
    began = time.monotonic()
    rows = []
    with output.with_suffix(".samples.jsonl").open("x", encoding="utf-8") as journal:
        print(f"{mode}: armed for {start}; journal: {journal.name}", file=sys.stderr, flush=True)
        last_report = -1
        while time.monotonic() - began < 900:
            sampled_at = time.monotonic()
            if verify_clock is not None:
                verify_clock()
            with opener.open(STATE, timeout=8) as response:
                row = snapshot(json.load(response))
            if not rows and row[0] < start_tick - 2 * TICKS:
                if sampled_at - began >= 30:
                    raise ValueError("game clock did not approach the agreed start within 30 wall seconds")
                time.sleep(max(0, cadence - (time.monotonic() - sampled_at)))
                continue
            if not rows and row[0] > start_tick:
                raise ValueError("missed the agreed start tick")
            if rows and row[0] < rows[-1][0]:
                raise ValueError("game clock went backwards")
            if rows and row[0] - rows[-1][0] > MAX_GAP:
                journal.write(json.dumps({"wall_s": sampled_at - began, "row": row}) + "\n")
                raise ValueError("sample gap during capture (clock rate changed or capture stalled)")
            if not rows or row[0] > rows[-1][0]:
                rows.append(row)
                journal.write(json.dumps({"wall_s": sampled_at - began, "row": row}) + "\n")
                journal.flush()
            elapsed = (row[0] - start_tick) / TICKS
            if elapsed >= SECONDS:
                break
            bucket = int(max(0, elapsed)) // 30
            if bucket > last_report:
                last_report = bucket
                print(f"{mode}: {max(0, elapsed):.1f}/{SECONDS} game s",
                      file=sys.stderr, flush=True)
            time.sleep(max(0, cadence - (time.monotonic() - sampled_at)))
        else:
            raise ValueError("capture timed out before 600 game seconds")
    data = dict(mode=mode, save_sha256=manifest["save_sha256"], start_tick=start_tick, rows=rows,
                observed_wall_s=time.monotonic() - began)
    list(common_samples(data))
    write_new(output, data)
    return dict(captured=len(rows), receipt=str(output))


def common_samples(data):
    rows, start = data["rows"], data["start_tick"]
    if type(start) is not int or len(rows) < 2:
        raise ValueError("missing game-time window")
    for row in rows:
        validate_row(row)
        if row[5] or row[6] != 0:
            raise ValueError("open-sea control must remain undocked with surface as target")
    if not rows[0][0] <= start <= start + SECONDS * TICKS <= rows[-1][0]:
        raise ValueError("receipt does not cover the agreed 600 game seconds")
    for a, b in zip(rows, rows[1:]):
        if not 0 < b[0] - a[0] <= MAX_GAP:
            raise ValueError("sample gap or unordered ticks")
    index = 0
    for target in range(start, start + SECONDS * TICKS + 1, TICKS):
        while rows[index + 1][0] < target:
            index += 1
        a, b = rows[index:index + 2]
        weight = (target - a[0]) / (b[0] - a[0])
        yield [a[i] + weight * (b[i] - a[i]) for i in range(1, 5)]


def differences(a, b):
    pairs = list(zip(a, b))
    return dict(position_game_m=1000 * max(math.hypot(p[0] - q[0], p[1] - q[1]) for p, q in pairs),
                reported_speed=max(abs(p[2] - q[2]) for p, q in pairs),
                reported_depth_m=max(abs(p[3] - q[3]) for p, q in pairs))


def compare(paths):
    if len({path.resolve() for path in paths}) != 3:
        raise ValueError("three separate receipts required")
    runs = [read_json(path) for path in paths]
    if [run["mode"] for run in runs] != ["off", "off", "zero"]:
        raise ValueError("expected off, off, zero receipts")
    for key in ("save_sha256", "start_tick"):
        if len({run[key] for run in runs}) != 1:
            raise ValueError(f"receipts disagree on {key}")
    off_a, off_b, zero = [list(common_samples(run)) for run in runs]
    spread = differences(off_a, off_b)
    delta = differences(off_a, zero)
    return dict(off_spread=spread, zero_difference=delta, common_samples=len(zero),
                within_off_spread=all(delta[key] <= limit for key, limit in spread.items()))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    subs = parser.add_subparsers(dest="command", required=True)
    stage = subs.add_parser("prepare")
    stage.add_argument("saves", type=Path)
    stage.add_argument("name")
    stage.add_argument("work", type=Path)
    subs.add_parser("verify").add_argument("work", type=Path)
    live = subs.add_parser("capture")
    live.add_argument("work", type=Path)
    live.add_argument("mode", choices=("off", "zero"))
    live.add_argument("start", help="agreed game DateTime from /state (7 fractional digits)")
    live.add_argument("output", type=Path)
    live.add_argument("--log", type=Path, default=LOG)
    subs.add_parser("compare").add_argument("receipts", nargs=3, type=Path)
    args = parser.parse_args()
    try:
        if args.command == "prepare":
            result = prepare(args.saves, args.name, args.work)
        elif args.command == "verify":
            result = verify(args.work)
        elif args.command == "capture":
            result = capture(args.work, args.mode, args.start, args.output, args.log)
        else:
            result = compare(args.receipts)
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(2, f"{error}\n")
    print(json.dumps(result, indent=2, allow_nan=False))
    return 0 if result.get("within_off_spread", True) else 1


if __name__ == "__main__":
    raise SystemExit(main())
