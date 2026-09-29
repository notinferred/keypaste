#!/usr/bin/env python3
"""Outlines the keypaste marks from Hepta Slab and writes every file drawn from them.

The icon is "k." and the wordmark "keypaste.", Hepta Slab SemiBold (600) tracked -1%, each with
an amber dot; they are never set side by side (docs/BRAND.md). Outlines are the source of truth,
so no font is vendored: this writes

    assets/brand/ and docs/design/assets/   keypaste-glyph-{dark,light,mono}.svg,
                                             keypaste-wordmark-{dark,light,mono}.svg,
                                             keypaste-app-icon.svg, keypaste-favicon.svg
    site/public/favicon.svg                  the favicon cut
    src/Keypaste.App/Theme/BrandOutlines.axaml  the geometry BrandMark and BrandWordmark draw

and scripts/render-app-icon.py rasterizes the two tiles. It reads the variable font from
google/fonts, ofl/heptaslab/HeptaSlab[wght].ttf (SIL Open Font License 1.1), and refuses any other
bytes, so the same file always gives the same outlines. Needs fontTools and uharfbuzz.

    python scripts/outline-brand-marks.py <path to HeptaSlab[wght].ttf>
"""

import hashlib
import io
import sys
from pathlib import Path

import uharfbuzz as hb
from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont
from fontTools.ttLib.removeOverlaps import removeOverlaps
from fontTools.varLib.instancer import instantiateVariableFont

ROOT = Path(__file__).resolve().parent.parent
FONT_SHA256 = "737badc7944d5b6f92c05919995240d6ea70e86bebe45d196e61677e7a901f29"
WEIGHT = 600
TRACKING = -0.01
UNITS_PER_EM = 100

INK_DARK, INK_LIGHT, AMBER, TILE = "#F2F2F0", "#111214", "#F2B544", "#1D1E22"
# The glyph's share of a 64-unit tile: the app icon's, and the favicon cut's, larger so its serifs survive 16 px.
APP_ICON = {"height": 0.46, "radius": 15}
FAVICON = {"height": 0.66, "radius": 12}
MAX_WIDTH = 0.62


def number(value):
    text = f"{value:.2f}".rstrip("0").rstrip(".")
    return "0" if text == "-0" else text


def instance(path):
    data = Path(path).read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    if digest != FONT_SHA256:
        sys.exit(f"{path} is not the recorded HeptaSlab[wght].ttf (sha256 {digest})")
    font = instantiateVariableFont(TTFont(io.BytesIO(data)), {"wght": WEIGHT}, inplace=False)
    removeOverlaps(font)
    buffer = io.BytesIO()
    font.save(buffer)
    return TTFont(io.BytesIO(buffer.getvalue())), buffer.getvalue()


def outline(font, data, text):
    """The ink and the dot of text, in units of 1/100 em with the ink box's top-left at the origin."""
    upm = font["head"].unitsPerEm
    scale = UNITS_PER_EM / upm
    glyphs = font.getGlyphSet()
    order = font.getGlyphOrder()

    buffer = hb.Buffer()
    buffer.add_str(text)
    buffer.guess_segment_properties()
    hb.shape(hb.Font(hb.Face(data)), buffer, {"kern": True, "liga": False})

    placed, x = [], 0.0
    for index, (info, position) in enumerate(zip(buffer.glyph_infos, buffer.glyph_positions)):
        placed.append((text[info.cluster], order[info.codepoint], x + position.x_offset, position.y_offset))
        x += position.x_advance + (TRACKING * upm if index < len(buffer.glyph_infos) - 1 else 0)

    bounds = BoundsPen(glyphs)
    for _, name, dx, dy in placed:
        glyphs[name].draw(TransformPen(bounds, (1, 0, 0, -1, dx, -dy)))
    left, top, right, bottom = bounds.bounds

    def path(chars):
        pen = SVGPathPen(glyphs, ntos=number)
        for char, name, dx, dy in placed:
            if char in chars:
                glyphs[name].draw(TransformPen(pen, (scale, 0, 0, -scale, (dx - left) * scale, (-dy - top) * scale)))
        return pen.getCommands()

    ink = path(set(text) - {"."})
    dot = path({"."})
    return {"ink": ink, "dot": dot, "width": (right - left) * scale, "height": (bottom - top) * scale}


