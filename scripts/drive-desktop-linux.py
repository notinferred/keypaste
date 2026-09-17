#!/usr/bin/env python3
"""4.7b: drives an installed keypaste desktop app from outside its process through AT-SPI and xdotool (D-0199).

One action per call; prints what it saw and exits non-zero when the action could not be done. Needs an X
display, a session bus with org.a11y.Status enabled, python3-gi with Atspi, xdotool and ImageMagick.
"""

import subprocess
import sys
import time

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi, GLib  # noqa: E402

TITLE = "keypaste"


class Refused(Exception):
    pass


def until(seconds, probe):
    deadline = time.monotonic() + seconds
    while True:
        found = probe()
        if found is not None:
            return found
        if time.monotonic() >= deadline:
            return None
        time.sleep(0.25)


def window():
    desktop = Atspi.get_desktop(0)
    for i in range(desktop.get_child_count()):
        app = desktop.get_child_at_index(i)
        if app is None:
            continue
        for j in range(app.get_child_count()):
            frame = app.get_child_at_index(j)
            if frame is not None and frame.get_name() == TITLE:
                return frame
    return None


def descendants(root, depth=0):
    if root is None or depth > 60:
        return
    yield root
    for i in range(root.get_child_count()):
        yield from descendants(root.get_child_at_index(i), depth + 1)


def named(name, predicate=lambda e: True):
    frame = window()
    for element in descendants(frame):
        if element.get_name() == name and predicate(element):
            return element
    return None


def has(element, interface):
    return interface in element.get_interfaces()


def xwindow():
    ids = subprocess.run(["xdotool", "search", "--sync", "--name", f"^{TITLE}$"],
                         capture_output=True, text=True, timeout=30, check=True).stdout.split()
    if not ids:
        raise Refused("no X window named keypaste")
    return ids[0]


def foreground():
    wid = xwindow()
    subprocess.run(["xdotool", "windowactivate", "--sync", wid], check=True, timeout=10)
    time.sleep(0.3)
    return wid


def act(action, args):
    if action == "window":
        frame = until(int(args[0]), window)
        if frame is None:
            raise Refused(f"no accessible window named {TITLE} within {args[0]} s")
        return f"window '{frame.get_name()}' of {frame.get_application().get_name()}"

    if action == "screenshot":
        wid = xwindow()
        subprocess.run(["import", "-window", wid, args[0]], check=True, timeout=30)
        colours = subprocess.run(["identify", "-format", "%k", args[0]],
                                 capture_output=True, text=True, check=True, timeout=30).stdout.strip()
        size = subprocess.run(["identify", "-format", "%wx%h", args[0]],
                              capture_output=True, text=True, check=True, timeout=30).stdout.strip()
        return f"colours={colours} size={size}"

    if action == "type":
        wid = foreground()
        subprocess.run(["xdotool", "type", "--window", wid, "--delay", "40", args[0]], check=True, timeout=60)
        return f"typed {len(args[0])} characters"

    if action == "key":
        wid = foreground()
        subprocess.run(["xdotool", "key", "--window", wid, args[0]], check=True, timeout=10)
        return f"sent {args[0]}"

    if action == "find":
        element = until(30, lambda: named(args[0]))
        if element is None:
            raise Refused(f"nothing named '{args[0]}' appeared")
        return f"found '{args[0]}' as {element.get_role_name()}"

    if action == "find-prefix":
        def probe():
            for element in descendants(window()):
                name = element.get_name() or ""
                if name.startswith(args[0]):
                    return name
            return None
        text = until(30, probe)
        if text is None:
            raise Refused(f"nothing starting '{args[0]}' appeared")
        return text

    if action == "select":
        element = until(30, lambda: named(args[0], lambda e: not has(e, "Action")))
        if element is None:
            raise Refused(f"no text '{args[0]}' to select")
        item = element
        while item is not None and item.get_role() != Atspi.Role.LIST_ITEM:
            item = item.get_parent()
        if item is None:
            raise Refused(f"'{args[0]}' is not inside a list item")
        parent = item.get_parent()
        if not has(parent, "Selection") or not Atspi.Selection.select_child(parent, item.get_index_in_parent()):
            raise Refused(f"the list holding '{args[0]}' refused the selection")
        return f"selected '{args[0]}'"

    if action == "invoke":
        button = until(30, lambda: named(args[0], lambda e: has(e, "Action")))
        if button is None:
            raise Refused(f"no button '{args[0]}'")
        if not Atspi.Action.do_action(button, 0):
            raise Refused(f"'{args[0]}' refused its action")
        return f"invoked '{args[0]}'"

    if action == "set-after":
        label = until(30, lambda: named(args[0], lambda e: not has(e, "EditableText")))
        if label is None:
            raise Refused(f"no label '{args[0]}'")
        parent = label.get_parent()
        edit = None
        for i in range(label.get_index_in_parent() + 1, parent.get_child_count()):
            candidate = parent.get_child_at_index(i)
            if candidate is not None and has(candidate, "EditableText"):
                edit = candidate
                break
        if edit is None:
            raise Refused(f"no field after '{args[0]}'")
        return set_text(edit, args[1], f"the field after '{args[0]}'")

    if action == "set-only":
        def probe():
            found = [e for e in descendants(window()) if has(e, "EditableText")]
            return found or None
        edits = until(30, probe) or []
        if len(edits) != 1:
            raise Refused(f"expected one field, found {len(edits)}")
        return set_text(edits[0], args[0], "the only field")

    raise Refused(f"no action named {action}")


def set_text(edit, value, what):
    Atspi.EditableText.set_text_contents(edit, value)
    read = Atspi.Text.get_text(edit, 0, -1)
    if read != value:
        raise Refused(f"{what} reads '{read}'")
    return f"set {what}"


def main():
    if len(sys.argv) < 2:
        print("usage: drive-desktop-linux.py <action> [args...]")
        return 2
    try:
        print(act(sys.argv[1], sys.argv[2:]))
        return 0
    except (Refused, subprocess.SubprocessError, GLib.Error) as error:
        print(error)
        return 1


if __name__ == "__main__":
    sys.exit(main())
