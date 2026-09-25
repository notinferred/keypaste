import { test } from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

import worker from "../src/worker.js";
import { memory, sqlStore } from "../src/share.js";

const vector = JSON.parse(await readFile(new URL("./share-vector.json", import.meta.url), "utf8"));
const LOCAL = "http://127.0.0.1:8787";
const ctx = { waitUntil() {} };
const enabled = { SHARE_ENABLED: "1", SHARE_DEV_MEMORY: "1", ASSETS: assets() };
const REVOKE = "revoke-token-for-the-tests";

const seen = [];

function assets() {
  return {
    async fetch(request) {
      return new Response("<!doctype html><title>viewer</title>", {
        status: 200,
        headers: { "content-type": "text/html", "x-asset-path": new URL(request.url).pathname },
      });
    },
  };
}

async function call(method, path, { env = enabled, base = LOCAL, headers = {}, body } = {}) {
  const response = await worker.fetch(new Request(base + path, { method, headers, body }), env, ctx);
  seen.push(response);
  return response;
}

async function sha256Hex(text) {
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text)));
  return Array.from(digest, (b) => b.toString(16).padStart(2, "0")).join("");
}

function passphraseEnvelope(iterations = 600000) {
  const envelope = JSON.parse(vector.cases[1].envelope);
  return { ...envelope, kdf: { ...envelope.kdf, iterations } };
}

async function createBody(overrides = {}) {
  return JSON.stringify({
    envelope: JSON.parse(vector.cases[0].envelope),
    views: 2,
    ttl_seconds: 3600,
    revoke_sha256: await sha256Hex(REVOKE),
    ...overrides,
  });
}

async function create(overrides = {}, options = {}) {
  return call("POST", "/api/share", {
    headers: { "content-type": "application/json" },
    body: await createBody(overrides),
    ...options,
  });
}

test("with SHARE_ENABLED unset the API answers 503 and the viewer 404, never the gone answer", async () => {
  const disabled = { SHARE_DEV_MEMORY: "1", ASSETS: assets() };
  const id = "AAAAAAAAAAAAAAAAAAAAAA";
  const api = [
    await call("POST", "/api/share", { env: disabled, headers: { "content-type": "application/json" }, body: await createBody() }),
    await call("GET", `/api/share/${id}`, { env: disabled }),
    await call("POST", `/api/share/${id}/open`, { env: disabled }),
    await call("DELETE", `/api/share/${id}`, { env: disabled, headers: { authorization: `Bearer ${REVOKE}` } }),
  ];
  const viewer = [await call("GET", "/s/", { env: disabled }), await call("GET", "/s/viewer.js", { env: disabled })];

  for (const answer of api) {
    assert.equal(answer.status, 503);
    assert.deepEqual(await answer.json(), { error: "sharing is not available" });
  }
  for (const answer of viewer) assert.equal(answer.status, 404);
  for (const answer of [...api, ...viewer]) {
    assert.equal(answer.headers.get("cache-control"), "no-store");
    assert.equal(answer.headers.get("x-keypaste-share"), null);
  }
});

test("switching sharing off and on again leaves a share revocable", async () => {
  const { id } = await (await create()).json();
  const disabled = { SHARE_DEV_MEMORY: "1", ASSETS: assets() };

  const refused = await call("DELETE", `/api/share/${id}`, { env: disabled, headers: { authorization: `Bearer ${REVOKE}` } });
  assert.equal(refused.status, 503);
  assert.equal((await call("GET", `/api/share/${id}`)).status, 200);

  assert.equal((await call("DELETE", `/api/share/${id}`, { headers: { authorization: `Bearer ${REVOKE}` } })).status, 204);
});

test("only a lookup's 404 says the share is gone", async () => {
  const id = "AAAAAAAAAAAAAAAAAAAAAA";
  for (const answer of [
    await call("GET", `/api/share/${id}`),
    await call("POST", `/api/share/${id}/open`),
    await call("DELETE", `/api/share/${id}`, { headers: { authorization: `Bearer ${REVOKE}` } }),
  ]) {
    assert.equal(answer.status, 404);
    assert.equal(answer.headers.get("x-keypaste-share"), "gone");
  }

  const unrouted = await call("GET", `/api/share/${id}/extra`);
  assert.equal(unrouted.status, 404);
  assert.equal(unrouted.headers.get("x-keypaste-share"), null);
});

