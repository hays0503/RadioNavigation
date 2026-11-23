"""Cross-check the map wind row against bridge numbers, with screenshots."""
import argparse
import json
import math
import os
import shutil
import sys
import time
from pathlib import Path
from urllib.parse import urlencode
from urllib.request import Request

ROOT = Path(__file__).resolve().parents[2]
live = None
request = state = console = None
FIXTURE = "UBOAT.Mods.RadioNavigation.WindLabelLiveFixture"
STAGE = ROOT / "artifacts/reports/current-wind-stage-20261122"
FIXTURE_PATH = Path(__file__).resolve().with_name("WindLabelLiveFixture.cs")
TARGET = ROOT / "Source/Logic" / FIXTURE_PATH.name
EN_CARDINALS = ("N", "NE", "E", "SE", "S", "SW", "W", "NW")
RU_CARDINALS = ("С", "СВ", "В", "ЮВ", "Ю", "ЮЗ", "З", "СЗ")


def fixture(method, *args):
    receipt = request("/reflect/call", type=FIXTURE, method=method,
                      args=json.dumps(args, separators=(",", ":")))
    if receipt.get("ok") is not True:
        raise RuntimeError(f"{method}: {receipt}")
    return receipt.get("value")


def parse(probe):
    data = {}
    for part in probe.split(";"):
        if "=" in part:
            key, _, value = part.partition("=")
            data.setdefault(key, []).append(value)
    return data


def shot(name):
    result = live.get("/screenshot?mode=frame", timeout=20)
    if result.get("ok"):
        shutil.copyfile(result["path"], live.WORK / name)
    return result


def scale(value):
    with live.HTTP.open(Request(live.BOOT + f"/time/scale?value={value}", method="POST"), timeout=10) as stream:
        reply = json.load(stream)
    if reply.get("ok") is not True or reply.get("scale") != value:
        raise RuntimeError(f"expected TC {value}: {reply}")


def load_save(name):
    url = live.BOOT + "/load?" + urlencode(dict(name=name))
    with live.HTTP.open(Request(url, method="POST"), timeout=180) as stream:
        reply = json.load(stream)
    if reply.get("ok") is not True or not reply.get("waited"):
        raise RuntimeError(f"load refused: {reply}")
    return reply


def snapshot():
    return {"probe": parse(fixture("Probe")), "panel": fixture("Panel")}


def still_frame(name):
    # The bridge reads a previous presented frame, not the transaction's
    # logical frame. Require a stable paused state on both sides and keep
    # pixel acceptance separate from the numerical overlay receipt.
    scale(0)
    try:
        time.sleep(1)
        before = snapshot()
        capture = shot(name)
        after = snapshot()
    finally:
        scale(1)
    return {"before": before, "capture": capture, "after": after,
            "stable": consistent(before, capture, after)}


def consistent(before, capture, after):
    a, b = before["probe"], after["probe"]
    names = ("ticks", "tc", "depth", "ground", "heading", "wind", "knots", "map", "mapNative")
    if any(key not in a or a[key] != b.get(key) for key in names) or a["tc"] != ["0"]:
        return False
    if not capture.get("ok") or capture.get("blackFrame") is not False or not capture.get("includesOverlay"):
        return False
    return (int(a["frame"][0]) <= capture["frame"] <= int(b["frame"][0])
            and before["panel"] == after["panel"])


def wait_depth(predicate, message, timeout_s=300):
    deadline = time.monotonic() + timeout_s
    while time.monotonic() < deadline:
        if predicate(float(parse(fixture("Probe"))["depth"][0])):
            return
        time.sleep(2)
    raise RuntimeError(message)