def placed_in_tile(font, data, text, cut):
    """The outline moved and scaled into a 64-unit tile, centred, as the app icon and favicon draw it."""
    mark = outline(font, data, text)
    aspect = mark["width"] / mark["height"]
    height = 64 * cut["height"]
    width = height * aspect
    if width > 64 * MAX_WIDTH:
        width = 64 * MAX_WIDTH
        height = width / aspect
    scale = height / mark["height"]
    x, y = (64 - width) / 2, (64 - height) / 2

    def moved(d):
        pen = SVGPathPen(None, ntos=number)
        transform = TransformPen(pen, (scale, 0, 0, scale, x, y))
        replay(d, transform)
        return pen.getCommands()

    return moved(mark["ink"]), moved(mark["dot"])


def replay(d, pen):
    """Draws an absolute path, as SVGPathPen writes one, into another pen."""
    for letter in "MLHVQCZ":
        d = d.replace(letter, f" {letter} ")
    tokens = d.split()
    arity = {"M": 2, "L": 2, "H": 1, "V": 1, "Q": 4, "C": 6, "Z": 0}
    index, current, is_open = 0, (0.0, 0.0), False
    while index < len(tokens):
        command = tokens[index]
        values = [float(token) for token in tokens[index + 1:index + 1 + arity[command]]]
        index += 1 + arity[command]
        if command == "Z":
            pen.closePath()
            is_open = False
            continue
        if command == "H":
            points = [(values[0], current[1])]
        elif command == "V":
            points = [(current[0], values[0])]
        else:
            points = list(zip(values[0::2], values[1::2]))
        if command == "M":
            if is_open:
                pen.closePath()
            pen.moveTo(points[0])
            is_open = True
        elif command in "LHV":
            pen.lineTo(points[0])
        elif command == "Q":
            pen.qCurveTo(*points)
        else:
            pen.curveTo(*points)
        current = points[-1]


def svg(width, height, body, size=None):
    w, h = size or (number(width), number(height))
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" '
        f'viewBox="0 0 {number(width)} {number(height)}" role="img" aria-label="keypaste">\n{body}</svg>\n'
    )


def marks(font, data):
    files = {}
    for name, text in (("glyph", "k."), ("wordmark", "keypaste.")):
        mark = outline(font, data, text)
        for variant, ink, dot in (("dark", INK_DARK, AMBER), ("light", INK_LIGHT, AMBER), ("mono", "currentColor", "currentColor")):
            body = f'  <path fill="{ink}" d="{mark["ink"]}"/>\n  <path fill="{dot}" d="{mark["dot"]}"/>\n'
            files[f"keypaste-{name}-{variant}.svg"] = svg(mark["width"], mark["height"], body)

    for name, cut, size in (("keypaste-app-icon.svg", APP_ICON, 1024), ("keypaste-favicon.svg", FAVICON, 32)):
        ink, dot = placed_in_tile(font, data, "k.", cut)
        body = (
            f'  <rect width="64" height="64" rx="{cut["radius"]}" fill="{TILE}"/>\n'
            f'  <path fill="{INK_DARK}" d="{ink}"/>\n  <path fill="{AMBER}" d="{dot}"/>\n'
        )
        files[name] = svg(64, 64, body, size=(size, size))
    return files


def axaml(font, data):
    glyph, word = outline(font, data, "k."), outline(font, data, "keypaste.")
    return (
        '<ResourceDictionary xmlns="https://github.com/avaloniaui"\n'
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n\n'
        "  <!-- Written by scripts/outline-brand-marks.py from Hepta Slab SemiBold; regenerate rather than edit.\n"
        "       Units are 1/100 em with each mark's top-left at the origin. -->\n"
        f'  <StreamGeometry x:Key="KpGlyphInkGeometry">{glyph["ink"]}</StreamGeometry>\n'
        f'  <StreamGeometry x:Key="KpGlyphDotGeometry">{glyph["dot"]}</StreamGeometry>\n'
        f'  <StreamGeometry x:Key="KpWordmarkInkGeometry">{word["ink"]}</StreamGeometry>\n'
        f'  <StreamGeometry x:Key="KpWordmarkDotGeometry">{word["dot"]}</StreamGeometry>\n\n'
        "</ResourceDictionary>\n"
    )


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    font, data = instance(sys.argv[1])
    files = marks(font, data)
    for folder in (ROOT / "assets" / "brand", ROOT / "docs" / "design" / "assets"):
        for name, text in files.items():
            (folder / name).write_text(text, encoding="utf-8", newline="\n")
    (ROOT / "site" / "public" / "favicon.svg").write_text(files["keypaste-favicon.svg"], encoding="utf-8", newline="\n")
    (ROOT / "src" / "Keypaste.App" / "Theme" / "BrandOutlines.axaml").write_text(axaml(font, data), encoding="utf-8", newline="\n")
    print(f"wrote {len(files)} marks to assets/brand and docs/design/assets, the site favicon and BrandOutlines.axaml")


if __name__ == "__main__":
    main()
