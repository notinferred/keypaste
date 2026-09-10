#!/usr/bin/env bash
# Asks keypaste.com whether the disclosure is actually on it.
#
# site/public/index.html is deployed by hand with `npx wrangler deploy` and no workflow publishes it,
# so verify-release-matrix.sh - which reads the file at the checked-out ref, and says so - can be
# green while the live page is months older. Every other gate in this repository is blind to that
# gap, and what sits on the far side of it is the list of ways the advertised download can destroy
# somebody's vault. So this one asks the origin instead of the working tree.
#
# Run it after `npx wrangler deploy`. The live-fetch mode is deliberately NOT wired into ci.yml or
# release.yml: nothing in CI deploys the site, so a check that stays red until a person runs wrangler
# would be red for a reason CI cannot fix. `--selftest` is the half that does run in ci.yml - it
# needs no network and holds the reader below to what it has to do.
#
# Usage:
#   scripts/verify-site-disclosure.sh [url]     (default: https://keypaste.com/)
#   scripts/verify-site-disclosure.sh --selftest
#
# Environment:
#   KEYPASTE_RELEASE_DEFINITION  the definition to read   (default: release-targets.json)
#   KEYPASTE_SITE_BODY           a local file to read instead of fetching (fixtures)
set -euo pipefail

readonly SELF="${BASH_SOURCE[0]}"
readonly DEFINITION="${KEYPASTE_RELEASE_DEFINITION:-release-targets.json}"
readonly CR=$'\r'

die() { echo "::error::$*" >&2; exit 1; }

# EVERY read of the definition goes through this, and the `tr` is the whole point of it.
#
# jq.exe on Windows terminates its lines with CRLF. Read through a heredoc, that puts a trailing \r
# on every phrase but the last - the last loses it to the trailing-newline stripping that command
# substitution does anyway - so `grep -F` looked for a carriage return the page does not contain and
# eight of nine defects read as missing from a page that carried all nine. The same trap is why
# verify-release-matrix.sh has had a `jqr` since it was written; this script was the one that used a
# bare `jq -r`, and `--selftest` below is what stops that being rediscovered a third time.
jqr() { command jq -r "$@" | tr -d "$CR"; }

# ---------------------------------------------------------------------------
# The check
# ---------------------------------------------------------------------------
run_check() {
  local url="$1" body version origin missing=0 phrase code

  command -v jq >/dev/null 2>&1 || die \
    "no jq on PATH - run this under Git Bash, not WSL. A Windows 'bash' on PATH is usually the WSL launcher, which starts a different machine with its own PATH and its own idea of what is installed"
  [ -f "$DEFINITION" ] || die "no release definition at $DEFINITION"

  version="$(jqr '[.published[] | select(.advertised == true)] | last | .version' "$DEFINITION")"
  origin="$(jqr '[.published[] | select(.advertised == true)] | last | .origin'  "$DEFINITION")"
  [ -n "$version" ] && [ "$version" != "null" ] || die "$DEFINITION advertises no version"

  body="$(mktemp)"
  trap 'rm -f "$body"' RETURN

  if [ -n "${KEYPASTE_SITE_BODY:-}" ]; then
    [ -f "$KEYPASTE_SITE_BODY" ] || die "KEYPASTE_SITE_BODY names no file: $KEYPASTE_SITE_BODY"
    cat "$KEYPASTE_SITE_BODY" > "$body"
  else
    command -v curl >/dev/null 2>&1 || die "no curl to ask $url with"
    code="$(curl -fsS -L --max-time 30 -o "$body" -w '%{http_code}' "$url" 2>/dev/null || echo 000)"
    [ "$code" = "200" ] || die "$url answered $code; nothing was checked"
  fi

  # Positive control. An error page, a placeholder or a cached shell would fail every phrase below
  # and read as "the disclosure is missing" when the truth is "that is not the install page". The
  # page has to prove it is the one that sends people to the advertised download before its silence
  # means anything - the same shape as publish-release.sh's probe prefix.
  grep -qF -- "$origin" "$body" \
    || die "$url does not name $origin, so it is not the page that advertises $version; nothing was checked"

  while IFS= read -r phrase; do
    [ -n "$phrase" ] || continue
    if grep -qF -- "$phrase" "$body"; then
      echo "  ok   $phrase"
    else
      echo "::error::  missing from $url: $phrase"
      missing=$((missing + 1))
    fi
  done <<EOF
$(jqr '[.published[] | select(.advertised == true)] | last | (.known_defects // []) | .[].phrase' "$DEFINITION")
EOF

  # The other half, and the half a flip silently drops. When the advertised version moves, the
  # defects of the release people ALREADY INSTALLED do not stop existing - and those people are the
  # only ones the disclosure was ever for. They have no self-update and no version check to tell
  # them, so the served page is the only thing that reaches them. A check that only asked about the
  # current download would go green the moment the warning they need disappeared.
  local superseded notice_missing=0 found phrase_count
  while IFS= read -r superseded; do
    [ -n "$superseded" ] || continue
    [ "$superseded" = "$version" ] && continue

    phrase_count="$(jqr --arg v "$superseded" '[.published[] | select(.version == $v)] | last | (.known_defects // []) | length' "$DEFINITION")"
    case "$phrase_count" in '' | *[!0-9]*) phrase_count=0 ;; esac
    [ "$phrase_count" -gt 0 ] || continue

    if ! grep -qF -- "$superseded" "$body"; then
      echo "::error::  $url does not name $superseded, which people are still running and which is still broken"
      notice_missing=$((notice_missing + 1))
      continue
    fi

    found=0
    while IFS= read -r phrase; do
      [ -n "$phrase" ] || continue
      if grep -qF -- "$phrase" "$body"; then found=1; break; fi
    done <<PHRASES
