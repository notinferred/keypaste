// The home page's story: one field of dots behind every chapter, gathering, passing a gate, branching, regrouping and
// tunnelling as the page scrolls. Each chapter draws its formation on a 480 by 360 stage placed beside its words.

const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
const colorScheme = window.matchMedia('(prefers-color-scheme: light)');
const story = document.querySelector('.story');
const canvas = story.querySelector('.story-field');
const context = canvas.getContext('2d');
const chapters = [...story.querySelectorAll('[data-chapter]')];

const MAX_PIXEL_RATIO = 2;
const TAU = Math.PI * 2;
const DOTS = 144;
const PHONE_DOTS = 81;

const ease = (t) => (t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t));
const span = (t, start, length) => ease((t - start) / length);
const lerp = (from, to, p) => from + (to - from) * p;
const seeded = (seed) => () => (seed = (seed * 16807) % 2147483647) / 2147483647;
const curve = (from, via, to, p) => ({
  x: (1 - p) * (1 - p) * from.x + 2 * (1 - p) * p * via.x + p * p * to.x,
  y: (1 - p) * (1 - p) * from.y + 2 * (1 - p) * p * via.y + p * p * to.y,
});

const seeds = (() => {
  const random = seeded(7);
  return Array.from({ length: DOTS }, () => ({
    radius: random(),
    angle: random() * TAU,
    speed: 0.15 + random() * 0.4,
    delay: random(),
    x: random(),
    y: random(),
  }));
})();

let colors;
const readColors = () => {
  const style = getComputedStyle(document.documentElement);
  const read = (name) => style.getPropertyValue(name).trim();
  colors = {
    dot: read('--text-secondary'),
    label: read('--text-muted'),
    line: read('--border-strong'),
    accent: read('--accent-ring'),
    accentText: read('--accent-text'),
    mark: read('--mark-dot'),
    sans: read('--font-sans'),
    mono: read('--font-mono'),
  };
};

// Where a chapter's stage sits: beside its words on a wide screen, above them on a narrow one.
const placeFor = (side, view) => {
  const { width, height } = view;
  if (view.narrow) return { x: width / 2, y: height * 0.27, scale: Math.min((width * 0.94) / 480, (height * 0.4) / 360) };
  const scale = Math.min((width * 0.42) / 480, (height * 0.68) / 360);
  if (side === 'top') return { x: width / 2, y: height * 0.27, scale: scale * 0.55 };
  return { x: side === 'right' ? width * 0.73 : width * 0.27, y: height * 0.5, scale };
};
const at = (place, x, y) => ({ x: place.x + (x - 240) * place.scale, y: place.y + (y - 180) * place.scale });
const atPoint = (place, point) => at(place, point.x, point.y);

