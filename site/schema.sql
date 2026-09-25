-- The keypaste.com launch-notification list.
--
-- Applied once, by hand, with psql and an admin credential that is not stored anywhere in this
-- repository. No code in this repository executes this file, and there is no migration framework:
-- there is one table, and a second one would be a decision rather than a migration.

CREATE TABLE IF NOT EXISTS public.signup (
  email      text        PRIMARY KEY,   -- trimmed and lowercased before it gets here
  created_at timestamptz NOT NULL DEFAULT now(),
  source     text        NOT NULL DEFAULT 'site'
);

-- Deliberately not stored: IP address, user agent, country. request.cf.country is one property
-- access away in the Worker and the page promises privacy, so the column does not exist to tempt
-- anyone. A row is an address and the moment it arrived.


-- ---------------------------------------------------------------- the role the Worker connects as
--
-- It must be able to add an address and not to read one back, so that a fully compromised Worker -
-- or a compromised dependency inside it - cannot dump the subscriber list. Adding `RETURNING`, or
-- switching to `DO UPDATE`, would require SELECT and quietly undo this.
--
-- **And so does naming the conflict target.** An earlier version of this comment claimed that
-- `INSERT ... ON CONFLICT DO NOTHING` without `RETURNING` needs only the insert privilege. That is
-- true only of the bare form. Measured on PostgreSQL 18.4 against this database:
--
--     insert ... values (...)                          -> ok with INSERT alone
--     insert ... on conflict do nothing                -> ok with INSERT alone
--     insert ... on conflict (email) do nothing        -> 42501 permission denied for table signup
--
-- Resolving an inference specification reads the table's indexes and therefore wants SELECT. The
-- Worker used the third form, which is why a correctly configured write-only role still produced a
-- 503 on every signup. It now uses the second. If a future change reintroduces a conflict target,
-- the symptom is that every submission fails and nothing in the privilege grants looks wrong.
--
-- Do NOT create this with a raw `CREATE ROLE`. On PlanetScale the documented path is a *managed*
-- role, created in the dashboard or with `pscale role create` or the Roles API. A managed role
-- shows up in the dashboard, rotates with `pscale role reset`, and can carry a TTL; a role created
-- with raw SQL is invisible to all of that and its lifecycle becomes yours to remember.
--
-- The catch is that the managed role builder only offers cluster-wide predefined roles
-- (`pg_read_all_data` and friends) and cannot express "INSERT on one table". So the path is both:
-- create the managed role with NO inherited roles, then grant it what it needs here.
--
--     pscale role create <database> <branch> keypaste-signup --inherited-roles ''
--
-- That prints a generated username - PlanetScale ignores the name you typed - of the form
-- `pscale_<id>`. Substitute it for <ROLE> below.
--
-- **When connecting, append the branch id**: `pscale_<id>.<branch-id>`. PlanetScale's proxy routes
-- on the username. Inside the database `current_user` is the bare `pscale_<id>`, which is the form
-- these grants use. Getting this backwards is the most likely reason a correct-looking
-- `wrangler hyperdrive update` fails to authenticate.

-- The database provisioned on 2026-09-25 (D-0364) has no pscale CLI behind it, so its role was
-- created with SQL instead, NOINHERIT and with no memberships; <ROLE> is keypaste_signup_writer:
--
--     CREATE ROLE keypaste_signup_writer LOGIN NOINHERIT NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD '…';

GRANT CONNECT ON DATABASE postgres   TO "<ROLE>";
GRANT USAGE   ON SCHEMA  public      TO "<ROLE>";
GRANT INSERT  ON TABLE  public.signup TO "<ROLE>";
-- No SELECT, no UPDATE, no DELETE. This is the point of the role.
--
-- And specifically NOT `pg_write_all_data`, which Cloudflare's generic Hyperdrive guide suggests:
-- it is cluster-wide rather than per-table, and in practice wants `pg_read_all_data` beside it to
-- be useful, which is the exact privilege being withheld here.

-- PlanetScale grants CONNECT on a new database to PUBLIC, meaning every current and future role.
-- Close that, now that the one role that needs it has been granted it explicitly. Not applied to the
-- 2026-09-25 database: PlanetScale's own roles (pscale_exporter, pscale_pgbouncer, ...) may connect
-- through that grant, and the table grants already confine the Worker's roles.
REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;


-- ---------------------------------------------------------------- share links (D-0355)
--
-- One row per share link: an envelope the server cannot open, the views left, the expiry and the
-- SHA-256 of the revoke token. The link's key never reaches the server. A row is deleted when its
-- last view is spent, when it is revoked, and by the Worker's cron sweep once it expires.

CREATE TABLE IF NOT EXISTS public.share (
  id            text        PRIMARY KEY,                          -- 22-char base64url, server-generated
  envelope      text        NOT NULL CHECK (length(envelope) <= 16384),
  views_left    integer     NOT NULL CHECK (views_left BETWEEN 0 AND 10),
  expires_at    timestamptz NOT NULL,
  revoke_sha256 text        NOT NULL CHECK (revoke_sha256 ~ '^[0-9a-f]{64}$'),
  passphrase    boolean     NOT NULL,
  created_at    timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS share_expires_at ON public.share (expires_at);

-- A separate role and a separate Hyperdrive config created with --caching-disabled, bound as
-- SHARE_DB. The signup role gains nothing. On the 2026-09-25 database <SHARE_ROLE> is keypaste_share,
-- created like keypaste_signup_writer above; it is granted CONNECT explicitly so it keeps working
-- wherever the statement above has revoked it from PUBLIC.
GRANT CONNECT ON DATABASE postgres TO "<SHARE_ROLE>";
GRANT USAGE ON SCHEMA public TO "<SHARE_ROLE>";
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.share TO "<SHARE_ROLE>";
