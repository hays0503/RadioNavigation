"""Synthetic coastal support compiler and lookup; no real shoreline or FPS benchmark."""
from dataclasses import dataclass
from enum import IntEnum
import json
import math


class Support(IntEnum):
    VALID = 0
    LAND = 1
    BELOW = 2
    UNSUPPORTED = 3
    UNAVAILABLE = 4
    CORRUPT = 5


@dataclass(frozen=True)
class Footprint:
    # Bounds on water depth anywhere in the footprint, not a centre sample.
    least_depth: float
    most_depth: float


def depth_bounds(cell, base, shoal, depth):
    """Conservative bounds for a rectangular shoal inside an otherwise flat sea."""
    x0, y0, x1, y1 = cell
    sx0, sy0, sx1, sy1 = shoal
    if x1 < sx0 or sx1 < x0 or y1 < sy0 or sy1 < y0:
        return Footprint(base, base)
    covered = sx0 <= x0 <= x1 <= sx1 and sy0 <= y0 <= y1 <= sy1
    return Footprint(min(base, depth), min(base, depth) if covered else base)


def geometry(footprint, shallow, deep):
    if footprint.most_depth <= 0:
        return Support.LAND
    if footprint.least_depth <= 0:
        return Support.UNSUPPORTED
    if footprint.most_depth <= shallow:
        return Support.BELOW
    if footprint.least_depth <= deep:
        return Support.UNSUPPORTED
    return Support.VALID


