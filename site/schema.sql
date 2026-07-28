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

GRANT CONNECT ON DATABASE postgres   TO "<ROLE>";
GRANT USAGE   ON SCHEMA  public      TO "<ROLE>";
GRANT INSERT  ON TABLE  public.signup TO "<ROLE>";
-- No SELECT, no UPDATE, no DELETE. This is the point of the role.
--
-- And specifically NOT `pg_write_all_data`, which Cloudflare's generic Hyperdrive guide suggests:
-- it is cluster-wide rather than per-table, and in practice wants `pg_read_all_data` beside it to
-- be useful, which is the exact privilege being withheld here.

-- PlanetScale grants CONNECT on a new database to PUBLIC, meaning every current and future role.
-- Close that, now that the one role that needs it has been granted it explicitly.
REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;
