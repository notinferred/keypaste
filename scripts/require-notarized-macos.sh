#!/usr/bin/env bash
# Takes the macOS packages a release publishes from the notarize.yml run that finished them, and refuses them unless
# they are this release's own, notarized as Gatekeeper meets them.
#
# Apple answers a submission minutes or hours after it, so no release job waits: notarize.yml is dispatched with the
# tag's app run and this release run, and this release run's failed jobs are re-run once that finish has succeeded.
# The notarize run is found by its name, and nothing it uploaded is believed for that: the DMG must seal the same
# code directory as the one app.yml attested for the tag, and the CLI archive must be this run's own bytes.
#
# Usage:
#   require-notarized-macos.sh find <app-run> <release-run> <tag>
#       Prints run=<id> for $GITHUB_OUTPUT: the newest successful notarize.yml finish for both runs, dispatched on
#       main or the tag. With none, refuses and names the dispatches to make.
#   require-notarized-macos.sh check <version> <rid> <app-dir> <cli-dir> <notarized-dir> <out-dir>
#       On macOS. The app run's DMG is the one app.yml attested for v<version>; the notarized DMG seals the same code
#       directory, carries its stapled ticket and Gatekeeper accepts it; the notarized CLI archive is byte for byte
#       the one in cli-dir, and its code runs quarantined. Copies the notarized DMG and a .sha256 into out-dir.
#   require-notarized-macos.sh --selftest
#
# Environment:
#   GITHUB_REPOSITORY, GH_TOKEN   for find and for check's attestation lookup
#   KEYPASTE_RELEASE_DEFINITION   the definition to read (default: release-targets.json)
set -euo pipefail

readonly SELF="${BASH_SOURCE[0]}"
ROOT="$(cd "$(dirname "$SELF")/.." && pwd)"
readonly ROOT
readonly CR=$'\r'

die() { echo "::error::$*" >&2; exit 1; }
jqr() { command jq -r "$@" | tr -d "$CR"; }
definition() { printf '%s' "${KEYPASTE_RELEASE_DEFINITION:-$ROOT/release-targets.json}"; }

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum < "$1" | awk '{print $1}'; else shasum -a 256 < "$1" | awk '{print $1}'; fi
}

# notarize.yml's run-name, which is how a run's inputs can be read back.
title_of() { printf 'notarize finish app run %s release run %s' "$1" "$2"; }

find_run() {
  local app_run="$1" release_run="$2" tag="$3" title id
  [ -n "${GITHUB_REPOSITORY:-}" ] || die "GITHUB_REPOSITORY is not set"
  title="$(title_of "$app_run" "$release_run")"
  # The API lists the newest run first.
  # shellcheck disable=SC2016 # jq expands these variables, not bash.
  id="$(gh api "repos/$GITHUB_REPOSITORY/actions/workflows/notarize.yml/runs?event=workflow_dispatch&status=success&per_page=100" \
    | jqr --arg t "$title" --arg tag "$tag" \
      '[.workflow_runs[]? | select(.display_title == $t and (.head_branch == "main" or .head_branch == $tag)) | .id] | first // empty')" \
    || die "could not list notarize.yml's runs"
  if [ -z "$id" ]; then
    echo "::error::no notarize.yml finish has succeeded for app run $app_run and release run $release_run, so nothing macOS is notarized yet" >&2
    echo "::error::dispatch: gh workflow run notarize.yml -f mode=submit -f app_run=$app_run -f release_run=$release_run; then, once Apple has answered, mode=finish with submit_run set to that run; then re-run this run's failed jobs" >&2
    exit 1
  fi
  echo "notarize.yml run $id finished app run $app_run and release run $release_run" >&2
  echo "run=$id"
}

# Each is one call, so the selftest can stand in for what only a macOS runner and GitHub can answer.
attested() { bash "$ROOT/scripts/verify-desktop-candidate.sh" "$@" >&2; }
cdhash_of() { codesign -dvvv "$1" 2>&1 | sed -n 's/^CDHash=//p' | head -n 1; }
gatekeeper_accepts() { bash "$ROOT/scripts/verify-macos-signature.sh" "$1" < /dev/null; }

