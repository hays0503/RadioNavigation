from copy import deepcopy
import unittest

from run_wind_probe import bearings, consistent, layout_ok, overlay_matches, wind_value_matches


class WindLiveReceiptTests(unittest.TestCase):
    def test_coordinates_use_direction_of_motion_and_refuse_stationary_course(self):
        self.assertIsNone(bearings(0, 0))
        self.assertEqual(bearings(1, 0), 90)
        self.assertEqual(bearings(-1, 0), 270)

    def test_telemetry_checks_actual_bearing_and_depth_independent_of_pixels(self):
        probe = {"depth": ["-4.9"], "wind": ["-3,0"]}
        self.assertTrue(overlay_matches(probe, "node=RadioNavWindBearing:active=True:text=90° E@2450,1200"))
        self.assertFalse(overlay_matches(probe, "node=RadioNavWindBearing:active=True:text=270° W@2450,1200"))
        probe["lang"] = ["russian"]
        self.assertTrue(overlay_matches(probe, "node=RadioNavWindBearing:active=True:text=90° В@2450,1200"))
        self.assertFalse(overlay_matches(probe, "node=RadioNavWindBearing:active=True:text=90° E@2450,1200"))
        probe["depth"] = ["4.9"]
        self.assertTrue(overlay_matches(probe, "node=RadioNavWindBearing:active=False:text=90° В@2450,1200"))
        self.assertFalse(overlay_matches(probe, "node=RadioNavWindBearing:active=True:text=90° В@2450,1200"))

    def test_layout_rejects_overlap_overflow_and_duplicates(self):
        clean_panel = "\n".join([
            "group=Wind speed Icon@2320,1190:2300.0..2330.0,1175.0..1205.0:chain",
            "container=Parameters:2290.0..2480.0,1000.0..1238.0",
            "node=Wind speed:active=True:text=6 уз@2410,1190:bounds=2335.0..2375.0,1180.0..1200.0:rect=2335.0..2535.0,1180.0..1200.0",
            "node=RadioNavWindBearing:active=True:text=95° В@2420,1190:bounds=2382.0..2430.0,1180.0..1200.0:rect=2382.0..2430.0,1180.0..1200.0",
        ])
        self.assertTrue(layout_ok(clean_panel, True))

        overlap_panel = clean_panel.replace("2382.0..2430.0", "2360.0..2410.0")
        self.assertFalse(layout_ok(overlap_panel, True))

        overflow_panel = clean_panel.replace("2382.0..2430.0", "2450.0..2510.0")
        self.assertFalse(layout_ok(overflow_panel, True))

        dup_panel = clean_panel + "\nnode=RadioNavWindBearing:active=False:text=@0,0:bounds=0..0,0..0:rect=0..0,0..0"
        self.assertFalse(layout_ok(dup_panel, True))

    def test_wind_value_matches_units(self):
        self.assertTrue(wind_value_matches({"units": ["MetricWithKnots"], "nativeKnots": ["6.33"], "mapParsed": ["1:6 уз"]}))
        self.assertFalse(wind_value_matches({"units": ["MetricWithKnots"], "nativeKnots": ["6.33"], "mapParsed": ["1:4 knots"]}))
        self.assertTrue(wind_value_matches({"units": ["Metric"], "nativeKnots": ["6.33"], "mapParsed": ["1:12 км/ч"]}))

    def test_logical_state_brackets_observed_frame_but_does_not_claim_exact_frame(self):
        probe = {key: ["x"] for key in ("ticks", "depth", "ground", "heading", "wind", "knots", "map", "mapNative")}
        probe["frame"], probe["tc"] = ["11"], ["0"]
        before = {"probe": probe, "panel": "same"}
        after = deepcopy(before)
        after["probe"]["frame"] = ["15"]
        capture = {"frame": 12, "ok": True, "blackFrame": False, "includesOverlay": True}
        self.assertTrue(consistent(before, capture, after))
        capture["frame"] = 9
        self.assertFalse(consistent(before, capture, after))
        capture["frame"] = 12
        after["probe"]["ticks"] = ["moved"]
        self.assertFalse(consistent(before, capture, after))


if __name__ == "__main__":
    unittest.main()
