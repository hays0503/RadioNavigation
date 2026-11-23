"""High-TC player route and distant group verification against TC 1 and independent integrals (§13.36 #3.1)."""
import argparse
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
FIXTURE = "UBOAT.Mods.RadioNavigation.HighTcLiveFixture"
FIXTURE_PATH = Path(__file__).resolve().with_name("HighTcLiveFixture.cs")
TARGET = ROOT / "Source/Logic" / FIXTURE_PATH.name
STAGE = ROOT / "artifacts/reports/current-wind-stage-20261122"
ATLAS_DIR = ROOT / "Source/Data/CurrentAtlas"
KM_PER_DEG = 40075.16 / 360.0
TICKS_PER_S = 10_000_000

sys.path.insert(0, str(Path(__file__).resolve().with_name("CoastCompare")))
from atlantic_tile import Tile  # noqa: E402

live = None
request = None


def call(method, *args):
    reply = request("/reflect/call", type=FIXTURE, method=method,
                    args=json.dumps(args, separators=(",", ":")))
    if reply.get("ok") is not True:
        raise RuntimeError(f"{method} failed: {reply}")
    return reply["value"]


def post(path, **query):
    url = live.BOOT + path + ("?" + urlencode(query) if query else "")
    with live.HTTP.open(Request(url, method="POST"), timeout=180) as stream:
        data = json.load(stream)
    if data.get("ok") is not True:
        raise RuntimeError(f"{path} refused: {data}")
    return data


def player_state():
    parts = call("PlayerState").split(",")
    return {
        "ticks": int(parts[0]),
        "utc": parts[1],
        "tc": float(parts[2]),
        "x": float(parts[3]),
        "y": float(parts[4]),
        "vx": float(parts[5]),
        "vy": float(parts[6]),
        "vz": float(parts[7]),
        "heading": float(parts[8]),
        "depth": float(parts[9]),
        "routeCount": int(parts[10]),
        "wp0": [float(parts[11]), float(parts[12])],
        "etaS": None if parts[13] in ("NaN", "Infinity", "-Infinity") else float(parts[13]),
        "etaWp": [float(parts[14]), float(parts[15])],
        "solved": int(parts[16]),
        "covered": parts[17] == "1",
        "water": [float(parts[18]), float(parts[19])],
        "waterSteps": int(parts[20]),
    }


def far_state(group_id):
    parts = call("FarGroupState", group_id).split(",", 14)
    return {
        "ticks": int(parts[0]),
        "spawnState": int(parts[1]),
        "x": float(parts[2]),
        "y": float(parts[3]),
        "vxKmps": float(parts[4]),
        "vyKmps": float(parts[5]),
        "velKmps": float(parts[6]),
        "routeCount": int(parts[7]),
        "wp0": [float(parts[8]), float(parts[9])],
        "covered": parts[10] == "1",
        "water": [float(parts[11]), float(parts[12])],
        "lastUpdateTicks": int(parts[13]),
        "describe": parts[14],
    }


def parse_far_counters(describe_text):
    out = {}
    for token in describe_text.split():
        if "=" in token:
            k, _, v = token.partition("=")
            if v.lstrip("-").isdigit():
                out[k] = int(v)
    return out


def temp_save_path(name):
    return ROOT.parents[1] / "Saves" / f"{name}.save"


def remove_temp_saves(names):
    for name in names:
        p = temp_save_path(name)
        if p.exists():
            p.unlink()


def fine_atlas_integrate(tiles, x0, y0, body_vx_kmps, body_vy_kmps, utc_s, duration_s, steps=120):
    dt = duration_s / steps
    x, y = x0, y0
    covered_dt = 0.0
    for i in range(steps):
        t_mid = utc_s + (i + 0.5) * dt
        lat, lon = y / KM_PER_DEG, x / KM_PER_DEG
        w = next((tile.sample(t_mid, 0.0, lat, lon) for tile in tiles if tile.sample(t_mid, 0.0, lat, lon) is not None), None)
        we = (w[0] / 1000.0) if w is not None else 0.0
        wn = (w[1] / 1000.0) if w is not None else 0.0
        if w is not None:
            covered_dt += dt
        x += (body_vx_kmps + we) * dt
        y += (body_vy_kmps + wn) * dt
    return x, y, covered_dt


