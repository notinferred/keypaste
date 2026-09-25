# keypaste.com

The waitlist site has two static pages, one form endpoint and the share-link routes. Cloudflare's Git integration deploys `site/` on pushes to `main`. `public/` contains the pages; `src/worker.js` handles `/subscribe`, hands `/api/share*` and `/s/*` to `src/share.js`, and returns 404 for unmatched routes. The home and thanks pages use a plain form and load no scripts or cookies; that promise covers those pages, and the share viewer at `/s/` is a separate page that needs JavaScript to decrypt.

Database permissions were last verified on 2026-07-28 (D-0037). Page deployment and endpoint checks were repeated on 2026-09-12 (D-0127). Those checks do not establish current database grants. The focused product is local. Hosted vault services and other expansion ideas are deferred to [BACKLOG](../docs/BACKLOG.md).

## Database connection

The database passwords live in account-level Hyperdrive configurations. `wrangler.jsonc` contains their IDs; no database password or Wrangler secret belongs in the repository or Worker.

Since 2026-09-25 both tables are in one PlanetScale Postgres database on `aws-us-east-1-3.pg.psdb.cloud` (D-0364). `HYPERDRIVE` (`keypaste-signup`, `efcf6358…`) connects as `keypaste_signup_writer`, which has `INSERT` on `public.signup` and no `SELECT`: a compromised handler can still read newly submitted addresses, but not the stored list. `SHARE_DB` (`keypaste-share`, `59f7f117…`) connects as `keypaste_share`, which can read and write `public.share` and nothing else. Both configs are uncached and use `verify-full` against the uploaded ISRG Root X1 (`f8411755…`). `schema.sql` owns the intended grants; verify effective permissions after any role or connection change.

Both roles were created with SQL (`CREATE ROLE … LOGIN NOINHERIT`), not as PlanetScale managed roles, so they do not appear in the dashboard's role list. Rotating one is `ALTER ROLE … PASSWORD` with an admin connection followed by `wrangler hyperdrive update` with the new password, re-checking the CA and SSL mode afterwards. The connection username carries the branch suffix, `<role>.qhhxudm5mg87`.

## Setup

Run examples from `site/` after `npm ci`. Replace placeholders with provider-returned values and supply credentials through your local secret workflow.

Create a managed role without inherited roles. Use its returned SQL username in `schema.sql` and verify its memberships.

```sh
pscale role create <database> <branch> keypaste-signup --inherited-roles ''
```

Apply the table and grants using an admin connection. `schema.sql` does not create the role and must have `<ROLE>` replaced first.

```sh
psql "postgresql://ADMIN@aws-us-east-2-1.pg.psdb.cloud:5432/postgres?sslmode=verify-full" \
     -f schema.sql
```

The file revokes `CONNECT` on the `postgres` database from `PUBLIC`. Check existing roles with `\du` before applying it, because services relying on that grant also lose access. Provide explicit grants where required.

Obtain the provider's applicable CA certificate and upload it for the connection's `verify-full` policy.

```sh
npx wrangler cert upload certificate-authority --ca-cert ca.pem --name planetscale-pg-ca
```

Connect Hyperdrive to the restricted role. PlanetScale requires the `.<branch-id>` routing suffix; inside the database, `current_user` remains the bare role name.

```sh
npx wrangler hyperdrive create keypaste-signup \
  --connection-string="postgresql://pscale_<id>.<branch-id>:PASSWORD@aws-us-east-2-1.pg.psdb.cloud:5432/postgres" \
  --ca-certificate-id <CA_CERT_ID> \
  --sslmode verify-full
```

Put the returned ID into the `HYPERDRIVE` binding in `wrangler.jsonc`, then inspect the configuration.

```sh
npx wrangler hyperdrive get <HYPERDRIVE_ID>
```

