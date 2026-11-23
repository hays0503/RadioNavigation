"""Coastal ORA-20C (bundled) fairway passage at a covered port (§13.36 #3.2)."""
import argparse
import json
import math
import os
from pathlib import Path
import shutil
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
STAGE = ROOT / "artifacts/reports/current-wind-stage-20261122"
FIXTURE = ROOT / "Dev/CurrentAtlasPrototype/CurrentPlanLiveFixture.cs"
TARGET = ROOT / "Source/Logic" / FIXTURE.name
ATLAS_DIR = ROOT / "Source/Data/CurrentAtlas"
KM_PER_DEG = 40075.16 / 360.0

sys.path.insert(0, str(Path(__file__).resolve().with_name("CoastCompare")))
from atlantic_tile import Tile  # noqa: E402


def check_port_coverage(tiles, port_lane_points, trigger):
    pts = list(port_lane_points) + [tuple(trigger)]
    times = sorted({float(t) for tile in tiles for t in tile.axes[0]})
    depths = [0.0, 5.0]
    summary = {}
    for d in depths:
        counts = []
        for utc in times:
            c = 0
            for x, y in pts:
                la, lo = y / KM_PER_DEG, x / KM_PER_DEG
                if any(tile.sample(utc, d, la, lo) is not None for tile in tiles):
                    c += 1
            counts.append(c)
        summary[str(d)] = dict(points=len(pts), min_covered=min(counts), max_covered=max(counts), counts=counts)
    return summary


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("work", type=Path)
    parser.add_argument("--port", default="Marseille")
    parser.add_argument("--stage", type=Path, default=STAGE)
    parser.add_argument("--confirm-cloud-off", action="store_true")
    args = parser.parse_args()
    if not args.confirm_cloud_off:
        parser.error("Steam Cloud must be off")

    sys.path.insert(0, str(Path(__file__).resolve().parent))
    sys.path.insert(0, str(ROOT / "artifacts/reports/current-b1-control-20261121"))
    import run_plan_acceptance as plan
    live = plan.live
    if live.game_running() or live.bridge_listening() or TARGET.exists():
        parser.error("game, bridge or unexpected temporary fixture present")

    manifest = live.b1.verified_stage(args.stage)
    live.b1.verify(args.stage)
    live.WORK = args.work.resolve()
    live.WORK.mkdir(parents=True, exist_ok=False)
    for name in ("manifest.json", live.b1.COPY_NAME):
        shutil.copyfile(args.stage / name, live.WORK / name)

    tiles = [Tile(p.read_bytes()) for p in sorted(ATLAS_DIR.glob("*.rnw"))]
    os.environ["RADIONAV_ROUTE_CURRENT"] = "1"

    def collect(label):
        plan.console(f'Rebase "{args.port}"')
        plan.wait(plan.sailing, lambda boat: boat["docked"], 180, f"rebase {args.port}")
        plan.fairway(label, capture_motion=True, min_entry_km=5, min_offshore_km=5)
        for name in ("fairway.json", "fairway-track.jsonl"):
            (live.WORK / name).rename(live.WORK / f"{label}-{name}")

    def report():
        res = live.b1.read_json(live.WORK / "bundled-fairway.json")
        rows = [json.loads(line) for line in (live.WORK / "bundled-fairway-track.jsonl").read_text(encoding="utf-8").splitlines()]
        sw = [r["instruments"] for r in rows if not r["docked"]]
        pts = [(r["x"], r["y"]) for r in rows]
        preflight = check_port_coverage(tiles, pts, res["trigger"])
        all_covered = bool(sw) and all(m["covered"] for m in sw)
        docked_held = bool(res["docked"]) and res["held_move_m"] is not None and res["held_move_m"] <= 2.0
        passed = docked_held and all_covered
        live.b1.write_new(live.WORK / "comparison.json", dict(
            status="PASS" if passed else "FAIL",
            port=args.port,
            docked=res["docked"],
            held_move_m=res["held_move_m"],
            all_track_covered_live=all_covered,
            covered_samples=sum(1 for m in sw if m["covered"]),
            total_samples=len(sw),
            preflight_by_depth=preflight,
            wall_seconds=res["wall_seconds"],
        ))
        return 0 if passed else 1

    content = FIXTURE.read_bytes()
    with TARGET.open("xb") as stream:
        stream.write(content)
    try:
        return live.isolated([("bundled", "atlas", dict(atlas="bundled", wind=True, collector=collect))], report)
    finally:
        if TARGET.is_file() and TARGET.read_bytes() == content:
            TARGET.unlink()


if __name__ == "__main__":
    raise SystemExit(main())
