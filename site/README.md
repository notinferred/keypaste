# keypaste.com

A single static page and one form endpoint, deployed to Cloudflare Workers by hand. `public/` is
the site; `src/worker.js` is the only server-side code, and it is reached for `/subscribe` and
nothing else.

The page loads no third-party scripts, sets no cookies, and runs no JavaScript of its own. The
signup form is a plain `<form method="post">` that redirects to a static `/thanks/` page, which is
why all of that is true at once.

## Where the database password is

**Not here, and not in the Worker.** It lives in an account-level Cloudflare Hyperdrive config.
`wrangler.jsonc` carries only that config's `id`, which is a handle and is useless without access
to the Cloudflare account. There is no `wrangler secret` in this setup and there should not be one.

The role behind the connection is `signup_writer`, which can `INSERT` into `public.signup` and
cannot `SELECT` from it. A compromised Worker, or a compromised dependency inside it, cannot read
the list back. `schema.sql` is where that is set up and why.

## Setting it up, once

```sh
# 1. Create the table and the write-only role. Uses an admin credential that is not stored
#    anywhere in this repository, and is not needed again after this.
psql "postgresql://ADMIN@aws-us-east-2-1.pg.psdb.cloud:5432/postgres?sslmode=verify-full" \
     -f schema.sql

# 2. Upload the database's CA chain, so Hyperdrive can verify the server certificate rather than
#    merely encrypt to it. *.pem is gitignored; keep the file out of the repository anyway.
npx wrangler cert upload certificate-authority --ca-cert ca.pem --name planetscale-pg-ca

# 3. Create the Hyperdrive config, pointing at signup_writer and NOT at the admin user.
npx wrangler hyperdrive create keypaste-signup \
  --connection-string="postgresql://signup_writer:PASSWORD@aws-us-east-2-1.pg.psdb.cloud:5432/postgres" \
  --ca-certificate-id <CA_CERT_ID> \
  --sslmode verify-full

# 4. Put the returned id into wrangler.jsonc, replacing REPLACE_WITH_HYPERDRIVE_ID.

# 5. Confirm the config has the sslmode you asked for rather than one it fell back to.
npx wrangler hyperdrive get <HYPERDRIVE_ID>
```

Step 5 is not a formality. Hyperdrive exists in this design specifically so the connection is
`verify-full` — a Worker connecting directly has no system CA store, so the honest setting there is
`require`, which encrypts without authenticating the server. If step 5 does not say `verify-full`,
the reason for the whole arrangement is gone and `DECISIONS.md` D-0036 is wrong.

## Deploying

```sh
npm ci
npx wrangler deploy
```

## Running it locally

```sh
CLOUDFLARE_HYPERDRIVE_LOCAL_CONNECTION_STRING_HYPERDRIVE="postgresql://..." npx wrangler dev
```

The environment-variable form, rather than `localConnectionString` in `wrangler.jsonc`, because the
second one puts a real password in a tracked file. Point it at a scratch database if you have one.

Worth checking by hand before a deploy, because none of it is in CI:

- Submitting a valid address redirects to `/thanks/` and the row appears.
- Submitting the same address again still redirects cleanly and adds nothing.
- Submitting with the `website` field filled redirects to `/thanks/` and adds nothing.
- Submitting nonsense gets a `400` page that says what was wrong.
- The page still works with JavaScript disabled in the browser. If it does not, the promise the
  footer makes is no longer true.
- View source: no `<script>` tags, no external origins.

## What is not here

No CI job. `.github/workflows/ci.yml` is the .NET gate and does not look at this directory, which
is a deliberate choice for ninety lines of JavaScript deployed by hand — and the reason the manual
checks above are written down rather than assumed.

No rate limiting in code. That belongs in a Cloudflare rule on `POST /subscribe`; check whether the
account's plan actually offers one before treating it as the defence, because the honeypot and the
body, content-type and origin guards are what is genuinely shipped.

No Turnstile and no managed challenge. Both inject a script, and the page says it does not load
one. See `DECISIONS.md` D-0036 for the rest of what was deliberately left out.
