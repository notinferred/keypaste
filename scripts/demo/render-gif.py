#!/usr/bin/env python3
"""Renders a tape from capture-demo.py as the demo GIF: two terminal windows in the brand's terminal style.

The top window replays keypaste agent's pseudo-terminal byte for byte, colour escapes included; the
commands typed at its shell are animated at the tape's typing speed, and nothing a program did not
print is drawn into a terminal body. The bottom window shows the scripted MCP client's calls and the
text keypaste mcp returned to it, verbatim. Keys the person pressed, which the approver does not
echo, are named in the window header while they take effect.

Needs Pillow and ffmpeg on PATH.

  python scripts/demo/render-gif.py artifacts/demo/tape.json docs/demo/keypaste-demo.gif
"""
import argparse
import json
import math
import shutil
import subprocess
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
FONT = ROOT / "src/Keypaste.App/Assets/Fonts/FragmentMono-Regular.ttf"

SCALE = 2
WIDTH = 960
MARGIN, GAP = 16, 12
FONT_SIZE, LINE_HEIGHT = 14, 18
HEADER_HEIGHT, PAD_Y = 28, 10
TOP_ROWS, BOTTOM_ROWS = 22, 11
FINAL_HOLD = 4.5
KEY_SHOWN = 1.6
MIN_FRAME = 0.03


def oklch(lightness, chroma, hue):
    """An sRGB colour from OKLCH, for the brand's status colours (docs/BRAND.md)."""
    a, b = chroma * math.cos(math.radians(hue)), chroma * math.sin(math.radians(hue))
    l_ = (lightness + 0.3963377774 * a + 0.2158037573 * b) ** 3
    m_ = (lightness - 0.1055613458 * a - 0.0638541728 * b) ** 3
    s_ = (lightness - 0.0894841775 * a - 1.2914855480 * b) ** 3
    linear = (
        4.0767416621 * l_ - 3.3077115913 * m_ + 0.2309699292 * s_,
        -1.2684380046 * l_ + 2.6097574011 * m_ - 0.3413193965 * s_,
        -0.0041960863 * l_ - 0.7034186147 * m_ + 1.7076147010 * s_,
    )

    def encode(channel):
        channel = min(max(channel, 0.0), 1.0)
        return 12.92 * channel if channel <= 0.0031308 else 1.055 * channel ** (1 / 2.4) - 0.055

    return tuple(round(encode(channel) * 255) for channel in linear)


def hex_rgb(value):
    return tuple(int(value[i:i + 2], 16) for i in (1, 3, 5))


CANVAS = hex_rgb("#111214")
TERMINAL = hex_rgb("#0C0D0E")
DIVIDER = hex_rgb("#1D1E22")
BORDER = hex_rgb("#26282C")
DOT = hex_rgb("#3A3D43")
KEYCAP = hex_rgb("#2A2C30")
TEXT = hex_rgb("#F2F2F0")
SECONDARY = hex_rgb("#C9CACD")
MUTED = hex_rgb("#8E9096")
AMBER = hex_rgb("#F2B544")
INK = hex_rgb("#111214")
OK = oklch(0.80, 0.13, 155)
DANGER = oklch(0.74, 0.14, 25)

# The CLI's 256-colour escapes when the terminal does not advertise truecolor (SystemConsoleStyle).
INDEXED = {214: AMBER, 78: OK, 210: DANGER, 246: MUTED}

def paint(colour, text):
    return f"\x1b[38;2;{';'.join(map(str, colour))}m{text}\x1b[0m"


PROMPT = paint(MUTED, "~ ") + paint(AMBER, "›") + " "


