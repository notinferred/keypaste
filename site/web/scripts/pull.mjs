// Vendored from https://infernocms.com/pull.mjs on 2026-11-12; replace it with a fresh download to update.
// Writes a site's published InfernoCMS pages as Markdown files, file for file what
// `inferno pull --site <id> --out <dir>` writes. No dependencies; Node 20.12 or later.
//
// Usage: node pull.mjs --site <id> --out <dir> [--api https://infernocms.com] [--clean] [--jsonld <file.json>]
//                      [--public <dir>] [--if-missing]
//   --site        the site id (or INFERNO_SITE)
//   --out         where the pages go, e.g. src/content/docs
//   --api         the server (or INFERNO_API; default https://infernocms.com)
//   --clean       remove .md/.mdx files under --out that are no longer pages
//   --jsonld      also write every page's JSON-LD to this file, as { slug: document }
//   --public      refuse pages whose address a file in this directory (e.g. public/) already serves
//   --if-missing  do nothing when the outputs already exist, so a dev server needs no network

import { existsSync, mkdirSync, readdirSync, writeFileSync, unlinkSync } from 'node:fs';
import { dirname, join, posix, relative } from 'node:path';
import { parseArgs } from 'node:util';
import { fileURLToPath } from 'node:url';

const DEFAULT_API = 'https://infernocms.com';
const LAYOUT_KEY = 'inferno';

/** A JSON number kept as its source text, so it is written as serde_json would write it. */
export class JsonNumber {
  constructor(source) {
    this.source = source;
  }
}

const NUMBER = /-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?/y;

/** JSON.parse that keeps every object's key order (as a Map) and every number's source text. */
export function parseJson(text) {
  let at = 0;
  const fail = (what) => {
    throw new SyntaxError(`${what} at offset ${at} of the server's JSON`);
  };
  const skipSpace = () => {
    while (at < text.length && ' \t\n\r'.includes(text[at])) at++;
  };
  const expect = (char) => {
    skipSpace();
    if (text[at] !== char) fail(`expected '${char}'`);
    at++;
  };
  const string = () => {
    let end = at + 1;
    while (end < text.length && text[end] !== '"') end += text[end] === '\\' ? 2 : 1;
    if (end >= text.length) fail('unterminated string');
    const source = text.slice(at, end + 1);
    at = end + 1;
    return JSON.parse(source);
  };
  const value = () => {
    skipSpace();
    const char = text[at];
    if (char === '{') {
      at++;
      const map = new Map();
      skipSpace();
      if (text[at] === '}') return at++, map;
      for (;;) {
        skipSpace();
        if (text[at] !== '"') fail('expected a key');
        const key = string();
        expect(':');
        map.set(key, value());
        skipSpace();
        if (text[at] === ',') at++;
        else return expect('}'), map;
      }
    }
    if (char === '[') {
      at++;
      const items = [];
      skipSpace();
      if (text[at] === ']') return at++, items;
      for (;;) {
        items.push(value());
        skipSpace();
        if (text[at] === ',') at++;
        else return expect(']'), items;
      }
    }
    if (char === '"') return string();
    for (const [word, result] of [['true', true], ['false', false], ['null', null]]) {
      if (text.startsWith(word, at)) return (at += word.length), result;
    }
    NUMBER.lastIndex = at;
    const number = NUMBER.exec(text);
    if (!number) fail('unexpected character');
    at += number[0].length;
    return new JsonNumber(number[0]);
  };
  const result = value();
  skipSpace();
  if (at !== text.length) fail('trailing characters');
  return result;
}

/** Each page's path relative to the output dir and its file contents (pull.rs `page_files`). */
export function pageFiles(pages) {
  return pages.map((page) => {
    const hasChildren = pages.some((other) => other.slug.startsWith(`${page.slug}/`));
    const extension = page.format;
    const path =
      hasChildren || isIndexFile(page.frontmatter) ? `${page.slug}/index.${extension}` : `${page.slug}.${extension}`;
    return [path, render(page)];
  });
}

