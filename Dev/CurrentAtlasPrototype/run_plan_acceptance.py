"""Acceptance queue from plan §13.34: boundary, handoff continuity, full fairway pass."""
import json
import math
import os
from pathlib import Path
import re
import shutil
import sys
import time
from urllib.parse import urlencode
from urllib.request import Request

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "artifacts/reports/current-b1-control-20261121"))
import run_live_series as live
from run_depth_receipt import SHIP, request
from run_nearby_receipt import PREIMAGE, console, current_report
from run_underway_receipt import post

FIXTURE = "UBOAT.Mods.RadioNavigation.CurrentPlanLiveFixture"
REPORTS = ROOT / "artifacts/reports"
FAR = re.compile(r"\[CURRENT\] far applied=(\d+) skippedLong=(\d+) noCoverage=(\d+)"
                 r" noOrigin=(\d+) unsupported=(\d+)")
DAY = 2592000
FIELD_MPS = 0.01
GEAR = 2


def fixture(method, *args):
    receipt = request("/reflect/call", type=FIXTURE, method=method,
                      args=json.dumps(args, separators=(",", ":")))
    if receipt.get("ok") is not True:
        raise RuntimeError(f"{method}: {receipt}")
    return receipt.get("value")


def numbers(line, count=11):
    values = [float(value) for value in line.split(",")]
    if len(values) != count:
        raise ValueError(f"expected {count} numbers, got {values!r}")
    return values


def sailing():
    docked, x, y, route, distance = fixture("Progress").split(",")
    return dict(docked=docked == "1", x=float(x), y=float(y), route=int(route),
                distance=float(distance))


def far_counters():
    for line in current_report():
        match = FAR.search(line)
        if match:
            return dict(applied=int(match[1]), skipped=int(match[2]), no_coverage=int(match[3]),
                        no_origin=int(match[4]), unsupported=int(match[5]))
    raise RuntimeError("no far counters")


def wait(read, done, timeout, label):
    end = time.monotonic() + timeout
    while True:
        value = read()
        if done(value):
            return value
        if time.monotonic() >= end:
            raise RuntimeError(f"{label}: timeout, last {value!r}")
        time.sleep(1)


def boundary(label):
    rows = []
    for seconds in (DAY - 1, DAY, DAY + 1, 600):
        result = numbers(fixture("Boundary", seconds))
        rows.append(dict(seconds=seconds, drift_km=result[0], cross_km=result[1],
                         body_km_s=result[2:4], tick=result[4],
                         before=result[5:7], after=result[7:9], water_mps=result[9:11],
                         counters=far_counters()))
    live.b1.write_new(live.WORK / "boundary.json", dict(
        expected_km=DAY * FIELD_MPS / 1000, tolerance_km=0.05, rows=rows))


def handoff(label):
    if fixture("WatchStart") is not True:
        raise RuntimeError("watch start refused")
    console("Spawn Transport")
    pick = wait(lambda: fixture("WatchPick"), lambda value: value == "spawned",
                120, "spawned transport")
    if pick != "spawned":
        raise RuntimeError(f"no fully spawned moving group: {pick}")
    ship = numbers(fixture("HandoffShip"), 7)
    raw = fixture("HandoffUnspawn")
    if raw.startswith("error:"):
        raise RuntimeError(raw)
    unspawned = numbers(raw, 8)
    far = numbers(fixture("HandoffFar"), 4)
    far_vec = unspawned[1:3]
    water = unspawned[5:7]
    stock = [far_vec[0] - water[0] / 1000, far_vec[1] - water[1] / 1000]
    jump = math.hypot(*far_vec) * 1000
    stock_mps = math.hypot(*stock) * 1000
    added = jump - stock_mps
    native = math.hypot(ship[4], ship[6])
    live.b1.write_new(live.WORK / f"{label}.json", dict(
        spawn_state=pick, tick=ship[0], state=unspawned[0],
        ship_velocity=ship[4:7], native_mps=native, far_mps=jump,
        body_km_s=unspawned[1:3], expected_km_s=unspawned[3:5],
        water_mps=water, stock_mps=stock_mps, added_mps=added,
        native_delta_mps=jump - native,
        body_matches_expected=math.dist(unspawned[1:3], unspawned[3:5]) < 1e-6,
        far_position=far[1:3]))