def fine_atlas_water_chord(tiles, x0, y0, x1, y1, utc_s, duration_s, steps=120):
    dt = duration_s / steps
    dx = dy = 0.0
    for i in range(steps):
        frac = (i + 0.5) / steps
        t_mid = utc_s + frac * duration_s
        lat = (y0 + (y1 - y0) * frac + dy) / KM_PER_DEG
        lon = (x0 + (x1 - x0) * frac + dx) / KM_PER_DEG
        w = next((tile.sample(t_mid, 0.0, lat, lon) for tile in tiles if tile.sample(t_mid, 0.0, lat, lon) is not None), None)
        if w is not None:
            dx += (w[0] / 1000.0) * dt
            dy += (w[1] / 1000.0) * dt
    return dx, dy


def wait_save_written(name, timeout=15):
    p = temp_save_path(name)
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if p.exists() and p.stat().st_size > 0:
            time.sleep(1.0)
            return
        time.sleep(0.2)
    time.sleep(1.5)


def run_player_tc_leg(save_name, tc, target_ticks, check_wp_change=False, checkpoint_save=None):
    post("/time/scale", value=1)
    post("/load", name=save_name)
    time.sleep(1.5)
    start = player_state()
    post("/time/scale", value=tc)
    trace = [start]
    wp_reset = None
    saved_mid = False
    deadline = time.monotonic() + 300
    while time.monotonic() < deadline:
        cur = player_state()
        trace.append(cur)
        if check_wp_change and wp_reset is None and cur["routeCount"] == 1:
            wp_reset = {
                "ticks": cur["ticks"],
                "wp0": cur["wp0"],
                "etaWp": cur["etaWp"],
                "etaS": cur["etaS"],
                "matchesNewWp": math.dist(cur["wp0"], cur["etaWp"]) < 0.05 and (cur["etaS"] or 0) > 10.0,
            }
        if checkpoint_save and not saved_mid and (cur["ticks"] - start["ticks"]) >= (target_ticks - start["ticks"]) * 0.45:
            post("/time/scale", value=1)
            post("/save", name=checkpoint_save)
            wait_save_written(checkpoint_save)
            saved_mid = True
            post("/time/scale", value=tc)
        if cur["ticks"] >= target_ticks or cur["routeCount"] == 0:
            break
        time.sleep(0.25 if tc > 1 else 0.5)
    post("/time/scale", value=1)
    end = player_state()
    return dict(tc=tc, start=start, end=end, elapsed_s=(end["ticks"] - start["ticks"]) / TICKS_PER_S,
                wp_reset=wp_reset, samples=len(trace), trace=trace[:3] + trace[-3:])


