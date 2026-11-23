"""Live verification for naturalStorm and expanded vessel/convoy coverage."""
import argparse
import json
import math
import os
import re
import shutil
import sys
import time
from pathlib import Path
from urllib.request import Request

ROOT = Path(__file__).resolve().parents[2]
STAGE = ROOT / "artifacts/reports/current-wind-stage-20261122"
FIXTURE_PATH = Path(__file__).resolve().with_name("WindLabelLiveFixture.cs")
TARGET = ROOT / "Source/Logic" / FIXTURE_PATH.name
HIGH_FIXTURE_PATH = Path(__file__).resolve().with_name("HighTcLiveFixture.cs")
HIGH_TARGET = ROOT / "Source/Logic" / HIGH_FIXTURE_PATH.name

BOUND_RE = re.compile(r"\[RadioNav\]\[CURRENT\] bound nearby (\S+) \(([^)]+)\)")
NEARBY_RE = re.compile(r"\[RadioNav\]\[CURRENT\] nearby (\S+) water=(\d+) air=(\d+) velocity=\S+ current=\S+ exposure=\S+ active=(True|False)")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("work", type=Path)
    parser.add_argument("--stage", type=Path, default=STAGE)
    parser.add_argument("--confirm-cloud-off", action="store_true")
    args = parser.parse_args()
    if not args.confirm_cloud_off:
        parser.error("Steam Cloud must be off")

    sys.path.insert(0, str(ROOT / "artifacts/reports/current-b1-control-20261121"))
    import run_live_series as live
    from run_depth_receipt import request, state
    from run_nearby_receipt import console

    if live.game_running() or live.bridge_listening() or TARGET.exists() or HIGH_TARGET.exists():
        parser.error("game, bridge or temporary fixture is already present")

    manifest = live.b1.verified_stage(args.stage)
    live.b1.verify(args.stage)
    if Path(manifest["saves"]).resolve() != (ROOT.parents[1] / "Saves").resolve():
        parser.error("stage manifest must protect actual UBOAT Saves directory")

    live.WORK = args.work.resolve()
    live.WORK.mkdir(parents=True, exist_ok=False)
    for name in ("manifest.json", live.b1.COPY_NAME):
        shutil.copyfile(args.stage / name, live.WORK / name)

    def collect(label):
        # 1. Natural storm on surface & submerged player boat
        console("Weather Storm")
        time.sleep(3.0)
        before_storm = console("rn_current")
        surface_probe = request("/reflect/call", type="UBOAT.Mods.RadioNavigation.WindLabelLiveFixture", method="Probe", args="[]")
        time.sleep(15.0)
        after_storm = console("rn_current")

        # Dive beneath 0.5 m during storm -> direct wind must stop while water continues
        dive_reply = request("/reflect/call", type="UBOAT.Mods.RadioNavigation.WindLabelLiveFixture", method="SetDepth", args="[12.0]")
        deadline = time.monotonic() + 180
        while time.monotonic() < deadline:
            p = request("/reflect/call", type="UBOAT.Mods.RadioNavigation.WindLabelLiveFixture", method="Probe", args="[]")
            val = p.get("value", "")
            depth_part = [x.split("=", 1)[1] for x in val.split(";") if x.startswith("depth=")]
            if depth_part and float(depth_part[0]) > 3.0:
                break
            time.sleep(2.0)
        dived_before = console("rn_current")
        time.sleep(12.0)
        dived_after = console("rn_current")

        # Resurface & reset weather
        request("/reflect/call", type="UBOAT.Mods.RadioNavigation.WindLabelLiveFixture", method="SetDepth", args="[-4.0]")
        console("Weather Reset")
        time.sleep(3.0)

        # 2. Spawn expanded vessel types & convoy (Convoy, Warships, Transport, CoastalPatrol/Submarine)
        spawn_replies = {}
        for cmd in ("Spawn Convoy * 3", "Spawn Warships * 4", "Spawn Transport * 4", "Spawn Submarine * 3"):
            spawn_replies[cmd] = console(cmd)
            time.sleep(1.5)

        time.sleep(25.0)
        nearby_after = console("rn_current")

        log_text = live.b1.LOG.read_text(encoding="utf-8-sig", errors="replace")
        bound = [{"kind": k, "name": n} for k, n in BOUND_RE.findall(log_text)]
        nearby_rows = [{"kind": k, "water": int(w), "air": int(a), "active": act == "True"}
                       for k, w, a, act in NEARBY_RE.findall(log_text)]

        live.b1.write_new(live.WORK / f"{label}-data.json", dict(
            before_storm=before_storm,
            surface_probe=surface_probe,
            after_storm=after_storm,
            dive_reply=dive_reply,
            dived_before=dived_before,
            dived_after=dived_after,
            spawn_replies=spawn_replies,
            nearby_after=nearby_after,
            bound=bound,
            nearby_rows=nearby_rows,
        ))

    def parse_counts(rn_reply):
        line = next((row for row in rn_reply.get("lines", []) if "waterSteps=" in row), "")
        def num(key):
            m = re.search(rf"{key}=(\d+)", line)
            return int(m.group(1)) if m else 0
        wind_m = re.search(r"wind=([^,\s]+),([^\s]+)", line)
        w_spd = math.hypot(float(wind_m.group(1)), float(wind_m.group(2))) if wind_m else 0.0
        exp_m = re.search(r"exposure=([0-9.]+)", line)
        exp = float(exp_m.group(1)) if exp_m else -1.0
        return dict(water=num("waterSteps"), air=num("airSteps"), tracked=num("trackedNearby"), windMps=w_spd, exposure=exp)

    def report():
        d = live.b1.read_json(live.WORK / "storm_convoy-data.json")
        bs = parse_counts(d["before_storm"])
        asf = parse_counts(d["after_storm"])
        db = parse_counts(d["dived_before"])
        da = parse_counts(d["dived_after"])
        na = parse_counts(d["nearby_after"])

        storm_wind_ok = bs["windMps"] >= 8.0 and asf["windMps"] >= 8.0
        storm_surface_ok = asf["water"] > bs["water"] + 50 and asf["air"] > bs["air"] + 50 and asf["exposure"] > 0.5
        storm_dived_ok = da["water"] > db["water"] + 50 and da["air"] == db["air"] and abs(da["exposure"]) < 1e-6

        distinct_kinds = sorted({row["kind"] for row in d["bound"]})
        active_kinds = sorted({row["kind"] for row in d["nearby_rows"] if row["water"] > 20})
        no_aircraft = "Aircraft" not in distinct_kinds
        convoy_multi_ok = len(d["bound"]) >= 2 and na["tracked"] >= 2 and no_aircraft

        passed = storm_wind_ok and storm_surface_ok and storm_dived_ok and convoy_multi_ok
        summary = dict(
            status="PASS" if passed else "FAIL",
            storm_wind_ok=storm_wind_ok,
            storm_surface_ok=storm_surface_ok,
            storm_dived_ok=storm_dived_ok,
            convoy_multi_ok=convoy_multi_ok,
            no_aircraft=no_aircraft,
            distinct_bound_kinds=distinct_kinds,
            active_nearby_kinds=active_kinds,
            bound_count=len(d["bound"]),
            tracked_nearby=na["tracked"],
            storm_surface_wind_mps=asf["windMps"],
            storm_surface_water_delta=asf["water"] - bs["water"],
            storm_surface_air_delta=asf["air"] - bs["air"],
            storm_dived_water_delta=da["water"] - db["water"],
            storm_dived_air_delta=da["air"] - db["air"],
        )
        live.b1.write_new(live.WORK / "comparison.json", summary)
        return 0 if passed else 1

    os.environ["RADIONAV_ROUTE_CURRENT"] = "1"
    c1 = FIXTURE_PATH.read_bytes()
    with TARGET.open("xb") as s:
        s.write(c1)
    try:
        series = [("storm_convoy", "atlas", dict(atlas="bundled", wind=True, nearby=True, rudder=True, far=True, collector=collect))]
        return live.isolated(series, report)
    finally:
        if TARGET.is_file() and TARGET.read_bytes() == c1:
            TARGET.unlink()


if __name__ == "__main__":
    raise SystemExit(main())
