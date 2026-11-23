"""Build a two-tile North Atlantic RNW1 atlas from monthly 1941 ORA-20C."""
import argparse
import calendar
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime
import hashlib
from itertools import product
import json
from pathlib import Path
from urllib.parse import quote
from urllib.request import urlopen

from ora20c_region import BASE, pack, values


RECORD = ("https://cen.uni-hamburg.de/en/icdc/data/ocean/easy-init-ocean/"
          "ecmwf-ensemble-of-ocean-reanalyses-of-the-20th-century-ora-20c.html")
YEAR = 1941
LAT = (120, 150)  # 30.5–60.5° N
LON = ((300, 329), (330, 359), (0, 10))
DIMS = (12, 5, LAT[1] - LAT[0] + 1)
MAX_REPLY = 4 * 1024 * 1024


def fetch(raw_dir, component, bounds):
    lon = f"[{bounds[0]}:{bounds[1]}]"
    lat = f"[{LAT[0]}:{LAT[1]}]"
    grid = "[0:4]" + lat + lon
    query = component + "[0:11]" + grid + ",mask" + grid
    query += ",depth[0:4],lat" + lat + ",lon" + lon + ",time[0:11]"
    url = BASE + f"{component}_ora20c_1m_{YEAR}_grid_1x1.nc.gz.ascii?" + quote(query, safe=",")
    file = raw_dir / f"{component}_{bounds[0]}_{bounds[1]}.txt"
    if not file.exists():
        with urlopen(url, timeout=120) as response:
            payload = response.read(MAX_REPLY + 1)
        if len(payload) > MAX_REPLY:
            raise ValueError("oversized DAP response")
        file.write_bytes(payload)
    payload = file.read_bytes()
    text = payload.decode("ascii")
    shape = DIMS + (bounds[1] - bounds[0] + 1,)
    print(f"{file.name}: {len(payload)} bytes", flush=True)
    return ([values(text, name, (size,)) for name, size in zip(("time", "depth", "lat", "lon"), shape)],
            values(text, component, shape), values(text, "mask", shape[1:]),
            dict(file=file.name, url=url, sha256=hashlib.sha256(payload).hexdigest()))


def merge(replies):
    reference = replies[0][0][:3]
    if any(reply[0][:3] != reference for reply in replies):
        raise ValueError("DAP replies disagree on shared axes")
    nlat = DIMS[2]
    widths = [len(reply[0][3]) for reply in replies]
    longitude = [lon - 360 if lon > 180 else lon for reply in replies for lon in reply[0][3]]
    if any(b != a + 1 for a, b in zip(longitude, longitude[1:])):
        raise ValueError("noncontiguous longitude grid")
    data = [value for row in range(DIMS[0] * DIMS[1] * nlat)
            for reply, width in zip(replies, widths) for value in reply[1][row * width:(row + 1) * width]]
    mask = [value for row in range(DIMS[1] * nlat)
            for reply, width in zip(replies, widths) for value in reply[2][row * width:(row + 1) * width]]
    epoch = calendar.timegm(datetime(YEAR, 1, 1).timetuple())
    return [[epoch + t for t in reference[0]], *reference[1:], longitude], data, mask


def tile(axes, east, north, mask, start, stop, output, name):
    nt, nd, ny, nx = map(len, axes)
    # Constant reconstruction from the first wet model level up to 0 m.
    # Repeat the masks too: no new wet nodes or currents are invented on land.
    rows = list(product(range(nt), [0, *range(nd)], range(ny)))
    indices = [((t * nd + d) * ny + y) * nx + x
               for t, d, y in rows for x in range(start, stop)]
    tile_mask = [mask[(d * ny + y) * nx + x] for d, y in product([0, *range(nd)], range(ny))
                 for x in range(start, stop)]
    tile_axes = [axes[0], [0., *axes[1]], axes[2], axes[3][start:stop]]
    data, error, supported = pack(tile_axes, [east[i] for i in indices],
                                  [north[i] for i in indices], tile_mask)
    (output / name).write_bytes(data)
    return dict(file=name, bytes=len(data), sha256=hashlib.sha256(data).hexdigest(),
                supported=supported, total=len(indices), max_quantization_mps=error)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("raw", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    args.raw.mkdir(parents=True, exist_ok=True)
    args.output.mkdir(parents=True, exist_ok=False)
    with ThreadPoolExecutor(max_workers=2) as executor:
        futures = [executor.submit(fetch, args.raw, component, bounds)
                   for component, bounds in product(("uo", "vo"), LON)]
        u, v = [future.result() for future in futures[:3]], [future.result() for future in futures[3:]]
    axes, east, umask = merge(u)
    vaxes, north, vmask = merge(v)
    if axes != vaxes:
        raise ValueError("component axes disagree")
    mask = [a == b == 1 for a, b in zip(umask, vmask)]
    split = len(axes[3]) // 2
    tiles = [tile(axes, east, north, mask, start, stop, args.output, name)
             for start, stop, name in ((0, split + 1, "ora_atlantic_west.rnw"),
                                       (split, len(axes[3]), "ora_atlantic_east.rnw"))]
    top = axes[1][0]
    provenance = dict(schema=1, dataset="ECMWF ORA-20C, member opa0, WOA 1° grid, 1941",
                      provider="Integrated Climate Data Center (ICDC), Universität Hamburg",
                      record_url=RECORD, attribution="ECMWF ORA-20C, distributed by ICDC, Universität Hamburg.",
                      axes=dict(time_centres_utc_seconds=axes[0], depth_meters=[0., *axes[1]],
                                latitude_degrees=axes[2], longitude_degrees_east=axes[3]),
                      surface=f"0 m repeats the shallowest model level ({top} m) and its wet mask; not observed surface data",
                      quantization_mps=0.0001,
                      max_quantization_error_mps=max(t["max_quantization_mps"] for t in tiles),
                      tiles=tiles, source_slices=[r[3] for r in u + v],
                      terms=dict(status="Included by project decision with attribution; "
                                        "no dataset-specific redistribution grant verified"))
    (args.output / "atlas.provenance.json").write_text(
        json.dumps(provenance, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"{len(tiles)} tiles; {sum(t['bytes'] for t in tiles)} bytes")


if __name__ == "__main__":
    main()
