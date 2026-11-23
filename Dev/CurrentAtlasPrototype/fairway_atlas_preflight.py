"""Check RNW1 support on a recorded fairway, without starting the game."""
import argparse
from datetime import datetime, timedelta, timezone
import hashlib
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().with_name("CoastCompare")))
from atlantic_tile import Tile

KM_PER_DEGREE = 40075.16 / 360
EPOCH = datetime(1970, 1, 1, tzinfo=timezone.utc)


def inspect(tiles, points, times, depth):
    result = []
    for row in points:
        latitude = row["y"] / KM_PER_DEGREE
        longitude = row["x"] / KM_PER_DEGREE
        samples = []
        for utc in times:
            vectors = (tile.sample(utc, depth, latitude, longitude) for tile in tiles)
            samples.append(next((vector for vector in vectors if vector is not None), None))
        result.append(dict(**row, latitude=latitude, longitude=longitude, waterMps=samples))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("atlas", type=Path)
    parser.add_argument("track", type=Path)
    parser.add_argument("fairway", type=Path, help="fairway.json providing the berth trigger")
    parser.add_argument("output", type=Path)
    parser.add_argument("--utc", help="one explicit UTC epoch; default: every atlas monthly centre")
    parser.add_argument("--depth", type=float, default=0)
    args = parser.parse_args()
    paths = sorted(args.atlas.glob("*.rnw"))
    if not paths:
        parser.error("no RNW1 tiles")
    raw_tiles = [path.read_bytes() for path in paths]
    tiles = [Tile(raw) for raw in raw_tiles]
    if args.utc:
        epoch = datetime.fromisoformat(args.utc)
        if epoch.tzinfo is None or epoch.utcoffset().total_seconds() != 0:
            parser.error("UTC must include Z or +00:00")
        times = [(epoch - EPOCH).total_seconds()]
    else:
        times = sorted({float(t) for tile in tiles for t in tile.axes[0]})
    points = []
    for index, line in enumerate(args.track.read_text(encoding="utf-8-sig").splitlines()):
        row = json.loads(line)
        points.append(dict(point=index, x=row["x"], y=row["y"], distanceKm=row["distance"]))
    if not points:
        parser.error("empty trajectory")
    x, y = json.loads(args.fairway.read_text(encoding="utf-8-sig"))["trigger"]
    points.append(dict(point="berthTrigger", x=x, y=y, distanceKm=0))
    rows = inspect(tiles, points, times, args.depth)
    coverage = [sum(row["waterMps"][i] is not None for row in rows) for i in range(len(times))]
    complete = all(count == len(rows) for count in coverage)
    report = dict(coverage="FULL" if complete else "PARTIAL" if any(coverage) else "NONE",
                  depthM=args.depth, points=len(rows),
                  utc=[(EPOCH + timedelta(seconds=t)).isoformat() for t in times],
                  coveredPointCounts=coverage, rows=rows,
                  atlas={p.name: hashlib.sha256(raw).hexdigest() for p, raw in zip(paths, raw_tiles)},
                  note="Offline spatial preflight of recorded points, not a live ORA-20C passage")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2, allow_nan=False)
        stream.write("\n")
    print(f"{report['coverage']}: {coverage} of {len(rows)} points by UTC centre")


if __name__ == "__main__":
    main()
