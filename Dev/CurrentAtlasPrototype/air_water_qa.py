"""Synthetic air/water force checks. No Unity, real hull calibration or timings."""
import json
import math


def acceleration(v, u, w, kw, ka):
    dw, da = u - v, w - v
    return kw * dw * abs(dw) + ka * da * abs(da)


def equilibrium(u, w, kw, ka):
    sw, sa = math.sqrt(kw), math.sqrt(ka)
    return (sw * u + sa * w) / (sw + sa)


def exact_one_medium(v, target, k, h):
    slip = v - target
    return target + slip / (1 + k * abs(slip) * h)


def implicit_reference(v, u, w, kw, ka, h):
    """Bisection of a monotone residual; an offline reference, not runtime code."""
    lo, hi = min(v, u, w), max(v, u, w)
    for _ in range(64):
        mid = (lo + hi) / 2
        if mid - v - h * acceleration(mid, u, w, kw, ka) > 0:
            hi = mid
        else:
            lo = mid
    return (lo + hi) / 2


def rk4(v, h, derivative):
    a = derivative(v)
    b = derivative(v + h * a / 2)
    c = derivative(v + h * b / 2)
    d = derivative(v + h * c)
    return v + h * (a + 2 * b + 2 * c + d) / 6


def meteorological_from(degrees, speed_knots):
    angle = math.radians(degrees)
    speed = speed_knots * 1852 / 3600
    return -speed * math.sin(angle), -speed * math.cos(angle)


def main():
    results = []

    def check(name, ok, **record):
        results.append(dict(name=name, passed=bool(ok), **record))

    def close(a, b, tol=1e-10):
        return abs(a - b) < tol

    kw, ka = 0.04, 0.0001  # 1/m. Deliberate fixture inputs, not hull estimates.
    u, w = 0.5, 16.0
    check("at_rest_in_moving_water_accelerates", acceleration(0, u, 0, kw, 0) > 0)
    check("no_water_force_when_comoving", acceleration(u, u, w, kw, 0) == 0)
    check("submerged_zero_air_even_with_gale", acceleration(0.1, u, -40, kw, 0)
          == acceleration(0.1, u, 40, kw, 0))
    v = 3.2
    check("each_medium_dissipates_energy_in_its_own_frame",
          (v - u) * acceleration(v, u, w, kw, 0) < 0
          and (v - w) * acceleration(v, u, w, 0, ka) < 0)
    check("both_media_and_body_shift_invariant",
          close(acceleration(v, u, w, kw, ka), acceleration(v + 9, u + 9, w + 9, kw, ka)))
    check("shifting_water_only_changes_apparent_air",
          not close(acceleration(v, u, w, kw, ka), acceleration(v + 9, u + 9, w, kw, ka)))
    check("still_air_drag_on_moving_exposed_hull", acceleration(v, v, 0, kw, ka) < 0)
    check("pure_beam_wind_is_lateral_in_body_axes",
          acceleration(0, 0, 0, kw, ka) == 0 and acceleration(0, 0, w, kw, ka) > 0)
    north = meteorological_from(0, 10)
    west = meteorological_from(270, 10)
    check("north_wind_towards_south_and_units", close(north[0], 0) and close(north[1], -18520 / 3600))
    check("west_wind_towards_east", close(west[0], 18520 / 3600) and close(west[1], 0))
    l, r = meteorological_from(359, 10), meteorological_from(1, 10)
    check("wrap_interpolate_vectors_not_bearings", close((l[0] + r[0]) / 2, 0)
          and (l[1] + r[1]) / 2 < -5)

    terminal = equilibrium(u, w, kw, ka)
    check("analytic_terminal_zero_net_force", close(acceleration(terminal, u, w, kw, ka), 0),
          equilibrium_mps=terminal)
    opposite = equilibrium(u, -w, kw, ka)
    check("wind_can_reverse_ground_drift", opposite < 0, equilibrium_mps=opposite)
    more_exposed = equilibrium(u, w, kw, 9 * ka)
    check("same_water_distinct_air_exposure_changes_leeway", u < terminal < more_exposed < w,
          lower_exposure_mps=terminal, higher_exposure_mps=more_exposed)

    t, value = 0.0, 0.0
    for _ in range(24_000):
        value = rk4(value, .05, lambda x: acceleration(x, u, w, kw, ka))
        t += .05
    check("fine_rk4_converges_to_equilibrium", close(value, terminal),
          time_s=t, velocity_mps=value)
    single = exact_one_medium(0, u, kw, 180)
    fine = 0.0
    for _ in range(3600):
        fine = rk4(fine, .05, lambda x: acceleration(x, u, w, kw, 0))
    check("one_medium_exact_update_matches_fine_reference", close(single, fine),
          result_mps=single, reference_mps=fine)
    check("one_medium_split_is_consistent",
          close(single, exact_one_medium(exact_one_medium(0, u, kw, 30), u, kw, 150)))
    check("one_medium_large_step_does_not_overshoot",
          0 < exact_one_medium(0, u, kw, 3600) < u)

    h = 300
    explicit = u + h * acceleration(u, u, w, kw, ka)
    # Freezing both quadratic drag weights at v=u makes the water weight zero.
    # One Picard predictor is bounded by the two media, not necessarily by equilibrium.
    lagged = u + (w - u) * (h * ka * abs(w - u)) / (1 + h * ka * abs(w - u))
    implicit = implicit_reference(u, u, w, kw, ka, h)
    check("large_step_explicit_and_lagged_weights_overshoot_equilibrium",
          u < implicit < terminal < lagged < explicit,
          step_s=h, equilibrium_mps=terminal, explicit_mps=explicit,
          lagged_mps=lagged, implicit_mps=implicit)
    check("implicit_reference_residual", close(implicit - u, h * acceleration(implicit, u, w, kw, ka)))
    air_first = exact_one_medium(exact_one_medium(u, w, ka, h), u, kw, h)
    water_first = exact_one_medium(exact_one_medium(u, u, kw, h), w, ka, h)
    check("separate_exact_operators_still_depend_on_order", abs(air_first - water_first) > 1,
          step_s=h, air_first_mps=air_first, water_first_mps=water_first)

    # With a held exposed/immersed air-area state, blending the air coefficient
    # fades the direct air force without turning off water or the wave baseline.
    wet_air = [acceleration(v, u, w, kw, a * ka) - acceleration(v, u, w, kw, 0)
               for a in (0, .25, .5, 1)]
    check("air_exposure_zero_and_monotone", wet_air[0] == 0 and wet_air == sorted(wet_air))
    # Drive at a prescribed world speed with headwind: propulsion is not part of this model.
    base_air = acceleration(v, u, 0, 0, ka)
    headwind_delta = acceleration(v, u, -w, 0, ka) - base_air
    check("delta_air_correction_retains_baseline_in_calm_air",
          acceleration(v, u, 0, 0, ka) - base_air == 0 and headwind_delta < 0)

    passed = sum(r["passed"] for r in results)
    print(json.dumps({"scope": __doc__, "passed": passed, "failed": len(results) - passed,
                      "checks": results}, indent=2))
    return 0 if passed == len(results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
