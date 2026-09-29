#!/usr/bin/env python3
"""Renders the app icon from the brand's tiles.

Reads assets/brand/keypaste-app-icon.svg and keypaste-favicon.svg, which
scripts/outline-brand-marks.py writes, and writes src/Keypaste.App/Assets/keypaste.ico (PNG payloads
at 16-256 px), keypaste-256.png, packaging/linux/com.keypaste.app.svg and packaging/macos/keypaste.icns
(PNG payloads at 128-1024 px). Sizes up to 16 px use the favicon cut, whose larger k keeps its serifs
apart that small. Needs Pillow.

    python scripts/render-app-icon.py
"""

import io
import re
import struct
import xml.etree.ElementTree as ET
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
BRAND = ROOT / "assets" / "brand"
ASSETS = ROOT / "src" / "Keypaste.App" / "Assets"
LINUX_SVG = ROOT / "packaging" / "linux" / "com.keypaste.app.svg"
MACOS_ICNS = ROOT / "packaging" / "macos" / "keypaste.icns"

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SUPERSAMPLE = 16
CURVE_STEPS = 24
ICNS_TYPES = {128: b"ic07", 256: b"ic08", 512: b"ic09", 1024: b"ic10"}
# Apple's icon grid draws the tile at 824 of 1024 px, so the Dock and Finder show it at the size of its neighbours.
MACOS_TILE = 824 / 1024
SVG = "{http://www.w3.org/2000/svg}"


def tile(name):
    """The tile's radius and fill, and each path's fill and contours, on its 64-unit grid."""
    root = ET.parse(BRAND / name).getroot()
    rect = root.find(f"{SVG}rect")
    paths = [(path.get("fill"), contours(path.get("d"))) for path in root.iter(f"{SVG}path")]
    return {"radius": float(rect.get("rx")), "fill": rect.get("fill"), "paths": paths}


def contours(d):
    """The path's closed contours as point lists, with each curve flattened."""
    tokens = re.findall(r"[MLHVQCZ]|-?[0-9.]+", d)
    arity = {"M": 2, "L": 2, "H": 1, "V": 1, "Q": 4, "C": 6, "Z": 0}
    shapes, points, index = [], [], 0
    while index < len(tokens):
        command = tokens[index]
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
        elif command == "Q":
            (cx, cy), (x, y) = values[0:2], values[2:4]
            points += [bezier((here, (cx, cy), (x, y)), step / CURVE_STEPS) for step in range(1, CURVE_STEPS + 1)]
        elif command == "C":
            controls = (here, tuple(values[0:2]), tuple(values[2:4]), tuple(values[4:6]))
            points += [bezier(controls, step / CURVE_STEPS) for step in range(1, CURVE_STEPS + 1)]
        else:
            shapes.append(points)
            points = []
    if points:
        shapes.append(points)
    return shapes


def bezier(controls, t):
    while len(controls) > 1:
        controls = [((1 - t) * a[0] + t * b[0], (1 - t) * a[1] + t * b[1]) for a, b in zip(controls, controls[1:])]
    return controls[0]


def render(design, size, supersample=SUPERSAMPLE):
    big = size * supersample
    unit = big / 64
    image = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    ImageDraw.Draw(image).rounded_rectangle((0, 0, big - 1, big - 1), radius=design["radius"] * unit, fill=design["fill"])
    for fill, shapes in design["paths"]:
        # Even-odd: each contour toggles coverage, so a counter inside a letter stays open.
        mask = Image.new("L", (big, big), 0)
        for shape in shapes:
            contour = Image.new("L", (big, big), 0)
            ImageDraw.Draw(contour).polygon([(x * unit, y * unit) for x, y in shape], fill=255)
            mask = ImageChops.logical_xor(mask.convert("1"), contour.convert("1")).convert("L")
        image.paste(Image.new("RGBA", (big, big), fill), (0, 0), mask)
    return image.resize((size, size), Image.Resampling.LANCZOS)


def png(image):
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


def ico(images):
    """An ICO container holding PNG payloads, which every Windows since Vista reads."""
    payloads = [png(image) for image in images]
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = b""
    for image, payload in zip(images, payloads):
        side = image.width if image.width < 256 else 0
        entries += struct.pack("<BBBBHHII", side, side, 0, 0, 1, 32, len(payload), offset)
        offset += len(payload)
    return header + entries + b"".join(payloads)


def macos(design, size):
    tile = round(size * MACOS_TILE)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.paste(render(design, tile, supersample=4), ((size - tile) // 2,) * 2)
    return canvas


def icns(images):
    """An ICNS container holding PNG payloads under the ic07-ic10 types, which macOS 10.7 and later read."""
    chunks = b"".join(
        ICNS_TYPES[image.width] + struct.pack(">I", 8 + len(payload)) + payload
        for image in images
        for payload in [png(image)]
    )
    return b"icns" + struct.pack(">I", 8 + len(chunks)) + chunks


def main():
    app_icon, favicon = tile("keypaste-app-icon.svg"), tile("keypaste-favicon.svg")
    images = [render(favicon if size <= 16 else app_icon, size) for size in SIZES]
    (ASSETS / "keypaste.ico").write_bytes(ico(images))
    images[-1].save(ASSETS / "keypaste-256.png", format="PNG", optimize=True)
    linux = (BRAND / "keypaste-app-icon.svg").read_text(encoding="utf-8").replace('width="1024" height="1024"', 'width="256" height="256"', 1)
    LINUX_SVG.write_text(linux, encoding="utf-8", newline="\n")
    MACOS_ICNS.parent.mkdir(exist_ok=True)
    MACOS_ICNS.write_bytes(icns([macos(app_icon, size) for size in ICNS_TYPES]))
    print(f"wrote keypaste.ico ({', '.join(map(str, SIZES))}), keypaste-256.png, {LINUX_SVG.name} and {MACOS_ICNS.name}")


if __name__ == "__main__":
    main()
