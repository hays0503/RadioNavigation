"""Deterministic analytic fixtures, NOT execution of Unity/game/PyFES.

Python stdlib only. No atlas downloads or full-grid allocations.
The source contract checks benchmark call arguments; numerical fixtures exercise
the formulas/policies below, not the C# implementation of those policies.
"""
import argparse
import csv
import hashlib
import json
import math
from pathlib import Path
import re
import sys

PERIODS = [44714.16, 43200, 86164, 92950, 22357.08, 21600, 15483, 14400]
ROOT = Path(__file__).resolve().parent


def coefficients(count):
    return [
        (2 * math.pi / PERIODS[k % len(PERIODS)],
         .18 / (k + 1) * math.cos(.7 * k),
         .18 / (k + 1) * math.sin(.7 * k))
        for k in range(count)
    ]


def velocity(waves, t):
    return math.fsum(a * math.cos(w * t) + b * math.sin(w * t)
                     for w, a, b in waves)


def exact_integral(waves, t, dt):
    mid = t + dt / 2
    return math.fsum(
        (dt if w == 0 else 2 * math.sin(w * dt / 2) / w)
        * (a * math.cos(w * mid) + b * math.sin(w * mid))
        for w, a, b in waves
    )


def endpoint_integral(waves, t, dt):
    return math.fsum(
        a * dt if w == 0 else (
            a * (math.sin(w * (t + dt)) - math.sin(w * t))
            + b * (math.cos(w * t) - math.cos(w * (t + dt)))
        ) / w
        for w, a, b in waves
    )


