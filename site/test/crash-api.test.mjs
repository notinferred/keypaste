import { test } from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { DatabaseSync } from "node:sqlite";

import worker from "../src/worker.js";
import { KEPT_DAYS, NEW_ROWS_A_DAY } from "../src/crash.js";

const vector = JSON.parse(await readFile(new URL("./crash-vector.json", import.meta.url), "utf8"));
const schema = await readFile(new URL("../crash-schema.sql", import.meta.url), "utf8");
const reports = vector.cases.map((each) => each.report);
const BASE = "https://keypaste.com";
const SENDER = "203.0.113.7";
const DAY_MS = 86400 * 1000;
const ctx = { waitUntil() {} };

const seen = [];

// D1's binding over node:sqlite, so the route's SQL runs against the schema H-0028 applies.
function database() {
  const sqlite = new DatabaseSync(":memory:");
  sqlite.exec(schema);
  return {
    sqlite,
    rows: () => sqlite.prepare("select * from crash order by fingerprint").all().map((row) => ({ ...row })),
    prepare(sql) {
      return {
        bind: (...values) => ({
          async run() {
            const { changes } = sqlite.prepare(sql).run(...values);
            return { success: true, results: [], meta: { changes: Number(changes) } };
          },
        }),
      };
    },
  };
}

const on = (extra = {}) => ({ CRASH_ENABLED: "1", CRASH_DB: database(), ...extra });

async function post(env, body, headers = {}) {
  const request = new Request(BASE + "/api/crash", {
    method: "POST",
    headers: { "content-type": "application/json", "cf-connecting-ip": SENDER, ...headers },
    body,
  });
  const response = await worker.fetch(request, env, ctx);
  seen.push(response);
  return response;
}

const dayOn = (ms) => new Date(ms).toISOString().slice(0, 10);
const today = () => dayOn(Date.now());
const daysAgo = (days) => dayOn(Date.now() - days * DAY_MS);

// A vector report with a change, written compactly in its own field order, as the scrubber writes one.
function changed(index, change) {
  const report = JSON.parse(reports[index]);
  change(report);
  return JSON.stringify(report);
}

// A valid report of exactly this many bytes, its frames' names cut from the last one back.
function sized(length) {
  const report = JSON.parse(reports[0]);
  const frame = () => ({ asm: "Keypaste.Core", type: "T".repeat(256), method: "M", il: 0 });
  report.exception.frames = Array.from({ length: 48 }, frame);
  report.exception.inner = Array.from({ length: 4 }, () => ({
    type: "System.Exception",
    hresult: 0,
    omitted: 0,
    frames: Array.from({ length: 16 }, frame),
  }));

  const frames = [...report.exception.frames, ...report.exception.inner.flatMap((inner) => inner.frames)].reverse();
  let excess = JSON.stringify(report).length - length;
  for (const each of frames) {
    const cut = Math.min(excess, 255);
    each.type = each.type.slice(cut);
    excess -= cut;
    if (excess === 0) break;
  }

  const text = JSON.stringify(report);
  assert.equal(text.length, length);
  return text;
}

function insertRow(db, fingerprint, firstSeenOn, lastSeenOn) {
  db.sqlite
    .prepare("insert into crash values (?, '1.0.0', 'linux', 'x64', 1, ?, ?, '{}')")
    .run(fingerprint, firstSeenOn, lastSeenOn);
}

test("the vector is written as JSON.stringify writes it, so changed() makes reports in the scrubber's form", () => {
  for (const report of reports) assert.equal(JSON.stringify(JSON.parse(report)), report);
});

test("switched off or with no database the route answers 503 with a day's Retry-After and stores nothing", async () => {
  const db = database();
  for (const env of [{}, { CRASH_ENABLED: "1" }, { CRASH_DB: db }, { CRASH_ENABLED: "0", CRASH_DB: db }, { CRASH_ENABLED: "true", CRASH_DB: db }]) {
    const response = await post(env, reports[0]);
    assert.equal(response.status, 503);
    assert.equal(response.headers.get("retry-after"), "86400");
    assert.deepEqual(await response.json(), { error: "crash reports are not being taken" });
  }

  const get = await worker.fetch(new Request(BASE + "/api/crash"), { CRASH_DB: db }, ctx);
  seen.push(get);
  assert.equal(get.status, 503);
  assert.deepEqual(db.rows(), []);
});

