"""Offline first game tick of a waypoint half-plane crossing for CurrentTrack2D."""
import json
import math
import random


TICKS = 10_000_000


def travel(ticks, start_speed, end_speed, tau):
    elapsed = ticks / TICKS
    ratio = elapsed / tau
    lost = ratio * (1 - ratio / 2) if ratio < 1e-4 else -math.expm1(-ratio)
    response = (elapsed * ratio * (0.5 - ratio / 6) if ratio < 1e-4
                else elapsed - tau * lost)
    return start_speed * elapsed + (end_speed - start_speed) * response


def crossing(remaining, start_speed, end_speed, tau, duration):
    """Native arrival uses negative remaining, not radial proximity or equality."""
    if remaining < 0:
        return 0
    upper = duration
    if start_speed > 0 > end_speed:
        peak = tau * math.log1p(start_speed / -end_speed) * TICKS
        if peak < upper:
            before, after = int(peak), min(upper, math.ceil(peak))
            upper = max((before, after), key=lambda tick: travel(tick, start_speed, end_speed, tau))
    if travel(upper, start_speed, end_speed, tau) <= remaining:
        return None
    lower = 0
    while lower + 1 < upper:
        middle = lower + (upper - lower) // 2
        if travel(middle, start_speed, end_speed, tau) > remaining:
            upper = middle
        else:
            lower = middle
    return upper


def main():
    assert crossing(-1, -1, -1, 1, 20 * TICKS) == 0
    assert crossing(2, 1, 1, 5, 2 * TICKS) is None
    assert crossing(2, 1, 1, 5, 2 * TICKS + 1) == 2 * TICKS + 1
    assert crossing(0.5, 2, -1, 1, 20 * TICKS) < 2 * TICKS
    assert crossing(0.901389, 2, -1, 1, 20 * TICKS) is None  # just beyond the turn-back peak
    assert crossing(0.1, -2, 1, 1, 20 * TICKS) > 2 * TICKS
    assert crossing(1, -2, -1, 1, 20 * TICKS) is None
    rng = random.Random(219)
    for _ in range(400):
        duration = rng.randint(1, 3000)
        args = (rng.uniform(-0.0001, 0.003), rng.uniform(-10, 10),
                rng.uniform(-10, 10), rng.uniform(0.00001, 0.001))
        remaining, initial, equilibrium, tau = args
        expected = next((t for t in range(duration + 1)
                         if travel(t, initial, equilibrium, tau) > remaining), None)
        assert crossing(*args, duration) == expected, (args, duration, expected)
    print(json.dumps(dict(passed=407, wall="not measured", scope=__doc__,
                          limit="known plane and constant equilibrium; not an event inventory or runtime clock hook")))


if __name__ == "__main__":
    main()
