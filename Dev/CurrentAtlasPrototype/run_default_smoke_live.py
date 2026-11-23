"""Live A/B smoke-test comparing explicit off (RADIONAV_CURRENTS=0) vs clean default launch (no RADIONAV_* env)."""
import argparse
import os
import re
import shutil
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
STAGE = ROOT / "artifacts/reports/current-wind-stage-20261122"
FIXTURE_PATH = Path(__file__).resolve().with_name("WindLabelLiveFixture.cs")
TARGET = ROOT / "Source/Logic" / FIXTURE_PATH.name


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
    from run_depth_receipt import request
    from run_nearby_receipt import console

    if live.game_running() or live.bridge_listening() or TARGET.exists():
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
        time.sleep(15.0)
        settings_before = console("rn_currents")
        current = console("rn_current")
        route = console("rn_current_route")
        probe = request("/reflect/call", type="UBOAT.Mods.RadioNavigation.WindLabelLiveFixture", method="Probe", args="[]")
        prefs_roundtrip = {}
        if label == "default":
            prefs_roundtrip["off"] = console("rn_currents off")
            prefs_roundtrip["status_off"] = console("rn_currents")
            prefs_roundtrip["on"] = console("rn_currents on")
            prefs_roundtrip["status_on"] = console("rn_currents")
        live.b1.write_new(live.WORK / f"{label}-data.json", dict(
            settings_before=settings_before,
            current=current,
            route=route,
            probe=probe,
            prefs_roundtrip=prefs_roundtrip,
            game_log_excerpt=[
                line for line in live.b1.LOG.read_text(encoding="utf-8-sig", errors="replace").splitlines()
                if "[RadioNav][CURRENT]" in line
            ],
        ))

    def num(line, key):
        m = re.search(rf"{key}=(\d+)", line)
        return int(m.group(1)) if m else 0

    def report():
        off = live.b1.read_json(live.WORK / "off-data.json")
        dflt = live.b1.read_json(live.WORK / "default-data.json")

        off_log = "\n".join(off["game_log_excerpt"])
        off_cur = "\n".join(off["current"].get("lines", []))
        off_set = "\n".join(off["settings_before"].get("lines", []))
        off_ok = (
            "disabled (RADIONAV_CURRENT_FIELD unset)" in off_log
            and "water drag transpilers installed" not in off_log
            and "kind=none" in off_cur
            and "waterSteps=0" in off_cur
            and "airSteps=0" in off_cur
            and "master=False" in off_set
        )

        d_log = "\n".join(dflt["game_log_excerpt"])
        d_cur = "\n".join(dflt["current"].get("lines", []))
        d_set = "\n".join(dflt["settings_before"].get("lines", []))
        d_route = "\n".join(dflt["route"].get("lines", []))
        probe_val = dflt["probe"].get("value", "")

        default_ok = (
            "tiles=6" in d_cur
            and "kind=atlas" in d_cur
            and "boundPlayer=True" in d_cur
            and "relativeRudder=True" in d_cur
            and "nearbyFlag=True" in d_cur
            and num(d_cur, "waterSteps") > 100
            and num(d_cur, "airSteps") > 100
            and num(d_cur, "applied") > 50
            and "current-aware steering hook installed" in d_log
            and "installed; one offset per native group interval" in d_log
            and "[RadioNav][CURRENT][ROUTE]" in d_route
            and "master=True" in d_set
            and 'atlas="bundled"' in d_set
            and "map=" in probe_val
            and ":on:" in probe_val
            and "master=False" in "\n".join(dflt["prefs_roundtrip"]["status_off"].get("lines", []))
            and "master=True" in "\n".join(dflt["prefs_roundtrip"]["status_on"].get("lines", []))
        )

        passed = off_ok and default_ok
        summary = dict(
            status="PASS" if passed else "FAIL",
            off_ok=off_ok,
            default_ok=default_ok,
            off_water_steps=num(off_cur, "waterSteps"),
            default_water_steps=num(d_cur, "waterSteps"),
            default_air_steps=num(d_cur, "airSteps"),
            default_far_applied=num(d_cur, "applied"),
            default_tiles="tiles=6" in d_cur,
            prefs_roundtrip_ok=(
                "master=False" in "\n".join(dflt["prefs_roundtrip"]["status_off"].get("lines", []))
                and "master=True" in "\n".join(dflt["prefs_roundtrip"]["status_on"].get("lines", []))
            ),
        )
        live.b1.write_new(live.WORK / "comparison.json", summary)
        return 0 if passed else 1

    os.environ.pop("RADIONAV_ROUTE_CURRENT", None)
    c1 = FIXTURE_PATH.read_bytes()
    with TARGET.open("xb") as s:
        s.write(c1)
    try:
        series = [
            ("off", "off", dict(collector=collect)),
            ("default", "default", dict(collector=collect)),
        ]
        return live.isolated(series, report)
    finally:
        if TARGET.is_file() and TARGET.read_bytes() == c1:
            TARGET.unlink()


if __name__ == "__main__":
    raise SystemExit(main())
