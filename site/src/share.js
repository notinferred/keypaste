// Share links (D-0355): the server keeps an envelope it cannot open, a view count, an expiry and the
// SHA-256 of a revoke token. The key is in the link's fragment, which no browser sends here.
//
// Every route answers the same 404 until SHARE_ENABLED is "1", which is set only after the founder
// ratifies sharing and provisions its database; merging this into main deploys it switched off.
//
// Unknown, spent, expired and revoked shares, and a wrong revoke token, all answer the one 404, so
// a caller cannot tell a share that never existed from one somebody opened. Logs name an error's
// name, code and message only: never an id, an envelope or an address.
import postgres from "postgres";

const ORIGINS = new Set(["https://keypaste.com", "https://www.keypaste.com"]);
const MAX_BODY = 16384;
const MAX_CT = 12000;
const ID = /^[A-Za-z0-9_-]{22}$/;
const B64URL = /^[A-Za-z0-9_-]+$/;
const SHA256_HEX = /^[0-9a-f]{64}$/;
const LOCAL_HOSTS = new Set(["localhost", "127.0.0.1"]);

const API_HEADERS = {
  "content-type": "application/json",
  "cache-control": "no-store",
  "x-content-type-options": "nosniff",
  "referrer-policy": "no-referrer",
};

// The same as public/_headers, set here too because the Worker answers /s/* before the asset layer.
const VIEWER_HEADERS = {
  "content-security-policy":
    "default-src 'none'; script-src 'self'; style-src 'self'; font-src 'self'; img-src 'self' data:; " +
    "connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'",
  "referrer-policy": "no-referrer",
  "x-content-type-options": "nosniff",
  "cache-control": "no-store",
};

// Development only: reached solely with SHARE_DEV_MEMORY=1 on a localhost request. Exported so the
// tests can age a share past its expiry.
export const memory = new Map();

export function isShareRoute(pathname) {
  return pathname === "/api/share" || pathname.startsWith("/api/share/") || pathname === "/s" || pathname.startsWith("/s/");
}

export async function handleShare(request, env, ctx) {
  const url = new URL(request.url);

  if (env.SHARE_ENABLED !== "1") return notFound();

  if (url.pathname === "/s" || url.pathname.startsWith("/s/")) return viewer(request, env);

  const route = match(request.method, url.pathname);
  if (!route) return notFound();

  const origin = request.headers.get("origin");
  if (origin && !ORIGINS.has(origin) && origin !== url.origin) return json(403, { error: "forbidden" });

  const limiter = route.kind === "create" || route.kind === "revoke" ? env.SHARE_CREATE_LIMIT : env.SHARE_OPEN_LIMIT;
  if (limiter && !(await limiter.limit({ key: request.headers.get("cf-connecting-ip") ?? "unknown" })).success) {
    return json(429, { error: "too many requests" });
  }

  const store = openStore(env, url, ctx);
  if (!store) return json(503, { error: "sharing is not available" });

  try {
    switch (route.kind) {
      case "create":
        return await create(request, store);
      case "status":
        return await status(route.id, store);
      case "open":
        return await open(route.id, store);
      default:
        return await revoke(request, route.id, store);
    }
  } catch (error) {
    console.error("share failed:", error?.name, error?.code, error?.message);
    return json(503, { error: "sharing is not available" });
  } finally {
    store.end?.();
  }
}

export async function sweepShares(env, ctx) {
  if (env.SHARE_DB) {
    const store = postgresStore(env, ctx);
    try {
      await store.sweep();
    } finally {
      store.end();
    }
    return;
  }

  memoryStore().sweep();
}

function match(method, pathname) {
  if (pathname === "/api/share") return method === "POST" ? { kind: "create" } : null;

  const parts = pathname.slice("/api/share/".length).split("/");
  if (!ID.test(parts[0])) return null;

  if (parts.length === 1 && method === "GET") return { kind: "status", id: parts[0] };
  if (parts.length === 1 && method === "DELETE") return { kind: "revoke", id: parts[0] };
  if (parts.length === 2 && parts[1] === "open" && method === "POST") return { kind: "open", id: parts[0] };
  return null;
}

async function viewer(request, env) {
  const asset = await env.ASSETS.fetch(request);
  const response = new Response(asset.body, asset);
  for (const [name, value] of Object.entries(VIEWER_HEADERS)) response.headers.set(name, value);
  return response;
}

async function create(request, store) {
  if (!(request.headers.get("content-type") ?? "").startsWith("application/json")) {
    return json(400, { error: "the body must be JSON" });
  }
  if (Number(request.headers.get("content-length") ?? 0) > MAX_BODY) return json(413, { error: "too large" });

  const bytes = await request.arrayBuffer();
  if (bytes.byteLength > MAX_BODY) return json(413, { error: "too large" });

  let body;
  try {
    body = JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes));
  } catch {
    return json(400, { error: "the body must be JSON" });
  }

  const envelope = validEnvelope(body?.envelope);
  if (!envelope) return json(400, { error: "the envelope is not one keypaste makes" });
  if (!Number.isInteger(body.views) || body.views < 1 || body.views > 10) {
    return json(400, { error: "views must be 1 to 10" });
  }
  if (!Number.isInteger(body.ttl_seconds) || body.ttl_seconds < 300 || body.ttl_seconds > 604800) {
    return json(400, { error: "ttl_seconds must be 300 to 604800" });
  }
  if (typeof body.revoke_sha256 !== "string" || !SHA256_HEX.test(body.revoke_sha256)) {
    return json(400, { error: "revoke_sha256 must be 64 lowercase hex characters" });
  }

  const id = newId();
  const expiresAt = await store.create({
    id,
    envelope: JSON.stringify(envelope),
    views: body.views,
    ttl: body.ttl_seconds,
    revokeSha256: body.revoke_sha256,
    passphrase: envelope.kdf !== null,
  });

  return json(201, { id, expires_at: new Date(expiresAt).toISOString() });
}

