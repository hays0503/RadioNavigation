"""Offline edge candidates and explicitly versioned model predictions.

Brightness and texture cannot establish whether an edge is sea, land, fog or UI.
This instrument reports candidates, RGB8 limb estimates and configured geometry
SEPARATELY. Rejected edges/fits yield no measured gap. Only an explicitly marked
editor-model frame may receive an old-to-installed extrapolation; installed-model
frames are never shifted again. Neither kind is an accepted navigational sight.

No game/network calls; writes only to a new artifacts/ receipt. Inputs are hashed
before and after. Historical frames and reports are never changed.
"""
import argparse
import hashlib
import json
import math
import re
import struct
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
PRODUCTION = ROOT / "Source" / "Logic" / "SolarSeaHorizon.cs"
FIXTURE = ROOT / "Tools" / "solar-sight" / "water-curvature-verified.json"
DEFAULT_OUTPUT = ROOT / "artifacts/reports/solar-table-20261008/horizon-20261009/sea-horizon"

# Outdated TestUI editor include (withdrawn as a source 2026-10-09). It is
# embedded ONLY to quantify the old-to-installed configuration difference. A
# near-1.571 ratio in the recorded eye/clip range is not a causal pixel proof or
# an identity valid at every eye height/far clip.
EDITOR_FALL = [
    0, 0.0121026969828186, 0.0484107893459207, 0.1089242749673370,
    0.1936431552617130, 0.3025674295217270, 0.4356970970400550, 0.5930321578166970,
    0.7745726104370080, 0.9803184549009860, 1.2102696897939900, 1.4644263151160100,
    1.7427883294524000, 2.0453557313885300, 2.3721285202170600, 2.7231066945233600,
    3.0982902528927700, 3.4976791946179600, 3.9212735168696600, 4.3690732196478600,
    4.8410782994159400, 5.3372887561739000, 5.8577045870924500, 6.4023257900496200,
    6.9711523629234500, 7.5641843042992800, 8.1814216113478300, 8.8228642819471200,
    9.4885123139751800, 10.1783657038954000, 10.8924244502931000, 11.6306885489245000,
    12.3931579990820000, 13.1798327958146000, 13.9907129377075000, 14.8257984205168000,
    15.6850892414132000,
]
LUMA = np.array([0.2126, 0.7152, 0.0722])


def under_artifacts(path):
    path = path.resolve()
    if not path.is_relative_to((ROOT / "artifacts").resolve()):
        raise ValueError("Output must stay under this repository's artifacts/.")
    return path


def fingerprint(path):
    stat = path.stat()
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for part in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(part)
    return {"sha256": digest.hexdigest(), "length": stat.st_size, "mtime_ns": stat.st_mtime_ns}


def installed_fall():
    """Parse the production array: the model under test is production, not a copy."""
    text = PRODUCTION.read_text(encoding="utf-8")
    block = re.search(r"double\[\]\s*fall\s*=\s*\{(.*?)\};", text, re.S)
    if not block:
        raise ValueError("SolarSeaHorizon fall array not found")
    values = [float(x.strip()) for x in block.group(1).split(",") if x.strip()]
    fixture = json.loads(FIXTURE.read_text(encoding="utf-8-sig"))
    expected = [-struct.unpack(">f", bytes.fromhex(bits))[0]
                for bits in fixture["signed_x_bits"][:37]]
    if values != expected:
        raise ValueError("Production curvature knots do not match the installed fixture")
    return values


def horizon_altitude_deg(fall, eye, far=9000.0, curved=True):
    """Mirror of SolarSeaHorizon.TryAltitude (ideal mean-water silhouette)."""
    if not math.isfinite(eye) or not 0 <= eye <= 14 or not math.isfinite(far) or not 250 <= far <= 9000:
        raise ValueError("Eye/clip outside the production model domain")
    if len(fall) < 37 or not all(math.isfinite(v) and v >= 0 for v in fall):
        raise ValueError("Missing or invalid curvature knots")
    slope = float("-inf")
    for i in range(1, len(fall)):
        if i * 250 <= far:
            slope = max(slope, (-eye - (fall[i] if curved else 0)) / (i * 250))
    index = far / 250
    low = min(len(fall) - 2, int(index))
    drop = fall[low] + (index - low) * (fall[low + 1] - fall[low]) if curved else 0
    return math.degrees(math.atan(max(slope, (-eye - drop) / far)))