def profile_policy(depth, bottom, tile_status="ready", dry=False,
                   valid_levels=(True, True, True, True),
                   u_valid=(True, True, True, True),
                   v_valid=(True, True, True, True)):
    """Conservative four-corner policy; zero is a valid value, never missing."""
    if tile_status != "ready":
        return tile_status, None
    if not math.isfinite(depth) or depth < 0:
        return "invalid_depth", None
    if dry:
        return "dry", None
    if depth > bottom:
        return "below_bottom", None
    levels = [0, 30, 100, 300]
    if depth > levels[-1]:
        return "unsupported_depth", None
    if not all(u_valid) or not all(v_valid):
        return "missing_vector_support", None
    if depth in levels:
        required = [levels.index(depth)]
    else:
        lo = next(i for i in range(len(levels) - 1)
                  if levels[i] < depth < levels[i + 1])
        required = [lo, lo + 1]
    if any(not valid_levels[i] or levels[i] > bottom for i in required):
        return "missing_profile", None
    return "valid", (0.0, 0.0)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True)
    parser.add_argument("--phase-only", action="store_true")
    parser.add_argument("--source", default="Program.cs")
    args = parser.parse_args()
    out = ROOT / args.output
    out.mkdir(parents=True, exist_ok=False)
    records, interpolation, integration, masks, memory = [], [], [], [], []

    def check(name, condition, **evidence):
        records.append(dict(name=name, passed=bool(condition), **evidence))

    source_path = ROOT / args.source
    source = source_path.read_text(encoding="utf-8-sig")
    benchmark = source.split("private static void Benchmark(", 1)[1].split(
        "private static void RefreshBenchmark(", 1)[0]
    constants = {k: int(v) for k, v in re.findall(r"\b(Benchmark\w+)\s*=\s*(\d+)", source)}

    def resolve(token):
        return int(token) if token.isdecimal() else constants.get(token)

    shared = re.search(r"basis\.Set\((\w+)\)", benchmark)
    preflight = source.split("private static void WorkloadPreflight(", 1)[1].split(
        "private static double SnapshotBound(", 1)[0]
    preflight_time = re.search(r"basis\.Set\((\w+)\)", preflight)
    workload = source.split("private static Vec SampleWorkload(", 1)[1].split(
        "private static void MovingCursorChecks(", 1)[0]
    scratch = re.search(r"return Sample\([^;]*scratch,\s*(\w+)\)", workload)
    call = re.search(r"Anchor\(active,\s*(\w+),\s*(\w+)\)", source)
    anchors = [resolve(token) for token in call.groups()] if call else []
    # Extract actual Sample snapshot calls rather than merely testing names.
    sample = source.split("private static Vec Sample(", 1)[1].split(
        "private static void Materialize(", 1)[0]
    mixes = re.findall(r"Bilinear\(t\.Tide1,\s*[01],\s*2,\s*q\),\s*(\.\d+)\)", sample)
    expected = (anchors[0] + .4 * (anchors[1] - anchors[0])
                if len(anchors) == 2 and None not in anchors and anchors[1] > anchors[0]
                else None)
    times = [resolve(m.group(1)) if m else None for m in (shared, scratch, preflight_time)]
    check("benchmark_call_arguments_share_snapshot_timestamp",
          expected is not None and len(mixes) == 2
          and all(float(x) == .4 for x in mixes)
          and all(t == expected for t in times),
           shared_time=times[0], scratch_time=times[1], preflight_time=times[2],
           snapshot_anchors=anchors,
          snapshot_mix=.4, expected_query_time=expected,
          scope="C# invocation-argument source contract, not DLL execution")

    if not args.phase_only:
        # x' = 1 + L*(x-t), x(0)=0 -> x(t)=t. Exact time integral per
        # frozen-position step gives e_next = (1+L*h)*e - L*h*h/2.
        length, slope = 86400, 1e-5
        step = 2 * 100 / (length * slope)
        t = error = local_sum = 0.0
        while t < length:
            h = min(step, length - t)
            error = (1 + slope * h) * error - slope * h * h / 2
            local_sum += slope * h * h / 2
            t += h
        check("local_defects_do_not_bound_endpoint_error",
              local_sum <= 100.00001 and 157 < abs(error) < 160,
              step_s=step, local_sum_m=local_sum, endpoint_error_m=abs(error))
        # Static scheduling arithmetic, not observed game execution.
        hour_ticks = 36000000000
        start_hour, requested_hours = 12, 72
        until_eleven = 24 - start_hour + 11 if start_hour > 11 else 11 - start_hour
        remaining = min(requested_hours, 24 + until_eleven) * hour_ticks + 10
        check("docked_noon_tick_count", remaining == 47 * hour_ticks + 10,
              simulation_ticks=remaining)
        chunks = []
        while remaining:
            chunk = min(remaining, 5 * hour_ticks)
            chunks.append(chunk)
            remaining -= chunk
        check("docked_noon_medium_skip", len(chunks) == 10
              and chunks[-1] == 2 * hour_ticks + 10, chunks_ticks=chunks)
        delta = 1 / 60
        base_interval, exploration, sandbox, artificial = 30, 2, 3, 4
        interval = base_interval * exploration / sandbox
        share = min(.25, delta * artificial / interval)
        sweep_game_s = delta * artificial * sandbox / share
        check("live_round_robin_respects_nonunit_scales", math.isclose(sweep_game_s, 60),
              fraction_per_frame=share, sweep_game_s=sweep_game_s)
        phases = [math.radians(359), math.radians(1)]
        real = math.fsum(math.cos(p) for p in phases) / 2
        imag = math.fsum(math.sin(p) for p in phases) / 2
        bad_phase = math.radians((359 + 1) / 2)
        check("phase_wrap_complex_interpolation",
              abs(real - math.cos(math.radians(1))) < 1e-14
              and abs(imag) < 1e-14 and math.cos(bad_phase) < -.99,
              real=real, imag=imag, naive_amplitude_phase_real=math.cos(bad_phase))
        check("opposing_complex_coefficients_cancel",
              math.hypot((1 + -1) / 2, (0 + 0) / 2) == 0)
        for count in (1, 8, 34):
            waves = coefficients(count)
            for start in (0.0, 123456.0):
                for h in (1, 60, 300, 1800, 3600, 36000):
                    bound = math.fsum(math.hypot(a, b) * w * w
                                      for w, a, b in waves) * h * h / 8
                    maximum = 0.0
                    for j in range(101):
                        f = j / 100
                        linear = (1 - f) * velocity(waves, start) + f * velocity(waves, start + h)
                        maximum = max(maximum, abs(velocity(waves, start + f * h) - linear))
                    passed = maximum <= bound + 2e-13
                    interpolation.append(dict(constituents=count, start_game_s=start,
                        spacing_game_s=h, max_error_mps=maximum, bound_mps=bound, passed=passed))
                    check("linear_basis_bound_K%d_t%s_h%s" % (count, start, h), passed)
        # Explicit cancellation/slack-water fixture: relative error is undefined.
        slack = [(2 * math.pi / PERIODS[0], 0.0, 1.0)]
        h, f = 60, .4
        t0 = -h * f
        linear = (1 - f) * velocity(slack, t0) + f * velocity(slack, t0 + h)
        bound = slack[0][0] ** 2 * h ** 2 / 8
        check("slack_water_absolute_not_relative_error",
              velocity(slack, 0) == 0 and abs(linear) <= bound,
              reference_mps=0, interpolated_mps=linear, bound_mps=bound,
              relative_error=None)
        for name, waves in (("single_M2_idealized", [(2 * math.pi / PERIODS[0], .8, .6)]),
                            ("synthetic_K8", coefficients(8)),
                            ("synthetic_K34_repeated_periods", coefficients(34))):
            for start in (0.0, 123456.0):
                for label, dt in (("1h", 3600), ("6h", 21600),
                                  ("M2_full_period", PERIODS[0]), ("24h", 86400),
                                  ("72h", 259200), ("30days", 2592000)):
                    exact = exact_integral(waves, start, dt)
                    alternate = endpoint_integral(waves, start, dt)
                    frozen = velocity(waves, start) * dt
                    midpoint = velocity(waves, start + dt / 2) * dt
                    agreement = abs(exact - alternate) <= 2e-8
                    integration.append(dict(fixture=name, start_game_s=start, duration=label,
                        elapsed_game_s=dt, exact_displacement_m=exact,
                        endpoint_formula_displacement_m=alternate,
                        frozen_displacement_m=frozen, midpoint_displacement_m=midpoint,
                        frozen_error_m=abs(frozen - exact),
                        midpoint_error_m=abs(midpoint - exact), passed=agreement))
                    check("analytic_integral_%s_t%s_%s" % (name, start, label), agreement)
                    if name == "single_M2_idealized" and label == "M2_full_period":
                        check("single_full_period_zero_t%s" % start, abs(exact) < 1e-8)
        check("zero_frequency_integral_limit", exact_integral([(0, .8, .6)], 10, 3600) == 2880)
        cases = [
            ("valid_zero", {}, "valid"),
            ("unavailable_tile", {"tile_status": "unavailable_tile"}, "unavailable_tile"),
            ("corrupt_tile", {"tile_status": "corrupt_tile"}, "corrupt_tile"),
            ("dry", {"dry": True}, "dry"),
            ("below_bottom", {"bottom": 20}, "below_bottom"),
            ("profile_gap", {"valid_levels": (True, True, False, True)}, "missing_profile"),
            ("v_mask_gap", {"v_valid": (True, True, False, True)}, "missing_vector_support"),
            ("u_mask_gap", {"u_valid": (True, True, True, False)}, "missing_vector_support"),
            ("no_below_bottom_bracket", {"bottom": 80}, "missing_profile"),
            ("exact_valid_level_needs_no_invalid_deeper_level",
             {"depth": 30, "valid_levels": (True, True, False, False)}, "valid"),
            ("depth_nan", {"depth": math.nan}, "invalid_depth"),
            ("depth_negative", {"depth": -1}, "invalid_depth"),
            ("beyond_profile", {"depth": 301, "bottom": 1000}, "unsupported_depth"),
        ]
        for name, changes, expected_status in cases:
            params = dict(depth=40, bottom=1000)
            params.update(changes)
            status, value = profile_policy(**params)
            passed = status == expected_status and (value == (0., 0.) if status == "valid" else value is None)
            masks.append(dict(fixture=name, expected=expected_status, actual=status,
                              value=value, passed=passed))
            check("policy_" + name, passed,
                  scope="Python policy fixture; prototype C# lacks per-depth masks")
        fill = 9.96921e36
        check("finite_fill_is_still_missing", math.isfinite(fill) and fill == 9.96921e36,
              scope="raw sentinel comparison before scale; IsFinite alone is insufficient")
        for k in (8, 16, 34):
            n, depths, seasons = 65 * 65, 4, 2
            packed_season = 4 * n * depths * seasons
            packed_tide = 8 * n * k
            snapshots = 16 * n
            for tiles in (1, 16):
                packed_resident = tiles * (packed_season + packed_tide + n + snapshots)
                prototype_resident = tiles * (3 * (packed_season + packed_tide) + n + snapshots)
                # Example reservation: old+new resident generations and one decoder.
                decode_example = packed_season + packed_tide + n + 32
                memory.append(dict(fixture="prototype_dimensions_not_chosen_atlas",
                    constituents=k, tiles=tiles, nodes_per_tile=n, depths=depths, seasons=seasons,
                    packed_cache_payload_bytes=packed_resident,
                    actual_prototype_array_payload_bytes=prototype_resident,
                    refresh_guard_and_outputs_extra_bytes=16 * n * tiles,
                    prototype_during_refresh_array_payload_bytes=prototype_resident + 16 * n * tiles,
                    two_generations_plus_decode_example_bytes=2 * packed_resident + decode_example,
                    excludes="headers, objects, compression scratch/input, indices, queues, CLR overhead"))
        global_bytes = 4 * 4320 * 2040 * 50 * 12
        check("hypothetical_global_memory_arithmetic", global_bytes == 21150720000,
              bytes=global_bytes, gib=global_bytes / 2**30,
              scope="arithmetic only; hypothetical dimensions, no NetCDF allocation or metadata claim")

    def save_csv(name, rows):
        if not rows:
            return
        with (out / name).open("w", encoding="utf-8", newline="") as file:
            writer = csv.DictWriter(file, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)

    save_csv("basis-interpolation.csv", interpolation)
    save_csv("integration.csv", integration)
    save_csv("mask-depth-policy.csv", masks)
    save_csv("memory.csv", memory)
    report = dict(
        description="Deterministic Python analytic QA; not Unity/PyFES/C# execution or real-data validation",
        source_contract_path=(source_path.relative_to(ROOT).as_posix()
                              if source_path.is_relative_to(ROOT) else source_path.as_posix()),
        source_sha256=hashlib.sha256(source_path.read_bytes()).hexdigest(),
        script_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        formulas=dict(linear_bound="h^2/8 * sum(A_k*omega_k^2), fixed coefficients/factors",
                      integral="sum 2*sin(omega*dt/2)/omega * (a*cos(omega*mid)+b*sin(omega*mid))",
                      phase_convention="a*cos(theta)+b*sin(theta), a=A*cos(g), b=A*sin(g)"),
        passed=sum(r["passed"] for r in records),
        failed=sum(not r["passed"] for r in records),
        checks=records,
    )
    (out / "qa.json").write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print(json.dumps(dict(output=str(out), passed=report["passed"], failed=report["failed"])))
    return 1 if report["failed"] else 0


if __name__ == "__main__":
    sys.exit(main())
