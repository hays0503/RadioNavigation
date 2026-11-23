"""Build/read one offline open-Atlantic HYCOM witness tile. Not a runtime atlas."""
import argparse
import hashlib
import json
from math import isfinite
from pathlib import Path
import struct
from bisect import bisect_right

import numpy as np
from scipy.io import netcdf_file

from velocity_audit import REGIONS, read_component, subset_url, text


SCALE = 0.0001
MISSING = -32768
HEADER = struct.Struct("<4s4H")
SHA = "1c321e7f894fc7d981c282d37b9046c9c47df7fca41a34a4a337955540ac35c9"


def pack(axes, values, supported):
    sizes = tuple(len(axis) for axis in axes)
    if not all(2 <= length <= 64 for length in sizes) or values.shape != (*sizes, 2):
        raise ValueError("unexpected tile dimensions")
    for axis in axes:
        if not np.isfinite(axis).all() or not (np.diff(axis) > 0).all():
            raise ValueError("invalid axis")
    rounded = np.rint(values[supported] / SCALE)
    if not np.isfinite(rounded).all() or (np.abs(rounded) > 32767).any():
        raise ValueError("velocity does not fit the tile scale")
    vectors = np.full(values.shape, MISSING, dtype="<i2")
    vectors[supported] = rounded.astype("<i2")
    return (HEADER.pack(b"RNW1", *sizes)
            + b"".join(np.asarray(axis, dtype="<f8").tobytes() for axis in axes)
            + vectors.tobytes())


class Tile:
    def __init__(self, data):
        if len(data) < HEADER.size:
            raise ValueError("truncated tile")
        magic, *sizes = HEADER.unpack_from(data)
        if magic != b"RNW1" or not all(2 <= n <= 64 for n in sizes):
            raise ValueError("unsupported tile")
        vectors = np.prod(sizes, dtype=np.int64)
        if len(data) != HEADER.size + 8 * sum(sizes) + 4 * vectors:
            raise ValueError("bad tile length")
        offset = HEADER.size
        self.axes = []
        for count in sizes:
            axis = np.frombuffer(data, "<f8", count, offset)
            if not np.isfinite(axis).all() or not (np.diff(axis) > 0).all():
                raise ValueError("invalid tile axis")
            self.axes.append(axis)
            offset += count * 8
        self.values = np.frombuffer(data, "<i2", offset=offset).reshape(*sizes, 2)
        if ((self.values[..., 0] == MISSING) != (self.values[..., 1] == MISSING)).any():
            raise ValueError("mismatched missing vector components")

    def sample(self, utc_seconds, depth_m, lat, east_lon):
        intervals = []
        for axis, value in zip(self.axes, (utc_seconds, depth_m, lat, east_lon)):
            if not isfinite(value) or value < axis[0] or value > axis[-1]:
                return None
            lo = min(bisect_right(axis, value) - 1, len(axis) - 2)
            intervals.append((lo, (value - axis[lo]) / (axis[lo + 1] - axis[lo])))
        packed = self.values[tuple(slice(lo, lo + 2) for lo, _ in intervals)]
        # Conservative support: every corner of the entire time/depth/space
        # cell, even corners with zero interpolation weight.
        if (packed == MISSING).any():
            return None
        result = packed.astype(float) * SCALE
        for _, f in intervals:
            result = result[0] * (1 - f) + result[1] * f
        return tuple(result)


def self_check():
    axes = tuple(np.array([0., 1.]) for _ in range(4))
    values = np.zeros((2, 2, 2, 2, 2))
    values[1, :, :, :, 0] = 1
    values[:, 1, :, :, 1] = -2
    valid = np.ones(values.shape[:-1], dtype=bool)
    data = pack(axes, values, valid)
    tile = Tile(data)
    assert np.allclose(tile.sample(.25, .75, .1, .2), (.25, -1.5), rtol=0, atol=1e-12)
    assert tile.sample(0, 0, 0, 0) == (0, 0)
    assert tile.sample(2, 0, 0, 0) is None
    valid[1, 1, 1, 1] = False
    assert Tile(pack(axes, values, valid)).sample(0, 0, 0, 0) is None
    try:
        Tile(data[:-1])
    except ValueError:
        pass
    else:
        raise AssertionError("truncation accepted")


def build(source, output):
    if hashlib.sha256(source.read_bytes()).hexdigest() != SHA:
        raise ValueError("wrong open-Atlantic source file")
    with netcdf_file(source, "r", mmap=False) as nc:
        v = nc.variables
        names = ("time", "depth", "latitude", "longitude")
        axes = [v[key][:] for key in names]
        expected = ("seconds since 1970-01-01T00:00:00Z", "m", "degrees_north", "degrees_east")
        if (tuple(text(v[key].units) for key in names) != expected
                or text(v["depth"].positive) != "down" or text(v["time"].calendar) != "gregorian"):
            raise ValueError("unexpected coordinates")
        u, wet_u = read_component(v["water_u"], "eastward_sea_water_velocity")
        w, wet_v = read_component(v["water_v"], "northward_sea_water_velocity")
        values = np.stack((u, w), axis=-1)
        wet = wet_u & wet_v
    data = pack(axes, values, wet)
    tile = Tile(data)
    if not wet.all():
        raise ValueError("this offline witness requires an entirely wet open-ocean tile")
    defects = np.linalg.norm(tile.values * SCALE - values, axis=-1)
    limit = np.sqrt(2) * SCALE * .5 + 1e-10
    if defects.max() > limit:
        raise ValueError("quantization budget exceeded")
    middle = [float((axis[0] + axis[-1]) / 2) for axis in axes]
    midpoint = tile.sample(*middle)
    assert midpoint is not None and tile.sample(0, *middle[1:]) is None
    output.mkdir()
    (output / "atlantic.rnw").write_bytes(data)
    receipt = dict(source_sha256=SHA, tile_sha256=hashlib.sha256(data).hexdigest(),
                   source_url=subset_url(REGIONS["open_atlantic"]), bytes=len(data),
                   shape=list(wet.shape), native_depths_m=axes[1].tolist(),
                   bbox_east_positive=[float(axis[i]) for axis in axes[2:] for i in (0, -1)],
                   max_quantization_mps=float(defects.max()), midpoint_mps=midpoint,
                   scope="offline modern HYCOM witness; not cleared for shipping or historical reconstruction")
    (output / "atlantic.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    self_check()
    build(args.source, args.output)
