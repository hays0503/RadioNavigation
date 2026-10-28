"""Offline regressions for the sea-horizon detector and the model cross-check."""
import importlib.util
import json
import math
import sys
import tempfile
import unittest
from pathlib import Path

import numpy as np
from PIL import Image

TOOL = Path(__file__).resolve().parents[1] / "sea_horizon.py"
spec = importlib.util.spec_from_file_location("sea_horizon", TOOL)
sea = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sea)

WIDTH, HEIGHT, CENTRE = 2560, 1440, 720


def frame(horizon_y=728, sky=210.0, water=50.0, cloud_from=None, cloud_to=None,
          jitter=0.0, uniform=False, land=False, texture=3.0, seed=7):
    random = np.random.default_rng(seed)
    image = np.zeros((HEIGHT, WIDTH, 3))
    if uniform:
        image[:, :, :] = sky
        return image
    image[:, :, :] = sky
    waves = texture * random.normal(0, 1.0, (HEIGHT, WIDTH, 1))
    image[horizon_y:, :, :] = water
    for x in range(700, 1261):
        y = horizon_y
        if jitter:
            y += int(round(random.normal(0, jitter)))
        if land:
            image[y:, x, :] = 14.0  # opaque silhouette, no sea below it
            continue
        image[y:, x, :] = water + waves[y:, x, :]
    if cloud_from is not None:
        image[:, :cloud_to, :] = sky
        for x in range(cloud_from, cloud_to):
            image[horizon_y:, x, :] = water + waves[horizon_y:, x, :]
            image[:horizon_y, x, :] = 18.0  # dark wall, no sky-to-water step
    image += random.normal(0, 0.6, image.shape)
    return image


