import json
from io import StringIO
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch

import b1_control as b1


SCRIPT = Path(__file__).with_name("b1_control.py")


class B1ControlTests(unittest.TestCase):
    def run_cli(self, *args, code=0):
        result = subprocess.run([sys.executable, str(SCRIPT), *map(str, args)],
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, code, result.stdout + result.stderr)
        return json.loads(result.stdout) if code != 2 else result.stderr

    def test_copy_and_restore_rehearsal_never_write_protected_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            saves = root / "Saves"
            saves.mkdir()
            source = saves / "original.save"
            source.write_bytes(b"original")
            other = saves / "nested"
            other.mkdir()
            (other / "other.save").write_bytes(b"other")
            work = root / "receipt"
            receipt = self.run_cli("prepare", saves, source.name, work)
            self.assertEqual(receipt["protected_file_count"], 2)
            self.assertTrue(receipt["restore_rehearsal"])
            self.assertEqual((work / "RadioNav_B1_Control.save").read_bytes(), b"original")
            self.assertEqual(sorted(p.name for p in saves.iterdir()), ["nested", "original.save"])
            self.assertEqual(source.read_bytes(), b"original")
            self.run_cli("verify", work)
            (other / "other.save").write_bytes(b"changed")
            self.assertIn("nested/other.save", self.run_cli("verify", work, code=2))

    def test_control_compares_600_seconds_on_common_ticks_in_game_meters(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            paths = []
            start = 612470741400000001
            for index, mode in enumerate(("off", "off", "zero")):
                path = root / f"{index}.json"
                offset = (0, 2e-4, 1e-4)[index]
                rows = [[start + s * 10_000_000, -1752 + offset, 5372, 0, 4, False, 0]
                        for s in range(601)]
                payload = dict(mode=mode, save_sha256="a" * 64, start_tick=start, rows=rows)
                path.write_text(json.dumps(payload))
                paths.append(path)
            result = self.run_cli("compare", *paths)
            self.assertTrue(result["within_off_spread"])
            self.assertAlmostEqual(result["off_spread"]["position_game_m"], 0.2, places=6)
            self.assertAlmostEqual(result["zero_difference"]["position_game_m"], 0.1, places=6)
            payload["rows"][300][1] += 0.001
            paths[-1].write_text(json.dumps(payload))
            self.assertFalse(self.run_cli("compare", *paths, code=1)["within_off_spread"])
            payload["rows"].pop(300)
            paths[-1].write_text(json.dumps(payload))
            self.assertIn("sample gap", self.run_cli("compare", *paths, code=2))

    def test_capture_keeps_raw_journal_but_rejects_gapped_window(self):
        with tempfile.TemporaryDirectory() as directory:
            work = Path(directory)
            saves = work / "Saves"
            saves.mkdir()
            (work / b1.COPY_NAME).write_bytes(b"fixture")
            (saves / b1.COPY_NAME).write_bytes(b"fixture")
            (work / "manifest.json").write_text(json.dumps(
                dict(saves=str(saves.resolve()), save_sha256=b1.file_hash(work / b1.COPY_NAME))))
            log = work / "log.txt"
            log.write_text("[RadioNav][CURRENT] disabled (RADIONAV_CURRENT_FIELD unset)")
            output = work / "off.json"
            opener = Mock()
            opener.open.side_effect = [StringIO(json.dumps(dict(
                ok=True, sandbox=True, loading=dict(ready=True),
                time=dict(ok=True, current=f"1941-11-05T{stamp}.0000001"),
                position=dict(ok=True, x=1, z=2),
                ship=dict(ok=True, velocity=0, depth=0, targetDepth=0, docked=False))))
                for stamp in ("04:00:00", "04:10:00")]
            with patch.object(b1, "build_opener", return_value=opener), patch.object(b1.time, "sleep"):
                with self.assertRaisesRegex(ValueError, "sample gap"):
                    b1.capture(work, "off", "1941-11-05T04:00:00.0000001", output, log)
            self.assertFalse(output.exists())
            rows = [json.loads(line)["row"] for line in
                    output.with_suffix(".samples.jsonl").read_text().splitlines()]
            self.assertEqual(rows[-1][0] - rows[0][0], 6_000_000_000)
            self.assertEqual(rows[0][0] % b1.TICKS, 1)

    def test_zero_mode_requires_no_production_detour(self):
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / "game.log"
            request = "[RadioNav][CURRENT] requested field east=0 north=0 m/s\n"
            log.write_text(request + "[RadioNav][CURRENT] water drag transpilers installed\n")
            with self.assertRaisesRegex(ValueError, "does not corroborate"):
                b1.check_log(log, "zero")
            log.write_text(request + "[RadioNav][CURRENT] no ambient flow requested; no water drag transpilers installed\n")
            b1.check_log(log, "zero")


if __name__ == "__main__":
    unittest.main()
