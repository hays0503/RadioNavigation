"""Aggregate the §13.34 receipts: boundary and handoff from live-9, fairway from live-13."""
import json
from pathlib import Path

REPORTS = Path(__file__).resolve().parents[2] / "artifacts/reports"
BOUNDARY = REPORTS / "current-plan-live-9/boundary.json"
HANDOFF = {side: REPORTS / f"current-plan-live-9/handoff-{side}.json" for side in ("off", "on")}
FAIRWAY = REPORTS / "current-plan-live-13/fairway.json"
OUT = REPORTS / "current-plan-live-13/comparison.json"
DAY = 2592000
FIELD = 0.01

boundary = json.loads(BOUNDARY.read_text())
rows = {row["seconds"]: row for row in boundary["rows"]}
tolerance = boundary["tolerance_km"]


def near(value, expected, limit=tolerance):
    return abs(value - expected) <= limit


boundary_ok = (
    near(rows[DAY - 1]["drift_km"], (DAY - 1) * FIELD / 1000)
    and near(rows[DAY]["drift_km"], DAY * FIELD / 1000)
    and abs(rows[DAY + 1]["drift_km"]) <= 0.001
    and rows[DAY + 1]["counters"]["unsupported"] == rows[DAY]["counters"]["unsupported"] + 1
    and near(rows[600]["drift_km"], 600 * FIELD / 1000)
)

off = json.loads(HANDOFF["off"].read_text())
on = json.loads(HANDOFF["on"].read_text())
handoff_ok = (abs(on["added_mps"]) <= 0.05 and abs(off["added_mps"]) <= 1e-6
              and on["body_matches_expected"] and off["body_matches_expected"])

fairway = json.loads(FAIRWAY.read_text())
fairway_ok = (fairway["docked"] and fairway["entry_km"] > 20
              and fairway["held_move_m"] is not None and fairway["held_move_m"] <= 2.0)

result = dict(
    passed=boundary_ok and handoff_ok and fairway_ok,
    boundary_ok=boundary_ok, handoff_ok=handoff_ok, fairway_ok=fairway_ok,
    boundary=dict(rows={str(DAY - 1): rows[DAY - 1]["drift_km"], str(DAY): rows[DAY]["drift_km"],
                        str(DAY + 1): rows[DAY + 1]["drift_km"], "600": rows[600]["drift_km"]},
                  expected_km=boundary["expected_km"], tolerance_km=tolerance),
    handoff=dict(off_added_mps=off["added_mps"], on_added_mps=on["added_mps"],
                 on_water_mps=on["water_mps"], off_native_delta_mps=off["native_delta_mps"],
                 on_native_delta_mps=on["native_delta_mps"],
                 body_matches_expected=on["body_matches_expected"] and off["body_matches_expected"]),
    fairway=dict(entry_km=fairway["entry_km"], docked=fairway["docked"],
                 wall_seconds=fairway["wall_seconds"], held_move_m=fairway["held_move_m"],
                 lane_points=fairway["lane_points"]),
    sources=dict(boundary=str(BOUNDARY.relative_to(REPORTS)),
                 handoff=[str(HANDOFF[s].relative_to(REPORTS)) for s in HANDOFF],
                 fairway=str(FAIRWAY.relative_to(REPORTS))),
    note="Boundary and handoff were measured in one isolated session (live-9); the fairway "
         "pass in another (live-13, TC 140, TC 1 below 1 km). Each launch re-verified all "
         "30 original saves. F8 is not part of this receipt: no target machine was declared.",
)
OUT.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps({key: result[key] for key in ("passed", "boundary_ok", "handoff_ok", "fairway_ok")}))
