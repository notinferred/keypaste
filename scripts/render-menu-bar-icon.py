#!/usr/bin/env python3
"""Renders the macOS menu bar's template image from the one-colour icon.

Reads assets/brand/keypaste-glyph-mono.svg, which scripts/outline-brand-marks.py writes, and writes
src/Keypaste.App/Assets/keypaste-menu-bar.png: the "k." in black on transparent, centred on a square
canvas, which macOS tints for the menu bar's appearance because the tray icon is marked a template.
The glyph's outline is straight segments only, so it needs no library beyond the standard one.

    python3 scripts/render-menu-bar-icon.py
"""

import re
import struct
import xml.etree.ElementTree as ET
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "assets" / "brand" / "keypaste-glyph-mono.svg"
TARGET = ROOT / "src" / "Keypaste.App" / "Assets" / "keypaste-menu-bar.png"

SIZE = 64
# The glyph's height on the canvas, leaving the margin a menu bar icon keeps above and below.
GLYPH_HEIGHT = 52
SUPERSAMPLE = 16
SVG = "{http://www.w3.org/2000/svg}"


def contours(d):
    """The path's closed contours as point lists; only straight segments are expected."""
    tokens = re.findall(r"[A-Za-z]|-?[0-9.]+", d)
    arity = {"M": 2, "L": 2, "H": 1, "V": 1, "Z": 0}
    shapes, points, index = [], [], 0
    while index < len(tokens):
        command = tokens[index]
        if command not in arity:
            raise SystemExit(f"{SOURCE.name} has a '{command}' segment; this renderer draws straight segments only")
        values = [float(token) for token in tokens[index + 1:index + 1 + arity[command]]]
        index += 1 + arity[command]
        here = points[-1] if points else (0.0, 0.0)
        if command == "M":
            if points:
                shapes.append(points)
            points = [(values[0], values[1])]
        elif command == "L":
            points.append((values[0], values[1]))
        elif command == "H":
            points.append((values[0], here[1]))
        elif command == "V":
            points.append((here[0], values[0]))
        else:
            shapes.append(points)
            points = []
    if points:
        shapes.append(points)
    return shapes


def coverage(shapes, width, height):
    """Each pixel's covered share, from even-odd scanlines through SUPERSAMPLE sub-rows and sub-columns."""
    edges = [(a, b) for shape in shapes for a, b in zip(shape, shape[1:] + shape[:1]) if a[1] != b[1]]
    covered = [[0.0] * width for _ in range(height)]
    step = 1 / SUPERSAMPLE
    for row in range(height * SUPERSAMPLE):
        y = (row + 0.5) * step
        crossings = sorted(
            a[0] + (y - a[1]) * (b[0] - a[0]) / (b[1] - a[1])
            for a, b in edges
            if min(a[1], b[1]) <= y < max(a[1], b[1]))
        for start, end in zip(crossings[0::2], crossings[1::2]):
            first = max(0, int(start / step + 0.5))
            last = min(width * SUPERSAMPLE, int(end / step + 0.5))
            for column in range(first, last):
                covered[row // SUPERSAMPLE][column // SUPERSAMPLE] += 1 / (SUPERSAMPLE * SUPERSAMPLE)
    return covered


def png(covered):
    """An 8-bit RGBA PNG, black with the coverage as alpha."""
    raw = b"".join(
        b"\x00" + b"".join(struct.pack("BBBB", 0, 0, 0, min(255, round(share * 255))) for share in row)
        for row in covered)

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    header = struct.pack(">IIBBBBB", len(covered[0]), len(covered), 8, 6, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def main():
    root = ET.parse(SOURCE).getroot()
    _, _, width, height = (float(value) for value in root.get("viewBox").split())
    scale = GLYPH_HEIGHT / height
    left, top = (SIZE - width * scale) / 2, (SIZE - GLYPH_HEIGHT) / 2
    shapes = [
        [(left + x * scale, top + y * scale) for x, y in shape]
        for path in root.iter(f"{SVG}path")
        for shape in contours(path.get("d"))]
    TARGET.write_bytes(png(coverage(shapes, SIZE, SIZE)))
    print(f"wrote {TARGET.relative_to(ROOT)} ({SIZE} px, the glyph {GLYPH_HEIGHT} px high)")


if __name__ == "__main__":
    main()