def collect_constant(label):
    created_saves = []
    try:
        # 1. Dogleg 2-waypoint comparison TC 1 vs TC 12 vs TC 48 vs TC 140 + ETA reset + save/load
        sx, sy = -1850.0, 5350.0
        wp1 = (-1849.6, 5350.4)
        wp2 = (-1849.2, 5351.8)
        post("/ship/heading", deg=45)
        post("/ship/rudder", value=0)
        time.sleep(0.5)
        res = call("SetPlayerRoute", sx, sy, 2, f"{wp1[0]},{wp1[1]};{wp2[0]},{wp2[1]}")
        if not res.startswith("ok:2"):
            raise RuntimeError(f"SetPlayerRoute refused: {res}")
        time.sleep(2.0)
        save_dogleg = "RadioNav_HighTC_Dogleg"
        created_saves.append(save_dogleg)
        post("/save", name=save_dogleg)
        wait_save_written(save_dogleg)
        t0 = player_state()["ticks"]
        target_180 = t0 + 180 * TICKS_PER_S
        target_360 = t0 + 360 * TICKS_PER_S

        tc1_run = run_player_tc_leg(save_dogleg, 1, target_180, check_wp_change=False)
        tc12_run = run_player_tc_leg(save_dogleg, 12, target_180, check_wp_change=False)
        ckpt_name = "RadioNav_HighTC_Ckpt"
        created_saves.append(ckpt_name)
        tc48_180 = run_player_tc_leg(save_dogleg, 48, target_180, check_wp_change=False)
        tc48_run = run_player_tc_leg(save_dogleg, 48, target_360, check_wp_change=True, checkpoint_save=ckpt_name)
        tc140_run = run_player_tc_leg(save_dogleg, 140, target_360, check_wp_change=True)

        # Replay from checkpoint after load at TC 48
        post("/load", name=ckpt_name)
        time.sleep(1.5)
        ckpt_loaded = player_state()
        post("/time/scale", value=48)
        deadline = time.monotonic() + 120
        while time.monotonic() < deadline:
            cur = player_state()
            if cur["ticks"] >= target_360 or cur["routeCount"] == 0:
                break
            time.sleep(0.25)
        post("/time/scale", value=1)
        ckpt_end = player_state()

        # 2. Long straight leg at TC 140 vs TC 1800
        long_wp = (sx + 10.0, sy + 18.0)
        post("/ship/heading", deg=29)
        post("/ship/rudder", value=0)
        time.sleep(0.5)
        call("SetPlayerRoute", sx, sy, 2, f"{long_wp[0]},{long_wp[1]}")
        time.sleep(1.5)
        save_long = "RadioNav_HighTC_Long"
        created_saves.append(save_long)
        post("/save", name=save_long)
        wait_save_written(save_long)
        t_long0 = player_state()["ticks"]
        t_long_end = t_long0 + 1800 * TICKS_PER_S
        run_140_long = run_player_tc_leg(save_long, 140, t_long_end)
        run_1800_long = run_player_tc_leg(save_long, 1800, t_long_end)

        live.b1.write_new(live.WORK / "constant.json", dict(
            tc1_180s=tc1_run, tc12_180s=tc12_run, tc48_180s=tc48_180,
            tc48_360s=tc48_run, tc140_360s=tc140_run,
            load_replay=dict(loaded=ckpt_loaded, end=ckpt_end),
            long_140=run_140_long, long_1800=run_1800_long,
        ))
    finally:
        post("/time/scale", value=1)
        remove_temp_saves(created_saves)


