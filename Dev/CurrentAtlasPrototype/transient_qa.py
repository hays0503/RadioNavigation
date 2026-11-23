"""Offline air/water transient and handoff fixtures, not a game benchmark."""
from dataclasses import dataclass
import json
import math
import random


@dataclass(frozen=True)
class Axis:
    u: float
    w: float
    kw: float
    ka: float
    drive: float

    def force(self, v):
        rw, ra = self.u - v, self.w - v
        return self.drive + self.kw * rw * abs(rw) + self.ka * ra * abs(ra)

    def damping(self, v):
        return 2 * (self.kw * abs(self.u - v) + self.ka * abs(self.w - v))

    def equilibrium(self):
        shift = math.copysign(math.sqrt(abs(self.drive) / (self.kw + self.ka)), self.drive)
        lo, hi = min(self.u, self.w) + shift, max(self.u, self.w) + shift
        for _ in range(60):  # Independent reference; production candidate is Balance/Program.cs.
            mid = (lo + hi) / 2
            if self.force(mid) > 0:
                lo = mid
            else:
                hi = mid
        return (lo + hi) / 2


def exponential(v, target, rate, h):
    r = rate * h
    phi = -math.expm1(-r) / r if r else 1
    return target + (v - target) * math.exp(-r), (target + (v - target) * phi) * h


def exact_single_medium(v, target, k, h):
    q = v - target
    r = k * abs(q) * h
    phi = math.log1p(r) / r if r else 1
    return target + q / (1 + r), (target + q * phi) * h


def envelope(axis, v, target, h):
    lo, hi = sorted((v, target))
    rates = [axis.damping(lo), axis.damping(hi)]
    least = min(rates + [axis.damping(t) for t in (axis.u, axis.w) if lo < t < hi])
    most = max(rates)  # Damping is convex and piecewise linear.
    slow, fast = exponential(v, target, least, h), exponential(v, target, most, h)
    return tuple(sorted((slow[i], fast[i])) for i in range(2))


def average_abs_slip(a, b):
    """Mean absolute slip along the segment (including a possible zero crossing)."""
    aa, bb = abs(a), abs(b)
    if a < 0 < b or b < 0 < a:
        width = aa + bb
        return (aa * (aa / width) + bb * (bb / width)) / 2
    return (aa + bb) / 2


def approx(axis, v, target, h):
    if v == target or not h:
        return v, v * h
    if axis.drive == 0 and (axis.ka == 0 or axis.u == axis.w):
        return exact_single_medium(v, axis.u, axis.kw + axis.ka, h)
    # Compute the force secant as a mean derivative, without subtracting nearly
    # equal forces or dividing by a vanishing gap to equilibrium.
    rate = 2 * (axis.kw * average_abs_slip(v - axis.u, target - axis.u)
                + axis.ka * average_abs_slip(v - axis.w, target - axis.w))
    return exponential(v, target, rate, h)


def defect_bound(axis, v, target, h, result):
    # The exact one-medium branch needs no dynamic approximation envelope.
    # Neither path accounts for floating-point or input variation error.
    if v == target or not h or (axis.drive == 0 and (axis.ka == 0 or axis.u == axis.w)):
        return 0.0, 0.0
    return tuple(max(abs(value - lo), abs(value - hi))
                 for value, (lo, hi) in zip(result, envelope(axis, v, target, h)))


def two_halves(axis, v, target, h):
    half = h / 2
    first = approx(axis, v, target, half)
    b_v1, b_x1 = defect_bound(axis, v, target, half, first)
    second = approx(axis, first[0], target, half)
    b_v2, b_x2 = defect_bound(axis, first[0], target, half, second)
    # Exact scalar flows here are non-expansive in speed. Integrated position
    # over the second half can differ by up to half * b_v1 from that start error.
    return (second[0], first[1] + second[1]), (b_v1 + b_v2, b_x1 + half * b_v1 + b_x2)


