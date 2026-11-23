"""Run controlled long-interval/HUD and packaged-atlas receipts on disposable saves."""
import json
import math
import os
from pathlib import Path
import re
import shutil
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "artifacts/reports/current-b1-control-20261121"))
import run_live_series as live
from run_depth_receipt import SHIP, request, state
from run_nearby_receipt import console, current_report
from run_underway_receipt import controls, post


FIXTURE = "UBOAT.Mods.RadioNavigation.CurrentExpansionLiveFixture"
REPORTS = ROOT / "artifacts/reports"
PREIMAGE = REPORTS / "current-b1-control-20261121"
FIELD = re.compile(r"field=([-+\d.eE]+),([-+\d.eE]+).*?kind=atlas")
ETA = re.compile(r"~\d+")
SECONDS = 36 * 3600


def fixture(method, *args):
    receipt = request("/reflect/call", type=FIXTURE, method=method,
                      args=json.dumps(args, separators=(",", ":")))
    if receipt.get("ok") is not True:
        raise RuntimeError(f"{method}: {receipt}")
    return receipt.get("value")


def wait(read, predicate, timeout):
    end = time.monotonic() + timeout
    while True:
        value = read()
        if predicate(value):
            return value
        if time.monotonic() >= end:
            raise RuntimeError(f"{read}: timeout, last reading {value!r}")
        time.sleep(1)


def constant(label):
    vector = [float(item) for item in fixture("LongInterval", SECONDS).split(",")]
    assert abs(vector[0] - SECONDS / 1000) < .05 and abs(vector[1]) < .005, vector
    assert all(abs(a - b) < 2e-7 for a, b in zip(vector[2:], [.001, 0, 0, .001, 0])), vector
    resets = [float(item) for item in fixture("NativeVelocityReset").split(",")]
    assert all(abs(a - b) < 2e-7 for a, b in zip(resets, [.001, 0, .001, 0])), resets
    live.b1.write_new(live.WORK / "far.json", dict(seconds=SECONDS, result=vector, resets=resets))
    import run_underway_receipt
    run_underway_receipt.OUT = live.WORK
    controls(label)
    row = state()
    assert fixture("SetLeg", row[1], row[2] + 0.75)
    console("Map")
    first = wait(lambda: fixture("Hud"), lambda text: text and ETA.search(text), 60)
    time.sleep(12)
    second = fixture("Hud")
    assert second and ETA.search(second), second
    shot = live.get("/screenshot?mode=frame", timeout=15)
    if shot.get("ok"):
        shutil.copyfile(shot["path"], live.WORK / "hud.png")
    assert fixture("ClearLeg")
    wait(lambda: fixture("Hud"), lambda text: text == "", 8)
    live.b1.write_new(live.WORK / "hud.json",
                      dict(first=first, after_12s=second, after_clear="", capture=shot))


def current():
    match = FIELD.search("\n".join(current_report()))
    if not match:
        raise RuntimeError("packaged atlas not active")
    return [float(match[1]), float(match[2])]


def atlas(label):
    player, = request("/reflect/instances", type=SHIP)["instances"]
    samples = []
    for lat, lon, covered in ((48, -15.75, True), (52, -34, True), (29, -34, False)):
        console(f"Teleport {lat} {lon}")
        time.sleep(4)
        depth = request("/reflect/get", type=SHIP, instance_id=player["id"], member="Depth")["value"]
        water = current()
        samples.append(dict(lat=lat, lon=lon, depth=depth, water=water))
        assert float(depth) < 5, samples
        assert (math.hypot(*water) > .001) == covered, samples
    live.b1.write_new(live.WORK / "surface.json", samples)
    measurement = [float(value) for value in fixture("AtlasInterval", 52., -34.).split(",")]
    expected = [component * .6 for component in samples[1]["water"]]
    assert math.dist(measurement[:2], expected) < .007, (measurement, expected)
    assert math.hypot(*measurement[2:4]) > 1e-6, measurement
    live.b1.write_new(live.WORK / "far-atlas.json", dict(result=measurement, expected_km=expected))


def sailing():
    docked, x, y, route = fixture("Sailing").split(",")
    return dict(docked=docked == "1", x=float(x), y=float(y), route=int(route))


def fairway(label):
    if not sailing()["docked"]:
        console('Rebase "La Rochelle"')
        wait(sailing, lambda boat: boat["docked"], 150)
    inner = fixture("FairwayStart", False)
    if inner == "missing":
        raise RuntimeError("native dock area missing")
    offshore = fixture("FairwayStart", True)
    player, = request("/reflect/instances", type=SHIP)["instances"]
    request("/reflect/call", type=SHIP, instance_id=player["id"], method="UndockAsync", args="[]")
    wait(sailing, lambda boat: not boat["docked"], 60)
    for phase, station in (("offshore", offshore), ("inner", inner)):
        x, y, dx, dy, route = [float(value) for value in station.split(",")]
        assert fixture("Place", x, y)
        time.sleep(5)
        if phase == "inner":
            wait(lambda: fixture("DockAreas"), lambda areas: "active=True|free=True|ally=True" in areas, 65)
        start = sailing()
        assert not start["docked"] and math.hypot(x - start["x"], y - start["y"]) < .2, start
        assert math.hypot(start["x"] - dx, start["y"] - dy) > .2, (start, station)
        assert fixture("ClearLeg")
        order = console("rn_current_fairway")
        native = sailing()
        live.b1.write_new(live.WORK / f"fairway-{phase}-order.json",
                          dict(station=station, start=start, order=order, boat=native, areas=fixture("DockAreas")))
        if phase == "inner":
            assert native["route"] > 0, order
        else:
            assert native["route"] >= route - 1, order
    import run_underway_receipt
    run_underway_receipt.OUT = live.WORK
    heading = math.degrees(math.atan2(dx - start["x"], dy - start["y"])) % 360
    post("/ship/heading", deg=heading)
    post("/ship/rudder", value=0)
    request("/reflect/call", type=SHIP, instance_id=player["id"], method="OrderEngineGear", args="[2,false,1]")
    post("/time/scale", value=2)
    deadline = time.monotonic() + 190
    try:
        with (live.WORK / "fairway-track.jsonl").open("x") as trace:
            started = time.monotonic()
            while True:
                boat = sailing()
                trace.write(json.dumps(boat) + "\n")
                trace.flush()
                if boat["docked"]:
                    live.b1.write_new(live.WORK / "dock.json", dict(start=start, end=boat,
                                      wall_seconds=time.monotonic() - started, authored=route >= 2))
                    break
                if time.monotonic() >= deadline:
                    raise RuntimeError(f"native dock trigger did not fire: {boat}")
                time.sleep(3)
    finally:
        post("/time/scale", value=1)


def main():
    live.b1.verify(PREIMAGE)
    live.WORK = REPORTS / sys.argv[1]
    live.WORK.mkdir(exist_ok=False)
    for name in ("manifest.json", live.b1.COPY_NAME):
        shutil.copyfile(PREIMAGE / name, live.WORK / name)
    os.environ["RADIONAV_ROUTE_CURRENT"] = "1"
    cases = dict(constant=("constant", "field", dict(field="1,0", far=True, collector=constant)),
                 atlas=("atlas", "atlas", dict(atlas="bundled", far=True, collector=atlas)),
                 fairway=("fairway", "field", dict(field="0.2,0", wind=True, collector=fairway)))
    series = [cases[name] for name in (sys.argv[2:] or cases)]
    return live.isolated(series, lambda: 0)


if __name__ == "__main__":
    raise SystemExit(main())
