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

The role behind the connection is meant to be one that can `INSERT` into `public.signup` and cannot
`SELECT` from it, so that a compromised Worker — or a compromised dependency inside it — cannot read
the list back. `schema.sql` is where that is set up and why. Do not go looking for a role called
`signup_writer`: PlanetScale discards the name you ask for and issues its own `pscale_<id>`.
**As of today the deployed config does not use such a role at all** — see "The config that exists
today is half-fixed" below before trusting the paragraph above.

## Setting it up, once

**The order below is not arbitrary and the old version of this list had it wrong.** The role has to
exist before `schema.sql` can grant anything to it, because the grants name it — and `schema.sql`
cannot create it, since on PlanetScale a role created with raw SQL is invisible to `pscale role`,
the dashboard, and rotation. So: role, then table and grants, then swap the connection to it.

```sh
# 1. Create the managed role with NO inherited roles. PlanetScale discards the name you type and
#    prints a generated one of the form pscale_<id>; that generated name is what schema.sql wants.
pscale role create <database> <branch> keypaste-signup --inherited-roles ''

# 2. Create the table and grant that role INSERT on it, and nothing else. Substitute the generated
#    name for <ROLE> in schema.sql first - applying the file untouched fails on a role that does
#    not exist. Uses an admin credential that is not stored anywhere in this repository.
psql "postgresql://ADMIN@aws-us-east-2-1.pg.psdb.cloud:5432/postgres?sslmode=verify-full" \
     -f schema.sql

# 3. Upload the database's CA chain, so Hyperdrive can verify the server certificate rather than
#    merely encrypt to it. *.pem is gitignored; keep the file out of the repository anyway.
npx wrangler cert upload certificate-authority --ca-cert ca.pem --name planetscale-pg-ca

# 4. Point Hyperdrive at that role and NOT at an admin user. Append the BRANCH ID to the username:
#    PlanetScale's proxy routes on it, and omitting it fails to authenticate for reasons that look
#    nothing like the cause. Inside the database current_user is still the bare pscale_<id>.
npx wrangler hyperdrive create keypaste-signup \
  --connection-string="postgresql://pscale_<id>.<branch-id>:PASSWORD@aws-us-east-2-1.pg.psdb.cloud:5432/postgres" \
  --ca-certificate-id <CA_CERT_ID> \
  --sslmode verify-full

# 5. Put the returned id into wrangler.jsonc, replacing REPLACE_WITH_HYPERDRIVE_ID.

# 6. Confirm the config has the sslmode you asked for rather than one it fell back to.
npx wrangler hyperdrive get <HYPERDRIVE_ID>
```

**Steps 2 and 4 leave a window, and it is worth closing on purpose.** Between them the table exists
while the Worker still connects as whatever role it connected as before — so a signup that arrives
in the gap lands in a table that role can read, which is exactly the guarantee this arrangement
exists to provide. Do the two in one sitting, and check `public.signup` is empty afterwards if the
gap was longer than a minute. Before step 2 there is no window at all, because a signup against a
missing table fails and the Worker says so.

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
it did not break the connection. An earlier version of this note guessed that the update had
detached the config from the PlanetScale integration; `wrangler hyperdrive get` says otherwise —
`integration_name: planetScale` and the organisation and database names are all still on it.

**The role: still wrong. Do not deploy.** The config connects as `pscale_api_yq4xhf9tbm3v`, which a
query through the binding reports is `rolcreaterole`, `rolcreatedb`, `rolbypassrls`, and a member of
`pg_read_all_data`, `pg_write_all_data` and `postgres`. It can read and write every table in the
cluster. **The guarantee `schema.sql` and D-0037 are built on — that nothing reachable from the
Worker can read the list back — is currently false**, and by a wider margin than "it probably has
SELECT".

`public.signup` does not exist yet either; `schema.sql` has never been applied.

Two things about PlanetScale usernames, both easy to get wrong:

- `pscale_api_yq4xhf9tbm3v` is not an API or admin credential despite the name. It is the generated
  username of an ordinary *managed role* — PlanetScale discards the name you type and issues its
  own. The problem is its inherited roles, not what it is called. `pscale role list <db> <branch>`
  is the authoritative view of that.
- The `.jb6eu3wgh2u3` suffix is the **branch id, not part of the credential**. PlanetScale's proxy
  routes on the username, so you connect as `<role>.<branch-id>` while `current_user` inside the
  database is the bare `<role>`. A `wrangler hyperdrive update --origin-user` that omits the suffix
  will fail to authenticate for reasons that look nothing like the cause.

**What is live right now, established by probing it rather than assumed.** The Worker is deployed:
`GET /subscribe` answers `303` to `/`, which is its own non-POST branch, and an unknown path gets
the static `404`. So the form reaches real code. That code inserts into `public.signup`, which does
not exist, so it throws — and the handler catches it and returns a `503` page that says in as many
words that the address was **not** stored. Nothing is being silently dropped. Nothing is being
stored either, and every signup since the deploy has failed that way.

That ordering is lucky rather than designed, and it is why there is no leak to clean up: the
over-privileged role can read every table in the cluster, but there is no subscriber table yet for
it to read. **Which settles the sequencing question — the role must be swapped before, or in the
same sitting as, the table being created.** Creating the table first would mean the first real
signup lands somewhere the guarantee does not hold.

So: steps 1, 2 and 4 of the setup list above, in that order, then:

```sh
npx wrangler hyperdrive update 9ef85ab258e846fbb2c0d3457b744282 \
  --origin-user '<pscale_role_id>.<branch-id>' --origin-password '<generated>'
npx wrangler hyperdrive get 9ef85ab258e846fbb2c0d3457b744282   # verify-full must survive
```

Then run the by-hand checks below, because a successful signup is the only thing that proves both
halves at once: the row lands, and the role that landed it cannot read it back.

**One thing in `schema.sql` to look at before running it.** Its last line is
`REVOKE CONNECT ON DATABASE postgres FROM PUBLIC`, which is correct in intent — PlanetScale grants
CONNECT to PUBLIC on a new database, meaning every current and future role. But this is the
`postgres` maintenance database on a cluster PlanetScale manages, so anything of theirs that
connects through PUBLIC rather than an explicit grant loses access at that moment. Run `\du` and
check which roles exist and what they inherit before revoking, and be ready to grant CONNECT back
explicitly. It is the one line in the file that can affect something other than this application.

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