const paint = (c, color, alpha) => {
  c.fillStyle = color;
  c.strokeStyle = color;
  c.globalAlpha = Math.max(0, alpha);
};
const square = (c, point, size) => c.fillRect(point.x - size / 2, point.y - size / 2, size, size);
const circle = (c, point, radius) => {
  c.beginPath();
  c.arc(point.x, point.y, radius, 0, TAU);
  c.fill();
};
const words = (c, view, text, point, color, alpha, mono = false) => {
  if (alpha <= 0) return;
  c.font = `${view.narrow ? 12 : 13}px ${mono ? colors.mono : colors.sans}`;
  c.textAlign = 'center';
  paint(c, color, alpha);
  c.fillText(text, point.x, point.y);
};
const dotted = (c, place, from, to, step, alpha, via = null) => {
  const length = Math.hypot(to.x - from.x, to.y - from.y);
  const count = Math.max(1, Math.round(length / step));
  paint(c, colors.line, alpha);
  for (let i = 0; i <= count; i++) {
    const p = i / count;
    const point = via ? curve(from, via, to, p) : { x: lerp(from.x, to.x, p), y: lerp(from.y, to.y, p) };
    square(c, atPoint(place, point), 1.6);
  }
};
const outline = (c, place, x, y, width, height, radius, alpha, dash = []) => {
  const corner = at(place, x, y);
  paint(c, colors.line, alpha);
  c.lineWidth = 1.2;
  c.setLineDash(dash);
  c.beginPath();
  c.roundRect(corner.x, corner.y, width * place.scale, height * place.scale, radius * place.scale);
  c.stroke();
  c.setLineDash([]);
};
// DESIGN's waiting state: an accent dot with a halo.
const waiting = (c, point, alpha) => {
  paint(c, colors.accent, alpha * 0.22);
  circle(c, point, 7);
  paint(c, colors.accent, alpha);
  circle(c, point, 3.5);
};
const pulse = (c, point, p, reach, color, alpha) => {
  if (p <= 0 || p >= 1) return;
  c.lineWidth = 1.2;
  paint(c, color, 0.7 * (1 - p) * alpha);
  c.beginPath();
  c.arc(point.x, point.y, 4 + p * reach, 0, TAU);
  c.stroke();
};
const inGrid = (index, count, x, y, gap) => {
  const columns = Math.ceil(Math.sqrt(count));
  const rows = Math.ceil(count / columns);
  return { x: x + ((index % columns) - (columns - 1) / 2) * gap, y: y + (Math.floor(index / columns) - (rows - 1) / 2) * gap };
};
const orbit = (seed, x, y, t, inner, outer) => {
  const angle = seed.angle + t * seed.speed;
  const radius = inner + seed.radius * (outer - inner);
  return { x: x + Math.cos(angle) * radius, y: y + Math.sin(angle) * radius * 0.75 };
};
const dot = (point, alpha = 0.9, role = 'dot', size = 1) => ({ ...point, alpha, role, size });

// 0. Scattered: five groups of secrets kept in five different places.

const SOURCES = {
  wide: [
    { x: 0.1, y: 0.4, label: 'password manager' },
    { x: 0.9, y: 0.4, label: 'secrets platform' },
    { x: 0.22, y: 0.74, label: '.env files' },
    { x: 0.78, y: 0.74, label: 'CI variables' },
    { x: 0.5, y: 0.8, label: 'team chat' },
  ],
  narrow: [
    { x: 0.12, y: 0.74, label: 'passwords' },
    { x: 0.88, y: 0.74, label: 'secrets' },
    { x: 0.31, y: 0.74, label: '.env' },
    { x: 0.69, y: 0.74, label: 'CI' },
    { x: 0.5, y: 0.74, label: 'chat' },
  ],
};
const sourcesFor = (view) => (view.narrow ? SOURCES.narrow : SOURCES.wide);

const scattered = (i, n, t, place, view) => {
  const source = sourcesFor(view)[i % 5];
  const spread = view.narrow ? 0.55 : 1;
  const point = orbit(seeds[i], source.x * view.width, source.y * view.height, t, 8 * spread, 42 * spread);
  return dot(point, view.narrow ? 0.4 : 0.6);
};
const scatteredLabels = (c, t, place, view, weight) => {
  const spread = view.narrow ? 0.55 : 1;
  for (const source of sourcesFor(view)) {
    words(c, view, source.label, { x: source.x * view.width, y: source.y * view.height - 50 * spread }, colors.label, weight * weight);
  }
};

// 1. One place you own: every dot settles into one square, the keypaste mark.

const VAULT = { x: 240, y: 172, width: 156 };
const gridGap = (n, width) => width / (Math.ceil(Math.sqrt(n)) - 1);
const vaultPoint = (i, n) => inGrid(i, n, VAULT.x, VAULT.y, gridGap(n, VAULT.width));
const gathered = (i, n, t, place) => dot(atPoint(place, vaultPoint(i, n)), 0.82 + 0.1 * Math.sin(t * 0.8 + i));
const markDot = (c, place, n, width, alpha) => {
  const half = width / 2;
  paint(c, colors.mark, alpha);
  square(c, at(place, VAULT.x + half + gridGap(n, width) + 1, VAULT.y + half), Math.max(5, 6 * place.scale));
};
const vaultLabels = (c, t, place, view, weight, n) => {
  markDot(c, place, n, VAULT.width, weight);
  words(c, view, 'vault.kdbx', at(place, VAULT.x, VAULT.y + VAULT.width / 2 + 34), colors.label, weight * weight, true);
};