def fairway(label, capture_motion=False, min_entry_km=20, min_offshore_km=25):
    if not sailing()["docked"]:
        console('Rebase "La Rochelle"')
        wait(sailing, lambda boat: boat["docked"], 180, "rebase")
    if capture_motion:
        wait(lambda: fixture("DepartureCacheReady"), lambda ready: ready is True,
             30, "departure berth cache")
    plan = numbers(fixture("PassageRoute"), 7)
    entry_x, entry_y, dock_x, dock_y, heading, entry_km, lane = plan[:7]
    if entry_km < min_entry_km:
        raise RuntimeError(f"outer entry is only {entry_km:.1f} km from the berth")
    player, = request("/reflect/instances", type=SHIP)["instances"]
    request("/reflect/call", type=SHIP, instance_id=player["id"], method="UndockAsync", args="[]")
    wait(sailing, lambda boat: not boat["docked"], 90, "undock")
    if fixture("Place", entry_x, entry_y) is not True:
        raise RuntimeError("outer-entry placement refused")
    time.sleep(4)
    start = sailing()
    if start["docked"] or math.hypot(start["x"] - entry_x, start["y"] - entry_y) > 0.2:
        raise RuntimeError(f"prepared start is not the outer entry: {start}")
    if math.hypot(start["x"] - dock_x, start["y"] - dock_y) < min_offshore_km:
        raise RuntimeError("prepared start is not offshore enough for the passage")
    post("/ship/heading", deg=heading % 360)
    post("/ship/rudder", value=0)
    if fixture("ClearLeg") is not True:
        raise RuntimeError("could not clear the undock route")
    order = console("rn_current_fairway")
    if capture_motion and sailing()["route"] <= 0:
        # The first command can run while the launched port scene remains
        # active; no-route intentionally preserves the route. Wait for the
        # native request to become available, not for a forced dock or teleport.
        def try_order():
            receipt = console("rn_current_fairway")
            return (sailing()["route"] > 0, receipt)
        _, order = wait(try_order, lambda result: result[0], 20, "native fairway route")
    if sailing()["route"] <= 0:
        raise RuntimeError(f"fairway order refused: {order}")
    request("/reflect/call", type=SHIP, instance_id=player["id"],
            method="OrderEngineGear", args=f"[{GEAR},false,1]")
    post("/time/scale", value=140)
    trace = []
    began = time.monotonic()
    docked = None
    heartbeat = 0
    try:
        with (live.WORK / "fairway-track.jsonl").open("x", encoding="utf-8") as journal:
            while time.monotonic() - began < 900:
                boat = sailing()
                boat["wall_s"] = round(time.monotonic() - began, 1)
                if capture_motion:
                    boat["instruments"] = fairway_motion()
                trace.append(boat)
                journal.write(json.dumps(boat) + "\n")
                journal.flush()
                if capture_motion and boat["wall_s"] >= heartbeat:
                    live.progress(f"{label}: {boat['distance']:.2f} km from berth; "
                                  f"TC={boat['instruments']['tc']:g}; {900-boat['wall_s']:.0f}s budget left")
                    heartbeat = boat["wall_s"] + 20
                if boat["docked"]:
                    docked = boat
                    held = sailing()
                    if capture_motion:
                        held["instruments"] = fairway_motion()
                        held["drift"] = fixture("FairwayDrift")
                    time.sleep(15)
                    later = sailing()
                    if capture_motion:
                        later["instruments"] = fairway_motion()
                        later["drift"] = fixture("FairwayDrift")
                    break
                if boat["distance"] < 1 and boat["route"] > 0:
                    post("/time/scale", value=1)
                    boat["motion"] = fixture("Motion")
                time.sleep(3)
    finally:
        post("/time/scale", value=1)
    held_move_m = math.hypot(later["x"] - held["x"], later["y"] - held["y"]) * 1000 if docked else None
    result = dict(
        entry_km=entry_km, lane_points=int(lane), start=start, trigger=[dock_x, dock_y],
        order=order, docked=docked is not None, end=docked, held_move_m=held_move_m,
        wall_seconds=time.monotonic() - began,
        wall_budget_s=900,
        tc=dict(cruise=140, approach=1, approach_below_km=1),
        min_distance_km=min([row["distance"] for row in trace] or [99.0]),
        trace_len=len(trace))
    if capture_motion and docked:
        result["heldInstruments"] = [held["instruments"], later["instruments"]]
        result["heldDrift"] = [held["drift"], later["drift"]]
    live.b1.write_new(live.WORK / "fairway.json", result)


