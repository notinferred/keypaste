#!/usr/bin/env node
'use strict';

// Draws src/icons/ from the brand's tiles in assets/brand/: the mark as SVG and PNG, and each toolbar state as the
// favicon cut with a badge. Sizes up to 32 px use the favicon cut, as scripts/render-app-icon.py does. Needs only Node.

const fs = require('node:fs');
const path = require('node:path');
const zlib = require('node:zlib');

const BRAND = path.join(__dirname, '..', 'assets', 'brand');
const ICONS = path.join(__dirname, 'src', 'icons');
const MARK_SIZES = [ 16, 32, 48, 96, 128 ];
const STATE_SIZES = [ 16, 32 ];
const SUPERSAMPLE = 16;
const INK = '#111214';

const BADGE = { cx: 49, cy: 49, r: 12, ring: 14.5 };

const rect = (x1, y1, x2, y2) => [ [ x1, y1 ], [ x2, y1 ], [ x2, y2 ], [ x1, y2 ] ];

const bar = function(angle, length, width) {
    const [ c, s ] = [ Math.cos(angle), Math.sin(angle) ];
    return [ [ -length, -width ], [ length, -width ], [ length, width ], [ -length, width ] ]
        .map(([ x, y ]) => [ BADGE.cx + x * c - y * s, BADGE.cy + x * s + y * c ]);
};

const arc = function(cx, cy, r, from, to, steps = 16) {
    return Array.from({ length: steps + 1 }, (_, i) => {
        const angle = from + (to - from) * i / steps;
        return [ cx + r * Math.cos(angle), cy - r * Math.sin(angle) ];
    });
};

const shackle = [
    ...arc(49, 45.5, 4.5, Math.PI, 0), [ 53.5, 48 ], [ 51.5, 48 ],
    ...arc(49, 45.5, 2.5, 0, Math.PI), [ 46.5, 48 ], [ 44.5, 48 ]
];

// Each state's badge fill and glyph, its shape saying the state as well as its colour.
const STATES = {
    locked: { fill: '#F2F2F0', glyph: [ shackle, rect(43, 48, 55, 57) ] },
    disconnected: { fill: '#F7857D', glyph: [ bar(Math.PI / 4, 6.5, 1.75), bar(-Math.PI / 4, 6.5, 1.75) ] },
    unpaired: { fill: '#F2B544', glyph: [ rect(47.25, 40.5, 50.75, 51), rect(47.25, 53.5, 50.75, 57) ] },
};

const contours = function(d) {
    const tokens = d.match(/[MLHVZ]|-?[0-9.]+/g);
    const shapes = [];
    let points = [];
    for (let i = 0; i < tokens.length;) {
        const command = tokens[i++];
        const here = points[points.length - 1];
        if (command === 'M') {
            points = [ [ +tokens[i++], +tokens[i++] ] ];
        } else if (command === 'L') {
            points.push([ +tokens[i++], +tokens[i++] ]);
        } else if (command === 'H') {
            points.push([ +tokens[i++], here[1] ]);
        } else if (command === 'V') {
            points.push([ here[0], +tokens[i++] ]);
        } else if (command === 'Z') {
            shapes.push(points);
            points = [];
        } else {
            throw new Error(`The brand outline uses ${command}, which this script does not draw`);
        }
    }
    return shapes;
};

const tile = function(name) {
    const svg = fs.readFileSync(path.join(BRAND, name), 'utf8');
    const radius = Number(/<rect [^>]*rx="([0-9.]+)"/.exec(svg)[1]);
    const fill = /<rect [^>]*fill="(#[0-9A-F]+)"/i.exec(svg)[1];
    const paths = [ ...svg.matchAll(/<path fill="(#[0-9A-F]+)" d="([^"]+)"\/>/gi) ].map(([ , color, d ]) => ({ fill: color, d }));
    return { radius, fill, paths };
};

// A design is its tile and its layers, each a fill and even-odd contours or a circle, painted in order.
const design = function(base, state) {
    const layers = base.paths.map(({ fill, d }) => ({ fill, contours: contours(d) }));
    if (state) {
        layers.push({ fill: base.fill, circle: { cx: BADGE.cx, cy: BADGE.cy, r: BADGE.ring } });
        layers.push({ fill: state.fill, circle: { cx: BADGE.cx, cy: BADGE.cy, r: BADGE.r } });
        for (const contour of state.glyph) {
            layers.push({ fill: INK, contours: [ contour ] });
        }
    }
    return { radius: base.radius, fill: base.fill, layers };
};

const insideTile = function(x, y, r) {
    const dx = Math.max(r - x, x - (64 - r), 0);
    const dy = Math.max(r - y, y - (64 - r), 0);
    return x >= 0 && x <= 64 && y >= 0 && y <= 64 && dx * dx + dy * dy <= r * r;
};

