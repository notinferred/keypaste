#!/usr/bin/env python3
"""Converts Lucide SVG icons into src/Keypaste.App/Theme/Icons.axaml.

Each icon becomes a StreamGeometry keyed Icon.<name> on Lucide's 24-unit grid. The geometry is an
outline to be stroked, never filled: draw it with Controls/KpIcon.

    python scripts/lucide-to-axaml.py            # regenerate from ICONS below
    python scripts/lucide-to-axaml.py name ...   # regenerate with extra icons added
"""

import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "third_party" / "lucide" / "icons"
TARGET = ROOT / "src" / "Keypaste.App" / "Theme" / "Icons.axaml"

ICONS = """
activity alert-triangle arrow-right bot bug check chevron-down chevron-right chevrons-up-down
circle-check circle-dot clipboard-check clock copy credit-card database download ellipsis
external-link eye eye-off file-code file-down file-input file-key file-lock-2 file-up filter
fingerprint folder folder-git-2 folder-input folder-open folder-pen folder-plus globe history
hourglass import info key key-round key-square layers link lock lock-open log-out moon
more-horizontal panel-left pencil plug plus refresh-cw rotate-cw search server settings share-2
shield shield-check sticky-note sun terminal ticket trash-2 unlock upload usb user vault wand-2 x
""".split()

# Names the design uses that this Lucide release publishes under another name.
ALIASES = {
    "alert-triangle": "triangle-alert",
    "more-horizontal": "ellipsis",
    "unlock": "lock-open",
    "wand-2": "wand-sparkles",
}

NUMBER = re.compile(r"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")
ARGS = {"m": 2, "l": 2, "h": 1, "v": 1, "c": 6, "s": 4, "q": 4, "t": 2, "a": 7, "z": 0}


def fmt(value):
    text = f"{value:.4f}".rstrip("0").rstrip(".")
    return "0" if text in ("", "-0") else text


def path_tokens(data):
    """Splits SVG path data, including arc flags written without separators ("a1 1 0 011 1")."""
    i, command, out = 0, None, []
    while i < len(data):
        ch = data[i]
        if ch.isspace() or ch == ",":
            i += 1
            continue
        if ch.isalpha():
            command = ch
            out.append(ch)
            i += 1
            count = 0
            continue
        if command is None:
            raise ValueError(f"number before a command in {data!r}")
        if command in "Mm" and count == 2:
            # Pairs after a moveto are line-tos; say so rather than rely on the parser knowing.
            command = "L" if command == "M" else "l"
            out.append(command)
            count = 0
        position = count % ARGS[command.lower()] if ARGS[command.lower()] else 0
        if command.lower() == "a" and position in (3, 4):
            out.append(ch)
            i += 1
        else:
            match = NUMBER.match(data, i)
            if not match:
                raise ValueError(f"cannot read {data[i:i + 12]!r}")
            out.append(fmt(float(match.group())))
            i = match.end()
        count += 1
    # Each <path> starts at the origin, but concatenated figures would not, so a leading relative
    # moveto is made absolute and the pairs implied after it stay relative line-tos.
    if out and out[0] == "m":
        out[0] = "M"
    return " ".join(out)


def rect(x, y, w, h, rx, ry):
    if rx == 0 and ry == 0:
        return f"M{fmt(x)} {fmt(y)} H{fmt(x + w)} V{fmt(y + h)} H{fmt(x)} Z"
    rx, ry = min(rx or ry, w / 2), min(ry or rx, h / 2)
    arc = f"A{fmt(rx)} {fmt(ry)} 0 0 1"
    return (
        f"M{fmt(x + rx)} {fmt(y)} H{fmt(x + w - rx)} {arc} {fmt(x + w)} {fmt(y + ry)} "
        f"V{fmt(y + h - ry)} {arc} {fmt(x + w - rx)} {fmt(y + h)} H{fmt(x + rx)} "
        f"{arc} {fmt(x)} {fmt(y + h - ry)} V{fmt(y + ry)} {arc} {fmt(x + rx)} {fmt(y)} Z"
    )


def ellipse(cx, cy, rx, ry):
    arc = f"A{fmt(rx)} {fmt(ry)} 0 1 0"
    return f"M{fmt(cx - rx)} {fmt(cy)} {arc} {fmt(cx + rx)} {fmt(cy)} {arc} {fmt(cx - rx)} {fmt(cy)} Z"


def points(text, close):
    values = [fmt(float(n)) for n in NUMBER.findall(text)]
    pairs = [f"{values[i]} {values[i + 1]}" for i in range(0, len(values), 2)]
    return "M" + pairs[0] + "".join(f" L{p}" for p in pairs[1:]) + (" Z" if close else "")


def figure(element):
    tag = element.tag.split("}")[-1]
    a = {k: v for k, v in element.attrib.items()}
    n = lambda key, default=0.0: float(a.get(key, default))
    if tag == "path":
        return path_tokens(a["d"])
    if tag == "circle":
        return ellipse(n("cx"), n("cy"), n("r"), n("r"))
    if tag == "ellipse":
        return ellipse(n("cx"), n("cy"), n("rx"), n("ry"))
    if tag == "rect":
        return rect(n("x"), n("y"), n("width"), n("height"), n("rx"), n("ry"))
    if tag == "line":
        return f"M{fmt(n('x1'))} {fmt(n('y1'))} L{fmt(n('x2'))} {fmt(n('y2'))}"
    if tag in ("polyline", "polygon"):
        return points(a["points"], close=tag == "polygon")
    raise ValueError(f"unsupported element <{tag}>")


def geometry(name):
    source = SOURCE / f"{ALIASES.get(name, name)}.svg"
    svg = ET.fromstring(source.read_text(encoding="utf-8"))
    return " ".join(figure(child) for child in svg)


def main():
    names = sorted(set(ICONS) | set(sys.argv[1:]))
    lines = [
        '<ResourceDictionary xmlns="https://github.com/avaloniaui"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
        "",
        "  <!-- Generated by scripts/lucide-to-axaml.py from third_party/lucide (ISC). Do not edit;",
        "       add a name to the script and run it again. Stroke these with Controls/KpIcon. -->",
        "",
    ]
    for name in names:
        lines.append(f'  <StreamGeometry x:Key="Icon.{name}">{geometry(name)}</StreamGeometry>')
    lines += ["", "</ResourceDictionary>", ""]
    TARGET.write_text("\n".join(lines), encoding="utf-8", newline="\n")
    print(f"wrote {len(names)} icons to {TARGET.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
