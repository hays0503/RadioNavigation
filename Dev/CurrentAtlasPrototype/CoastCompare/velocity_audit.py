"""Small official HYCOM NetCDF subsets: vertical support/decimation audit, not game calibration."""
import argparse
import hashlib
import json
from pathlib import Path
from types import SimpleNamespace
from urllib.parse import quote
from urllib.request import urlopen

import numpy as np
from scipy.io import netcdf_file


SOURCE = "https://coastwatch.pfeg.noaa.gov/erddap/griddap/nrlHycomGLBu008e910D.nc"
REGIONS = {
    "gibraltar": (353.6, 35.7, 355.0, 36.4),
    "jade_approach": (7.7, 53.3, 8.6, 54.1),
    "open_atlantic": (338.0, 46.0, 338.5, 46.5),
}
TIME = "[(2014-04-06T00:00:00Z):(2014-04-08T00:00:00Z)]"
LEVELS = (0, 10, 20, 50, 100, 200)
DENSE_LEVELS = (0, 4, 10, 15, 20, 30, 40, 50, 70, 100, 125, 150, 200)


def subset_url(region):
    w, s, e, n = region
    sl = f"{TIME}[(0):(200)][({s}):({n})][({w}):({e})]"
    return SOURCE + "?" + quote(f"water_u{sl},water_v{sl}", safe="(),:")


def text(value):
    return value.decode("ascii") if isinstance(value, bytes) else str(value)


def read_component(var, standard_name):
    if (var.dimensions != ("time", "depth", "latitude", "longitude")
            or text(var.standard_name) != standard_name or text(var.units) != "m/s"):
        raise ValueError("wrong velocity dimensions, standard name, or units")
    raw = var[:]
    valid = np.isfinite(raw)
    for name in ("_FillValue", "missing_value"):
        if hasattr(var, name):
            for value in np.atleast_1d(getattr(var, name)):
                valid &= raw != value
    if hasattr(var, "valid_range"):
        lo, hi = var.valid_range
        valid &= (raw >= lo) & (raw <= hi)
    if hasattr(var, "valid_min"):
        valid &= raw >= var.valid_min
    if hasattr(var, "valid_max"):
        valid &= raw <= var.valid_max
    result = np.full(raw.shape, np.nan)
    result[valid] = (raw[valid].astype(float) * float(getattr(var, "scale_factor", 1))
                     + float(getattr(var, "add_offset", 0)))
    if not np.isfinite(result[valid]).all():
        raise ValueError("nonfinite decoded velocity")
    return result, valid


def statistics(errors):
    if not errors.size:
        return dict(count=0)
    return dict(count=int(errors.size), p95_mps=float(np.quantile(errors, .95)),
                max_mps=float(np.max(errors)))


def vertical(depths, velocity, valid, levels, errors_by_node=None):
    keep = np.where(np.isin(depths, levels))[0]
    if tuple(depths[keep]) != levels:
        raise ValueError("missing retained native levels")
    errors, surface, brackets = [], [], []
    false_support = reverse = energetic = 0
    for a, b in zip(keep, keep[1:]):
        bracket_errors = []
        for z in range(a + 1, b):
            endpoints = valid[:, a] & valid[:, b]
            # Require all native layers in the coarse bracket, not just its ends.
            supported = valid[:, a:b + 1].all(axis=1)
            false_support += np.count_nonzero(endpoints & ~supported)
            p = (depths[z] - depths[a]) / (depths[b] - depths[a])
            pred = velocity[:, a] * (1 - p) + velocity[:, b] * p
            actual = velocity[:, z]
            defects = np.linalg.norm((pred - actual)[supported], axis=-1)
            errors.extend(defects)
            bracket_errors.extend(defects)
            if errors_by_node is not None:
                errors_by_node[:, z][supported] = defects
        brackets.append(dict(endpoints_m=[float(depths[a]), float(depths[b])],
                             **statistics(np.asarray(bracket_errors))))
    for z in range(1, len(depths)):
        both = valid[:, 0] & valid[:, z]
        top, actual = velocity[:, 0][both], velocity[:, z][both]
        surface.extend(np.linalg.norm(top - actual, axis=-1))
        fast = (np.linalg.norm(top, axis=-1) >= .05) & (np.linalg.norm(actual, axis=-1) >= .05)
        energetic += np.count_nonzero(fast)
        reverse += np.count_nonzero(fast & ((top * actual).sum(axis=-1) < 0))
    return dict(interpolated=statistics(np.asarray(errors)), surface_held=statistics(np.asarray(surface)),
                endpoint_only_false_support_queries=int(false_support),
                above_90_deg_from_surface=int(reverse), paired_speed_ge_0_05=int(energetic),
                brackets=brackets)