def collect_atlas(label):
    created_saves = []
    try:
        # 1. Player crossing -24.5 deg tile seam (48.0N, -24.53E -> -24.47E)
        y_seam = 48.0 * KM_PER_DEG
        x_west = -24.53 * KM_PER_DEG
        x_east = -24.47 * KM_PER_DEG
        post("/ship/heading", deg=90)
        post("/ship/rudder", value=0)
        time.sleep(0.5)
        call("SetPlayerRoute", x_west, y_seam, 4, f"{x_east},{y_seam}")
        time.sleep(1.5)
        seam_start = player_state()
        post("/time/scale", value=140)
        seam_samples = [seam_start]
        deadline = time.monotonic() + 120
        while time.monotonic() < deadline:
            cur = player_state()
            seam_samples.append(cur)
            if cur["routeCount"] == 0:
                break
            time.sleep(0.3)
        post("/time/scale", value=1)

        # 2. Player exiting northern atlas coverage (60.47N -> 60.56N, -15.0W)
        x_north = -15.0 * KM_PER_DEG
        y_in = 60.47 * KM_PER_DEG
        y_out = 60.56 * KM_PER_DEG
        post("/ship/heading", deg=0)
        post("/ship/rudder", value=0)
        time.sleep(0.5)
        call("SetPlayerRoute", x_north, y_in, 4, f"{x_north},{y_out}")
        time.sleep(1.5)
        edge_start = player_state()
        post("/time/scale", value=140)
        edge_samples = [edge_start]
        deadline = time.monotonic() + 120
        while time.monotonic() < deadline:
            cur = player_state()
            edge_samples.append(cur)
            if cur["routeCount"] == 0 or cur["y"] >= y_out - 0.5:
                break
            time.sleep(0.3)
        post("/time/scale", value=1)

        # 3. Distant unspawned group at high intervals: turn/waypoint, seam crossing, out-of-coverage, >30d cap, post-load
        # Place far group on a 2-leg turn across -24.5 deg seam (leg 1 > 12 km so steps[1..2] stay on leg 1)
        fg_start = (x_west, y_seam)
        fg_wp1 = (-24.42 * KM_PER_DEG, y_seam + 3.0)
        fg_wp2 = (-24.30 * KM_PER_DEG, y_seam + 13.0)
        gid_str = call("SetupFarGroup", fg_start[0], fg_start[1],
                       f"{fg_wp1[0]},{fg_wp1[1]};{fg_wp2[0]},{fg_wp2[1]}", -1.0)
        gid = int(gid_str)
        fg0 = far_state(gid)
        fg0["utc"] = player_state()["utc"]
        fg_steps = [fg0]
        # Advance in 1200s chunks + save/load midway
        save_fg = "RadioNav_HighTC_FarCkpt"
        created_saves.append(save_fg)
        fg1 = parse_far_str(call("ForceFarInterval", gid, 1200))
        fg1["utc"] = player_state()["utc"]
        fg_steps.append(fg1)
        post("/save", name=save_fg)
        wait_save_written(save_fg)
        before_reload = far_state(gid)
        fg_steps.append(parse_far_str(call("ForceFarInterval", gid, 1200)))
        post("/load", name=save_fg)
        time.sleep(1.5)
        gid_reloaded = int(call("FindFarGroupNear", before_reload["x"], before_reload["y"]))
        after_reload = far_state(gid_reloaded)
        fg_after_reload_step = parse_far_str(call("ForceFarInterval", gid_reloaded, 1200))

        # Measure turn-crossing behavior on a 90-deg corner (2.0 km East -> 12.0 km North)
        gid_turn = int(call("SetupFarGroup", x_west, y_seam,
                            f"{x_west + 2.0},{y_seam};{x_west + 2.0},{y_seam + 12.0}", 0.004))
        turn_before = far_state(gid_turn)
        turn_before["utc"] = player_state()["utc"]
        turn_after = parse_far_str(call("ForceFarInterval", gid_turn, 1200))
        turn_after["utc"] = player_state()["utc"]

        # Multi-interval 90-deg corner (3 x 400 s: leg 1 -> hit wp0 -> leg 2 North)
        gid_turn_m = int(call("SetupFarGroup", x_west, y_seam,
                              f"{x_west + 2.0},{y_seam};{x_west + 2.0},{y_seam + 12.0}", 0.004))
        turn_multi = [far_state(gid_turn_m)]
        turn_multi[0]["utc"] = player_state()["utc"]
        for _ in range(3):
            step = parse_far_str(call("ForceFarInterval", gid_turn_m, 400))
            step["utc"] = player_state()["utc"]
            turn_multi.append(step)

        # Test >30 days (2592000 s) limit MUST NOT silently pass/extrapolate: push 2600000s!
        before_cap = far_state(gid_reloaded)
        after_cap = parse_far_str(call("ForceFarInterval", gid_reloaded, 2600000))

        # Test out-of-coverage far interval at 61.0N, -15.0W
        gid_out = int(call("SetupFarGroup", x_north, 61.0 * KM_PER_DEG, f"{x_north},{61.2 * KM_PER_DEG}", 0.0))
        out_before = far_state(gid_out)
        out_after = parse_far_str(call("ForceFarInterval", gid_out, 1200))

        live.b1.write_new(live.WORK / "atlas.json", dict(
            player_seam=seam_samples,
            player_north_exit=edge_samples,
            far_group=dict(
                steps=fg_steps, before_reload=before_reload, after_reload=after_reload,
                after_reload_step=fg_after_reload_step,
                over_30d_before=before_cap, over_30d_after=after_cap,
                turn_before=turn_before, turn_after=turn_after,
                turn_multi=turn_multi,
                out_of_coverage_before=out_before, out_of_coverage_after=out_after,
            ),
        ))
    finally:
        post("/time/scale", value=1)
        remove_temp_saves(created_saves)


