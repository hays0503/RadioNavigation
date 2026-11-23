"""Read-only audit of UBOAT's base elevation raster, not native scene terrain."""
import argparse
import hashlib
import json
import math
from pathlib import Path

import numpy as np


COLS, ROWS, TILE = 21601, 10801, 256
TX, TY = math.ceil(COLS / TILE), math.ceil(ROWS / TILE)
WORLD_M = 40075160.0
REGIONS = {
    "gibraltar": (-6.4, 35.7, -5.0, 36.4),
    "jade_approach": (7.7, 53.3, 8.6, 54.1),
    "open_atlantic": (-22.0, 46.0, -21.5, 46.5),
}
DEPTHS = (0, 10, 20, 50, 100, 200)


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def game_index(lon, lat):
    # Ideal geographic-to-encounter mapping, not a native Unity float query.
    scale = COLS * 2.495311310048419e-8
    return (WORLD_M * (0.5 + lon / 360) * scale,
            WORLD_M * (0.25 - lat / 360) * scale)


def bilinear(nodes, x, y):
    x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
    a, b = x - x0, y - y0
    values = [np.asarray(nodes(x0 + dx, y0 + dy), dtype=np.float64)
              for dy in (0, 1) for dx in (0, 1)]
    if not all(np.isfinite(v).all() for v in values):
        raise ValueError("nonfinite elevation")
    v00, v10, v01, v11 = values
    return (v00 * (1 - a) + v10 * a) * (1 - b) + (v01 * (1 - a) + v11 * a) * b


def breaks(lo, hi):
    if not lo <= hi:
        raise ValueError("reversed footprint")
    return [lo, *range(math.floor(lo) + 1, math.ceil(hi)), hi]


def bounds(nodes, x0, y0, x1, y1):
    # Clipping to each native patch leaves rectangles. On each rectangle a
    # bilinear function reaches its extremes at corners, including edge ties.
    x, y = np.meshgrid(breaks(x0, x1), breaks(y0, y1))
    values = bilinear(nodes, x, y)
    return float(values.min()), float(values.max())


def conservative_limit(shoal_elevation, allowance, quantum):
    # Positive-down support ceiling; round toward a shallower limit.
    # Returning 0 cannot authorize even z=0 under the strict test z < limit.
    return max(0, math.floor((-shoal_elevation - allowance) / quantum))


class Raster:
    def __init__(self, tiled_path, flat_path):
        self.tiles = self._open(tiled_path, (TY, TX, TILE, TILE))
        self.flat = self._open(flat_path, (ROWS, COLS))

    @staticmethod
    def _open(path, shape):
        if path.stat().st_size != 4 * math.prod(shape):
            raise ValueError(f"unexpected input size: {path}")
        return np.memmap(path, dtype="<f4", mode="r", shape=shape)

    def nodes(self, x, y):
        if (np.any(x < 0) or np.any(y < 0) or np.any(x >= COLS) or np.any(y >= ROWS)):
            raise ValueError("out of domain: audit does not use native edge padding")
        return self.tiles[y // TILE, x // TILE, y % TILE, x % TILE]


def compare_nodes(raster, x0, y0, x1, y1):
    x, y = np.meshgrid(np.arange(math.floor(x0), math.floor(x1) + 2),
                       np.arange(math.floor(y0), math.floor(y1) + 2))
    tiled, flat = raster.nodes(x, y), raster.flat[y, x]
    return dict(checked=int(x.size), unequal=int((tiled != flat).sum()),
                max_abs_m=float(np.max(np.abs(tiled.astype(float) - flat))))


def audit_region(raster, region):
    w, s, e, n = region
    nx = round((e - w) * 20)
    ny = round((n - s) * 20)
    lons, lats = np.linspace(w, e, nx + 1), np.linspace(n, s, ny + 1)
    x, y = game_index(lons, lats)
    raw = compare_nodes(raster, x[0], y[0], x[-1], y[-1])
    shallow = np.empty((ny, nx))
    deep = np.empty_like(shallow)
    for j in range(ny):
        for i in range(nx):
            deep[j, i], shallow[j, i] = bounds(raster.nodes, x[i], y[j], x[i + 1], y[j + 1])
    a, b = np.meshgrid(x, y)
    outer = bilinear(raster.nodes, a, b)
    corner_max = np.maximum.reduce((outer[:-1, :-1], outer[1:, :-1],
                                   outer[:-1, 1:], outer[1:, 1:]))
    a, b = np.meshgrid((x[:-1] + x[1:]) / 2, (y[:-1] + y[1:]) / 2)
    center = bilinear(raster.nodes, a, b)
    if not (np.all(shallow >= corner_max - 1e-8) and np.all(shallow >= center - 1e-8)):
        raise AssertionError("whole footprint cannot be deeper than its probes")
    thresholds = {}
    # A 1 cm diagnostic exclusion band removes numerical threshold ties;
    # it is not a clearance or vertical datum allowance for production.
    for d in DEPTHS:
        blocked = shallow > -d + .01
        thresholds[str(d)] = dict(
            whole_deeper=int((shallow < -d - .01).sum()),
            false_center=int(((center < -d - .01) & blocked).sum()),
            false_four_corners=int(((corner_max < -d - .01) & blocked).sum()))
    return dict(raw_nodes=raw, cells=nx * ny, min_elevation_m=float(deep.min()),
                max_elevation_m=float(shallow.max()),
                max_corner_understates_shoal_m=float((shallow - corner_max).max()),
                thresholds=thresholds)


def self_check():
    # Integer-native ridge hidden from the coarse footprint corners AND centre.
    ridge = np.full((5, 5), -100.0)
    ridge[1, 1] = -3
    sample = lambda x, y: ridge[y, x]
    assert bounds(sample, .2, .3, 3.8, 3.6) == (-100, -3), "interior_native_ridge"
    assert bilinear(sample, np.array(2.), np.array(1.95)) < -20
    # Clipped footprint cannot use the unclipped native node minimum/maximum.
    plane = lambda x, y: 2 * x - 3 * y
    lo, hi = bounds(plane, .25, .5, .75, .9)
    assert math.isclose(lo, -2.2) and math.isclose(hi, 0, abs_tol=1e-12)
    # Guard against rounding a shallow threshold up, or admitting a tie.
    limit = conservative_limit(-19.9, .5, 1)
    assert limit == 19 and 18.99 < limit and not 19 < limit, "conservative_depth_rounding"
    assert conservative_limit(-.2, .5, 1) == 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tiled", type=Path, required=True)
    parser.add_argument("--flat", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    self_check()
    with args.output.open("x", encoding="utf-8") as stream:
        raster = Raster(args.tiled, args.flat)
        regions = {}
        for i, (name, region) in enumerate(REGIONS.items(), 1):
            print(f"DEPTH {i}/{len(REGIONS)} {name}", flush=True)
            regions[name] = audit_region(raster, region)
        print("DEPTH hashing local inputs", flush=True)
        json.dump(dict(scope=__doc__, numpy=np.__version__, tiled_sha256=digest(args.tiled),
                       flat_sha256=digest(args.flat), regions=regions), stream, indent=2)
        stream.write("\n")
    print(args.output)


if __name__ == "__main__":
    main()
