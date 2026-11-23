"""Build a small synthetic RNW1 tile for live atlas plumbing tests.

Not a climatology: a lat/lon rectangle with a sharp 1 m/s east plateau
around the given centre, zero elsewhere, constant at all depths.
"""
import argparse
import calendar
import hashlib
import struct
from datetime import datetime, timedelta
from pathlib import Path

SCALE = 10000  # 1.0 m/s


def axis(center, before, after):
    return [center - 0.15, center - before, center + after, center + 0.15]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--lat", type=float, required=True)
    parser.add_argument("--lon", type=float, required=True)
    parser.add_argument("--utc", required=True, help="campaign UTC, e.g. 1941-11-05T03:00:00Z")
    args = parser.parse_args()

    start = datetime.strptime(args.utc, "%Y-%m-%dT%H:%M:%SZ")
    t0 = calendar.timegm(start.timetuple())
    times = [t0 - 3600, t0 + 3600]
    depths = [0.0, 30.0]
    latitudes = [args.lat - 0.15, args.lat - 0.01, args.lat,
                 args.lat + 0.01, args.lat + 0.05, args.lat + 0.15]
    longitudes = [args.lon - 0.15, args.lon + 0.15]
    if not all(a < b for a, b in zip(latitudes, latitudes[1:])) \
            or not all(a < b for a, b in zip(longitudes, longitudes[1:])):
        raise SystemExit("degenerate axes")

    data = bytearray()
    data += b"RNW1"
    for size in (len(times), len(depths), len(latitudes), len(longitudes)):
        data += struct.pack("<H", size)
    for values in (times, depths, latitudes, longitudes):
        for value in values:
            data += struct.pack("<d", float(value))
    # The centre plateau (lat index <= 2) carries 1 m/s east; everything
    # north of it is zero. Longitude is constant across the small box.
    for _ in times:
        for _ in depths:
            for lat_index in range(len(latitudes)):
                for _ in longitudes:
                    east = SCALE if lat_index <= 2 else 0
                    data += struct.pack("<hh", east, 0)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(data)
    print(f"wrote {args.output} ({len(data)} B) sha256={hashlib.sha256(data).hexdigest()}")
    print(f"lat={latitudes} lon={longitudes} utc={(times[0], times[1])}")


if __name__ == "__main__":
    main()
