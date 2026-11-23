"""Paired La Rochelle P2 passage on a constant field, with stock weather."""
import argparse
import json
import math
import os
from pathlib import Path
import re
import shutil

ROOT = Path(__file__).resolve().parents[2]
FIXTURE = ROOT / "Dev/CurrentAtlasPrototype/CurrentPlanLiveFixture.cs"
TARGET = ROOT / "Source/Logic" / FIXTURE.name
POLICY = {"fieldMps": [0.02, 0.0], "maxHeldMovementM": 2.0, "minWindMps": 0.3,
          "minEntryKm": 25.0, "cruiseTc": 140, "approachTc": 1, "wallBudgetS": 900}
AIR_WATER = re.compile(r"waterSteps=(\d+) airSteps=(\d+)")


def evidence(result, rows, wind_enabled):
    sw = [row["instruments"] for row in rows if not row["docked"]]
    held = result.get("heldDrift", [])
    counts = [AIR_WATER.search(row) for row in held]
    measured_wind = any(m["weather"] and math.hypot(m["windEastMps"], m["windNorthMps"])
                        >= POLICY["minWindMps"] and m["depthM"] <= 0.5 for m in sw)
    same_water = bool(sw) and all(m["covered"] and math.dist(
        (m["waterEastMps"], m["waterNorthMps"]), POLICY["fieldMps"]) <= 1e-6 for m in sw)
    at_berth = bool(result["docked"]) and result["held_move_m"] is not None and (
        result["held_move_m"] <= POLICY["maxHeldMovementM"])
    gates = {
        "start": result["entry_km"] >= POLICY["minEntryKm"],
        "nativeDockAndHold": at_berth and result["wall_seconds"] <= POLICY["wallBudgetS"],
        "constantWaterAvailable": same_water,
        "windExposure": measured_wind,
        "nearBerthAtTc1": any(not row["docked"] and row["distance"] < 1
                              and row["instruments"]["tc"] == POLICY["approachTc"] for row in rows),
        "noDriftStepsWhileHeld": len(counts) == 2 and None not in counts
        and counts[0].groups() == counts[1].groups() and all("docked=True" in row for row in held),
        "directWindMode": bool(counts and counts[0]) and
        (int(counts[0][2]) > 0 if wind_enabled else int(counts[0][2]) == 0),
    }
    return {"status": "PASS" if all(gates.values()) else "FAIL", "checks": gates}


def main():
    import run_plan_acceptance as plan
    live = plan.live

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("work", type=Path)
    parser.add_argument("--stage", type=Path, required=True)
    parser.add_argument("--confirm-cloud-off", action="store_true")
    args = parser.parse_args()
    if not args.confirm_cloud_off:
        parser.error("Steam Cloud must be off")
    if live.game_running() or live.bridge_listening() or TARGET.exists():
        parser.error("game, bridge or an unexpected temporary fixture is present")
    manifest = live.b1.verified_stage(args.stage)
    live.b1.verify(args.stage)
    if Path(manifest["saves"]).resolve() != (ROOT.parents[1] / "Saves").resolve():
        parser.error("stage manifest must protect the actual UBOAT Saves directory")
    live.WORK = args.work.resolve()
    live.WORK.mkdir(parents=True, exist_ok=False)
    for name in ("manifest.json", live.b1.COPY_NAME):
        shutil.copyfile(args.stage / name, live.WORK / name)
    live.b1.write_new(live.WORK / "run-contract.json", POLICY)
    os.environ["RADIONAV_ROUTE_CURRENT"] = "1"

    def collect(label):
        plan.fairway(label, capture_motion=True)
        for name in ("fairway.json", "fairway-track.jsonl"):
            (live.WORK / name).rename(live.WORK / f"{label}-{name}")

    def report():
        results = {}
        for label in ("water", "water-wind"):
            result = live.b1.read_json(live.WORK / f"{label}-fairway.json")
            track = live.WORK / f"{label}-fairway-track.jsonl"
            rows = [json.loads(line) for line in track.read_text(encoding="utf-8").splitlines()]
            results[label] = evidence(result, rows, label == "water-wind")
        passed = all(row["status"] == "PASS" for row in results.values())
        live.b1.write_new(live.WORK / "comparison.json",
                          {"passed": passed, "parts": results, "not_run": ["ORA-20C", "naturalStorm"]})
        return 0 if passed else 1

    content = FIXTURE.read_bytes()
    with TARGET.open("xb") as stream:
        stream.write(content)
    try:
        series = [(label, "field", dict(field="0.02,0", wind=label == "water-wind", collector=collect))
                  for label in ("water", "water-wind")]
        return live.isolated(series, report)
    except (OSError, RuntimeError, ValueError) as error:
        live.b1.write_new(live.WORK / "incomplete.json", {"status": "NOT RUN", "reason": str(error)})
        raise
    finally:
        if TARGET.is_file() and TARGET.read_bytes() == content:
            TARGET.unlink()


if __name__ == "__main__":
    raise SystemExit(main())
