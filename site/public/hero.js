const canvas = document.getElementById('hero-field');
const context = canvas.getContext('2d');
const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
const colorScheme = window.matchMedia('(prefers-color-scheme: light)');

const SOURCES = [
  { x: 0.1, y: 0.3, label: 'password manager' },
  { x: 0.26, y: 0.78, label: '.env files' },
  { x: 0.5, y: 0.9, label: 'team sharing' },
  { x: 0.74, y: 0.78, label: 'CI variables' },
  { x: 0.9, y: 0.3, label: 'secrets platform' },
];
const SIDE = 11;
const CYCLE_SECONDS = 12;
const LAST_DELAY = 0.35;

let seed = 7;
const random = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
const dots = Array.from({ length: SIDE * SIDE }, (_, index) => ({
  source: SOURCES[index % SOURCES.length],
  column: index % SIDE,
  row: Math.floor(index / SIDE),
  radius: 8 + random() * 30,
  angle: random() * Math.PI * 2,
  speed: 0.2 + random() * 0.5,
  delay: random() * LAST_DELAY,
}));

const ease = (t) => (t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t));

let colors;
const readColors = () => {
  const style = getComputedStyle(document.documentElement);
  colors = {
    dot: style.getPropertyValue('--text-secondary'),
    label: style.getPropertyValue('--text-muted'),
    mark: style.getPropertyValue('--mark-dot'),
  };
};

// 0 while the five groups sit apart, 1 while they hold together as one grid.
const gatheredAt = (seconds) => {
  const t = seconds % CYCLE_SECONDS;
  if (t < 3) return 0;
  if (t < 6) return (t - 3) / 3;
  if (t < 10) return 1;
  return 1 - (t - 10) / 2;
};

const draw = (seconds, gathered) => {
  const width = canvas.clientWidth;
  const height = canvas.clientHeight;
  const ratio = window.devicePixelRatio || 1;
  if (canvas.width !== Math.round(width * ratio) || canvas.height !== Math.round(height * ratio)) {
    canvas.width = Math.round(width * ratio);
    canvas.height = Math.round(height * ratio);
  }
  context.setTransform(ratio, 0, 0, ratio, 0, 0);
  context.clearRect(0, 0, width, height);

  const narrow = width < 640;
  const gap = narrow ? 8 : 12;
  const left = width / 2 - ((SIDE - 1) * gap) / 2;
  const top = height * 0.42 - ((SIDE - 1) * gap) / 2;
  const spread = narrow ? 0.6 : 1;

  context.font = `${narrow ? 10 : 12}px "Fragment Mono", ui-monospace, monospace`;
  context.textAlign = 'center';
  context.fillStyle = colors.label;
  context.globalAlpha = 1 - ease(gathered * 1.6);
  for (const source of SOURCES) {
    context.fillText(source.label, 0.5 * width + (source.x - 0.5) * width * (narrow ? 0.82 : 1), source.y * height - 40 * spread - 8);
  }

  context.fillStyle = colors.dot;
  for (const dot of dots) {
    const angle = dot.angle + seconds * dot.speed;
    const homeX = 0.5 * width + (dot.source.x - 0.5) * width * (narrow ? 0.82 : 1) + Math.cos(angle) * dot.radius * spread;
    const homeY = dot.source.y * height + Math.sin(angle) * dot.radius * spread * 0.7;
    const arrived = ease((gathered - dot.delay) / (1 - LAST_DELAY));
    context.globalAlpha = 0.55 + 0.4 * arrived;
    context.fillRect(
      homeX + (left + dot.column * gap - homeX) * arrived - 1.5,
      homeY + (top + dot.row * gap - homeY) * arrived - 1.5,
      3,
      3,
    );
  }

  const settled = ease((gathered - 0.85) / 0.15);
  if (settled > 0) {
    context.globalAlpha = settled;
    context.fillStyle = colors.mark;
    context.fillRect(left + SIDE * gap, top + (SIDE - 1) * gap - 3, 7, 7);
    context.fillStyle = colors.label;
    context.fillText('keypaste', width / 2, top + SIDE * gap + 22);
  }
  context.globalAlpha = 1;
};

let frame = 0;
let visible = true;
const tick = (now) => {
  draw(now / 1000, gatheredAt(now / 1000));
  frame = visible ? requestAnimationFrame(tick) : 0;
};
const restart = () => {
  cancelAnimationFrame(frame);
  frame = 0;
  readColors();
  if (reducedMotion.matches) return draw(0, 1);
  draw(0, 0);
  if (visible) frame = requestAnimationFrame(tick);
};

new IntersectionObserver(([entry]) => {
  visible = entry.isIntersecting;
  if (visible && !frame) restart();
}).observe(canvas);
reducedMotion.addEventListener('change', restart);
colorScheme.addEventListener('change', restart);
window.addEventListener('resize', () => reducedMotion.matches && draw(0, 1));
document.fonts.ready.then(restart);
