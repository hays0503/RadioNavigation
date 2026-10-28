"""Isolated mutation check for the offline bench; NEVER edits working sources.

Each mutant is compiled/loaded in a separate Python process, then executes the
actual bench tests. Fixture mutants get a report-local copy. A mutation counts
only when the named expected test fails; crash/zero tests is a harness error.
No real bridge request is made by this suite.
"""

import argparse
import importlib.util
import io
import json
import subprocess
import sys
import types
import unittest
from pathlib import Path

import audit


SOURCE = Path(__file__).with_name("audit.py")
TESTS = Path(__file__).with_name("tests") / "test_audit.py"
CASES = [
    ("index-sign", "h = hs + index / 60.0", "h = hs - index / 60.0", "test_additive_index_sign_and_fviii_uses_corrected_h"),
    ("duplicate-sd", "ho = h + (correction[\"base_arcmin\"] + correction[\"monthly_arcmin\"]) / 60.0",
     "ho = h + (correction[\"base_arcmin\"] + correction[\"monthly_arcmin\"] + 16.0) / 60.0",
     "test_verified_38_degree_cells_and_month_are_not_double_sd"),
    ("monthly-term-lost", "fixture[\"monthly_lower_limb_arcmin\"][month - 1]", "0.0",
     "test_monthly_terms_include_all_twelve_printed_values"),
    ("extrapolation-allowed", "finite(value, label, axis[0], axis[-1])", "value = max(axis[0], min(axis[-1], value))",
     "test_no_extrapolation_or_upper_limb_fallback"),
    ("upper-limb-accepted", "if sight[\"limb\"] != \"lower\":", "if False:",
     "test_no_extrapolation_or_upper_limb_fallback"),
    ("duplicate-correction-accepted", "unknown = set(data) - set(allowed)", "unknown = set()",
     "test_rejects_duplicate_and_hidden_correction_fields"),
    ("output-scope-lost", "if not output.is_relative_to(ARTIFACTS.resolve()):", "if False:",
     "test_report_output_cannot_touch_originals_sources_or_inputs"),
    ("late-time-accepted", "if moment != utc_time(sight[\"gmt\"]):", "if False:",
     "test_residuals_refuse_later_time_or_different_index_and_raw_arc"),
    ("calibration-residual-zeroed", "(audit[\"calibrated\"][\"altDeg\"] - audit[\"uncalibrated\"][\"altDeg\"]) * 60.0", "0.0",
     "test_residuals_keep_live_position_ap_and_time_calibration_separate"),
    ("live-position-replaced-by-ap", "lat = finite(position[\"latDeg\"], \"live latitude\", -90, 90)",
     "lat = sight[\"assumed_lat_deg\"]", "test_residuals_keep_live_position_ap_and_time_calibration_separate"),
    ("inactive-limb-prediction", "if configured is not None and measuring.get(\"ready\") is True and measuring.get(\"active\") is True:",
     "if configured is not None:", "test_no_limb_prediction_from_an_inactive_disc_or_missing_directions"),
    ("projection-radius-instead-of-diameter", "predicted = height * math.tan(math.radians(diameter / 2)) / math.tan(math.radians(fov / 2))",
     "predicted = 0.5 * height * math.tan(math.radians(diameter / 2)) / math.tan(math.radians(fov / 2))",
     "test_projection_is_full_diameter_and_inverts_the_manual_measurement"),
    ("snapshot-write-route", '"/time", "/sextant", "/sky", "/state?fields=position,time", "/time"',
     '"/time", "/sextant/record", "/sky", "/state?fields=position,time", "/time"',
     "test_snapshot_routes_are_get_only_and_do_not_change_a_series"),
    ("fixture-60deg-11m-transcription", None, None, "test_fixture_boundary_cells_are_independent_printed_literals"),
]


