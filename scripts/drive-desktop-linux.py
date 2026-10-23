#!/usr/bin/env python3
"""4.7b, R.1a: drives an installed keypaste desktop app from outside its process through AT-SPI and xdotool (D-0199).

One verb per call, as scripts/lib/desktop-drive.sh defines them; prints what it saw and exits 1 when the verb could
not be done. DRIVE_WINDOW names the app's window to act in. Needs an X display, a session bus with org.a11y.Status
enabled, python3-gi with Atspi, xdotool and ImageMagick.

A refusal to find an element also prints what the window offers to act on: its buttons, fields, lists and controls
with an automation id, by role, name and id, never a text's or a list item's name, which can carry what it shows.
"""

import difflib
import os
import re
import subprocess
import sys
import time

import gi

gi.require_version("Atspi", "2.0")
from gi.repository import Atspi, GLib  # noqa: E402

MAIN = "keypaste"
TITLE = os.environ.get("DRIVE_WINDOW") or MAIN
POLL = 0.05


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
        time.sleep(POLL)
        forget()


def forget():
    """Drops what libatspi cached of the window, so a poll reads the tree as it is now."""
    frame = window()
    if frame is not None:
        try:
            frame.clear_cache()
        except (AttributeError, GLib.Error):
            pass


def child(element, i):
    try:
        return element.get_child_at_index(i)
    except GLib.Error:
        return None


def children(element):
    try:
        count = element.get_child_count()
    except GLib.Error:
        return
    for i in range(count):
        found = child(element, i)
        if found is not None:
            yield found


def frames():
    """Each application's top-level windows, as (application, frame)."""
    desktop = Atspi.get_desktop(0)
    for app in children(desktop):
        for frame in children(app):
            yield app, frame


def name_of(element):
    try:
        return element.get_name() or ""
    except GLib.Error:
        return ""


def window():
    for _, frame in frames():
        if name_of(frame) == TITLE:
            return frame
    return None


def descendants(root, depth=0):
    if root is None or depth > 60:
        return
    yield root
    for found in children(root):
        yield from descendants(found, depth + 1)


def parse(locator):
    index = 1
    counted = re.fullmatch(r"(.*)\[(\d+)\]", locator)
    if counted:
        locator, index = counted.group(1), int(counted.group(2))
    if locator.startswith("id:"):
        return "id", locator[3:], index
    return "name", locator, index


def automation_id(element):
    try:
        return element.get_accessible_id() or ""
    except GLib.Error:
        return ""


def named(locator, predicate=lambda e: True):
    """A name, or id:<automation id>, either with [n] for the nth match in tree order."""
    kind, wanted, index = parse(locator)
    seen = 0
    for element in descendants(window()):
        value = automation_id(element) if kind == "id" else name_of(element)
        if value == wanted and predicate(element):
            seen += 1
            if seen == index:
                return element
    return None


def has(element, interface):
    try:
        return interface in element.get_interfaces()
    except GLib.Error:
        return False


def role(element):
    try:
        return element.get_role()
    except GLib.Error:
        return None


def role_name(element):
    try:
        return element.get_role_name()
    except GLib.Error:
        return "unknown"


def state(element, which):
    try:
        return element.get_state_set().contains(which)
    except GLib.Error:
        return False


QUIET_ROLES = {Atspi.Role.LABEL, Atspi.Role.TEXT, Atspi.Role.STATIC, Atspi.Role.LIST_ITEM, Atspi.Role.HEADING}


def offered():
    """The window's buttons, fields, lists and named controls, for a refusal; no text's or list item's name."""
    frame = window()
    if frame is None:
        titles = [name_of(f) for _, f in frames()]
        return f"no window named '{TITLE}'; the desktop shows {titles}"
    shown = []
    for element in descendants(frame):
        if role(element) in QUIET_ROLES:
            continue
        ident = automation_id(element)
        if has(element, "Selection"):
            shown.append(listed(element))
        elif ident or has(element, "Action") or has(element, "EditableText"):
            shown.append(f"{role_name(element)} '{name_of(element)}'" + (f" #{ident}" if ident else ""))
    return "the window offers: " + "; ".join(shown[:150])


def listed(listing):
    """A list as AT-SPI reports it: its name, its automation id and how many items it holds."""
    ident = automation_id(listing)
    return f"{role_name(listing)} '{name_of(listing)}'" + (f" #{ident}" if ident else "") + \
        f" of {listing.get_child_count()} items"