def inspect(path):
    with netcdf_file(path, "r", mmap=False) as nc:
        v = nc.variables
        t, z, y, x = (v[k][:] for k in ("time", "depth", "latitude", "longitude"))
        for name, axis, units in (("depth", z, "m"), ("latitude", y, "degrees_north"),
                                  ("longitude", x, "degrees_east")):
            if text(v[name].units) != units or not np.all(np.diff(axis) > 0):
                raise ValueError(f"unexpected {name} metadata")
        if (text(v["depth"].positive) != "down"
                or text(v["time"].units) != "seconds since 1970-01-01T00:00:00Z"
                or text(v["time"].calendar) != "gregorian"
                or not np.array_equal(t, [1396742400, 1396828800, 1396915200])):
            raise ValueError("unexpected time cadence or depth sign")
        u, um = read_component(v["water_u"], "eastward_sea_water_velocity")
        vv, vm = read_component(v["water_v"], "northward_sea_water_velocity")
        valid = um & vm
        field = np.stack((u, vv), axis=-1)
        a, b = (np.full(valid.shape, np.nan) for _ in range(2))
        sparse = vertical(z, field, valid, LEVELS, a)
        dense = vertical(z, field, valid, DENSE_LEVELS, b)
        paired = np.isfinite(a) & np.isfinite(b)
        omitted_by_both = ~np.isin(z, (*LEVELS, *DENSE_LEVELS))[None, :, None, None]
        return dict(sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                    dimensions=dict(zip(("time", "depth", "latitude", "longitude"), u.shape)),
                    depth_m=z.tolist(), time=t.tolist(), time_units=text(v["time"].units),
                    time_calendar=text(v["time"].calendar),
                    bbox_deg=[float(x[0]), float(y[0]), float(x[-1]), float(y[-1])],
                    packed_dtype=str(v["water_u"][:].dtype),
                    fill_u=float(v["water_u"]._FillValue), fill_v=float(v["water_v"]._FillValue),
                    has_scale_factor=[hasattr(v[k], "scale_factor") for k in ("water_u", "water_v")],
                    valid_at_surface=int(valid[:, 0].sum()), valid_at_200=int(valid[:, -1].sum()),
                    component_mask_disagreements=int(np.count_nonzero(um != vm)),
                    mask_changes_over_three_dates=int(np.count_nonzero((valid != valid[:1]).any(axis=0))),
                    peak_vector_mps=float(np.max(np.linalg.norm(field[valid], axis=-1))),
                    sparse=sparse, dense=dense,
                    same_queries=dict(sparse=statistics(a[paired]), dense=statistics(b[paired]),
                                      dense_only=int((omitted_by_both & np.isfinite(b) & ~np.isfinite(a)).sum())))


def self_check():
    class Fixture(SimpleNamespace):
        def __getitem__(self, key):
            return self.data[key]

    raw = Fixture(dimensions=("time", "depth", "latitude", "longitude"),
                  standard_name=b"eastward_sea_water_velocity", units=b"m/s",
                  _FillValue=-30000, missing_value=-29999, scale_factor=.001,
                  add_offset=.125, data=np.array([-30000, -29999, 0, 1000], dtype=np.int16))
    decoded, good = read_component(raw, "eastward_sea_water_velocity")
    assert good.tolist() == [False, False, True, True], "packed_mask_before_scaling"
    assert np.isnan(decoded[:2]).all() and decoded[2:].tolist() == [.125, 1.125], "decode_once"
    z = np.array([0., 2., 10.])
    field = np.zeros((1, 3, 1, 1, 2))
    wet = np.array([True, False, True]).reshape(1, 3, 1, 1)
    result = vertical(z, field, wet, (0, 10))
    assert result["endpoint_only_false_support_queries"] == 1 and (
        result["interpolated"]["count"] == 0), "interior_gap_not_interpolated"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--input-dir", type=Path, help="reuse saved subsets without network")
    args = parser.parse_args()
    self_check()
    args.output_dir.mkdir()
    results = {}
    for i, (name, region) in enumerate(REGIONS.items(), 1):
        print(f"VELOCITY {i}/{len(REGIONS)} {name}", flush=True)
        url = subset_url(region)
        path = (args.input_dir or args.output_dir) / (name + ".nc")
        if args.input_dir is None:
            with urlopen(url, timeout=20) as response:
                content = response.read(512 * 1024 + 1)
            if not content.startswith(b"CDF\x01") or len(content) > 512 * 1024:
                raise ValueError("expected bounded NetCDF classic subset")
            path.write_bytes(content)
        results[name] = dict(url=url, **inspect(path))
    summary = args.output_dir / "velocity.json"
    summary.write_text(json.dumps(dict(sparse_levels_m=LEVELS, dense_levels_m=DENSE_LEVELS,
                                       regions=results), indent=2) + "\n", encoding="utf-8")
    print(summary)


if __name__ == "__main__":
    main()
