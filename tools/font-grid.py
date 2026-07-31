#!/usr/bin/env python3
"""Report a font's design pixel grid, i.e. the only font_size values it renders crisply at.

docs/ART_SPEC.md section 7 requires bitmap/pixel faces to be rendered at their design
size or an exact integer multiple of it. Godot's `font_size` sets the *em* in pixels, so
the legal sizes are the integer multiples of

    design em = unitsPerEm / (authoring grid unit, in font units)

A pixel face draws every stem and counter on that grid, so dividing the em by it gives
the em measured in design pixels.

The grid unit is not simply the gcd of the outline coordinates. Silkscreen Bold, for
example, draws on a 125-unit grid but nudges a handful of points by 5 units to thicken
the bold weight, which drags the gcd down to 5 and reports a nonsense 200px em. So this
takes the *largest* divisor of the em that accounts for at least COVERAGE of the
coordinates, which ignores a few outliers without ever inventing a grid that isn't there.

This exists because Jersey 15 was shipped at font_size 16 while its design em is 27,
i.e. 0.59 device pixels per design pixel. The rasterizer had to drop ~40% of the stems,
which mangled the letterforms badly enough that "Deal 6 damage" read as "Deal 8 damage".
Nothing caught it: the name "Jersey 15" refers to the face's *cap height* in design
pixels, not its em, and ART_SPEC recorded that 15 as the design size.

Deliberately parses head/loca/glyf/cmap by hand rather than depending on fontTools, so
it runs on a bare Python install like the rest of tools/.

Usage:  tools/font-grid.py assets/fonts/*.ttf
"""

import struct
import sys

# Enough of the alphabet, digits, ascenders, descenders and round shapes to be sure the
# grid reflects the real authoring grid rather than one unusually simple glyph.
SAMPLE = "HAMBGOQnexpqy0123456789"

# Fraction of outline coordinates that must land on a candidate grid for it to count.
# Below 1.0 so a few bold-weight overshoots can't collapse the estimate; high enough
# that a genuinely off-grid face can't sneak through.
COVERAGE = 0.9


def _tables(data):
    count = struct.unpack(">H", data[4:6])[0]
    out = {}
    for i in range(count):
        rec = 12 + 16 * i
        tag = data[rec : rec + 4].decode("latin1")
        offset, length = struct.unpack(">II", data[rec + 8 : rec + 16])
        out[tag] = (offset, length)
    return out


def _cmap_format4(data, tables):
    """Offset of the last format-4 cmap subtable, which is the Unicode BMP one."""
    base, _ = tables["cmap"]
    count = struct.unpack(">H", data[base + 2 : base + 4])[0]
    chosen = None
    for i in range(count):
        rec = base + 4 + 8 * i
        sub = base + struct.unpack(">I", data[rec + 4 : rec + 8])[0]
        if struct.unpack(">H", data[sub : sub + 2])[0] == 4:
            chosen = sub
    if chosen is None:
        raise ValueError("no format-4 cmap subtable")
    return chosen


def _glyph_id(data, sub, char):
    seg_x2 = struct.unpack(">H", data[sub + 6 : sub + 8])[0]
    segs = seg_x2 // 2
    ends = sub + 14
    starts = ends + seg_x2 + 2
    deltas = starts + seg_x2
    ranges = deltas + seg_x2
    code = ord(char)
    for i in range(segs):
        end = struct.unpack(">H", data[ends + 2 * i : ends + 2 + 2 * i])[0]
        start = struct.unpack(">H", data[starts + 2 * i : starts + 2 + 2 * i])[0]
        if not start <= code <= end:
            continue
        delta = struct.unpack(">h", data[deltas + 2 * i : deltas + 2 + 2 * i])[0]
        range_offset = struct.unpack(">H", data[ranges + 2 * i : ranges + 2 + 2 * i])[0]
        if range_offset == 0:
            return (code + delta) & 0xFFFF
        at = ranges + 2 * i + range_offset + 2 * (code - start)
        glyph = struct.unpack(">H", data[at : at + 2])[0]
        return (glyph + delta) & 0xFFFF if glyph else 0
    return 0