test("each vector report is stored as it was sent, with the day it came and nothing of its sender", async () => {
  const env = on();
  for (const report of reports) {
    const response = await post(env, report);
    assert.equal(response.status, 204);
    assert.equal(await response.text(), "");
  }

  const rows = env.CRASH_DB.rows();
  assert.deepEqual(rows.map((row) => row.report).sort(), [...reports].sort());
  for (const row of rows) {
    const report = JSON.parse(row.report);
    assert.deepEqual(Object.keys(row), ["fingerprint", "version", "os", "arch", "failures", "first_seen_on", "last_seen_on", "report"]);
    assert.match(row.fingerprint, /^[0-9a-f]{64}$/);
    assert.deepEqual([row.version, row.os, row.arch], [report.version, report.os, report.arch]);
    assert.equal(row.failures, report.count);
    assert.equal(row.first_seen_on, today());
    assert.equal(row.last_seen_on, today());
    assert.ok(Object.values(row).every((value) => !String(value).includes(SENDER)));
  }
});

test("a repeat on the same version and platform adds its count to one row and keeps the first report", async () => {
  const env = on();
  const later = changed(0, (report) => {
    report.os_version = "15.7.2";
    report.screen = "items";
    report.uptime = ">1d";
    report.occurred_on = "2026-10-29";
    report.exception.frames[0].il = 213;
    report.exception.inner = [];
    report.count = 4;
  });
  for (const body of [reports[0], later, reports[1], reports[1]]) assert.equal((await post(env, body)).status, 204);

  const rows = env.CRASH_DB.rows();
  assert.equal(rows.length, 2);
  assert.equal(rows.find((row) => row.report === reports[0]).failures, 5);
  assert.equal(rows.find((row) => row.report === reports[1]).failures, 6);

  const others = [
    changed(0, (report) => (report.version = "1.0.1")),
    changed(0, (report) => (report.os = "linux")),
    changed(0, (report) => (report.arch = "x64")),
    changed(0, (report) => (report.kind = "unobserved-task")),
    changed(0, (report) => (report.exception.type = "System.ArgumentException")),
    changed(0, (report) => (report.exception.frames[1].method = "Rethrow")),
    changed(1, (report) => (report.site = "WindowsHello.Unlock")),
  ];
  for (const body of others) assert.equal((await post(env, body)).status, 204);
  assert.equal(env.CRASH_DB.rows().length, 2 + others.length);
});