def deg_per_pixel(focal):
    return math.degrees(math.atan(1.0 / focal))


def historical_edge(image, x0=1185, x1=1250, y0=720, y1=751):
    """The withdrawn hardcoded ROI: strongest per-column sky-to-water step."""
    luma = image @ LUMA
    crop = luma[y0:y1, x0:x1]
    drop = crop[:-1] - crop[1:]
    index = np.argmax(drop, axis=0)
    peak = np.max(drop, axis=0)
    y = y0 + index + 1.0
    return {"median_y_px": float(np.median(y)), "min_y_px": float(np.min(y)),
            "max_y_px": float(np.max(y)), "columns": x1 - x0,
            "roi": [x0, y0, x1, y1],
            "median_drop_rgb8": float(np.median(peak)),
            "status": "withdrawn-hardcoded-roi"}


def historical_roi_diagnosis(image, x0=1185, x1=1250, y0=720, y1=751):
    """Diagnostic intensities only; they cannot establish sea/fog/land identity."""
    luma = image @ LUMA
    edge = historical_edge(image, x0, x1, y0, y1)
    y = int(round(edge["median_y_px"]))
    sky = luma[y - 22:y - 3, x0:x1]
    water = luma[y + 3:y + 22, x0:x1]
    reference = float(np.max(luma[y0:y0 + 30, x0:x1]))
    sky_median = float(np.median(sky))
    verdict = "compatible-intensity-profile" if sky_median >= 0.5 * reference and \
        float(np.median(water)) < sky_median - 8 else "incompatible-intensity-profile"
    return {"sky_band_median_rgb8": sky_median,
            "sky_band_spread_rgb8": float(np.ptp(sky)),
            "water_band_median_rgb8": float(np.median(water)),
            "sky_reference_rgb8": reference,
            "verdict": verdict}