function render(page) {
  const entries = [['title', page.title]];
  if (page.summary !== '') entries.push(['description', page.summary]);
  for (const [key, value] of page.frontmatter) if (key !== LAYOUT_KEY) entries.push([key, value]);
  const order = keyOrder(page.frontmatter);
  const position = (key) => (order.includes(key) ? order.indexOf(key) : Infinity);
  entries.sort(([a], [b]) => (position(a) === position(b) ? 0 : position(a) < position(b) ? -1 : 1));
  return `---\n${emit(entries)}---\n${page.body}`;
}

function isIndexFile(frontmatter) {
  return frontmatter.get(LAYOUT_KEY)?.get?.('index_file') === true;
}

function keyOrder(frontmatter) {
  const order = frontmatter.get(LAYOUT_KEY)?.get?.('key_order');
  return Array.isArray(order) ? order.filter((key) => typeof key === 'string') : [];
}

/** Writes entries as a YAML block mapping in the given order (frontmatter.rs `emit`). */
export function emit(entries) {
  let out = '';
  for (const [key, value] of entries) out += entry(key, value, 0);
  return out;
}

function entry(key, value, indent) {
  let out = ' '.repeat(indent) + stringScalar(key, indent) + ':';
  if (value instanceof Map && value.size > 0) return out + '\n' + mapping(value, indent + 2);
  if (Array.isArray(value) && value.length > 0) return out + '\n' + sequence(value, indent + 2);
  return out + ' ' + inlineScalar(value, indent) + '\n';
}

function mapping(map, indent) {
  let out = '';
  for (const [key, value] of map) out += entry(key, value, indent);
  return out;
}

function sequence(items, indent) {
  let out = '';
  for (const item of items) {
    out += ' '.repeat(indent) + '-';
    if (item instanceof Map && item.size > 0) out += ' ' + mapping(item, indent + 2).slice(indent + 2);
    else if (Array.isArray(item) && item.length > 0) out += '\n' + sequence(item, indent + 2);
    else out += ' ' + inlineScalar(item, indent) + '\n';
  }
  return out;
}

function inlineScalar(value, indent) {
  if (value === null) return 'null';
  if (typeof value === 'boolean') return String(value);
  if (value instanceof JsonNumber) return numberText(value.source);
  if (typeof value === 'string') return stringScalar(value, indent);
  return Array.isArray(value) ? '[]' : '{}';
}

/** serde_json's text for a number: an integer that fits i64 or u64 as written, anything else in serde_json's shortest f64 form. */
function numberText(source) {
  if (/^-?\d+$/.test(source)) {
    const integer = BigInt(source);
    if (integer >= -(2n ** 63n) && integer < 2n ** 64n) return integer.toString();
  }
  const value = Number(source);
  if (!Number.isFinite(value)) return 'null';
  const sign = value < 0 || Object.is(value, -0) ? '-' : '';
  if (value === 0) return `${sign}0.0`;
  const [mantissa, exponent] = Math.abs(value).toExponential().split('e');
  const digits = mantissa.replace('.', '');
  const point = Number(exponent) + 1;
  if (point >= digits.length && point <= 16) return `${sign}${digits}${'0'.repeat(point - digits.length)}.0`;
  if (point > 0 && point <= 16) return `${sign}${digits.slice(0, point)}.${digits.slice(point)}`;
  if (point > -5 && point <= 0) return `${sign}0.${'0'.repeat(-point)}${digits}`;
  const fraction = digits.length > 1 ? `.${digits.slice(1)}` : '';
  return `${sign}${digits[0]}${fraction}e${point > 1 ? '+' : ''}${point - 1}`;
}

/** The plainest form that reads back as the same string: plain, then a literal block, then double-quoted. */
function stringScalar(text, indent) {
  const multiline = text.includes('\n');
  if (!multiline && readsBackPlain(text)) return text;
  if (multiline && literalReadsBack(text)) return literalBlock(text, indent + 2);
  return JSON.stringify(text);
}

