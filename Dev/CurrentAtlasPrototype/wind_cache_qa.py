"""Synthetic wind preparation QA. Not game weather, calibration or FPS."""
from dataclasses import dataclass
from itertools import product
import json
import math
import random
from types import MappingProxyType


def wind(x, y, t):
    """Coordinates are km, t is game s, output is east/north m/s."""
    phase = x / 40 + y / 60 - t / 7200
    return 10 + 5 * math.sin(phase), 3 + 6 * math.cos(phase)


def proposed_bound(spacing):
    # ||d²W/dq²|| <= 6*kq² for this analytic fixture only.
    return 6 / 8 * sum((h * k) ** 2 for h, k in zip(spacing, (1 / 40, 1 / 60, 1 / 7200)))


@dataclass(frozen=True)
class Snapshot:
    revision: int
    spacing: tuple
    anchors: dict

    def sample(self, point, revision):
        if revision != self.revision:
            return None
        keys, fractions = cell(point, self.spacing)
        east, north = 0., 0.
        for bits, key in keys:
            value = self.anchors.get(key)
            if value is None:
                return None
            w = math.prod(f if bit else 1 - f for bit, f in zip(bits, fractions))
            east += w * value[0]
            north += w * value[1]
        return east, north


def cell(point, spacing):
    coord = [v / h for v, h in zip(point, spacing)]
    lower = [math.floor(v) for v in coord]
    weights = [v - k for v, k in zip(coord, lower)]
    keys = [(bits, tuple(k + b for k, b in zip(lower, bits)))
            for bits in product((0, 1), repeat=3)]
    return keys, weights


def prepare(points, spacing, revision, evaluate, previous=None):
    pending = {key for point in points for _, key in cell(point, spacing)[0]}
    reusable = (previous.anchors if previous is not None and previous.revision == revision
                and previous.spacing == spacing else {})
    values = {key: (reusable[key] if key in reusable else evaluate(*(k * h for k, h in zip(key, spacing))))
              for key in sorted(pending)}
    return Snapshot(revision, spacing, MappingProxyType(values))


def force(w, v, coefficients):
    slip = [a - b for a, b in zip(w, v)]
    return tuple(c * r * abs(r) for c, r in zip(coefficients, slip))


def force_defect_limit(w0, w1, v, coefficients):
    # |a|a - |b|b has absolute magnitude at most |a-b|(|a|+|b|).
    return math.hypot(*(c * abs(a - b) * (abs(a - g) + abs(b - g))
                        for a, b, g, c in zip(w0, w1, v, coefficients)))


def speed_bound_to_force_limit(delta, approximate, v, coefficients):
    # For measured/certified ||Wtrue-Wapprox|| <= delta in a fixed
    # orthonormal frame, |e_i|<=delta and ||e||<=delta. No true wind input.
    return delta * max(c * (2 * abs(a - g) + delta)
                       for a, g, c in zip(approximate, v, coefficients))


def distance(a, b):
    return math.hypot(a[0] - b[0], a[1] - b[1])


def workloads():
    rng = random.Random(101)
    bins = [30 * k for k in range(8)]  # eight epochs, game seconds
    local = [(rng.uniform(-2, 2), rng.uniform(-2, 2), bins[i % 8]) for i in range(64)]
    clustered = [(rng.uniform(-60, 60), rng.uniform(-40, 40), bins[i % 8]) for i in range(200)]
    # Adversarial wide domain; same object count, many more unique anchors.
    distributed = [(i * 80 + rng.uniform(-12, 12), i % 7 * 80 + rng.uniform(-12, 12), bins[i % 8])
                   for i in range(200)]
    return {"clustered": local + clustered, "distributed": local + distributed}