test("the viewer is served from the assets with its security headers once enabled", async () => {
  const response = await call("GET", "/s/");

  assert.equal(response.status, 200);
  assert.equal(response.headers.get("x-asset-path"), "/s/");
  assert.match(response.headers.get("content-security-policy"), /script-src 'self'/);
  assert.match(response.headers.get("content-security-policy"), /frame-ancestors 'none'/);
  assert.equal(response.headers.get("referrer-policy"), "no-referrer");
});

test("create, status, open and delete", async () => {
  const created = await create();
  assert.equal(created.status, 201);
  const { id, expires_at } = await created.json();
  assert.match(id, /^[A-Za-z0-9_-]{22}$/);
  assert.ok(Date.parse(expires_at) > Date.now());

  const status = await call("GET", `/api/share/${id}`);
  assert.equal(status.status, 200);
  const meta = await status.json();
  assert.equal(meta.views_left, 2);
  assert.equal(meta.kdf, null);
  assert.equal(meta.ct, undefined);
  assert.equal(meta.iv, undefined);
  assert.ok(meta.check && meta.check_iv);

  const opened = await call("POST", `/api/share/${id}/open`);
  assert.equal(opened.status, 200);
  const { envelope, views_left } = await opened.json();
  assert.equal(views_left, 1);
  assert.deepEqual(envelope, JSON.parse(vector.cases[0].envelope));

  const revoked = await call("DELETE", `/api/share/${id}`, { headers: { authorization: `Bearer ${REVOKE}` } });
  assert.equal(revoked.status, 204);
  assert.equal((await call("GET", `/api/share/${id}`)).status, 404);
});

test("a status request spends no view, and the last view deletes the share", async () => {
  const { id } = await (await create({ views: 1 })).json();

  await call("GET", `/api/share/${id}`);
  await call("GET", `/api/share/${id}`);

  const opened = await call("POST", `/api/share/${id}/open`);
  assert.equal(opened.status, 200);
  assert.equal((await opened.json()).views_left, 0);
  assert.equal(memory.has(id), false);

  assert.equal((await call("POST", `/api/share/${id}/open`)).status, 404);
  assert.equal((await call("GET", `/api/share/${id}`)).status, 404);
});

test("an expired share answers 404", async () => {
  const { id } = await (await create()).json();
  memory.get(id).expires_at = Date.now() - 1;

  assert.equal((await call("GET", `/api/share/${id}`)).status, 404);
  assert.equal((await call("POST", `/api/share/${id}/open`)).status, 404);
});

test("a wrong revoke token answers the same 404 and revokes nothing", async () => {
  const { id } = await (await create()).json();

  const wrong = await call("DELETE", `/api/share/${id}`, { headers: { authorization: "Bearer not-the-token" } });
  const missing = await call("DELETE", `/api/share/${id}`);
  assert.equal(wrong.status, 404);
  assert.equal(missing.status, 404);
  assert.deepEqual(await wrong.json(), { error: "not found" });
  assert.equal((await call("GET", `/api/share/${id}`)).status, 200);
});

test("an unknown id and a malformed id answer 404", async () => {
  assert.equal((await call("GET", "/api/share/AAAAAAAAAAAAAAAAAAAAAA")).status, 404);
  assert.equal((await call("GET", "/api/share/short")).status, 404);
  assert.equal((await call("GET", "/api/share/AAAAAAAAAAAAAAAAAAAAAA/extra")).status, 404);
});

test("a malformed envelope or limit is refused with 400", async () => {
  const envelope = JSON.parse(vector.cases[0].envelope);
  const refusals = [
    { views: 0 },
    { views: 11 },
    { views: 1.5 },
    { ttl_seconds: 299 },
    { ttl_seconds: 604801 },
    { revoke_sha256: "ABC" },
    { envelope: { ...envelope, v: 2 } },
    { envelope: { ...envelope, alg: "A128GCM" } },
    { envelope: { ...envelope, iv: envelope.iv + "A" } },
    { envelope: { ...envelope, check: "short" } },
    { envelope: { ...envelope, ct: "x".repeat(12001) } },
    { envelope: { ...envelope, ct: "not base64url!" } },
    { envelope: passphraseEnvelope(99999) },
    { envelope: passphraseEnvelope(10000001) },
    { envelope: { ...passphraseEnvelope(), kdf: { name: "PBKDF2-SHA256", iterations: 600000, salt: "short" } } },
    { envelope: "a string" },
  ];

  for (const overrides of refusals) {
    const response = await create(overrides);
    assert.equal(response.status, 400, JSON.stringify(overrides).slice(0, 80));
    assert.ok((await response.json()).error);
  }

  const notJson = await call("POST", "/api/share", { headers: { "content-type": "text/plain" }, body: await createBody() });
  assert.equal(notJson.status, 400);
});