def collect(label):
    console("Map")
    time.sleep(4)
    surface_frame = still_frame("surface.png")
    live.b1.write_new(live.WORK / "surface-frame.json", surface_frame)
    weathers = fixture("Weathers")
    surface_state = state()

    live.progress(f"{label}: calm check")
    fixture("SetCalm", True)
    try:
        time.sleep(2)
        calm_frame = still_frame("calm.png")
    finally:
        fixture("SetCalm", False)
    time.sleep(2)

    live.progress(f"{label}: map closed -> open check")
    closed_mode = fixture("SetMapOpen", False)
    time.sleep(2.5)
    closed_probe = parse(fixture("Probe"))
    closed_panel = fixture("Panel")
    reopen_mode = fixture("SetMapOpen", True)
    time.sleep(2.5)
    reopened_frame = still_frame("reopened.png")

    live.progress(f"{label}: units & locale matrix")
    try:
        fixture("SetUnits", True)
        time.sleep(1.5)
        metric_ru_frame = still_frame("metric-ru.png")

        fixture("SetLanguageInMemory", "english", "knots", "km/h")
        time.sleep(1.5)
        metric_en_frame = still_frame("metric-en.png")

        fixture("SetUnits", False)
        time.sleep(1.5)
        imperial_en_frame = still_frame("imperial-en.png")
    finally:
        fixture("RestoreLocaleAndUnits")
    time.sleep(1.5)

    live.progress(f"{label}: second UI scale check")
    orig_scale = float(surface_frame["before"]["probe"].get("uiScale", ["1"])[0])
    alt_scale = 0.85 if orig_scale >= 1.15 else 1.25
    try:
        fixture("SetUiScaleInMemory", alt_scale)
        time.sleep(1.5)
        second_scale_frame = still_frame("second-scale.png")
    finally:
        fixture("RestoreUiScale")
    time.sleep(1.5)

    live.progress(f"{label}: alt resolution (1280x720) check")
    try:
        fixture("SetResolutionInMemory", 1280, 720)
        time.sleep(1.5)
        alt_res_frame = still_frame("alt-resolution.png")
    finally:
        fixture("RestoreResolution")
    time.sleep(1.5)

    live.progress(f"{label}: natural storm weather check")
    console("Weather Storm")
    time.sleep(2.5)
    storm_frame = still_frame("storm.png")
    console("Weather Reset")
    time.sleep(1.5)

    live.progress(f"{label}: requesting dive")
    if fixture("SetDepth", 12.0) is not True:
        raise RuntimeError("dive order refused")
    wait_depth(lambda d: d > 3.0, "dive did not reach 3 m", timeout_s=240)
    time.sleep(4)
    dived_frame = still_frame("dived.png")
    dived_state = state()

    live.progress(f"{label}: requesting resurface")
    if fixture("SetDepth", -4.0) is not True:
        raise RuntimeError("surface order refused")
    wait_depth(lambda d: d <= 0.5, "did not resurface", timeout_s=300)
    time.sleep(2)
    resurfaced_frame = still_frame("resurfaced.png")

    live.progress(f"{label}: save/load transition check")
    scale(1)
    load_reply = load_save(live.b1.COPY_NAME[:-5])
    console("Map")
    time.sleep(3)
    reloaded_frame = still_frame("reloaded.png")

    live.b1.write_new(live.WORK / "probe.json", dict(
        surface=surface_frame["before"]["probe"],
        panel=surface_frame["before"]["panel"],
        weathers=weathers,
        surface_state=surface_state,
        surface_frame=surface_frame,
        calm_frame=calm_frame,
        closed_mode=closed_mode,
        reopen_mode=reopen_mode,
        closed_probe=closed_probe,
        closed_panel=closed_panel,
        reopened_frame=reopened_frame,
        metric_ru_frame=metric_ru_frame,
        metric_en_frame=metric_en_frame,
        imperial_en_frame=imperial_en_frame,
        second_scale_frame=second_scale_frame,
        alt_res_frame=alt_res_frame,
        storm_frame=storm_frame,
        dived=dived_frame["before"]["probe"],
        dived_panel=dived_frame["before"]["panel"],
        dived_state=dived_state,
        dived_frame=dived_frame,
        resurfaced_frame=resurfaced_frame,
        reloaded_frame=reloaded_frame,
        load_reply=load_reply))


def bearings(east, north):
    if math.hypot(east, north) < 0.05:
        return None
    return math.degrees(math.atan2(east, north)) % 360


