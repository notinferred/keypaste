# keypaste.com

Two static pages and one form endpoint, deployed to Cloudflare Workers by Cloudflare's Git integration on a push to `main`. `public/` is the site; `src/worker.js` handles `/subscribe` and returns 404 for other unmatched paths.

This is the waitlist site, not the planned hosted vault service. **Last recorded live verification: 2026-07-28**, in D-0037 of [DECISIONS.md](../DECISIONS.md). The documentation review on 2026-09-07 checked source and provider documentation, not the live account, database permissions or deployed behavior. Repeat the checks below before relying on that historical deployment evidence.

The page loads no third-party scripts, sets no cookies, and runs no JavaScript of its own. The signup form is a plain `<form method="post">` that redirects to a static `/thanks/` page, which is why all of that is true at once.

## Where the database password is

**Not here, and not in the Worker.** It lives in an account-level Cloudflare Hyperdrive config. `wrangler.jsonc` carries only that config's `id`, which is a handle and is useless without access to the Cloudflare account. There is no `wrangler secret` in this setup and there should not be one.

The recorded connection role is `keypaste_signup_writer`, with `INSERT` on `public.signup` and no `SELECT`. The July probe confirmed reads were refused with 42501. This limits access to stored subscribers; it cannot protect a newly submitted address from a compromised handler processing that submission. `schema.sql` defines the intended grants; check effective permissions after each role or connection change.

