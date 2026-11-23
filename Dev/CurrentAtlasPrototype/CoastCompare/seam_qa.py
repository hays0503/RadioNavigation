"""Offline tests of shared tile traces; optional reuse of the HYCOM NetCDF witness."""
import argparse
import hashlib
import json
import math
from pathlib import Path

import numpy as np
from scipy.io import netcdf_file

from velocity_audit import DENSE_LEVELS, LEVELS, read_component


def trace(depth, field, valid, levels, query):
    """Evaluate a piecewise linear trace without crossing missing native support."""
    out = np.full((len(query), *field.shape[1:]), np.nan)
    for i, z in enumerate(query):
        j = int(np.searchsorted(levels, z))
        if j < len(levels) and levels[j] == z:
            source = int(np.searchsorted(depth, z))
            out[i] = np.where(valid[source][..., None], field[source], np.nan)
            continue
        if j == 0 or j == len(levels):
            continue
        lo, hi = levels[j - 1], levels[j]
        a, b = np.searchsorted(depth, (lo, hi))
        p = (z - lo) / (hi - lo)
        wet = valid[a:b + 1].all(axis=0)
        out[i] = np.where(wet[..., None], field[a] * (1 - p) + field[b] * p, np.nan)
    return out


def pack_shared(field, scale):
    """Canonical face codes, copied identically rather than quantized by each tile."""
    value = np.rint(field / scale)
    if not (np.isfinite(value).all() and np.all(np.abs(value) <= 32767)):
        raise ValueError("shared face coefficient cannot be encoded")
    return value.astype("<i2")


def vector_error(a, b):
    e = np.linalg.norm(a - b, axis=-1)
    have = e[np.isfinite(e)]
    return dict(count=int(have.size), max_mps=float(have.max()) if have.size else None,
                p95_mps=float(np.quantile(have, .95)) if have.size else None)


def sample_cell(depth, nodes, ids, x, y, z):
    corners = []
    for i in ids:
        field, valid, levels = nodes[i]
        corners.append(trace(depth, field, valid, levels, [z])[0])
    if not np.isfinite(corners).all():
        return None
    a, b, c, d = corners
    return (a * (1 - x) + b * x) * (1 - y) + (c * (1 - x) + d * x) * y


def inspect(path):
    with netcdf_file(path, "r", mmap=False) as nc:
        u, um = read_component(nc.variables["water_u"], "eastward_sea_water_velocity")
        v, vm = read_component(nc.variables["water_v"], "northward_sea_water_velocity")
        depth = nc.variables["depth"][:].copy()
    field = np.stack((u, v), axis=-1).swapaxes(0, 1)
    wet = (um & vm).swapaxes(0, 1)
    # Treat each horizontal node/day as a possible shared face anchor. This is
    # not a mosaic of independently chosen geographic regions or sources.
    z = np.unique(np.concatenate((depth, (depth[:-1] + depth[1:]) / 2)))
    sparse = trace(depth, field, wet, LEVELS, z)
    dense = trace(depth, field, wet, DENSE_LEVELS, z)
    native = trace(depth, field, wet, tuple(depth), z)
    paired = np.isfinite(sparse).all(axis=-1) & np.isfinite(dense).all(axis=-1)
    canonical = np.where(paired[..., None], dense, np.nan)  # Candidates share the denser representation.
    # Worst shared/nonshared differences are measured on identical supported samples.
    unreliable = np.isfinite(sparse).all(axis=-1) ^ np.isfinite(dense).all(axis=-1)
    return dict(input_sha256=hashlib.sha256(path.read_bytes()).hexdigest(), query_depth_count=len(z),
                unstitched_jump=vector_error(sparse, dense),
                sparse_vs_native_same_queries=vector_error(np.where(paired[..., None], sparse, np.nan), native),
                canonical_vs_native_same_queries=vector_error(canonical, native),
                support_disagreements=int(unreliable.sum()))


