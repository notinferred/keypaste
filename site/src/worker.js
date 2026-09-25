// keypaste.com's server-side code: the signup form's one INSERT, and share links in share.js.
//
// Static assets win by default, so this Worker is reached for /subscribe, /api/* and /s/* (declared
// in wrangler.jsonc under assets.run_worker_first) and for nothing else that exists on disk.
//
// The signup page it serves has no JavaScript, so this endpoint is reached by a plain form navigation and
// answers with a redirect. Success goes to a static /thanks/ page rather than HTML built here, so
// the site's markup stays in one language and cannot rot in two places.
//
// The database credential is not in this repository and is not in this Worker's environment. It
// lives in an account-level Hyperdrive config; env.HYPERDRIVE.connectionString is a local handle.
// The role behind it can INSERT into one table and cannot SELECT from it, so nothing reachable from
// here can read the list back. See DECISIONS.md D-0036 and site/README.md.
import postgres from "postgres";
import { handleShare, isShareRoute, readCapped, sweepShares } from "./share.js";

const ORIGINS = new Set(["https://keypaste.com", "https://www.keypaste.com"]);
const MAX_BODY = 1024;

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);

    if (isShareRoute(url.pathname)) {
      return handleShare(request, env, ctx);
    }

    if (url.pathname !== "/subscribe") {
      return new Response("Not found\n", { status: 404 });
    }
    if (request.method !== "POST") {
      return seeOther("/");
    }

    try {
      return await subscribe(request, env, ctx);
    } catch (error) {
      // Name, code and message only. Logging the error object would print the driver's options,
      // and an address in a log line would contradict what the page promises about it.
      console.error("subscribe failed:", error?.name, error?.code, error?.message);
      return page(
        503,
        "That did not save",
        "The database did not answer, so your address was not stored — this page would rather " +
          "say so than thank you for nothing. Try again in a minute. Watching the repository on " +
          "GitHub works just as well.",
      );
    }
  },

  async scheduled(controller, env, ctx) {
    ctx.waitUntil(sweepShares(env, ctx));
  },
};

async function subscribe(request, env, ctx) {
  const origin = request.headers.get("origin");
  if (origin && !ORIGINS.has(origin)) {
    return refuse("That submission did not come from keypaste.com.");
  }

  const contentType = request.headers.get("content-type") ?? "";
  if (!contentType.startsWith("application/x-www-form-urlencoded")) {
    return refuse("That submission was not a form.");
  }

  const raw = await readCapped(request, MAX_BODY);
  if (!raw) {
    return refuse("That submission was too large.");
  }
  const body = new TextDecoder().decode(raw);

  const form = new URLSearchParams(body);

  // Offscreen in CSS rather than hidden in HTML, because a bot skips type="hidden" and fills this.
  // A caught bot is told the same thing a person is told; anything else teaches it what tripped.
  if ((form.get("website") ?? "") !== "") {
    return seeOther("/thanks/");
  }

  const email = normalize(form.get("email") ?? "");
  if (email === null) {
    return refuse("That does not look like an email address.");
  }

  const sql = postgres(env.HYPERDRIVE.connectionString, { fetch_types: false });
  try {
    // A tagged template, so the address is a bound parameter, never concatenated into SQL. And no
    // RETURNING: that would need SELECT on the table, the one privilege this role deliberately
    // lacks, so asking for it would undo the reason the role exists.
    //
    // `on conflict do nothing` with NO inference specification, and that is load-bearing. Naming
    // the arbiter - `on conflict (email) do nothing` - makes PostgreSQL 18 require SELECT on the
    // table to resolve it, and the role does not have SELECT, so the insert fails with 42501 and
    // the visitor gets the 503 page. Measured against the live database, not assumed. The bare
    // form needs only INSERT.
    //
    // The trade is that this swallows a conflict on *any* constraint rather than specifically the
    // primary key. Today `email` is the only one, so the behaviour is identical; adding a second
    // constraint later means revisiting this line rather than inheriting a silent no-op.
    await sql`
      insert into public.signup (email, source)
      values (${email}, 'site')
      on conflict do nothing
    `;
  } finally {
    ctx.waitUntil(sql.end());
  }

  // A duplicate lands here too. The address is on the list, which is what the next page says.
  return seeOther("/thanks/");
}

// Deliberately loose. Every regex that claims to implement RFC 5322 is wrong, and turning away a
// real subscriber costs more than storing a junk row. The browser's type="email" is a convenience;
// this is the trust boundary and re-checks regardless.
function normalize(raw) {
  const email = raw.trim().toLowerCase();
  if (email.length < 3 || email.length > 254) return null;
  if (/[\s<>",;\\]/.test(email)) return null;

  const at = email.indexOf("@");
  if (at < 1 || at > 64 || at !== email.lastIndexOf("@")) return null;

  const domain = email.slice(at + 1);
  if (domain.length < 3 || domain.length > 253) return null;
  if (!domain.includes(".") || domain.startsWith(".") || domain.endsWith(".")) return null;
  if (domain.includes("..")) return null;

  return email;
}

const seeOther = (location) => new Response(null, { status: 303, headers: { location } });

const refuse = (why) => page(400, "That did not go through", why);

// Every string reaching this function is a literal above it. Nothing a visitor typed is
// interpolated into the markup, which is the only reason building HTML this way is safe here.
function page(status, heading, body) {
  const html = `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>keypaste — ${heading}</title>
<meta name="robots" content="noindex">
<meta name="color-scheme" content="dark light">
<link rel="icon" href="/favicon.svg" type="image/svg+xml">
<link rel="stylesheet" href="/brand.css">
<style>
  main { max-width: 34rem; margin: 0 auto; padding: 48px 16px 64px; }
  .lockup { margin-bottom: 32px; }
  .card { padding: 28px; border: 1px solid var(--border-subtle); border-radius: 14px; background: var(--bg-panel); }
  h1 { margin: 0 0 12px; font-size: 24px; font-weight: 600; letter-spacing: -0.03em; }
  p { margin: 0 0 16px; color: var(--text-secondary); font-size: 15px; }
</style>
</head>
<body>
<main>
  <a class="lockup" href="/" aria-label="keypaste home">
    <svg width="24" height="24" viewBox="0 0 64 64" aria-hidden="true"><rect class="mark-stem" x="10" y="8" width="10" height="48"/><polygon class="mark-arm" points="40,24 54,24 38,40 54,56 40,56 24,40"/></svg>
    <span>keypaste</span>
  </a>
  <section class="card">
    <h1>${heading}</h1>
    <p>${body}</p>
    <a class="button primary" href="/">Back to keypaste.com</a>
  </section>
</main>
</body>
</html>
`;
  return new Response(html, { status, headers: { "content-type": "text/html; charset=utf-8" } });
}
