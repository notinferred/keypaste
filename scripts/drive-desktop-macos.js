// 4.7e, R.1a: drives an installed keypaste desktop app from outside its process through the macOS accessibility API,
// System Events' keystrokes, and screencapture (D-0199).
//
// One verb per call, as scripts/lib/desktop-drive.sh defines them; prints what it saw and exits 1 when the verb could
// not be done, 2 when this driver does not implement it. DRIVE_WINDOW names the app's window to act in. Needs
// Accessibility and Apple Events to System Events for the process that runs osascript, which a hosted runner grants.
//
// Usage: osascript -l JavaScript drive-desktop-macos.js <pid> <verb> [args...]
//        osascript -l JavaScript drive-desktop-macos.js - dialog|press <process-name> [button]
ObjC.import('Foundation');
ObjC.import('AppKit');
ObjC.import('CoreGraphics');
ObjC.import('ApplicationServices');
ObjC.import('stdlib');

const TITLE = ObjC.unwrap($.NSProcessInfo.processInfo.environment.objectForKey($('DRIVE_WINDOW'))) || 'keypaste';
const UNIMPLEMENTED = new Set(['windows', 'key-wait', 'wait', 'wait-gone', 'read', 'select-nth', 'check', 'click', 'right-click', 'focus']);
const DEPTH = 40;
const LIMIT = 4000;
const FIELDS = new Set(['AXTextField', 'AXTextArea']);
const events = Application('System Events');
const shell = Application.currentApplication();
shell.includeStandardAdditions = true;

class Refused extends Error {}

class Unimplemented extends Error {}

function print(text) {
  $.NSFileHandle.fileHandleWithStandardOutput.writeData($(`${text}\n`).dataUsingEncoding($.NSUTF8StringEncoding));
}