def expected_cardinal(bearing, russian):
    index = ((bearing + 22.5) // 45) % 8
    return (RU_CARDINALS if russian else EN_CARDINALS)[int(index)]


def numbers(data):
    east, north = map(float, data["wind"][0].split(","))
    ground_east, ground_north = map(float, data["ground"][0].split(","))
    return {
        "depthM": float(data["depth"][0]),
        "seaDepthAtBoatRasterM": float(data["seaDepthAtBoatRasterM"][0]),
        "headingDeg": float(data["heading"][0]),
        "averageShipSpeedMps": float(data["velocity"][0]),
        "groundSpeedMps": math.hypot(ground_east, ground_north),
        "trackDeg": bearings(ground_east, ground_north),
        "windMps": math.hypot(east, north),
        "windKnots": float(data["knots"][0]),
        "windFromDeg": bearings(-east, -north),
        "windToDeg": bearings(east, north),
        "nativeWeather": int(data["nativeWeather"][0]),
        "nativeKnots": float(data["nativeKnots"][0]),
        "units": data.get("units", ["?"])[0],
        "lang": data.get("lang", ["?"])[0],
        "uiScale": float(data["uiScale"][0]) if "uiScale" in data else None,
        "screen": data.get("screen", [None])[0],
        "mapScale": data.get("mapScale", []),
        "nativeMapTextAndCache": data["mapNative"],
        "mapParsed": data.get("mapParsed", []),
    }


def parse_bounds(text):
    if not text or text == "-" or "," not in text or ".." not in text:
        return None
    xs, ys = text.split(",", 1)
    x0, x1 = map(float, xs.split("..", 1))
    y0, y1 = map(float, ys.split("..", 1))
    return (x0, x1, y0, y1)


def parse_panel(panel):
    icon = container = None
    wind_nodes = []
    bearing_nodes = []
    for row in panel.splitlines():
        if row.startswith("group=Wind speed Icon@"):
            parts = row.split(":")
            if len(parts) >= 2:
                icon = parse_bounds(parts[1])
        elif row.startswith("container=Parameters:"):
            container = parse_bounds(row.split(":", 2)[1])
        elif row.startswith("node=Wind speed:") or row.startswith("node=RadioNavWindBearing:"):
            entry = {"raw": row}
            for part in row.split(":"):
                if part.startswith("node="):
                    entry["name"] = part[5:]
                elif part.startswith("active="):
                    entry["active"] = part[7:] == "True"
                elif part.startswith("text="):
                    entry["text"] = part[5:].split("@", 1)[0]
                elif part.startswith("bounds="):
                    entry["bounds"] = parse_bounds(part[7:])
                elif part.startswith("rect="):
                    entry["rect"] = parse_bounds(part[5:])
            (wind_nodes if entry.get("name") == "Wind speed" else bearing_nodes).append(entry)
    return dict(icon=icon, container=container, wind=wind_nodes, bearing=bearing_nodes)


def overlaps_x(a, b, tolerance=0.5):
    return a[0] < b[1] - tolerance and b[0] < a[1] - tolerance


def layout_ok(panel, expect_visible):
    parsed = parse_panel(panel)
    bearings_all = parsed["bearing"]
    if len(bearings_all) > 1:
        return False
    active_bearings = [node for node in bearings_all if node.get("active")]
    if not expect_visible:
        return len(active_bearings) == 0
    if len(active_bearings) != 1:
        return False
    bear = active_bearings[0].get("bounds")
    cont = parsed["container"]
    icon = parsed["icon"]
    active_wind = [node for node in parsed["wind"] if node.get("active")]
    if bear is None or cont is None or icon is None or len(active_wind) != 1:
        return False
    wind = active_wind[0].get("bounds")
    if wind is None:
        return False
    inside_container = cont[0] - 1.0 <= bear[0] and bear[1] <= cont[1] + 1.0
    return inside_container and not overlaps_x(bear, wind) and not overlaps_x(bear, icon)


def wind_value_matches(probe):
    units = probe.get("units", ["?"])[0]
    native_knots = float(probe["nativeKnots"][0])
    expected = round(native_knots * 1.85166) if units == "Metric" else round(native_knots)
    parsed_maps = probe.get("mapParsed", [])
    if not parsed_maps:
        return False
    for entry in parsed_maps:
        text = entry.split(":", 1)[1] if ":" in entry else entry
        parts = text.strip().split()
        if parts and parts[0].lstrip("-").isdigit() and int(parts[0]) == expected:
            return True
    return False


def overlay_matches(probe, panel):
    depth = float(probe["depth"][0])
    east, north = map(float, probe["wind"][0].split(","))
    bearings_all = [row for row in panel.splitlines() if "RadioNavWindBearing" in row]
    suffixes = [row for row in bearings_all if ":active=True:text=" in row]
    if len(bearings_all) > 1:
        return False
    if depth > 0.5 or math.hypot(east, north) < 0.3:
        return not suffixes
    if len(suffixes) != 1:
        return False
    shown = suffixes[0].split(":text=", 1)[1].split("°", 1)
    expected = math.floor(bearings(-east, -north) + 0.5) % 360
    if not (len(shown) == 2 and shown[0].isdigit() and int(shown[0]) == expected):
        return False
    if "lang" in probe:
        russian = probe["lang"][0].lower().startswith("ru")
        token = shown[1].split("@", 1)[0].strip()
        if token != expected_cardinal(expected, russian):
            return False
    return True


def check_frame(name, frame, expect_visible):
    before = frame["before"]
    probe, panel = before["probe"], before["panel"]
    return {
        "name": name,
        "stable": frame["stable"],
        "overlayOk": overlay_matches(probe, panel),
        "layoutOk": layout_ok(panel, expect_visible),
        "windValueOk": wind_value_matches(probe),
        "numbers": numbers(probe),
    }


def report():
    data = json.loads((live.WORK / "probe.json").read_text(encoding="utf-8-sig"))
    specs = [
        ("surface", data["surface_frame"], True),
        ("calm", data["calm_frame"], False),
        ("reopened", data["reopened_frame"], True),
        ("metric_ru", data["metric_ru_frame"], True),
        ("metric_en", data["metric_en_frame"], True),
        ("imperial_en", data["imperial_en_frame"], True),
        ("second_scale", data["second_scale_frame"], True),
        ("alt_resolution", data["alt_res_frame"], True),
        ("storm", data["storm_frame"], True),
        ("dived", data["dived_frame"], False),
        ("resurfaced", data["resurfaced_frame"], True),
        ("reloaded", data["reloaded_frame"], True),
    ]
    checks = [check_frame(name, frame, visible) for name, frame, visible in specs]
    closed_nodes = [row for row in data.get("closed_panel", "").splitlines()
                    if "RadioNavWindBearing" in row]
    closed_ok = len(closed_nodes) <= 1 and all(":active=False" in row for row in closed_nodes)
    surf_scale = checks[0]["numbers"].get("uiScale")
    alt_num = next((item["numbers"].get("uiScale") for item in checks if item["name"] == "second_scale"), None)
    second_scale_ok = (surf_scale is not None and alt_num is not None and abs(alt_num - surf_scale) >= 0.1)
    alt_res_screen = next((item["numbers"].get("screen") for item in checks if item["name"] == "alt_resolution"), None)
    alt_resolution_ok = alt_res_screen == "1280x720"
    storm_knots = next((item["numbers"].get("windKnots", 0.0) for item in checks if item["name"] == "storm"), 0.0)
    storm_ok = storm_knots >= 15.0
    ok = (closed_ok and second_scale_ok and alt_resolution_ok and storm_ok
          and float(data["surface"]["depth"][0]) <= 0.5
          and float(data["dived"]["depth"][0]) > 0.5
          and all(item["stable"] and item["overlayOk"] and item["layoutOk"] and item["windValueOk"]
                  for item in checks))
    live.b1.write_new(live.WORK / "comparison.json", dict(
        telemetry="PASS" if ok else "FAIL",
        closed_ok=closed_ok,
        second_scale_ok=second_scale_ok,
        alt_resolution_ok=alt_resolution_ok,
        storm_ok=storm_ok,
        closed_mode=data.get("closed_mode"),
        reopen_mode=data.get("reopen_mode"),
        closed_nodes=closed_nodes,
        checks=checks,
        weathers=data["weathers"],
        load_reply=data["load_reply"],
        visual="NOT RUN",
        visual_note="inspect each composited PNG before a visual verdict",
        not_run=[]))
    return 0 if ok else 1


def main():
    global live, request, state, console
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("work", type=Path)
    parser.add_argument("--stage", type=Path, default=STAGE)
    parser.add_argument("--confirm-cloud-off", action="store_true")
    args = parser.parse_args()
    if not args.confirm_cloud_off:
        parser.error("Steam Cloud must be off")
    if os.environ.get("RADIONAV_ROUTE_CURRENT") == "1":
        parser.error("unset RADIONAV_ROUTE_CURRENT to keep the test stock")
    os.environ["RADIONAV_ROUTE_CURRENT"] = "0"
    sys.path.insert(0, str(ROOT / "artifacts/reports/current-b1-control-20261121"))
    import run_live_series as live
    from run_depth_receipt import request, state
    from run_nearby_receipt import console
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
    content = FIXTURE_PATH.read_bytes()
    with TARGET.open("xb") as stream:
        stream.write(content)
    try:
        return live.isolated([("probe", "off", dict(collector=collect))], report)
    except (OSError, RuntimeError, ValueError) as error:
        live.b1.write_new(live.WORK / "incomplete.json", {"status": "NOT RUN", "reason": str(error)})
        raise
    finally:
        if TARGET.is_file() and TARGET.read_bytes() == content:
            TARGET.unlink()


if __name__ == "__main__":
    raise SystemExit(main())
