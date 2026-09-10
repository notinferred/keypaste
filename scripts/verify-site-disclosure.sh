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

  [ "$missing" -eq 0 ] || die \
    "$url is serving a page that does not disclose $missing of the defects $version is known to carry - deploy site/public/index.html with 'npx wrangler deploy'"

  echo "ok: $url discloses every defect recorded against the advertised $version"
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

  local origin
  origin="$("$real_jq" -r '[.published[] | select(.advertised == true)] | last | .origin' "$DEFINITION" | tr -d "$CR")"

  build_body() { # $1 = how many phrases to drop from the end; $2 = include the origin
    local drop="$1" with_origin="$2" out="$work/body.html"
    : > "$out"
    [ "$with_origin" = "yes" ] && echo "<a href=\"$origin\">download</a>" >> "$out"
    "$real_jq" -r '[.published[] | select(.advertised == true)] | last | (.known_defects // []) | .[].phrase' \
      "$DEFINITION" | tr -d "$CR" | { [ "$drop" -gt 0 ] && head -n "-$drop" || cat; } \
      | sed 's|^|<li>|; s|$|</li>|' >> "$out"
    echo "$out"
  }

  expect() { # $1 name, $2 expected exit (0 ok / 1 refusal), $3 body, $4 want-in-output
    local name="$1" want_exit="$2" body="$3" want="$4" out rc=0
    cases=$((cases + 1))
    out="$(KEYPASTE_SITE_BODY="$body" PATH="$work/fakebin:$PATH" bash "$SELF" "https://fixture.invalid/" 2>&1)" || rc=$?
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

  # The regression itself. Every phrase is on the page; only the reader can fail this.
  body="$(build_body 0 yes)"
  expect "crlf-phrases-still-match" 0 "$body" "discloses every defect"

  # And the check still refuses for the right reason when a phrase really is absent, so the fixture
  # above cannot be satisfied by a reader that matches nothing at all.
  body="$(build_body 1 yes)"
  expect "crlf-and-one-phrase-missing" 1 "$body" "does not disclose 1 of the defects"

  # The positive control, under the same CRLF reader: the origin is read through jq too.
  body="$(build_body 0 no)"
  expect "crlf-and-not-the-install-page" 1 "$body" "nothing was checked"

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