def fairway_motion():
    raw = fixture("FairwayMotion").split(",")
    if len(raw) != 18:
        raise ValueError(f"fairway motion unavailable: {raw}")
    keys = ("tc", "sandboxScale", "depthM", "yawDeg", "shipAverageMps",
            "groundEastMps", "groundNorthMps", "windEastMps", "windNorthMps",
            "windKnots", "weather", "covered", "waypointEastKm", "waypointNorthKm",
            "waterEastMps", "waterNorthMps")
    return dict(ticks=int(raw[0]), utc=raw[1], **dict(zip(keys, map(float, raw[2:]))))


def report():
    """Verdict over the parts actually executed. A missing part is NOT RUN,
    never a silent pass; every executed part must pass."""
    folder = live.WORK
    verdicts = {}
    result = {}

    if (folder / "boundary.json").is_file():
        boundary = json.loads((folder / "boundary.json").read_text())
        result["boundary"] = boundary
        rows = boundary["rows"]
        expected = boundary["expected_km"]
        tolerance = boundary["tolerance_km"]
        at_limit, above, after = rows[1], rows[2], rows[3]
        verdicts["boundary"] = (
            abs(at_limit["drift_km"] - expected) <= tolerance
            and abs(rows[0]["drift_km"] - (DAY - 1) * FIELD_MPS / 1000) <= tolerance
            and abs(above["drift_km"]) <= 0.001
            and above["counters"]["unsupported"] > at_limit["counters"]["unsupported"]
            and abs(after["drift_km"] - 600 * FIELD_MPS / 1000) <= tolerance)

    paths = {side: folder / f"{side}.json" for side in ("handoff-off", "handoff-on")}
    if all(path.is_file() for path in paths.values()):
        off = json.loads(paths["handoff-off"].read_text())
        on = json.loads(paths["handoff-on"].read_text())
        result["handoff"] = dict(
            off_added_mps=off["added_mps"], on_added_mps=on["added_mps"],
            on_water_mps=on["water_mps"], off_water_mps=off["water_mps"],
            off_native_delta_mps=off["native_delta_mps"],
            on_native_delta_mps=on["native_delta_mps"],
            body_matches_expected=on["body_matches_expected"] and off["body_matches_expected"])
        verdicts["handoff"] = (
            abs(on["added_mps"]) <= 0.05 and abs(off["added_mps"]) <= 1e-6
            and on["body_matches_expected"] and off["body_matches_expected"])

    if (folder / "fairway.json").is_file():
        fairway = json.loads((folder / "fairway.json").read_text())
        result["fairway_docked"] = fairway["docked"]
        result["fairway_held_move_m"] = fairway["held_move_m"]
        verdicts["fairway"] = bool(fairway["docked"]) and fairway["held_move_m"] is not None \
            and fairway["held_move_m"] <= 2.0

    missing = [name for name in ("boundary", "handoff", "fairway") if name not in verdicts]
    passed = bool(verdicts) and all(verdicts.values())
    live.b1.write_new(folder / "comparison.json", dict(
        passed=passed,
        parts={name: ("PASS" if verdict else "FAIL") for name, verdict in verdicts.items()},
        not_run=missing, **result))
    return 0 if passed else 1


def main():
    live.b1.verify(PREIMAGE)
    live.WORK = REPORTS / sys.argv[1]
    live.WORK.mkdir(exist_ok=False)
    for name in ("manifest.json", live.b1.COPY_NAME):
        shutil.copyfile(PREIMAGE / name, live.WORK / name)
    os.environ["RADIONAV_ROUTE_CURRENT"] = "1"
    cases = [
        ("boundary", "field", dict(field=f"{FIELD_MPS},0", far=True, collector=boundary)),
        ("handoff-off", "off", dict(collector=handoff)),
        ("handoff-on", "field", dict(field=f"{FIELD_MPS},0", far=True, collector=handoff)),
        ("fairway", "field", dict(field="0.02,0", wind=False, collector=fairway)),
    ]
    chosen = [case for case in cases if not sys.argv[2:] or case[0] in sys.argv[2:]]
    return live.isolated(chosen, report)


if __name__ == "__main__":
    raise SystemExit(main())