test("only the validated envelope fields are stored", async () => {
  const envelope = { ...passphraseEnvelope(), extra: "dropped" };
  const { id } = await (await create({ envelope })).json();

  const stored = JSON.parse(memory.get(id).envelope);
  assert.equal(stored.extra, undefined);
  assert.deepEqual(Object.keys(stored), ["v", "alg", "kdf", "iv", "ct", "check_iv", "check"]);
});

test("an oversized body is refused with 413", async () => {
  const response = await call("POST", "/api/share", {
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ padding: "x".repeat(17000) }),
  });

  assert.equal(response.status, 413);
});

test("a streamed body with no Content-Length is cut off at the cap, not read whole", async () => {
  let pulled = 0;
  const chunk = new Uint8Array(4096).fill(0x78);
  const endless = new ReadableStream({
    pull(controller) {
      pulled += chunk.byteLength;
      controller.enqueue(chunk);
    },
  });

  const request = new Request(LOCAL + "/api/share", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: endless,
    duplex: "half",
  });
  assert.equal(request.headers.get("content-length"), null);

  const response = await worker.fetch(request, enabled, ctx);

  assert.equal(response.status, 413);
  assert.ok(pulled < 64 * 1024, `read ${pulled} bytes of an endless body`);
});

test("a foreign Origin is refused with 403 and keypaste.com's own is accepted", async () => {
  const foreign = await create({}, { headers: { "content-type": "application/json", origin: "https://evil.example" } });
  assert.equal(foreign.status, 403);
  assert.equal(foreign.headers.get("access-control-allow-origin"), null);

  const own = await create({}, { headers: { "content-type": "application/json", origin: LOCAL } });
  assert.equal(own.status, 201);
});

test("with no database and no development store the routes answer 503", async () => {
  const production = { SHARE_ENABLED: "1", ASSETS: assets() };

  const response = await create({}, { env: production });
  assert.equal(response.status, 503);
  assert.deepEqual(await response.json(), { error: "sharing is not available" });
});

test("the memory store is refused for a host that is not localhost", async () => {
  const response = await create({}, { base: "https://keypaste.com", headers: { "content-type": "application/json" } });

  assert.equal(response.status, 503);
});

test("rate limit bindings answer 429 when exceeded", async () => {
  const limited = { ...enabled, SHARE_CREATE_LIMIT: { limit: async () => ({ success: false }) } };

  assert.equal((await create({}, { env: limited })).status, 429);
});

test("the sweep deletes expired shares", async () => {
  const { id } = await (await create()).json();
  memory.get(id).expires_at = Date.now() - 1;

  let pending;
  await worker.scheduled({}, enabled, { waitUntil: (promise) => (pending = promise) });
  await pending;

  assert.equal(memory.has(id), false);
});

test("the last view returns its envelope even when the cleanup delete fails", async (t) => {
  const id = "AAAAAAAAAAAAAAAAAAAAAA";
  const envelope = vector.cases[0].envelope;
  const sql = async (strings) => {
    if (strings[0].trim().startsWith("update")) return [{ envelope, views_left: 0 }];
    throw Object.assign(new Error("connection terminated"), { code: "CONNECTION_ENDED" });
  };
  const logged = t.mock.method(console, "error", () => {});

  const row = await sqlStore(sql, ctx).open(id);

  assert.deepEqual(row, { envelope, views_left: 0 });
  assert.equal(logged.mock.callCount(), 1);
  assert.doesNotMatch(logged.mock.calls[0].arguments.join(" "), new RegExp(id));
});

test("every share response carried no-store and the API headers", () => {
  assert.ok(seen.length > 20);
  for (const response of seen) {
    assert.equal(response.headers.get("cache-control"), "no-store");
    assert.equal(response.headers.get("referrer-policy"), "no-referrer");
    assert.equal(response.headers.get("x-content-type-options"), "nosniff");
  }
});
