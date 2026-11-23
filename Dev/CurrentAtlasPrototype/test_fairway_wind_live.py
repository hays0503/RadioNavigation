import unittest

from run_fairway_wind_live import evidence


def passage(air=80, held_air=80):
    instruments = dict(tc=1, depthM=-3, weather=True, windEastMps=-3, windNorthMps=2,
                       covered=True, waterEastMps=0.02, waterNorthMps=0)
    return (dict(docked=True, entry_km=35, held_move_m=0, wall_seconds=240,
                 heldDrift=[f"docked=True waterSteps=50 airSteps={n}" for n in (air, held_air)]),
            [dict(docked=False, distance=0.6, instruments=instruments)])


class FairwayWindVerdict(unittest.TestCase):
    def test_wind_and_water_are_separate_modes(self):
        self.assertEqual(evidence(*passage(air=0, held_air=0), False)["status"], "PASS")
        self.assertEqual(evidence(*passage(), True)["status"], "PASS")
        self.assertFalse(evidence(*passage(air=0, held_air=0), True)["checks"]["directWindMode"])

    def test_dock_does_not_count_running_patches_as_off(self):
        result = evidence(*passage(air=8, held_air=9), True)
        self.assertEqual(result["status"], "FAIL")
        self.assertFalse(result["checks"]["noDriftStepsWhileHeld"])

    def test_actual_tc_and_sample_coverage_are_required(self):
        result, rows = passage()
        rows[0]["instruments"].update(tc=140, covered=False)
        receipt = evidence(result, rows, True)
        self.assertFalse(receipt["checks"]["nearBerthAtTc1"])
        self.assertFalse(receipt["checks"]["constantWaterAvailable"])


if __name__ == "__main__":
    unittest.main()