def detect_sea_horizon(image, width, height, band_above=44, band_sky_min=60,
                       sky_ratio_min=0.5, max_edge_spread_px=2.0,
                       min_drop_rgb8=35.0, min_segment_columns=120, max_gap_columns=40,
                       min_water_texture=1.0, max_sky_spread_rgb8=60.0):
    """A qualified edge candidate, not semantic proof of a sea horizon."""
    if image.shape != (height, width, 3) or not np.isfinite(image).all():
        raise ValueError("Image dimensions or pixels are invalid")
    luma = image @ LUMA
    centre_x, centre_y = width / 2.0, height / 2.0
    radius = 0.88 * height / 2.0
    x_lo = int(centre_x - radius + 60)
    x_hi = int(min(centre_x - 20, width - 1))
    y_lo = max(band_above, int(centre_y - 90))
    y_hi = min(height - band_above, int(centre_y + 70))
    refusal = {"edge_candidate_found": False, "edge_y_px": None,
               "sea_horizon_identity_verified": False, "contact_accepted": False}
    if x_lo < 0 or x_hi - x_lo < 30 or y_hi <= y_lo:
        return dict(refusal, reason="aperture-too-narrow")
    columns = []
    for x in range(x_lo, x_hi):
        sky_reference = float(np.max(luma[y_lo:y_lo + 30, x]))
        profile = luma[:, x]
        drops = profile[y_lo:y_hi - 1] - profile[y_lo + 1:y_hi]
        index = int(np.argmax(drops))
        y = y_lo + index + 1
        peak = float(drops[index])
        sky_band = profile[y - band_above:y - 3]
        water_band = profile[y + 3:y + band_above]
        sky_median = float(np.median(sky_band)) if sky_band.size else 0.0
        sky_spread = float(np.ptp(sky_band)) if sky_band.size else 255.0
        water_median = float(np.median(water_band)) if water_band.size else 255.0
        water_spread = float(np.std(water_band)) if water_band.size else 0.0
        columns.append({"x": x, "y": y, "drop": peak, "sky_median": sky_median,
                        "sky_spread": sky_spread, "sky_reference": sky_reference,
                        "water_median": water_median, "water_spread": water_spread})
    median_y = float(np.median([c["y"] for c in columns]))
    strong = [c for c in columns if c["drop"] >= min_drop_rgb8]
    if not strong:
        return dict(refusal, reason="no-strong-sky-to-water-step",
                search={"x": [x_lo, x_hi], "y": [y_lo, y_hi],
                            "columns_scanned": len(columns),
                            "min_contiguous_columns": min_segment_columns},
                qualified_columns=0, rejected_reasons={"no-strong-step": len(columns)},
                all_column_median_y_px=median_y)
    # Cluster only on strong steps: a dark cloud/haze wall elsewhere in the beam
    # must not drag the horizon's median.
    best_y = float(np.median([c["y"] for c in strong]))
    cluster = [c for c in strong if abs(c["y"] - best_y) <= max_edge_spread_px]
    qualified, rejected = [], {}
    for column in cluster:
        if column["sky_median"] < sky_ratio_min * max(column["sky_reference"], band_sky_min):
            rejected["no-uniform-sky-above"] = rejected.get("no-uniform-sky-above", 0) + 1
            continue
        if column["sky_spread"] > max_sky_spread_rgb8:
            rejected["nonuniform-band-above"] = rejected.get("nonuniform-band-above", 0) + 1
            continue
        if column["water_median"] > column["sky_median"] - 8:
            rejected["no-water-below"] = rejected.get("no-water-below", 0) + 1
            continue
        qualified.append(column)
    rejected["weak-step"] = len(columns) - len(strong)
    rejected["other-strong-step"] = len(strong) - len(cluster)
    # Accept the longest contiguous qualified segment (gaps tolerated), never an
    # average over the whole beam.
    best, run = [], []
    for column in qualified:
        if run and column["x"] - run[-1]["x"] - 1 > max_gap_columns:
            if len(run) > len(best):
                best = run
            run = []
        run.append(column)
    if len(run) > len(best):
        best = run
    ys = [c["y"] for c in best]
    result = dict(refusal)
    result.update({"aperture": {"centre_x": centre_x, "centre_y": centre_y, "radius_px": radius},
              "search": {"x": [x_lo, x_hi], "y": [y_lo, y_hi],
                         "columns_scanned": len(columns),
                         "min_contiguous_columns": min_segment_columns},
              "median_all_columns_y_px": median_y,
              "median_strong_columns_y_px": best_y,
              "qualified_columns": len(qualified),
              "rejected_reasons": rejected,
              "rejected_examples": [c for c in columns
                                    if abs(c["y"] - median_y) <= max_edge_spread_px][:3],
              "segment_x": [best[0]["x"], best[-1]["x"]] if best else None,
              "segment_width_columns": len(best)})
    if len(best) < min_segment_columns:
        result.update({"reason": "no-contiguous-edge-segment (%d columns)" % len(best),
                       "margin_rule": "A rejected candidate yields NO measured gap."})
        return result
    spread = float(max(ys) - min(ys))
    texture = float(np.median([c["water_spread"] for c in best]))
    if texture < min_water_texture:
        result.update({"reason": "no-texture-below (median %.2f RGB8)" % texture,
                       "segment_water_texture_rgb8": texture})
        return result
    result.update({"edge_candidate_found": True, "edge_y_px": float(np.median(ys)),
                   "edge_min_y_px": float(min(ys)), "edge_max_y_px": float(max(ys)),
                   "edge_spread_px": spread,
                   "segment_water_texture_rgb8": texture,
                   "median_drop_rgb8": float(np.median([c["drop"] for c in best]))})
    return result


def luma_of(image):
    return image @ LUMA