function until(seconds, probe) {
  const deadline = Date.now() + seconds * 1000;
  for (;;) {
    const found = probe();
    if (found !== null && found !== undefined) return found;
    if (Date.now() >= deadline) return null;
    delay(0.25);
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
};

function window(pid) {
  const app = $.AXUIElementCreateApplication(pid);
  $.AXUIElementSetMessagingTimeout(app, 5);
  return ax.children(app).find(w => ax.text(w, 'AXRole') === 'AXWindow' && ax.text(w, 'AXTitle') === TITLE) || null;
}

function nodes(root) {
  const out = [];
  const visit = (element, depth, parent) => {
    if (depth > DEPTH || out.length >= LIMIT) return;
    for (const child of ax.children(element)) {
      if (out.length >= LIMIT) return;
      out.push({
        element: child, depth, parent,
        role: ax.text(child, 'AXRole'), title: ax.text(child, 'AXTitle'), value: ax.text(child, 'AXValue'), description: ax.text(child, 'AXDescription'),
      });
      visit(child, depth + 1, out.length - 1);
    }
  };
  visit(root, 0, -1);
  return out;
}

function texts(node) {
  return [node.title, node.value, node.description];
}

// A locator is a name, or id:<automation id>, either with [n] for the nth match in tree order.
function locate(locator) {
  const counted = /^(.*)\[(\d+)\]$/.exec(locator);
  const name = counted ? counted[1] : locator;
  if (name.startsWith('id:')) throw new Unimplemented('the id: locator');
  return { name, index: counted ? Number(counted[2]) : 1 };
}

function find(pid, predicate, seconds = 60, index = 1) {
  return until(seconds, () => {
    const frame = window(pid);
    if (!frame) return null;
    const all = nodes(frame);
    let seen = 0;
    const at = all.findIndex(node => predicate(node) && ++seen === index);
    return at < 0 ? null : { node: all[at], all, index: at };
  });
}

function named(label) {
  return node => texts(node).includes(label);
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
  const read = ax.text(node.element, 'AXValue');
  if (read !== value) throw new Refused(`${what} reads '${read}'`);
  return `set ${what}`;
}

function windowNumber(pid) {
  const listed = ObjC.deepUnwrap(ObjC.castRefToObject($.CGWindowListCopyWindowInfo(1 | 16, 0))) || [];
  const own = listed.filter(w => w.kCGWindowOwnerPID === pid && w.kCGWindowLayer === 0);
  return own.length ? own[0].kCGWindowNumber : null;
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

// The element a verb's locator names, among those the predicate accepts.
function located(pid, locator, predicate) {
  const { name, index } = locate(locator);
  return find(pid, node => named(name)(node) && predicate(node), 60, index);
}

function act(pid, action, args) {
  if (UNIMPLEMENTED.has(action)) throw new Unimplemented(`the verb ${action}`);
  switch (action) {
    case 'window': {
      const frame = until(Number(args[0]), () => window(pid));
      if (!frame) throw new Refused(`no window named ${TITLE} for process ${pid} within ${args[0]} s`);
      return `window '${TITLE}' of process ${pid}`;
    }
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
    case 'key': {
      front(pid);
      const chord = /^(cmd|ctrl)\+(.)$/.exec(args[0]);
      if (args[0] === 'Return') events.keyCode(36);
      else if (chord) events.keystroke(chord[2], { using: chord[1] === 'cmd' ? 'command down' : 'control down' });
      else throw new Unimplemented(`the chord ${args[0]}`);
      return `sent ${args[0]}`;
    }
    case 'find': {
      const found = located(pid, args[0], () => true);
      if (!found) throw new Refused(`nothing named '${args[0]}' appeared`);
      return `found '${args[0]}' as ${found.node.role}`;
    }
    case 'find-prefix': {
      const found = find(pid, node => texts(node).some(t => t.startsWith(args[0])));
      if (!found) throw new Refused(`nothing starting '${args[0]}' appeared`);
      return texts(found.node).find(t => t.startsWith(args[0]));
    }
    case 'select': {
      const found = located(pid, args[0], () => true);
      if (!found) throw new Refused(`no text '${args[0]}' to select`);
      for (let at = found.index; at >= 0; at = found.all[at].parent) {
        const item = found.all[at];
        if (ax.settable(item.element, 'AXSelected')) {
          const status = ax.set(item.element, 'AXSelected', $.NSNumber.numberWithBool(true));
          if (status !== 0) throw new Refused(`the ${item.role} holding '${args[0]}' refused the selection (${status})`);
          return `selected '${args[0]}' through its ${item.role}`;
        }
      }
      throw new Refused(`nothing holding '${args[0]}' can be selected`);
    }
    case 'invoke':
    case 'toggle': {
      // Avalonia lists no actions on macOS, yet performs AXPress.
      const found = located(pid, args[0], node => /Button|CheckBox/.test(node.role));
      if (!found) throw new Refused(`no button '${args[0]}'`);
      const status = $.AXUIElementPerformAction(found.node.element, $('AXPress'));
      if (status !== 0) throw new Refused(`'${args[0]}' refused AXPress (${status})`);
      return `invoked '${args[0]}'`;
    }
    case 'set-after': {
      const found = located(pid, args[0], node => node.role === 'AXStaticText');
      if (!found) throw new Refused(`no label '${args[0]}'`);
      const field = found.all.slice(found.index + 1).find(node => node.parent === found.node.parent && FIELDS.has(node.role));
      if (!field) throw new Refused(`no field after '${args[0]}'`);
      return setText(field, args[1], `the field after '${args[0]}'`);
    }
    case 'set-named': {
      const found = located(pid, args[0], node => FIELDS.has(node.role));
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
  try {
    print(act(Number(argv[0]), argv[1], argv.slice(2)));
  } catch (error) {
    if (error instanceof Unimplemented) {
      print(`${error.message} not implemented on macos`);
      $.exit(2);
    }
    print(error instanceof Refused ? error.message : `${argv[1]} failed: ${error.message}`);
    $.exit(1);
  }
  $.exit(0);
}
