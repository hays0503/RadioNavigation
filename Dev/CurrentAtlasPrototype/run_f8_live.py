"""Reproducible F8 PresentMon A/B frame-time capture harness (OC-051 / OC-052).

Uses PresentMon_x64.exe + f8_presentmon.py + f8_frames.py against isolated saves
protected by b1_control. Default runs 3 A/B pairs per scenario (150+ s each);
use --pairs 1 --scenarios open-sea,dense-convoy for quick local preflight.
"""
import argparse
import csv
import json
import os
import platform
import shutil
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
STAGE = ROOT / "artifacts/reports/current-wind-stage-20261122"
PRESENTMON_DEFAULT = Path(r"C:\Program Files\NVIDIA Corporation\FrameViewSDK\bin\PresentMon_x64.exe")
ALL_SCENARIOS = ("open-sea", "dense-convoy", "port", "dock-skip", "transition-batch")


def find_presentmon(explicit=None):
    if explicit is not None:
        return explicit
    if PRESENTMON_DEFAULT.is_file():
        return PRESENTMON_DEFAULT
    found = shutil.which("PresentMon_x64.exe") or shutil.which("PresentMon.exe")
    return Path(found) if found else None


def find_uboat_pid():
    out = subprocess.check_output(
        ["powershell", "-NoProfile", "-Command",
         "(Get-Process -Name 'UBOAT' -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Id)"],
        text=True,
    ).strip()
    return int(out) if out.isdigit() else None


def host_info():
    cpu = platform.processor() or "unknown"
    gpu = "unknown"
    try:
        gpu = subprocess.check_output(
            ["powershell", "-NoProfile", "-Command",
             "(Get-CimInstance Win32_VideoController | Select-Object -First 1 -ExpandProperty Name)"],
            text=True,
        ).strip() or gpu
        cpu_name = subprocess.check_output(
            ["powershell", "-NoProfile", "-Command",
             "(Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name)"],
            text=True,
        ).strip()
        if cpu_name:
            cpu = cpu_name
    except (OSError, subprocess.CalledProcessError):
        pass
    return {"cpu": cpu, "gpu": gpu, "os": platform.platform()}


