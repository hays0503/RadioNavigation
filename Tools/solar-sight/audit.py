"""Offline solar lower-limb bench. Never records a sight or changes a game.

All outputs are restricted to this project's artifacts/. Tables and kernels
are opened read-only. JPL requires an existing local kernel; no downloads.
F VIII is a bounded, visually verified subset of the user's scanned table.
These diagnostics are NOT a table-compatible optical/navigation mode.
"""

import argparse
import hashlib
import json
import math
import re
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path
from urllib.error import URLError
from urllib.parse import urlsplit
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener


ROOT = Path(__file__).resolve().parents[2]
ARTIFACTS = ROOT / "artifacts"
DEFAULT_OUT = ARTIFACTS / "reports/solar-sight"
REFERENCES = ROOT / "documents-readonly/celestian-navigation-sh5"
ALMANAC_NAME = "[1] Nautical Almanac SH5 1939-1945.pdf"
ALMANAC_HASH = "0faa33d1afc3835a4cca72c8c7b21750e611ad5e11a339adffe14c6f50068f48"
DE421_HASH = "a20a7139da04cbc462454634918e9a9ca69127044e2cc9d4f9c16e238d2deedc"
FVIII_PATH = Path(__file__).with_name("fviii-verified.json")
MONTHS = "Jan Feb Mar Apr May Jun Jul Aug Sep Oct Nov Dec".split()
DATE_LABEL = r"\d{1,2}-(?:" + "|".join(MONTHS) + r")"
LIMITATION = "Arithmetic diagnostic only: SH5 time/declination convention and the game's limb/horizon/refraction model are not accepted."


def read_json(path):
    with Path(path).open(encoding="utf-8-sig") as stream:
        return json.load(stream, parse_constant=lambda value: fail("Non-finite JSON: " + value))


def fail(message):
    raise ValueError(message)


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def report_path(path, inputs=()):
    output = Path(path).resolve()
    if not output.is_relative_to(ARTIFACTS.resolve()):
        fail("Output must be under this project's artifacts/ (never Source, PDFs or saves)")
    if any(output == Path(source).resolve() for source in inputs):
        fail("Output must not overwrite an input")
    return output