Verify the CA reference, `verify-full` mode and a successful restricted-role query before enabling intake. For an existing deployment, inspect the active connection first and pause intake if its privileges are excessive. A replacement configuration does not change the connection still serving requests. See [Hyperdrive TLS configuration](https://developers.cloudflare.com/hyperdrive/configuration/tls-ssl-certificates-for-hyperdrive/) and [Wrangler commands](https://developers.cloudflare.com/hyperdrive/reference/wrangler-commands/).

## Recorded database verification

On 2026-09-25, against the new database, each role was checked by connecting as it: `keypaste_signup_writer` inserted into `signup` inside a rolled-back transaction and was refused (42501) reading `signup` and touching `share`; `keypaste_share` inserted, updated, read and deleted a `share` row and was refused (42501) reading or inserting `signup`. Neither role has memberships. Both Hyperdrive configs report `verify-full` with CA `f8411755…`. `REVOKE CONNECT ON DATABASE postgres FROM PUBLIC` was not applied to this database, because PlanetScale's own roles (`pscale_exporter`, `pscale_pgbouncer`, `pscale_replication`) may rely on it; the table grants are what restrict the Worker's roles.

The rest of this section records the previous database, which is gone along with its Hyperdrive config `9ef85ab2…`. On 2026-07-28, configuration `9ef85ab258e846fbb2c0d3457b744282` used `keypaste_signup_writer.jb6eu3wgh2u3`, with `NOINHERIT`, no memberships, no superuser or `bypassrls`, and only the required insert access. Select, count, returning, update, delete and other-table reads failed with 42501. Valid submissions returned 303 to `/thanks/` and inserted a row; duplicates and honeypots inserted nothing. Invalid input, origin and content type returned 400.

The original integration role inherited `postgres` and `pscale_superuser`. It was replaced before the signup table existed; prior submissions returned 503. Integration-created roles require the same permission review as manually configured roles.

The July TLS check used ISRG Root X1, certificate ID `f8411755-7948-4b31-aa11-2a79710ce1d4`, with `verify-full` and a successful query. The configuration retained its PlanetScale integration metadata. Treat these as historical settings and obtain the applicable CA for a new connection.

A credential update was observed to clear the `mtls` configuration. After updates, inspect and restore the CA and SSL mode, then repeat restricted-role queries. PostgreSQL 18 required select access for `ON CONFLICT (email) DO NOTHING`; the shipped insert uses bare `ON CONFLICT DO NOTHING`.

The recorded follow-up is migration to a managed role without inherited roles. Check the active role and subscriber state, verify replacement grants, update Hyperdrive including TLS settings, repeat permission and submission checks, then retire the old credential. Until then, rotation requires SQL administration and a Hyperdrive update.

## Deploying

Cloudflare's Git integration owns deployment (D-0127). Configure Worker `keypaste-site` with root directory `site`, no build command, deploy command `npm run deploy`, production branch `main` and non-production builds disabled. `assets.directory` is `./public` relative to that root. The build watch path must include nested files under `site/public/`.

For a manual deployment:

```sh
npm ci
npx wrangler deploy
```

After every deployment, run the live checks:

```sh
../scripts/verify-site-disclosure.sh
../scripts/verify-site-endpoint.sh
```

These checks are manual. GitHub workflows inspect checked-out site content without querying the live origin.

## Deployment evidence

On 2026-09-12, build `1716bdb6` from `309aac3` deployed version `1fcc038c` in 24 seconds. Both live checks passed and the origin matched `public/index.html`. The Git connection had retained deleted repository ID `1312113438` after the repository was recreated as `1358644975` on 2026-09-05, so intervening pushes had not deployed. If pushes stop reaching the origin, compare the connection's `repo_id` with `gh api repos/notinferred/keypaste --jq .id`.

The removed `site.yml` workflow had no `CLOUDFLARE_API_TOKEN` and failed to deploy in all four runs. Its environment was removed on 2026-09-12. Two manual deployments on 2026-09-11 were checked against the live origin:

| Version | Content |
|---|---|
| `463e2856-b29b-43ee-b603-110940ab7fc7` | `cf2df87`; concurrent-save defect disclosure |
| `55a31129-c245-40a5-bc27-c16330f4a2e0` | F.6 tree; Windows `10.0.26100` save-failure disclosure |

The second deployment used then-commit `9de4a16`, later replaced by the F.6 squash. Its site bytes match the squashed commit; an older clone may retain the original SHA.

## Local checks

Use an environment variable for a local connection string, keeping credentials out of tracked configuration. Prefer a scratch database.

```sh
CLOUDFLARE_HYPERDRIVE_LOCAL_CONNECTION_STRING_HYPERDRIVE="postgresql://..." npx wrangler dev
```

[verify-site-endpoint.sh](../scripts/verify-site-endpoint.sh) checks honeypot redirection, invalid-form refusals, origin and size limits, route behavior and the absence of scripts. These cases complete before opening the database connection and can run after deployment.

Use a dedicated test address for checks that write rows. Verify valid submissions and duplicate suppression, restricted insert-only grants, CA and SSL settings, browser operation with JavaScript disabled, and absence of analytics or third-party assets. Reading the inserted row requires a separate authorized database role; the Worker role cannot verify it.

## Share links

`src/share.js` serves `keypaste share` links (D-0355). The server stores an encrypted envelope, the views left, the expiry and the SHA-256 of a revoke token; the link's key stays in its fragment and never reaches the Worker. `public/s/` is the viewer, which decrypts in the browser under the content security policy in `public/_headers`, set again by the Worker.

| Method, path | Answer |
|---|---|
| `POST /api/share` | 201 `{"id","expires_at"}` for a valid envelope, 1–10 views and 300–604800 seconds |
| `GET /api/share/<id>` | 200 with the views left, expiry and passphrase check; spends no view |
| `POST /api/share/<id>/open` | 200 with the envelope; spends one view and deletes the row at zero |
| `DELETE /api/share/<id>` | 204 with `authorization: Bearer <revoke token>` |

Unknown, spent, expired and revoked shares and a wrong revoke token all answer the same 404, and only that 404 carries `x-keypaste-share: gone`, the one answer on which keypaste forgets a share and its revoke token. A foreign `Origin` answers 403, a malformed request 400, an oversized one 413 and a rate-limited one 429.

Sharing is on: `SHARE_ENABLED` is `"1"` in `wrangler.jsonc`'s `vars`, which every deploy sets (D-0364). Any other value makes the API routes answer 503 and the viewer 404, and so does a missing `SHARE_DB` binding. Switching sharing off never makes a client forget a live share, so it can still be revoked once sharing is back on.

The share database was provisioned on 2026-09-25: the `share` table and its index from `schema.sql`, the `keypaste_share` role with only the grants listed there, and the uncached `keypaste-share` Hyperdrive config bound as `SHARE_DB`. Before the deploy, a link made by `keypaste share` through a local `wrangler dev` on that database opened once in headless Edge and was refused on its second open.

A cron trigger every 30 minutes deletes expired and spent rows.

For local development without a database, put `SHARE_ENABLED=1` and `SHARE_DEV_MEMORY=1` in `site/.dev.vars` (ignored by version control) and point the CLI at the server with `KEYPASTE_SHARE_URL=http://127.0.0.1:8787`:

```sh
CLOUDFLARE_HYPERDRIVE_LOCAL_CONNECTION_STRING_HYPERDRIVE="postgresql://unused:unused@127.0.0.1:1/unused" \
  npx wrangler dev --port 8787 --ip 127.0.0.1 --local-upstream 127.0.0.1:8787
```

The signup binding needs some connection string to start, and the share routes never use it. `--local-upstream` keeps the request URL on `127.0.0.1`; without it Wrangler rewrites it to the routed `keypaste.com`, and the memory store, which is refused for any host other than `localhost` and `127.0.0.1`, answers 503. With a local Postgres, set `CLOUDFLARE_HYPERDRIVE_LOCAL_CONNECTION_STRING_SHARE_DB` once the binding exists.

`npm test` runs the share API and cryptography tests on Node 20 or later; `test/share-vector.json` holds vectors the .NET core sealed, which the viewer's code must open.

## Unimplemented features

The handler stores signup requests without sending confirmation mail; existing rows are unconfirmed. Double opt-in and list verification are optional work in [BACKLOG](../docs/BACKLOG.md), not prerequisites for the local desktop release. No mail may be sent to the list before the promised consent flow exists and each recipient has confirmed.

The signup handler has no rate limiter. Verify an applicable Cloudflare rule before relying on one; it has honeypot, body, content-type and origin checks. The share routes use the `SHARE_CREATE_LIMIT` and `SHARE_OPEN_LIMIT` rate-limiting bindings. Turnstile and managed challenges are absent because they introduce scripts. The site has no automated database-verification job.
