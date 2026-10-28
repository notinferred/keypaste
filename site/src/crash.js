// The desktop app's crash reports (D-0586 to D-0588). A report is taken only byte for byte as CrashScrubber writes it
// (D-0558), so no field, name or text that grammar does not allow is ever stored, and repeats of one failure on one
// version and platform add to one row's count. No address, header or time of day is kept, and nothing here reads a
// report back.
//
// The route answers 503 until CRASH_ENABLED is "1" and CRASH_DB is bound, which H-0028 adds in one commit
// (site/README.md). Logs name an error's name and message only.
import { readCapped } from "./share.js";

export const NEW_ROWS_A_DAY = 200;
export const KEPT_DAYS = 90;

const ORIGINS = new Set(["https://keypaste.com", "https://www.keypaste.com"]);
const MAX_BODY = 16 * 1024;
const INT32_MIN = -2147483648;
const INT32_MAX = 2147483647;
const DAY_SECONDS = 86400;
const LIMIT_SECONDS = 60;

const NO_STORE = {
  "cache-control": "no-store",
  "x-content-type-options": "nosniff",
  "referrer-policy": "no-referrer",
};

// CrashScrubber's grammar, rule for rule.
const KINDS = ["crash", "unobserved-task", "unexpected", "ended-unexpectedly"];
const SYSTEMS = ["windows", "macos", "linux", "other"];
const ARCHITECTURES = ["x64", "arm64", "x86", "arm", "other"];
const UPTIMES = ["<1m", "<10m", "<1h", "<1d", ">1d"];
const ASSEMBLIES = [
  "keypaste-app", "Keypaste", "KeePassLib", "Avalonia", "System", "Microsoft", "Yubico", "SkiaSharp", "HarfBuzzSharp", "Tmds.DBus",
  "WinRT.Runtime",
];
const PLACEHOLDERS = ["[unnamed]", "[external]", "[dynamic]"];
const UNNAMED = "[unnamed]";
const NAME = /^[A-Za-z0-9_.+<>`$|-]{1,256}$/;
const SCREEN = /^[a-z][a-z0-9-]{0,31}$/;
const VERSION = /^[A-Za-z0-9.-]{1,64}$/;
const COMMIT = /^[0-9a-f]{40}$/;
const NUMBERS = /^[0-9]{1,10}\.[0-9]{1,10}(\.[0-9]{1,10})?$/;
const DATE = /^([0-9]{4})-([0-9]{2})-([0-9]{2})$/;
const FRAMES = 48;
const INNER = 4;
const INNER_FRAMES = 16;

// A repeat always adds to its row; a new row is made only while fewer than NEW_ROWS_A_DAY were first seen today, and
// otherwise nothing is written and changes is 0.
const UPSERT = `
  insert into crash (fingerprint, version, os, arch, failures, first_seen_on, last_seen_on, report)
  select ?, ?, ?, ?, ?, ?, ?, ?
  where exists (select 1 from crash where fingerprint = ? and version = ? and os = ? and arch = ?)
     or (select count(*) from crash where first_seen_on = ?) < ?
  on conflict (fingerprint, version, os, arch) do update
    set failures = failures + excluded.failures, last_seen_on = excluded.last_seen_on
`;

export function isCrashRoute(pathname) {
  return pathname === "/api/crash";
}

export async function handleCrash(request, env) {
  if (env.CRASH_ENABLED !== "1" || !env.CRASH_DB) return refuse(503, "crash reports are not being taken", DAY_SECONDS);

  if (request.method !== "POST") {
    const response = refuse(405, "only POST is answered");
    response.headers.set("allow", "POST");
    return response;
  }

  const origin = request.headers.get("origin");
  if (origin && !ORIGINS.has(origin)) return refuse(403, "forbidden");

  if (env.CRASH_LIMIT && !(await env.CRASH_LIMIT.limit({ key: request.headers.get("cf-connecting-ip") ?? "unknown" })).success) {
    return refuse(429, "too many requests", LIMIT_SECONDS);
  }

  if (!(request.headers.get("content-type") ?? "").startsWith("application/json")) return refuse(400, "the body must be JSON");
  const bytes = await readCapped(request, MAX_BODY);
  if (!bytes) return refuse(413, "too large");

  const read = readReport(bytes);
  if (!read) return refuse(400, "the body is not a report keypaste writes");

  const now = Date.now();
  try {
    if (!(await store(env.CRASH_DB, read, dateOn(now)))) {
      return refuse(503, "no new reports are being taken today", secondsToMidnight(now));
    }
  } catch (error) {
    console.error("crash failed:", error?.name, error?.message);
    return refuse(503, "crash reports are not being taken");
  }

  return new Response(null, { status: 204, headers: NO_STORE });
}

export async function sweepCrashes(env) {
  if (!env.CRASH_DB) return;

  await env.CRASH_DB.prepare("delete from crash where last_seen_on < ?")
    .bind(dateOn(Date.now() - KEPT_DAYS * DAY_SECONDS * 1000))
    .run();
}

// Byte for byte what CrashScrubber writes, so another field, order or spacing is refused, and so is a report that a
// merged count could push past the size limit, as CrashScrubber.TryRead refuses them.
function readReport(bytes) {
  let text;
  let root;
  try {
    text = new TextDecoder("utf-8", { fatal: true, ignoreBOM: true }).decode(bytes);
    root = JSON.parse(text);
  } catch {
    return null;
  }

  const report = readBody(root);
  if (!report || write(report, report.count) !== text || write(report, INT32_MAX).length > MAX_BODY) return null;
  return { report, text };
}

function readBody(root) {
  if (!isObject(root)) return null;

  const kind = textOf(root, "kind");
  const site = textOf(root, "site");
  const version = textOf(root, "version");
  const build = textOf(root, "build");
  const os = textOf(root, "os");
  const osVersion = textOf(root, "os_version");
  const arch = textOf(root, "arch");
  const runtime = textOf(root, "runtime");
  const uptime = textOf(root, "uptime");
  const screen = textOf(root, "screen");
  const occurredOn = textOf(root, "occurred_on");
  const count = int32Of(root, "count");

  if (
    int32Of(root, "schema") !== 1 ||
    !KINDS.includes(kind) ||
    (kind === "unexpected" ? site === null || name(site) !== site : site !== null) ||
    version === null || !VERSION.test(version) ||
    (build !== null && !COMMIT.test(build)) ||
    !SYSTEMS.includes(os) ||
    osVersion === null || !NUMBERS.test(osVersion) ||
    !ARCHITECTURES.includes(arch) ||
    runtime === null || !NUMBERS.test(runtime) ||
    (uptime !== null && !UPTIMES.includes(uptime)) ||
    (screen !== null && screenName(screen) !== screen) ||
    !isDate(occurredOn) ||
    count === null || count < 1 ||
    !Object.hasOwn(root, "exception")
  ) {
    return null;
  }

  let exception = null;
  if (kind === "ended-unexpectedly") {
    if (root.exception !== null) return null;
  } else if ((exception = readError(root.exception, true)) === null) {
    return null;
  }

  return { schema: 1, kind, site, version, build, os, os_version: osVersion, arch, runtime, uptime, screen, occurred_on: occurredOn, count, exception };
}

function readError(element, top) {
  const type = textOf(element, "type");
  const hresult = int32Of(element, "hresult");
  const omitted = int32Of(element, "omitted");
  if (type === null || !NAME.test(type) || hresult === null || omitted === null || omitted < 0) return null;

  const listed = element.frames;
  if (!Object.hasOwn(element, "frames") || !Array.isArray(listed) || listed.length > (top ? FRAMES : INNER_FRAMES)) return null;

  const frames = [];
  for (const each of listed) {
    const asm = textOf(each, "asm");
    const declaring = textOf(each, "type");
    const method = textOf(each, "method");
    const il = int32Of(each, "il");
    if (asm === null || declaring === null || method === null || il === null || il < -1 || !isFrame(asm, declaring, method)) {
      return null;
    }
    frames.push({ asm, type: declaring, method, il });
  }

  if (!top) return { type, hresult, omitted, frames };

  const nested = element.inner;
  if (!Object.hasOwn(element, "inner") || !Array.isArray(nested) || nested.length > INNER) return null;

  const inner = [];
  for (const each of nested) {
    const read = isObject(each) ? readError(each, false) : null;
    if (read === null) return null;
    inner.push(read);
  }

  return { type, hresult, omitted, frames, inner };
}

// Every key in the order CrashScrubber writes it; its strings are ASCII that neither writer escapes.
function write(report, count) {
  return JSON.stringify({ ...report, count });
}

async function store(db, { report, text }, today) {
  const key = [await fingerprint(report), report.version, report.os, report.arch];
  const result = await db
    .prepare(UPSERT)
    .bind(...key, report.count, today, today, text, ...key, today, NEW_ROWS_A_DAY)
    .run();
  return result.meta.changes > 0;
}

// One failure: its kind, catch site, exception type and first eight frames' names, so offsets, the OS's version, the
// screen and the uptime do not split a row.
async function fingerprint({ kind, site, exception }) {
  const frames = (exception?.frames ?? []).slice(0, 8).map(({ asm, type, method }) => [asm, type, method]);
  const identity = JSON.stringify([kind, site, exception?.type ?? null, frames]);
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(identity)));
  return Array.from(digest, (b) => b.toString(16).padStart(2, "0")).join("");
}

function isObject(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function textOf(element, key) {
  return isObject(element) && Object.hasOwn(element, key) && typeof element[key] === "string" ? element[key] : null;
}

function int32Of(element, key) {
  if (!isObject(element) || !Object.hasOwn(element, key)) return null;
  const value = element[key];
  return Number.isInteger(value) && value >= INT32_MIN && value <= INT32_MAX ? value : null;
}

function name(text) {
  return NAME.test(text) ? text : UNNAMED;
}

function screenName(text) {
  return SCREEN.test(text) ? text : UNNAMED;
}

function isAllowedAssembly(text) {
  return NAME.test(text) && ASSEMBLIES.some((allowed) => text === allowed || text.startsWith(allowed + "."));
}

function isFrame(asm, type, method) {
  return PLACEHOLDERS.includes(asm)
    ? type === asm && method === asm
    : isAllowedAssembly(asm) && name(type) === type && name(method) === method;
}

function isDate(text) {
  const match = text === null ? null : DATE.exec(text);
  if (!match) return false;

  const [year, month, day] = match.slice(1).map(Number);
  const leap = (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0;
  const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  return year >= 1 && month >= 1 && month <= 12 && day >= 1 && day <= days[month - 1];
}

function dateOn(ms) {
  return new Date(ms).toISOString().slice(0, 10);
}

function secondsToMidnight(now) {
  const midnight = new Date(now);
  midnight.setUTCHours(24, 0, 0, 0);
  return Math.max(1, Math.ceil((midnight.getTime() - now) / 1000));
}

function refuse(status, error, retryAfter) {
  const headers = { "content-type": "application/json", ...NO_STORE };
  if (retryAfter) headers["retry-after"] = String(retryAfter);
  return new Response(JSON.stringify({ error }), { status, headers });
}
