"""Build two synthetic RNW1 tiles with a shared, exactly matching longitude face."""
import argparse
import calendar
from datetime import datetime
from itertools import product
from pathlib import Path
import struct


def write_tile(path, times, depths, latitudes, longitudes, seam_lon, width):
    axes = (times, depths, latitudes, longitudes)
    header = struct.pack("<4s4H", b"RNW1", *(len(axis) for axis in axes))
    coordinates = b"".join(struct.pack("<d", value) for axis in axes for value in axis)
    # East transport is continuous through the seam. North transport varies
    # with time, depth and latitude, so comparing only the surface is not enough.
    vectors = bytearray()
    for t, d, lat, lon in product(*axes):
        east = round(8500 - 7500 * abs(lon - seam_lon) / width)
        north = round(1500 + 10 * d + 200 * (t - times[0]) / 7200 + 300 * (lat - latitudes[0]))
        vectors += struct.pack("<hh", east, north)
    path.write_bytes(header + coordinates + vectors)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output_dir", type=Path)
    parser.add_argument("--lat", type=float, required=True)
    parser.add_argument("--lon", type=float, required=True)
    parser.add_argument("--utc", required=True, help="campaign UTC, e.g. 1941-11-05T03:08:50Z")
    parser.add_argument("--width", type=float, default=0.1, help="longitude span of each tile in degrees")
    args = parser.parse_args()
    if not 0 < args.width <= 1:
        parser.error("width must be in (0, 1] degrees")
    centre = calendar.timegm(datetime.strptime(args.utc, "%Y-%m-%dT%H:%M:%SZ").timetuple())
    args.output_dir.mkdir(parents=True, exist_ok=False)
    for name, longitude in (("west", [args.lon - args.width, args.lon]),
                            ("east", [args.lon, args.lon + args.width])):
        path = args.output_dir / f"{name}.rnw"
        write_tile(path, [centre - 3600, centre + 3600], [0.0, 30.0],
                   [args.lat - 0.15, args.lat + 0.15], longitude, args.lon, args.width)
        print(f"{path}: {path.stat().st_size} bytes; longitudes {longitude}")


if __name__ == "__main__":
    main()