$(jqr --arg v "$superseded" '[.published[] | select(.version == $v)] | last | (.known_defects // []) | .[].phrase' "$DEFINITION")
PHRASES

    if [ "$found" -eq 1 ]; then
      echo "  ok   the upgrade notice for $superseded, and what it does"
    else
      echo "::error::  $url names $superseded and none of what it does, which is a version number and not a warning"
      notice_missing=$((notice_missing + 1))
    fi
  done <<EOF
$(jqr '[.published[] | select((.known_defects // []) | length > 0) | .version] | .[]' "$DEFINITION")
EOF

  [ "$missing" -eq 0 ] || die \
    "$url is serving a page that does not disclose $missing of the defects $version is known to carry - deploy site/public/index.html with 'npx wrangler deploy'"
  [ "$notice_missing" -eq 0 ] || die \
    "$url is serving a page that drops the upgrade notice for $notice_missing superseded release(s) - the people running them have no other way to find out"

  echo "ok: $url discloses every defect recorded against the advertised $version, and keeps the upgrade notice for every superseded release that carried any"
}

# ---------------------------------------------------------------------------
# The fixtures. No network, so ci.yml can run them.
#
# The fake jq forces CRLF on EVERY line whatever the platform, which is what makes these meaningful
# on the Linux runner: the bug they exist for cannot occur there, and a fixture that could only fail
# on the maintainer's machine is not a gate. The bodies are built with the real jq, explicitly
# stripped - if they were built through the reader under test, a broken reader would write \r into
# both sides and match itself.
# ---------------------------------------------------------------------------
selftest() {
  local work real_jq body cases=0 failures=0

  command -v jq >/dev/null 2>&1 || die "no jq on PATH - run this under Git Bash, not WSL"
  real_jq="$(command -v jq)"
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' RETURN

  mkdir -p "$work/fakebin"
  # Quoted heredoc, and the real jq arrives in the environment rather than being interpolated: a
  # printf format writing this had its own \r eaten by printf and produced a shim that changed
  # nothing, which is the failure this whole file is about. awk rather than `sed s/$/\r/`, because
  # only GNU sed reads \r in a replacement as a carriage return.
  cat > "$work/fakebin/jq" <<'SHIM'
#!/usr/bin/env bash
set -euo pipefail
"$KEYPASTE_REAL_JQ" "$@" | tr -d '\r' | awk '{ printf "%s\r\n", $0 }'
SHIM
  chmod +x "$work/fakebin/jq"
  export KEYPASTE_REAL_JQ="$real_jq"

  # Proof the fake is actually lying the way these fixtures need it to. Counted in bytes rather than
  # asked of grep: MSYS grep treats CRLF as a line ending and strips the \r before matching, so
  # `grep -q $'\r'` answers "no" on a file that plainly contains one. That is also the far half of
  # the bug this file exists for - the \r survives in the PATTERN, which comes from a variable, and
  # is gone from the LINE, so the two can never match.
  local with_cr without_cr
  PATH="$work/fakebin:$PATH" jq -r '.schema' "$DEFINITION" > "$work/probe"
  with_cr="$(wc -c < "$work/probe")"
  without_cr="$(tr -d "$CR" < "$work/probe" | wc -c)"
  [ "$with_cr" -gt "$without_cr" ] \
    || die "the CRLF fake produced no carriage return, so these fixtures prove nothing"

  # The fixtures bring their own definition. Driving them off the repository's means they only test
  # whatever it happens to advertise today: an advertised release with no known defects makes the
  # phrase cases vacuous, and a repository with nothing superseded makes the notice cases vacuous -
  # both passing by having nothing to check, exactly when the next release needs them working.
  local fixdef origin
  fixdef="$work/definition.json"
  origin="https://fixture.invalid/v2.0.0/"
  cat > "$fixdef" <<'FIXDEF'
{
  "schema": 1,
  "components": { "cli": { "origin": "https://fixture.invalid/v{version}/" } },
  "published": [
    {
      "version": "1.0.0", "component": "cli", "advertised": false,
      "origin": "https://fixture.invalid/v1.0.0/",
      "known_defects": [
        { "id": "X.1", "phrase": "the old one can delete your vault" },
        { "id": "X.2", "phrase": "the old one can pick the wrong entry" }
      ]
    },
    {
      "version": "2.0.0", "component": "cli", "advertised": true,
      "origin": "https://fixture.invalid/v2.0.0/",
      "known_defects": [
        { "id": "Y.1", "phrase": "the new one hums audibly" },
        { "id": "Y.2", "phrase": "the new one is warm to the touch" }
      ]
    }
  ]
}
FIXDEF

  # $1 advertised phrases to drop from the end; $2 include the origin; $3 notice: full|bare|none
  build_body() {
    local drop="$1" with_origin="$2" notice="$3" out="$work/body.html"
    : > "$out"
    [ "$with_origin" = "yes" ] && echo "<a href=\"$origin\">download</a>" >> "$out"
    "$real_jq" -r '[.published[] | select(.advertised == true)] | last | (.known_defects // []) | .[].phrase' \
      "$fixdef" | tr -d "$CR" | { [ "$drop" -gt 0 ] && head -n "-$drop" || cat; } \
      | sed 's|^|<li>|; s|$|</li>|' >> "$out"
    case "$notice" in
      full) echo "<p>If you installed 1.0.0, replace it: the old one can delete your vault</p>" >> "$out" ;;
      bare) echo "<p>1.0.0 is superseded.</p>" >> "$out" ;;
      none) ;;
    esac
    echo "$out"
  }

  expect() { # $1 name, $2 expected exit (0 ok / 1 refusal), $3 body, $4 want-in-output
    local name="$1" want_exit="$2" body="$3" want="$4" out rc=0
    cases=$((cases + 1))
    out="$(KEYPASTE_SITE_BODY="$body" KEYPASTE_RELEASE_DEFINITION="$fixdef" \
           PATH="$work/fakebin:$PATH" bash "$SELF" "https://fixture.invalid/" 2>&1)" || rc=$?
    if [ "$rc" -ne "$want_exit" ]; then
      echo "::error::fixture '$name' exited $rc, wanted $want_exit"
      echo "$out" | sed 's/^/::error::    /'
      failures=$((failures + 1))
      return
    fi
    if [ -n "$want" ] && ! printf '%s' "$out" | grep -qF -- "$want"; then
      echo "::error::fixture '$name' did not say '$want'"
      echo "$out" | sed 's/^/::error::    /'
      failures=$((failures + 1))
      return
    fi
    echo "  ok: $name"
  }

  echo "== fixtures: the definition is read through a jq that writes CRLF"

  # The regression this file exists for. Every phrase and the notice are on the page; only the
  # reader can fail this, and it fails it the moment the \r strip comes out of jqr().
  body="$(build_body 0 yes full)"
  expect "crlf-phrases-still-match" 0 "$body" "keeps the upgrade notice"

  # ... and the check still refuses for the right reason when a phrase really is absent, so the case
  # above cannot be satisfied by a reader that matches nothing at all.
  body="$(build_body 1 yes full)"
  expect "crlf-and-one-phrase-missing" 1 "$body" "does not disclose 1 of the defects"

  # The positive control, under the same CRLF reader: the origin is read through jq too.
  body="$(build_body 0 no full)"
  expect "crlf-and-not-the-install-page" 1 "$body" "nothing was checked"

  # The superseded release's warning, which is the half a flip drops. The people running 1.0.0 have
  # no self-update and no version check; this page is the only thing that reaches them.
  body="$(build_body 0 yes none)"
  expect "upgrade-notice-missing-entirely" 1 "$body" "still running and which is still broken"

  # A version number with nothing said about it is a changelog entry, not a warning.
  body="$(build_body 0 yes bare)"
  expect "upgrade-notice-without-the-defect" 1 "$body" "a version number and not a warning"

  echo
  if [ "$failures" -gt 0 ]; then
    die "$failures of $cases fixture cases failed"
  fi
  echo "ok: $cases fixture cases; the definition survives a jq that writes CRLF"
}

case "${1:-}" in
  --selftest) selftest ;;
  *)          run_check "${1:-https://keypaste.com/}" ;;
esac