def main():
    passed = []

    def check(name, ok):
        if not ok:
            raise AssertionError(name)
        passed.append(name)

    point = (-5., 3., 400.)
    spacing = (20., 20., 900.)
    analytic = prepare([point], spacing, 1, lambda x, y, t: (x + 2 * y, .01 * t - y))
    check("signed_coordinates_and_game_time_weights", distance(analytic.sample(point, 1), (1, 1)) < 1e-12)
    check("generation_boundary_requires_new_snapshot", analytic.sample(point, 2) is None)
    missing = Snapshot(1, spacing, {})
    check("missing_anchor_is_not_still_air", missing.sample(point, 1) is None)
    def forbidden(*_):
        raise AssertionError("warm_cell_calls_weather")

    warm = prepare([(-4., 5., 500.)], spacing, 1, forbidden, previous=analytic)
    check("warm_cell_reuses_nodes_not_weights", distance(warm.sample((-4., 5., 500.), 1), (6, 0)) < 1e-12)
    revised = prepare([point], spacing, 2, lambda *_: (30, 2), previous=analytic)
    check("new_revision_cannot_reuse_old_anchors",
          distance(revised.sample(point, 2), (30, 2)) < 1e-12
          and distance(analytic.sample(point, 1), (1, 1)) < 1e-12)

    receipt = []
    fixed_coefficients = (.0002, .0008)  # Synthetic acceleration coefficients.
    for case, points in workloads().items():
        for h in ((10., 10., 300.), (20., 20., 900.), (40., 40., 1800.)):
            snap = prepare(points, h, 3, wind)
            reordered = prepare(reversed(points), h, 3, wind)
            for p in points:
                assert snap.sample(p, 3) == reordered.sample(p, 3), "query_order_invariant"
            bound = proposed_bound(h)
            wind_errors, force_errors = [], []
            for i, p in enumerate(points):
                real, interp = wind(*p), snap.sample(p, 3)
                error = distance(real, interp)
                assert error <= bound + 1e-12, "analytic_wind_error_bound"
                v = (4 * math.sin(i), 3 * math.cos(i))
                defect = distance(force(real, v, fixed_coefficients), force(interp, v, fixed_coefficients))
                assert defect <= force_defect_limit(real, interp, v, fixed_coefficients) + 1e-12
                assert defect <= speed_bound_to_force_limit(bound, interp, v, fixed_coefficients) + 1e-12
                wind_errors.append(error)
                force_errors.append(defect)
            receipt.append(dict(workload=case, spacing=h, objects=len(points),
                                cold_anchors=len(snap.anchors), bound_mps=bound,
                                max_wind_error_mps=max(wind_errors),
                                max_acceleration_error_mps2=max(force_errors)))
    check("node_reuse_order_and_error_bounds_across_six_workloads", len(receipt) == 6)
    check("cold_grid_can_cost_more_than_direct_queries",
          any(r["cold_anchors"] > r["objects"] for r in receipt if r["workload"] == "distributed"))
    check("clustered_grid_can_cost_less_than_direct_queries",
          any(r["cold_anchors"] < r["objects"] for r in receipt if r["workload"] == "clustered"))
    check("identical_vector_error_is_not_identical_force_error",
          distance(force((10, 0), (2, 0), fixed_coefficients), force((11, 0), (2, 0), fixed_coefficients))
          > distance(force((0, 0), (2, 0), fixed_coefficients), force((1, 0), (2, 0), fixed_coefficients)))
    check("interpolate_wind_before_quadratic_force",
          math.isclose(force((10, 0), (0, 0), fixed_coefficients)[0], .02)
          and math.isclose((force((0, 0), (0, 0), fixed_coefficients)[0]
                            + force((20, 0), (0, 0), fixed_coefficients)[0]) / 2, .04))
    check("complete_immersion_has_no_direct_wind_error",
          force((40, -40), (1, 2), (0, 0)) == force((-40, 40), (1, 2), (0, 0)))

    # A direction reversal can be hidden from both anchors and midpoints.
    period = 1200.
    gust = lambda t: (12 * math.cos(4 * math.pi * t / period), 0)
    aliased = prepare([(0., 0., 0.)], (20., 20., period), 2, lambda x, y, t: gust(t))
    mid = aliased.sample((0., 0., period / 2), 2)
    quarter = aliased.sample((0., 0., period / 4), 2)
    check("temporal_midpoint_can_alias_direction_reversal",
          distance(mid, gust(period / 2)) < 1e-12 and distance(quarter, gust(period / 4)) > 23.99)
    print(json.dumps(dict(scope=__doc__, passed=len(passed), checks=passed, workloads=receipt), indent=2))


if __name__ == "__main__":
    main()
