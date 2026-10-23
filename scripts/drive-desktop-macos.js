// 4.7e, R.1a: drives an installed keypaste desktop app from outside its process through the macOS accessibility API,
// System Events' keystrokes, CGEvent's pointer and screencapture (D-0199).
//
// One verb per call, as scripts/lib/desktop-drive.sh defines them; prints what it saw and exits 1 when the verb could
// not be done, 2 when this driver does not implement it. DRIVE_WINDOW names the app's window to act in. When a verb
// cannot be done and DRIVE_TREE_DIR names a folder, the window's accessibility tree is written there, with no text
// field's value. Needs Accessibility and Apple Events to System Events for the process that runs osascript, which a
// hosted runner grants.
//
// Usage: osascript -l JavaScript drive-desktop-macos.js <pid> <verb> [args...]
//        osascript -l JavaScript drive-desktop-macos.js - dialog|press <process-name> [button]
ObjC.import('Foundation');
ObjC.import('AppKit');
ObjC.import('CoreGraphics');
ObjC.import('ApplicationServices');
ObjC.import('stdlib');

const TITLE = environment('DRIVE_WINDOW') || 'keypaste';
const DEPTH = 40;
const LIMIT = 4000;
const FIELDS = new Set(['AXTextField', 'AXTextArea']);
const PRESSABLE = /Button|CheckBox|MenuItem|Link|DisclosureTriangle/;
// Avalonia gives a toggle's state as AXValue: a CheckBox is an AXCheckBox, a ToggleButton or ToggleSwitch an AXButton.
const TOGGLES = /CheckBox|Button/;
const NAMES = ['AXTitle', 'AXValue', 'AXDescription'];
const KEYS = { Return: 36, Tab: 48, Escape: 53, Delete: 117 };
const MOUSE = { left: { down: 1, up: 2, button: 0 }, right: { down: 3, up: 4, button: 1 } };
const MOUSE_MOVED = 5;
const HID_TAP = 0;
const events = Application('System Events');
const shell = Application.currentApplication();
shell.includeStandardAdditions = true;

class Refused extends Error {}

class Unimplemented extends Error {}

function environment(name) {
  return ObjC.unwrap($.NSProcessInfo.processInfo.environment.objectForKey($(name))) || '';
}

function print(text) {
  $.NSFileHandle.fileHandleWithStandardOutput.writeData($(`${text}\n`).dataUsingEncoding($.NSUTF8StringEncoding));
}

function until(seconds, probe, interval = 0.25) {
  const deadline = Date.now() + seconds * 1000;
  for (;;) {
    const found = probe();
    if (found !== null && found !== undefined) return found;
    if (Date.now() >= deadline) return null;
    delay(interval);
  }
}

function attempt(read, fallback) {
  try {
    return read();
  } catch (error) {
    return fallback;
  }
}

function text(value) {
  return value === null || value === undefined ? '' : String(value);
}

// The API takes its attribute and action names as NSStrings; a JavaScript string is refused as an illegal argument.
const ax = {
  value(element, name) {
    const out = Ref();
    return $.AXUIElementCopyAttributeValue(element, $(name), out) === 0 ? ObjC.castRefToObject(out[0]) : null;
  },
  text(element, name) {
    const value = ax.value(element, name);
    return value ? text(attempt(() => ObjC.unwrap(value.description), '')) : '';
  },
  children(element) {
    const list = ax.value(element, 'AXChildren');
    const out = [];
    for (let i = 0; list && i < list.count; i++) out.push(list.objectAtIndex(i));
    return out;
  },
  settable(element, name) {
    const out = Ref();
    return $.AXUIElementIsAttributeSettable(element, $(name), out) === 0 && Boolean(out[0]);
  },
  set(element, name, value) {
    return $.AXUIElementSetAttributeValue(element, $(name), value);
  },
  press(element) {
    return $.AXUIElementPerformAction(element, $('AXPress'));
  },
  // AXPosition and AXSize are AXValues, read from their description: {value = x:12.0 y:34.0 type = kAXValueCGPointType}.
  frame(element) {
    const position = /x:(-?[\d.]+) y:(-?[\d.]+)/.exec(ax.text(element, 'AXPosition'));
    const size = /w:([\d.]+) h:([\d.]+)/.exec(ax.text(element, 'AXSize'));
    if (!position || !size) return null;
    return { x: Number(position[1]), y: Number(position[2]), width: Number(size[1]), height: Number(size[2]) };
  },
};

// One element of the tree, its attributes read once each and only when asked for.
class Node {
  constructor(element, depth, parent) {
    this.element = element;
    this.depth = depth;
    this.parent = parent;
    this.read = {};
  }

