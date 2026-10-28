"""Read-only audit of installed UBOAT D3D11 water curvature, no game or network.

UnityPy reads the AssetBundle; Windows D3DDisassemble documents the instructions.
The actual constant fixture comes from raw DXBC float bits, NOT the six-decimal
disassembly, a radius formula, editor includes, or an observed pixel discrepancy.
All generated output must be under this repository's artifacts/ directory.
"""
import argparse
import ctypes
import hashlib
import json
import re
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_BUNDLE = Path(r"C:\Program Files (x86)\Steam\steamapps\common\UBOAT\UBOAT_Data\StreamingAssets\Packages\uboat\Bundles\uboat.core")
DEFAULT_OUTPUT = ROOT / "artifacts/reports/solar-table-20261008/horizon-20261009/shader"


def fingerprint(path):
    stat = path.stat()
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for part in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(part)
    return {"path": str(path), "sha256": digest.hexdigest(), "length": stat.st_size,
            "mtime_ns": stat.st_mtime_ns}


def under_artifacts(path):
    path = path.resolve()
    if not path.is_relative_to((ROOT / "artifacts").resolve()):
        raise ValueError("Output must be under this repository's artifacts/.")
    return path


def dxbc_from_code(code):
    # Unity stores a short platform-specific prefix before the DXBC container.
    start = code.find(b"DXBC")
    if start < 0:
        return None
    if len(code) - start < 32:
        raise ValueError("Truncated DXBC header.")
    length = struct.unpack_from("<I", code, start + 24)[0]
    if length < 32 or start + length > len(code):
        raise ValueError("Invalid DXBC container size.")
    return code[start:start + length]


