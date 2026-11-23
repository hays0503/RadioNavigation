"""Check the local source shape assumptions behind the wind probe."""
import argparse
from pathlib import Path
import re


FILES = {
    "Weather.cs": [
        r"dateTime\.Ticks - gameTime\.CampaignStartStoryTicks",
        r"gameTime\.StoryTicksSinceCampaignStart",
        r"40f \* windSpeedProbabilityCurve\.Evaluate\(NextPerlin\(ref coords\)\)",
        r"NextPerlin\(ref coords2\) \* 360f",
        r"coords\.x \+= weatherSeed",
        r"windSpeedOverride > 0f",
        r"private const float maxWindSpeed = 40f",
        r"windDirectionTransform\.localEulerAngles",
    ],
    "Perlin.cs": [
        r"t \* t \* t \* \(t \* \(t \* 6f - 15f\) \+ 10f\)",
        r"int num = hash & 0xF",
    ],
    "Parameter.cs": [r"GetValueWithModifierOverride"],
}


def check(root: Path) -> None:
    for name, markers in FILES.items():
        matches = list(root.rglob(name))
        if len(matches) != 1:
            raise AssertionError(f"{name}: expected one file, found {len(matches)}")
        text = matches[0].read_text(encoding="utf-8")
        for marker in markers:
            if not re.search(marker, text):
                raise AssertionError(f"{name}: missing {marker}")
        print(f"PASS {name}: {len(markers)} markers")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-source", type=Path, required=True)
    check(parser.parse_args().game_source)


if __name__ == "__main__":
    main()
