"""Acquire a tiny ORA-20C local test slice and pack it into RNW1; not for redistribution."""
import argparse
from datetime import datetime, timezone
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
SLICE = "[9:10][0:4][137:138][343:344]"
SELECT = ",mask[0:4][137:138][343:344],depth[0:4],lat[137:138],lon[343:344],time[9:10]"
ORDER = ("time", "depth", "lat", "lon")
SHAPE = (2, 5, 2, 2)
QUANTIZE = 10000


def values(ascii_text, name, shape):
    body = ascii_text.split("---------------------------------------------", 1)[1]
    header = name + "".join(f"[{size}]" for size in shape)
    block = re.search(rf"(?m)^{re.escape(header)}\r?\n((?:[^\r\n]+\r?\n?)+)", body)
    if block is None:
        raise ValueError(f"missing DAP array {header}")
    rows = block.group(1).splitlines()
    indexes = list(product(*(range(size) for size in shape[:-1])))
    if len(rows) != len(indexes):
        raise ValueError(f"unexpected row count in {header}")
    result = []
    for prefix, row in zip(indexes, rows):
        parts = row.strip().split(",")
        if prefix and parts.pop(0) != "".join(f"[{index}]" for index in prefix):
            raise ValueError(f"wrong row order in {header}")
        if len(parts) != shape[-1]:
            raise ValueError(f"wrong inner axis in {header}")
        result.extend(float(part) for part in parts)
    return result


def fetch(component, output):
    url = BASE + f"{component}_ora20c_1m_1941_grid_1x1.nc.gz.ascii?"
    url += quote(component + SLICE + SELECT, safe=",")
    with urlopen(url, timeout=30) as response:
        payload = response.read(65537)
    if len(payload) > 65536:
        raise ValueError("reply exceeds the local trial limit")
    text = payload.decode("ascii")
    (output / (component + ".txt")).write_bytes(payload)
    return text, dict(url=url, bytes=len(payload), sha256=hashlib.sha256(payload).hexdigest())


def pack(east_text, north_text):
    axes = [values(east_text, name, (size,)) for name, size in zip(ORDER, SHAPE)]
    if axes != [values(north_text, name, (size,)) for name, size in zip(ORDER, SHAPE)]:
        raise ValueError("vector component axes disagree")
    if any(not all(map(math.isfinite, axis)) or any(a >= b for a, b in zip(axis, axis[1:])) for axis in axes):
        raise ValueError("bad source axis")
    if axes[0] != [24926400, 27561600] or axes[1][0] < 5 or axes[2:] != [[47.5, 48.5], [343.5, 344.5]]:
        raise ValueError("unexpected coordinates, depth convention or 1941 time axis")
    base = datetime(1941, 1, 1, tzinfo=timezone.utc).timestamp()
    axes[0] = [base + value for value in axes[0]]
    east = values(east_text, "uo", SHAPE)
    north = values(north_text, "vo", SHAPE)
    mask_e = values(east_text, "mask", SHAPE[1:])
    mask_n = values(north_text, "mask", SHAPE[1:])
    data = bytearray(struct.pack("<4s4H", b"RNW1", *SHAPE))
    for axis in axes:
        data += struct.pack("<" + "d" * len(axis), *axis)
    errors, speeds = [], []
    for index, (u, v) in enumerate(zip(east, north)):
        node = index % len(mask_e)
        if mask_e[node] != 1 or mask_n[node] != 1 or not all(math.isfinite(x) and abs(x) < 1e30 for x in (u, v)):
            data += struct.pack("<hh", -32768, -32768)
            continue
        packed = tuple(round(x * QUANTIZE) for x in (u, v))
        if any(abs(x) > 32767 for x in packed):
            raise ValueError("supported vector out of RNW1 range")
        data += struct.pack("<hh", *packed)
        errors.append(math.dist((u, v), tuple(x / QUANTIZE for x in packed)))
        speeds.append(math.hypot(u, v))
    return data, dict(axes=axes, supported=len(errors), missing=len(east) - len(errors),
                      max_vector_quantization_mps=max(errors), max_speed_mps=max(speeds))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    east, east_meta = fetch("uo", args.output)
    north, north_meta = fetch("vo", args.output)
    tile, stats = pack(east, north)
    (args.output / "ora20c-local.rnw").write_bytes(tile)
    result = dict(source={"uo": east_meta, "vo": north_meta}, packed=stats,
                  sha256=hashlib.sha256(tile).hexdigest(),
                  note="interpolation between centres of two monthly means; no surface or time extrapolation")
    (args.output / "manifest.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result))


if __name__ == "__main__":
    main()
