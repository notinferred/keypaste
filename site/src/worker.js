// The only server-side code keypaste.com runs: one form POST, one INSERT.
//
// Static assets win by default, so this Worker is reached for /subscribe (declared in
// wrangler.jsonc under assets.run_worker_first) and for nothing else that exists on disk.
//
// The page it serves has no JavaScript, so this endpoint is reached by a plain form navigation and
// answers with a redirect. Success goes to a static /thanks/ page rather than HTML built here, so
// the site's markup stays in one language and cannot rot in two places.
//
// The database credential is not in this repository and is not in this Worker's environment. It
// lives in an account-level Hyperdrive config; env.HYPERDRIVE.connectionString is a local handle.
// The role behind it can INSERT into one table and cannot SELECT from it, so nothing reachable from
// here can read the list back. See DECISIONS.md D-0036 and site/README.md.
import postgres from "postgres";

const ORIGINS = new Set(["https://keypaste.com", "https://www.keypaste.com"]);
const MAX_BODY = 1024;

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);

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

  if (Number(request.headers.get("content-length") ?? 0) > MAX_BODY) {
    return refuse("That submission was too large.");
  }

  const body = await request.text();
  if (body.length > MAX_BODY) {
    return refuse("That submission was too large.");
  }

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
<meta name="color-scheme" content="light dark">
<style>
  body { margin: 0; background: #fbfbfa; color: #1a1a19;
         font: 17px/1.65 ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; }
  main { max-width: 32rem; margin: 0 auto; padding: 5rem 1.5rem; }
  h1 { font-size: 1.25rem; font-weight: 600; margin: 0 0 1rem; }
  a { color: #3d5a80; }
  @media (prefers-color-scheme: dark) {
    body { background: #141414; color: #ececea; }
    a { color: #8fb0d9; }
  }
</style>
</head>
<body><main>
  <h1>${heading}</h1>
  <p>${body}</p>
  <p><a href="/">Back to keypaste.com</a></p>
</main></body>
</html>
`;
  return new Response(html, { status, headers: { "content-type": "text/html; charset=utf-8" } });
}
