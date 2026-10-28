"""Reproducible SH5 Sun pack; originals are read-only, output is artifacts/.

Uses positional year columns, including the EMPTY non-leap Feb 29 columns.
The d column is the signed FOUR-hour declination increment (PDF p.73).
No JPL fitting, time shift, SD substitution or missing-row fabrication.
"""

import argparse
import hashlib
import importlib.util
import logging
import re
import struct
from datetime import datetime, timedelta, timezone
from pathlib import Path

from pypdf import PdfReader

spec = importlib.util.spec_from_file_location("solar_audit", Path(__file__).with_name("audit.py"))
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)
ANGLE = re.compile(r"(\d{1,3})\s+(\d+,\d)\s+([+−-]?\d{1,2})\s+(\d+,\d)")


def campaign(pdf):
    if audit.sha256(pdf) != audit.ALMANAC_HASH:
        audit.fail("Unexpected almanac hash")
    logging.getLogger("pypdf").setLevel(logging.ERROR)
    reader = PdfReader(pdf)
    records = {}
    first = datetime(1940, 1, 1)
    pages = []
    for page_no in range(27, 73):
        page = reader.pages[page_no - 1]
        regular = page.extract_text() or ""
        layout = page.extract_text(extraction_mode="layout") or ""
        count = 6 if page_no == 72 else 8
        days = [first + timedelta(days=i) for i in range(count)]
        labels = re.findall(audit.DATE_LABEL, regular)
        expected = [str(d.day) + "-" + audit.MONTHS[d.month - 1] for d in days]
        if len(labels) != count or set(labels) != set(expected):
            audit.fail("Unexpected dates at PDF page " + str(page_no))
        header = next(line for line in layout.splitlines() if re.search(r"1939\s+1940", line))
        if [int(v) for v in re.findall(r"19\d{2}", header)] != list(range(1939, 1946)):
            audit.fail("Unexpected year columns")
        lines = [line for line in layout.splitlines() if re.match(r"^\s*(?:0|4|8|12|16|20)\s+", line)]
        if len(lines) != count * 6:
            audit.fail("Unexpected hour rows at PDF page " + str(page_no))
        starts = [m.start() for m in ANGLE.finditer(lines[0])]
        if len(starts) != 7:
            audit.fail("Missing normal baseline columns")
        for i, line in enumerate(lines):
            day = days[i // 6]
            hour = i % 6 * 4
            if int(line.split()[0]) != hour:
                audit.fail("Reordered hours")
            increment = re.search(r"([+−-]?)\s+(\d+,\d)\s*$", line)
            if increment is None:
                audit.fail("Missing signed four-hour d at p." + str(page_no) + ": " + line)
            d = float(increment[2].replace(",", ".")) * (-1 if increment[1] in "−-" else 1)
            if increment[1] == "" and d != 0:
                audit.fail("Nonzero d without sign at p." + str(page_no))
            seen = set()
            for match in ANGLE.finditer(line):
                column = min(range(7), key=lambda n: abs(starts[n] - match.start()))
                if abs(starts[column] - match.start()) > 3 or column in seen:
                    audit.fail("Ambiguous positional year column")
                seen.add(column)
                year = 1939 + column
                try:
                    moment = datetime(year, day.month, day.day, hour, tzinfo=timezone.utc)
                except ValueError:
                    audit.fail("Data in a non-leap Feb 29 column")
                gha = audit.printed_angle(match[1], match[2]) % 360
                dec = audit.printed_angle(match[3], match[4])
                if not (0 <= gha < 360 and -25 <= dec <= 25 and abs(d) <= 5):
                    audit.fail("Solar value out of bounds")
                if moment in records:
                    audit.fail("Duplicate campaign time")
                records[moment] = {"utc": audit.iso(moment), "gha_deg": gha, "declination_deg": dec,
                                   "d_arcmin_per_four_hours": d, "pdf_page": page_no,
                                   "printed_tokens": list(match.groups()), "printed_row": line}
            expected_columns = {1, 5} if day.month == 2 and day.day == 29 else set(range(7))
            if seen != expected_columns:
                audit.fail("Missing or extra year column")
        pages.append({"pdf_page": page_no, "first_date": first.strftime("%m-%d"), "days": count})
        first += timedelta(days=count)
    start = datetime(1939, 1, 1, tzinfo=timezone.utc)
    end = datetime(1946, 1, 1, tzinfo=timezone.utc)
    moments = [start + timedelta(hours=4 * i) for i in range(int((end-start).total_seconds()/14400))]
    if set(moments) != set(records):
        audit.fail("Campaign is not complete")
    return {"schema": 1, "body": "Sun", "source_sha256": audit.ALMANAC_HASH,
            "method": "Printed four-hour rows at GMT; GHA adds 15 degrees/hour; signed d adds elapsed/4 hours. No SUNDRIFT.",
            "pages": pages, "rows": [records[m] for m in moments]}


def pack(data):
    result = bytearray(b"RNS1" + struct.pack("<i", len(data["rows"])) + bytes.fromhex(audit.ALMANAC_HASH))
    for row in data["rows"]:
        gha = round(row["gha_deg"] * 600) % 216000
        dec = round(row["declination_deg"] * 600)
        d = round(row["d_arcmin_per_four_hours"] * 10)
        result.extend(struct.pack("<ihh", gha, dec, d))
    return bytes(result)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pdf", type=Path, default=audit.REFERENCES / audit.ALMANAC_NAME)
    parser.add_argument("--out", type=Path, default=audit.ARTIFACTS / "reports/solar-table-20261008")
    args = parser.parse_args()
    output = audit.report_path(args.out / "sh5-sun.bin", [args.pdf])
    data = campaign(args.pdf)
    payload = pack(data)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(payload)
    audit.write_report(args.out / "campaign-extraction.json", data, [args.pdf])
    receipt = {"rows": len(data["rows"]), "bytes": len(payload), "sha256": hashlib.sha256(payload).hexdigest(),
               "source_sha256": audit.sha256(args.pdf), "first": data["rows"][0]["utc"], "last": data["rows"][-1]["utc"]}
    audit.write_report(args.out / "campaign-pack.json", receipt, [args.pdf])
    print(receipt)


if __name__ == "__main__":
    main()
