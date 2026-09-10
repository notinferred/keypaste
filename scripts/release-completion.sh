#!/usr/bin/env bash
# The completion record for a published component release, and the check that the bytes actually
# being served are the ones it names.
#
# R.0b. `require-release-assets.sh` decides whether a STAGED directory is a whole release; this
# decides whether a PUBLISHED one is, which is a different question with a different failure mode.
# An upload that stops partway through leaves a prefix that looks published and is not, and the
# destination guard will then refuse that version forever (F.4a, deliberately) - so the only safe
# recovery is a new version number, and the only way to know a recovery is needed is to ask the
# public origin what it is serving. Nothing did.
#
# What it does NOT do:
#   - It does not publish the record. Adding a file to the uploaded set changes what becomes
#     world-readable, which `require-release-assets.sh`'s allowlist exists to hold; that is a
#     deliberate change and not a side effect of adding a check. The record is retained release
#     evidence (RELEASE.md requirement 7), and `SHA256SUMS` is what an outsider verifies against.
#   - It does not authenticate origin. A hash fetched from the same origin as the bytes proves
#     transport, not provenance - SECURITY.md says so plainly and 3.8 owns the rest (D-0116).
#   - It does not move the advertised version. That is one hand-made `published` entry in
#     release-targets.json, kept separate on purpose so a green run is not self-promoting.
#
# Usage:
#   release-completion.sh record <version> <tag> <commit> <dir>   # prints the record to stdout
#   release-completion.sh verify <version> <record.json>          # asks the public origin
#
# Environment:
#   KEYPASTE_RELEASE_DEFINITION  the definition to read   (default: release-targets.json)
#   KEYPASTE_ORIGIN_OVERRIDE     fetch from here instead of the definition's origin (fixtures)
#   KEYPASTE_VERIFY_ATTEMPTS     tries per asset before refusing  (default: 5)
#   KEYPASTE_VERIFY_SLEEP        seconds between tries            (default: 6)
set -euo pipefail

readonly DEFINITION="${KEYPASTE_RELEASE_DEFINITION:-release-targets.json}"
readonly CR=$'\r'

die() { echo "::error::$*" >&2; exit 1; }
jqr() { command jq -r "$@" | tr -d "$CR"; }

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$1" | awk '{print $1}'
  else
    die "no sha256sum or shasum; this gate cannot hash and will not guess"
  fi
}

bytes_of() { wc -c < "$1" | tr -d '[:space:]'; }

# The one list of what a whole release is, computed the way require-release-assets.sh computes it so
# the two cannot drift into disagreeing about the same release.
expected_assets() {
  local version="$1" archives
  archives="$(jqr --arg v "$version" '
    .components.cli as $c
    | $c.targets[]
    | . as $t
    | $c.archive_pattern
    | split("{version}") | join($v)
    | split("{rid}")     | join($t.rid)
    | split("{ext}")     | join($t.archive)
  ' "$DEFINITION")"
  [ -n "$archives" ] || die "$DEFINITION named no archives for $version"
  {
    printf '%s\n' "$archives"
    printf '%s\n' "$archives" | sed 's/$/.sha256/'
    printf 'keypaste-%s-source.tar.gz\n'        "$version"
    printf 'keypaste-%s-source.tar.gz.sha256\n' "$version"
    printf 'SHA256SUMS\n'
  } | sed '/^$/d'
}

origin_for() {
  local version="$1"
  if [ -n "${KEYPASTE_ORIGIN_OVERRIDE:-}" ]; then
    printf '%s' "$KEYPASTE_ORIGIN_OVERRIDE"
    return
  fi
  jqr --arg v "$version" '.components.cli.origin | split("{version}") | join($v)' "$DEFINITION"
}

# ---------------------------------------------------------------------------
cmd_record() {
  local version="$1" tag="$2" commit="$3" dir="$4" origin name first=1
  [ -d "$dir" ] || die "no directory at $dir"
  origin="$(origin_for "$version")"
  [ -n "$origin" ] && [ "$origin" != "null" ] || die "no origin for $version in $DEFINITION"

  # A record is written from a COMPLETE directory or not at all. A target that failed to build would
  # otherwise produce a smaller record that then verifies perfectly against a smaller release.
  while IFS= read -r name; do
    [ -n "$name" ] || continue
    [ -f "$dir/$name" ] \
      || die "$dir has no $name, so this is not a whole release and gets no completion record"
  done < <(expected_assets "$version")

  printf '{\n'
  printf '  "schema": 1,\n'
  printf '  "component": "cli",\n'
  printf '  "version": "%s",\n' "$version"
  printf '  "tag": "%s",\n'     "$tag"
  printf '  "commit": "%s",\n'  "$commit"
  printf '  "origin": "%s",\n'  "$origin"
  printf '  "assets": [\n'
  while IFS= read -r name; do
    [ -n "$name" ] || continue
    [ "$first" -eq 1 ] || printf ',\n'
    first=0
    printf '    { "name": "%s", "sha256": "%s", "bytes": %s, "url": "%s%s" }' \
      "$name" "$(sha256_of "$dir/$name")" "$(bytes_of "$dir/$name")" "$origin" "$name"
  done < <(expected_assets "$version")
  printf '\n  ]\n}\n'
}