cli_archive() {
  local version="$1" rid="$2" ext
  # shellcheck disable=SC2016 # jq expands $rid, not bash.
  ext="$(jqr --arg rid "$rid" '.components.cli.targets[] | select(.rid == $rid) | .archive' "$(definition)")"
  [ -n "$ext" ] || die "$(definition) declares no cli target $rid"
  jqr '.components.cli.archive_pattern' "$(definition)" | sed -e "s/{version}/$version/g" -e "s/{rid}/$rid/g" -e "s/{ext}/$ext/g"
}

check() {
  local version="$1" rid="$2" app_dir="$3" cli_dir="$4" notarized_dir="$5" out="$6" dmg archive original notarized signed sealed unpacked
  dmg="$(bash "$ROOT/scripts/verify-desktop-candidate.sh" --name "$version" "$rid")" || exit 1
  archive="$(cli_archive "$version" "$rid")" || exit 1
  original="$app_dir/$dmg" notarized="$notarized_dir/$dmg"

  attested "$original" "$version" "$rid" || die "$dmg from the app run is not the one app.yml attested for v$version"
  [ -f "$notarized" ] || die "the notarize run holds no $dmg"
  signed="$(cdhash_of "$original")" sealed="$(cdhash_of "$notarized")"
  [ -n "$signed" ] || die "$dmg from the app run carries no code directory"
  [ "$sealed" = "$signed" ] || die "the notarized $dmg seals code directory ${sealed:-none}, not the attested image's $signed"
  gatekeeper_accepts "$notarized" || die "Gatekeeper does not accept the notarized $dmg"

  [ -f "$cli_dir/$archive" ] || die "this release run holds no $archive"
  [ -f "$notarized_dir/$archive" ] || die "the notarize run did not notarize $archive; dispatch it with this release run as release_run"
  cmp -s "$cli_dir/$archive" "$notarized_dir/$archive" || die "the notarize run's $archive is not this release run's"
  unpacked="$(mktemp -d)"
  tar -xzf "$cli_dir/$archive" -C "$unpacked"
  gatekeeper_accepts "$unpacked" || { rm -rf "$unpacked"; die "Gatekeeper does not run the code in $archive"; }
  rm -rf "$unpacked"

  mkdir -p "$out"
  cp "$notarized" "$out/$dmg"
  printf '%s  %s\n' "$(sha256_of "$out/$dmg")" "$dmg" > "$out/$dmg.sha256"
  echo "ok: $dmg is app.yml's attested image, notarized and stapled; $archive is this run's, notarized" >&2
}