def cross_validate(case, image, width, height, installed, frame_model, editor=EDITOR_FALL):
    """Keep configuration, RGB8 estimates and editor-only predictions distinct."""
    if frame_model not in ("editor", "installed"):
        raise ValueError("Frame model must be explicitly editor or installed")
    predicted = case["predicted"]
    configured = predicted["radius"]
    focal = predicted["focal"]
    if not all(math.isfinite(predicted[k]) for k in ("cx", "cy", "radius", "focal")) \
            or configured <= 0 or focal <= 0:
        raise ValueError("Invalid configured projection")
    eyes = [bracket["eye_y"] for bracket in predicted["brackets"]]
    if not eyes or not all(math.isfinite(eye) and 0 <= eye <= 14 for eye in eyes):
        raise ValueError("Frame geometry carries no valid eye-height bracket")
    # Brackets are not a certified same-presented-frame pose; keep the range.
    eye = (min(eyes) + max(eyes)) / 2.0
    detected = detect_sea_horizon(image, width, height)
    old_alt = horizon_altitude_deg(editor, eye)
    new_alt = horizon_altitude_deg(installed, eye)
    raise_px = (new_alt - old_alt) / deg_per_pixel(focal)
    edge_y = detected["edge_y_px"]
    minute_per_pixel = 60.0 * deg_per_pixel(focal)
    fit_reasons = []
    fit_keys = ("cx", "cy", "radius_px", "lower_limb_y_px", "rmse_rgb8")
    finite_fit = all(isinstance(case.get(k), (int, float)) and math.isfinite(case[k]) for k in fit_keys)
    if case.get("fit_success") is not True or not finite_fit:
        fit_reasons.append("missing-or-failed-fit")
    else:
        if abs(case["radius_px"] - configured) > 2.0 or case["radius_px"] <= 0:
            fit_reasons.append("radius-mismatch")
        if math.hypot(case["cx"] - predicted["cx"], case["cy"] - predicted["cy"]) > 2.0:
            fit_reasons.append("centre-mismatch")
        if not 0 <= case["rmse_rgb8"] <= 2.0:
            fit_reasons.append("fit-rmse")
        if abs(case["lower_limb_y_px"] - case["cy"] - case["radius_px"]) > 1e-6:
            fit_reasons.append("inconsistent-fit-limb")
    usable = detected["edge_candidate_found"] and not fit_reasons
    measured_gap = case["lower_limb_y_px"] - edge_y if usable else None
    configured_gap = predicted["cy"] + configured - edge_y if edge_y is not None else None
    predicted_limb = case["lower_limb_y_px"] - raise_px if usable and frame_model == "editor" else None
    predicted_gap = predicted_limb - edge_y if predicted_limb is not None else None
    return {"case": case["case"], "frame_model": frame_model,
            "screen_space_measurement_usable": usable,
            "eye_estimate_m": eye, "eye_bracket_m": [min(eyes), max(eyes)], "focal_px": focal,
            "ruler": "rgb8-fitted-circle" if not fit_reasons else "rejected-fit",
            "fit_rejection_reasons": fit_reasons,
            "fitted_lower_limb_y_px": case.get("lower_limb_y_px"),
            "fitted_radius_px": case.get("radius_px"),
            "configured_radius_px": configured,
            "radius_error_px": case["radius_px"] - configured if finite_fit else None,
            "old_model_horizon_arcmin": -60 * old_alt,
            "installed_model_horizon_arcmin": -60 * new_alt,
            "geometric_dip_arcmin": 1.9260249 * math.sqrt(eye),
            "model_raise_arcmin": 60 * (new_alt - old_alt),
            "model_raise_px": raise_px,
            "applied_old_to_installed_shift_px": raise_px if predicted_limb is not None else 0.0,
            "edge_candidate_y_px": edge_y,
            "edge_candidate_found": detected["edge_candidate_found"],
            "sea_horizon_identity_verified": False,
            "measured_limb_minus_candidate_px": measured_gap,
            "measured_limb_minus_candidate_arcmin": measured_gap * minute_per_pixel if usable else None,
            "predicted_installed_limb_y_px": predicted_limb,
            "predicted_installed_residual_px": predicted_gap,
            "predicted_installed_residual_arcmin": predicted_gap * minute_per_pixel
                if predicted_gap is not None else None,
            "configured_limb_minus_candidate_px": configured_gap,
            "configured_limb_minus_candidate_arcmin": configured_gap * minute_per_pixel
                if configured_gap is not None else None,
            "same_presented_frame_pose_verified": False,
            "detected": detected,
            "contact_accepted": False}