def child(case_name, result_path, fixture_path):
    source = SOURCE.read_text(encoding="utf-8")
    if case_name != "baseline":
        _, old, new, _ = next(case for case in CASES if case[0] == case_name)
        if old is not None:
            if source.count(old) != 1:
                raise ValueError("Mutation target must match exactly once: " + case_name)
            source = source.replace(old, new, 1)
    code = compile(source, str(SOURCE), "exec")
    module = types.ModuleType("audit")
    module.__file__ = str(SOURCE)
    sys.modules["audit"] = module
    exec(code, module.__dict__)
    if fixture_path:
        module.FVIII_PATH = Path(fixture_path)
    spec = importlib.util.spec_from_file_location("solar_audit_mutation_tests", TESTS)
    tests = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(tests)
    log = io.StringIO()
    result = unittest.TextTestRunner(stream=log, verbosity=2).run(unittest.defaultTestLoader.loadTestsFromModule(tests))
    errors = [test.id() for test, _ in result.errors]
    failures = [test.id() for test, _ in result.failures]
    Path(result_path).write_text(json.dumps({"compiled": True, "tests_run": result.testsRun,
                                            "errors": errors, "failures": failures,
                                            "skipped": [test.id() for test, _ in result.skipped],
                                            "log": log.getvalue()}, ensure_ascii=False, indent=2), encoding="utf-8")
    return 0


def run(output):
    output = audit.report_path(output)
    output.mkdir(parents=True, exist_ok=True)
    protected = {str(path): audit.sha256(path) for path in (SOURCE, TESTS, audit.FVIII_PATH)}
    results = []
    for name in ["baseline"] + [case[0] for case in CASES]:
        path = output / (name + ".json")
        fixture_path = None
        if name.startswith("fixture-"):
            fixture = audit.read_json(audit.FVIII_PATH)
            fixture["base_arcmin"][-1][11] = 9.7
            fixture_path = audit.write_report(output / "mutant-fixture.json", fixture)
        command = [sys.executable, "-X", "utf8", str(Path(__file__).resolve()), "--child", name,
                   "--out", str(path)]
        if fixture_path:
            command.extend(["--fixture", str(fixture_path)])
        process = subprocess.run(command, cwd=audit.ROOT, capture_output=True, text=True, encoding="utf-8")
        if process.returncode != 0:
            raise RuntimeError("Mutation harness crashed: " + name + "\n" + process.stdout + process.stderr)
        result = audit.read_json(path)
        if not result["compiled"] or result["tests_run"] == 0 or result["errors"]:
            raise RuntimeError("Invalid mutation result: " + name)
        if name == "baseline":
            if result["failures"] or result["skipped"]:
                raise RuntimeError("Baseline must pass ALL tests, including the original PDF")
            print("Baseline PASS", result["tests_run"], "tests")
            continue
        expected = next(case[3] for case in CASES if case[0] == name)
        caught = any(expected in failure for failure in result["failures"])
        results.append({"name": name, "compiled": True, "caught": caught, "expected_failure": expected})
        print(("CAUGHT " if caught else "SURVIVED ") + name)
    after = {path: audit.sha256(path) for path in protected}
    if protected != after:
        raise RuntimeError("Working source/fixture changed during the isolated check")
    audit.write_report(output / "results.json", {"schema": 1, "cases": results,
                                               "working_files_unchanged": protected,
                                               "method": "Fresh processes; in-memory code and report-local fixture mutants, no edits/restoration"})
    return 0 if all(result["caught"] for result in results) else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=audit.DEFAULT_OUT / "mutations")
    parser.add_argument("--child", help=argparse.SUPPRESS)
    parser.add_argument("--fixture", help=argparse.SUPPRESS)
    args = parser.parse_args()
    if args.child:
        audit.report_path(args.out, [SOURCE, TESTS, audit.FVIII_PATH])
        return child(args.child, args.out, args.fixture)
    return run(args.out)


if __name__ == "__main__":
    sys.exit(main())