async function status(id, store) {
  const row = await store.status(id);
  if (!row) return notFound();

  const { kdf, check_iv, check } = JSON.parse(row.envelope);
  return json(200, { views_left: row.views_left, expires_at: new Date(row.expires_at).toISOString(), kdf, check_iv, check });
}

async function open(id, store) {
  const row = await store.open(id);
  if (!row) return notFound();

  return json(200, { envelope: JSON.parse(row.envelope), views_left: row.views_left });
}

async function revoke(request, id, store) {
  const authorization = request.headers.get("authorization") ?? "";
  const token = authorization.startsWith("Bearer ") ? authorization.slice("Bearer ".length) : "";
  if (!token || token.length > 128) return notFound();

  const removed = await store.revoke(id, await sha256Hex(token));
  return removed ? new Response(null, { status: 204, headers: API_HEADERS }) : notFound();
}

// Re-serialized from the validated fields only, so nothing else a client sent is ever stored.
function validEnvelope(envelope) {
  if (!envelope || typeof envelope !== "object" || Array.isArray(envelope)) return null;
  if (envelope.v !== 1 || envelope.alg !== "A256GCM") return null;
  if (!b64url(envelope.iv, 16) || !b64url(envelope.check_iv, 16) || !b64url(envelope.check, 22)) return null;
  if (typeof envelope.ct !== "string" || envelope.ct.length < 22 || envelope.ct.length > MAX_CT || !B64URL.test(envelope.ct)) {
    return null;
  }

  let kdf = null;
  if (envelope.kdf !== null) {
    const { name, iterations, salt } = envelope.kdf ?? {};
    if (name !== "PBKDF2-SHA256" || !Number.isInteger(iterations) || iterations < 100000 || iterations > 10000000) {
      return null;
    }
    if (!b64url(salt, 22)) return null;
    kdf = { name, iterations, salt };
  }

  return { v: 1, alg: "A256GCM", kdf, iv: envelope.iv, ct: envelope.ct, check_iv: envelope.check_iv, check: envelope.check };
}

function b64url(value, length) {
  return typeof value === "string" && value.length === length && B64URL.test(value);
}

function newId() {
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  return btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

async function sha256Hex(text) {
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text)));
  return Array.from(digest, (b) => b.toString(16).padStart(2, "0")).join("");
}

function openStore(env, url, ctx) {
  if (env.SHARE_DB) return postgresStore(env, ctx);
  if (env.SHARE_DEV_MEMORY === "1" && LOCAL_HOSTS.has(url.hostname)) return memoryStore();
  return null;
}

function postgresStore(env, ctx) {
  return sqlStore(postgres(env.SHARE_DB.connectionString, { fetch_types: false }), ctx);
}

// Exported so the tests can run the SQL paths against a stand-in for the driver.
export function sqlStore(sql, ctx) {
  return {
    async create({ id, envelope, views, ttl, revokeSha256, passphrase }) {
      const [row] = await sql`
        insert into public.share (id, envelope, views_left, expires_at, revoke_sha256, passphrase)
        values (${id}, ${envelope}, ${views}, now() + make_interval(secs => ${ttl}), ${revokeSha256}, ${passphrase})
        returning expires_at
      `;
      return row.expires_at;
    },
    async status(id) {
      const [row] = await sql`
        select views_left, expires_at, envelope from public.share
        where id = ${id} and views_left > 0 and expires_at > now()
      `;
      return row ?? null;
    },
    async open(id) {
      const [row] = await sql`
        update public.share set views_left = views_left - 1
        where id = ${id} and views_left > 0 and expires_at > now()
        returning envelope, views_left
      `;
      // The view is spent once the update commits; if this cleanup fails, the sweep deletes the row.
      if (row && row.views_left === 0) {
        try {
          await sql`delete from public.share where id = ${id} and views_left = 0`;
        } catch (error) {
          console.error("share cleanup failed:", error?.name, error?.code, error?.message);
        }
      }
      return row ?? null;
    },
    async revoke(id, revokeSha256) {
      const result = await sql`delete from public.share where id = ${id} and revoke_sha256 = ${revokeSha256}`;
      return result.count > 0;
    },
    async sweep() {
      await sql`delete from public.share where expires_at <= now() or views_left = 0`;
    },
    end() {
      ctx.waitUntil(sql.end());
    },
  };
}

function memoryStore() {
  const live = (row) => row && row.views_left > 0 && row.expires_at > Date.now();

  return {
    async create({ id, envelope, views, ttl, revokeSha256 }) {
      const expiresAt = Date.now() + ttl * 1000;
      memory.set(id, { envelope, views_left: views, expires_at: expiresAt, revoke_sha256: revokeSha256 });
      return expiresAt;
    },
    async status(id) {
      const row = memory.get(id);
      return live(row) ? row : null;
    },
    async open(id) {
      const row = memory.get(id);
      if (!live(row)) return null;
      row.views_left -= 1;
      if (row.views_left === 0) memory.delete(id);
      return { envelope: row.envelope, views_left: row.views_left };
    },
    async revoke(id, revokeSha256) {
      const row = memory.get(id);
      if (!row || row.revoke_sha256 !== revokeSha256) return false;
      memory.delete(id);
      return true;
    },
    sweep() {
      for (const [id, row] of memory) if (!live(row)) memory.delete(id);
    },
  };
}

function json(status, body) {
  return new Response(JSON.stringify(body), { status, headers: API_HEADERS });
}

function notFound() {
  return json(404, { error: "not found" });
}
