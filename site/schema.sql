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

-- The role the Worker actually connects as. It can add an address and cannot read one back, so a
-- fully compromised Worker - or a compromised dependency inside it - cannot dump this list.
-- INSERT ... ON CONFLICT DO NOTHING without RETURNING needs only INSERT; adding RETURNING, or
-- switching to DO UPDATE, would require SELECT and quietly undo this.
CREATE ROLE signup_writer LOGIN PASSWORD 'set-this-out-of-band-and-never-commit-it';
GRANT CONNECT ON DATABASE postgres TO signup_writer;
GRANT USAGE  ON SCHEMA public       TO signup_writer;
GRANT INSERT ON TABLE public.signup TO signup_writer;
-- No SELECT, no UPDATE, no DELETE. This is the point of the role.
