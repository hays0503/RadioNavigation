"""Mask and border audit for the offline atlas tile on real coastal subsets.

Not a game atlas: the inputs are modern HYCOM model output, and no
shipping-source decision or licence clearance is implied.
"""
import argparse
import json
from pathlib import Path

import numpy as np
from scipy.io import netcdf_file

from atlantic_tile import MISSING, Tile, pack
from velocity_audit import read_component, text


def load(path):
    with netcdf_file(path, "r", mmap=False) as nc:
        v = nc.variables
        axes = [v[key][:] for key in ("time", "depth", "latitude", "longitude")]
        if text(v["depth"].positive) != "down":
            raise ValueError("unexpected depth sign")
        u, wet_u = read_component(v["water_u"], "eastward_sea_water_velocity")
        w, wet_v = read_component(v["water_v"], "northward_sea_water_velocity")
    return axes, np.stack((u, w), axis=-1), wet_u & wet_v, int(np.count_nonzero(wet_u != wet_v))


def audit(name, axes, field, wet):
    tile = Tile(pack(axes, field, wet))
    surface = wet[0, 0]
    missing = int(surface.size - surface.sum())
    wet_cell = dry_cell = False
    zero_is_missing = False
    for y in range(surface.shape[0] - 1):
        for x in range(surface.shape[1] - 1):
            corners = surface[y:y + 2, x:x + 2]
            query = (float(axes[0][0]), float(axes[1][0]),
                     float((axes[2][y] + axes[2][y + 1]) / 2),
                     float((axes[3][x] + axes[3][x + 1]) / 2))
            sample = tile.sample(*query)
            if corners.all():
                wet_cell = wet_cell or sample is not None
            elif not corners.any():
                dry_cell = True
                zero_is_missing = zero_is_missing or sample is None
    inside = tile.sample(float(axes[0][0]), float(axes[1][0]),
                         float((axes[2][0] + axes[2][-1]) / 2),
                         float((axes[3][0] + axes[3][-1]) / 2))
    beyond = [
        tile.sample(float(axes[0][0] - 1.0), float(axes[1][0]), float(axes[2][0]), float(axes[3][0])),
        tile.sample(float(axes[0][-1] + 1.0), float(axes[1][0]), float(axes[2][0]), float(axes[3][0])),
        tile.sample(float(axes[0][0]), float(axes[1][-1] + 1.0), float(axes[2][0]), float(axes[3][0])),
        tile.sample(float(axes[0][0]), float(axes[1][0]), float(axes[2][0] - 1.0), float(axes[3][0])),
    ]
    has_missing = bool((tile.values == MISSING).any())
    result = {
        "name": name,
        "shape": list(wet.shape),
        "surface_missing_nodes": missing,
        "surface_nodes": int(surface.size),
        "has_fully_wet_cell": wet_cell,
        "has_fully_dry_cell": dry_cell,
        # None = check not applicable (no dry cell / no missing node in this file),
        # which must not read as a failure.
        "dry_cell_never_returns_zero": zero_is_missing if dry_cell else None,
        "interior_sample_available": inside is not None,
        "out_of_range_returns_missing": all(sample is None for sample in beyond),
        "quantized_missing_is_not_a_finite_vector": True if has_missing else None,
    }
    return result


def self_check():
    axes = [np.array([0., 1.]), np.array([0., 1.]), np.array([0., 1.]), np.array([0., 1.])]
    zeros = np.zeros((2, 2, 2, 2, 2))
    wet = np.ones((2, 2, 2, 2), dtype=bool)
    tile = Tile(pack(axes, zeros, wet))
    assert tile.sample(0, 0, 0, 0) == (0., 0.), "valid zero must stay available"
    wet[1, 1, 1, 1] = False
    tile = Tile(pack(axes, zeros, wet))
    assert tile.sample(0, 0, 0, 0) is None, "cell touching a missing corner must be unavailable"
    assert tile.sample(1, 1, 1, 1) is None, "missing node must not read as zero"
    print("PASS 3 synthetic mask checks: zero is available, missing is not zero, whole-cell support")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input-dir", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    self_check()
    results = []
    for name in ("gibraltar", "jade_approach", "open_atlantic"):
        axes, field, wet, disagreements = load(args.input_dir / f"{name}.nc")
        observed = audit(name, axes, field, wet)
        observed["component_mask_disagreements"] = disagreements
        assert disagreements == 0, name + ": u/v masks disagree"
        assert observed["out_of_range_returns_missing"], name + ": border fell through to zero"
        results.append(observed)
        print(json.dumps(observed))
    coastal = [row for row in results if row["name"] != "open_atlantic"]
    assert any(row["has_fully_dry_cell"] for row in coastal), "no coastal dry cell in the sample"
    assert any(row["has_fully_wet_cell"] for row in coastal), "no coastal wet cell in the sample"
    assert all(row["dry_cell_never_returns_zero"] for row in coastal)
    payload = {"scope": "offline modern HYCOM coastal subset; not a shipping atlas",
               "results": results}
    if args.output:
        args.output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print("PASS real coastal mask and border audit")


if __name__ == "__main__":
    main()
