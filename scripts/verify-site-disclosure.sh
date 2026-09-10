#!/usr/bin/env bash
# Asks keypaste.com whether the disclosure is actually on it.
#
# site/public/index.html is deployed by hand with `npx wrangler deploy` and no workflow publishes it,
# so verify-release-matrix.sh - which reads the file at the checked-out ref, and says so - can be
# green while the live page is months older. Every other gate in this repository is blind to that
# gap, and what sits on the far side of it is the list of ways the advertised download can destroy
# somebody's vault. So this one asks the origin instead of the working tree.
#
# Run it after `npx wrangler deploy`. It is deliberately NOT wired into ci.yml or release.yml:
# nothing in CI deploys the site, so a check that stays red until a person runs wrangler would be
# red for a reason CI cannot fix. install.yml is where it belongs if it is ever put on a schedule.
#
# Usage:
#   scripts/verify-site-disclosure.sh [url]     (default: https://keypaste.com/)
#
# Environment:
#   KEYPASTE_RELEASE_DEFINITION  the definition to read (default: release-targets.json)
set -euo pipefail

readonly DEFINITION="${KEYPASTE_RELEASE_DEFINITION:-release-targets.json}"
readonly URL="${1:-https://keypaste.com/}"

die() { echo "::error::$*" >&2; exit 1; }

command -v curl >/dev/null 2>&1 || die "no curl to ask $URL with"
command -v jq   >/dev/null 2>&1 || die "no jq to read $DEFINITION with"
[ -f "$DEFINITION" ] || die "no release definition at $DEFINITION"

version="$(jq -r '[.published[] | select(.advertised == true)] | last | .version' "$DEFINITION")"
origin="$(jq -r '[.published[] | select(.advertised == true)] | last | .origin'  "$DEFINITION")"
[ -n "$version" ] && [ "$version" != "null" ] || die "$DEFINITION advertises no version"

body="$(mktemp)"; trap 'rm -f "$body"' EXIT
code="$(curl -fsS -L --max-time 30 -o "$body" -w '%{http_code}' "$URL" 2>/dev/null || echo 000)"
[ "$code" = "200" ] || die "$URL answered $code; nothing was checked"

# Positive control. An error page, a placeholder or a cached shell would fail every phrase below and
# read as "the disclosure is missing" when the truth is "that is not the install page". The page has
# to prove it is the one that sends people to the advertised download before its silence means
# anything - the same shape as publish-release.sh's probe prefix.
grep -qF -- "$origin" "$body" \
  || die "$URL does not name $origin, so it is not the page that advertises $version; nothing was checked"

missing=0
while IFS= read -r phrase; do
  [ -n "$phrase" ] || continue
  if grep -qF -- "$phrase" "$body"; then
    echo "  ok   $phrase"
  else
    echo "::error::  missing from $URL: $phrase"
    missing=$((missing + 1))
  fi
done <<EOF
$(jq -r '[.published[] | select(.advertised == true)] | last | (.known_defects // []) | .[].phrase' "$DEFINITION")
EOF

if [ "$missing" -gt 0 ]; then
  die "$URL is serving a page that does not disclose $missing of the defects $version is known to carry - deploy site/public/index.html with 'npx wrangler deploy'"
fi

echo "ok: $URL discloses every defect recorded against the advertised $version"