const insideContours = function(x, y, shapes) {
    let inside = false;
    for (const shape of shapes) {
        for (let i = 0, j = shape.length - 1; i < shape.length; j = i++) {
            const [ xi, yi ] = shape[i];
            const [ xj, yj ] = shape[j];
            if ((yi > y) !== (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) {
                inside = !inside;
            }
        }
    }
    return inside;
};

const inside = function(layer, x, y) {
    if (layer.circle) {
        const { cx, cy, r } = layer.circle;
        return (x - cx) ** 2 + (y - cy) ** 2 <= r * r;
    }
    return insideContours(x, y, layer.contours);
};

const rgb = (hex) => [ 1, 3, 5 ].map((i) => parseInt(hex.slice(i, i + 2), 16));

const render = function(art, size) {
    const pixels = Buffer.alloc(size * size * 4);
    const step = 64 / size / SUPERSAMPLE;
    for (let py = 0; py < size; py++) {
        for (let px = 0; px < size; px++) {
            const sum = [ 0, 0, 0, 0 ];
            for (let sy = 0; sy < SUPERSAMPLE; sy++) {
                for (let sx = 0; sx < SUPERSAMPLE; sx++) {
                    const x = (px * SUPERSAMPLE + sx + 0.5) * step;
                    const y = (py * SUPERSAMPLE + sy + 0.5) * step;
                    if (!insideTile(x, y, art.radius)) {
                        continue;
                    }
                    let fill = art.fill;
                    for (const layer of art.layers) {
                        if (inside(layer, x, y)) {
                            fill = layer.fill;
                        }
                    }
                    const [ r, g, b ] = rgb(fill);
                    sum[0] += r;
                    sum[1] += g;
                    sum[2] += b;
                    sum[3] += 1;
                }
            }
            const offset = (py * size + px) * 4;
            const covered = sum[3];
            if (covered > 0) {
                pixels[offset] = Math.round(sum[0] / covered);
                pixels[offset + 1] = Math.round(sum[1] / covered);
                pixels[offset + 2] = Math.round(sum[2] / covered);
                pixels[offset + 3] = Math.round(255 * covered / (SUPERSAMPLE * SUPERSAMPLE));
            }
        }
    }
    return pixels;
};

const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
    let c = n;
    for (let k = 0; k < 8; k++) {
        c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
    }
    return c >>> 0;
});

const crc32 = function(data) {
    let crc = 0xFFFFFFFF;
    for (const byte of data) {
        crc = CRC_TABLE[(crc ^ byte) & 0xFF] ^ (crc >>> 8);
    }
    return (crc ^ 0xFFFFFFFF) >>> 0;
};

const chunk = function(type, data) {
    const length = Buffer.alloc(4);
    length.writeUInt32BE(data.length);
    const body = Buffer.concat([ Buffer.from(type, 'ascii'), data ]);
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(body));
    return Buffer.concat([ length, body, crc ]);
};

const png = function(pixels, size) {
    const header = Buffer.alloc(13);
    header.writeUInt32BE(size, 0);
    header.writeUInt32BE(size, 4);
    header.set([ 8, 6, 0, 0, 0 ], 8);
    const rows = Buffer.alloc(size * (size * 4 + 1));
    for (let y = 0; y < size; y++) {
        pixels.copy(rows, y * (size * 4 + 1) + 1, y * size * 4, (y + 1) * size * 4);
    }
    return Buffer.concat([
        Buffer.from([ 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A ]),
        chunk('IHDR', header),
        chunk('IDAT', zlib.deflateSync(rows, { level: 9 })),
        chunk('IEND', Buffer.alloc(0)),
    ]);
};

const number = (value) => String(Math.round(value * 100) / 100);

const svg = function(base, state, label) {
    const lines = [
        `<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 64 64" role="img" aria-label="${label}">`,
        `  <rect width="64" height="64" rx="${base.radius}" fill="${base.fill}"/>`,
        ...base.paths.map(({ fill, d }) => `  <path fill="${fill}" d="${d}"/>`),
    ];
    if (state) {
        lines.push(`  <circle cx="${BADGE.cx}" cy="${BADGE.cy}" r="${BADGE.ring}" fill="${base.fill}"/>`);
        lines.push(`  <circle cx="${BADGE.cx}" cy="${BADGE.cy}" r="${BADGE.r}" fill="${state.fill}"/>`);
        for (const contour of state.glyph) {
            lines.push(`  <path fill="${INK}" d="M${contour.map(([ x, y ]) => `${number(x)} ${number(y)}`).join('L')}Z"/>`);
        }
    }
    lines.push('</svg>', '');
    return lines.join('\n');
};

const write = function(name, data) {
    fs.writeFileSync(path.join(ICONS, name), data);
    console.log(`icons/${name}`);
};

const appIcon = tile('keypaste-app-icon.svg');
const favicon = tile('keypaste-favicon.svg');

write('keypaste.svg', svg(favicon, null, 'keypaste'));
write('keypaste-large.svg', svg(appIcon, null, 'keypaste').replace('width="32" height="32"', 'width="128" height="128"'));
for (const size of MARK_SIZES) {
    write(`keypaste-${size}.png`, png(render(design(size <= 32 ? favicon : appIcon), size), size));
}
for (const [ name, state ] of Object.entries(STATES)) {
    write(`keypaste-${name}.svg`, svg(favicon, state, `keypaste, ${name}`));
    for (const size of STATE_SIZES) {
        write(`keypaste-${name}-${size}.png`, png(render(design(favicon, state), size), size));
    }
}