test("anything but a report as the scrubber writes it is refused with 400, and nothing is stored", async () => {
  const env = on();
  const refusals = [
    ["an exception's message", changed(0, (r) => (r.exception.message = "could not open /home/someone/Personal.kdbx"))],
    ["an inner exception's message", changed(0, (r) => (r.exception.inner[0].message = "the value"))],
    ["a field the scrubber does not write", changed(2, (r) => (r.vault = "Personal"))],
    ["a frame's file", changed(0, (r) => (r.exception.frames[0].file = "/src/SessionAuthority.cs"))],
    ["another field order", JSON.stringify((({ kind, ...rest }) => ({ kind, ...rest }))(JSON.parse(reports[0])))],
    ["spacing", JSON.stringify(JSON.parse(reports[0]), null, 1)],
    ["a trailing newline", reports[0] + "\n"],
    ["a byte order mark", "\uFEFF" + reports[0]],
    ["another schema", changed(0, (r) => (r.schema = 2))],
    ["an unknown kind", changed(0, (r) => (r.kind = "fatal"))],
    ["a site on a crash", changed(0, (r) => (r.site = "LockedNotice.Show"))],
    ["no site on an unexpected exception", changed(1, (r) => (r.site = null))],
    ["a site that is a sentence", changed(1, (r) => (r.site = "the vault Personal"))],
    ["a version with a space", changed(0, (r) => (r.version = "1.0 beta"))],
    ["an uppercase build", changed(0, (r) => (r.build = r.build.toUpperCase()))],
    ["a short build", changed(0, (r) => (r.build = "0787be6e"))],
    ["an unknown OS", changed(0, (r) => (r.os = "android"))],
    ["an OS version with a name", changed(0, (r) => (r.os_version = "15.7.1 (24H2)"))],
    ["a one-part runtime", changed(0, (r) => (r.runtime = "10"))],
    ["an unknown architecture", changed(0, (r) => (r.arch = "riscv64"))],
    ["an unknown uptime", changed(0, (r) => (r.uptime = "3m"))],
    ["a screen that is an entry's name", changed(0, (r) => (r.screen = "GitHub token"))],
    ["an impossible date", changed(0, (r) => (r.occurred_on = "2026-02-30"))],
    ["a time beside the date", changed(0, (r) => (r.occurred_on = "2026-10-28T12:00:00Z"))],
    ["a count of 0", changed(0, (r) => (r.count = 0))],
    ["a count past 32 bits", changed(0, (r) => (r.count = 2147483648))],
    ["a crash with no exception", changed(0, (r) => (r.exception = null))],
    ["an ended app with an exception", changed(2, (r) => (r.exception = JSON.parse(reports[0]).exception))],
    ["an exception type with a path", changed(0, (r) => (r.exception.type = "C:/Users/someone"))],
    ["an HResult past 32 bits", changed(0, (r) => (r.exception.hresult = 4294967295))],
    ["a negative omitted count", changed(0, (r) => (r.exception.omitted = -1))],
    ["a frame from an assembly off the allowlist", changed(0, (r) => (r.exception.frames[0].asm = "Evil.Plugin"))],
    ["an allowed name's start without its dot", changed(0, (r) => (r.exception.frames[0].asm = "SystemX"))],
    ["an external frame that names its method", changed(0, (r) => (r.exception.frames[2].method = "Run"))],
    ["a method name with a space", changed(0, (r) => (r.exception.frames[0].method = "Move Next"))],
    ["an offset below -1", changed(0, (r) => (r.exception.frames[0].il = -2))],
    ["49 frames", changed(0, (r) => (r.exception.frames = Array(49).fill(r.exception.frames[0])))],
    ["17 frames in an inner exception", changed(0, (r) => (r.exception.inner[0].frames = Array(17).fill(r.exception.inner[0].frames[0])))],
    ["five inner exceptions", changed(0, (r) => (r.exception.inner = Array(5).fill(r.exception.inner[0])))],
    ["an inner exception's own inner list", changed(0, (r) => (r.exception.inner[0].inner = []))],
    ["no inner list", changed(0, (r) => delete r.exception.inner)],
    ["a report in an array", `[${reports[0]}]`],
    ["not JSON", "schema=1"],
  ];

  const invalidUtf8 = new TextEncoder().encode(reports[2]);
  invalidUtf8[reports[2].indexOf("linux") + 4] = 0xff;
  refusals.push(["a byte that is not UTF-8", invalidUtf8]);

  for (const [what, body] of refusals) {
    const response = await post(env, body);
    assert.equal(response.status, 400, what);
    assert.ok((await response.json()).error, what);
  }

  const notJson = await post(env, reports[0], { "content-type": "text/plain" });
  assert.equal(notJson.status, 400);
  assert.deepEqual(await notJson.json(), { error: "the body must be JSON" });

  assert.deepEqual(env.CRASH_DB.rows(), []);
});

test("a body past 16 KiB is refused with 413, and a report a merged count could push past it with 400", async () => {
  const env = on();

  assert.equal((await post(env, sized(16375))).status, 204);
  assert.equal((await post(env, sized(16376))).status, 400);
  assert.equal((await post(env, sized(16385))).status, 413);
  assert.equal(env.CRASH_DB.rows().length, 1);
});

test("a foreign Origin is refused with 403 and keypaste.com's own is taken", async () => {
  const env = on();

  const foreign = await post(env, reports[0], { origin: "https://evil.example" });
  assert.equal(foreign.status, 403);
  assert.equal(foreign.headers.get("access-control-allow-origin"), null);
  assert.equal((await post(env, reports[0], { origin: "https://keypaste.com" })).status, 204);
  assert.equal(env.CRASH_DB.rows().length, 1);
});