# ---------------------------------------------------------------------------
cmd_verify() {
  local version="$1" record="$2" work name want_sha want_bytes url got_sha got_bytes
  local problems=0 checked=0 expected_count actual_count
  local attempts="${KEYPASTE_VERIFY_ATTEMPTS:-5}" naptime="${KEYPASTE_VERIFY_SLEEP:-6}"
  case "$attempts" in '' | *[!0-9]*) die "KEYPASTE_VERIFY_ATTEMPTS is not a number: $attempts" ;; esac
  [ "$attempts" -ge 1 ] || die "KEYPASTE_VERIFY_ATTEMPTS must be at least 1"

  [ -f "$record" ] || die "no completion record at $record"
  command -v curl >/dev/null 2>&1 || die "no curl to ask the public origin with"

  # A record that itself omits a target would verify perfectly against a release that omits it too.
  expected_count="$(expected_assets "$version" | sed '/^$/d' | wc -l | tr -d '[:space:]')"
  actual_count="$(jqr '.assets | length' "$record")"
  [ "$actual_count" = "$expected_count" ] \
    || die "the record names $actual_count assets and a whole $version is $expected_count; it is a record of an incomplete release"

  work="$(mktemp -d)"
  trap 'rm -rf "$work"' RETURN

  while IFS=$'\t' read -r name want_sha want_bytes url; do
    [ -n "$name" ] || continue
    checked=$((checked + 1))
    local attempt=1 why=""
    # Tried more than once, and this is not superstition. The bytes are ALREADY uploaded by the time
    # this runs, so a false refusal here does not fail a bad release - it condemns a good one, and
    # F.4a's destination guard then refuses that version forever, making the only recovery a version
    # number spent for nothing. An origin that has not caught up yet is a wait, not an answer. A
    # mismatch is retried for the same reason: a cache mid-fill can serve a short or stale object.
    while :; do
      why=""
      # Anonymous on purpose: no credential, no header, nothing this repository holds. What a
      # stranger gets is the only thing this check is about.
      if ! curl -fsS -L --max-time 60 -o "$work/asset" "$url" 2>/dev/null; then
        why="is not being served at $url"
      else
        got_sha="$(sha256_of "$work/asset")"
        got_bytes="$(bytes_of "$work/asset")"
        if [ "$got_sha" != "$want_sha" ]; then
          why="at $url hashes $got_sha and the record says $want_sha"
        elif [ "$got_bytes" != "$want_bytes" ]; then
          why="at $url is $got_bytes bytes and the record says $want_bytes"
        fi
      fi
      [ -n "$why" ] || break
      [ "$attempt" -lt "$attempts" ] || break
      echo "  .. $name $why (try $attempt of $attempts)"
      attempt=$((attempt + 1))
      sleep "$naptime"
    done

    if [ -n "$why" ]; then
      echo "::error::  $name $why"
      problems=$((problems + 1))
      continue
    fi
    echo "  ok  $name ($got_bytes bytes)"
  done < <(jqr '.assets[] | [.name, .sha256, (.bytes|tostring), .url] | @tsv' "$record")

  [ "$checked" -gt 0 ] || die "the record named no assets, so nothing was checked"
  [ "$problems" -eq 0 ] \
    || die "$problems of $checked published assets are not what the record says; this version is not complete and must not be advertised - recover with a NEW version, never by rewriting this one"

  echo "ok: all $checked assets of $version are served at the public origin exactly as recorded"
}

case "${1:-}" in
  record) shift; [ $# -eq 4 ] || die "usage: release-completion.sh record <version> <tag> <commit> <dir>"; cmd_record "$@" ;;
  verify) shift; [ $# -eq 2 ] || die "usage: release-completion.sh verify <version> <record.json>";        cmd_verify "$@" ;;
  *) die "usage: release-completion.sh record|verify ..." ;;
esac
