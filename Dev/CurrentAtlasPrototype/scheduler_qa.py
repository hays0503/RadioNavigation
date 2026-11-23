"""Synthetic budgeted publication; no Unity, physical forces, or wall-time timings."""
from dataclasses import dataclass, replace
import json


@dataclass(frozen=True)
class Group:
    x: int
    speed: int
    line: int | None = None


class Barrier:
    """All fixture groups are due at each epoch. Work is staged across frames."""

    def __init__(self, groups):
        self.groups = tuple(groups)
        self.time = 0
        self.events = ()
        self._running = False

    def start(self, request, window):
        if self._running or request < self.time or window < 1:
            raise ValueError("invalid or overlapping request")
        self._running = True
        self.job = self._advance(request, window)

    def pump(self, budget):
        if not self._running or budget < 0:
            raise ValueError("invalid frame")
        work = 0
        while work < budget:
            try:
                next(self.job)
            except StopIteration:
                self._running = False
                break
            work += 1
        return work

    def _advance(self, request, window):
        while self.time < request:
            end = min(request, self.time + window)
            for g in self.groups:
                if g.line is not None and g.x < g.line and g.speed > 0:
                    # Integral game ticks: first tick on or beyond the line.
                    hit = self.time + (g.line - g.x + g.speed - 1) // g.speed
                    end = min(end, hit)
                yield

            staged = []
            for g in self.groups:
                staged.append(replace(g, x=g.x + g.speed * (end - self.time)))
                yield

            pending = []
            # Stable group order. Re-read staged state after an earlier effect.
            # Fixture effects can change speed, never position or activate a same-tick rule.
            for i in range(len(staged)):
                g = staged[i]
                if g.line is not None and g.x >= g.line:
                    staged[i] = replace(g, line=None)
                    pending.append((end, i))
                    if i == 0:
                        staged[1] = replace(staged[1], speed=1)
                yield

            # Tiny fixture: the tuple copies are not a bounded runtime design.
            self.time, self.groups, self.events = end, tuple(staged), self.events + tuple(pending)
            yield


def run(groups, requested, window, budgets):
    sim = Barrier(groups)
    sim.start(requested, window)
    used, publications = [], []
    for frame in range(10_000):
        before = sim.time, sim.groups, sim.events
        limit = budgets[frame % len(budgets)]
        work = sim.pump(limit)
        used.append(work)
        assert work <= limit
        if sim.time == before[0]:
            assert (sim.groups, sim.events) == before[1:], "partial state leaked across frames"
        else:
            publications.append((sim.time, sim.groups, sim.events))
        if not sim._running:
            break
    assert sim.time == requested and not sim._running
    return publications, used


def main():
    passed = []

    def check(name, ok):
        if not ok:
            raise AssertionError(name)
        passed.append(name)

    # Group 0 hits at t=3 and slows group 1 (which otherwise hits at t=5).
    # Group 1 is at x=6 at t=3, then reaches x=10 at t=7. Group 2 also hits t=3.
    groups = (Group(0, 1, 3), Group(0, 2, 10), Group(3, 1, 6))
    sliced, used = run(groups, 9, 9, (0, 1, 2))
    full, _ = run(groups, 9, 9, (100,))
    narrow, _ = run(groups, 9, 2, (1,))
    t, state, events = sliced[-1]
    check("frame_budget_changes_latency_not_result", sliced[-1] == full[-1])
    check("scan_window_changes_intermediate_publications_not_result", narrow[-1] == sliced[-1])
    check("earliest_barrier_cuts_tentative_interval", [p[0] for p in sliced] == [3, 7, 9])
    check("same_epoch_order_and_future_candidate_recomputed_after_event",
          events == ((3, 0), (3, 2), (7, 1)))
    check("final_positions_keep_pre_event_and_post_event_motion", t == 9
          and tuple(g.x for g in state) == (9, 12, 12))
    check("event_epoch_publishes_all_groups_and_events_together",
          tuple(g.x for g in sliced[0][1]) == (3, 6, 6)
          and sliced[0][2] == ((3, 0), (3, 2)))
    check("remainder_is_exactly_covered_after_event", sum(b - a for a, b in
          zip((0, 3, 7), [p[0] for p in sliced])) == 9)

    sim = Barrier(groups)
    sim.start(9, 9)
    assert sim.pump(9) == 9  # Three scans, three moves, three staged event checks.
    check("private_events_and_positions_not_visible_before_publish",
          sim.time == 0 and sim.groups == groups and sim.events == ())
    check("zero_budget_does_not_publish", sim.pump(0) == 0 and sim.time == 0)
    try:
        sim.start(9, 9)
    except ValueError:
        check("duplicate_active_request_is_rejected", True)
    else:
        check("duplicate_active_request_is_rejected", False)
    assert sim.pump(1) == 1
    check("retry_continues_staged_work_not_restart", sim.time == 3
          and sim.events == ((3, 0), (3, 2)))

    # 200 due groups with a declared number of work units, not a frame-time claim.
    stress = tuple(Group(0, 1, 3) for _ in range(200))
    stress_publications, stress_used = run(stress, 9, 9, (32,))
    check("stress_caps_work_and_finishes_without_event_loss",
          max(stress_used) == 32 and sum(stress_used) == 1202
          and stress_publications[-1][2] == tuple((3, i) for i in range(200)))
    print(json.dumps(dict(scope=__doc__, passed=len(passed), checks=passed,
                          stress_groups=len(stress), stress_work=sum(stress_used),
                          stress_pumps=len(stress_used), stress_cap=32), indent=2))


if __name__ == "__main__":
    main()
