"""Mutation witness: source-shape checks must fail when a checked marker moves."""
from pathlib import Path
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parent
SOURCE = Path(r"C:\code\projects\uboat")
TARGETS = {
    "coords.x": ("Weather.cs", "coords.x += weatherSeed", "coords.x = weatherSeed"),
    "windSpeedOverride": ("Weather.cs", "windSpeedOverride > 0f", "windSpeedOverride >= 0f"),
    "40f": ("Weather.cs", "40f * windSpeedProbabilityCurve.Evaluate", "30f * windSpeedProbabilityCurve.Evaluate"),
}


def main():
    for name, (file_name, old, new) in TARGETS.items():
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / "game"
            for source_file in SOURCE.rglob("*.cs"):
                if source_file.name in {"Weather.cs", "Perlin.cs", "Parameter.cs"}:
                    target = root / source_file.relative_to(SOURCE)
                    target.parent.mkdir(parents=True, exist_ok=True)
                    text = source_file.read_text(encoding="utf-8")
                    if source_file.name == file_name:
                        assert text.count(old) >= 1, name
                        # Replace every occurrence so duplicated branches cannot
                        # satisfy the marker while the witness mutates only one.
                        text = text.replace(old, new)
                    target.write_text(text, encoding="utf-8")
            result = subprocess.run([sys.executable, str(ROOT / "verify_sources.py"),
                                     "--game-source", str(root)], capture_output=True, text=True)
            assert result.returncode != 0, result.stdout
            assert "AssertionError" in result.stderr, result.stderr
            print("EXPECTED FAIL", name)


if __name__ == "__main__":
    main()