// 2. Agents: a request waits at your gate; on Allow once one field passes back, and a refusal passes nothing.

const GATE = { x: 250, y: 176 };
const ASK_SECONDS = 4;
const keptFor = (n) => Math.floor(Math.sqrt(n * 0.56)) ** 2;
const vaultCell = (index, n) => inGrid(index, keptFor(n), 112, 176, n > 100 ? 10 : 12);

const asking = (i, n, t, place) => {
  const kept = keptFor(n);
  if (i < kept) return dot(atPoint(place, vaultCell(i, n)));
  return dot(atPoint(place, orbit(seeds[i], 384, 176, t, 10, 50)), 0.7);
};
const gate = (c, t, place, view, weight, n) => {
  for (let y = 116; y <= 156; y += 6) {
    paint(c, colors.line, weight);
    square(c, at(place, GATE.x, y), 2);
    square(c, at(place, GATE.x, 2 * GATE.y - y), 2);
  }
  const round = Math.floor(t / ASK_SECONDS);
  const u = t % ASK_SECONDS;
  const allowed = round % 3 !== 2;
  const waitAt = at(place, GATE.x + 14, GATE.y);
  if (u < 1) {
    paint(c, colors.dot, weight * span(u, 0, 0.2));
    square(c, at(place, lerp(346, GATE.x + 14, span(u, 0, 1)), GATE.y), 3);
  } else if (u < 1.6) {
    waiting(c, waitAt, weight);
  } else if (!allowed) {
    const fall = span(u, 1.6, 0.8);
    paint(c, colors.label, weight * (1 - fall));
    square(c, { x: waitAt.x + fall * 8, y: waitAt.y + fall * 24 * place.scale }, 3);
  }
  pulse(c, at(place, GATE.x, GATE.y), span(u, 1.6, 0.8), 24 * place.scale, allowed ? colors.accent : colors.label, weight);
  if (allowed && u >= 1.8 && u < 3.4) {
    const cell = vaultCell((round * 11 + 5) % keptFor(n), n);
    const across = span(u, 1.8, 1.4);
    paint(c, colors.accent, weight * (1 - span(u, 3.1, 0.3)));
    square(c, at(place, lerp(cell.x, 350, across), lerp(cell.y, GATE.y, across)), 4);
  }
  const said = span(u, 1.65, 0.2) * (1 - span(u, 3.3, 0.4));
  words(c, view, allowed ? 'Allow once' : 'Deny', at(place, GATE.x, 100), allowed ? colors.accentText : colors.label, weight * said);
  words(c, view, 'your vault', at(place, 112, 240), colors.label, weight * weight);
  words(c, view, 'you', at(place, GATE.x, 262), colors.label, weight * weight);
  words(c, view, 'AI agent', at(place, 384, 240), colors.label, weight * weight);
};

// 3. Sharing: one project branches to each teammate, its key travelling to each, never a loose copy.

const TEAM_Y = [92, 176, 260];
const memberSide = (n) => (n > 100 ? 4 : 3);

const shared = (i, n, t, place) => {
  const side = memberSide(n);
  const own = n - 3 * side * side;
  if (i < own) return dot(atPoint(place, inGrid(i, own, 112, 176, n > 100 ? 9 : 11)));
  const j = i - own;
  const member = side * side;
  return dot(atPoint(place, inGrid(j % member, member, 392, TEAM_Y[Math.floor(j / member)], n > 100 ? 8 : 10)));
};
const branches = (c, t, place, view, weight) => {
  TEAM_Y.forEach((y, k) => {
    const from = { x: 168, y: 176 };
    const via = { x: 290, y };
    const to = { x: 362, y };
    dotted(c, place, from, to, 7, weight * 0.8, via);
    const phase = (t * 0.32 + k / 3) % 1;
    paint(c, colors.accent, weight * Math.sin(Math.PI * phase));
    square(c, atPoint(place, curve(from, via, to, phase)), 4);
  });
  words(c, view, 'you', at(place, 112, 244), colors.label, weight * weight);
  words(c, view, 'your team', at(place, 392, 314), colors.label, weight * weight);
};

