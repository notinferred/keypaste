#!/usr/bin/env bash
#
# verify-site-endpoint.sh
#
# The half of site/README.md's pre-deploy checklist (H-0011) that writes nothing, run against the
# live site after site.yml deploys it.
#
# EVERY request here is one the Worker refuses or redirects BEFORE it reaches the database. That is
# the whole admission rule for this file, and it is checked against src/worker.js rather than
# assumed: `subscribe()` opens no connection until after the origin check, the content-type check,
# the size checks and the honeypot have all had their say, and `normalize()` returns null for a bad
# address before `postgres(...)` is called at all. A check that submits a real address belongs in
# the by-hand list in site/README.md and must never be added here - this script runs on every
# deploy, and a gate that quietly accumulates rows in a production table is worse than no gate.
#
# What it deliberately does NOT cover, and what stays by hand:
#   - a valid address is stored          writes a row, and the role cannot SELECT it back
#   - the same address twice adds one    same
#   - the database role's grants         needs an admin credential this job must never hold
#
# Usage:  scripts/verify-site-endpoint.sh [base-url]      (default: https://keypaste.com)
set -euo pipefail

readonly BASE="${1:-https://keypaste.com}"
readonly SUBSCRIBE="$BASE/subscribe"

failures=0

die()  { echo "::error::$*" >&2; exit 1; }
fail() { echo "::error::  $*" >&2; failures=$((failures + 1)); }
pass() { echo "  ok   $*"; }

# Status and body in one request. The body goes to a file rather than into the status output,
# because an error page is HTML with newlines in it and a `%{http_code}` suffix on that is a parsing
# problem nobody needs.
body="$(mktemp)"
trap 'rm -f "$body"' EXIT

request() {
  local method="$1" url="$2"; shift 2
  curl -sS -o "$body" -w '%{http_code}' -X "$method" --max-time 30 "$@" "$url" 2>/dev/null || echo 000
}

# Expects a status, and the beginning of what the page should say. The reason text is asserted and
# not just the code: every refusal below answers 400, so a script that checked only the number would
# pass with the Worker refusing everything for one reason.
expect() {
  local what="$1" want_code="$2" want_text="$3" got="$4"

  if [ "$got" != "$want_code" ]; then
    fail "$what: answered $got, expected $want_code"
    return
  fi
  if [ -n "$want_text" ] && ! grep -qF -- "$want_text" "$body"; then
    fail "$what: answered $got but does not say \"$want_text\""
    return
  fi
  pass "$what"
}

echo "asking $BASE what it refuses"

# Positive control, first. If the site is serving something that is not keypaste.com - a parked
# page, an error shell, somebody else's origin - then every refusal below would "pass" by failing to
# match, and the run would go green having tested nothing.
code="$(request GET "$BASE/")"
[ "$code" = "200" ] || die "$BASE/ answered $code; nothing was checked"
grep -qF -- "dl.keypaste.com" "$body" \
  || die "$BASE/ does not name dl.keypaste.com, so it is not the install page; nothing was checked"

# The promise the footer makes: no JavaScript on the page, so it still works with JavaScript off.
# Asserted on the served bytes rather than on the file in the repository, because that promise is
# about what a visitor's browser receives - a zone feature that injected a script would break it
# without any commit doing so.
if grep -qiE '<script|javascript:' "$body"; then
  fail "the served page carries a script, which contradicts what its footer promises"
else
  pass "no script on the served page"
fi

# A GET of the endpoint is a navigation, not a submission, and must not be treated as one.
expect "GET /subscribe redirects home" 303 "" "$(request GET "$SUBSCRIBE")"

# Static assets win for everything on disk; the Worker answers for /subscribe alone.
expect "an unknown path is 404" 404 "Not found" "$(request GET "$BASE/no-such-page-$RANDOM")"

expect "the thanks page is served" 200 "" "$(request GET "$BASE/thanks/")"

# --- Everything below refuses before the database is opened. ---

expect "a submission that is not a form is refused" 400 "That submission was not a form." \
  "$(request POST "$SUBSCRIBE" -H 'content-type: text/plain' --data-raw 'email=nobody@example.com')"

expect "a submission from another origin is refused" 400 "That submission did not come from keypaste.com." \
  "$(request POST "$SUBSCRIBE" -H 'origin: https://not-keypaste.example' \
       -H 'content-type: application/x-www-form-urlencoded' --data-raw 'email=nobody@example.com')"

expect "an address that is not one is refused" 400 "That does not look like an email address." \
  "$(request POST "$SUBSCRIBE" -H 'content-type: application/x-www-form-urlencoded' \
       --data-raw 'email=not-an-address')"

expect "an oversized submission is refused" 400 "That submission was too large." \
  "$(request POST "$SUBSCRIBE" -H 'content-type: application/x-www-form-urlencoded' \
       --data-raw "email=$(printf 'x%.0s' $(seq 1 1100))@example.com")"

# The honeypot, and the one request here that carries a plausible address. It is safe for exactly
# one reason: `website` being non-empty returns 303 at src/worker.js before any connection is
# opened, so nothing is stored. Leave `website` set. Emptying it turns this line into a write
# against the production table.
expect "a filled honeypot is thanked and not stored" 303 "" \
  "$(request POST "$SUBSCRIBE" -H 'content-type: application/x-www-form-urlencoded' \
       --data-raw 'website=a-bot-filled-this&email=honeypot@example.com')"

if [ "$failures" -ne 0 ]; then
  die "$BASE answered $failures check(s) wrongly"
fi

echo "ok: $BASE refuses every submission it should, and stores nothing on the way"