def missing(locator, what):
    kind, wanted, _ = parse(locator)
    near = ""
    if kind == "name":
        names = {name_of(e) for e in descendants(window())}
        close = difflib.get_close_matches(wanted, [n for n in names if 0 < len(n) <= 80], n=5, cutoff=0.75)
        if close:
            near = f"; names like it: {close}"
    return Refused(f"{what}{near}\n{offered()}")


def located(locator, predicate=lambda e: True, seconds=30, what=None):
    element = until(seconds, lambda: named(locator, predicate))
    if element is None:
        raise missing(locator, what or f"nothing named '{locator}' appeared within {seconds} s")
    return element


def xwindow():
    ids = subprocess.run(["xdotool", "search", "--sync", "--name", f"^{re.escape(TITLE)}$"],
                         capture_output=True, text=True, timeout=30, check=True).stdout.split()
    if not ids:
        raise Refused(f"no X window named {TITLE}")
    return ids[0]


def foreground():
    wid = xwindow()
    subprocess.run(["xdotool", "windowactivate", "--sync", wid], check=True, timeout=10)
    time.sleep(0.3)
    return wid


def centre(element):
    box = Atspi.Component.get_extents(element, Atspi.CoordType.SCREEN)
    if box.width <= 0 or box.height <= 0:
        raise Refused(f"it has no area on the screen: {box.x},{box.y} {box.width}x{box.height}")
    return box.x + box.width // 2, box.y + box.height // 2


def action_index(element, wanted):
    for i in range(Atspi.Action.get_n_actions(element)):
        if Atspi.Action.get_name(element, i) == wanted:
            return i
    return 0


def pressable(locator, what):
    """A control with an action once it is enabled, as a person waits for a button that is armed a moment late."""
    element = located(locator, lambda e: has(e, "Action"), what=what)
    if until(30, lambda: True if state(element, Atspi.StateType.ENABLED) else None) is None:
        raise Refused(f"'{locator}' stayed disabled for 30 s")
    return element