def checks():
    passed = []

    def check(name, ok):
        if not ok:
            raise AssertionError(name)
        passed.append(name)

    depth = np.array([0., 5., 10.])
    field = np.array([[0., 0.], [1., -1.], [0., 0.]])
    wet = np.ones(3, dtype=bool)
    query = np.array([0., 2.5, 5., 7.5, 10.])
    coarse = trace(depth, field, wet, (0, 10), query)
    fine = trace(depth, field, wet, (0, 5, 10), query)
    check("common_endpoints_hide_interior_seam",
          np.array_equal(coarse[[0, -1]], fine[[0, -1]])
          and math.isclose(vector_error(coarse, fine)["max_mps"], math.sqrt(2)))

    codes = pack_shared(field + .0013, .002)
    left, right = codes.copy(), codes.copy()
    check("shared_bytes_decode_to_same_trace_on_both_sides",
          left.tobytes() == right.tobytes() and np.array_equal(
              trace(depth, left * .002, wet, (0, 5, 10), query),
              trace(depth, right * .002, wet, (0, 5, 10), query)))
    check("quantized_shared_trace_preserves_vector_error_bound",
          vector_error(codes * .002, field + .0013)["max_mps"] <= math.sqrt(2) * .001)
    check("same_coefficients_quantized_independently_can_jump",
          not np.array_equal(pack_shared(field + .0013, .002) * .002,
                             pack_shared(field + .0013, .003) * .003))
    try:
        pack_shared(np.array([65.536]), .002)
    except ValueError:
        check("shared_code_rejects_overflow", True)
    else:
        check("shared_code_rejects_overflow", False)

    dry = trace(depth, field, np.array([True, False, True]), (0, 10), query)
    check("stitch_does_not_fill_dry_depth_interval", np.isnan(dry[1:-1]).all()
          and np.isfinite(dry[[0, -1]]).all())

    # Cells own no copies of the vertical profile rules; they reference nodes.
    nodes = [(field + offset, wet, levels) for offset, levels in
             ((0, (0, 10)), (2, (0, 5, 10)), (4, (0, 5, 10)),
              (1, (0, 10)), (3, (0, 5, 10)), (5, (0, 5, 10)))]
    left_ids, right_ids = (0, 1, 3, 4), (1, 2, 4, 5)
    for z in query:
        for y in np.linspace(0, 1, 9):
            l = sample_cell(depth, nodes, left_ids, 1., y, z)
            r = sample_cell(depth, nodes, right_ids, 0., y, z)
            assert np.array_equal(l, r), "mixed_cells_share_same_face_at_all_query_depths"
    passed.append("mixed_cells_share_same_face_at_all_query_depths")
    check("edge_continuity_does_not_claim_matching_gradients",
          not np.array_equal(sample_cell(depth, nodes, left_ids, 1., .5, 5)
                             - sample_cell(depth, nodes, left_ids, .99, .5, 5),
                             sample_cell(depth, nodes, right_ids, .01, .5, 5)
                             - sample_cell(depth, nodes, right_ids, 0., .5, 5)))
    nodes[2] = (field, np.array([True, False, True]), (0, 5, 10))
    check("whole_cell_support_guard_is_not_overridden_by_edge_value",
          sample_cell(depth, nodes, left_ids, 1., .5, 5) is not None
          and sample_cell(depth, nodes, right_ids, 0., .5, 5) is None)

    # Uniform divergence-free velocities separately: 0.4 m/s and 0.8 m/s east.
    # Across a straight w=x/L blend in a box of constant cross-section A, the
    # through-face flux difference is nonzero even though the blended field is C0.
    area, length, a, b = 1000, 2000, .4, .8
    imbalance = area * b - area * a
    check("smooth_blend_does_not_preserve_flux", math.isclose(imbalance, 400)
          and math.isclose((b - a) / length * length * area, imbalance))
    return passed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input-dir", type=Path, help="local subsets from velocity_audit.py")
    args = parser.parse_args()
    passed = checks()
    result = dict(scope=__doc__, passed=len(passed), checks=passed)
    if args.input_dir:
        result["hycom"] = {name: inspect(args.input_dir / (name + ".nc"))
                           for name in ("gibraltar", "jade_approach", "open_atlantic")}
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
