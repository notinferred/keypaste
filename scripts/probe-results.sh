#!/usr/bin/env bash
set -euo pipefail

readonly SELF="${BASH_SOURCE[0]}"
readonly ESC=$'\033'
readonly CR=$'\r'

die() { echo "::error::$*" >&2; exit 1; }

summarize() {
  local work log bad=0
  work="$(mktemp -d)"
  trap 'rm -f "$work"/*; rmdir "$work"' RETURN
  : > "$work/names"

  for log in "$@"; do
    if [ ! -f "$log" ]; then
      echo "::error::missing failed log: $log" >&2
      bad=1
      continue
    fi

    if [ ! -s "$log" ]; then
      echo "::error::empty failed log: $log" >&2
      bad=1
      continue
    fi

    # Microsoft.Testing.Platform can emit ANSI colour even when output is redirected.
    if ! sed -E "s/${ESC}[[][0-9;]*[A-Za-z]//g; s/${CR}$//" "$log" \
      | awk '
          /^failed +/ {
            name = $2
            sub(/\(.*/, "", name)
            if (name ~ /^Keypaste\.[A-Za-z0-9_]+(\.[A-Za-z0-9_]+)+$/) print name
            else bad = 1
          }
          END { if (bad) exit 1 }
        ' > "$work/one"; then
      echo "::error::unrecognized failure result in failed log: $log" >&2
      bad=1
      continue
    fi

    if [ ! -s "$work/one" ]; then
      echo "::error::no failing test name parsed from failed log: $log" >&2
      bad=1
      continue
    fi

    cat "$work/one" >> "$work/names"
  done

  [ "$bad" -eq 0 ] || return 1
  LC_ALL=C sort "$work/names" | uniq -c | LC_ALL=C sort -k1,1nr -k2,2 \
    | awk '{ print $1 " " $2 }'
}

selftest() {
  local work cases=0 failures=0 status
  work="$(mktemp -d)"
  trap 'rm -f "$work"/*; rmdir "$work"' RETURN

  printf '%s\n' \
    'failed Keypaste.Mcp.Tests.ListingTests.ListsNames [12 ms]' \
    'failed Keypaste.Core.Tests.SaveTests.RefusesMissingDirectory [1 s]' \
    '  at Keypaste.Core.Tests.SaveTests.RefusesMissingDirectory()' \
    'Test run summary: Failed! - 2 failed' > "$work/plain.log"
  printf '\033[31mfailed \033[m Keypaste.Mcp.Tests.ListingTests.ListsNames [3 s]\r\n' > "$work/ansi.log"
  printf 'failed Keypaste.Mcp.Tests.ListingTests.ListsNames(value: 3) [1 ms]\r\n' > "$work/crlf.log"
  printf '%s\n' 'test host crashed before reporting a test result' > "$work/unparsed.log"
  printf '%s\n' \
    'failed Keypaste.Mcp.Tests.ListingTests.ListsNames [1 ms]' \
    'failed to start another test host' > "$work/partial.log"
  : > "$work/empty.log"

  check() {
    local name="$1" expected_status="$2" expected_out="$3" expected_error="$4" error_matches=false
    shift 4
    cases=$((cases + 1))
    status=0
    bash "$SELF" "$@" > "$work/actual" 2> "$work/error" || status=$?

    if [ -z "$expected_error" ]; then
      if [ ! -s "$work/error" ]; then error_matches=true; fi
    elif grep -Fq -- "$expected_error" "$work/error"; then
      error_matches=true
    fi

    if [ "$status" -eq "$expected_status" ] \
      && cmp -s "$expected_out" "$work/actual" \
      && [ "$error_matches" = true ]; then
      echo "ok   $name"
    else
      echo "FAIL $name (exit $status, expected $expected_status)"
      cat "$work/actual" "$work/error"
      failures=$((failures + 1))
    fi
  }

  printf '%s\n' \
    '1 Keypaste.Core.Tests.SaveTests.RefusesMissingDirectory' \
    '1 Keypaste.Mcp.Tests.ListingTests.ListsNames' > "$work/expected-plain"
  printf '%s\n' \
    '3 Keypaste.Mcp.Tests.ListingTests.ListsNames' \
    '1 Keypaste.Core.Tests.SaveTests.RefusesMissingDirectory' > "$work/expected-all"
  printf '%s\n' '1 Keypaste.Mcp.Tests.ListingTests.ListsNames' > "$work/expected-one"
  : > "$work/expected-empty"

  check "plain results exclude stack frames and summaries" 0 "$work/expected-plain" '' "$work/plain.log"
  check "ANSI colour and CRLF are readable" 0 "$work/expected-one" '' "$work/ansi.log"
  check "parameterized CRLF results retain the test method" 0 "$work/expected-one" '' "$work/crlf.log"
  check "multiple logs aggregate and sort by count then name" 0 "$work/expected-all" '' "$work/plain.log" "$work/ansi.log" "$work/crlf.log"
  check "missing input is an error" 1 "$work/expected-empty" 'missing failed log:' "$work/missing.log"
  check "empty failed input is an error" 1 "$work/expected-empty" 'empty failed log:' "$work/empty.log"
  check "a host crash is not zero failures" 1 "$work/expected-empty" 'no failing test name parsed' "$work/unparsed.log"
  check "one readable log cannot hide an unreadable failed run" 1 "$work/expected-empty" 'no failing test name parsed' "$work/plain.log" "$work/unparsed.log"
  check "a partial result inside one log is refused" 1 "$work/expected-empty" 'unrecognized failure result' "$work/partial.log"
  check "no inputs is a usage error" 2 "$work/expected-empty" 'usage:'

  echo "$((cases - failures)) of $cases cases passed"
  [ "$failures" -eq 0 ] || die "the probe result reader does not read what it claims"
}

case "${1:-}" in
  --selftest) selftest ;;
  -h | --help) echo "usage: bash scripts/probe-results.sh <failed-log>... | --selftest" ;;
  '') echo "usage: bash scripts/probe-results.sh <failed-log>... | --selftest" >&2; exit 2 ;;
  *) summarize "$@" ;;
esac