def load_image(path):
    with Image.open(path) as handle:
        return np.asarray(handle.convert("RGB"), dtype=float)


def run(run_dir, output, frame_model, cases=None, analysis_path=None, frames_dir=None):
    run_dir = run_dir.resolve()
    output = under_artifacts(output)
    if (output / "sea-horizon.json").exists():
        raise ValueError("Receipt exists; choose a distinct artifacts directory.")
    analysis_path = Path(analysis_path) if analysis_path else run_dir / "pixel-analysis.json"
    frames_dir = Path(frames_dir) if frames_dir else run_dir
    if not analysis_path.is_file():
        raise ValueError("No pixel analysis at %s" % analysis_path)
    installed = installed_fall()
    protected_paths = sorted(frames_dir.glob("*.png")) + [analysis_path, PRODUCTION, FIXTURE]
    protected = {str(p.resolve()): fingerprint(p) for p in protected_paths}
    analysis = json.loads(analysis_path.read_text(encoding="utf-8-sig"))
    first = analysis["sun_filter_cases"][0]["case"]
    height, width = load_image(frames_dir / (first + "-on.png")).shape[:2]
    rows = []
    for case in analysis["sun_filter_cases"]:
        if cases and case["case"] not in cases:
            continue
        image = load_image(frames_dir / (case["case"] + "-on.png"))
        row = cross_validate(case, image, width, height, installed, frame_model)
        row["frame"] = case["case"] + "-on.png"
        row["historical_edge"] = historical_edge(image)
        row["withdrawn_roi_diagnosis"] = historical_roi_diagnosis(image)
        rows.append(row)
    if not rows:
        raise ValueError("No matching cases; available: " +
                         ", ".join(c["case"] for c in analysis["sun_filter_cases"]))
    changed = [name for name, before in protected.items()
                if not Path(name).is_file() or fingerprint(Path(name)) != before]
    if changed:
        raise ValueError("Frames changed: %s" % changed)
    result = {"schema": 2, "run": str(run_dir), "frame_model": frame_model,
              "inputs": protected,
              "installed_knots": installed[:1] + ["..."] + installed[-1:],
              "installed_source": "parsed from Source/Logic/SolarSeaHorizon.cs",
              "editor_fall_status": "withdrawn TestUI copy, embedded only to size the historical error",
              "rows": rows,
              "edge_candidate_frames": sum(1 for row in rows if row["edge_candidate_found"]),
              "screen_space_usable_frames": sum(1 for row in rows if row["screen_space_measurement_usable"]),
              "editor_prediction_frames": sum(1 for row in rows
                                              if row["predicted_installed_residual_px"] is not None),
              "horizon_contact_accepted": False,
              "goal_complete": False,
              "real_navigation_fix_tested": False,
              "limits": ["An intensity/texture candidate does not certify sea/fog/land/UI identity.",
                         "Configured geometry is not a measured physical alpha limb.",
                         "Editor-frame extrapolations are predictions, not installed-build pixels.",
                         "Eye brackets are not certified same-presented-frame poses.",
                         "A model/disc placement error CAN bias a visually aimed drum reading.",
                         "No controlled sea-state/mesh/fog A/B established the cause of any gap."]}
    output.mkdir(parents=True, exist_ok=True)
    with (output / "sea-horizon.json").open("x", encoding="utf-8") as stream:
        stream.write(json.dumps(result, ensure_ascii=False, indent=2, allow_nan=False) + "\n")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--frame-model", choices=("editor", "installed"), required=True)
    parser.add_argument("--analysis", type=Path)
    parser.add_argument("--frames", type=Path)
    parser.add_argument("--case", action="append", dest="cases")
    args = parser.parse_args()
    result = run(args.run, args.output, args.frame_model, args.cases, args.analysis, args.frames)
    print({"run": args.run.name, "edge_candidate_frames": result["edge_candidate_frames"],
            "rows": len(result["rows"]),
            "screen_space_usable_frames": result["screen_space_usable_frames"],
            "editor_prediction_frames": result["editor_prediction_frames"],
            "contact_accepted": False})