test("only POST is answered at /api/crash, and nothing below it is a route", async () => {
  for (const method of ["GET", "PUT", "DELETE"]) {
    const response = await worker.fetch(new Request(BASE + "/api/crash", { method }), on(), ctx);
    seen.push(response);
    assert.equal(response.status, 405);
    assert.equal(response.headers.get("allow"), "POST");
  }

  const below = await worker.fetch(new Request(BASE + "/api/crash/reports", { method: "POST" }), on(), ctx);
  assert.equal(below.status, 404);
});

test("CRASH_LIMIT is asked with the sender's address, and its refusal answers 429 with a minute's Retry-After", async () => {
  const asked = [];
  const limiter = {
    async limit({ key }) {
      asked.push(key);
      return { success: asked.length <= 10 };
    },
  };
  const env = on({ CRASH_LIMIT: limiter });

  for (let i = 0; i < 10; i++) assert.equal((await post(env, reports[1])).status, 204);
  const limited = await post(env, reports[1]);

  assert.equal(limited.status, 429);
  assert.equal(limited.headers.get("retry-after"), "60");
  assert.deepEqual(new Set(asked), new Set([SENDER]));
  assert.equal(env.CRASH_DB.rows()[0].failures, 30);
});

test(`at most ${NEW_ROWS_A_DAY} new rows a day are made, while a repeat still adds to its row`, async () => {
  const env = on();
  assert.equal((await post(env, reports[1])).status, 204);
  for (let i = 1; i < NEW_ROWS_A_DAY; i++) insertRow(env.CRASH_DB, `filler-${i}`, today(), today());

  const refused = await post(env, reports[0]);
  assert.equal(refused.status, 503);
  const wait = Number(refused.headers.get("retry-after"));
  assert.ok(wait >= 1 && wait <= 86400, String(wait));

  assert.equal((await post(env, reports[1])).status, 204);
  assert.equal(env.CRASH_DB.rows().length, NEW_ROWS_A_DAY);
  assert.equal(env.CRASH_DB.rows().find((row) => row.report === reports[1]).failures, 6);

  env.CRASH_DB.sqlite.prepare("update crash set first_seen_on = ?").run(daysAgo(1));
  assert.equal((await post(env, reports[0])).status, 204);
  assert.equal(env.CRASH_DB.rows().length, NEW_ROWS_A_DAY + 1);
});

test(`the cron deletes reports last seen more than ${KEPT_DAYS} days ago, with the route switched off too`, async () => {
  const db = database();
  insertRow(db, "old", daysAgo(KEPT_DAYS + 5), daysAgo(KEPT_DAYS + 1));
  insertRow(db, "edge", daysAgo(KEPT_DAYS + 5), daysAgo(KEPT_DAYS));
  insertRow(db, "recent", daysAgo(KEPT_DAYS + 5), today());

  const pending = [];
  await worker.scheduled({}, { CRASH_DB: db }, { waitUntil: (promise) => pending.push(promise) });
  await Promise.all(pending);

  assert.deepEqual(db.rows().map((row) => row.fingerprint), ["edge", "recent"]);
});

test("a database failure answers 503 with no Retry-After and logs nothing of the report or its sender", async (t) => {
  const failing = {
    prepare: () => ({
      bind: () => ({
        run: async () => {
          throw new Error("D1_ERROR: no such table: crash: SQLITE_ERROR");
        },
      }),
    }),
  };
  const logged = t.mock.method(console, "error", () => {});

  const response = await post({ CRASH_ENABLED: "1", CRASH_DB: failing }, reports[0]);

  assert.equal(response.status, 503);
  assert.equal(response.headers.get("retry-after"), null);
  assert.equal(logged.mock.callCount(), 1);
  const line = logged.mock.calls[0].arguments.join(" ");
  for (const part of ["0787be6e", "SessionAuthority", "agents", SENDER]) assert.ok(!line.includes(part), part);
});

test("every crash answer carried no-store and the API headers", () => {
  assert.ok(seen.length > 60, String(seen.length));
  for (const response of seen) {
    assert.equal(response.headers.get("cache-control"), "no-store");
    assert.equal(response.headers.get("referrer-policy"), "no-referrer");
    assert.equal(response.headers.get("x-content-type-options"), "nosniff");
  }
});