// 4. The organization: its projects above its members; one member leaves and the key is wrapped again without them.

const MEMBER_Y = 244;
const ORG_SECONDS = 9;
const leavingAt = (t) => {
  const u = t % ORG_SECONDS;
  return span(u, 3.5, 1.2) * (1 - span(u, 7.4, 1));
};
const organized = (i, n, t, place) => {
  const projectSide = n > 100 ? 8 : 6;
  const side = memberSide(n);
  const projects = projectSide * projectSide;
  if (i < projects) return dot(atPoint(place, inGrid(i, projects, 240, 116, n > 100 ? 8 : 10)));
  const j = i - projects;
  const member = Math.floor(j / (side * side));
  const point = inGrid(j % (side * side), side * side, 80 + member * 80, MEMBER_Y, n > 100 ? 7 : 9);
  const leaving = member === 4 ? leavingAt(t) : 0;
  return dot(at(place, point.x + leaving * 66, point.y), 0.85 * (1 - 0.65 * leaving));
};
const organization = (c, t, place, view, weight) => {
  outline(c, place, 28, 44, 424, 262, 14, weight * 0.8, [4, 5]);
  c.font = `${view.narrow ? 12 : 13}px ${colors.sans}`;
  c.textAlign = 'left';
  paint(c, colors.label, weight * weight);
  const corner = at(place, 46, 68);
  c.fillText('organization', corner.x, corner.y);
  const leaving = leavingAt(t);
  for (let member = 0; member < 5; member++) {
    dotted(c, place, { x: 240, y: 152 }, { x: 80 + member * 80, y: MEMBER_Y - 24 }, 7, weight * 0.7 * (member === 4 ? 1 - leaving : 1));
  }
  pulse(c, at(place, 240, 116), span(t % ORG_SECONDS, 4.7, 1.1), 56 * place.scale, colors.accent, weight);
};

// 5. Servers, a direction being explored: a person reaches one server through a narrow lane instead of a handed-out config.

const LANE = { from: 150, to: 356, y: 176 };
const reaching = (i, n, t, place) => {
  const lane = n > 100 ? 28 : 14;
  const people = n > 100 ? 30 : 18;
  if (i < lane) {
    const phase = (t * 0.22 + i / lane) % 1;
    return dot(at(place, lerp(LANE.from, LANE.to, phase), LANE.y + (seeds[i].y - 0.5) * 14), 0.9 * Math.sqrt(Math.sin(Math.PI * phase)));
  }
  if (i < lane + people) return dot(atPoint(place, orbit(seeds[i], 96, 176, t, 6, 30)), 0.8);
  const seed = seeds[i];
  return dot(at(place, 20 + seed.x * 440 + Math.sin(t * 0.2 + i) * 6, 30 + seed.y * 300 + Math.cos(t * 0.2 + i) * 6), 0.16, 'dot', 0.7);
};
const server = (c, t, place, view, weight) => {
  dotted(c, place, { x: LANE.from - 10, y: 162 }, { x: LANE.to, y: 162 }, 7, weight * 0.6);
  dotted(c, place, { x: LANE.from - 10, y: 190 }, { x: LANE.to, y: 190 }, 7, weight * 0.6);
  for (const y of [128, 162, 196]) {
    outline(c, place, 362, y, 76, 26, 5, weight);
    paint(c, colors.dot, weight);
    square(c, at(place, 374, y + 13), 3);
  }
  words(c, view, 'you', at(place, 96, 226), colors.label, weight * weight);
  words(c, view, 'server', at(place, 400, 250), colors.label, weight * weight);
};

// 6. The close: the square again, small, over the last words.

const closed = (i, n, t, place) => dot(atPoint(place, vaultPoint(i, n)), 0.9);
const closeMark = (c, t, place, view, weight, n) => markDot(c, place, n, VAULT.width, weight);

