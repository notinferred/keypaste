-- keypaste.com's crash reports in Cloudflare D1 (D-0587), applied once by H-0028; site/README.md's Crash reports
-- section says what each column holds.
create table if not exists crash (
  fingerprint text not null,
  version text not null,
  os text not null,
  arch text not null,
  failures integer not null,
  first_seen_on text not null,
  last_seen_on text not null,
  report text not null,
  primary key (fingerprint, version, os, arch)
);

create index if not exists crash_first_seen_on on crash (first_seen_on);
create index if not exists crash_last_seen_on on crash (last_seen_on);