def reference(axis, v, h, n):
    step, pos = h / n, 0.0
    for _ in range(n):
        a = axis.force(v)
        vb = v + step * a / 2
        b = axis.force(vb)
        vc = v + step * b / 2
        c = axis.force(vc)
        vd = v + step * c
        d = axis.force(vd)
        pos += step * (v + 2 * vb + 2 * vc + vd) / 6
        v += step * (a + 2 * b + 2 * c + d) / 6
    return v, pos


def inside(x, bounds, allowance=1e-9):
    return bounds[0] - allowance <= x <= bounds[1] + allowance


def longitudinal_for_world_speed(speed, lateral, direction):
    """One chosen branch of a fixed-heading ground-speed target; no thrust tested."""
    cross = abs(lateral)
    if speed < cross:
        return None
    return direction * math.sqrt((speed - cross) * (speed + cross))


@dataclass(frozen=True)
class Committed:
    authority: tuple
    through_s: int
    position: tuple
    velocity: tuple

    def handoff(self, authority, epoch, next_owner):
        if authority != self.authority or epoch != self.through_s:
            return None
        return Committed((next_owner, authority[1] + 1), epoch, self.position, self.velocity)

    def advance(self, authority, start, end):
        if authority != self.authority or start != self.through_s or end <= start:
            return None
        # Constant world velocity isolates ownership from the force formula.
        pos = tuple(x + v * (end - start) for x, v in zip(self.position, self.velocity))
        return Committed(authority, end, pos, self.velocity)


def handoff_cases(check):
    initial = Committed(("physical", 0), 100, (10, 20), (2, -3))
    p = initial.advance(("physical", 0), 100, 105)
    check("cannot_handoff_ahead_of_committed_epoch",
          p.handoff(("physical", 0), 106, "strategy") is None)
    s = p.handoff(("physical", 0), 105, "strategy")
    check("handoff_transfers_both_velocity_components_without_moving",
          s.position == (20, 5) and s.velocity == (2, -3) and s.through_s == 105)
    check("former_owner_cannot_integrate_next_interval",
          s.advance(("physical", 0), 105, 110) is None)
    check("future_start_cannot_skip_interval",
          s.advance(("strategy", 1), 106, 110) is None)
    check("overlap_cannot_repeat_prior_owner_interval",
          s.advance(("strategy", 1), 104, 110) is None)
    done = s.advance(("strategy", 1), 105, 110)
    check("already_committed_interval_cannot_be_replayed",
          done.advance(("strategy", 1), 105, 110) is None)
    restored = done.handoff(("strategy", 1), 110, "physical")
    check("old_generation_of_current_owner_is_rejected",
          restored.advance(("physical", 0), 110, 112) is None)
    final = restored.advance(("physical", 2), 110, 112)
    check("both_handoffs_cover_exactly_one_contiguous_vector_trajectory",
          final.through_s == 112 and final.position == (34, -16) and final.velocity == (2, -3))


