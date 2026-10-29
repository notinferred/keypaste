#!/usr/bin/env bash
# Refuses a release whose vendored Public Suffix List is more than 60 days old (D-0616).
#
# The age is the list's own VERSION line, the time publicsuffix.org built it, to today's date in UTC. A list too old
# counts a hosting platform added since as one site with its tenants, so browser fill would match across them;
# scripts/update-public-suffix.sh refreshes it. A VERSION that is missing, malformed or after today refuses too.
#
# Usage:
#   require-public-suffix-fresh.sh [list]
#   require-public-suffix-fresh.sh --selftest
#
# Environment:
#   KEYPASTE_TODAY  the date to measure to, YYYY-MM-DD (fixtures; default: today in UTC)
set -euo pipefail

readonly MAX_DAYS=60
readonly DEFAULT_LIST=third_party/publicsuffix/public_suffix_list.dat

die() { echo "::error::$*" >&2; exit 1; }

# Days since 1970-01-01 of a proleptic Gregorian date (Howard Hinnant's days_from_civil), so no date(1) dialect is needed.
days_from_civil() {
  local y=$((10#$1)) m=$((10#$2)) d=$((10#$3)) era yoe doy doe
  if [ "$m" -le 2 ]; then y=$((y - 1)); fi
  era=$((y / 400))
  yoe=$((y - era * 400))
  doy=$(((153 * ((m + 9) % 12) + 2) / 5 + d - 1))
  doe=$((yoe * 365 + yoe / 4 - yoe / 100 + doy))
  echo $((era * 146097 + doe - 719468))
}

day_number() {
  local text="$1" y m d
  [[ "$text" =~ ^([0-9]{4})-([0-9]{2})-([0-9]{2})$ ]] || return 1
  y="${BASH_REMATCH[1]}" m="${BASH_REMATCH[2]}" d="${BASH_REMATCH[3]}"
  [ $((10#$m)) -ge 1 ] && [ $((10#$m)) -le 12 ] && [ $((10#$d)) -ge 1 ] && [ $((10#$d)) -le 31 ] || return 1
  days_from_civil "$y" "$m" "$d"
}

check() {
  local list="$1" versions version today listed now age
  [ -f "$list" ] || die "no Public Suffix List at $list"

  versions="$(sed -n 's|^// VERSION: \([0-9-]*\)_[0-9]\{2\}-[0-9]\{2\}-[0-9]\{2\}_UTC$|\1|p' "$list")"
  [ "$(printf '%s\n' "$versions" | grep -c .)" = 1 ] || die "$list names no single VERSION, so its age is unknown"
  version="$versions"

  today="${KEYPASTE_TODAY:-$(date -u +%Y-%m-%d)}"
  listed="$(day_number "$version")" || die "$list's VERSION $version is not a date"
  now="$(day_number "$today")" || die "today, $today, is not a date"

  age=$((now - listed))
  [ "$age" -ge 0 ] || die "$list's VERSION $version is after today, $today"
  [ "$age" -le "$MAX_DAYS" ] \
    || die "$list is $age days old, from $version; run scripts/update-public-suffix.sh and commit it before releasing"

  echo "  ok  $list is $age days old, from $version"
}

selftest() {
  local self cases=0
  self="${BASH_SOURCE[0]}"
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' EXIT

  listed_on() { printf '// header\n\n// VERSION: %s_08-36-41_UTC\n// COMMIT: %040d\n\ncom\n' "$1" 0 > "$work/$2"; }

  expect() {
    local want="$1" text="$2" today="$3" list="$4" rc=0
    cases=$((cases + 1))
    KEYPASTE_TODAY="$today" bash "$self" "$list" > "$work/out" 2>&1 || rc=$?
    if [ "$want" = accept ]; then [ "$rc" -eq 0 ] || { cat "$work/out" >&2; die "selftest: $list on $today was refused"; }
    else [ "$rc" -ne 0 ] || { cat "$work/out" >&2; die "selftest: $list on $today was accepted"; }
    fi
    grep -qF -- "$text" "$work/out" || { cat "$work/out" >&2; die "selftest: $list on $today never said '$text'"; }
  }

  listed_on 2026-10-22 list
  expect accept "is 0 days old" 2026-10-22 "$work/list"
  expect accept "is 60 days old" 2026-12-21 "$work/list"
  expect refuse "is 61 days old" 2026-12-22 "$work/list"
  expect refuse "is after today" 2026-10-21 "$work/list"

  # A leap year's 29 February is one of the 60 days.
  listed_on 2028-01-01 leap
  expect accept "is 60 days old" 2028-03-01 "$work/leap"
  expect refuse "is 61 days old" 2028-03-02 "$work/leap"
  listed_on 2026-12-01 year
  expect accept "is 31 days old" 2027-01-01 "$work/year"

  printf '// header\n\ncom\n' > "$work/unversioned"
  expect refuse "names no single VERSION" 2026-10-22 "$work/unversioned"
  { cat "$work/list"; printf '// VERSION: 2026-10-23_08-36-41_UTC\n'; } > "$work/twice"
  expect refuse "names no single VERSION" 2026-10-22 "$work/twice"
  listed_on 2026-13-01 month
  expect refuse "is not a date" 2027-01-01 "$work/month"
  expect refuse "no Public Suffix List at" 2026-10-22 "$work/absent"
  expect refuse "today, 22/10/2026, is not a date" 22/10/2026 "$work/list"

  # The vendored list's own VERSION line is read: a century later it is refused for its age, not its form.
  local vendored
  vendored="$(dirname "$self")/../$DEFAULT_LIST"
  expect refuse "days old, from" 2126-01-01 "$vendored"

  echo "require-public-suffix-fresh.sh: $cases cases, a list up to $MAX_DAYS days old accepted and one older, undated or from the future refused"
}

case "${1:-}" in
  --selftest) [ $# -eq 1 ] || die "usage: require-public-suffix-fresh.sh --selftest"; selftest ;;
  *) [ $# -le 1 ] || die "usage: require-public-suffix-fresh.sh [list]"; check "${1:-$DEFAULT_LIST}" ;;
esac
