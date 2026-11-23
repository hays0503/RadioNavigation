"""P0/P1: observe one stock Liberty crossing in three OFF runs before allowing ON.

Use the same already-staged campaign for both phases. The player or a declared
test setup brings the Liberty through native spawn and unspawn range; this
runner never moves the group or calls spawn/unspawn. No save/load claim yet.
"""
import argparse
from contextlib import contextmanager
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import sys
import time
from urllib.parse import urlencode
from urllib.request import Request

ROOT = Path(__file__).resolve().parents[2]
FIXTURE = "UBOAT.Mods.RadioNavigation.CurrentHandoffLiveFixture"
REPORTS = ROOT / "artifacts/reports"
BASELINE_COUNT = 3
WATER_MPS = (0.6, 0.0)
DIAGNOSTIC_NORTH_KM = (30.0, 12.0, 10.0, 7.0, 4.0, 2.0, 45.0)
POLICY = {
    "positionFloorM": 1.0,
    "velocityFloorMps": 0.05,
    "farStepFloorM": 1.0,
    "maxHandoffGameSeconds": 10.0,
    "targetStartMaxKm": 1.0,
    "armMaxGameSeconds": 20.0,
    "minSignalRatio": 2.0,
    "controlMargin": 2.0,
    "maxFarStepGameSeconds": 5.0,
}
FIXTURE_PATH = Path(__file__).resolve().with_name("CurrentHandoffLiveFixture.cs")
STAGED_FIXTURE = ROOT / "Source/Logic" / FIXTURE_PATH.name


def vector(row, east, north, multiplier=1.0):
    values = (row[east], row[north])
    if not all(isinstance(value, (int, float)) and math.isfinite(value) for value in values):
        raise ValueError(f"{row['phase']}: {east}/{north} not finite")
    return tuple(value * multiplier for value in values)


def position(row):
    return vector(row, "eastKm", "northKm", 1000.0)


def velocity(row):
    return vector(row, "eastMps", "northMps")


def jump(before, after, elapsed, outgoing=None):
    p0, p1 = position(before), position(after)
    v0, v1 = velocity(before) if outgoing is None else outgoing, velocity(after)
    return {
        "positionM": [b - a - v * elapsed for a, b, v in zip(p0, p1, v0)],
        "velocityMps": [b - a for a, b in zip(v0, v1)],
        "gameSeconds": elapsed,
    }


def seconds(before, after, clock, maximum, minimum=0):
    value = (after[clock] - before[clock]) / 10_000_000
    if not minimum <= value <= maximum:
        raise ValueError(f"{before['phase']} → {after['phase']}: bad {clock} interval {value}")
    return value


