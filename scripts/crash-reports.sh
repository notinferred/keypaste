#!/usr/bin/env bash
#
# crash-reports.sh
#
# Reads keypaste.com's crash reports from D1, by hand on the founder's machine (D-0587). Every query is a fixed select,
# run through the site's pinned Wrangler with `d1 execute --remote` under the account `npx wrangler login` signed in;
# the repository holds no credential. No workflow runs it: CI never asks the live origin or its database.
#
# Usage:
#   scripts/crash-reports.sh [--days N] [--version V]   failures grouped by fingerprint, the most frequent first,
#                                                       last seen within N days (default 90) and on version V if given
#   scripts/crash-reports.sh --show FINGERPRINT         that failure's rows, each with its first report
#
# Needs: Node 22, jq, `npm ci` run in site/, and H-0028's CRASH_DB binding in site/wrangler.jsonc.
set -euo pipefail

readonly DATABASE=keypaste-crash
readonly NAME_RE='^[A-Za-z0-9.-]{1,64}$'
readonly FINGERPRINT_RE='^[0-9a-f]{64}$'

die() { echo "crash-reports: $*" >&2; exit 1; }
usage() {
  cat >&2 <<'USAGE'
Usage: scripts/crash-reports.sh [--days N] [--version V]
       scripts/crash-reports.sh --show FINGERPRINT
USAGE
  exit 2
}

days=90 version='' show=''
while [ "$#" -gt 0 ]; do
  case "$1" in
    --days) [ "$#" -ge 2 ] || usage; days="$2"; shift ;;
    --version) [ "$#" -ge 2 ] || usage; version="$2"; shift ;;
    --show) [ "$#" -ge 2 ] || usage; show="$2"; shift ;;
    -h|--help) usage ;;
    *) usage ;;
  esac
  shift
done

# Each value reaches the SQL text, so each must fit a grammar that holds no quote.
[[ "$days" =~ ^[0-9]{1,4}$ ]] || die "--days takes a number of days"
[ -z "$version" ] || [[ "$version" =~ $NAME_RE ]] || die "--version takes a version as a report holds it, such as 1.0.0"
[ -z "$show" ] || [[ "$show" =~ $FINGERPRINT_RE ]] || die "--show takes a fingerprint: 64 lowercase hex characters"

site="$(cd "$(dirname "${BASH_SOURCE[0]}")/../site" && pwd)"
grep -q '"binding": *"CRASH_DB"' "$site/wrangler.jsonc" \
  || die "site/wrangler.jsonc binds no CRASH_DB yet; H-0028 in site/README.md makes the database"
[ -x "$site/node_modules/.bin/wrangler" ] || die "no Wrangler in site/node_modules; run npm ci in site/ first"
command -v jq >/dev/null 2>&1 || die "no jq on PATH"

query() {
  (cd "$site" && WRANGLER_SEND_METRICS=false node_modules/.bin/wrangler d1 execute "$DATABASE" --remote --json --command "$1") \
    | jq '.[0].results'
}

if [ -n "$show" ]; then
  query "select version, os, arch, failures, first_seen_on, last_seen_on, report from crash
         where fingerprint = '$show' order by last_seen_on desc" \
    | jq '[.[] | .report |= fromjson]'
  exit 0
fi

filter="last_seen_on >= date('now', '-$days days')"
[ -z "$version" ] || filter="$filter and version = '$version'"

# Every row of one fingerprint holds the same kind, type and first frames, so any of its reports names them.
query "select fingerprint, sum(failures) as failures, min(first_seen_on) as first_seen_on, max(last_seen_on) as last_seen_on,
              group_concat(distinct version) as versions, group_concat(distinct os || '/' || arch) as platforms,
              json_extract(min(report), '\$.kind') as kind, json_extract(min(report), '\$.site') as site,
              json_extract(min(report), '\$.exception.type') as type,
              json_extract(min(report), '\$.exception.frames[0].type') || '.' ||
                json_extract(min(report), '\$.exception.frames[0].method') as top_frame
       from crash where $filter group by fingerprint order by failures desc limit 100"