class DetectorTests(unittest.TestCase):
    def test_flat_sea_horizon_is_detected(self):
        result = sea.detect_sea_horizon(frame(), WIDTH, HEIGHT)
        self.assertTrue(result["edge_candidate_found"], result)
        self.assertFalse(result["sea_horizon_identity_verified"])
        self.assertFalse(result["contact_accepted"])
        self.assertAlmostEqual(result["edge_y_px"], 728.0, delta=1.0)
        self.assertGreaterEqual(result["segment_width_columns"], 400)

    def test_cloud_wall_part_of_the_beam_still_finds_the_sea(self):
        result = sea.detect_sea_horizon(frame(cloud_from=700, cloud_to=1080), WIDTH, HEIGHT)
        self.assertTrue(result["edge_candidate_found"], result)
        self.assertGreaterEqual(result["segment_x"][0], 1070)
        self.assertGreaterEqual(result["segment_width_columns"], 150)
        self.assertAlmostEqual(result["edge_y_px"], 728.0, delta=1.0)

    def test_uniform_sky_has_no_horizon(self):
        result = sea.detect_sea_horizon(frame(uniform=True), WIDTH, HEIGHT)
        self.assertFalse(result["edge_candidate_found"])
        self.assertIn("reason", result)

    def test_dark_mass_above_the_edge_is_not_a_sea_horizon(self):
        result = sea.detect_sea_horizon(frame(land=True), WIDTH, HEIGHT)
        self.assertFalse(result["edge_candidate_found"])
        self.assertIn("no-texture-below", result["reason"])

    def test_chaotic_edge_is_not_a_horizon(self):
        random = np.random.default_rng(3)
        image = np.zeros((HEIGHT, WIDTH, 3))
        image[:, :, :] = 210.0
        for x in range(700, 1261):
            y = 728 + int(random.integers(-14, 15))
            image[y:, x, :] = 50.0 + random.normal(0, 3.0, (HEIGHT - y, 3))
        image += random.normal(0, 0.6, image.shape)
        result = sea.detect_sea_horizon(image, WIDTH, HEIGHT)
        self.assertFalse(result["edge_candidate_found"], result)

    def test_weak_contrast_step_is_not_a_horizon(self):
        image = np.zeros((HEIGHT, WIDTH, 3))
        image[:, :, :] = 200.0
        image[728:, :, :] = 182.0  # 18 RGB8 step: below the 35 RGB8 floor
        result = sea.detect_sea_horizon(image, WIDTH, HEIGHT)
        self.assertFalse(result["edge_candidate_found"])
        self.assertIn("no-strong-sky-to-water-step", result["reason"])

    def test_land_silhouette_is_not_a_sea_horizon(self):
        # Bright sky over an opaque featureless silhouette: the strongest
        # downward step is its top edge, and below that edge there is no wave
        # texture at all, so the frame is refused instead of being measured.
        result = sea.detect_sea_horizon(frame(land=True), WIDTH, HEIGHT)
        self.assertFalse(result["edge_candidate_found"], result)
        self.assertIn("no-texture-below", result["reason"])

    def test_wavy_edge_is_detected_and_its_spread_reported(self):
        result = sea.detect_sea_horizon(frame(jitter=3.0), WIDTH, HEIGHT)
        self.assertTrue(result["edge_candidate_found"], result)
        self.assertAlmostEqual(result["edge_y_px"], 728.0, delta=1.5)
        self.assertGreaterEqual(result["edge_spread_px"], 2.0)

    def test_output_dir_cannot_escape_artifacts(self):
        self.assertEqual(sea.DEFAULT_OUTPUT, sea.under_artifacts(sea.DEFAULT_OUTPUT))
        for path in (sea.ROOT / "Source/Data/out", sea.ROOT / "documents-readonly/out"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                sea.under_artifacts(path)

    def test_textured_dark_silhouette_cannot_be_certified_as_sea(self):
        # A textured land/cloud/UI silhouette can have the SAME intensity
        # profile as this synthetic water. The algorithm may find its edge,
        # but must never claim semantic identity or accepted contact.
        result = sea.detect_sea_horizon(frame(water=14, texture=3), WIDTH, HEIGHT)
        self.assertTrue(result["edge_candidate_found"], result)
        self.assertFalse(result["sea_horizon_identity_verified"])
        self.assertFalse(result["contact_accepted"])

    def test_nonuniform_band_above_is_actually_checked(self):
        image = frame()
        image[690:710, :, :] = 100.0
        result = sea.detect_sea_horizon(image, WIDTH, HEIGHT)
        self.assertFalse(result["edge_candidate_found"], result)
        self.assertIn("nonuniform-band-above", result["rejected_reasons"])

    def test_dimensions_and_nonfinite_pixels_are_refused(self):
        image = np.zeros((100, 100, 3))
        with self.assertRaises(ValueError):
            sea.detect_sea_horizon(image, WIDTH, HEIGHT)
        image[0, 0, 0] = float("nan")
        with self.assertRaises(ValueError):
            sea.detect_sea_horizon(image, 100, 100)

    def test_small_aperture_yields_no_edge_number(self):
        result = sea.detect_sea_horizon(np.zeros((100, 100, 3)), 100, 100)
        self.assertFalse(result["edge_candidate_found"])
        self.assertIsNone(result["edge_y_px"])


class ModelTests(unittest.TestCase):
    def test_installed_model_is_close_to_spherical_dip_when_tangent_is_inside_clip(self):
        fall = sea.installed_fall()
        # While the tangent ray lies inside the finite 9000 m clip the model is
        # close to spherical dip, not bit-identical. At higher eyes/shorter clips
        # the tangent is unavailable and the far-edge ray must be used.
        for eye in (0.5, 1.0, 3.205094575881958, 5.5, 5.8505, 6.3):
            with self.subTest(eye=eye):
                model_arcmin = -60 * sea.horizon_altitude_deg(fall, eye)
                geometric = 1.9260249 * math.sqrt(eye)
                self.assertAlmostEqual(model_arcmin, geometric, delta=0.01)

    def test_model_falls_back_to_the_finite_clip_for_high_eyes(self):
        fall = sea.installed_fall()
        tangent = math.sqrt(2 * 6371000.0 * 13.5)
        self.assertGreater(tangent, 9000.0)
        model = -60 * sea.horizon_altitude_deg(fall, 13.5)
        far_edge = -60 * math.degrees(math.atan((-13.5 - 6.356928825378418) / 9000.0))
        self.assertAlmostEqual(model, far_edge, delta=0.01)

    def test_withdrawn_editor_copy_was_a_constant_factor(self):
        fall = sea.installed_fall()
        for eye in (3.205094575881958, 5.541750431060791, 5.833658695220947):
            with self.subTest(eye=eye):
                installed = -60 * sea.horizon_altitude_deg(fall, eye)
                editor = -60 * sea.horizon_altitude_deg(sea.EDITOR_FALL, eye)
                self.assertAlmostEqual(editor / installed, 1.571, delta=0.002)

    def test_ratio_is_not_constant_beyond_the_recorded_eye_clip_range(self):
        fall = sea.installed_fall()
        ratio = sea.horizon_altitude_deg(sea.EDITOR_FALL, 14) / sea.horizon_altitude_deg(fall, 14)
        self.assertGreater(abs(ratio - 1.571), 0.05)
        ratio = sea.horizon_altitude_deg(sea.EDITOR_FALL, 5.5, 1000) \
            / sea.horizon_altitude_deg(fall, 5.5, 1000)
        self.assertGreater(abs(ratio - 1.571), 0.4)

    def test_model_boundaries_and_flat_water(self):
        fall = sea.installed_fall()
        for eye, far in ((-0.01, 9000), (14.01, 9000), (5.5, 249), (5.5, 9001),
                         (float("nan"), 9000), (5.5, float("inf"))):
            with self.subTest(eye=eye, far=far), self.assertRaises(ValueError):
                sea.horizon_altitude_deg(fall, eye, far)
        self.assertAlmostEqual(sea.horizon_altitude_deg(fall, 5.5, curved=False),
                               math.degrees(math.atan(-5.5 / 9000)), places=12)


class ProductionTests(unittest.TestCase):
    def test_production_array_matches_the_installed_fixture(self):
        block = sea.PRODUCTION.read_text(encoding="utf-8")
        self.assertIn("0.004905038978904486", block)
        self.assertNotIn("0.0121026969828186", block)
        self.assertIn("installed uboat.core D3D11 water shader DXBC", block)

    def test_every_production_knot_is_checked_against_raw_fixture_bits(self):
        self.assertEqual(len(sea.installed_fall()), 37)


def fitted_case():
    return {"case": "filter0", "cx": 1352.0, "cy": 700.0, "radius_px": 30.0,
            "lower_limb_y_px": 730.0, "rmse_rgb8": 1.0, "fit_success": True,
            "predicted": {"cx": 1352.0, "cy": 700.0, "radius": 30.0, "focal": 6682.0,
                          "brackets": [{"eye_y": 5.5}, {"eye_y": 5.6}]}}


class EstimatorTests(unittest.TestCase):
    def estimate(self, case=None, image=None, model="editor"):
        return sea.cross_validate(case or fitted_case(), image if image is not None else frame(),
                                  WIDTH, HEIGHT, sea.installed_fall(), model)

    def test_rejected_edge_never_falls_back_to_the_historical_roi(self):
        result = self.estimate(image=frame(uniform=True))
        self.assertFalse(result["screen_space_measurement_usable"])
        for key in ("measured_limb_minus_candidate_px", "predicted_installed_residual_px",
                    "configured_limb_minus_candidate_px", "edge_candidate_y_px"):
            self.assertIsNone(result[key], key)
        self.assertEqual(result["applied_old_to_installed_shift_px"], 0)

    def test_rejected_fit_gives_only_a_labelled_configuration_difference(self):
        case = fitted_case()
        case["radius_px"] = 26.0
        case["lower_limb_y_px"] = 726.0
        result = self.estimate(case)
        self.assertEqual(result["ruler"], "rejected-fit")
        self.assertFalse(result["screen_space_measurement_usable"])
        self.assertIsNone(result["measured_limb_minus_candidate_px"])
        self.assertIsNone(result["predicted_installed_residual_px"])
        self.assertEqual(result["configured_limb_minus_candidate_px"], 2)

    def test_failed_inconsistent_or_bad_centre_fits_are_refused(self):
        for change in ({"fit_success": False}, {"cx": 1355.0}, {"rmse_rgb8": 2.1},
                       {"lower_limb_y_px": 731.0}, {"radius_px": float("nan")}):
            with self.subTest(change=change):
                case = fitted_case()
                case.update(change)
                result = self.estimate(case)
                self.assertFalse(result["screen_space_measurement_usable"])
                self.assertIsNone(result["measured_limb_minus_candidate_arcmin"])

    def test_installed_frames_are_never_shifted_again(self):
        result = self.estimate(model="installed")
        self.assertTrue(result["screen_space_measurement_usable"])
        self.assertEqual(result["measured_limb_minus_candidate_px"], 2)
        self.assertIsNone(result["predicted_installed_residual_px"])
        self.assertEqual(result["applied_old_to_installed_shift_px"], 0)

    def test_editor_frames_receive_only_a_separate_offline_prediction(self):
        result = self.estimate(model="editor")
        self.assertEqual(result["measured_limb_minus_candidate_px"], 2)
        self.assertAlmostEqual(result["predicted_installed_residual_px"],
                               2 - result["model_raise_px"], places=12)
        self.assertFalse(result["contact_accepted"])
        self.assertFalse(result["sea_horizon_identity_verified"])
        self.assertFalse(result["same_presented_frame_pose_verified"])

    def test_frame_model_cannot_be_guessed(self):
        with self.assertRaises(ValueError):
            self.estimate(model=None)

    def test_configured_pixels_never_replace_a_rejected_measured_limb(self):
        case = fitted_case()
        case["fit_success"] = False
        first = self.estimate(case, model="installed")
        case["predicted"]["cy"] += 10
        second = self.estimate(case, model="installed")
        self.assertEqual(second["configured_limb_minus_candidate_px"]
                         - first["configured_limb_minus_candidate_px"], 10)
        self.assertIsNone(first["measured_limb_minus_candidate_px"])
        self.assertIsNone(second["measured_limb_minus_candidate_px"])

    def test_separate_frames_directory_is_preserved_and_receipt_not_overwritten(self):
        parent = sea.ROOT / "artifacts" / "tests"
        parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="sea-horizon-", dir=parent) as tmp:
            directory = Path(tmp)
            frames = directory / "frames"
            frames.mkdir()
            path = frames / "filter0-on.png"
            Image.fromarray(np.clip(frame(), 0, 255).astype(np.uint8)).save(path)
            before = sea.fingerprint(path)
            analysis = directory / "analysis.json"
            analysis.write_text(json.dumps({"sun_filter_cases": [fitted_case()]}), encoding="utf-8")
            output = directory / "output"
            result = sea.run(directory, output, "installed", analysis_path=analysis, frames_dir=frames)
            self.assertEqual(sea.fingerprint(path), before)
            self.assertIn(str(path.resolve()), result["inputs"])
            self.assertEqual(result["editor_prediction_frames"], 0)
            self.assertFalse(result["horizon_contact_accepted"])
            receipt = sea.fingerprint(output / "sea-horizon.json")
            with self.assertRaises(ValueError):
                sea.run(directory, output, "installed", analysis_path=analysis, frames_dir=frames)
            self.assertEqual(sea.fingerprint(output / "sea-horizon.json"), receipt)


