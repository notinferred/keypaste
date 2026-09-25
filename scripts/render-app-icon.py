#!/usr/bin/env python3
"""Renders the app icon from the brand geometry.

Writes src/Keypaste.App/Assets/keypaste.ico (PNG payloads at 16-256 px), keypaste-256.png and
packaging/linux/com.keypaste.app.svg. Sizes up to 16 px use the favicon cut (amber tile, ink
glyph), because the app icon's thin stem does not survive that small. Needs Pillow.

    python scripts/render-app-icon.py
"""

import io
import struct
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "Keypaste.App" / "Assets"
LINUX_SVG = ROOT / "packaging" / "linux" / "com.keypaste.app.svg"

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SUPERSAMPLE = 16

# docs/design/assets, on a 64-unit grid.
APP_ICON = {
    "tile": "#1D1E22",
    "stem": ((16, 14, 7, 36), "#F2F2F0"),
    "arm": ([(38, 26), (48, 26), (36.5, 38), (48, 50), (38, 50), (26.5, 38)], "#F2B544"),
}
FAVICON = {
    "tile": "#F2B544",
    "stem": ((14, 12, 10, 40), "#111214"),
    "arm": ([(36, 24), (50, 24), (36, 38), (50, 52), (36, 52), (28, 38)], "#111214"),
}


def render(design, size):
    big = size * SUPERSAMPLE
    unit = big / 64
    image = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((0, 0, big - 1, big - 1), radius=15 * unit, fill=design["tile"])
    (x, y, w, h), stem = design["stem"]
    draw.rectangle((x * unit, y * unit, (x + w) * unit - 1, (y + h) * unit - 1), fill=stem)
    points, arm = design["arm"]
    draw.polygon([(px * unit, py * unit) for px, py in points], fill=arm)
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


def svg(design):
    (x, y, w, h), stem = design["stem"]
    points, arm = design["arm"]
    polygon = " ".join(f"{px:g},{py:g}" for px, py in points)
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 64 64">\n'
        f'  <rect width="64" height="64" rx="15" fill="{design["tile"]}"/>\n'
        f'  <rect x="{x}" y="{y}" width="{w}" height="{h}" fill="{stem}"/>\n'
        f'  <polygon points="{polygon}" fill="{arm}"/>\n'
        "</svg>\n"
    )


def main():
    images = [render(FAVICON if size <= 16 else APP_ICON, size) for size in SIZES]
    (ASSETS / "keypaste.ico").write_bytes(ico(images))
    images[-1].save(ASSETS / "keypaste-256.png", format="PNG", optimize=True)
    LINUX_SVG.write_text(svg(APP_ICON), encoding="utf-8", newline="\n")
    print(f"wrote keypaste.ico ({', '.join(map(str, SIZES))}), keypaste-256.png and {LINUX_SVG.name}")


if __name__ == "__main__":
    main()