def metrics(trace, config, feature):
    if trace.get("error") or not trace.get("complete"):
        raise ValueError(trace.get("error") or "incomplete native lifecycle")
    if not trace["enteredNativeArea"] or not trace["leftNativeArea"]:
        raise ValueError("player's native encounter area did not trigger both directions")
    rows = trace["rows"]
    prefix = (["far.before", "scene.native"] + ["scene.fixed"] * 4
              + ["scene.before", "far.release", "far.initial"])
    step_count = len(rows) - len(prefix)
    if (step_count < 2 or step_count % 2 != 0
            or [row["phase"] for row in rows[:len(prefix)]] != prefix
            or [row["phase"] for row in rows[len(prefix):]] != ["far.step.before", "far.step.after"] * (step_count // 2)):
        raise ValueError("unexpected native handoff sequence")
    if any(b["storyTicks"] < a["storyTicks"] for a, b in zip(rows, rows[1:])):
        raise ValueError("nonmonotonic game clock")
    if any(row["tc"] != 1 for row in rows):
        raise ValueError("sample not at TC 1")
    if trace["target"]["type"] != "Liberty":
        raise ValueError("not a Liberty")
    before, spawn = rows[:2]
    fixed_rows = rows[2:6]
    exit_scene, release, far = rows[6:9]
    steps = rows[9:]
    if [row["fixedTick"] for row in fixed_rows] != list(range(spawn["fixedTick"] + 1, spawn["fixedTick"] + 5)):
        raise ValueError("missing first consecutive fixed observations")
    if before["state"] not in ("NotSpawned", "SpawningImpostors", "SpawnedImpostors"):
        raise ValueError("first sample was not far")
    if any(row["state"] != "Spawned" or not row["hasSceneBody"] or row["isKinematic"]
           for row in [spawn, *fixed_rows, exit_scene]):
        raise ValueError("missing physical scene body")
    if far["state"] != "NotSpawned" or far["hasSceneBody"] or release["hasSceneBody"]:
        raise ValueError("far handoff did not release the scene body")
    if feature and any(not row["covered"] or math.dist(vector(row, "eastWaterMps",
                       "northWaterMps"), WATER_MPS) > 1e-6 for row in rows):
        raise ValueError("ON did not receive its configured constant field")
    if not feature and any(row["covered"] for row in rows):
        raise ValueError("invalid stock control: field is active")

    def boundary(a, b, outgoing=None):
        return jump(a, b, seconds(a, b, "storyTicks", config["maxHandoffGameSeconds"]), outgoing)

    offsets = []
    for a, b in zip(steps[::2], steps[1::2]):
        if a["state"] != "NotSpawned" or b["state"] != "NotSpawned" or a["groupTicks"] <= 0:
            raise ValueError("far body changed ownership inside a counted interval")
        if a["groupTicks"] > b["storyTicks"] or b["groupTicks"] != b["storyTicks"]:
            raise ValueError("far interval was not measured at its native publication epoch")
        elapsed = seconds(a, b, "groupTicks", config["maxFarStepGameSeconds"], 1e-7)
        stock = vector(a, "stockEastMps", "stockNorthMps")
        # Same route leg throughout the measured interval; do not explain away
        # a waypoint snap as missing/double water.
        if a["routeCount"] == 0 or a["routeCount"] != b["routeCount"]:
            raise ValueError("far interval crossed a route event")
        p0 = vector(a, "groupEastKm", "groupNorthKm", 1000.0)
        p1 = vector(b, "groupEastKm", "groupNorthKm", 1000.0)
        offsets.append(([y - x - v * elapsed for x, y, v in zip(p0, p1, stock)], elapsed))
    # Far Position is advanced by the route, not by Unity's 2D predictor.
    # Stock leaves the latter at zero in the SpawnedImpostors state.
    outgoing = vector(before, "stockEastMps", "stockNorthMps")
    if feature:
        outgoing = tuple(a + b for a, b in zip(outgoing, WATER_MPS))
    return {
        "farToScene": boundary(before, spawn, outgoing),
        "farToSceneRawBody": boundary(before, spawn),
        "sceneToFar": boundary(exit_scene, far),
        "farFirstUpdate": boundary(exit_scene, steps[1]),
        "firstFixed": [boundary(spawn, row) for row in fixed_rows],
        "preSpawnBodyResidualMps": far_body_residual(before, feature),
        "postReleaseBodyResidualMps": far_body_residual(steps[1], feature),
        "farOffsetM": [sum(row[0][axis] for row in offsets) for axis in range(2)],
        "farGameSeconds": sum(row[1] for row in offsets),
    }


def far_body_residual(row, feature):
    if row["routeCount"] <= 0:
        raise ValueError("no route at far-body control point")
    stock = vector(row, "stockEastMps", "stockNorthMps")
    predicted = [v + w for v, w in zip(stock, WATER_MPS if feature else (0, 0))]
    return math.dist(vector(row, "farBodyEastMps", "farBodyNorthMps"), predicted)


def common_target(traces, config):
    target = traces[0]["target"]
    for trace in traces[1:]:
        other = trace["target"]
        if any(target[key] != other[key] for key in ("name", "type", "prefab")):
            raise ValueError("different vessel across campaign copies")
        for east, north in (("east", "north"), ("playerEast", "playerNorth")):
            if math.dist((target[east], target[north]), (other[east], other[north])) > config["targetStartMaxKm"]:
                raise ValueError("target or player armed at incomparable positions")
        epoch_delta = abs(trace["armedStoryTicks"] - traces[0]["armedStoryTicks"]) / 10_000_000
        if epoch_delta > config["armMaxGameSeconds"]:
            raise ValueError("target armed at incomparable game epochs")
    return target


def freeze(traces, config):
    if len(traces) != BASELINE_COUNT:
        raise ValueError("three completed OFF controls required")
    target = common_target(traces, config)
    rows = [metrics(trace, config, False) for trace in traces]
    limits = {}
    for phase in ("farToScene", "sceneToFar", "farFirstUpdate"):
        limits[phase] = {}
        for key, floor in (("positionM", config["positionFloorM"]),
                           ("velocityMps", config["velocityFloorMps"])):
            values = [row[phase][key] for row in rows]
            center = [sum(axis) / len(values) for axis in zip(*values)]
            spread = max(math.dist(row, center) for row in values)
            limits[phase][key] = {"center": center, "spread": spread,
                                  "limit": max(floor, config["controlMargin"] * spread)}
    far_limit = max(config["farStepFloorM"], config["controlMargin"] *
                    max(math.hypot(*row["farOffsetM"]) for row in rows))
    return {"target": target, "armedStoryTicks": traces[0]["armedStoryTicks"],
            "limits": limits, "farLimitM": far_limit, "controls": rows}


def evaluate(trace, frozen, config):
    common_target([frozen, trace], config)
    row = metrics(trace, config, True)
    outcomes = {}
    for phase, checks in frozen["limits"].items():
        outcomes[phase] = {}
        for key, rule in checks.items():
            error = math.dist(row[phase][key], rule["center"])
            resolved = key != "velocityMps" or math.hypot(*WATER_MPS) >= (
                config["minSignalRatio"] * rule["limit"])
            status = ("NOT RUN" if not resolved else "PASS" if error <= rule["limit"] else "FAIL")
            outcomes[phase][key] = {"status": status, "residual": error, "limit": rule["limit"]}
    predicted = [v * row["farGameSeconds"] for v in WATER_MPS]
    error = math.dist(row["farOffsetM"], predicted)
    status = ("NOT RUN" if math.hypot(*predicted) < config["minSignalRatio"] * frozen["farLimitM"]
              else "PASS" if error <= frozen["farLimitM"] else "FAIL")
    outcomes["farIntervals"] = {"status": status, "predictedM": predicted, "residualM": error,
                                "limitM": frozen["farLimitM"]}
    body_limit = config["velocityFloorMps"]
    for key in ("preSpawnBodyResidualMps", "postReleaseBodyResidualMps"):
        error = row[key]
        outcomes[key] = {"status": "PASS" if error <= body_limit else "FAIL",
                         "residual": error, "limit": body_limit}
    # Keep the native-boundary verdict separate from unexecuted experiments.
    states = [test["status"] for phase in frozen["limits"] for test in outcomes[phase].values()]
    states += [outcomes[key]["status"] for key in ("farIntervals", "preSpawnBodyResidualMps",
                                                  "postReleaseBodyResidualMps")]
    return {"status": "FAIL" if "FAIL" in states else "NOT RUN" if "NOT RUN" in states else "PASS",
            "outcomes": outcomes, "metrics": row, "not_run": ["saveLoadAtBoundary"]}


def fixture(live, method, *args):
    query = urlencode(dict(type=FIXTURE, method=method, args=json.dumps(args, separators=(",", ":"))))
    request = Request(live.BOOT + "/reflect/call?" + query, method="POST")
    with live.HTTP.open(request, timeout=20) as response:
        receipt = json.load(response)
    if receipt.get("ok") is not True:
        raise RuntimeError(f"{method}: {receipt}")
    return receipt["value"]


def post(live, path, **query):
    url = live.BOOT + path + ("?" + urlencode(query) if query else "")
    with live.HTTP.open(Request(url, method="POST"), timeout=180) as response:
        data = json.load(response)
    if data.get("ok") is not True:
        raise RuntimeError(f"{path} refused: {data}")
    return data


def remove_temp_saves(names):
    for name in names:
        p = ROOT.parents[1] / "Saves" / f"{name}.save"
        if p.exists():
            p.unlink()


def wait_save_written(name, timeout=15):
    p = ROOT.parents[1] / "Saves" / f"{name}.save"
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if p.exists() and p.stat().st_size > 0:
            time.sleep(1.0)
            return
        time.sleep(0.2)
    time.sleep(1.5)


def probe(live, method, *args):
    raw = fixture(live, method, *args)
    if not raw.startswith("{"):
        raise RuntimeError(f"{method} failed: {raw}")
    return json.loads(raw)


def run_pre_entry_save_load(live, args):
    created = []
    try:
        candidates = json.loads(fixture(live, "Discover")).get("values")
        target = choose_target(candidates, args.vessel, args.target)
        setup_continuous_chord(live, "pre_entry", target, args)
        refreshed = choose_target(json.loads(fixture(live, "Discover")).get("values"), args.vessel,
                                  (target["east"], target["north"]))
        save_name = "RadioNav_Handoff_PreEntry"
        created.append(save_name)
        post(live, "/save", name=save_name)
        wait_save_written(save_name)

        # Branch 1: continuous entry from pre-save point (capture boundary + 3s in-scene)
        if fixture(live, "Arm", refreshed["id"]) != "armed":
            raise RuntimeError("pre-entry arm failed")
        cont_spawn = None
        cont_probe = None
        try:
            deadline = time.monotonic() + 240
            while time.monotonic() < deadline:
                trace = json.loads(fixture(live, "Read"))
                fixed = [r for r in trace["rows"] if r["phase"] == "scene.fixed"]
                if len(fixed) >= 4:
                    cont_spawn = fixed[0]
                    time.sleep(3.0)
                    cont_probe = probe(live, "Probe", refreshed["id"])
                    break
                time.sleep(0.25)
        finally:
            fixture(live, "Stop")
        if cont_probe is None:
            raise RuntimeError("pre-entry continuous branch did not reach scene")

        # Branch 2: real disk load of saved pre-entry checkpoint, then re-cross boundary
        post(live, "/load", name=save_name)
        time.sleep(1.5)
        reloaded_target = choose_target(json.loads(fixture(live, "Discover")).get("values"),
                                        args.vessel, (refreshed["east"], refreshed["north"]))
        if fixture(live, "Arm", reloaded_target["id"]) != "armed":
            raise RuntimeError("reloaded pre-entry arm failed")
        reload_spawn = None
        reload_probe = None
        try:
            deadline = time.monotonic() + 240
            while time.monotonic() < deadline:
                if reload_spawn is None:
                    rtrace = json.loads(fixture(live, "Read"))
                    rfixed = [r for r in rtrace["rows"] if r["phase"] == "scene.fixed"]
                    if rfixed:
                        reload_spawn = rfixed[0]
                cur = probe(live, "Probe", reloaded_target["id"])
                if cur["state"] == "Spawned" and (cur["storyTicks"] - reload_spawn["storyTicks"]) >= (cont_probe["storyTicks"] - cont_spawn["storyTicks"]):
                    reload_probe = cur
                    break
                time.sleep(0.05)
        finally:
            fixture(live, "Stop")
        if reload_probe is None or reload_spawn is None:
            raise RuntimeError("reloaded pre-entry branch did not reach continuous epoch")

        spawn_pos_m = math.dist((cont_spawn["eastKm"] * 1000.0, cont_spawn["northKm"] * 1000.0),
                                (reload_spawn["eastKm"] * 1000.0, reload_spawn["northKm"] * 1000.0))
        spawn_vel_mps = math.dist((cont_spawn["eastMps"], cont_spawn["northMps"]),
                                  (reload_spawn["eastMps"], reload_spawn["northMps"]))
        epoch_diff_s = abs(reload_probe["storyTicks"] - cont_probe["storyTicks"]) / 10_000_000.0
        post_spawn_pos_m = math.dist((cont_probe["eastKm"] * 1000.0 - cont_spawn["eastKm"] * 1000.0,
                                      cont_probe["northKm"] * 1000.0 - cont_spawn["northKm"] * 1000.0),
                                     (reload_probe["eastKm"] * 1000.0 - reload_spawn["eastKm"] * 1000.0,
                                      reload_probe["northKm"] * 1000.0 - reload_spawn["northKm"] * 1000.0))
        vel_diff_mps = math.dist((cont_probe["eastMps"], cont_probe["northMps"]),
                                 (reload_probe["eastMps"], reload_probe["northMps"]))
        passed = (reload_probe["state"] == "Spawned"
                  and spawn_vel_mps <= 0.05 and vel_diff_mps <= 0.05
                  and spawn_pos_m <= 1.0 and post_spawn_pos_m <= 1.0)
        live.b1.write_new(live.WORK / "pre-entry-save-load.json", dict(
            status="PASS" if passed else "FAIL",
            continuous=cont_probe,
            reloaded=reload_probe,
            continuousSpawn=cont_spawn,
            reloadedSpawn=reload_spawn,
            spawnPosDiffM=spawn_pos_m,
            spawnVelDiffMps=spawn_vel_mps,
            postSpawnDisplacementDiffM=post_spawn_pos_m,
            velDiffMps=vel_diff_mps,
            epochDiffS=epoch_diff_s,
        ))
    finally:
        remove_temp_saves(created)


def run_post_exit_save_load(live, trace):
    created = []
    try:
        tid = trace["target"]["id"]
        saved = probe(live, "Probe", tid)
        save_name = "RadioNav_Handoff_PostExit"
        created.append(save_name)
        post(live, "/save", name=save_name)
        wait_save_written(save_name)
        cont = probe(live, "Probe", tid)
        post(live, "/load", name=save_name)
        time.sleep(1.5)
        reloaded = probe(live, "ProbeNear", saved["eastKm"], saved["northKm"])
        pos_diff_m = math.dist((saved["eastKm"] * 1000.0, saved["northKm"] * 1000.0),
                               (reloaded["eastKm"] * 1000.0, reloaded["northKm"] * 1000.0))
        vel_diff_mps = math.dist((saved["eastMps"], saved["northMps"]),
                                 (reloaded["eastMps"], reloaded["northMps"]))
        # While unpaused at TC 1, storyTicks and groupTicks advance together; verify the
        # per-frame offset stays invariant (no retroactive/replayed pre-save interval).
        saved_lag = saved["storyTicks"] - saved["groupTicks"]
        reloaded_lag = reloaded["storyTicks"] - reloaded["groupTicks"]
        lag_preserved = abs(reloaded_lag - saved_lag) <= 200_000
        passed = (reloaded["state"] in ("NotSpawned", "SpawnedImpostors")
                  and lag_preserved
                  and pos_diff_m <= 1.0 and vel_diff_mps <= 0.05)
        live.b1.write_new(live.WORK / "post-exit-save-load.json", dict(
            status="PASS" if passed else "FAIL",
            saved=saved,
            continuous=cont,
            reloaded=reloaded,
            posDiffM=pos_diff_m,
            velDiffMps=vel_diff_mps,
            savedLagTicks=saved_lag,
            reloadedLagTicks=reloaded_lag,
            lagPreserved=lag_preserved,
        ))
    finally:
        remove_temp_saves(created)


def collector(live, args):
    def collect(label):
        raw = fixture(live, "Discover")
        if not raw.startswith("{"):
            raise ValueError(f"candidate discovery unavailable: {raw}")
        candidates = json.loads(raw).get("values")
        if not isinstance(candidates, list):
            raise ValueError(f"malformed candidate discovery: {raw[:200]}")
        live.b1.write_new(live.WORK / f"{label}-candidates.json", candidates)
        if args.phase == "discover":
            return
        target = choose_target(candidates, args.vessel, args.target)
        if args.player_reposition:
            place(live, label, target, DIAGNOSTIC_NORTH_KM[0], "setup")
        elif args.chord:
            setup_continuous_chord(live, label, target, args)
        answer = fixture(live, "Arm", target["id"])
        if answer != "armed":
            raise RuntimeError(answer)
        deadline = time.monotonic() + args.timeout
        heartbeat = 0
        live.progress(f"{label}: armed {target['name']}; awaiting native approach and departure at TC 1")
        offsets = iter(DIAGNOSTIC_NORTH_KM[1:-1])
        next_move = time.monotonic() + 3
        departure_requested = False
        try:
            while True:
                now = time.monotonic()
                trace = json.loads(fixture(live, "Read"))
                clock = live.get("/time")
                if not trace["complete"] and (clock.get("scale") != 1 or now >= deadline):
                    trace["error"] = (f"expected TC 1, saw {clock.get('scale')}"
                                      if clock.get("scale") != 1 else "native crossing timed out")
                if trace.get("error") or trace["complete"]:
                    live.b1.write_new(live.WORK / f"{label}.json", trace)
                    if trace.get("error"):
                        raise RuntimeError(trace["error"])
                    if args.phase == "on" and args.save_load_post:
                        run_post_exit_save_load(live, trace)
                    return
                if args.player_reposition and now >= next_move:
                    phases = [row["phase"] for row in trace["rows"]]
                    if not departure_requested and phases.count("scene.fixed") == 4:
                        place(live, label, target, DIAGNOSTIC_NORTH_KM[-1], "departure")
                        departure_requested = True
                    elif "far.before" not in phases:
                        distance = next(offsets, None)
                        if distance is None:
                            raise RuntimeError("no native spawn on the declared diagnostic approach")
                        place(live, label, target, distance, "approach")
                    next_move = now + 12
                if now >= heartbeat:
                    phase = trace["rows"][-1]["phase"] if trace["rows"] else "awaiting spawn"
                    live.progress(f"{label}: {phase}; {len(trace['rows'])}/11 samples; budget {deadline-now:.0f}s left")
                    heartbeat = now + 20
                time.sleep(2)
        finally:
            fixture(live, "Stop")
    return collect


def choose_target(candidates, name, point):
    matches = [candidate for candidate in candidates if candidate["name"] == name]
    if point is not None:
        matches = [candidate for candidate in matches
                   if math.dist((candidate["east"], candidate["north"]), point) <= 1]
    if len(matches) != 1:
        raise ValueError(f"expected one unspawned {name!r} near {point}, found {len(matches)}")
    return matches[0]


def setup_continuous_chord(live, label, target, args):
    # Single pre-arm setup outside areaRadiusKm on a grazing chord opposite Liberty's
    # course so TC 1 sailing enters and leaves Liberty's native encounter zone with no mid-run teleports.
    heading_deg = 241.4
    hr = math.radians(heading_deg)
    vp = (6.84e-3 * math.sin(hr), 6.84e-3 * math.cos(hr))
    v_lib = (3.1244e-3, 1.7018e-3)
    vr = (vp[0] - v_lib[0], vp[1] - v_lib[1])
    mag = math.hypot(*vr)
    u = (vr[0] / mag, vr[1] / mag)
    n = (-u[1], u[0])
    r_in = target.get("effectiveAreaRadiusKm", target["areaRadiusKm"])
    b = r_in - 0.060
    s_enter = -math.sqrt(r_in * r_in - b * b)

    # Stabilize heading and speed far outside areaRadiusKm before final pre-arm placement.
    warm_east = target["east"] + b * n[0] + (s_enter - 4.0) * u[0]
    warm_north = target["north"] + b * n[1] + (s_enter - 4.0) * u[1]
    warm_reply = fixture(live, "PlacePlayer", target["id"], warm_east, warm_north)
    if not warm_reply.startswith("{"):
        raise RuntimeError(warm_reply)
    with live.HTTP.open(Request(live.BOOT + "/reflect/instances?" + urlencode(
            dict(type="UBOAT.Game.Scene.Entities.PlayerShip"))), timeout=10) as stream:
        ship_id = json.load(stream)["instances"][0]["id"]
    with live.HTTP.open(Request(live.BOOT + f"/ship/heading?deg={heading_deg}", method="POST"), timeout=10) as stream:
        if not json.load(stream).get("ok"):
            raise RuntimeError("ship heading order refused")
    with live.HTTP.open(Request(live.BOOT + "/ship/rudder?value=0", method="POST"), timeout=10) as stream:
        json.load(stream)
    query = urlencode(dict(type="UBOAT.Game.Scene.Entities.PlayerShip",
                           instance_id=ship_id, method="OrderEngineGear", args="[5,false,1]"))
    with live.HTTP.open(Request(live.BOOT + "/reflect/call?" + query, method="POST"), timeout=10) as stream:
        gear_reply = json.load(stream)
    if gear_reply.get("ok") is not True:
        raise RuntimeError(f"engine gear refused: {gear_reply}")
    time.sleep(30)

    refreshed = choose_target(json.loads(fixture(live, "Discover")).get("values"), args.vessel,
                              (target["east"], target["north"]))
    s_start = s_enter - 0.220
    east = refreshed["east"] + b * n[0] + s_start * u[0]
    north = refreshed["north"] + b * n[1] + s_start * u[1]
    reply = fixture(live, "PlacePlayer", refreshed["id"], east, north)
    if not reply.startswith("{"):
        raise RuntimeError(reply)
    actual = json.loads(reply)
    if math.dist((actual["playerEast"], actual["playerNorth"]), (east, north)) > 0.15:
        raise RuntimeError("player did not reach the pre-arm chord start point")
    with (live.WORK / f"{label}-player-setup.json").open("x", encoding="utf-8") as f:
        json.dump(dict(east=east, north=north, headingDeg=heading_deg, gear=5, preArmWarmupS=30, actual=actual), f, indent=2)
    live.progress(f"{label}: pre-arm chord set at ({east:.2f}, {north:.2f}), dist={actual['distanceKm']:.3f} km, hdg={heading_deg}, gear=5")


def place(live, label, target, north_km, reason):
    east = target["east"]
    north = target["north"] + north_km
    reply = fixture(live, "PlacePlayer", target["id"], east, north)
    if not reply.startswith("{"):
        raise RuntimeError(reply)
    actual = json.loads(reply)
    if math.dist((actual["playerEast"], actual["playerNorth"]), (east, north)) > 0.15:
        raise RuntimeError("player did not reach the declared diagnostic start point")
    with (live.WORK / f"{label}-player-setup.jsonl").open("a", encoding="utf-8") as f:
        f.write(json.dumps(dict(reason=reason, east=east, north=north, actual=actual)) + "\n")
    live.progress(f"{label}: player-only diagnostic reposition {reason}: N +{north_km:.1f} km")


def code_fingerprint():
    paths = sorted((ROOT / "Source").rglob("*.cs"))
    paths += [Path(__file__).resolve(), FIXTURE_PATH]
    return {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths}


@contextmanager
def staged_fixture():
    content = FIXTURE_PATH.read_bytes()
    with STAGED_FIXTURE.open("xb") as destination:
        destination.write(content)
    try:
        yield
    finally:
        if STAGED_FIXTURE.is_file() and STAGED_FIXTURE.read_bytes() == content:
            STAGED_FIXTURE.unlink()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("phase", choices=("discover", "pilot", "off", "on", "diag", "diag_on", "diag_chord", "save_load"))
    parser.add_argument("work", type=Path)
    parser.add_argument("--stage", type=Path, required=True)
    parser.add_argument("--vessel")
    parser.add_argument("--target", type=float, nargs=2, metavar=("E_KM", "N_KM"))
    parser.add_argument("--player-reposition", action="store_true",
                        help="controlled diagnostic only; do not claim continuous sailing")
    parser.add_argument("--chord", action="store_true",
                        help="place player once before arming on a continuous TC 1 crossing chord")
    parser.add_argument("--save-load-post", action="store_true",
                        help="capture and verify post-unspawn save/load checkpoint after ON completes")
    parser.add_argument("--control", type=Path, help="frozen-control.json from OFF")
    parser.add_argument("--timeout", type=int, default=900)
    parser.add_argument("--confirm-cloud-off", action="store_true")
    args = parser.parse_args()
    if not args.confirm_cloud_off or (args.phase != "discover" and not args.vessel):
        parser.error("live run requires cloud-off confirmation and a named vessel (except discovery)")
    if args.phase == "on" and not args.control:
        parser.error("ON requires frozen OFF control limits")
    if args.player_reposition and args.target is None:
        parser.error("controlled diagnostic requires explicit encounter coordinates for the target")
    if args.timeout <= 0:
        parser.error("timeout must be positive")
    if STAGED_FIXTURE.exists():
        parser.error("temporary fixture already exists under Source; inspect it before proceeding")
    if os.environ.get("RADIONAV_ROUTE_CURRENT") == "1":
        parser.error("unset RADIONAV_ROUTE_CURRENT; the inherited runner does not clear it")
    os.environ["RADIONAV_ROUTE_CURRENT"] = "0"

    sys.path.insert(0, str(REPORTS / "current-b1-control-20261121"))
    import run_live_series as live
    live.WORK = args.work.resolve()
    if live.game_running() or live.bridge_listening():
        parser.error("close the existing game normally before launching the isolated run")
    manifest = live.b1.verified_stage(args.stage)
    live.b1.verify(args.stage)
    if Path(manifest["saves"]).resolve() != (ROOT.parents[1] / "Saves").resolve():
        parser.error("stage manifest must protect the actual UBOAT Saves directory")
    managed = live.GAME / "UBOAT_Data/Managed/com.uboat.game.dll"
    signature = {"code": code_fingerprint(), "save": manifest["save_sha256"],
                 "nativeGameDll": live.b1.file_hash(managed), "waterMps": list(WATER_MPS),
                 "policy": POLICY, "tc": 1, "vessel": args.vessel, "target": args.target,
                 "chord": bool(args.chord),
                 "playerRepositionNorthKm": list(DIAGNOSTIC_NORTH_KM) if args.player_reposition else None}
    frozen = live.b1.read_json(args.control) if args.control else None
    if args.phase == "on" and (frozen["signature"] != signature or frozen["target"]["name"] != args.vessel):
        parser.error("frozen OFF contract does not match this run")
    live.WORK.mkdir(parents=True, exist_ok=False)
    for name in ("manifest.json", live.b1.COPY_NAME):
        shutil.copyfile(args.stage / name, live.WORK / name)
    live.b1.write_new(live.WORK / "run-contract.json", signature)
    collect = collector(live, args)
    settings = {"collector": collect}
    if args.phase == "on":
        settings.update(field=",".join(map(str, WATER_MPS)), nearby=True, rudder=True, far=True)
    if args.phase in ("diag", "diag_chord"):
        series = [
            ("off", "off", {"collector": collect}),
            ("on", "off", {"collector": collect, "field": ",".join(map(str, WATER_MPS)), "nearby": True, "rudder": True, "far": True}),
        ]
    elif args.phase == "diag_on":
        series = [("on", "off", {"collector": collect, "field": ",".join(map(str, WATER_MPS)), "nearby": True, "rudder": True, "far": True})]
    elif args.phase == "save_load":
        series = [("save_load", "off", {"collector": lambda _label: run_pre_entry_save_load(live, args),
                                         "field": ",".join(map(str, WATER_MPS)), "nearby": True, "rudder": True, "far": True})]
    else:
        labels = ["off-a", "off-b", "off-c"] if args.phase == "off" else [args.phase]
        series = [(label, "off", settings) for label in labels]

    def report():
        if args.phase == "off":
            result = freeze([live.b1.read_json(live.WORK / f"{label}.json") for label in labels], POLICY)
            live.b1.write_new(live.WORK / "frozen-control.json", dict(signature=signature, **result))
        elif args.phase == "on":
            result = evaluate(live.b1.read_json(live.WORK / "on.json"), frozen, POLICY)
            if args.player_reposition:
                result["not_run"].append("continuousSailingAcrossEncounterBoundary")
            live.b1.write_new(live.WORK / "comparison.json", result)
            return 0 if result["status"] == "PASS" else 1
        elif args.phase == "pilot":
            receipt = live.b1.read_json(live.WORK / "pilot.json")
            live.b1.write_new(live.WORK / "pilot-metrics.json", metrics(receipt, POLICY, False))
        elif args.phase == "save_load":
            receipt = live.b1.read_json(live.WORK / "pre-entry-save-load.json")
            return 0 if receipt.get("status") == "PASS" else 1
        return 0

    with staged_fixture():
        try:
            return live.isolated(series, report)
        except (OSError, RuntimeError, ValueError) as error:
            live.b1.write_new(live.WORK / "incomplete.json", {
                "status": "NOT RUN", "phase": args.phase, "reason": str(error)
            })
            raise


if __name__ == "__main__":
    raise SystemExit(main())