def _glyph_range(data, tables, long_format, glyph_id):
    loca, _ = tables["loca"]
    if long_format:
        at = loca + 4 * glyph_id
        return struct.unpack(">II", data[at : at + 8])
    at = loca + 2 * glyph_id
    start, end = struct.unpack(">HH", data[at : at + 4])
    return start * 2, end * 2


def _outline_coords(data, offset):
    """Every on/off-curve x and y of a simple glyph, in font units."""
    contours = struct.unpack(">h", data[offset : offset + 2])[0]
    if contours < 0:  # composite glyph; its parts are measured on their own
        return []
    at = offset + 10
    ends = [struct.unpack(">H", data[at + 2 * i : at + 2 + 2 * i])[0] for i in range(contours)]
    at += 2 * contours
    at += 2 + struct.unpack(">H", data[at : at + 2])[0]  # skip instructions

    points = ends[-1] + 1 if ends else 0
    flags = []
    while len(flags) < points:
        flag = data[at]
        at += 1
        flags.append(flag)
        if flag & 8:  # repeat
            run = data[at]
            at += 1
            flags.extend([flag] * run)
    flags = flags[:points]

    coords = []
    for short_bit, same_bit in ((2, 16), (4, 32)):  # x pass, then y pass
        value = 0
        axis = []
        for flag in flags:
            if flag & short_bit:
                delta = data[at]
                at += 1
                value += delta if flag & same_bit else -delta
            elif not flag & same_bit:
                value += struct.unpack(">h", data[at : at + 2])[0]
                at += 2
            axis.append(value)
        coords.extend(axis)
    return coords


def grid(path):
    data = open(path, "rb").read()
    tables = _tables(data)
    head, _ = tables["head"]
    upem = struct.unpack(">H", data[head + 18 : head + 20])[0]
    long_format = struct.unpack(">h", data[head + 50 : head + 52])[0] == 1
    glyf, _ = tables["glyf"]
    cmap = _cmap_format4(data, tables)

    # Every coordinate, with duplicates kept: coverage has to be weighted by how often
    # a value actually occurs. Deduplicating first would give a handful of rare bold
    # overshoots the same weight as the grid they deviate from, and Silkscreen Bold's
    # real 125-unit grid would score 64% instead of 97%.
    values = []
    for char in SAMPLE:
        glyph_id = _glyph_id(data, cmap, char)
        if not glyph_id:
            continue
        start, end = _glyph_range(data, tables, long_format, glyph_id)
        if start == end:  # empty glyph, e.g. space
            continue
        values.extend(_outline_coords(data, glyf + start))

    values = [abs(v) for v in values if v]
    if not values:
        raise ValueError(f"{path}: no outline coordinates found")

    # Candidates are the divisors of the em, largest first - a grid that doesn't divide
    # the em evenly isn't a pixel grid. The first one that covers enough of the outline
    # is the authoring grid; 1 always matches, and means "not a pixel face".
    for unit in sorted((d for d in range(1, upem + 1) if upem % d == 0), reverse=True):
        hits = sum(1 for v in values if v % unit == 0)
        if hits >= COVERAGE * len(values):
            return upem, unit, upem // unit, hits / len(values)
    raise AssertionError("unreachable: unit 1 always covers")


def main(paths):
    if not paths:
        print(__doc__.strip().splitlines()[-1])
        return 1
    for path in paths:
        upem, unit, em_px, coverage = grid(path)
        name = path.rsplit("/", 1)[-1]
        print(f"{name}")
        print(f"  unitsPerEm       {upem}")
        print(f"  grid unit        {unit} font units ({coverage:.0%} of coordinates on grid)")
        if unit == 1:
            print("  design em        none - this is not a pixel face, any size resamples")
            continue
        legal = ", ".join(str(em_px * n) for n in (1, 2, 3))
        print(f"  design em        {em_px} px   <- legal font_size values: {legal}, ...")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
