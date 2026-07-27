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
the reason for the whole arrangement is gone and `DECISIONS.md` D-0037 is wrong.

### The config that exists today is half-fixed

Hyperdrive config `9ef85ab258e846fbb2c0d3457b744282` was created through the Cloudflare dashboard's
PlanetScale integration rather than by the steps above, and it started out wrong in two ways. One
is fixed; the other is not, and it is the one that matters.

**TLS: fixed, and verified against the database.** PlanetScale serves a Let's Encrypt chain, so
ISRG Root X1 was uploaded (`wrangler cert upload certificate-authority`, id
`f8411755-7948-4b31-aa11-2a79710ce1d4`) and the config set to `--sslmode verify-full`. Cloudflare
rejects `verify-full` outright without a CA, so this is not a setting that can be silently ignored.
A query through the binding then succeeded, which is the part worth trusting: the mode is real and
it did not break the connection. Note the update appears to have detached the config from the
PlanetScale integration.

**The role: still wrong. Do not deploy.** The config connects as `pscale_api_yq4xhf9tbm3v`, which a
query through the binding reports is `rolcreaterole`, `rolcreatedb`, `rolbypassrls`, and a member of
`pg_read_all_data`, `pg_write_all_data` and `postgres`. It can read and write every table in the
database. **The guarantee `schema.sql` and D-0037 are built on — that nothing reachable from the
Worker can read the list back — is currently false**, and by a wider margin than "it probably has
SELECT".

`public.signup` does not exist yet either; `schema.sql` has never been applied. The same query
confirms `rolcreaterole` is available, so `CREATE ROLE signup_writer` will work — the least-
privilege design is viable, it simply has not been done. Run steps 1 and 3 above, using
`wrangler hyperdrive update <id> --origin-user signup_writer --origin-password …` in place of
`create`, then re-run step 5 to confirm `verify-full` survived the update.

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