def main():
    passed = []

    def check(name, condition):
        if not condition:
            raise AssertionError(name)
        passed.append(name)

    water = Axis(.5, 16, .04, 0, 0)
    terminal = water.equilibrium()
    v, pos = approx(water, 0, terminal, 60)
    check("water_only_exact_case", abs(v - .27272727272727) < 1e-12
          and abs(pos - (30 - math.log(2.2) / .04)) < 1e-12)
    check("co_located_media_exact_case",
          approx(Axis(.5, .5, .02, .02, 0), 0, .5, 60) == (v, pos))
    check("exact_current_integral_is_not_inertial_hull_displacement",
          abs(.5 * 60 - pos - math.log(2.2) / .04) < 1e-12 and .5 * 60 - pos > 19)
    va, xa = approx(water, 0, terminal, 17)
    vb, xb = approx(water, va, terminal, 43)
    check("exact_branch_split_invariant", abs(vb - v) < 1e-12 and abs(xa + xb - pos) < 1e-12)
    check("end_velocity_times_interval_is_not_displacement", abs(v * 60 - pos) > 6)
    check("constant_target_zero_rate", exponential(2, -3, 0, 10) == (2, 20))
    check("zero_interval", approx(water, 2, .5, 0) == (2, 0))
    check("zero_slip", approx(water, .5, .5, 60) == (.5, 30))
    check("tiny_rate_stays_close_to_constant_velocity",
          abs(exponential(2, -3, 1e-16, 10)[1] - 20) < 1e-12)
    check("mean_abs_slip_accounts_for_crossing",
          average_abs_slip(-2, 4) == average_abs_slip(4, -2)
          and abs(average_abs_slip(-2, 4) - 5 / 3) < 1e-12
          and average_abs_slip(2, 4) == 3 and average_abs_slip(0, 0) == 0)
    check("lateral_speed_alone_can_make_world_speed_target_impossible",
          longitudinal_for_world_speed(2, -3, 1) is None)
    check("world_speed_target_keeps_commanded_longitudinal_branch",
          longitudinal_for_world_speed(5, -3, 1) == 4
          and longitudinal_for_world_speed(5, -3, -1) == -4
          and longitudinal_for_world_speed(3, -3, -1) == 0)

    # Test the composition itself: both local displacement bounds are measured
    # from their own initial speed. They do not already cover the second-half inlet error.
    p, v0, half = Axis(.5, 16, .04, .0001, 0), 0, 15
    e = p.equilibrium()
    first = approx(p, v0, e, half)
    b_v1, b_x1 = defect_bound(p, v0, e, half, first)
    second = approx(p, first[0], e, half)
    _, b_x2 = defect_bound(p, first[0], e, half, second)
    _, (_, b_x) = two_halves(p, v0, e, 2 * half)
    inlet_term = half * b_v1
    check("two_halves_include_propagated_inlet_velocity_error",
          inlet_term > 1 and math.isclose(b_x - (b_x1 + b_x2), inlet_term, abs_tol=1e-12))
    handoff_cases(check)

    cases = [
        ("above_water_start", Axis(.5, 16, .04, .0001, 0), 0),
        ("opposing_wind", Axis(.5, -16, .04, .0001, 0), 0),
        ("slowing_from_previous_weather", Axis(-1, 8, .04, .0004, 0), 3),
        ("held_thrust", Axis(.5, -10, .015, .0004, .15), 3),
        ("deep_inertial", water, 0),
        ("light_object", Axis(.5, 16, .1, .025, 0), .5),
    ]
    rows = []
    for name, p, v0 in cases:
        target = p.equilibrium()
        for h in (2, 30, 60, 300):
            vc, xc = reference(p, v0, h, 4096)
            vr, xr = reference(p, v0, h, 8192)
            assert abs(vc - vr) < 2e-6 and abs(xc - xr) < 2e-6, (name, h, vc - vr, xc - xr)
            ve, xe = approx(p, v0, target, h)
            vb, xb = envelope(p, v0, target, h)
            assert inside(vr, vb) and inside(xr, xb), (name, h, vr, xr, vb, xb)
            assert inside(ve, vb) and inside(xe, xb), (name, h, ve, xe, vb, xb)
            bound_v, bound_x = defect_bound(p, v0, target, h, (ve, xe))
            assert abs(vr - ve) <= bound_v + 1e-9 and abs(xr - xe) <= bound_x + 1e-9
            (v2, x2), (bv2, bx2) = two_halves(p, v0, target, h)
            assert abs(vr - v2) <= bv2 + 1e-9 and abs(xr - x2) <= bx2 + 1e-9
            rows.append(dict(case=name, game_s=h, v_eq_mps=target, reference_m=xr,
                             snap_error_m=abs(target * h - xr), candidate_error_m=abs(xe - xr),
                             candidate_error_mps=abs(ve - vr),
                             position_bound_m=bound_x, speed_bound_mps=bound_v,
                             refined_error_m=abs(x2 - xr), refined_position_bound_m=bx2,
                             refined_speed_bound_mps=bv2))
    check("24_converged_reference_cases_inside_one_and_two_step_bounds", len(rows) == 24)
    check("300s_surrogate_can_need_fallback",
          any(r["game_s"] == 300 and r["refined_error_m"] > 1 for r in rows))

    rng = random.Random(2411)
    max_reference_refinement = 0.0
    for i in range(160):
        p = Axis(rng.uniform(-2, 2), rng.uniform(-12, 12), rng.uniform(.008, .06),
                 0 if i % 7 == 0 else rng.uniform(.0002, .013), rng.uniform(-.15, .15))
        v0, h = rng.uniform(-5, 5), rng.uniform(1, 80)
        e = p.equilibrium()
        v1, x1 = reference(p, v0, h, 1024)
        v2, x2 = reference(p, v0, h, 2048)
        max_reference_refinement = max(max_reference_refinement, abs(x1 - x2), abs(v1 - v2) * h)
        vb, xb = envelope(p, v0, e, h)
        assert inside(v2, vb) and inside(x2, xb), (i, v2, x2, vb, xb)
        candidate = approx(p, v0, e, h)
        bounds = defect_bound(p, v0, e, h, candidate)
        assert abs(candidate[0] - v2) <= bounds[0] + 1e-9, i
        assert abs(candidate[1] - x2) <= bounds[1] + 1e-9, i
        assert abs(e * h - x2) <= abs(v0 - e) * h + 1e-9, i
        refined, bounds = two_halves(p, v0, e, h)
        assert abs(refined[0] - v2) <= bounds[0] + 1e-9, i
        assert abs(refined[1] - x2) <= bounds[1] + 1e-9, i
    check("160_seeded_enclosures_and_snap_defect_bound", max_reference_refinement < .0001)

    # Show why a scalar check per component does not enforce the same vector budget.
    test_axis = Axis(.5, 16, .04, .0001, 0)
    e = test_axis.equilibrium()
    h = 30
    end = approx(test_axis, 0, e, h)
    _, x_bound = defect_bound(test_axis, 0, e, h, end)
    _, xr = reference(test_axis, 0, h, 4096)
    test_budget = 1.2 * x_bound
    check("euclidean_gate_rejects_two_axes_that_each_pass",
          0 < x_bound < test_budget < math.hypot(x_bound, x_bound)
          and math.hypot(end[1] - xr, end[1] - xr) <= math.hypot(x_bound, x_bound))

    # Analytic split check only; ownership and lateral velocity are tested above.
    va, xa = exact_single_medium(0, .5, .04, 30)
    vb, xb = exact_single_medium(va, .5, .04, 30)
    v_full, x_full = exact_single_medium(0, .5, .04, 60)
    check("continued_scalar_solution_matches_uninterrupted_drift",
          abs(vb - v_full) < 1e-12 and abs(xa + xb - x_full) < 1e-12)
    snap_defect = xa + .5 * 30 - x_full
    check("equilibrium_handoff_is_a_detectable_mutation",
          snap_defect > 0 and abs(snap_defect - math.log(2.2 / 1.6) / .04) < 1e-12
          and .5 - v_full > .2)

    # A vessel initially moves against the current before reversing direction.
    # The exact trajectory enters and exits a threshold despite no endpoint change of side.
    stopped_v, stopped_x = exact_single_medium(-.5, .5, .04, 25)
    _, end_x = exact_single_medium(-.5, .5, .04, 100)
    check("transient_reversal_crossing_not_visible_at_endpoints",
          abs(stopped_v) < 1e-12 and stopped_x < -4 < min(0, end_x))

    print(json.dumps(dict(classification=__doc__, passed=len(passed), checks=passed,
                          max_random_reference_refinement_m_equivalent=max_reference_refinement,
                          second_half_inlet_bound_m=inlet_term,
                          handoff_snap_defect_m=snap_defect,
                          reversal_turn_m=stopped_x, reversal_endpoint_m=end_x, cases=rows), indent=2))


if __name__ == "__main__":
    main()