def parse_far_str(raw):
    parts = raw.split(",", 14)
    return {
        "ticks": int(parts[0]),
        "spawnState": int(parts[1]),
        "x": float(parts[2]),
        "y": float(parts[3]),
        "vxKmps": float(parts[4]),
        "vyKmps": float(parts[5]),
        "velKmps": float(parts[6]),
        "routeCount": int(parts[7]),
        "wp0": [float(parts[8]), float(parts[9])],
        "covered": parts[10] == "1",
        "water": [float(parts[11]), float(parts[12])],
        "lastUpdateTicks": int(parts[13]),
        "describe": parts[14],
    }


def report():
    const_data = live.b1.read_json(live.WORK / "constant.json")
    atlas_data = live.b1.read_json(live.WORK / "atlas.json")
    c1 = const_data["tc1_180s"]
    c12 = const_data["tc12_180s"]
    c48_180 = const_data["tc48_180s"]
    c48 = const_data["tc48_360s"]
    c140 = const_data["tc140_360s"]
    c_rep = const_data["load_replay"]
    diff_1_12_m = math.dist([c1["end"]["x"], c1["end"]["y"]], [c12["end"]["x"], c12["end"]["y"]]) * 1000.0
    diff_1_48_m = math.dist([c1["end"]["x"], c1["end"]["y"]], [c48_180["end"]["x"], c48_180["end"]["y"]]) * 1000.0
    diff_48_140_m = math.dist([c48["end"]["x"], c48["end"]["y"]], [c140["end"]["x"], c140["end"]["y"]]) * 1000.0
    diff_load_m = math.dist([c48["end"]["x"], c48["end"]["y"]], [c_rep["end"]["x"], c_rep["end"]["y"]]) * 1000.0
    l140 = const_data["long_140"]
    l1800 = const_data["long_1800"]
    diff_140_1800_m = math.dist([l140["end"]["x"], l140["end"]["y"]], [l1800["end"]["x"], l1800["end"]["y"]]) * 1000.0

    l140_dt = l140["elapsed_s"]
    l1800_dt = l1800["elapsed_s"]
    l140_dx = (l140["end"]["x"] - l140["start"]["x"]) * 1000.0
    l140_dy = (l140["end"]["y"] - l140["start"]["y"]) * 1000.0
    l1800_dx = (l1800["end"]["x"] - l1800["start"]["x"]) * 1000.0
    l1800_dy = (l1800["end"]["y"] - l1800["start"]["y"]) * 1000.0
    l140_hdg = math.degrees(math.atan2(l140_dx, l140_dy))
    l1800_hdg = math.degrees(math.atan2(l1800_dx, l1800_dy))
    long_course_diff_deg = abs((l1800_hdg - l140_hdg + 180.0) % 360.0 - 180.0)

    seam = atlas_data["player_seam"]
    seam_crossed = any(s["x"] < -24.5 * KM_PER_DEG for s in seam) and any(s["x"] > -24.5 * KM_PER_DEG for s in seam)
    seam_all_covered = all(s["covered"] for s in seam)
    seam_arrived = seam[-1]["routeCount"] == 0
    exit_rows = atlas_data["player_north_exit"]
    exited_coverage = any(s["covered"] for s in exit_rows) and any(not s["covered"] and s["water"] == [0.0, 0.0] for s in exit_rows)

    fg = atlas_data["far_group"]
    cap_before = parse_far_counters(fg["over_30d_before"]["describe"])
    cap_after = parse_far_counters(fg["over_30d_after"]["describe"])
    cap_rejected = cap_after.get("skippedLong", 0) > cap_before.get("skippedLong", 0) and cap_after.get("unsupported", 0) > cap_before.get("unsupported", 0)

    out_b = parse_far_counters(fg["out_of_coverage_before"]["describe"])
    out_a = parse_far_counters(fg["out_of_coverage_after"]["describe"])
    out_counted = out_a.get("noCoverage", 0) > out_b.get("noCoverage", 0)

    # Independent finer Python integration check for first 1200s far step
    tiles = [Tile(p.read_bytes()) for p in sorted(ATLAS_DIR.glob("*.rnw"))]
    s0, s1 = fg["steps"][0], fg["steps"][1]
    from datetime import datetime, timezone
    def parse_utc(iso):
        dt_parsed = datetime.fromisoformat(iso.replace("Z", "+00:00"))
        if dt_parsed.tzinfo is None:
            dt_parsed = dt_parsed.replace(tzinfo=timezone.utc)
        return dt_parsed.timestamp()

    dt_s = 1200.0 + (s1["lastUpdateTicks"] - s0["lastUpdateTicks"]) / TICKS_PER_S
    utc1 = parse_utc(s1["utc"])
    utc0 = utc1 - dt_s
    # Note: s0 is UpdateGroup before ForceFarInterval, stock velocity along leg:
    leg_dx = s0["wp0"][0] - s0["x"]
    leg_dy = s0["wp0"][1] - s0["y"]
    leg_len = math.hypot(leg_dx, leg_dy)
    ux = (leg_dx / leg_len * s0["velKmps"]) if leg_len > 0 else 0.0
    uy = (leg_dy / leg_len * s0["velKmps"]) if leg_len > 0 else 0.0
    fx, fy, _ = fine_atlas_integrate(tiles, s0["x"], s0["y"], ux, uy, utc0, dt_s)
    far_vs_fine_m = math.dist([s1["x"], s1["y"]], [fx, fy]) * 1000.0

    tb, ta = fg["turn_before"], fg["turn_after"]
    # Stock UBOAT UpdateGroup stops at wp0 on the crossing tick (discarding leftover 700 s).
    # Measure both stock waypoint quantization and water integration error along start -> wp0:
    expected_turn_corner_m = math.dist([ta["x"], ta["y"]], [tb["x"] + 2.0 + ta["water"][0] * 1.2, tb["y"] + 2.8 + ta["water"][1] * 1.2]) * 1000.0
    turn_dt_s = 1200.0 + (ta["lastUpdateTicks"] - tb["lastUpdateTicks"]) / TICKS_PER_S
    turn_utc0 = parse_utc(ta["utc"]) - turn_dt_s
    tw_dx, tw_dy = fine_atlas_water_chord(tiles, tb["x"], tb["y"], tb["wp0"][0], tb["wp0"][1], turn_utc0, turn_dt_s)
    far_turn_water_error_m = math.dist([ta["x"], ta["y"]], [tb["wp0"][0] + tw_dx, tb["wp0"][1] + tw_dy]) * 1000.0

    tm = fg["turn_multi"]
    tm_step_errors_m = []
    for idx in range(3):
        a_step, b_step = tm[idx], tm[idx + 1]
        s_dt = 400.0 + (b_step["lastUpdateTicks"] - a_step["lastUpdateTicks"]) / TICKS_PER_S
        s_utc0 = parse_utc(b_step["utc"]) - s_dt
        ldx = a_step["wp0"][0] - a_step["x"]
        ldy = a_step["wp0"][1] - a_step["y"]
        llen = max(1e-9, math.hypot(ldx, ldy))
        if b_step["routeCount"] < a_step["routeCount"]:
            stock_x, stock_y = a_step["wp0"][0], a_step["wp0"][1]
        else:
            stock_x = a_step["x"] + (ldx / llen) * a_step["velKmps"] * s_dt
            stock_y = a_step["y"] + (ldy / llen) * a_step["velKmps"] * s_dt
        sdx, sdy = fine_atlas_water_chord(tiles, a_step["x"], a_step["y"], stock_x, stock_y, s_utc0, s_dt)
        tm_step_errors_m.append(math.dist([b_step["x"], b_step["y"]], [stock_x + sdx, stock_y + sdy]) * 1000.0)
    turn_multi_water_error_m = max(tm_step_errors_m)
    turn_multi_leg2_ok = (tm[0]["routeCount"] == 2 and tm[1]["routeCount"] == 2 and tm[-1]["routeCount"] == 1)

    eta_reset_ok = bool(c48.get("wp_reset") and c48["wp_reset"].get("matchesNewWp"))
    load_replay_far_m = math.dist([fg["steps"][2]["x"], fg["steps"][2]["y"]],
                                  [fg["after_reload_step"]["x"], fg["after_reload_step"]["y"]]) * 1000.0

    passed = (eta_reset_ok and diff_1_12_m <= 150.0 and diff_1_48_m <= 150.0
              and diff_48_140_m <= 450.0 and diff_load_m <= 150.0
              and long_course_diff_deg <= 2.0 and far_vs_fine_m <= 150.0
              and far_turn_water_error_m <= 150.0
              and turn_multi_water_error_m <= 150.0 and turn_multi_leg2_ok
              and seam_crossed and seam_all_covered and seam_arrived and exited_coverage
              and cap_rejected and out_counted and load_replay_far_m <= 50.0)
    summary = dict(
        status="PASS" if passed else "FAIL",
        player_tc1_vs_tc12_180s_m=diff_1_12_m,
        player_tc1_vs_tc48_180s_m=diff_1_48_m,
        player_tc48_vs_tc140_360s_m=diff_48_140_m,
        player_load_replay_diff_m=diff_load_m,
        player_tc140_vs_tc1800_m=diff_140_1800_m,
        player_tc140_vs_tc1800_course_deg=long_course_diff_deg,
        player_tc1800_analytical_water_m=[0.6 * l1800_dt, 0.0],
        player_tc140_analytical_water_m=[0.6 * l140_dt, 0.0],
        eta_reset=c48.get("wp_reset"),
        seam_crossed=seam_crossed,
        seam_all_covered=seam_all_covered,
        seam_arrived=seam_arrived,
        north_exit_coverage_dropped=exited_coverage,
        far_1200s_vs_fine_integral_m=far_vs_fine_m,
        far_turn_approx_error_m=expected_turn_corner_m,
        far_turn_water_error_m=far_turn_water_error_m,
        far_turn_multistep_water_error_m=turn_multi_water_error_m,
        far_turn_multistep_leg2_ok=turn_multi_leg2_ok,
        far_over_30d_cap_rejected=cap_rejected,
        far_out_of_coverage_counted=out_counted,
        far_load_replay_diff_m=load_replay_far_m,
    )
    live.b1.write_new(live.WORK / "comparison.json", summary)
    return 0 if passed else 1


