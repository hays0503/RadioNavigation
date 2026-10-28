import importlib.util
import math
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import audit


class SolarBenchTests(unittest.TestCase):
    def setUp(self):
        self.fixture = audit.read_json(audit.FVIII_PATH)
        self.sight = {
            "gmt": "1939-09-02T10:03:07Z", "raw_hs_deg": 38.0,
            "index_correction_arcmin": 0.0, "index_correction_source": "test, explicitly known zero",
            "limb": "lower", "eye_height_m": 5.5, "horizon_reference": "test sea horizon, unverified optics",
            "assumed_lat_deg": 55.0, "assumed_lon_deg": 5.75,
            "table": {"gha_deg": 330.0, "declination_deg": 8.0,
                      "gmt": "1939-09-02T10:03:07Z", "source": "independent synthetic almanac place"}}

    def test_verified_38_degree_cells_and_month_are_not_double_sd(self):
        for height, expected in ((5, 10.9), (6, 10.5), (5.5, 10.7)):
            with self.subTest(height=height):
                result = audit.fviii(38, height, 9, self.fixture)
                self.assertAlmostEqual(expected, result["base_arcmin"])
                self.assertAlmostEqual(-0.1, result["monthly_arcmin"])
        chain = audit.reduce_sight(self.sight, self.fixture)["chain"]
        self.assertAlmostEqual(10.6, chain["total_fviii_arcmin"])
        self.assertAlmostEqual(38 + 10.6 / 60, chain["centre_ho_deg"])
        self.assertAlmostEqual((chain["centre_ho_deg"] - chain["centre_hc_deg"]) * 60, chain["intercept_nm"])

    def test_monthly_terms_include_all_twelve_printed_values(self):
        expected = [0.3, 0.2, 0.1, 0, -0.2, -0.2, -0.2, -0.2, -0.1, 0.1, 0.2, 0.3]
        for month, value in enumerate(expected, 1):
            self.assertEqual(value, audit.fviii(38, 5.5, month, self.fixture)["monthly_arcmin"])

    def test_fixture_boundary_cells_are_independent_printed_literals(self):
        # Catches transcription errors outside the original 38-degree sight.
        for altitude, height, base in ((30, 0, 14.5), (30, 14, 7.8),
                                      (45, 5, 11.2), (55, 6, 11.1),
                                      (60, 4, 12.0), (60, 11, 9.6), (60, 14, 8.9)):
            with self.subTest(altitude=altitude, height=height):
                self.assertEqual(base, audit.fviii(altitude, height, 4, self.fixture)["base_arcmin"])

    def test_bilinear_interpolation_retains_four_source_cells(self):
        result = audit.fviii(37, 5.5, 9, self.fixture)
        self.assertAlmostEqual(10.65, result["base_arcmin"])
        self.assertEqual(4, len(result["cells"]))
        self.assertEqual(0.5, result["altitude_weight"])
        self.assertEqual(0.5, result["height_weight"])

    def test_no_extrapolation_or_upper_limb_fallback(self):
        for h, eye in ((29.999, 5.5), (60.001, 5.5), (38, -0.01), (38, 14.01)):
            with self.subTest(h=h, eye=eye), self.assertRaises(ValueError):
                audit.fviii(h, eye, 9, self.fixture)
        self.sight["limb"] = "upper"
        with self.assertRaises(ValueError):
            audit.reduce_sight(self.sight, self.fixture)

    def test_additive_index_sign_and_fviii_uses_corrected_h(self):
        self.sight["raw_hs_deg"] = 37 + 59 / 60
        self.sight["index_correction_arcmin"] = 1.0
        chain = audit.reduce_sight(self.sight, self.fixture)["chain"]
        self.assertAlmostEqual(38, chain["index_corrected_h_deg"])
        self.assertAlmostEqual(10.6, chain["total_fviii_arcmin"])
        self.sight["index_correction_arcmin"] = -1.0
        chain = audit.reduce_sight(self.sight, self.fixture)["chain"]
        self.assertAlmostEqual(37 + 58 / 60, chain["index_corrected_h_deg"])

    def test_rejects_duplicate_and_hidden_correction_fields(self):
        for key in ("sd_arcmin", "dip_arcmin", "refraction_arcmin", "sundrift_minutes", "ho_deg"):
            with self.subTest(key=key):
                sight = dict(self.sight, **{key: 1})
                with self.assertRaises(ValueError):
                    audit.reduce_sight(sight, self.fixture)

    def test_manual_correction_is_explicit_and_can_cover_other_altitudes(self):
        self.sight["raw_hs_deg"] = 20.0
        self.sight["manual_fviii"] = {"base_arcmin": 9.5, "monthly_arcmin": -0.1,
                                      "source": "F VIII PDF p.98, 20 degrees / 5m (illustration)",
                                      "includes_sd_dip_refraction": True}
        chain = audit.reduce_sight(self.sight, self.fixture)["chain"]
        self.assertAlmostEqual(20 + 9.4 / 60, chain["centre_ho_deg"])
        self.sight["manual_fviii"]["includes_sd_dip_refraction"] = False
        with self.assertRaises(ValueError):
            audit.reduce_sight(self.sight, self.fixture)

    def test_rejects_unknown_index_or_horizon_and_wrong_gmt(self):
        for key in ("index_correction_source", "horizon_reference"):
            bad = dict(self.sight, **{key: ""})
            with self.subTest(key=key), self.assertRaises(ValueError):
                audit.reduce_sight(bad, self.fixture)
        self.sight["table"]["gmt"] = "1939-09-02T11:03:07Z"
        with self.assertRaises(ValueError):
            audit.reduce_sight(self.sight, self.fixture)
        for value in ("1939-09-02T10:03:07", "1939-09-02T10:03:07+01:00", "2026-01-01T00:00:00Z"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                audit.utc_time(value)

    def test_nonfinite_or_boolean_numbers_are_not_zeroes(self):
        for value in (None, True, float("nan"), float("inf"), "38.0"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                audit.reduce_sight(dict(self.sight, raw_hs_deg=value), self.fixture)

    def test_geocentric_hc_and_azimuth_use_east_positive_longitude(self):
        for lat, lon, gha, dec, expected_hc, expected_az in (
                (55, 0, 0, 0, 35, 180), (50, 0, 0, 80, 60, 0),
                (0, -90, 0, 0, 0, 90), (0, 90, 0, 0, 0, 270)):
            with self.subTest(lon=lon, dec=dec):
                hc, az = audit.table_place(lat, lon, gha, dec)
                self.assertAlmostEqual(expected_hc, hc)
                self.assertAlmostEqual(expected_az, az)
        hc, az = audit.table_place(0, 0, 0, 0)
        self.assertAlmostEqual(90, hc)
        self.assertIsNone(az)  # azimuth is undefined at zenith.

    def test_interpolation_wraps_gha_at_zero_not_via_180(self):
        rows = [{"utc": "1939-09-02T08:00:00Z", "gha_deg": 359, "declination_deg": 8},
                {"utc": "1939-09-02T12:00:00Z", "gha_deg": 59, "declination_deg": 7}]
        tables = {"rows": rows, "source": "synthetic"}
        result = audit.interpolated_place(tables, audit.utc_time("1939-09-02T10:00:00Z"))
        self.assertAlmostEqual(29, result["gha_deg"])
        self.assertAlmostEqual(7.5, result["declination_deg"])
        with self.assertRaises(ValueError):
            audit.interpolated_place(tables, audit.utc_time("1939-09-02T13:00:00Z"))
        with self.assertRaises(ValueError):
            audit.reduce_sight(self.sight, self.fixture, tables)

    def test_signed_zero_declination_is_preserved(self):
        self.assertAlmostEqual(-0.5, audit.printed_angle("-0", "30,0"))
        self.assertAlmostEqual(-0.5, audit.printed_angle("−0", "30,0"))
        self.assertAlmostEqual(0.5, audit.printed_angle("+0", "30,0"))
        with self.assertRaises(ValueError):
            audit.printed_angle("1", "60,1")

    def test_exact_printed_sixty_minutes_carries_without_changing_source_tokens(self):
        self.assertEqual(59, audit.printed_angle("58", "60,0"))
        self.assertEqual(-1, audit.printed_angle("-0", "60,0"))

    @staticmethod
    def fake_page():
        labels = [str(d) + "-Sep" for d in range(1, 9)]
        regular = "declination of the Sun " + " ".join(reversed(labels))
        layout = "1939 1940 1941 1942 1943 1944 1945\n" + "\n".join(
            str(i % 6 * 4) + " " + " ".join(str(i + year) + " 1,2 +8 3,4" for year in range(7))
            for i in range(48))
        return layout, regular

    def test_parser_is_anchored_to_chronology_not_rotated_label_order(self):
        layout, regular = self.fake_page()
        result = audit.parse_solar_page(layout, regular, 57, "1940-09-01")
        self.assertEqual(336, len(result["rows"]))
        self.assertEqual("1939-09-01T00:00:00Z", result["rows"][0]["utc"])
        self.assertAlmostEqual(0.02, result["rows"][0]["gha_deg"])
        self.assertEqual("1945-09-08T20:00:00Z", result["rows"][-1]["utc"])
        self.assertIsNone(result["rows"][0]["sd_arcmin"])
        with self.assertRaises(ValueError):
            audit.parse_solar_page(layout, regular, 57, "1940-09-02")

    def test_parser_rejects_missing_reordered_rows_and_year_columns(self):
        layout, regular = self.fake_page()
        for invalid in ("\n".join(layout.splitlines()[:-1]),
                        layout.replace("\n4 ", "\n8 ", 1), layout.replace("1939 1940", "1940 1939", 1)):
            with self.subTest(invalid=invalid[:60]), self.assertRaises(ValueError):
                audit.parse_solar_page(invalid, regular, 57, "1940-09-01")

    def test_parser_rejects_year_wrap_and_missing_leap_day(self):
        layout, regular = self.fake_page()
        with self.assertRaisesRegex(ValueError, "Year-wrap"):
            audit.parse_solar_page(layout, regular, 72, "1940-12-26")
        regular = "declination of the Sun 26-Feb 27-Feb 28-Feb 29-Feb 1-Mar 2-Mar 3-Mar 4-Mar"
        with self.assertRaisesRegex(ValueError, "Leap-day"):
            audit.parse_solar_page(layout, regular, 34, "1940-02-26")

    def test_merge_rejects_overlap_and_retains_page_receipts(self):
        first = {"source": {"sha256": audit.ALMANAC_HASH}, "pdf_page": 27,
                 "first_date_anchor": "1940-01-01", "printed_time_headers": ["Time = GMT-1"],
                 "rows": [{"utc": "1939-01-01T00:00:00Z"}]}
        second = dict(first, pdf_page=57, first_date_anchor="1940-08-28",
                      rows=[{"utc": "1939-08-28T00:00:00Z"}])
        with patch("audit.load_tables", side_effect=[first, second]), patch("audit.sha256", return_value="test-hash"):
            merged = audit.merge_tables([Path("one.json"), Path("two.json")])
            self.assertEqual(2, len(merged["rows"]))
            self.assertEqual([27, 57], [page["pdf_page"] for page in merged["pages"]])
            self.assertEqual("test-hash", merged["pages"][0]["input_sha256"])
        with patch("audit.load_tables", return_value=first), self.assertRaisesRegex(ValueError, "Overlapping"):
            audit.merge_tables([Path("one.json"), Path("same.json")])

    def test_missing_days_are_not_interpolated_across(self):
        tables = {"source": "synthetic", "rows": [
            {"utc": "1939-01-01T00:00:00Z", "gha_deg": 179, "declination_deg": -22},
            {"utc": "1939-08-28T00:00:00Z", "gha_deg": 179, "declination_deg": 10}]}
        with self.assertRaisesRegex(ValueError, "not four hours"):
            audit.interpolated_place(tables, audit.utc_time("1939-05-01T00:00:00Z"))

    def test_projection_is_full_diameter_and_inverts_the_manual_measurement(self):
        result = audit.projection(0.53, 65, 1000)
        expected = 1000 * math.tan(math.radians(0.53 / 2)) / math.tan(math.radians(65 / 2))
        self.assertAlmostEqual(expected, result["expected_on_axis_diameter_px"])
        recovered = audit.projection(0.53, 65, 1000, expected)
        self.assertAlmostEqual(0.53, recovered["observed_angular_diameter_deg"])

    def test_report_output_cannot_touch_originals_sources_or_inputs(self):
        for path in (audit.REFERENCES / "original.json", audit.ROOT / "Source/test.json", audit.ROOT / "outside.json"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                audit.report_path(path)
        output = audit.ARTIFACTS / "reports/solar-sight/test.json"
        self.assertEqual(output.resolve(), audit.report_path(output))
        with self.assertRaises(ValueError):
            audit.report_path(output, [output])

    def test_snapshot_routes_are_get_only_and_do_not_change_a_series(self):
        routes = []
        def getter(base, route, timeout):
            routes.append(route)
            if route == "/sextant":
                return {"ok": True, "loaded": True,
                        "solarAudit": self.configured_snapshot()["records"][0]["data"]["solarAudit"]}
            return {"ok": True}
        result = audit.capture_snapshot("http://127.0.0.1:28777", 1, getter)
        self.assertEqual(["/time", "/sextant", "/sky", "/state?fields=position,time", "/time"], routes)
        self.assertEqual("captured-configuration", result["status"])

    def test_missing_bridge_and_legacy_probe_are_not_accepted(self):
        routes = []
        def unavailable(base, route, timeout):
            routes.append(route)
            raise ConnectionRefusedError("test offline")
        result = audit.capture_snapshot("http://localhost:28777", 1, unavailable)
        self.assertEqual("unavailable", result["status"])
        self.assertEqual(["/time"], routes)
        result = audit.capture_snapshot("http://localhost:28777", 1, lambda *args: {"ok": True, "loaded": True})
        self.assertEqual("incomplete", result["status"])
        result = audit.capture_snapshot("http://localhost:28777", 1, lambda *args: {
            "ok": True, "loaded": True, "solarAudit": {"ok": True, "schema": 1, "campaignUtc": True,
                                                       "position": None, "uncalibrated": None, "calibrated": None}})
        self.assertEqual("incomplete", result["status"])
        for base in ("https://example.com", "http://example.com", "http://localhost:28777/sextant/set", "http://a:b@localhost:28777"):
            with self.subTest(base=base), self.assertRaises(ValueError):
                audit.local_bridge_url(base)

    @staticmethod
    def configured_snapshot():
        # Deliberately different AP and live position. Altitudes are independent
        # fixture values, not computed by the reduction being tested.
        sample = {"schema": 1, "ok": True, "campaignUtc": True,
                  "utc": "1939-09-02T10:03:07Z", "rawHsDeg": 38,
                  "indexCorrectionArcMin": 0, "position": {"latDeg": 0, "lonDeg": 0},
                  "uncalibrated": {"altDeg": 62, "ghaDeg": 330, "decDeg": 8},
                  "calibrated": {"altDeg": 60, "ghaDeg": 329, "decDeg": 8.1},
                  "nativeSun": {"altDeg": 61, "azDeg": 75},
                  "measuringSun": {"ready": True, "active": True, "configuredDiameterDeg": 0.53,
                                   "configuredDirection": {"altDeg": 61, "azDeg": 75}}}
        return {"schema": 1, "status": "captured-configuration", "records": [
            {"route": "/sextant", "data": {"solarAudit": sample}}]}

    def test_residuals_keep_live_position_ap_and_time_calibration_separate(self):
        reduced = audit.reduce_sight(self.sight, self.fixture)
        result = audit.residual_report(self.configured_snapshot(), reduced)
        metrics = result["metrics"]
        self.assertEqual(-120, metrics["calibration_alt_shift_arcmin"])
        self.assertEqual(0, metrics["configured_minus_native_alt_arcmin"])
        self.assertAlmostEqual(60.735, metrics["configured_geometric_lower_limb_alt_deg"])
        self.assertAlmostEqual((38 - 60.735) * 60, metrics["raw_hs_minus_geometric_limb_arcmin"])
        live = metrics["table_centre_at_live_position"]["alt_deg"]
        self.assertGreater(abs(live - reduced["chain"]["centre_hc_deg"]), 10)
        self.assertAlmostEqual((62 - live) * 60, metrics["uncalibrated_minus_table_alt_arcmin"])
        self.assertAlmostEqual((60 - live) * 60, metrics["calibrated_minus_table_alt_arcmin"])
        self.assertEqual("configuration-residuals-not-an-accepted-sight", result["status"])

    def test_residuals_refuse_later_time_or_different_index_and_raw_arc(self):
        reduced = audit.reduce_sight(self.sight, self.fixture)
        for key, value in (("utc", "1939-09-02T11:03:07Z"), ("rawHsDeg", 37), ("indexCorrectionArcMin", 1)):
            snapshot = self.configured_snapshot()
            snapshot["records"][0]["data"]["solarAudit"][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                audit.residual_report(snapshot, reduced)
        with self.assertRaises(ValueError):
            audit.residual_report({"schema": 1, "status": "unavailable"}, reduced)

    def test_no_limb_prediction_from_an_inactive_disc_or_missing_directions(self):
        snapshot = self.configured_snapshot()
        source = snapshot["records"][0]["data"]["solarAudit"]
        source["measuringSun"]["ready"] = False
        source["nativeSun"] = None
        result = audit.residual_report(snapshot, audit.reduce_sight(self.sight, self.fixture))
        self.assertNotIn("configured_geometric_lower_limb_alt_deg", result["metrics"])
        self.assertIsNone(result["metrics"]["native_minus_table_alt_arcmin"])

    def test_bad_bridge_reply_is_unavailable_not_a_successful_snapshot(self):
        result = audit.capture_snapshot("http://localhost:28777", 1, lambda *args: [])
        self.assertEqual("unavailable", result["status"])


class OriginalPdfIntegrationTests(unittest.TestCase):
    def test_actual_solar_page_retains_checked_september_second_cells(self):
        if not importlib.util.find_spec("pypdf") or not (audit.REFERENCES / audit.ALMANAC_NAME).exists():
            self.skipTest("Original PDF/pypdf unavailable; run tables command locally for integration acceptance")
        args = audit.parser().parse_args(["tables"])
        result = audit.extract_tables(args)
        self.assertEqual(336, len(result["rows"]))
        row = next(r for r in result["rows"] if r["utc"] == "1939-09-02T08:00:00Z")
        # Independent literals from the user's PDF, not generated expectations.
        self.assertEqual(["300", "1,1", "+8", "11,1"], row["printed_tokens"])
        self.assertAlmostEqual(300 + 1.1 / 60, row["gha_deg"])
        self.assertAlmostEqual(8 + 11.1 / 60, row["declination_deg"])
        self.assertEqual(15.9, row["sd_arcmin"])
        self.assertEqual(audit.ALMANAC_HASH, result["source"]["sha256"])
        self.assertEqual(audit.sha256(audit.ROOT / audit.read_json(audit.FVIII_PATH)["source"]),
                         audit.read_json(audit.FVIII_PATH)["sha256"])


if __name__ == "__main__":
    unittest.main(testRunner=unittest.TextTestRunner(stream=sys.stdout, verbosity=2))
