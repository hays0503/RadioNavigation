"""No game, network or bundle writes: extraction parser and provenance regressions."""
import importlib.util
import json
import struct
import sys
import unittest
from pathlib import Path

TOOL = Path(__file__).resolve().parents[1] / "audit_water_shader.py"
spec = importlib.util.spec_from_file_location("audit_water_shader", TOOL)
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


def container(payload, custom_class=3):
    tokens = [0x00050050, 0, 53 | (custom_class << 11), len(payload) + 2, *payload,
              (1 << 24) | 62]  # SM5 token header, CUSTOMDATA, ret
    tokens[1] = len(tokens)
    code = struct.pack("<" + "I" * len(tokens), *tokens)
    chunk = b"SHEX" + struct.pack("<I", len(code)) + code
    length = 36 + len(chunk)
    return b"DXBC" + bytes(16) + struct.pack("<IIII", 1, length, 1, 36) + chunk


def fixture_payload():
    fixture = json.loads((TOOL.parent / "water-curvature-verified.json").read_text(encoding="utf-8"))
    return fixture, [v for text in fixture["signed_x_bits"] for v in (int(text, 16), 0, 0, 0)]


class WaterShaderTests(unittest.TestCase):
    def test_exact_raw_bits_not_disassembly_rounding(self):
        fixture, payload = fixture_payload()
        table = audit.curvature_table(audit.constant_buffers(container(payload)))
        self.assertEqual(fixture["signed_x_bits"], table["signed_x_bits"])
        self.assertEqual(0.004905038978904486, table["drop_float32"][1])
        self.assertNotEqual(0.004905, table["drop_float32"][1])

    def test_customdata_class_and_vector_shape_are_required(self):
        _, payload = fixture_payload()
        self.assertEqual([], audit.constant_buffers(container(payload, custom_class=2)))
        with self.assertRaises(ValueError):
            audit.constant_buffers(container(payload + [0]))

    def test_truncated_or_corrupted_container_never_becomes_a_fixture(self):
        _, payload = fixture_payload()
        good = container(payload)
        bad_chunk = bytearray(good)
        struct.pack_into("<I", bad_chunk, 32, len(good) - 4)
        bad_program = bytearray(good)
        struct.pack_into("<I", bad_program, 48, 2)
        for bad in (b"", good[:-1], bytes(bad_chunk), bytes(bad_program)):
            with self.subTest(length=len(bad)), self.assertRaises(ValueError):
                audit.constant_buffers(bad)

    def test_invalid_or_ambiguous_water_table_is_rejected(self):
        _, payload = fixture_payload()
        buffers = audit.constant_buffers(container(payload))
        with self.assertRaises(ValueError):
            audit.curvature_table(buffers + buffers)
        buffers[0][10][1] = 1
        self.assertIsNone(audit.curvature_table(buffers))

    def test_unity_prefix_is_removed_without_guessing_a_container_length(self):
        _, payload = fixture_payload()
        dxbc = container(payload)
        self.assertEqual(dxbc, audit.dxbc_from_code(b"\x01\x00\x00\x00" + dxbc))
        self.assertIsNone(audit.dxbc_from_code(b"not a shader"))
        with self.assertRaises(ValueError):
            audit.dxbc_from_code(b"DXBC" + bytes(35))

    def test_indexing_receipt_requires_each_used_shader_operation(self):
        operations = ["dp2 r0.x, r0.xy, r0.xy", "sqrt r0.x, r0.x",
                      "mul r0.x, r0.x, l(0.004000)", "min r0.x, r0.x, l(79.000000)",
                      "round_ni r0.y, r0.x", "add r1.x, r0.y, l(1.000000)",
                      "add r1.y, -icb[r2.x].x, icb[r3.x].x",
                      "mad r0.x, r0.x, r1.y, icb[r2.x].x",
                      "eq r0.x, cb0[4].x, l(1.000000)"]
        self.assertTrue(all(audit.validate_indexing("\n".join(operations)).values()))
        for index in range(len(operations)):
            with self.subTest(missing=operations[index]), self.assertRaises(ValueError):
                audit.validate_indexing("\n".join(operations[:index] + operations[index + 1:]))

    def test_output_cannot_escape_into_sources_or_readonly_tables(self):
        self.assertEqual(audit.DEFAULT_OUTPUT, audit.under_artifacts(audit.DEFAULT_OUTPUT))
        for path in (audit.ROOT / "Source/Data/test.json", audit.ROOT / "documents-readonly/out",
                     audit.ROOT / "artifacts/../Tools/out"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                audit.under_artifacts(path)

    def test_checked_fixture_matches_the_readonly_current_bundle_receipt(self):
        # This check uses an existing offline extraction; never starts the game.
        path = audit.DEFAULT_OUTPUT / "audit.json"
        if not path.is_file():
            self.skipTest("No installed-bundle audit receipt on this machine.")
        receipt = json.loads(path.read_text(encoding="utf-8"))
        fixture, _ = fixture_payload()
        self.assertEqual(fixture["bundle_sha256"], receipt["bundle"]["sha256"])
        self.assertEqual(fixture["bundle_length"], receipt["bundle"]["length"])
        self.assertEqual(1, len(receipt["tables"]))
        self.assertEqual(fixture["signed_x_bits"], receipt["tables"][fixture["table_hash"]]["signed_x_bits"])
        self.assertEqual(fixture["curvature_variants"], len(receipt["variants"]))
        self.assertEqual(fixture["distinct_bytecodes"], len({v["bytecode_sha256"] for v in receipt["variants"]}))
        for variant in receipt["variants"]:
            self.assertTrue(all(variant["indexing"].values()))
            dxbc = (path.parent / (variant["bytecode_sha256"] + ".dxbc")).read_bytes()
            self.assertEqual(variant["bytecode_sha256"], audit.hashlib.sha256(dxbc).hexdigest())
            table = audit.curvature_table(audit.constant_buffers(dxbc))
            self.assertEqual(fixture["signed_x_bits"], table["signed_x_bits"])


if __name__ == "__main__":
    unittest.main(testRunner=unittest.TextTestRunner(stream=sys.stdout, verbosity=2))