  attribute(name) {
    if (!(name in this.read)) this.read[name] = ax.text(this.element, name);
    return this.read[name];
  }

  get role() { return this.attribute('AXRole'); }

  get identifier() { return this.attribute('AXIdentifier'); }

  get texts() { return NAMES.map(name => this.attribute(name)); }
}

function application(pid) {
  const app = $.AXUIElementCreateApplication(pid);
  $.AXUIElementSetMessagingTimeout(app, 5);
  return app;
}

function windowsOf(pid) {
  return ax.children(application(pid)).filter(w => ax.text(w, 'AXRole') === 'AXWindow');
}

function window(pid) {
  return windowsOf(pid).find(w => ax.text(w, 'AXTitle') === TITLE) || null;
}

// Visits the window's elements in tree order until stop says so; returns those visited.
function walk(root, stop) {
  const all = [];
  const visit = (element, depth, parent) => {
    if (depth > DEPTH) return false;
    for (const child of ax.children(element)) {
      if (all.length >= LIMIT) return true;
      const node = new Node(child, depth, parent);
      all.push(node);
      if (stop(node, all.length - 1)) return true;
      if (visit(child, depth + 1, all.length - 1)) return true;
    }
    return false;
  };
  visit(root, 0, -1);
  return all;
}

// A locator is a name, or id:<automation id>, either with [n] for the nth match in tree order.
function locate(locator) {
  const counted = /^(.*)\[(\d+)\]$/.exec(locator);
  const name = counted ? counted[1] : locator;
  const index = counted ? Number(counted[2]) : 1;
  if (name.startsWith('id:')) {
    const id = name.slice(3);
    return { index, matches: node => node.identifier === id };
  }
  return { index, matches: node => node.texts.includes(name) };
}

// The nth element the predicate accepts, once one is there; whole reads the rest of the tree too.
function search(pid, predicate, index = 1, whole = false) {
  const frame = window(pid);
  if (!frame) return null;
  let seen = 0;
  let at = -1;
  const all = walk(frame, (node, i) => {
    if (at < 0 && predicate(node) && ++seen === index) at = i;
    return at >= 0 && !whole;
  });
  return at < 0 ? null : { node: all[at], all, index: at };
}

function find(pid, predicate, seconds, index = 1, whole = false) {
  return until(seconds, () => search(pid, predicate, index, whole));
}

// The element a verb's locator names, among those the predicate accepts.
function located(pid, locator, predicate = () => true, seconds = 30, whole = false) {
  const { matches, index } = locate(locator);
  return find(pid, node => matches(node) && predicate(node), seconds, index, whole);
}

function present(pid, locator) {
  const { matches, index } = locate(locator);
  return search(pid, matches, index);
}

function front(pid) {
  const found = attempt(() => events.processes.whose({ unixId: pid })(), []);
  if (!found.length) throw new Refused(`no process ${pid} in System Events`);
  found[0].frontmost = true;
  delay(0.3);
}

function setText(node, value, what) {
  const status = ax.set(node.element, 'AXValue', $(value));
  if (status !== 0) throw new Refused(`${what} refused its value (${status})`);
  const read = until(5, () => (ax.text(node.element, 'AXValue') === value ? true : null));
  if (!read) throw new Refused(`${what} reads '${ax.text(node.element, 'AXValue')}'`);
  return `set ${what}`;
}

function settableText(node) {
  return FIELDS.has(node.role) || ax.settable(node.element, 'AXValue');
}

function enabled(node, seconds) {
  return until(seconds, () => (ax.text(node.element, 'AXEnabled') === '0' ? null : true)) !== null;
}

function press(node, locator) {
  if (!enabled(node, 10)) throw new Refused(`'${locator}' stayed disabled for 10 s`);
  const status = ax.press(node.element);
  if (status !== 0) throw new Refused(`'${locator}' refused AXPress (${status})`);
}

// A real pointer click at the element's centre, once the app has brought it into view.
function click(pid, node, locator, kind) {
  $.AXUIElementPerformAction(node.element, $('AXScrollToVisible'));
  delay(0.3);
  const frame = ax.frame(node.element);
  if (!frame || frame.width <= 0 || frame.height <= 0) throw new Refused(`'${locator}' has no frame on the screen`);
  const point = { x: Math.round(frame.x + frame.width / 2), y: Math.round(frame.y + frame.height / 2) };
  front(pid);
  const mouse = MOUSE[kind];
  for (const type of [MOUSE_MOVED, mouse.down, mouse.up]) {
    $.CGEventPost(HID_TAP, $.CGEventCreateMouseEvent(null, type, point, type === MOUSE_MOVED ? 0 : mouse.button));
    delay(0.08);
  }
  return point;
}