def compile_support(game_footprints, shallow, deep, all_components_valid, source_footprints=None):
    """Conservative whole-stencil rule for one vertical bracket on a common grid."""
    target = tuple(geometry(f, shallow, deep) for f in game_footprints)
    source = game_footprints if source_footprints is None else source_footprints
    # The fixture footprints tile the entire stencil, in both maps.
    stencil_ok = (all_components_valid and all(s == Support.VALID for s in target)
                  and all(geometry(f, shallow, deep) == Support.VALID for f in source))
    if not stencil_ok:
        target = tuple(Support.UNSUPPORTED if s == Support.VALID else s for s in target)
    packed = bytearray((len(target) + 1) // 2)
    for i, state in enumerate(target):
        packed[i // 2] |= int(state) << (4 * (i % 2))
    return packed


def unpack_support(packed, i):
    return Support((packed[i // 2] >> (4 * (i % 2))) & 15)


class SupportIndex:
    """Candidate layout: one descriptor per record, only mixed records use payload."""

    def __init__(self, records):
        self.descriptors = []
        self.detail = bytearray()
        for states in records:
            first = unpack_support(states, 0)
            if all(unpack_support(states, i) == first for i in range(64)):
                self.descriptors.append(int(first))
            else:
                self.descriptors.append((len(self.detail) // 32 << 4) | 15)
                self.detail.extend(states)

    def sample(self, record, subcell):
        descriptor = self.descriptors[record]
        if descriptor & 15 != 15:
            return Support(descriptor)
        index = (descriptor >> 4) * 64 + subcell
        return unpack_support(self.detail, index)


def select(packed, i, readiness=Support.VALID):
    # Static support decides which profile is applicable. Readiness governs
    # whether that profile may run; it does not select a different profile.
    static = unpack_support(packed, i)
    return readiness if static == Support.VALID else static


def field(packed, i, readiness, vector):
    state = select(packed, i, readiness)
    return state, vector if state == Support.VALID else None


def intersects_closed_box(a, b, box):
    """Offline witness for a swept straight chord, not a game event locator."""
    lo, hi = 0.0, 1.0
    for p, q, left, right in zip(a, b, box[:2], box[2:]):
        d = q - p
        if d == 0:
            if not left <= p <= right:
                return False
            continue
        entry, exit_ = sorted(((left - p) / d, (right - p) / d))
        lo, hi = max(lo, entry), min(hi, exit_)
        if hi < lo:
            return False
    return True


def main():
    passed = []

    def check(name, ok):
        if not ok:
            raise AssertionError(name)
        passed.append(name)

    ocean = [Footprint(100, 100)] * 16
    open_bracket = compile_support(ocean, 0, 20, True)
    check("uniform_ocean_valid_through_bracket", len(open_bracket) == 8
          and all(unpack_support(open_bracket, i) == Support.VALID for i in range(16)))

    island = ocean.copy()
    island[5] = Footprint(0, 0)
    check("island_fixture_keeps_all_four_coarse_corners_wet",
          all(island[i].least_depth > 20 for i in (0, 3, 12, 15)))
    island_mask = compile_support(island, 0, 20, True, ocean)
    check("hidden_island_rejects_coarse_stencil_even_at_wet_corner",
          unpack_support(island_mask, 5) == Support.LAND
          and unpack_support(island_mask, 0) == Support.UNSUPPORTED)
    fine_mask = compile_support(island[:1], 0, 20, True)
    check("native_fine_stencil_can_recover_support_without_filling_island",
          unpack_support(fine_mask, 0) == Support.VALID
          and unpack_support(island_mask, 5) == Support.LAND)
    check("source_land_is_not_game_land_but_invalidates_stencil",
          unpack_support(compile_support(ocean, 0, 20, True, island), 5) == Support.UNSUPPORTED
          and unpack_support(compile_support(island, 0, 20, True, ocean), 5) == Support.LAND)

    ridge = ocean.copy()
    for i in (1, 5, 9, 13):
        ridge[i] = Footprint(12, 12)
    top = compile_support(ridge, 0, 10, True)
    below = compile_support(ridge, 20, 30, True)
    through = compile_support(ridge, 10, 20, True)
    check("same_strait_can_be_connected_above_and_unusable_below",
          unpack_support(top, 1) == Support.VALID
          and unpack_support(below, 1) == Support.BELOW
          and unpack_support(below, 0) == Support.UNSUPPORTED)
    check("depth_bracket_must_be_wet_from_top_to_bottom",
          unpack_support(through, 1) == Support.UNSUPPORTED)
    check("mixed_land_water_is_unsupported_not_definitely_land",
          geometry(Footprint(0, 100), 0, 20) == Support.UNSUPPORTED)
    check("bottom_contact_is_not_wet", geometry(Footprint(20, 20), 20, 20) == Support.BELOW)
    shoal = (0.02, 0.02, 0.04, 0.04)
    check("footprint_overlap_not_center_point_detects_subpixel_island",
          geometry(depth_bounds((0, 0, 1, 1), 100, shoal, 0), 0, 20) == Support.UNSUPPORTED
          and geometry(depth_bounds((.5, .5, .5, .5), 100, shoal, 0), 0, 20) == Support.VALID)

    missing_v = compile_support(ocean, 0, 20, False)
    check("either_missing_velocity_component_rejects_vector",
          field(missing_v, 0, Support.VALID, (2, 0)) == (Support.UNSUPPORTED, None))
    check("valid_zero_is_not_unavailable_or_corrupt",
          field(open_bracket, 0, Support.VALID, (0, 0)) == (Support.VALID, (0, 0))
          and field(open_bracket, 0, Support.UNAVAILABLE, (0, 0)) == (Support.UNAVAILABLE, None)
          and field(open_bracket, 0, Support.CORRUPT, (0, 0)) == (Support.CORRUPT, None))
    check("static_unsupported_does_not_depend_on_loading",
          all(select(island_mask, 0, state) == Support.UNSUPPORTED
              for state in (Support.VALID, Support.UNAVAILABLE, Support.CORRUPT)))

    a, b = (0.0, 0.5), (1.0, 0.5)
    cut = (0.499, 0.49, 0.501, 0.51)
    check("valid_endpoints_and_sparse_probes_can_miss_thin_land",
          intersects_closed_box(a, b, cut)
          and not any(intersects_closed_box((x, .5), (x, .5), cut) for x in (0, .25, .75, 1)))
    check("closed_sweep_rejects_grazing_and_handles_reverse",
          intersects_closed_box((0, .49), (1, .49), cut)
          and intersects_closed_box(b, a, cut)
          and not intersects_closed_box((0, .48), (1, .48), cut))

    wet64 = [Footprint(100, 100)] * 64
    first = wet64.copy()
    second = wet64.copy()
    first[0] = Footprint(0, 0)
    second[-1] = Footprint(0, 0)
    masks = [compile_support(r, 0, 20, True) for r in (first, wet64, second)]
    index = SupportIndex(masks)
    check("mixed_descriptors_address_independent_fixed_width_payloads",
          index.descriptors == [15, 0, 31] and len(index.detail) == 64
          and all(index.sample(i, j) == unpack_support(masks[i], j)
                  for i in range(3) for j in range(64)))

    # Candidate byte accounting: uint32 descriptors and packed 8x8 nibble payloads.
    # Not the memory footprint of the Python objects.
    cells, brackets, fraction = 100_000, 6, .05
    mixed = math.floor(cells * brackets * fraction)
    print(json.dumps(dict(scope=__doc__, passed=len(passed), checks=passed,
                          layout_cells=cells, layout_brackets=brackets, layout_mixed=mixed,
                          candidate_bytes=cells * brackets * 4 + mixed * 32,
                          dense_bytes=cells * brackets * 32), indent=2))


if __name__ == "__main__":
    main()