class RecordedFrameTests(unittest.TestCase):
    def cases(self, relative, frames_relative):
        directory = sea.ROOT / "artifacts/reports/solar-table-20261008" / relative
        analysis_path = directory / "pixel-analysis.json"
        if not analysis_path.is_file():
            self.skipTest("Historical local frames are not included in this checkout")
        analysis = json.loads(analysis_path.read_text(encoding="utf-8-sig"))
        for case in analysis["sun_filter_cases"]:
            path = directory / frames_relative / (case["case"] + "-on.png")
            self.assertTrue(path.is_file(), path)
            image = sea.load_image(path)
            yield case, image, path

    def test_original_frames_extrapolate_without_becoming_corrected_build_pixels(self):
        count = 0
        for case, image, path in self.cases("render-only-20261009", "live-verified"):
            with self.subTest(case=case["case"]):
                before = sea.fingerprint(path)
                height, width = image.shape[:2]
                row = sea.cross_validate(case, image, width, height, sea.installed_fall(), "editor")
                self.assertTrue(row["screen_space_measurement_usable"], row)
                self.assertIn(row["edge_candidate_y_px"], (727, 727.5, 728))
                self.assertIsNotNone(row["predicted_installed_residual_arcmin"])
                self.assertFalse(row["contact_accepted"])
                self.assertFalse(row["sea_horizon_identity_verified"])
                self.assertEqual(sea.fingerprint(path), before)
                count += 1
        self.assertEqual(count, 5)

    def test_corrected_frames_reject_the_bad_circle_and_never_apply_the_old_shift(self):
        count = 0
        for case, image, path in self.cases("horizon-20261009/live-contact-2", "."):
            with self.subTest(case=case["case"]):
                before = sea.fingerprint(path)
                height, width = image.shape[:2]
                row = sea.cross_validate(case, image, width, height, sea.installed_fall(), "installed")
                # With the uniformity condition now actually enforced, the
                # filter0/repeat0 frames have only 97 qualified columns, below
                # the existing 120-column floor. Do NOT loosen the detector to
                # recover a previously reported number.
                expected_candidate = case["case"] not in ("filter0", "repeat0")
                self.assertEqual(row["edge_candidate_found"], expected_candidate, row)
                self.assertFalse(row["screen_space_measurement_usable"])
                self.assertIn("radius-mismatch", row["fit_rejection_reasons"])
                self.assertIsNone(row["measured_limb_minus_candidate_arcmin"])
                self.assertIsNone(row["predicted_installed_residual_arcmin"])
                self.assertEqual(row["applied_old_to_installed_shift_px"], 0)
                if expected_candidate:
                    self.assertIsNotNone(row["configured_limb_minus_candidate_arcmin"])
                else:
                    self.assertIsNone(row["configured_limb_minus_candidate_arcmin"])
                    self.assertEqual(row["detected"]["segment_width_columns"], 97)
                self.assertFalse(row["contact_accepted"])
                self.assertEqual(sea.fingerprint(path), before)
                count += 1
        self.assertEqual(count, 5)


if __name__ == "__main__":
    unittest.main(testRunner=unittest.TextTestRunner(stream=sys.stdout, verbosity=2))