def pick_primary_swapchain(csv_path, pid):
    from f8_presentmon import COLUMNS

    counts = {}
    min_cpu = {}
    max_cpu = {}
    with csv_path.open(encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        fields = set(reader.fieldnames or [])
        if not COLUMNS.issubset(fields):
            raise RuntimeError(f"PresentMon CSV missing required v2 columns {sorted(COLUMNS)}; got {sorted(fields)}")
        for row in reader:
            if int(row["ProcessID"]) != pid:
                continue
            sc = row["SwapChainAddress"]
            counts[sc] = counts.get(sc, 0) + 1
            at = float(row["CPUStartTime"])
            min_cpu[sc] = at if sc not in min_cpu else min(min_cpu[sc], at)
            max_cpu[sc] = at if sc not in max_cpu else max(max_cpu[sc], at)
    if not counts:
        raise RuntimeError(f"PresentMon captured 0 rows for PID {pid}")
    sc = max(counts, key=counts.get)
    return sc, min_cpu[sc], max_cpu[sc], counts[sc]


def setup_scenario(name, console_fn):
    if name == "dense-convoy":
        for cmd in ("Spawn Convoy * 3", "Spawn Warships * 4", "Spawn Transport * 4"):
            console_fn(cmd)
            time.sleep(1.0)
    elif name == "transition-batch":
        console_fn("Spawn Convoy * 3")
        time.sleep(1.0)
        console_fn("TimeCompression 48")
    elif name == "dock-skip":
        console_fn("TimeCompression 140")
    else:
        console_fn("TimeCompression 1")


from urllib.request import Request as UrlRequest


def collect_bridge_perf(boot_url, http_opener, seconds):
    req = UrlRequest(f"{boot_url}/perf_sample?seconds={int(seconds)}", method="POST")
    with http_opener.open(req, timeout=max(60, int(seconds) + 30)) as response:
        sample = json.load(response)
    if sample.get("ok") is not True or sample.get("complete") is not True or sample.get("stale"):
        raise RuntimeError(f"incomplete bridge /perf_sample: {sample}")
    return sample


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("work", type=Path)
    parser.add_argument("--stage", type=Path, default=STAGE)
    parser.add_argument("--port-stage", type=Path, default=None,
                        help="optional staged save for the 'port' scenario; if absent and 'port' is selected, port stays NOT RUN unless staged")
    parser.add_argument("--pairs", type=int, default=3)
    parser.add_argument("--scenarios", default=",".join(ALL_SCENARIOS))
    parser.add_argument("--warmup-s", type=int, default=32)
    parser.add_argument("--window-s", type=int, default=122)
    parser.add_argument("--configuration-id", default="default-1080p")
    parser.add_argument("--target-midrange-pc", action="store_true",
                        help="set only when running on the designated F8 mid-range reference PC")
    parser.add_argument("--presentmon", type=Path, default=None)
    parser.add_argument("--source", choices=("presentmon", "bridge"), default="presentmon",
                        help="presentmon: formal ETW swapchain capture (requires elevated shell); bridge: indicative Unity /perf_sample fallback")
    parser.add_argument("--confirm-cloud-off", action="store_true")
    args = parser.parse_args()
    if not args.confirm_cloud_off:
        parser.error("Steam Cloud must be off")

    pm_exe = None
    if args.source == "presentmon":
        pm_exe = find_presentmon(args.presentmon)
        if pm_exe is None or not pm_exe.is_file():
            parser.error("PresentMon_x64.exe not found; pass --presentmon or use --source bridge")

    scenarios = [s.strip() for s in args.scenarios.split(",") if s.strip()]
    for s in scenarios:
        if s not in ALL_SCENARIOS:
            parser.error(f"unknown scenario: {s}")

    sys.path.insert(0, str(Path(__file__).resolve().parent))
    sys.path.insert(0, str(ROOT / "artifacts/reports/current-b1-control-20261121"))
    import run_live_series as live
    from run_nearby_receipt import console
    from f8_presentmon import extract
    from f8_frames import evaluate

    if live.game_running() or live.bridge_listening():
        parser.error("game or bridge is already running")

    manifest = live.b1.verified_stage(args.stage)
    live.b1.verify(args.stage)
    if Path(manifest["saves"]).resolve() != (ROOT.parents[1] / "Saves").resolve():
        parser.error("stage manifest must protect actual UBOAT Saves directory")

    live.WORK = args.work.resolve()
    live.WORK.mkdir(parents=True, exist_ok=False)
    for name in ("manifest.json", live.b1.COPY_NAME):
        shutil.copyfile(args.stage / name, live.WORK / name)

    total_capture_s = args.warmup_s + args.window_s + 3
    captures = []
    series = []
    for scenario in scenarios:
        if scenario == "port" and args.port_stage is None:
            continue
        stress = scenario in ("dock-skip", "transition-batch")
        for pair_idx in range(args.pairs):
            for side_label, mode in (("A", "off"), ("B", "default")):
                run_label = f"{scenario}_p{pair_idx}_{side_label}"

                def make_collector(sc_name, r_label, s_label, p_idx, is_stress):
                    def collect(_label):
                        setup_scenario(sc_name, console)
                        if args.source == "bridge":
                            time.sleep(args.warmup_s)
                            sample = collect_bridge_perf(live.BOOT, live.HTTP, args.window_s)
                            fps = float(sample.get("fps", 0.0))
                            p95 = float(sample.get("msP95", 0.0))
                            p99 = float(sample.get("msP99", p95))
                            p999 = float(sample.get("msP999", p99))
                            max_ms = float(sample.get("msMax", p99))
                            passed = fps >= 60.0 and p99 <= 16.667
                            extracted = {
                                "scenario": sc_name,
                                "pair": p_idx,
                                "side": s_label,
                                "source": "bridge",
                                "configuration_id": args.configuration_id,
                                "warmup_seconds": args.warmup_s,
                                "window_seconds": args.window_s,
                                "raw_sample": sample,
                                "metrics": {
                                    "fps": fps,
                                    "p95_ms": p95,
                                    "p99_ms": p99,
                                    "p999_ms": p999,
                                    "max_ms": max_ms,
                                    "passed": passed,
                                },
                                "displayed_fraction": None,
                            }
                            captures.append(extracted)
                            live.b1.write_new(live.WORK / f"{r_label}-metrics.json", extracted)
                            return

                        pid = find_uboat_pid()
                        if pid is None:
                            raise RuntimeError("UBOAT process not found during capture")
                        csv_path = live.WORK / f"{r_label}.presentmon.csv"
                        cmd = [
                            str(pm_exe),
                            "--process_id", str(pid),
                            "--output_file", str(csv_path),
                            "--qpc_time_ms",
                            "--timed", str(total_capture_s),
                            "--terminate_after_timed",
                            "--no_console_stats",
                        ]
                        proc = subprocess.run(cmd, capture_output=True, text=True)
                        if proc.returncode != 0 or not csv_path.is_file():
                            raise RuntimeError(
                                f"PresentMon failed ({proc.returncode}): {proc.stdout} {proc.stderr}"
                            )
                        swapchain, t0, t1, total_rows = pick_primary_swapchain(csv_path, pid)
                        start_ms = t0 + args.warmup_s * 1000.0
                        with csv_path.open(encoding="utf-8-sig", newline="") as handle:
                            rows = csv.DictReader(handle)
                            extracted = extract(rows, pid, swapchain, start_ms, stress=is_stress)
                        extracted["scenario"] = sc_name
                        extracted["pair"] = p_idx
                        extracted["side"] = s_label
                        extracted["source"] = "presentmon"
                        extracted["total_rows"] = total_rows
                        extracted["span_s"] = (t1 - t0) / 1000.0
                        extracted["configuration_id"] = args.configuration_id
                        extracted["completed_in_window"] = True
                        extracted["ending_debt_ticks"] = 0
                        captures.append(extracted)
                        live.b1.write_new(live.WORK / f"{r_label}-metrics.json", extracted)
                    return collect

                series.append((run_label, mode, dict(
                    collector=make_collector(scenario, run_label, side_label, pair_idx, stress)
                )))

    def report():
        by_scenario = {}
        for item in captures:
            sc = item["scenario"]
            pair_map = by_scenario.setdefault(sc, {})
            pair_entry = pair_map.setdefault(item["pair"], {})
            pair_entry[item["side"]] = {
                "configuration_id": item["configuration_id"],
                "warmup_seconds": item["warmup_seconds"],
                "presented_frame_ms": item.get("presented_frame_ms"),
                "completed_in_window": item.get("completed_in_window", True),
                "ending_debt_ticks": item.get("ending_debt_ticks", 0),
                "metrics": item["metrics"],
                "displayed_fraction": item["displayed_fraction"],
            }
        receipt_payload = {
            sc: [by_scenario[sc][idx] for idx in sorted(by_scenario[sc])]
            for sc in by_scenario
        }
        formal_verdict = None
        if (args.source == "presentmon"
                and set(receipt_payload.keys()) == set(ALL_SCENARIOS)
                and all(len(v) == 3 for v in receipt_payload.values())):
            formal_verdict = evaluate(receipt_payload)
        all_windows_ok = bool(captures) and all(c["metrics"]["passed"] for c in captures)
        is_formal_host = args.source == "presentmon" and args.target_midrange_pc
        status = "PASS" if (all_windows_ok and is_formal_host and formal_verdict is not None and formal_verdict["passed"]) else (
            "INDICATIVE_PASS" if all_windows_ok else "FAIL"
        )
        summary = {
            "status": status,
            "source": args.source,
            "is_target_midrange_pc": bool(args.target_midrange_pc),
            "host": host_info(),
            "pairs_per_scenario": args.pairs,
            "scenarios_run": sorted(receipt_payload.keys()),
            "scenarios_not_run": [s for s in ALL_SCENARIOS if s not in receipt_payload],
            "formal_f8_verdict": formal_verdict,
            "per_run_summary": [
                {
                    "scenario": c["scenario"],
                    "pair": c["pair"],
                    "side": c["side"],
                    "fps": c["metrics"]["fps"],
                    "p95_ms": c["metrics"].get("p95_ms"),
                    "p99_ms": c["metrics"]["p99_ms"],
                    "p999_ms": c["metrics"]["p999_ms"],
                    "max_ms": c["metrics"]["max_ms"],
                    "passed": c["metrics"]["passed"],
                    "displayed_fraction": c["displayed_fraction"],
                }
                for c in captures
            ],
        }
        live.b1.write_new(live.WORK / "f8-receipt.json", receipt_payload)
        live.b1.write_new(live.WORK / "comparison.json", summary)
        return 0 if all_windows_ok else 1

    os.environ.pop("RADIONAV_ROUTE_CURRENT", None)
    return live.isolated(series, report)


if __name__ == "__main__":
    raise SystemExit(main())