def constant_buffers(dxbc):
    """Decode only D3D10/11 CUSTOMDATA immediate-constant buffers, fail closed.

    DXBC chunk SHDR/SHEX is a tokenized program. CUSTOMDATA opcode=53, class=3
    is an immediate constant buffer; its second DWORD includes the two-word
    header. Other instructions use the length in opcode-token bits 24..30.
    """
    if len(dxbc) < 32 or dxbc[:4] != b"DXBC":
        raise ValueError("Missing DXBC container.")
    length, count = struct.unpack_from("<II", dxbc, 24)
    if length != len(dxbc) or 32 + count * 4 > length:
        raise ValueError("Invalid DXBC chunk table.")
    result = []
    for index in range(count):
        offset = struct.unpack_from("<I", dxbc, 32 + index * 4)[0]
        if offset + 8 > length:
            raise ValueError("Truncated DXBC chunk header.")
        tag = dxbc[offset:offset + 4]
        size = struct.unpack_from("<I", dxbc, offset + 4)[0]
        if offset + 8 + size > length:
            raise ValueError("Truncated DXBC chunk.")
        if tag not in (b"SHDR", b"SHEX"):
            continue
        if size < 8 or size % 4:
            raise ValueError("Invalid tokenized program size.")
        words = struct.unpack_from("<" + "I" * (size // 4), dxbc, offset + 8)
        if words[1] != len(words):
            raise ValueError("Tokenized program length mismatch.")
        cursor = 2
        while cursor < len(words):
            token = words[cursor]
            opcode = token & 0x7FF
            if opcode == 53:
                if cursor + 1 >= len(words):
                    raise ValueError("Truncated CUSTOMDATA.")
                instruction_length = words[cursor + 1]
                if instruction_length < 2 or cursor + instruction_length > len(words):
                    raise ValueError("Invalid CUSTOMDATA length.")
                if token >> 11 == 3:
                    payload = words[cursor + 2:cursor + instruction_length]
                    if len(payload) % 4:
                        raise ValueError("Invalid immediate constant vector count.")
                    result.append([list(payload[i:i + 4]) for i in range(0, len(payload), 4)])
            else:
                instruction_length = (token >> 24) & 0x7F
                if instruction_length == 0 or cursor + instruction_length > len(words):
                    raise ValueError("Invalid shader instruction length.")
            cursor += instruction_length
    return result


def curvature_table(buffers):
    # Identify the full 0..20 km water table, not an arbitrary smaller ICB.
    candidates = []
    for vectors in buffers:
        if len(vectors) != 81 or any(any(row[1:]) for row in vectors):
            continue
        values = [struct.unpack("<f", struct.pack("<I", row[0]))[0] for row in vectors]
        if values[0] == 0 and all(values[i] < values[i - 1] for i in range(1, 81)):
            candidates.append({"drop_float32": [-x for x in values],
                               "signed_x_bits": [f"{row[0]:08x}" for row in vectors]})
    if len(candidates) > 1:
        raise ValueError("Ambiguous curvature constant buffers.")
    return candidates[0] if candidates else None


def disassemble(code):
    library = ctypes.WinDLL("d3dcompiler_47.dll")
    call = library.D3DDisassemble
    call.restype = ctypes.c_long
    call.argtypes = [ctypes.c_void_p, ctypes.c_size_t, ctypes.c_uint,
                     ctypes.c_char_p, ctypes.POINTER(ctypes.c_void_p)]
    blob = ctypes.c_void_p()
    buffer = ctypes.create_string_buffer(code)
    error = call(buffer, len(code), 0, None, ctypes.byref(blob))
    if error < 0 or not blob.value:
        raise OSError(f"D3DDisassemble failed: 0x{error & 0xFFFFFFFF:08x}")
    vtable = ctypes.cast(blob, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
    get_pointer = ctypes.WINFUNCTYPE(ctypes.c_void_p, ctypes.c_void_p)(vtable[3])
    get_size = ctypes.WINFUNCTYPE(ctypes.c_size_t, ctypes.c_void_p)(vtable[4])
    release = ctypes.WINFUNCTYPE(ctypes.c_ulong, ctypes.c_void_p)(vtable[2])
    try:
        return ctypes.string_at(get_pointer(blob), get_size(blob)).rstrip(b"\0").decode("utf-8").replace("\r\n", "\n")
    finally:
        release(blob)


def validate_indexing(text):
    """Receipt for used indexing/interpolation, not just existence of an ICB."""
    required = {
        "distance": r"dp2\s.*\n\s*sqrt\s",
        "250m_index": r"mul\s[^\n]*l\(0\.004000\)",
        "79_max_index": r"min\s[^\n]*l\(79\.000000\)",
        "floor_index": r"round_ni\s",
        "next_index": r"add\s[^\n]*l\(1\.000000\)",
        "fetch_difference": r"add\s[^\n]*-icb\[[^\n]*icb\[",
        "linear_interpolation": r"mad\s[^\n]*icb\[",
        "binary_curvature_gate": r"eq\s[^\n]*l\(1\.000000\)",
    }
    evidence = {name: bool(re.search(pattern, text)) for name, pattern in required.items()}
    if not all(evidence.values()):
        raise ValueError(f"Curvature-bearing program has unsupported indexing: {evidence}")
    return evidence


def audit(bundle, output):
    # Optional dependencies are loaded only for the real installed-bundle audit.
    import UnityPy
    from UnityPy.export import ShaderConverter as converter
    from UnityPy.streams import EndianBinaryReader

    output = under_artifacts(output)
    if (output / "audit.json").exists():
        raise ValueError("Audit receipt exists; choose a distinct artifacts output directory.")
    before = fingerprint(bundle)
    env = UnityPy.load(str(bundle))
    variants, groups, shader_names = [], {}, []
    output.mkdir(parents=True, exist_ok=True)
    for obj in env.objects:
        if obj.type.name != "Shader":
            continue
        shader = obj.read()
        name = shader.m_ParsedForm.m_Name
        if not name.startswith("PlayWay Water/"):
            continue
        shader_names.append(name)
        compressed = bytes(shader.compressedBlob)
        for platform_index, platform in enumerate(shader.platforms):
            # Unity ShaderCompilerPlatform D3D=4 (the Windows D3D11 shaders).
            if platform != 4:
                continue
            entries = lambda array: array[platform_index][0] if isinstance(array[platform_index], list) else array[platform_index]
            offset = entries(shader.offsets)
            size = entries(shader.compressedLengths)
            raw = converter.CompressionHelper.decompress_lz4(
                compressed[offset:offset + size], entries(shader.decompressedLengths))
            program = converter.ShaderProgram(EndianBinaryReader(raw, endian="<"), obj.version)
            for program_index, sub in enumerate(program.m_SubPrograms):
                # New Unity blobs also hold serialized parameter records. The
                # generic UnityPy program reader visits those too; they are not
                # D3D11 shader programs and may contain incidental "DXBC" text.
                if not sub.m_ProgramType.name.startswith("kShaderGpuProgramDX11"):
                    continue
                try:
                    bytecode = dxbc_from_code(bytes(sub.m_ProgramCode))
                except ValueError as error:
                    raise ValueError(f"{name} program {program_index} ({sub.m_ProgramType.name}): {error}; "
                                     f"code length={len(sub.m_ProgramCode)}, prefix={bytes(sub.m_ProgramCode[:40]).hex()}") from error
                if bytecode is None:
                    continue
                table = curvature_table(constant_buffers(bytecode))
                if table is None:
                    continue
                digest = hashlib.sha256(bytecode).hexdigest()
                text = disassemble(bytecode)
                indexing = validate_indexing(text)
                table_hash = hashlib.sha256(bytes.fromhex("".join(table["signed_x_bits"]))).hexdigest()
                if table_hash not in groups:
                    groups[table_hash] = table
                bytecode_file = output / (digest + ".dxbc")
                if not bytecode_file.exists():
                    bytecode_file.write_bytes(bytecode)
                    (output / (digest + ".asm")).write_text(text, encoding="utf-8")
                variants.append({"shader": name, "path_id": obj.path_id,
                                 "unity_version": list(obj.version), "platform_index": platform_index,
                                 "program_index": program_index, "stage": sub.m_ProgramType.name,
                                 "keywords": sub.m_Keywords, "bytecode_sha256": digest,
                                 "table_hash": table_hash, "indexing": indexing})
    if not variants:
        raise ValueError("No D3D11 water curvature table found; no fixture is verified.")
    if fingerprint(bundle) != before:
        raise ValueError("Installed bundle changed during read-only extraction.")
    result = {"schema": 1, "bundle": before, "unitypy_version": UnityPy.__version__,
              "shader_names_scanned": sorted(shader_names), "variants": variants, "tables": groups,
              "bundle_unchanged": True, "game_started": False, "gpu_submission_verified": False,
              "limits": "Installed programs prove constants/code, not the active variant, mesh or presented sea silhouette."}
    (output / "audit.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print({"water_shaders": len(shader_names), "curvature_variants": len(variants),
           "distinct_tables": len(groups), "distinct_bytecodes": len({v["bytecode_sha256"] for v in variants}),
           "bundle_unchanged": True, "output": str(output)})
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, default=DEFAULT_BUNDLE)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    audit(args.bundle.resolve(), args.output)