def act(action, args):
    if action == "window":
        frame = until(int(args[0]), window)
        if frame is None:
            raise Refused(f"no accessible window named {TITLE} within {args[0]} s")
        return f"window '{name_of(frame)}' of {frame.get_application().get_name()}"

    if action == "windows":
        apps = {app.get_process_id() for app, frame in frames() if name_of(frame).split(":")[0] == MAIN}
        titles = [name_of(frame) for app, frame in frames() if app.get_process_id() in apps]
        if not titles:
            raise Refused("no application shows a window named keypaste")
        return "\n".join(titles)

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

    if action == "key-wait":
        chord, locator, seconds = args[0], args[1], int(args[2])
        wid = foreground()
        looks = []

        def probe():
            began = time.monotonic()
            found = named(locator)
            looks.append(time.monotonic() - began)
            return found

        sent = time.monotonic()
        subprocess.run(["xdotool", "key", "--window", wid, chord], check=True, timeout=10)
        element = until(seconds, probe)
        elapsed = round((time.monotonic() - sent) * 1000)
        if element is None:
            raise missing(locator, f"nothing named '{locator}' appeared within {seconds} s of {chord}")
        fastest, slowest = round(min(looks) * 1000), round(max(looks) * 1000)
        return f"found '{locator}' {elapsed} ms after {chord}\n{len(looks)} looks took {fastest} to {slowest} ms each"

    if action == "find":
        element = located(args[0])
        return f"found '{args[0]}' as {role_name(element)}"

    if action == "wait":
        element = located(args[0], seconds=int(args[1]))
        return f"found '{args[0]}' as {role_name(element)}"

    if action == "wait-gone":
        gone = until(int(args[1]), lambda: True if named(args[0]) is None else None)
        if gone is None:
            raise Refused(f"'{args[0]}' was still there after {args[1]} s")
        return f"gone '{args[0]}'"

    if action == "find-prefix":
        def probe():
            for element in descendants(window()):
                name = name_of(element)
                if name.startswith(args[0]):
                    return name
            return None
        text = until(30, probe)
        if text is None:
            raise missing(args[0], f"nothing starting '{args[0]}' appeared")
        return text

    if action == "read":
        element = located(args[0])
        if has(element, "Text"):
            return Atspi.Text.get_text(element, 0, -1)
        return name_of(element)

    if action == "select":
        element = located(args[0], lambda e: not has(e, "Action"), what=f"no text '{args[0]}' to select")
        item = element
        while item is not None and role(item) != Atspi.Role.LIST_ITEM:
            item = item.get_parent()
        if item is None:
            raise Refused(f"'{args[0]}' is not inside a list item")
        parent = item.get_parent()
        index = item.get_index_in_parent()
        if not has(parent, "Selection") or not Atspi.Selection.select_child(parent, index):
            raise Refused(f"the list holding '{args[0]}' refused the selection")
        return f"selected '{args[0]}'\nitem {index + 1} of {listed(parent)}"

    if action == "select-nth":
        listing = located(args[0], lambda e: has(e, "Selection"), what=f"no list '{args[0]}'")
        index = int(args[1]) - 1
        if not Atspi.Selection.select_child(listing, index):
            raise Refused(f"'{args[0]}' refused to select item {args[1]} of its {listing.get_child_count()}")
        if until(5, lambda: True if Atspi.Selection.is_child_selected(listing, index) else None) is None:
            raise Refused(f"item {args[1]} of '{args[0]}' was not selected")
        return f"selected item {args[1]} of '{args[0]}'"

    if action in ("invoke", "toggle"):
        button = pressable(args[0], f"no button '{args[0]}'")
        if not Atspi.Action.do_action(button, 0):
            raise Refused(f"'{args[0]}' refused its action")
        return f"{'invoked' if action == 'invoke' else 'toggled'} '{args[0]}'"

    if action == "check":
        wanted = args[1] == "on"
        box = pressable(args[0], f"no check box '{args[0]}'")
        if not state(box, Atspi.StateType.CHECKABLE):
            raise Refused(f"'{args[0]}' is a {role_name(box)}, which has no on or off")
        if state(box, Atspi.StateType.CHECKED) != wanted:
            if not Atspi.Action.do_action(box, action_index(box, "toggle")):
                raise Refused(f"'{args[0]}' refused its toggle")
        if until(5, lambda: True if state(box, Atspi.StateType.CHECKED) == wanted else None) is None:
            raise Refused(f"'{args[0]}' did not turn {args[1]}")
        return f"'{args[0]}' is {args[1]}"

    if action in ("click", "right-click"):
        element = located(args[0])
        x, y = centre(element)
        foreground()
        button = "1" if action == "click" else "3"
        subprocess.run(["xdotool", "mousemove", "--sync", str(x), str(y), "click", button], check=True, timeout=10)
        return f"{'clicked' if action == 'click' else 'right-clicked'} '{args[0]}' at {x},{y}"

    if action == "focus":
        element = located(args[0])
        foreground()
        if not Atspi.Component.grab_focus(element):
            raise Refused(f"'{args[0]}' refused the focus")
        if until(5, lambda: True if state(element, Atspi.StateType.FOCUSED) else None) is None:
            raise Refused(f"'{args[0]}' did not take the focus")
        return f"focused '{args[0]}'"

    if action == "set-after":
        label = located(args[0], lambda e: not has(e, "EditableText"), what=f"no label '{args[0]}'")
        parent = label.get_parent()
        edit = None
        for i in range(label.get_index_in_parent() + 1, parent.get_child_count()):
            candidate = child(parent, i)
            if candidate is not None and has(candidate, "EditableText"):
                edit = candidate
                break
        if edit is None:
            raise Refused(f"no field after '{args[0]}'")
        return set_text(edit, args[1], f"the field after '{args[0]}'")

    if action == "set-named":
        edit = located(args[0], lambda e: has(e, "EditableText"), what=f"no field named '{args[0]}'")
        return set_text(edit, args[1], f"the field named '{args[0]}'")

    raise Refused(f"no verb named {action}")


def set_text(edit, value, what):
    Atspi.EditableText.set_text_contents(edit, value)
    read = Atspi.Text.get_text(edit, 0, -1)
    if read != value:
        raise Refused(f"{what} reads '{read}'")
    return f"set {what}"


def main():
    if len(sys.argv) < 2:
        print("usage: drive-desktop-linux.py <verb> [args...]")
        return 1
    try:
        print(act(sys.argv[1], sys.argv[2:]))
        return 0
    except (Refused, subprocess.SubprocessError, GLib.Error) as error:
        print(error)
        return 1


if __name__ == "__main__":
    sys.exit(main())