class Screen:
    """The part of a terminal the demo's programs use: text, colour, CR, LF and autowrap."""

    def __init__(self, columns):
        self.columns = columns
        self.lines = [[]]
        self.row = self.column = 0
        self.wrap_pending = False
        self.colour = None
        self.pending = ""

    def feed(self, text):
        text = self.pending + text
        self.pending = ""
        i = 0
        while i < len(text):
            char = text[i]
            if char == "\x1b":
                end = self._escape_end(text, i)
                if end is None:
                    self.pending = text[i:]
                    return
                self._escape(text[i:end])
                i = end
                continue
            if char == "\r":
                self.column, self.wrap_pending = 0, False
            elif char == "\n":
                self._line_feed()
            elif char == "\b":
                self.column, self.wrap_pending = max(0, self.column - 1), False
            elif char >= " ":
                self._put(char)
            i += 1

    @staticmethod
    def _escape_end(text, start):
        if start + 1 >= len(text):
            return None
        kind = text[start + 1]
        if kind == "[":
            for end in range(start + 2, len(text)):
                if "\x40" <= text[end] <= "\x7e":
                    return end + 1
            return None
        if kind == "]":
            for end in range(start + 2, len(text)):
                if text[end] == "\x07":
                    return end + 1
                if text[end] == "\\" and text[end - 1] == "\x1b":
                    return end + 1
            return None
        return start + 2

    def _escape(self, sequence):
        """Applies a colour; the keypad-mode escapes .NET writes on start move nothing and are dropped."""
        if not (sequence.startswith("\x1b[") and sequence.endswith("m")):
            return
        params = [int(p) if p.isdigit() else 0 for p in sequence[2:-1].split(";")]
        i = 0
        while i < len(params):
            p = params[i]
            if p in (0, 39):
                self.colour = None
            elif p == 38 and params[i + 1:i + 2] == [2]:
                self.colour = tuple(params[i + 2:i + 5])
                i += 4
            elif p == 38 and params[i + 1:i + 2] == [5]:
                self.colour = INDEXED.get(params[i + 2])
                i += 2
            i += 1

    def _line_feed(self):
        self.row += 1
        self.wrap_pending = False
        while len(self.lines) <= self.row:
            self.lines.append([])

    def _put(self, char):
        if self.wrap_pending:
            self.column = 0
            self._line_feed()
        line = self.lines[self.row]
        while len(line) <= self.column:
            line.append((" ", None))
        line[self.column] = (char, self.colour)
        if self.column == self.columns - 1:
            self.wrap_pending = True
        else:
            self.column += 1

    def at_line_start(self):
        return self.column == 0 and not self.wrap_pending

    def visible(self, rows):
        top = max(0, len(self.lines) - rows)
        cursor = (self.row - top, self.column)
        return [tuple(line) for line in self.lines[top:top + rows]], cursor