def write_report(path, payload, inputs=()):
    output = report_path(path, inputs)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(payload, ensure_ascii=False, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    return output


def finite(value, label, low=None, high=None):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
        fail(label + " must be a finite number")
    if low is not None and value < low or high is not None and value > high:
        fail(label + " is outside the accepted range")
    return float(value)


def text(value, label):
    if not isinstance(value, str) or not value.strip():
        fail(label + " must identify its source/reference")
    return value


def utc_time(value):
    if not isinstance(value, str) or not value.endswith("Z"):
        fail("gmt must have an explicit Z suffix; do not silently interpret port/local time")
    result = datetime.fromisoformat(value[:-1] + "+00:00")
    if not 1939 <= result.year <= 1945:
        fail("GMT must be in the 1939-1945 campaign")
    return result


def iso(value):
    return value.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")


def wrap180(value):
    return (value + 180.0) % 360.0 - 180.0


def printed_angle(degrees, minutes):
    degrees = degrees.replace("−", "-")
    deg = float(degrees.replace(",", "."))
    # The original prints 58 degrees 60.0' on p.38 (1942 Apr 1 16h).
    # Carry an exact rounded 60.0' arithmetically; retain the raw tokens.
    minute = finite(float(minutes.replace(",", ".")), "printed minutes", 0, 60.0)
    return (-1 if degrees.startswith("-") else 1) * (abs(deg) + minute / 60.0)


def parse_solar_page(layout, regular, page, first_date):
    """Numeric rows have top-to-bottom order; rotated date labels do NOT.

    Require an explicit chronological anchor and exact date/hour/column sets.
    Retain source lines and tokens. Do not sort rows by extraction-order labels.
    """
    if "declination" not in regular or "of the Sun" not in regular:
        fail("Selected page is not the Sun GHA/declination table")
    years = list(range(1939, 1946))
    header = next((line for line in layout.splitlines() if re.search(r"1939\s+1940", line)), "")
    if [int(x) for x in re.findall(r"19\d{2}", header)] != years:
        fail("Unexpected almanac year-column order")
    first = datetime.strptime(first_date, "%Y-%m-%d")
    days = [first + timedelta(days=i) for i in range(8)]
    if any(d.year != first.year for d in days):
        fail("Year-wrap pages require a reviewed parser; no automatic campaign-year remapping")
    expected = [str(d.day) + "-" + MONTHS[d.month - 1] for d in days]
    labels = re.findall(DATE_LABEL, regular)
    if len(labels) != 8 or set(labels) != set(expected):
        fail("Page date labels disagree with the eight-day --first-date anchor")
    lines = [line for line in layout.splitlines()
             if re.match(r"^\s*(?:0|4|8|12|16|20)\s+\d+\s+\d+,\d+\s", line)]
    if len(lines) != 48:
        fail("Expected exactly 48 solar rows, got " + str(len(lines)))
    # Only unambiguous adjacent SD/date tokens; irregular metadata remains null.
    # An incomplete SD extraction must never silently fabricate a whole table.
    sd_by_date = {}
    for sd, label in re.findall(r"(?<![\d,])(1[56],\d)\s+(" + DATE_LABEL + r")", regular):
        number = float(sd.replace(",", "."))
        if label in sd_by_date:
            fail("Ambiguous repeated SD/date pair: " + label)
        sd_by_date[label] = number
    rows = []
    for index, line in enumerate(lines):
        tokens = line.split()
        hour = (index % 6) * 4
        if int(tokens[0]) != hour or len(tokens) < 29:
            fail("Missing/reordered hour or columns at row " + str(index + 1))
        day = days[index // 6]
        for column, year in enumerate(years):
            try:
                moment = datetime(year, day.month, day.day, hour, tzinfo=timezone.utc)
            except ValueError:
                fail("Leap-day pages require a reviewed parser; no automatic year remapping")
            q = tokens[1 + 4 * column:5 + 4 * column]
            printed_gha = printed_angle(q[0], q[1])
            dec = printed_angle(q[2], q[3])
            finite(printed_gha, "printed GHA", 0, 360.0)
            gha = printed_gha % 360.0
            finite(dec, "printed declination", -90, 90)
            rows.append({"utc": iso(moment), "gha_deg": gha, "declination_deg": dec,
                         "sd_arcmin": sd_by_date.get(expected[index // 6]),
                         "pdf_page": page, "day_block": index // 6 + 1, "hour": hour,
                         "printed_tokens": q, "printed_row": line})
    return {"schema": 1, "body": "Sun", "pdf_page": page,
            "first_date_anchor": first_date, "printed_date_labels_extraction_order": labels,
            "printed_time_headers": [line.strip() for line in layout.splitlines() if line.strip().startswith("Time =")],
            "time_convention": "row hours provisionally treated as GMT/UT1; port header NOT applied",
            "warnings": [LIMITATION, "SD only from unambiguous adjacent tokens; missing SD stays null. Leap-day/year-wrap pages unsupported."],
            "rows": rows}


def extract_tables(args):
    from pypdf import PdfReader

    if sha256(args.pdf) != ALMANAC_HASH:
        fail("Almanac hash differs from the checked user's original; review before extracting")
    reader = PdfReader(args.pdf)
    if not 1 <= args.page <= len(reader.pages):
        fail("PDF page out of range")
    page = reader.pages[args.page - 1]
    result = parse_solar_page(page.extract_text(extraction_mode="layout"), page.extract_text() or "",
                              args.page, args.first_date)
    result["source"] = {"path": str(Path(args.pdf).resolve()), "sha256": ALMANAC_HASH}
    return result


def load_tables(path):
    data = read_json(path)
    if data.get("schema") != 1 or data.get("body") != "Sun" or data.get("source", {}).get("sha256") != ALMANAC_HASH:
        fail("Expected a Sun-page extraction with the checked almanac provenance")
    moments = [utc_time(row["utc"]) for row in data["rows"]]
    if not moments or len(moments) != len(set(moments)):
        fail("Empty or duplicate almanac rows")
    for row in data["rows"]:
        finite(row["gha_deg"], "table GHA", 0, 359.999999999)
        finite(row["declination_deg"], "table declination", -90, 90)
        if row["sd_arcmin"] is not None:
            finite(row["sd_arcmin"], "table solar SD", 15, 17)
    return data


def merge_tables(paths):
    pages = [load_tables(path) for path in paths]
    rows = sorted((row for page in pages for row in page["rows"]), key=lambda row: row["utc"])
    if len(rows) != len({row["utc"] for row in rows}):
        fail("Overlapping input pages; do not count the same almanac row twice")
    return {"schema": 1, "body": "Sun", "source": pages[0]["source"],
            "pages": [{"pdf_page": page["pdf_page"], "first_date_anchor": page["first_date_anchor"],
                       "printed_time_headers": page["printed_time_headers"], "input_sha256": sha256(path)}
                      for page, path in zip(pages, paths)],
            "time_convention": "row hours provisionally treated as GMT/UT1; port header NOT applied",
            "warnings": [LIMITATION, "Selected pages only; gaps cannot be interpolated across."], "rows": rows}


def bracket(axis, value, label):
    finite(value, label, axis[0], axis[-1])
    for i, item in enumerate(axis):
        if value == item:
            return i, i, 0.0
        if value < item:
            return i - 1, i, (value - axis[i - 1]) / (item - axis[i - 1])
    fail(label + " outside verified F VIII subset; supply an explicitly sourced manual correction")


def fviii(h_deg, eye_height_m, month, fixture):
    if month not in range(1, 13):
        fail("Bad month")
    a, b, altitude_weight = bracket(fixture["altitudes_deg"], h_deg, "index-corrected h")
    c, d, height_weight = bracket(fixture["eye_heights_m"], eye_height_m, "eye height")
    grid = fixture["base_arcmin"]
    low = grid[a][c] + (grid[a][d] - grid[a][c]) * height_weight
    high = grid[b][c] + (grid[b][d] - grid[b][c]) * height_weight
    base = low + (high - low) * altitude_weight
    return {"base_arcmin": base, "monthly_arcmin": fixture["monthly_lower_limb_arcmin"][month - 1],
            "source": fixture["source"], "pdf_page": fixture["pdf_page"],
            "cells": [{"h_deg": fixture["altitudes_deg"][row], "eye_height_m": fixture["eye_heights_m"][col],
                       "base_arcmin": grid[row][col]} for row in sorted({a, b}) for col in sorted({c, d})],
            "altitude_weight": altitude_weight, "height_weight": height_weight,
            "includes_sd_dip_refraction": True}


def table_place(lat, lon, gha, dec):
    """Geocentric centre; longitude positive east, GHA positive west."""
    phi, delta, lha = map(math.radians, (lat, dec, (gha + lon) % 360.0))
    e = -math.cos(delta) * math.sin(lha)
    n = math.sin(delta) * math.cos(phi) - math.cos(delta) * math.cos(lha) * math.sin(phi)
    u = math.sin(delta) * math.sin(phi) + math.cos(delta) * math.cos(lha) * math.cos(phi)
    hc = math.degrees(math.asin(max(-1.0, min(1.0, u))))
    az = math.degrees(math.atan2(e, n)) % 360.0 if math.hypot(e, n) > 1e-12 else None
    return hc, az


def interpolated_place(tables, moment):
    rows = sorted((utc_time(r["utc"]), r) for r in tables["rows"] if utc_time(r["utc"]).year == moment.year)
    if not rows or not rows[0][0] <= moment <= rows[-1][0]:
        fail("No almanac bracket at this GMT; no extrapolation")
    for i, (when, row) in enumerate(rows):
        if when == moment:
            return {"gha_deg": row["gha_deg"], "declination_deg": row["declination_deg"],
                    "gmt": iso(moment), "source": tables["source"], "weight": 0.0, "brackets": [row]}
        if when > moment:
            before, previous = rows[i - 1]
            interval = (when - before).total_seconds()
            if interval != 4 * 3600:
                fail("Almanac bracket is not four hours")
            weight = (moment - before).total_seconds() / interval
            gha = (previous["gha_deg"] + weight * ((row["gha_deg"] - previous["gha_deg"]) % 360.0)) % 360.0
            dec = previous["declination_deg"] + weight * (row["declination_deg"] - previous["declination_deg"])
            return {"gha_deg": gha, "declination_deg": dec, "gmt": iso(moment), "source": tables["source"],
                    "method": "linear four-hour interpolation of printed rows; NOT the full printed increment procedure",
                    "weight": weight, "brackets": [previous, row]}
    fail("No bracket")


def only_keys(data, allowed, label):
    if not isinstance(data, dict):
        fail(label + " must be an object")
    unknown = set(data) - set(allowed)
    if unknown:
        fail("Unsupported " + label + " fields (no duplicate SD/dip/refraction/SUNDRIFT): " + ", ".join(sorted(unknown)))


def reduce_sight(sight, fixture, tables=None):
    only_keys(sight, {"id", "gmt", "raw_hs_deg", "index_correction_arcmin", "index_correction_source", "limb",
                      "eye_height_m", "horizon_reference", "assumed_lat_deg", "assumed_lon_deg", "table", "manual_fviii"}, "sight")
    moment = utc_time(sight["gmt"])
    if sight["limb"] != "lower":
        fail("This bench implements the solar LOWER limb only")
    hs = finite(sight["raw_hs_deg"], "raw Hs", 0, 90)
    index = finite(sight["index_correction_arcmin"], "signed additive index correction", -5, 5)
    text(sight["index_correction_source"], "index correction source")
    eye = finite(sight["eye_height_m"], "eye height above the chosen sea horizon", 0, 60)
    text(sight["horizon_reference"], "horizon reference")
    lat = finite(sight["assumed_lat_deg"], "assumed latitude", -90, 90)
    lon = finite(sight["assumed_lon_deg"], "assumed east-positive longitude", -180, 180)
    h = hs + index / 60.0
    if "manual_fviii" in sight:
        supplied = sight["manual_fviii"]
        only_keys(supplied, {"base_arcmin", "monthly_arcmin", "source", "includes_sd_dip_refraction"}, "manual F VIII")
        if supplied.get("includes_sd_dip_refraction") is not True:
            fail("F VIII must already include SD/dip/refraction, each applied once")
        if supplied["monthly_arcmin"] != fixture["monthly_lower_limb_arcmin"][moment.month - 1]:
            fail("Manual monthly term disagrees with the lower-limb F VIII row for this month")
        correction = {"base_arcmin": finite(supplied["base_arcmin"], "F VIII base", -60, 60),
                      "monthly_arcmin": finite(supplied["monthly_arcmin"], "F VIII month", -1, 1),
                      "source": text(supplied["source"], "manual F VIII source"), "includes_sd_dip_refraction": True}
    else:
        correction = fviii(h, eye, moment.month, fixture)
    if tables is not None:
        if "table" in sight:
            fail("Choose --tables interpolation OR explicit sight.table, never both")
        place = interpolated_place(tables, moment)
    else:
        place = sight["table"]
        only_keys(place, {"gha_deg", "declination_deg", "gmt", "source"}, "almanac place")
        text(place["source"], "almanac source/page/increment procedure")
        if utc_time(place["gmt"]) != moment:
            fail("Almanac place GMT does not match the sight GMT")
    gha = finite(place["gha_deg"], "GHA", 0, 359.999999999)
    dec = finite(place["declination_deg"], "declination", -90, 90)
    ho = h + (correction["base_arcmin"] + correction["monthly_arcmin"]) / 60.0
    hc, az = table_place(lat, lon, gha, dec)
    intercept = (ho - hc) * 60.0
    return {"schema": 1, "status": "diagnostic-not-a-validated-fix", "input": sight,
            "chain": {"raw_hs_deg": hs, "index_correction_arcmin": index, "index_corrected_h_deg": h,
                      "fviii": correction, "total_fviii_arcmin": correction["base_arcmin"] + correction["monthly_arcmin"],
                      "centre_ho_deg": ho, "centre_hc_deg": hc, "azimuth_deg": az, "intercept_nm": intercept,
                      "direction": "toward" if intercept > 0 else "away" if intercept < 0 else "on-line"},
            "almanac_place": place, "warnings": [LIMITATION, "Single sight is one LOP, not a position fix. No live observations or frozen values are changed."]}


def summary(rows, metric):
    values = [r[metric] for r in rows if r.get(metric) is not None]
    return {"samples": len(values), "min_arcmin": min(values), "max_arcmin": max(values),
            "max_abs_arcmin": max(map(abs, values))} if values else {"samples": 0}


def compare_reference(args):
    from skyfield.api import load, load_file

    tables = load_tables(args.tables)
    kernel = Path(args.kernel).resolve(strict=True)
    if sha256(kernel) != DE421_HASH:
        fail("Kernel is not the checked local DE421; do not label a different kernel as this reference")
    ts = load.timescale(builtin=True)
    csharp = read_json(args.csharp) if args.csharp else None
    if csharp and csharp["inputSha256"] != sha256(args.tables):
        fail("C# output was made from different table input")
    models = {utc_time(r["utc"]): r for r in csharp["rows"]} if csharp else {}
    if csharp and len(models) != len(csharp["rows"]):
        fail("Duplicate C# comparison rows")
    eph = load_file(str(kernel))  # load_file does not download a missing kernel.
    results = []
    try:
        for row in tables["rows"]:
            moment = utc_time(row["utc"])
            shifted = moment + timedelta(hours=args.hour_shift)
            t = ts.ut1(shifted.year, shifted.month, shifted.day, shifted.hour, shifted.minute, shifted.second)
            ra, dec, distance = eph["earth"].at(t).observe(eph["sun"]).apparent().radec(epoch="date")
            gha = (t.gast * 15.0 - ra.hours * 15.0) % 360.0
            sd = math.degrees(math.asin(695700.0 / distance.km)) * 60.0
            result = dict(row, jpl_gha_deg=gha, jpl_dec_deg=dec.degrees, jpl_sd_arcmin=sd,
                          table_minus_jpl_gha_arcmin=wrap180(row["gha_deg"] - gha) * 60.0,
                          table_minus_jpl_dec_arcmin=(row["declination_deg"] - dec.degrees) * 60.0,
                          table_minus_jpl_sd_arcmin=(row["sd_arcmin"] - sd) if row["sd_arcmin"] is not None else None)
            if csharp:
                if moment not in models:
                    fail("C# row missing: " + iso(moment))
                for key in ("uncalibrated", "calibrated"):
                    model = models[moment][key]
                    result[key + "_minus_jpl_gha_arcmin"] = wrap180(model["ghaDeg"] - gha) * 60.0
                    result[key + "_minus_jpl_dec_arcmin"] = (model["decDeg"] - dec.degrees) * 60.0
                    result[key + "_minus_table_gha_arcmin"] = wrap180(model["ghaDeg"] - row["gha_deg"]) * 60.0
                    result[key + "_minus_table_dec_arcmin"] = (model["decDeg"] - row["declination_deg"]) * 60.0
                    result[key + "_minus_jpl_sd_arcmin"] = model["diameterDeg"] * 30.0 - sd
            results.append(result)
    finally:
        eph.close()
    if not results:
        fail("No comparison rows")
    metrics = sorted(k for k in results[0] if k.endswith("_arcmin") and "minus" in k)
    by_page = {str(page): {metric: summary([row for row in results if row["pdf_page"] == page], metric)
                          for metric in metrics} for page in sorted({row["pdf_page"] for row in results})}
    by_hour = {str(hour): {metric: summary([row for row in results if row["hour"] == hour], metric)
                          for metric in metrics} for hour in sorted({row["hour"] for row in results})}
    return {"schema": 1, "method": "SH5 printed Sun vs apparent geocentric JPL DE421 (GAST); row hours assumed UT1",
            "hour_shift_diagnostic_only": args.hour_shift, "kernel_sha256": sha256(kernel),
            "tables_sha256": sha256(args.tables), "csharp_receipt": {k: v for k, v in csharp.items() if k != "rows"} if csharp else None,
            "summary": {metric: summary(results, metric) for metric in metrics},
            "by_pdf_page": by_page, "by_hour": by_hour, "rows": results,
            "warnings": [LIMITATION, "C# uses GMST, JPL uses GAST. Time-shift experiments do NOT modify calibration or establish the table convention."]}


def audit_from_snapshot(snapshot):
    if snapshot.get("schema") != 1 or snapshot.get("status") != "captured-configuration":
        fail("Residuals require a captured configuration, not an unavailable/legacy probe")
    sextants = [record["data"] for record in snapshot["records"] if record["route"] == "/sextant"]
    if len(sextants) != 1:
        fail("Snapshot must contain exactly one /sextant read")
    sextant = sextants[0]
    audit = sextant["solarAudit"]
    if audit.get("schema") != 1 or audit.get("ok") is not True or audit.get("campaignUtc") is not True:
        fail("Solar audit is incomplete")
    return audit


def residual_report(snapshot, reduction):
    """Configuration residuals at one explicitly matched raw sight, not a fit
    or a guessed atmospheric/horizon correction. AP and live position differ.
    """
    audit = audit_from_snapshot(snapshot)
    moment = utc_time(audit["utc"])
    sight = reduction["input"]
    if moment != utc_time(sight["gmt"]):
        fail("Raw sight and solarAudit must use the SAME GMT; no matching to a later sight")
    if abs(finite(audit["rawHsDeg"], "snapshot raw Hs") - sight["raw_hs_deg"]) > 1e-6:
        fail("Raw arc does not match the configuration snapshot")
    if abs(finite(audit["indexCorrectionArcMin"], "snapshot index correction") - sight["index_correction_arcmin"]) > 1e-6:
        fail("Index correction does not match the configuration snapshot")
    position = audit["position"]
    if not isinstance(position, dict):
        fail("Live position missing; do not substitute an assumed position")
    lat = finite(position["latDeg"], "live latitude", -90, 90)
    lon = finite(position["lonDeg"], "live longitude", -180, 180)
    place = reduction["almanac_place"]
    table_alt, table_az = table_place(lat, lon, place["gha_deg"], place["declination_deg"])
    metrics = {"table_centre_at_live_position": {"alt_deg": table_alt, "az_deg": table_az},
               "external_reduction_at_assumed_position": reduction["chain"]}
    for key in ("uncalibrated", "calibrated"):
        model = audit[key]
        if not isinstance(model, dict):
            fail("Both explicit-time C# model samples are required")
        alt = finite(model["altDeg"], key + " model altitude", -90, 90)
        metrics[key + "_minus_table_alt_arcmin"] = (alt - table_alt) * 60.0
        metrics[key + "_minus_table_gha_arcmin"] = wrap180(model["ghaDeg"] - place["gha_deg"]) * 60.0
        metrics[key + "_minus_table_dec_arcmin"] = (model["decDeg"] - place["declination_deg"]) * 60.0
    metrics["calibration_alt_shift_arcmin"] = (audit["calibrated"]["altDeg"] - audit["uncalibrated"]["altDeg"]) * 60.0
    native = audit.get("nativeSun")
    measuring = audit.get("measuringSun", {})
    configured = measuring.get("configuredDirection")
    for name, direction in (("native", native), ("configured", configured)):
        if direction is None:
            metrics[name + "_minus_table_alt_arcmin"] = None
            continue
        alt = finite(direction["altDeg"], name + " altitude", -90, 90)
        metrics[name + "_minus_table_alt_arcmin"] = (alt - table_alt) * 60.0
        metrics[name + "_minus_uncalibrated_alt_arcmin"] = (alt - audit["uncalibrated"]["altDeg"]) * 60.0
        metrics[name + "_minus_calibrated_alt_arcmin"] = (alt - audit["calibrated"]["altDeg"]) * 60.0
        az = finite(direction["azDeg"], name + " azimuth", 0, 360)
        metrics[name + "_minus_table_az_arcmin"] = wrap180(az - table_az) * 60.0 if table_az is not None else None
    if configured is not None and native is not None:
        metrics["configured_minus_native_alt_arcmin"] = (configured["altDeg"] - native["altDeg"]) * 60.0
    if configured is not None and measuring.get("ready") is True and measuring.get("active") is True:
        diameter = finite(measuring["configuredDiameterDeg"], "configured solar diameter", 0.1, 1.0)
        lower_alt = configured["altDeg"] - diameter / 2.0
        metrics["configured_geometric_lower_limb_alt_deg"] = lower_alt
        metrics["raw_hs_minus_geometric_limb_arcmin"] = (sight["raw_hs_deg"] - lower_alt) * 60.0
        metrics["index_corrected_h_minus_geometric_limb_arcmin"] = (reduction["chain"]["index_corrected_h_deg"] - lower_alt) * 60.0
    return {"schema": 1, "status": "configuration-residuals-not-an-accepted-sight", "utc": iso(moment),
            "solar_audit": audit, "metrics": metrics, "almanac_place": place,
            "warnings": [LIMITATION, "AP reduction and live-position direction comparisons are kept separate.",
                         "raw-h minus geometric limb includes unknown horizon/aim/contact/refraction effects; NOT a fitted correction.",
                         "Direction/configured size can be stale LateUpdate state. No same-frame visible-limb or GPU proof."]}


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise URLError("Bridge redirects are forbidden")


def local_bridge_url(value):
    parts = urlsplit(value)
    if (parts.scheme != "http" or parts.hostname not in {"127.0.0.1", "localhost", "::1"}
            or parts.username or parts.password or parts.query or parts.fragment or parts.path not in {"", "/"}):
        fail("Bridge must be a local http://localhost:port base URL without path/credentials")
    return value.rstrip("/")


def get_json(base, route, timeout):
    opener = build_opener(ProxyHandler({}), NoRedirect())
    with opener.open(Request(base + route, method="GET"), timeout=timeout) as response:
        raw = response.read(2 * 1024 * 1024 + 1)
    if len(raw) > 2 * 1024 * 1024:
        fail("Bridge JSON is too large")
    return json.loads(raw.decode("utf-8"), parse_constant=lambda value: fail("Non-finite bridge JSON: " + value))


def capture_snapshot(base, timeout, getter=get_json):
    base = local_bridge_url(base)
    records = []
    # No /set, /record, /reset, prefs, save, load, pause or camera commands.
    for route in ("/time", "/sextant", "/sky", "/state?fields=position,time", "/time"):
        read_at = datetime.now(timezone.utc)
        try:
            data = getter(base, route, timeout)
            if not isinstance(data, dict):
                fail("Bridge reply is not a JSON object")
            records.append({"route": route, "read_at": iso(read_at), "data": data})
        except (OSError, ValueError) as error:
            records.append({"route": route, "read_at": iso(read_at), "error": str(error)})
            return {"schema": 1, "status": "unavailable", "records": records,
                    "warnings": ["No game restart or writes attempted; failed input is NOT an accepted sight."]}
    sextant = records[1]["data"]
    audit = sextant.get("solarAudit")
    complete = (sextant.get("ok") is True and sextant.get("loaded") is True
                and isinstance(audit, dict) and audit.get("schema") == 1 and audit.get("ok") is True and audit.get("campaignUtc") is True
                and isinstance(audit.get("position"), dict)
                and all(isinstance(audit.get(key), dict) for key in ("uncalibrated", "calibrated"))
                and all(r["data"].get("ok") is True for r in records))
    return {"schema": 1, "status": "captured-configuration" if complete else "incomplete",
            "records": records, "warnings": [LIMITATION,
                "Only solarAudit shares one GMT/position read. HTTP routes and presented pixels are NOT atomic; /time brackets retain clock motion.",
                "Native isolation readiness/recent entry is NOT GPU submission. Camera worldY is NOT a measured sea-relative eye height."]}


def projection(diameter, fov, target_height, observed=None):
    diameter = finite(diameter, "diameter degrees", 0.000001, 10)
    fov = finite(fov, "vertical FOV degrees", 0.01, 179)
    height = finite(target_height, "target height pixels", 1, 32768)
    predicted = height * math.tan(math.radians(diameter / 2)) / math.tan(math.radians(fov / 2))
    result = {"configured_diameter_deg": diameter, "fov_deg": fov, "target_height_px": height,
              "expected_on_axis_diameter_px": predicted,
              "warning": "Projection only, no visible-limb acceptance; use the PIP target scale, not an unrelated screen/crop scale."}
    if observed is not None:
        measured = finite(observed, "manually measured diameter pixels", 0.000001, 32768)
        result["observed_diameter_px"] = measured
        result["observed_angular_diameter_deg"] = math.degrees(2 * math.atan(measured / height * math.tan(math.radians(fov / 2))))
    return result


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    commands = result.add_subparsers(dest="command", required=True)
    tables = commands.add_parser("tables", help="Extract one checked Sun page, retaining original tokens")
    tables.add_argument("--pdf", type=Path, default=REFERENCES / ALMANAC_NAME)
    tables.add_argument("--page", type=int, default=57, help="1-based PDF page")
    tables.add_argument("--first-date", default="1940-08-28", help="Explicit chronological anchor (use leap year for the printed date set)")
    tables.add_argument("--out", type=Path, default=DEFAULT_OUT / "tables.json")
    merge = commands.add_parser("merge", help="Combine nonoverlapping extracted solar pages; retain page/input receipts")
    merge.add_argument("--inputs", type=Path, nargs="+", required=True)
    merge.add_argument("--out", type=Path, default=DEFAULT_OUT / "tables-multi.json")
    reference = commands.add_parser("reference", help="Compare table rows with a local JPL kernel and optional actual C# output")
    reference.add_argument("--tables", type=Path, required=True)
    reference.add_argument("--kernel", type=Path, required=True, help="Existing local de421.bsp; never downloaded")
    reference.add_argument("--csharp", type=Path)
    reference.add_argument("--hour-shift", type=float, choices=(-1, 0, 1), default=0)
    reference.add_argument("--out", type=Path, default=DEFAULT_OUT / "reference.json")
    reduce = commands.add_parser("reduce", help="Hs -> additive index -> F VIII ONCE -> centre Ho -> Ho-Hc")
    reduce.add_argument("--input", type=Path, required=True)
    reduce.add_argument("--tables", type=Path, help="Diagnostic four-hour interpolation; alternatively supply exact sight.table")
    reduce.add_argument("--out", type=Path, default=DEFAULT_OUT / "reduction.json")
    residual = commands.add_parser("residual", help="Matched snapshot + raw-sight reduction, keeping AP/live position separate")
    residual.add_argument("--snapshot", type=Path, required=True)
    residual.add_argument("--input", type=Path, required=True)
    residual.add_argument("--tables", type=Path)
    residual.add_argument("--out", type=Path, default=DEFAULT_OUT / "residual.json")
    snapshot = commands.add_parser("snapshot", help="GET-only bridge snapshot, never I/O keys or game/save/settings changes")
    snapshot.add_argument("--bridge", default="http://127.0.0.1:28777")
    snapshot.add_argument("--timeout", type=float, default=5)
    snapshot.add_argument("--out", type=Path, default=DEFAULT_OUT / "snapshot.json")
    pixels = commands.add_parser("projection", help="Predict on-axis limb pixels; invert a manually measured diameter")
    pixels.add_argument("--diameter-deg", type=float, required=True)
    pixels.add_argument("--fov-deg", type=float, required=True)
    pixels.add_argument("--target-height-px", type=float, required=True)
    pixels.add_argument("--observed-px", type=float)
    pixels.add_argument("--out", type=Path, default=DEFAULT_OUT / "projection.json")
    return result


def main(argv=None):
    args = parser().parse_args(argv)
    try:
        inputs = [getattr(args, key) for key in ("input", "tables", "pdf", "kernel", "csharp", "snapshot") if getattr(args, key, None)]
        inputs.extend(getattr(args, "inputs", []))
        report_path(args.out, inputs)
        if args.command == "tables":
            payload = extract_tables(args)
        elif args.command == "merge":
            payload = merge_tables(args.inputs)
        elif args.command == "reference":
            payload = compare_reference(args)
        elif args.command in {"reduce", "residual"}:
            fixture = read_json(FVIII_PATH)
            pdf = ROOT / fixture["source"]
            if sha256(pdf) != fixture["sha256"]:
                fail("F VIII reference PDF differs from the visually verified fixture")
            payload = reduce_sight(read_json(args.input), fixture, load_tables(args.tables) if args.tables else None)
            if args.command == "residual":
                payload = residual_report(read_json(args.snapshot), payload)
                payload["snapshot_sha256"] = sha256(args.snapshot)
            payload["input_sha256"] = sha256(args.input)
            payload["fviii_fixture_sha256"] = sha256(FVIII_PATH)
            payload["tables_sha256"] = sha256(args.tables) if args.tables else None
        elif args.command == "snapshot":
            finite(args.timeout, "timeout seconds", 0.01, 30)
            payload = capture_snapshot(args.bridge, args.timeout)
        else:
            payload = projection(args.diameter_deg, args.fov_deg, args.target_height_px, args.observed_px)
        output = write_report(args.out, payload, inputs)
        print(str(output))
        if "summary" in payload:
            print(json.dumps(payload["summary"], indent=2))
        if "chain" in payload:
            print(json.dumps(payload["chain"], ensure_ascii=False, indent=2))
        if payload.get("status") in {"unavailable", "incomplete"}:
            print(payload["status"], file=sys.stderr)
            return 2
        return 0
    except (OSError, ValueError, KeyError, ImportError) as error:
        print("Solar audit: " + str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