// What yaml-rust2 reads `k: <text>` as, for a one-line text: a plain scalar equal to it, then resolved to a string.
function readsBackPlain(text) {
  if (text === '' || /[\r\0]/.test(text)) return false;
  if (/^[ \t]|[ \t]$/.test(text) || /[[\]{},#&*!|>'"%@`]/.test(text[0])) return false;
  if (/^[-?:]/.test(text) && (text.length === 1 || /[ \t]/.test(text[1]))) return false;
  if (/:([ \t]|$)/.test(text) || /[ \t]#/.test(text)) return false;
  return resolvesToString(text);
}

// yaml-rust2's `Yaml::from_str`: what a plain scalar means when it is not a string.
function resolvesToString(text) {
  const fitsI64 = (digits, radix = 10) => {
    const negative = digits.startsWith('-');
    const body = digits.replace(/^[+-]/, '');
    const pattern = radix === 16 ? /^[0-9a-fA-F]+$/ : radix === 8 ? /^[0-7]+$/ : /^\d+$/;
    if (!pattern.test(body)) return false;
    const prefix = radix === 16 ? '0x' : radix === 8 ? '0o' : '';
    const value = BigInt(prefix + body) * (negative ? -1n : 1n);
    return value >= -(2n ** 63n) && value < 2n ** 63n;
  };
  if (text.startsWith('0x') && fitsI64(text.slice(2), 16)) return false;
  if (text.startsWith('0o') && fitsI64(text.slice(2), 8)) return false;
  if (text.startsWith('+') && fitsI64(text.slice(1))) return false;
  if (['~', 'null', 'true', 'True', 'TRUE', 'false', 'False', 'FALSE'].includes(text)) return false;
  if (fitsI64(text)) return false;
  if (/^[+-]?\.(inf|Inf|INF)$/.test(text) || /^\.(nan|NaN|NAN)$/.test(text)) return false;
  return !/^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$/.test(text);
}

// Whether `k: |...` written by literalBlock reads back as the text.
function literalReadsBack(text) {
  const content = text.replace(/\n+$/, '');
  if (content === '' || content.startsWith(' ') || /[\r\0]/.test(text)) return false;
  const firstLine = content.split('\n').find((line) => line !== '');
  return !firstLine.startsWith(' ');
}

function literalBlock(text, contentIndent) {
  const content = text.replace(/\n+$/, '');
  const trailing = text.length - content.length;
  const pad = ' '.repeat(contentIndent);
  let block = trailing === 0 ? '|-' : trailing === 1 ? '|' : '|+';
  for (const line of content.split('\n')) block += '\n' + (line === '' ? '' : pad + line);
  return block + '\n'.repeat(Math.max(0, trailing - 1));
}

/** Writes the files under `out`; with `clean`, removes .md/.mdx files there that are no longer pages. */
export function writeFiles(out, files, clean) {
  for (const [path] of files) {
    const parts = path.split('/');
    if (parts.some((part) => part === '..' || part === '') || parts[0] === '.') {
      throw new Error(`page path \`${path}\` leaves the output directory`);
    }
  }
  for (const [path, contents] of files) {
    const target = join(out, path);
    mkdirSync(dirname(target), { recursive: true });
    writeFileSync(target, contents);
  }
  const removed = [];
  if (clean) {
    const written = new Set(files.map(([path]) => posix.normalize(path)));
    for (const file of readdirSync(out, { recursive: true, withFileTypes: true })) {
      const path = relative(out, join(file.parentPath, file.name)).replaceAll('\\', '/');
      if (file.isFile() && /\.mdx?$/.test(file.name) && !written.has(path)) {
        unlinkSync(join(out, path));
        removed.push(path);
      }
    }
  }
  return removed;
}

/** The fields of a public page that pull writes, from the server's JSON. */
export function readPages(json) {
  const list = parseJson(json);
  if (!Array.isArray(list)) throw new Error('the pages response is not a list');
  return list.map((page) => ({
    slug: page.get('slug'),
    title: page.get('title'),
    summary: page.get('summary') ?? '',
    body: page.get('body') ?? '',
    format: page.get('format'),
    frontmatter: page.get('frontmatter') ?? new Map(),
  }));
}

// Files a site serves from its public directory stay in git: a CMS page at the same address would replace it in the build.
export function publicCollisions(pages, publicDir) {
  return pages
    .map((page) => page.slug)
    .filter((slug) => ['.html', '/index.html'].some((suffix) => existsSync(join(publicDir, `${slug}${suffix}`))));
}

async function fetchText(url) {
  let response;
  try {
    response = await fetch(url, { headers: { accept: 'application/json' } });
  } catch (error) {
    throw new Error(`GET ${url} failed: ${error.cause?.message ?? error.message}`);
  }
  const text = await response.text();
  if (!response.ok) throw new Error(`GET ${url} answered ${response.status}: ${text.slice(0, 500)}`);
  return text;
}

/** The command line and environment as options; throws on a missing or unknown option. */
export function readOptions(args, env = {}) {
  const { values } = parseArgs({
    args,
    options: {
      site: { type: 'string' },
      out: { type: 'string' },
      api: { type: 'string' },
      clean: { type: 'boolean', default: false },
      jsonld: { type: 'string' },
      public: { type: 'string' },
      'if-missing': { type: 'boolean', default: false },
    },
    strict: true,
  });
  const site = values.site ?? env.INFERNO_SITE;
  if (!site) throw new Error('--site <id> (or INFERNO_SITE) is required');
  if (!values.out) throw new Error('--out <dir> is required');
  return {
    site,
    out: values.out,
    api: (values.api ?? (env.INFERNO_API || DEFAULT_API)).replace(/\/+$/, ''),
    clean: values.clean,
    jsonld: values.jsonld ?? null,
    publicDir: values.public ?? null,
    ifMissing: values['if-missing'],
  };
}

async function main(options) {
  const { site, out, api, jsonld: jsonldFile, publicDir } = options;
  if (options.ifMissing && existsSync(out) && (!jsonldFile || existsSync(jsonldFile))) return;

  const sitePath = `${api}/v1/${encodeURIComponent(site)}`;
  const pages = readPages(await fetchText(`${sitePath}/pages`));
  if (pages.length === 0) throw new Error(`${api} has no published pages for ${site}; refusing to build an empty site`);
  if (publicDir) {
    const collisions = publicCollisions(pages, publicDir);
    if (collisions.length > 0) throw new Error(`these pages would replace files in ${publicDir}: ${collisions.join(', ')}`);
  }

  const files = pageFiles(pages);
  const removed = writeFiles(out, files, options.clean);

  if (jsonldFile) {
    const jsonld = {};
    const slugs = pages.map((page) => page.slug);
    for (let start = 0; start < slugs.length; start += 8) {
      const batch = slugs.slice(start, start + 8);
      const documents = await Promise.all(batch.map((slug) => fetchText(`${sitePath}/jsonld/${slug}`)));
      batch.forEach((slug, index) => (jsonld[slug] = JSON.parse(documents[index])));
    }
    mkdirSync(dirname(jsonldFile), { recursive: true });
    writeFileSync(jsonldFile, `${JSON.stringify(jsonld, null, 2)}\n`);
  }

  console.log(`wrote ${files.length} pages to ${relative(process.cwd(), out) || '.'} from ${api}`);
  if (jsonldFile) console.log(`wrote JSON-LD for ${pages.length} pages to ${jsonldFile}`);
  for (const path of removed) console.log(`removed ${path}`);
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  Promise.resolve()
    .then(() => main(readOptions(process.argv.slice(2), process.env)))
    .catch((error) => {
      console.error(`pull: ${error.message}`);
      process.exit(1);
    });
}
