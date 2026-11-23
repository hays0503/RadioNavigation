"""Build a 2x2-tile ORA-20C regional RNW1 set for a local test; not for shipping."""
import argparse
import calendar
from datetime import datetime
import hashlib
from itertools import product
import json
import math
from pathlib import Path
import re
import struct
from urllib.parse import quote
from urllib.request import urlopen


BASE = "https://icdc.cen.uni-hamburg.de/thredds/dodsC/ftpthredds/EASYInit/ora20c/opa0/"
TIME = (9, 10)          # October and November 1941 centres
DEPTHS = (0, 4)         # 5.02 .. 45.45 m
QUANTIZE = 10000


def fetch(component, lat, lon, output):
    selector = f"[{TIME[0]}:{TIME[1]}][{DEPTHS[0]}:{DEPTHS[1]}][{lat}:{lat + 1}][{lon}:{lon + 1}]"
    tail = (f",mask[{DEPTHS[0]}:{DEPTHS[1]}][{lat}:{lat + 1}][{lon}:{lon + 1}]"
            f",depth[{DEPTHS[0]}:{DEPTHS[1]}],lat[{lat}:{lat + 1}],lon[{lon}:{lon + 1}]"
            f",time[{TIME[0]}:{TIME[1]}]")
    url = BASE + f"{component}_ora20c_1m_1941_grid_1x1.nc.gz.ascii?" + quote(
        component + selector + tail, safe=",")
    with urlopen(url, timeout=40) as response:
        payload = response.read(200001)
    if len(payload) > 200000:
        raise ValueError("reply exceeds the local trial limit")
    name = f"{component}_{lat}_{lon}.txt"
    (output / name).write_bytes(payload)
    return payload.decode("ascii"), dict(url=url, bytes=len(payload),
                                        sha256=hashlib.sha256(payload).hexdigest(), file=name)


def values(ascii_text, name, shape):
    body = ascii_text.split("---------------------------------------------", 1)[1]
    header = name + "".join(f"[{size}]" for size in shape)
    block = re.search(rf"(?m)^{re.escape(header)}\r?\n((?:[^\r\n]+\r?\n?)+)", body)
    if block is None:
        raise ValueError(f"missing DAP array {header}")
    rows = block.group(1).splitlines()
    prefixes = list(product(*(range(size) for size in shape[:-1])))
    if len(rows) != len(prefixes):
        raise ValueError(f"unexpected row count in {header}")
    result = []
    for prefix, row in zip(prefixes, rows):
        parts = row.strip().split(",")
        if prefix and parts.pop(0) != "".join(f"[{index}]" for index in prefix):
            raise ValueError(f"wrong row order in {header}")
        if len(parts) != shape[-1]:
            raise ValueError(f"wrong inner axis in {header}")
        result.extend(float(part) for part in parts)
    return result


def pack(axes, east, north, masks):
    data = bytearray(struct.pack("<4s4H", b"RNW1", *(len(axis) for axis in axes)))
    for axis in axes:
        data += struct.pack("<" + "d" * len(axis), *axis)
    errors = 0.0
    supported = 0
    for index, (u, v) in enumerate(zip(east, north)):
        node = index % len(masks)
        if not masks[node] or not all(math.isfinite(x) and abs(x) < 1e30 for x in (u, v)):
            data += struct.pack("<hh", -32768, -32768)
            continue
        packed = tuple(round(x * QUANTIZE) for x in (u, v))
        if any(abs(x) > 32767 for x in packed):
            raise ValueError("supported vector out of RNW1 range")
        data += struct.pack("<hh", *packed)
        supported += 1
        errors = max(errors, math.dist((u, v), tuple(x / QUANTIZE for x in packed)))
    return data, errors, supported


def tile(output, lat, lon, base_seconds):
    shape = (TIME[1] - TIME[0] + 1, DEPTHS[1] - DEPTHS[0] + 1, 2, 2)
    east_text, east_meta = fetch("uo", lat, lon, output)
    north_text, north_meta = fetch("vo", lat, lon, output)
    east_axes = [values(east_text, name, (size,)) for name, size in
                 zip(("time", "depth", "lat", "lon"), shape)]
    north_axes = [values(north_text, name, (size,)) for name, size in
                  zip(("time", "depth", "lat", "lon"), shape)]
    if east_axes != north_axes:
        raise ValueError("vector components disagree on axes")
    axes = [[base_seconds + value for value in east_axes[0]], east_axes[1], east_axes[2], east_axes[3]]
    east = values(east_text, "uo", shape)
    north = values(north_text, "vo", shape)
    mask = [1.0 if a == 1 and b == 1 else 0.0 for a, b in
            zip(values(east_text, "mask", shape[1:]), values(north_text, "mask", shape[1:]))]
    data, error, supported = pack(axes, east, north, mask)
    path = output / f"ora_{lat}_{lon}.rnw"
    path.write_bytes(data)
    return dict(file=path.name, bytes=len(data), sha256=hashlib.sha256(data).hexdigest(),
                axes=axes, max_quantization_mps=error, supported=supported,
                total=len(east), sources=[east_meta, north_meta])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--lat", type=float, required=True)
    parser.add_argument("--lon", type=float, required=True)
    args = parser.parse_args()
    lat = math.floor(args.lat + 88.5)
    lon = math.floor(args.lon - 0.5)
    base = calendar.timegm(datetime(1941, 1, 1).timetuple())
    args.output.mkdir(parents=True, exist_ok=False)
    tiles = [tile(args.output, la, lo, base) for la in (lat, lat + 1) for lo in (lon, lon + 1)]
    manifest = dict(kind="local ORA-20C research slice", time_index=TIME, depth_index=DEPTHS,
                    grid=[lat, lat + 2, lon, lon + 2], quantization=QUANTIZE, tiles=tiles,
                    note="monthly model means, no time interpolation beyond the two centres,"
                         " no surface extrapolation; not licensed for redistribution")
    (args.output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: manifest[k] for k in ("grid", "time_index", "depth_index")}))
    print(f"{len(tiles)} tiles; max quantization {max(t['max_quantization_mps'] for t in tiles):.3e} m/s")


if __name__ == "__main__":
    main()