function focused(node) {
  return ax.text(node.element, 'AXFocused') === '1';
}

// The window's on-screen number: the one of the process's windows whose bounds are the AX window's.
function windowNumber(pid) {
  const listed = ObjC.deepUnwrap(ObjC.castRefToObject($.CGWindowListCopyWindowInfo(1 | 16, 0))) || [];
  const own = listed.filter(w => w.kCGWindowOwnerPID === pid && w.kCGWindowLayer === 0);
  if (!own.length) return null;
  const frame = attempt(() => ax.frame(window(pid)), null);
  const same = frame && own.find(w => {
    const b = w.kCGWindowBounds || {};
    return Math.abs(b.X - frame.x) < 2 && Math.abs(b.Y - frame.y) < 2 && Math.abs(b.Width - frame.width) < 2 && Math.abs(b.Height - frame.height) < 2;
  });
  return (same || own.find(w => w.kCGWindowName === TITLE) || own[0]).kCGWindowNumber;
}

function colours(path) {
  const image = $.NSBitmapImageRep.imageRepWithContentsOfFile($(path));
  if (image.isNil()) throw new Refused(`${path} is not an image`);
  const width = image.pixelsWide;
  const height = image.pixelsHigh;
  const stride = Math.max(1, Math.floor(Math.min(width, height) / 64));
  const seen = new Set();
  for (let y = 0; y < height; y += stride) {
    for (let x = 0; x < width; x += stride) {
      const colour = image.colorAtXY(x, y).colorUsingColorSpace($.NSColorSpace.sRGBColorSpace);
      seen.add([colour.redComponent, colour.greenComponent, colour.blueComponent].map(c => Math.round(c * 255)).join(','));
    }
  }
  return `colours=${seen.size} size=${width}x${height}`;
}

function quoted(value) {
  return `'${value.replace(/'/g, `'\\''`)}'`;
}

function dialog(name) {
  const owner = attempt(() => events.processes.byName(name), null);
  if (!owner || !attempt(() => owner.exists(), false)) return [];
  return attempt(() => owner.windows(), []).map(w => ({
    window: text(attempt(() => w.name(), '')),
    texts: attempt(() => w.staticTexts.value(), []).map(text),
    buttons: attempt(() => w.buttons.name(), []).map(text),
  }));
}

// A line of text that reads KEY=value could carry a value, so the tree keeps only that it was there.
function shown(value) {
  return /[A-Za-z_][A-Za-z0-9_]*=\S/.test(value) ? '<a KEY=value line, not kept>' : JSON.stringify(value);
}

function describe(node) {
  const parts = [`${'  '.repeat(node.depth)}${node.role || '?'}`];
  if (node.identifier) parts.push(`#${node.identifier}`);
  const [title, value, description] = node.texts;
  if (title) parts.push(`title=${shown(title)}`);
  if (value) parts.push(settableText(node) ? `value=<${value.length} characters, not kept>` : `value=${shown(value)}`);
  if (description) parts.push(`description=${shown(description)}`);
  if (focused(node)) parts.push('focused');
  if (ax.text(node.element, 'AXEnabled') === '0') parts.push('disabled');
  return parts.join(' ');
}

// Writes the app's windows and the acted-in window's tree to DRIVE_TREE_DIR; says where, or nothing.
function keepTree(pid, verb) {
  const folder = environment('DRIVE_TREE_DIR');
  if (!folder || !(pid > 0)) return '';
  const lines = [`after ${verb} could not be done; the app's windows: ${windowsOf(pid).map(w => JSON.stringify(ax.text(w, 'AXTitle'))).join(', ')}`];
  const frame = window(pid);
  if (frame) walk(frame, node => { lines.push(attempt(() => describe(node), `${'  '.repeat(node.depth)}unread`)); return false; });
  else lines.push(`no window titled ${JSON.stringify(TITLE)}`);
  const name = `tree-${Date.now()}.txt`;
  $(lines.join('\n') + '\n').writeToFileAtomicallyEncodingError($(`${folder}/${name}`), true, $.NSUTF8StringEncoding, null);
  return `; the window's tree is in ${name}`;
}

function sendKey(pid, chord) {
  front(pid);
  const modified = /^(cmd|ctrl|shift)\+(.+)$/.exec(chord);
  const key = modified ? modified[2] : chord;
  const using = modified && { using: { cmd: 'command down', ctrl: 'control down', shift: 'shift down' }[modified[1]] };
  if (key in KEYS && using) events.keyCode(KEYS[key], using);
  else if (key in KEYS) events.keyCode(KEYS[key]);
  else if (using && key.length === 1 && modified[1] !== 'shift') events.keystroke(key, using);
  else throw new Unimplemented(`the chord ${chord}`);
}