The recorded role was created with SQL. PlanetScale-managed roles have dashboard and CLI lifecycle controls; SQL-created roles require SQL administration and a routing suffix on the connection username. For a new setup, use the managed-role path below and verify its grants. See [PlanetScale role management](https://planetscale.com/docs/postgres/connecting/roles).

## Setting it up, once

Run these shell examples from `site/` after installing dependencies with `npm ci`. The role must exist before the grants name it. `schema.sql` creates the table and grants access; it contains no role-creation statement. Use the provider's returned database host, username and branch identifier, replacing every placeholder. Supply credentials through your approved local secret workflow and keep them out of tracked files.

```sh
# 1. Create a managed role with NO inherited roles. Use the returned SQL username in schema.sql,
#    not its display name. Confirm effective memberships before connecting the Worker.
pscale role create <database> <branch> keypaste-signup --inherited-roles ''

# 2. Create the table and grant that role INSERT on it, and nothing else. Substitute the generated
#    name for <ROLE> in schema.sql first - applying the file untouched fails on a role that does
#    not exist. Uses an admin credential that is not stored anywhere in this repository.
psql "postgresql://ADMIN@aws-us-east-2-1.pg.psdb.cloud:5432/postgres?sslmode=verify-full" \
     -f schema.sql

# 3. Upload the CA certificate required for this connection's explicit verify-full policy.
#    Obtain the correct current CA from the provider; do not assume the historical CA still applies.
npx wrangler cert upload certificate-authority --ca-cert ca.pem --name planetscale-pg-ca

# 4. Point Hyperdrive at that role and NOT at an admin user. Append the BRANCH ID to the username:
#    PlanetScale's proxy routes on it, and omitting it fails to authenticate for reasons that look
#    nothing like the cause. Inside the database current_user is still the bare pscale_<id>.
npx wrangler hyperdrive create keypaste-signup \
  --connection-string="postgresql://pscale_<id>.<branch-id>:PASSWORD@aws-us-east-2-1.pg.psdb.cloud:5432/postgres" \
  --ca-certificate-id <CA_CERT_ID> \
  --sslmode verify-full

# 5. Put the returned id into wrangler.jsonc's HYPERDRIVE binding; it currently records the old config.

# 6. Confirm the config has the sslmode you asked for rather than one it fell back to.
npx wrangler hyperdrive get <HYPERDRIVE_ID>
```

For a fresh setup, enable public intake only after the restricted connection passes verification. For an existing deployment, verify the current role before changing anything and pause intake if it has excessive privileges. Creating a restricted replacement does not remove the privileges of the connection still serving requests.

**Step 6 verifies the connection policy.** This deployment requires explicit `verify-full` with its configured CA and a successful query. Current Cloudflare documentation says Hyperdrive's default `require` mode also validates certificates through WebPKI; the older claim that it only encrypts was too broad. The selected `verify-full` mode adds explicit CA and hostname checks. See [Hyperdrive TLS configuration](https://developers.cloudflare.com/hyperdrive/configuration/tls-ssl-certificates-for-hyperdrive/) and [Wrangler Hyperdrive commands](https://developers.cloudflare.com/hyperdrive/reference/wrangler-commands/).

### How the config was wrong, and how it was found

Hyperdrive config `9ef85ab258e846fbb2c0d3457b744282` was created through the Cloudflare dashboard's PlanetScale integration rather than by the steps above, and it started out wrong in two ways. Both were recorded as fixed on 2026-07-28; this section preserves that troubleshooting history.

**TLS: verified against the database on 2026-07-28.** That deployment served a Let's Encrypt chain, so ISRG Root X1 was uploaded (`wrangler cert upload certificate-authority`, id `f8411755-7948-4b31-aa11-2a79710ce1d4`) and the config set to `--sslmode verify-full`. The configured CA and SSL mode were checked explicitly. A query through the binding then succeeded, which is the part worth trusting: the mode is real and it did not break the connection. An earlier version of this note guessed that the update had detached the config from the PlanetScale integration; `wrangler hyperdrive get` says otherwise — `integration_name: planetScale` and the organisation and database names are all still on it.

## Last recorded live checks — 2026-07-28

**The recorded Hyperdrive connection used `keypaste_signup_writer.jb6eu3wgh2u3`** — the role `keypaste_signup_writer` (a plain SQL role created with `CREATE ROLE`, which is why it kept the name typed; the managed path above would have issued a `pscale_<id>` one) with `INSERT` on `public.signup` and nothing else: no superuser, no `bypassrls`, `NOINHERIT`, zero role memberships. As that role, `select`, `count(*)`, `returning`, `update`, `delete` and reading any other table are refused with 42501. **`public.signup` exists**; `schema.sql` was applied on 2026-07-28. A live submission returns 303 to `/thanks/` and the row lands; a duplicate is a no-op; the honeypot stores nothing; nonsense, a wrong `Origin` and a non-form body each get 400. `CONNECT` on the database is no longer held by PUBLIC. Verified end to end on 2026-07-28, and `D-0037` is the record.

The role the PlanetScale integration first handed the config inherited `postgres` and through it `pscale_superuser` — logical replication plus write access everywhere, reachable from a public HTTP endpoint. It was swapped before the table was created, so no subscriber row was ever reachable by it; every submission before that point returned the handler's 503 saying the address was not stored. **Never let a Hyperdrive config keep whatever role an integration wizard hands it.**

Two things bit during the fix and are worth knowing before touching this again. **The July credential update was observed to wipe the `mtls` block**, dropping the CA and `verify-full`. After any update, verify both the CA reference and SSL mode with `wrangler hyperdrive get`, reapply missing settings, and run a restricted-role query before resuming intake. And **naming the conflict target in `ON CONFLICT (email) DO NOTHING` requires SELECT** on PostgreSQL 18; the bare form is what ships, and `schema.sql` says so.

Two things about PlanetScale usernames, both easy to get wrong: a `pscale_<id>` name is an ordinary managed role and not an API credential — the problem with the first one was its inherited roles, not its name; and the `.<branch-id>` suffix is routing, not part of the credential, so `current_user` inside the database is the bare role and a `--origin-user` that omits the suffix fails to authenticate for reasons that look nothing like the cause.

Recorded follow-up: migrate the SQL role to a managed role with no inherited roles and the grants in `schema.sql`. Check the current role and subscriber state before scheduling the change. Verify the replacement, update the connection with its CA and SSL mode, repeat the permission and submission checks, then retire the old credential. Until migrated, rotation requires SQL administration plus a Hyperdrive credential update.


**One thing in `schema.sql` to look at before running it.** Its last line is `REVOKE CONNECT ON DATABASE postgres FROM PUBLIC`, which is correct in intent — PlanetScale grants CONNECT to PUBLIC on a new database, meaning every current and future role. But this is the `postgres` maintenance database on a cluster PlanetScale manages, so anything of theirs that connects through PUBLIC rather than an explicit grant loses access at that moment. Run `\du` and check which roles exist and what they inherit before revoking, and be ready to grant CONNECT back explicitly. It is the one line in the file that can affect something other than this application.

## Deploying

**Cloudflare's Git integration deploys this, and no workflow in this repository does** (D-0127).
Any push to `main` builds; the settings under Workers → `keypaste-site` → Settings → Build are root
directory `site`, **no build command** — there is no build script and nothing compiles — deploy
command `npm run deploy`, production branch `main`, non-production branch builds off. There is no
output-directory field to set: `wrangler.jsonc`'s `assets.directory` is `./public` and resolves
against the root directory, which is why a project rooted at the repository root served nothing. The
Worker's dashboard name must stay `keypaste-site` to match `wrangler.jsonc`.

**The build watch path decides whether a page change deploys at all**, so it is not a performance
setting here. The file that carries the disclosures is `site/public/index.html`, two levels down, and
a pattern matching only one level would leave every disclosure edit silently undeployed — the exact
defect this arrangement exists to close. Keep it broad enough to cover `site/public/`.

By hand, to deploy a ref Cloudflare will not:

```sh
npm ci
npx wrangler deploy
```

**Then ask the origin, every time, because nothing else will:**

```sh
../scripts/verify-site-disclosure.sh
../scripts/verify-site-endpoint.sh
```

These are the only two things that ask keypaste.com rather than the checkout. They are deliberately
in no workflow — a job here that asked the live origin would go red for a Cloudflare outage it cannot
fix — so running them is yours after any deploy, Cloudflare's included.

### Deployed by hand on 2026-09-11

`site.yml` could not deploy: the `keypaste.com` environment held no `CLOUDFLARE_API_TOKEN`, so all
four of its runs failed at the deploy step, twice with the page already wrong about a data-loss
defect. That is why it is gone, and why the environment went with it on 2026-09-12 — it held a stale
`CLOUDFLARE_ACCOUNT_ID` and nothing else, and a dangling environment holding a credential fragment is
how the next reader concludes CI deploys this. Two versions went out from a checkout instead, each
verified against the live origin afterwards:

| Version id | Tree it carried |
|---|---|
| `463e2856-b29b-43ee-b603-110940ab7fc7` | `cf2df87` — disclosed the `0.2.0` concurrent-save defect |
| `55a31129-c245-40a5-bc27-c16330f4a2e0` | the F.6 commit — added the `10.0.26100` save-failure defect |

The second was deployed from the tree of a commit then called `9de4a16`. **That SHA no longer
exists**: F.6's work was squashed into one commit afterwards, and `site/public/index.html` is
byte-identical across the two, so the squashed commit carries the deployed bytes. An old clone
fetched before the rewrite still has `9de4a16`.

## Running it locally

```sh
CLOUDFLARE_HYPERDRIVE_LOCAL_CONNECTION_STRING_HYPERDRIVE="postgresql://..." npx wrangler dev
```

The environment-variable form, rather than `localConnectionString` in `wrangler.jsonc`, because the second one puts a real password in a tracked file. Point it at a scratch database if you have one.

Run these checks against a scratch database before deployment. Afterwards, the split is what each
check costs to repeat: anything that stores a row needs a scratch database and your judgement, and
everything else is one script.

**Scripted, and yours to run after a deploy** — [verify-site-endpoint.sh](../scripts/verify-site-endpoint.sh). Each
of these is refused or redirected before the Worker opens a database connection, which is the whole
reason they are safe to repeat:

- Submitting with the `website` field filled redirects to `/thanks/` and adds nothing.
- Submitting nonsense gets a `400` page that says what was wrong.
- Submitting something that is not a form, or that comes from another origin, or that is too large,
  each gets a `400` naming its own reason.
- `GET /subscribe` redirects home; an unknown path is a `404`; `/thanks/` is served.
- The served page carries no script, so the promise the footer makes about JavaScript still holds.

**Still by hand, with a dedicated test address** — these write a real row, and the role cannot read
one back, so no script can check them without a credential this repository must never hold:

- Submitting a valid address redirects to `/thanks/` and the row appears.
- Submitting the same address again still redirects cleanly and adds nothing.
- The configured role can insert but cannot select, update or delete subscriber records; the
  connection retains its expected CA and SSL mode.
- The page still works with JavaScript actually disabled in a browser. The automated check proves no
  script is served, which is the mechanism; this is the promise.
- The network panel shows no analytics requests or third-party assets. Ordinary outbound links are
  allowed.

## What is not here

No Worker build job, and no check that reaches the database. Cloudflare deploys, and the two scripts that ask the live page and the live endpoint are run by hand; the .NET workflow separately runs `scripts/verify-demo.sh`, which checks the transcript in `site/public/index.html` at the checked-out ref. None of them validates the database role, its grants or its CA, and none stores a row - those are the by-hand checks above (H-0011).

No email sender or confirmation flow. The current handler stores signup requests. Double opt-in and list verification are planned in step 5.6 of [STEPS](../docs/STEPS.md); do not treat existing rows as confirmed subscribers or claim confirmation mail is already sent.

No rate limiting in code. That belongs in a Cloudflare rule on `POST /subscribe`; check whether the account's plan actually offers one before treating it as the defence, because the honeypot and the body, content-type and origin guards are what is genuinely shipped.

No Turnstile and no managed challenge. Both inject a script, and the page says it does not load one. See `DECISIONS.md` D-0036 for the rest of what was deliberately left out.
