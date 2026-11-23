"""Build a six-tile North Atlantic & Nordic RNW1 atlas from monthly 1939-1945 ORA-20C."""
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
YEARS = tuple(range(1939, 1946))
LAT = (120, 165)  # 30.5-75.5 N (46 nodes)
LON = ((295, 329), (330, 359), (0, 25))  # -64.5..+25.5 E (91 nodes)
DIMS = (12, 5, LAT[1] - LAT[0] + 1)
MAX_REPLY = 4 * 1024 * 1024


def fetch(raw_dir, year, component, bounds):
    lon = f"[{bounds[0]}:{bounds[1]}]"
    lat = f"[{LAT[0]}:{LAT[1]}]"
    grid = "[0:4]" + lat + lon
    query = component + "[0:11]" + grid + ",mask" + grid
    query += ",depth[0:4],lat" + lat + ",lon" + lon + ",time[0:11]"
    url = BASE + f"{component}_ora20c_1m_{year}_grid_1x1.nc.gz.ascii?" + quote(query, safe=",")
    file = raw_dir / f"{year}_{component}_{bounds[0]}_{bounds[1]}.txt"
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


def merge_year(year, replies):
    reference = replies[0][0][:3]
    if any(reply[0][:3] != reference for reply in replies):
        raise ValueError(f"{year}: DAP replies disagree on shared axes")
    nlat = DIMS[2]
    widths = [len(reply[0][3]) for reply in replies]
    longitude = [lon - 360 if lon > 180 else lon for reply in replies for lon in reply[0][3]]
    if any(b != a + 1 for a, b in zip(longitude, longitude[1:])):
        raise ValueError(f"{year}: noncontiguous longitude grid")
    data = [value for row in range(DIMS[0] * DIMS[1] * nlat)
            for reply, width in zip(replies, widths) for value in reply[1][row * width:(row + 1) * width]]
    mask = [value for row in range(DIMS[1] * nlat)
            for reply, width in zip(replies, widths) for value in reply[2][row * width:(row + 1) * width]]
    epoch = calendar.timegm(datetime(year, 1, 1).timetuple())
    return [[epoch + t for t in reference[0]], *reference[1:], longitude], data, mask


def merge_years(yearly):
    base_axes = yearly[0][0][1:]
    for axes, _, mask in yearly[1:]:
        if axes[1:] != base_axes or mask != yearly[0][2]:
            raise ValueError("years disagree on spatial axes or wet mask")
    times = [t for axes, _, _ in yearly for t in axes[0]]
    data = [val for _, ydata, _ in yearly for val in ydata]
    return [times, *base_axes], data, yearly[0][2]


def tile(axes, east, north, mask, lat_range, lon_range, output, name):
    nt, nd, ny, nx = map(len, axes)
    y0, y1 = lat_range
    x0, x1 = lon_range
    rows = list(product(range(nt), [0, *range(nd)], range(y0, y1)))
    indices = [((t * nd + d) * ny + y) * nx + x for t, d, y in rows for x in range(x0, x1)]
    tile_mask = [mask[(d * ny + y) * nx + x]
                 for d, y in product([0, *range(nd)], range(y0, y1)) for x in range(x0, x1)]
    tile_axes = [axes[0], [0., *axes[1]], axes[2][y0:y1], axes[3][x0:x1]]
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

    tasks = [(year, component, bounds) for year in YEARS for component in ("uo", "vo") for bounds in LON]
    with ThreadPoolExecutor(max_workers=4) as executor:
        results = list(executor.map(lambda item: fetch(args.raw, *item), tasks))

    u_yearly = []
    v_yearly = []
    slices = []
    stride = len(LON)
    for y_idx, year in enumerate(YEARS):
        base = y_idx * 2 * stride
        u_replies = results[base:base + stride]
        v_replies = results[base + stride:base + 2 * stride]
        u_yearly.append(merge_year(year, u_replies))
        v_yearly.append(merge_year(year, v_replies))
        slices.extend(r[3] for r in u_replies + v_replies)

    axes, east, umask = merge_years(u_yearly)
    vaxes, north, vmask = merge_years(v_yearly)
    if axes != vaxes:
        raise ValueError("component axes disagree")
    mask = [a == b == 1 for a, b in zip(umask, vmask)]

    lat_split = axes[2].index(53.5)
    lon_west_seam = axes[3].index(-24.5)
    lon_zero_seam = axes[3].index(0.5)
    specs = (
        ((0, lat_split + 1), (0, lon_west_seam + 1), "ora_atlantic_sw.rnw"),
        ((0, lat_split + 1), (lon_west_seam, lon_zero_seam + 1), "ora_atlantic_sm.rnw"),
        ((0, lat_split + 1), (lon_zero_seam, len(axes[3])), "ora_atlantic_se.rnw"),
        ((lat_split, len(axes[2])), (0, lon_west_seam + 1), "ora_atlantic_nw.rnw"),
        ((lat_split, len(axes[2])), (lon_west_seam, lon_zero_seam + 1), "ora_atlantic_nm.rnw"),
        ((lat_split, len(axes[2])), (lon_zero_seam, len(axes[3])), "ora_atlantic_ne.rnw"),
    )
    tiles = [tile(axes, east, north, mask, lat_r, lon_r, args.output, name) for lat_r, lon_r, name in specs]
    top = axes[1][0]
    provenance = dict(
        schema=1,
        dataset="ECMWF ORA-20C, member opa0, WOA 1° grid, 1939-1945",
        provider="Integrated Climate Data Center (ICDC), Universität Hamburg",
        record_url=RECORD,
        attribution="ECMWF ORA-20C, distributed by ICDC, Universität Hamburg.",
        axes=dict(time_centres_utc_seconds=axes[0], depth_meters=[0., *axes[1]],
                  latitude_degrees=axes[2], longitude_degrees_east=axes[3]),
        surface=f"0 m repeats the shallowest model level ({top} m) and its wet mask; not observed surface data",
        quantization_mps=0.0001,
        max_quantization_error_mps=max(t["max_quantization_mps"] for t in tiles),
        tiles=tiles,
        source_slices=slices,
        terms=dict(status="Included by project decision with attribution; "
                          "no dataset-specific redistribution grant verified"),
    )
    (args.output / "atlas.provenance.json").write_text(
        json.dumps(provenance, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"{len(tiles)} tiles; {sum(t['bytes'] for t in tiles)} bytes")


if __name__ == "__main__":
    main()
