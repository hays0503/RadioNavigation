"""Offline verification of the P1 receipt verdict, not game evidence."""
from copy import deepcopy
from io import BytesIO
from types import SimpleNamespace
import unittest

from run_handoff_live import POLICY, WATER_MPS, choose_target, evaluate, fixture, freeze, jump


def recording(feature=False, spawn_water=None, far_water=None):
    speed = WATER_MPS[0] if feature else 0
    spawn_east = speed if spawn_water is None else spawn_water
    applied_east = speed if far_water is None else far_water
    far = "NotSpawned"
    scene = "Spawned"
    rows = []

    def emit(phase, state, t, fixed, east, east_speed, group_epoch=None):
        rows.append(dict(phase=phase, state=state, storyTicks=t * 10_000_000, fixedTick=fixed,
                         tc=1,
                         groupTicks=(t if group_epoch is None else group_epoch) * 10_000_000,
                         eastKm=east / 1000, northKm=t * 4 / 1000,
                         eastMps=east_speed, northMps=4, hasSceneBody=state == scene,
                         isKinematic=False, stockEastMps=0, stockNorthMps=4,
                         farBodyEastMps=speed, farBodyNorthMps=4,
                         covered=feature, eastWaterMps=speed, northWaterMps=0,
                         routeCount=1, groupEastKm=east / 1000, groupNorthKm=t * 4 / 1000))

    emit("far.before", far, 1, 0, 0, speed)
    emit("scene.native", scene, 1, 0, 0, spawn_east)
    for fixed in range(1, 5):
        emit("scene.fixed", scene, 1, fixed, 0, spawn_east)
    emit("scene.before", scene, 9, 10, 0, speed)
    emit("far.release", "Unspawning", 9, 10, 0, speed)
    emit("far.initial", far, 9, 10, 0, speed)
    emit("far.step.before", far, 9, 10, 0, speed)
    emit("far.step.after", far, 13, 10, applied_east * 4, speed)
    return {
        "complete": True, "enteredNativeArea": True, "leftNativeArea": True,
        "armedStoryTicks": 0, "target": {"name": "Liberty #1", "type": "Liberty", "prefab": "Liberty",
                                         "east": 0, "north": 0, "playerEast": 0, "playerNorth": 0},
        "rows": rows,
    }


class HandoffLiveVerdict(unittest.TestCase):
    def setUp(self):
        self.controls = [recording() for _ in range(3)]
        self.frozen = freeze(self.controls, POLICY)

    def test_consistent_world_motion_passes_without_claiming_save_load(self):
        receipt = evaluate(recording(feature=True), self.frozen, POLICY)
        self.assertEqual(receipt["status"], "PASS")
        self.assertEqual(receipt["not_run"], ["saveLoadAtBoundary"])

    def test_lost_or_doubled_water_fails_at_spawn(self):
        for windless_east in (0, 2 * WATER_MPS[0]):
            with self.subTest(windless_east=windless_east):
                receipt = evaluate(recording(feature=True, spawn_water=windless_east), self.frozen, POLICY)
                self.assertEqual(receipt["outcomes"]["farToScene"]["velocityMps"]["status"], "FAIL")

    def test_short_or_noisy_window_is_not_a_pass(self):
        limits = deepcopy(self.frozen)
        limits["farLimitM"] = 4
        limits["limits"]["farToScene"]["velocityMps"]["limit"] = 0.4
        receipt = evaluate(recording(feature=True), limits, POLICY)
        self.assertEqual(receipt["status"], "NOT RUN")
        self.assertEqual(receipt["outcomes"]["farIntervals"]["status"], "NOT RUN")

    def test_zero_stock_impostor_body_is_not_the_native_route_speed(self):
        for control in self.controls:
            row = control["rows"][0]
            row["state"] = "SpawnedImpostors"
            row["farBodyNorthMps"] = row["northMps"] = 0
        frozen = freeze(self.controls, POLICY)
        self.assertEqual(frozen["controls"][0]["farToSceneRawBody"]["velocityMps"], [0, 4])
        self.assertEqual(evaluate(recording(feature=True), frozen, POLICY)["status"], "PASS")

    def test_far_position_counted_once_and_far_body_validated_independently(self):
        for applied in (0, 2 * WATER_MPS[0]):
            receipt = evaluate(recording(feature=True, far_water=applied), self.frozen, POLICY)
            self.assertEqual(receipt["outcomes"]["farIntervals"]["status"], "FAIL")
        trace = recording(feature=True)
        trace["rows"][0]["farBodyEastMps"] = 0
        receipt = evaluate(trace, self.frozen, POLICY)
        self.assertEqual(receipt["outcomes"]["preSpawnBodyResidualMps"]["status"], "FAIL")

    def test_forced_or_partial_transition_rejected(self):
        for corruption in ("leftNativeArea", "complete"):
            trace = recording(feature=True)
            trace[corruption] = False
            with self.assertRaises(ValueError):
                evaluate(trace, self.frozen, POLICY)
        trace = recording(feature=True)
        del trace["rows"][3]
        with self.assertRaises(ValueError):
            evaluate(trace, self.frozen, POLICY)

    def test_phase_identity_and_origin(self):
        self.controls[1]["armedStoryTicks"] = 60 * 10_000_000
        with self.assertRaisesRegex(ValueError, "epochs"):
            freeze(self.controls, POLICY)
        trace = recording(feature=True)
        trace["rows"][9]["groupTicks"] = 0
        with self.assertRaisesRegex(ValueError, "ownership"):
            evaluate(trace, self.frozen, POLICY)

    def test_equal_magnitudes_do_not_hide_direction_change(self):
        a = dict(eastKm=0, northKm=0, eastMps=2, northMps=0)
        b = dict(eastKm=0.002, northKm=0, eastMps=-2, northMps=0)
        self.assertEqual(jump(a, b, 1)["positionM"], [0, 0])
        self.assertEqual(jump(a, b, 1)["velocityMps"], [-4, 0])

    def test_bridge_invocation_uses_post(self):
        def open_request(request, timeout):
            self.assertEqual(request.get_method(), "POST")
            self.assertIn("method=Arm&args=%5B17%5D", request.full_url)
            return BytesIO(b'{"ok":true,"value":"armed"}')

        bridge = SimpleNamespace(BOOT="http://localhost:28777",
                                 HTTP=SimpleNamespace(open=open_request))
        self.assertEqual(fixture(bridge, "Arm", 17), "armed")

    def test_duplicate_generic_vessel_names_need_coordinates(self):
        first = {"name": "Liberty", "east": -1896, "north": 5276}
        second = {"name": "Liberty", "east": -2424, "north": 5722}
        with self.assertRaisesRegex(ValueError, "found 2"):
            choose_target([first, second], "Liberty", None)
        self.assertEqual(choose_target([first, second], "Liberty", (-1896.2, 5276.0)), first)


if __name__ == "__main__":
    unittest.main()