function act(pid, action, args) {
  switch (action) {
    case 'window': {
      const frame = until(Number(args[0]), () => window(pid));
      if (!frame) throw new Refused(`no window named ${TITLE} for process ${pid} within ${args[0]} s`);
      return `window '${TITLE}' of process ${pid}`;
    }
    case 'windows':
      return windowsOf(pid).map(w => ax.text(w, 'AXTitle')).join('\n');
    case 'screenshot': {
      const number = until(10, () => windowNumber(pid));
      if (number === null) throw new Refused(`no on-screen window of process ${pid}`);
      shell.doShellScript(`/usr/sbin/screencapture -x -o -l ${number} ${quoted(args[0])}`);
      return `${colours(args[0])} window=${number}`;
    }
    case 'type':
      front(pid);
      events.keystroke(args[0]);
      return `typed ${args[0].length} characters`;
    case 'key':
      sendKey(pid, args[0]);
      return `sent ${args[0]}`;
    case 'key-wait': {
      const { matches, index } = locate(args[1]);
      sendKey(pid, args[0]);
      const sent = Date.now();
      let began = sent;
      const found = until(Number(args[2]), () => {
        began = Date.now();
        return search(pid, matches, index);
      }, 0.05);
      if (!found) throw new Refused(`nothing named '${args[1]}' appeared within ${args[2]} s of ${args[0]}`);
      return `found '${args[1]}' ${Date.now() - sent} ms after ${args[0]} (the look that found it began at ${began - sent} ms)`;
    }
    case 'find':
    case 'wait': {
      const found = located(pid, args[0], () => true, action === 'find' ? 30 : Number(args[1]));
      if (!found) throw new Refused(`nothing named '${args[0]}' appeared`);
      return `found '${args[0]}' as ${found.node.role}`;
    }
    case 'wait-gone': {
      const gone = until(Number(args[1]), () => (present(pid, args[0]) ? null : true));
      if (!gone) throw new Refused(`'${args[0]}' was still there after ${args[1]} s`);
      return `gone '${args[0]}'`;
    }
    case 'find-prefix': {
      const found = find(pid, node => node.texts.some(t => t.startsWith(args[0])), 30);
      if (!found) throw new Refused(`nothing starting '${args[0]}' appeared`);
      return found.node.texts.find(t => t.startsWith(args[0]));
    }
    case 'read': {
      const found = located(pid, args[0]);
      if (!found) throw new Refused(`nothing named '${args[0]}' to read`);
      const [title, value, description] = found.node.texts;
      return value || title || description;
    }
    case 'select': {
      const found = located(pid, args[0]);
      if (!found) throw new Refused(`no text '${args[0]}' to select`);
      for (let at = found.index; at >= 0; at = found.all[at].parent) {
        const item = found.all[at];
        if (ax.settable(item.element, 'AXSelected')) {
          const status = ax.set(item.element, 'AXSelected', $.NSNumber.numberWithBool(true));
          if (status !== 0) throw new Refused(`the ${item.role} holding '${args[0]}' refused the selection (${status})`);
          // Avalonia accepts the selection of a disabled item and does nothing with it; a choice that moves the app on
          // takes its list away.
          const outcome = until(5, () => {
            if (ax.text(item.element, 'AXSelected') === '1') return '';
            return present(pid, args[0]) ? null : ', and it then left the window';
          });
          if (outcome === null) {
            const disabled = ax.text(item.element, 'AXEnabled') === '0' ? ', which is disabled,' : '';
            throw new Refused(`the ${item.role} holding '${args[0]}'${disabled} is not selected after it was chosen`);
          }
          return `selected '${args[0]}' through its ${item.role}${outcome}`;
        }
      }
      throw new Refused(`nothing holding '${args[0]}' can be selected`);
    }
    case 'select-nth': {
      const found = located(pid, args[0]);
      if (!found) throw new Refused(`no list '${args[0]}'`);
      const wanted = Number(args[1]);
      const item = until(10, () => {
        const items = walk(found.node.element, () => false).filter(node => node.role === 'AXRow');
        return items.length >= wanted ? items[wanted - 1] : null;
      });
      if (!item) throw new Refused(`'${args[0]}' holds fewer than ${wanted} items`);
      const status = ax.set(item.element, 'AXSelected', $.NSNumber.numberWithBool(true));
      if (status !== 0) throw new Refused(`item ${wanted} of '${args[0]}' refused the selection (${status})`);
      if (!until(5, () => (ax.text(item.element, 'AXSelected') === '1' ? true : null))) {
        throw new Refused(`item ${wanted} of '${args[0]}' is not selected after it was chosen`);
      }
      return `selected item ${wanted} of '${args[0]}'`;
    }
    case 'invoke':
    case 'toggle': {
      // Avalonia lists no actions on macOS, yet performs AXPress.
      const found = located(pid, args[0], node => PRESSABLE.test(node.role));
      if (!found) throw new Refused(`no button '${args[0]}'`);
      press(found.node, args[0]);
      return `${action === 'invoke' ? 'invoked' : 'toggled'} '${args[0]}'`;
    }
    case 'check': {
      const want = { on: '1', off: '0' }[args[1]];
      if (!want) throw new Refused(`check takes on or off, not '${args[1]}'`);
      const found = located(pid, args[0], node => TOGGLES.test(node.role) && /^[01]$/.test(node.attribute('AXValue')));
      if (!found) throw new Refused(`no check box or switch '${args[0]}'`);
      if (found.node.attribute('AXValue') !== want) {
        press(found.node, args[0]);
        if (!until(5, () => (ax.text(found.node.element, 'AXValue') === want ? true : null))) {
          throw new Refused(`'${args[0]}' reads ${ax.text(found.node.element, 'AXValue')} after it was pressed`);
        }
      }
      return `'${args[0]}' is ${args[1]}`;
    }
    case 'click':
    case 'right-click': {
      const found = located(pid, args[0]);
      if (!found) throw new Refused(`nothing named '${args[0]}' to click`);
      const point = click(pid, found.node, args[0], action === 'click' ? 'left' : 'right');
      return `${action === 'click' ? 'clicked' : 'right-clicked'} '${args[0]}' at ${point.x},${point.y}`;
    }
    case 'focus': {
      const found = located(pid, args[0]);
      if (!found) throw new Refused(`nothing named '${args[0]}' to focus`);
      front(pid);
      ax.set(found.node.element, 'AXFocused', $.NSNumber.numberWithBool(true));
      if (until(2, () => (focused(found.node) ? true : null))) return `focused '${args[0]}'`;
      // As a person does when the field does not take the focus it is given.
      const point = click(pid, found.node, args[0], 'left');
      if (until(3, () => (focused(found.node) ? true : null))) return `focused '${args[0]}' by a click at ${point.x},${point.y}`;
      throw new Refused(`'${args[0]}' did not take the focus, given or clicked at ${point.x},${point.y}`);
    }
    case 'set-after': {
      const found = located(pid, args[0], node => node.role === 'AXStaticText', 30, true);
      if (!found) throw new Refused(`no label '${args[0]}'`);
      const field = found.all.slice(found.index + 1).find(node => node.parent === found.node.parent && FIELDS.has(node.role));
      if (!field) throw new Refused(`no field after '${args[0]}'`);
      return setText(field, args[1], `the field after '${args[0]}'`);
    }
    case 'set-named': {
      // An AutoCompleteBox is an AXGroup whose value is its text.
      const found = located(pid, args[0], settableText);
      if (!found) throw new Refused(`no field named '${args[0]}'`);
      return setText(found.node, args[1], `the field named '${args[0]}'`);
    }
    case 'dialog':
      return JSON.stringify(dialog(args[0]));
    case 'press': {
      if (/trash/i.test(args[1])) throw new Refused(`'${args[1]}' is never pressed`);
      const owner = events.processes.byName(args[0]);
      const button = attempt(() => owner.windows(), []).map(w => attempt(() => w.buttons.byName(args[1]), null))
        .find(b => b && attempt(() => b.exists(), false));
      if (!button) throw new Refused(`${args[0]} shows no button '${args[1]}'`);
      button.click();
      return `pressed '${args[1]}' in ${args[0]}`;
    }
    default:
      throw new Refused(`no verb named ${action}`);
  }
}

function run(argv) {
  if (argv.length < 2) {
    print('usage: drive-desktop-macos.js <pid> <verb> [args...]');
    $.exit(1);
  }
  const pid = Number(argv[0]);
  try {
    print(act(pid, argv[1], argv.slice(2)));
  } catch (error) {
    if (error instanceof Unimplemented) {
      print(`${error.message} not implemented on macos`);
      $.exit(2);
    }
    const said = error instanceof Refused ? error.message : `${argv[1]} failed: ${error.message}`;
    print(said + attempt(() => keepTree(pid, argv[1]), ''));
    $.exit(1);
  }
  $.exit(0);
}