def main():
    global live, request
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("work", type=Path)
    parser.add_argument("--stage", type=Path, default=STAGE)
    parser.add_argument("--confirm-cloud-off", action="store_true")
    args = parser.parse_args()
    if not args.confirm_cloud_off:
        parser.error("Steam Cloud must be off")
    sys.path.insert(0, str(ROOT / "artifacts/reports/current-b1-control-20261121"))
    import run_live_series as live_mod
    from run_depth_receipt import request as req_fn
    live, request = live_mod, req_fn
    if live.game_running() or live.bridge_listening() or TARGET.exists():
        parser.error("game, bridge or unexpected temporary fixture present")
    manifest = live.b1.verified_stage(args.stage)
    live.b1.verify(args.stage)
    live.WORK = args.work.resolve()
    live.WORK.mkdir(parents=True, exist_ok=False)
    for name in ("manifest.json", live.b1.COPY_NAME):
        shutil.copyfile(args.stage / name, live.WORK / name)
    os.environ["RADIONAV_ROUTE_CURRENT"] = "1"
    content = FIXTURE_PATH.read_bytes()
    with TARGET.open("xb") as stream:
        stream.write(content)
    try:
        series = [
            ("constant", "field", dict(field="0.6,0", far=True, collector=collect_constant)),
            ("atlas", "atlas", dict(atlas="bundled", far=True, collector=collect_atlas)),
        ]
        return live.isolated(series, report)
    finally:
        if TARGET.is_file() and TARGET.read_bytes() == content:
            TARGET.unlink()


if __name__ == "__main__":
    raise SystemExit(main())