class Painter:
    """Draws rows of cells at SCALE, caching each distinct row."""

    def __init__(self, columns):
        self.font = ImageFont.truetype(str(FONT), FONT_SIZE * SCALE)
        self.header_font = ImageFont.truetype(str(FONT), 12 * SCALE)
        self.advance = self.font.getlength("0")
        self.line_height = LINE_HEIGHT * SCALE
        self.columns = columns
        self.rows = {}
        ascent, descent = self.font.getmetrics()
        self.baseline = (self.line_height - (ascent + descent)) // 2 + ascent

    def x(self, column):
        return round(column * self.advance)

    def row(self, cells, cursor_column):
        key = (cells, cursor_column)
        if key in self.rows:
            return self.rows[key]
        image = Image.new("RGB", (self.x(self.columns), self.line_height), TERMINAL)
        draw = ImageDraw.Draw(image)
        for column, (char, colour) in enumerate(cells):
            fill = colour or TEXT
            if column == cursor_column:
                draw.rectangle((self.x(column), 0, self.x(column + 1) - 1, self.line_height - 1), fill=AMBER)
                fill = INK
            self._glyph(draw, column, char, fill)
        if cursor_column is not None and cursor_column >= len(cells):
            draw.rectangle((self.x(cursor_column), 0, self.x(cursor_column + 1) - 1, self.line_height - 1), fill=AMBER)
        self.rows[key] = image
        return image

    def _glyph(self, draw, column, char, fill):
        if char == " ":
            return
        if char == "─":
            # Fragment Mono has no box drawing; terminals draw this one themselves.
            middle = self.line_height // 2
            draw.rectangle((self.x(column), middle - SCALE // 2, self.x(column + 1) - 1, middle + SCALE - SCALE // 2 - 1), fill=fill)
            return
        draw.text((self.x(column), self.baseline), char, font=self.font, fill=fill, anchor="ls")


class Window:
    def __init__(self, painter, rows, title, cursor):
        self.painter = painter
        self.rows = rows
        self.title = title
        self.cursor = cursor
        self.screen = Screen(painter.columns)
        self.key = None

    def height(self):
        return HEADER_HEIGHT + 2 * PAD_Y + self.rows * LINE_HEIGHT

    def draw(self, frame, top):
        s = SCALE
        left, right = MARGIN * s, (WIDTH - MARGIN) * s
        bottom = top + self.height() * s
        draw = ImageDraw.Draw(frame)
        draw.rounded_rectangle((left, top, right - 1, bottom - 1), radius=10 * s, fill=TERMINAL, outline=BORDER, width=s)
        header = top + HEADER_HEIGHT * s
        draw.rectangle((left + s, header - s, right - 2 * s, header - 1), fill=DIVIDER)
        middle = top + HEADER_HEIGHT * s // 2
        for i in range(3):
            cx = left + (14 + 5 + i * 16) * s
            draw.ellipse((cx - 5 * s, middle - 5 * s, cx + 5 * s, middle + 5 * s), fill=DOT)
        draw.text((left + 64 * s, middle), self.title, font=self.painter.header_font, fill=MUTED, anchor="lm")
        if self.key:
            self._keycap(draw, right - 14 * s, middle)

        cells, (cursor_row, cursor_column) = self.screen.visible(self.rows)
        text_left = left + round(((right - left) - self.painter.x(self.painter.columns)) / 2)
        y = header + PAD_Y * s
        for index in range(self.rows):
            line = cells[index] if index < len(cells) else ()
            shown = cursor_column if self.cursor and index == cursor_row else None
            frame.paste(self.painter.row(line, shown), (text_left, y + index * self.painter.line_height))

    def _keycap(self, draw, right, middle):
        s = SCALE
        font = self.painter.header_font
        width = font.getlength(self.key) + 12 * s
        cap = (right - width, middle - 9 * s, right, middle + 9 * s)
        draw.rounded_rectangle(cap, radius=4 * s, fill=KEYCAP, outline=DOT, width=s)
        draw.text(((cap[0] + cap[2]) / 2, middle), self.key, font=font, fill=TEXT, anchor="mm")
        draw.text((cap[0] - 8 * s, middle), "you pressed", font=font, fill=MUTED, anchor="rm")


def client_lines(event, calls):
    """What the scripted client shows for one JSON-RPC message, or None for the handshake."""
    message = event["message"]
    if event["type"] == "send":
        if message.get("method") != "tools/call":
            return None
        calls.add(message["id"])
        params = message["params"]
        arguments = dict(params["arguments"])
        reason = arguments.pop("reason")
        fields = "  ".join(f"{name}: {value}" for name, value in arguments.items())
        return (
            paint(MUTED, "→ ") + paint(SECONDARY, f"{params['name']}  {fields}") + "\r\n"
            + paint(SECONDARY, f"  reason: {reason}") + "\r\n"
        )
    if message.get("id") not in calls:
        return None
    result = message["result"]
    text = "".join(block["text"] for block in result["content"] if block["type"] == "text")
    arrow = paint(DANGER if result.get("isError") else OK, "← ")
    return arrow + "\r\n  ".join(text.split("\n")) + "\r\n"


def timeline(tape):
    """Every change as (time, action), in order; an action changes the windows."""
    actions = []
    events = tape["events"]
    calls = set()
    last_top_output = [e["t"] for e in events if e["pane"] == "top" and e["type"] == "output"]

    for event in events:
        t = event["t"]
        kind = event["type"]
        if kind == "prompt":
            actions.append((t, ("top", "prompt", None)))
        elif kind == "command":
            text, cps = event["text"], event["cps"]
            for i, char in enumerate(text):
                actions.append((t + i / cps, ("top", "type", char)))
            typed = t + len(text) / cps
            following = min([o for o in last_top_output if o > t] or [typed + 0.3])
            actions.append((min(typed + 0.3, following), ("top", "enter", None)))
            actions.append((t, ("top", "title", " ".join(text.split()[:2]))))
        elif kind == "output":
            actions.append((t, ("top", "feed", event["text"])))
        elif kind == "key":
            name = "Ctrl+C" if event["key"] == "\x03" else event["key"]
            actions.append((t, ("top", "key", name)))
            actions.append((t + KEY_SHOWN, ("top", "key", None)))
        elif kind in ("send", "receive"):
            lines = client_lines(event, calls)
            if lines:
                actions.append((t, ("bottom", "client", lines)))
    actions.sort(key=lambda action: action[0])
    return actions


def apply(windows, action):
    pane, kind, value = action
    window = windows[pane]
    if kind == "prompt":
        if not window.screen.at_line_start():
            window.screen.feed("\r\n")
        window.screen.feed(PROMPT)
    elif kind == "type":
        window.screen.feed(value)
    elif kind == "enter":
        window.screen.feed("\r\n")
    elif kind == "title":
        window.title = value
    elif kind == "feed":
        window.screen.feed(value)
    elif kind == "key":
        window.key = value
    elif kind == "client":
        if window.screen.lines != [[]]:
            window.screen.feed("\r\n")
        window.screen.feed(value)


def render(tape, output):
    painter = Painter(tape["columns"])
    windows = {
        "top": Window(painter, TOP_ROWS, "keypaste agent", cursor=True),
        "bottom": Window(painter, BOTTOM_ROWS, "agent: a scripted MCP client, over stdio to keypaste mcp", cursor=False),
    }
    height = MARGIN * 2 + GAP + windows["top"].height() + windows["bottom"].height()
    actions = timeline(tape)
    end = actions[-1][0] + FINAL_HOLD

    work = Path(tempfile.mkdtemp(prefix="keypaste-gif-"))
    try:
        frames = []
        previous = None
        pending = iter(actions)
        upcoming = next(pending, None)
        for t in sorted({t for t, _ in actions} | {0.0}):
            while upcoming and upcoming[0] <= t:
                apply(windows, upcoming[1])
                upcoming = next(pending, None)
            frame = Image.new("RGB", (WIDTH * SCALE, height * SCALE), CANVAS)
            windows["top"].draw(frame, MARGIN * SCALE)
            windows["bottom"].draw(frame, (MARGIN + windows["top"].height() + GAP) * SCALE)
            frame = frame.resize((WIDTH, height), Image.LANCZOS)
            data = frame.tobytes()
            if data == previous:
                continue
            previous = data
            # Browsers hold a frame of under 2cs for 10cs, so a burst of output becomes one frame.
            if frames and t - frames[-1][1] < MIN_FRAME:
                path = frames[-1][0]
            else:
                path = work / f"{len(frames):04d}.png"
                frames.append((path, t))
            frame.save(path)

        # GIF delays are whole centiseconds; rounding each boundary keeps the total true. The
        # framerate option gives each image a 1/100 time base instead of the default 1/25.
        starts = [round(t * 100) for _, t in frames] + [round(end * 100)]
        delays = [following - start for start, following in zip(starts, starts[1:])]
        listing = work / "frames.txt"
        listing.write_text(
            "ffconcat version 1.0\n"
            + "".join(f"file '{path.as_posix()}'\noption framerate 100\nduration {delay / 100:.2f}\n" for (path, _), delay in zip(frames, delays)),
            encoding="utf-8",
        )

        Path(output).parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(
            [
                "ffmpeg", "-v", "error", "-y", "-f", "concat", "-safe", "0", "-i", str(listing),
                "-vf", "split[a][b];[a]palettegen=max_colors=256:stats_mode=full[p];[b][p]paletteuse=dither=none:diff_mode=rectangle",
                "-fps_mode", "vfr", "-loop", "0", "-final_delay", str(delays[-1]), str(output),
            ],
            check=True,
        )
        print(f"render: {len(frames)} frames, {WIDTH}x{height}, {end:.1f}s, {Path(output).stat().st_size} bytes to {output}")
    finally:
        shutil.rmtree(work, ignore_errors=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("tape")
    parser.add_argument("output")
    options = parser.parse_args()
    render(json.loads(Path(options.tape).read_text(encoding="utf-8")), options.output)


if __name__ == "__main__":
    main()
