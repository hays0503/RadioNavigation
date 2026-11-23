"""Read-only point comparison of UBOAT's vector coast and Natural Earth land."""
import argparse
import hashlib
from io import BytesIO
import json
from pathlib import Path
import subprocess
import zipfile

import numpy as np
import shapefile
import shapely


# Integer lattice (1 unit = .005 degree), aligned for 10x10 coarse cells.
REGIONS = {
    "gibraltar": (-1280, 7140, -1000, 7280),
    "jade_approach": (1540, 10660, 1720, 10820),
    "open_atlantic": (-4400, 9200, -4300, 9300),
}


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def grid(region):
    x0, y0, x1, y1 = REGIONS[region]
    return np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))


def sampled_corner_misses(water):
    misses = 0
    for y in range(0, water.shape[0] - 1, 10):
        for x in range(0, water.shape[1] - 1, 10):
            part = water[y:y + 11, x:x + 11]
            if part[0, 0] and part[0, -1] and part[-1, 0] and part[-1, -1] and not part.all():
                misses += 1
    return misses


def polygon_conflicts(geometries, xs, ys, both_water):
    cells = shapely.box(xs[:-1:10, :-1:10] / 200, ys[:-1:10, :-1:10] / 200,
                        xs[10::10, 10::10] / 200, ys[10::10, 10::10] / 200)
    wet = both_water[::10, ::10]
    all_wet_corners = wet[:-1, :-1] & wet[1:, :-1] & wet[:-1, 1:] & wet[1:, 1:]
    conflicts = np.zeros(cells.shape, dtype=bool)
    for geom in geometries:
        conflicts |= shapely.intersects(geom, cells)
    return int((conflicts & all_wet_corners).sum())


def coast_distances(dll, coast, xs, ys):
    points = "".join(f"{x},{y}\n" for x, y in zip(xs.flat, ys.flat))
    proc = subprocess.run(["dotnet", str(dll), str(coast)], input=points,
                          text=True, capture_output=True, check=True)
    lines = proc.stdout.splitlines()
    if len(lines) != xs.size:
        raise RuntimeError("unexpected number of game coastline results")
    values = np.array([float(d) for d in lines], dtype=float).reshape(xs.shape)
    if not np.isfinite(values).all():
        raise RuntimeError("nonfinite signed coast distance")
    return values


def compare(dll, coast, archive):
    regions = {}
    with zipfile.ZipFile(archive) as zip_file:
        version = zip_file.read("ne_10m_land.VERSION.txt").decode("utf-8").strip()
        shp = shapefile.Reader(shp=BytesIO(zip_file.read("ne_10m_land.shp")))
        for name, bounds in REGIONS.items():
            print(f"COAST {len(regions) + 1}/{len(REGIONS)} {name}", flush=True)
            xs, ys = grid(name)
            # Coarse bbox filter before the prepared point predicates.
            west, south, east, north = (i / 200 for i in bounds)
            selected = []
            for record in shp.iterShapes():
                x0, y0, x1, y1 = record.bbox
                if x1 >= west and east >= x0 and y1 >= south and north >= y0:
                    selected.append(shapely.geometry.shape(record.__geo_interface__))
            ne_land = np.zeros(xs.shape, dtype=bool)
            for geom in selected:
                shapely.prepare(geom)
                ne_land |= shapely.intersects_xy(geom, xs / 200, ys / 200)
            if name == "open_atlantic" and ne_land.any():
                raise RuntimeError("open-sea negative control failed for Natural Earth")
            distance = coast_distances(dll, coast, xs, ys)
            game_water = distance > 0
            both_water = game_water & ~ne_land
            disagreement = game_water == ne_land
            if name != "open_atlantic" and (not game_water.any() or game_water.all() or not ne_land.any() or ne_land.all()):
                raise RuntimeError(f"coastal window lacks land or sea: {name}")
            if name == "open_atlantic" and not both_water.all():
                raise RuntimeError("open-sea control failed")
            regions[name] = dict(bbox_deg=[west, south, east, north],
                                 points=int(xs.size), coarse_cells=(xs.shape[0] - 1) * (xs.shape[1] - 1) // 100,
                                 game_water=int(game_water.sum()), ne_water=int((~ne_land).sum()),
                                 both_water=int(both_water.sum()),
                                 game_water_ne_land=int((game_water & ne_land).sum()),
                                 game_land_or_edge_ne_water=int((~game_water & ~ne_land).sum()),
                                 different_outside_game_band=int((disagreement & (np.abs(distance) > .3)).sum()),
                                 game_corner_misses=sampled_corner_misses(game_water),
                                 ne_corner_misses=sampled_corner_misses(~ne_land),
                                 combined_corner_misses=sampled_corner_misses(both_water),
                                 ne_polygon_conflicts_at_combined_wet_corners=polygon_conflicts(
                                     selected, xs, ys, both_water))
    return dict(ne_version=version, coast_sha256=digest(coast), ne_sha256=digest(archive),
                versions=dict(numpy=np.__version__, shapely=shapely.__version__, pyshp=shapefile.__version__),
                regions=regions)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--coast", required=True, type=Path)
    parser.add_argument("--land-zip", required=True, type=Path)
    parser.add_argument("--samples-dll", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    with args.output.open("x", encoding="utf-8") as stream:
        report = compare(args.samples_dll, args.coast, args.land_zip)
        json.dump(report, stream, indent=2)
        stream.write("\n")
    print(args.output)


if __name__ == "__main__":
    main()
