"""Read-only wind-source audit. Mirrors Perlin.cs in Python only for witnesses."""
import argparse
import math
from pathlib import Path
import random
import re


PERM = [151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225,
        140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10, 23, 190, 6, 148,
        247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117, 35, 11, 32,
        57, 177, 33, 88, 237, 149, 56, 87, 174, 20, 125, 136, 171, 168, 68, 175,
        74, 165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122,
        60, 211, 133, 230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102, 143, 54,
        65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18, 169,
        200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64,
        52, 217, 226, 250, 124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212,
        207, 206, 59, 227, 47, 16, 58, 17, 182, 189, 28, 42, 223, 183, 170, 213,
        119, 248, 152, 2, 44, 154, 163, 70, 221, 153, 101, 155, 167, 43, 172, 9,
        129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112, 104,
        218, 246, 97, 228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241,
        81, 51, 145, 235, 249, 14, 239, 107, 49, 192, 214, 31, 181, 199, 106, 157,
        184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236, 205, 93,
        222, 114, 67, 29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180,
        151]


def fade(t):
    return t * t * t * (t * (t * 6 - 15) + 10)


def grad3(h, x, y, z):
    h &= 15
    a = x if h < 8 else y
    b = y if h < 4 else (x if h in (12, 14) else z)
    return (-a if h & 1 else a) + (-b if h & 2 else b)


def noise3(x, y, z):
    xi, yi, zi = int(math.floor(x)) & 255, int(math.floor(y)) & 255, int(math.floor(z)) & 255
    x, y, z = x - math.floor(x), y - math.floor(y), z - math.floor(z)
    fx, fy, fz = fade(x), fade(y), fade(z)
    a, b = (PERM[xi] + yi) & 255, (PERM[xi + 1] + yi) & 255
    aa, ab = (PERM[a] + zi) & 255, (PERM[a + 1] + zi) & 255
    ba, bb = (PERM[b] + zi) & 255, (PERM[b + 1] + zi) & 255
    lerp = lambda t, p, q: p + t * (q - p)
    return lerp(fz, lerp(fy, lerp(fx, grad3(PERM[aa], x, y, z), grad3(PERM[ba], x - 1, y, z)),
                        lerp(fx, grad3(PERM[ab], x, y - 1, z), grad3(PERM[bb], x - 1, y - 1, z))),
                lerp(fy, lerp(fx, grad3(PERM[aa + 1], x, y, z - 1), grad3(PERM[ba + 1], x - 1, y, z - 1)),
                     lerp(fx, grad3(PERM[ab + 1], x, y - 1, z - 1), grad3(PERM[bb + 1], x - 1, y - 1, z - 1))))


def next_noise(coord, seed):
    # C# caller mutates the same Vector3 before each call:
    # coords.x += weatherSeed; then Perlin.Noise(coords).
    coord[0] += seed
    return noise3(coord[0], coord[1], coord[2]) * .5 + .5


def base(x, y, since, dyn):
    t = since / 1e7 * dyn
    return [t, x * .00015 + t, y * .00015 - t]


def forecast_angle(x, y, since, seed, dyn):
    coord = base(x, y, since, dyn)
    for _ in range(7):
        next_noise(coord, seed)
    return next_noise([a * .25 for a in coord], seed) * 360


def scene_angle(x, y, since, seed, dyn):
    coord = base(x, y, since, dyn)
    for _ in range(9):
        next_noise(coord, seed)
    return next_noise([a * .25 for a in coord], seed) * 360


def reduced_angle(x, y, since, seed, dyn):
    """Six unscaled shifts collapse exactly to x += 6*seed, then one pre-angle shift."""
    coord = base(x, y, since, dyn)
    next_noise(coord, seed)
    coord[0] += 6 * seed
    return next_noise([a * .25 for a in coord], seed) * 360


def dropped_angle(x, y, since, seed, dyn):
    """Naive two-draw shortcut: no intermediate shifts at all."""
    coord = base(x, y, since, dyn)
    next_noise(coord, seed)
    return next_noise([a * .25 for a in coord], seed) * 360


def scaled_seed_angle(x, y, since, seed, dyn):
    """Wrong collapse: seed added after coordinate scaling."""
    coord = base(x, y, since, dyn)
    next_noise(coord, seed)
    coord[0] += 6 * seed * .25
    return next_noise([a * .25 for a in coord], seed) * 360


def angle_error(a, b):
    d = abs(a - b) % 360
    return min(d, 360 - d)


def source_checks(root):
    expected = {
        "Weather.cs": ["dateTime.Ticks - gameTime.CampaignStartStoryTicks",
                       "gameTime.StoryTicksSinceCampaignStart",
                       "40f * windSpeedProbabilityCurve.Evaluate(NextPerlin(ref coords))",
                       "NextPerlin(ref coords2) * 360f", "coords.x += weatherSeed",
                       "windSpeedOverride > 0f", "private const float maxWindSpeed = 40f"],
        "Perlin.cs": ["t * t * t * (t * (t * 6f - 15f) + 10f)", "int num = hash & 0xF"],
        "Parameter.cs": ["GetValueWithModifierOverride"]}
    for name, markers in expected.items():
        found = list(root.rglob(name))
        assert len(found) == 1, name
        text = found[0].read_text(encoding="utf-8")
        for marker in markers:
            assert marker in text, (name, marker)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-source", type=Path, required=True)
    source_checks(parser.parse_args().game_source)
    rng = random.Random(4678)
    order, reduced, dropped, scaled, clock = 0, 0, 0, 0, 0
    max_order = max_reduced = max_dropped = max_scaled = max_clock = 0.
    for i in range(50_000):
        x, y = rng.uniform(-8000, 13000), rng.uniform(3000, 7500)
        since, seed = rng.randrange(0, 5 * 365 * 24 * 3600) * 10_000_000, rng.randrange(1, 1001)
        dyn = .0001 if i % 2 == 0 else .0003
        angle = forecast_angle(x, y, since, seed, dyn)
        for value, label in ((scene_angle(x, y, since, seed, dyn), "order"),
                             (reduced_angle(x, y, since, seed, dyn), "reduced"),
                             (dropped_angle(x, y, since, seed, dyn), "dropped"),
                             (scaled_seed_angle(x, y, since, seed, dyn), "scaled"),
                             (forecast_angle(x, y, since - 3600 * 10_000_000, seed, dyn), "clock")):
            gap = angle_error(value, angle)
            if label == "order":
                order += gap > .1
                max_order = max(max_order, gap)
            elif label == "reduced":
                reduced += gap > .1
                max_reduced = max(max_reduced, gap)
            elif label == "dropped":
                dropped += gap > .1
                max_dropped = max(max_dropped, gap)
            elif label == "scaled":
                scaled += gap > .1
                max_scaled = max(max_scaled, gap)
            else:
                clock += gap > .1
                max_clock = max(max_clock, gap)
    # The exact reduction differs only by float rounding, never semantically.
    assert reduced == 0 and max_reduced < .1, "exact_reduction_must_match"
    assert order > 40_000 and dropped > 40_000 and scaled > 40_000, "mutations_must_differ"
    print(f"PASS source markers; 50000 draws; order={order} max={max_order:.3f}; "
          f"exact_reduction={reduced} max={max_reduced:.3f}; naive_drop={dropped} "
          f"max={max_dropped:.3f}; scaled_seed={scaled} max={max_scaled:.3f}; "
          f"mismatched_epoch={clock} max={max_clock:.3f}")


if __name__ == "__main__":
    main()