// Each chapter's formation, the drawing around it, the side of the page it takes and the moment its still shows.
const CHAPTERS = [
  { form: scattered, around: scatteredLabels, side: 'wide', still: 1 },
  { form: gathered, around: vaultLabels, side: 'right', still: 0 },
  { form: asking, around: gate, side: 'left', still: 2.6 },
  { form: shared, around: branches, side: 'right', still: 1.2 },
  { form: organized, around: organization, side: 'left', still: 6.4 },
  { form: reaching, around: server, side: 'right', still: 2 },
  { form: closed, around: closeMark, side: 'top', still: 0 },
];

// How far the page has scrolled between two chapters' middles; a formation holds for the first and last fifth.
const position = () => {
  const top = canvas.getBoundingClientRect().top;
  const middle = canvas.clientHeight / 2;
  const centers = chapters.map((chapter) => {
    const box = chapter.getBoundingClientRect();
    return box.top - top + box.height / 2;
  });
  const last = centers.length - 1;
  if (middle <= centers[0]) return { from: 0, to: 0, blend: 0 };
  for (let k = 0; k < last; k++) {
    if (middle < centers[k + 1]) {
      const p = ease(((middle - centers[k]) / (centers[k + 1] - centers[k]) - 0.2) / 0.6);
      return { from: k, to: k + 1, blend: reducedMotion.matches ? Math.round(p) : p };
    }
  }
  return { from: last, to: last, blend: 0 };
};

const render = (seconds) => {
  const width = canvas.clientWidth;
  const height = canvas.clientHeight;
  const ratio = Math.min(window.devicePixelRatio || 1, MAX_PIXEL_RATIO);
  if (canvas.width !== Math.round(width * ratio) || canvas.height !== Math.round(height * ratio)) {
    canvas.width = Math.round(width * ratio);
    canvas.height = Math.round(height * ratio);
  }
  context.setTransform(ratio, 0, 0, ratio, 0, 0);
  context.clearRect(0, 0, width, height);

  const view = { width, height, narrow: width < 900 };
  const n = width < 760 ? PHONE_DOTS : DOTS;
  const { from, to, blend } = position();
  const [a, b] = [CHAPTERS[from], CHAPTERS[to]];
  const [timeA, timeB] = reducedMotion.matches ? [a.still, b.still] : [seconds, seconds];
  const [placeA, placeB] = [placeFor(a.side, view), placeFor(b.side, view)];

  a.around(context, timeA, placeA, view, 1 - blend, n);
  if (to !== from) b.around(context, timeB, placeB, view, blend, n);

  const size = width < 760 ? 2.5 : 3;
  const fade = view.narrow ? 0.8 : 1;
  for (let i = 0; i < n; i++) {
    const start = a.form(i, n, timeA, placeA, view);
    const end = to === from ? start : b.form(i, n, timeB, placeB, view);
    const p = ease((blend - seeds[i].delay * 0.35) / 0.65);
    const point = { x: lerp(start.x, end.x, p), y: lerp(start.y, end.y, p) };
    paint(context, colors[p < 0.5 ? start.role : end.role], lerp(start.alpha, end.alpha, p) * fade);
    square(context, point, size * lerp(start.size, end.size, p));
  }
  context.globalAlpha = 1;
};

let frame = 0;
let visible = false;
const began = performance.now();
const tick = (now) => {
  render((now - began) / 1000);
  frame = visible ? requestAnimationFrame(tick) : 0;
};
// Under reduced motion nothing moves on its own: each chapter's still is drawn as the page is scrolled to it.
let pending = 0;
const drawStill = () => {
  if (!pending) pending = requestAnimationFrame(() => (pending = 0, render(0)));
};
const restart = () => {
  readColors();
  if (reducedMotion.matches) {
    cancelAnimationFrame(frame);
    frame = 0;
    return drawStill();
  }
  if (visible && !frame) frame = requestAnimationFrame(tick);
};

readColors();
new IntersectionObserver(([entry]) => {
  visible = entry.isIntersecting;
  if (visible) restart();
}).observe(story);
window.addEventListener('scroll', () => reducedMotion.matches && drawStill(), { passive: true });
window.addEventListener('resize', () => reducedMotion.matches && drawStill());
reducedMotion.addEventListener('change', restart);
colorScheme.addEventListener('change', restart);
document.fonts.ready.then(restart);