selftest() {
  local work v=9.9.9 rid=osx-arm64 cases=0 failures=0 dmg archive
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' RETURN
  export KEYPASTE_RELEASE_DEFINITION="$ROOT/release-targets.json" GITHUB_REPOSITORY=notinferred/keypaste
  dmg="$(bash "$ROOT/scripts/verify-desktop-candidate.sh" --name "$v" "$rid")"
  archive="$(cli_archive "$v" "$rid")"

  # The API's answer, and what the stand-ins say about each file, are files the cases rewrite.
  gh() { cat "$work/runs.json"; }
  attested() { [ ! -f "$work/unattested" ]; }
  cdhash_of() { head -n 1 "$1.cdhash" 2>/dev/null || true; }
  gatekeeper_accepts() { [ ! -f "$work/refuse-$(basename "$1")" ]; }

  run() { jq -n --argjson r "$1" '{workflow_runs: $r}' > "$work/runs.json"; }
  stage() {
    rm -rf "$work/app" "$work/cli" "$work/notarized" "$work/out" "$work/unattested" "$work"/refuse-*
    mkdir -p "$work/app" "$work/cli" "$work/notarized"
    printf 'image\n' > "$work/app/$dmg"; echo AAAA > "$work/app/$dmg.cdhash"
    printf 'image and ticket\n' > "$work/notarized/$dmg"; echo AAAA > "$work/notarized/$dmg.cdhash"
    mkdir -p "$work/code" && printf 'code\n' > "$work/code/keypaste"
    tar -czf "$work/cli/$archive" -C "$work/code" . && cp "$work/cli/$archive" "$work/notarized/$archive"
  }

  # expect <pass|refuse> <name> <text the run must print> -- <command>...
  expect() {
    local want="$1" name="$2" needle="$3" out status
    shift 4
    cases=$((cases + 1))
    set +e
    out="$("$@" 2>&1)"
    status=$?
    set -e
    if { [ "$want" = pass ] && [ "$status" -ne 0 ]; } || { [ "$want" = refuse ] && [ "$status" -eq 0 ]; }; then
      echo "::error::$name: wanted $want, exited $status: $out"; failures=$((failures + 1)); return
    fi
    case "$out" in *"$needle"*) echo "  $want  $name" ;; *) echo "::error::$name did not say '$needle': $out"; failures=$((failures + 1)) ;; esac
  }

  local title
  title="$(title_of 11 22)"
  run "[{\"id\": 3, \"display_title\": \"$title\", \"head_branch\": \"main\"}, {\"id\": 2, \"display_title\": \"$title\", \"head_branch\": \"main\"}]"
  expect pass finds-the-newest "run=3" -- find_run 11 22 v$v
  run "[{\"id\": 4, \"display_title\": \"$title\", \"head_branch\": \"v$v\"}]"
  expect pass finds-one-dispatched-on-the-tag "run=4" -- find_run 11 22 v$v
  run "[{\"id\": 5, \"display_title\": \"$title\", \"head_branch\": \"task/notarize\"}]"
  expect refuse refuses-a-branch-run "mode=submit -f app_run=11 -f release_run=22" -- find_run 11 22 v$v
  run "[{\"id\": 6, \"display_title\": \"$(title_of 11 23)\", \"head_branch\": \"main\"}, {\"id\": 7, \"display_title\": \"notarize submit app run 11 release run 22\", \"head_branch\": \"main\"}]"
  expect refuse refuses-another-release-or-a-submit "no notarize.yml finish has succeeded" -- find_run 11 22 v$v
  run '[]'
  expect refuse refuses-when-none-ran "re-run this run's failed jobs" -- find_run 11 22 v$v

  local args=("$v" "$rid" "$work/app" "$work/cli" "$work/notarized" "$work/out")
  stage
  expect pass takes-the-notarized-image "is app.yml's attested image" -- check "${args[@]}"
  [ "$(cat "$work/out/$dmg")" = "image and ticket" ] && [ "$(cut -d' ' -f1 < "$work/out/$dmg.sha256")" = "$(sha256_of "$work/out/$dmg")" ] \
    || { echo "::error::the notarized image and its .sha256 did not reach the output"; failures=$((failures + 1)); }
  stage; touch "$work/unattested"
  expect refuse refuses-an-unattested-image "is not the one app.yml attested" -- check "${args[@]}"
  stage; echo BBBB > "$work/notarized/$dmg.cdhash"
  expect refuse refuses-another-image "seals code directory BBBB" -- check "${args[@]}"
  stage; rm "$work/notarized/$dmg"
  expect refuse refuses-no-notarized-image "holds no $dmg" -- check "${args[@]}"
  stage; touch "$work/refuse-$dmg"
  expect refuse refuses-an-image-gatekeeper-refuses "Gatekeeper does not accept" -- check "${args[@]}"
  stage; rm "$work/notarized/$archive"
  expect refuse refuses-a-cli-never-submitted "did not notarize $archive" -- check "${args[@]}"
  stage; printf 'other\n' > "$work/notarized/$archive"
  expect refuse refuses-another-cli "is not this release run's" -- check "${args[@]}"
  [ ! -d "$work/out" ] || { echo "::error::a refused check left an output"; failures=$((failures + 1)); }

  [ "$failures" -eq 0 ] || die "$failures of $cases require-notarized-macos.sh cases failed"
  echo "ok: $cases require-notarized-macos.sh cases. The newest finish for both runs, on main or the tag, is found; a"
  echo "    branch's, a submit's or another release's is not. An unattested, different, absent or refused image and an"
  echo "    absent or different CLI archive all refuse, and nothing reaches the output."
}

case "${1:-}" in
  --selftest) selftest ;;
  find) [ $# -eq 4 ] || die "usage: require-notarized-macos.sh find <app-run> <release-run> <tag>"; find_run "$2" "$3" "$4" ;;
  check)
    [ $# -eq 7 ] || die "usage: require-notarized-macos.sh check <version> <rid> <app-dir> <cli-dir> <notarized-dir> <out-dir>"
    [ "$(uname -s)" = Darwin ] || die "a notarized package is checked on macOS"
    check "$2" "$3" "$4" "$5" "$6" "$7" ;;
  *) die "usage: require-notarized-macos.sh find <app-run> <release-run> <tag> | check <version> <rid> <app-dir> <cli-dir> <notarized-dir> <out-dir> | --selftest" ;;
esac
