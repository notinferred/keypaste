#!/usr/bin/env bash
# Decides whether a staged directory is a complete, publishable release, and nothing else.
#
# This is the last thing between a directory and a public bucket. It was three step bodies in
# release.yml's publish job, which runs only when guard says publish=true - so it ran only on a tag
# and only against the one directory a real release built. D-0109's rule applied to the steps with
# the most to lose: the decision is here, a fixture drives it, and the workflow calls it.
#
# What it refuses, and why each is not covered by the others:
#   - a checksum that does not verify. The archives were built on four machines and travelled
#     through artifact storage; a corrupt transport is otherwise invisible.
#   - the wrong NUMBER of assets, against TWO numbers, per component. Equality with what
#     release-targets.json advertises catches a target that failed to build. A floor taken from the
#     largest set that component has already published catches the edit that drops a target from the
#     definition, which equality alone would happily agree with (D-0108).
#   - a file that is not a release asset. The upload is recursive, so a stray vault fixture, a build
#     log or an .env picked up by a future edit to packaging would be published silently. The
#     expected names come from the definition, not from a list written out here.
#   - an asset with no checksum beside it, and a checksum with no asset beside it. An allowlist
#     alone is satisfied by an empty directory and by half a release.
#   - a release without its manifest or its attestation bundle, or without the source and SHA256SUMS
#     they vouch for. Once published a prefix cannot be completed, so a version uploaded without
#     its provenance could never be verified (3.8).
#   - a PACKAGE offered for publication that is not signed. A package is a thing a stranger
#     double-clicks, so it is published signed or it is not published at all (4.7c). The cli
#     declares no packages, so this rule cannot reach it; it starts deciding the day the definition
#     offers one.
#
# One invocation covers every component being published, because they share one directory and one
# upload: asked about the cli alone, this would see the desktop packages as strays. The allowlist is
# therefore the union, while the two counts stay per component.
#
# Usage:
#   require-release-assets.sh [--component <c>]... <version> <dir>
#   require-release-assets.sh --component <c> --publishable
#
# --publishable answers whether a component may be published at all, without looking at a directory:
#   0  every package it offers is signed and public, or it publishes archives
#   2  it is declared and not yet publishable, in the one recorded shape: the policy is none, every
#      package is internal and unsigned, and signing.until names the step that lifts it
#   1  anything else, including a half-signed mixture. A component does not drop out of a release
#      quietly.
#
# Environment:
#   KEYPASTE_RELEASE_DEFINITION  the definition to read (default: release-targets.json)
set -euo pipefail

readonly DEFINITION="${KEYPASTE_RELEASE_DEFINITION:-release-targets.json}"

die() { echo "::error::$*" >&2; exit 1; }

# Windows jq writes CRLF, and a CR riding on a filename makes every advertised archive both absent
# and unrecognised at once - which reads like a broken release rather than a broken read. Stripped
# at every jq call here for the reason verify-release-matrix.sh strips it at one: a call site is a
# thing somebody adds without.
CR="$(printf '\r')"
US="$(printf '\037')"
jqr() { command jq -r "$@" | tr -d "$CR"; }

COMPONENTS=()
PUBLISHABLE=false
while [ $# -gt 0 ]; do
  case "$1" in
    --component) [ $# -ge 2 ] || die "--component needs a component name"; COMPONENTS+=("$2"); shift 2 ;;
    --publishable) PUBLISHABLE=true; shift ;;
    *) break ;;
  esac
done
[ ${#COMPONENTS[@]} -gt 0 ] || COMPONENTS=(cli)

[ -f "$DEFINITION" ] || die "no release definition at $DEFINITION"
command -v jq >/dev/null 2>&1 || die "no jq; this gate reads the definition and cannot run without it"

for c in "${COMPONENTS[@]}"; do
  [ "$(jqr --arg c "$c" '.components | has($c)' "$DEFINITION")" = "true" ] \
    || die "$DEFINITION declares no component $c"
done

# ---------------------------------------------------------------------------
# --publishable: a question about the definition alone.
# ---------------------------------------------------------------------------
if [ "$PUBLISHABLE" = true ]; then
  [ ${#COMPONENTS[@]} -eq 1 ] || die "--publishable answers about one component at a time"
  c="${COMPONENTS[0]}"
  packages='.components[$c].targets[] | (.packages // [])[]'
  total="$(jqr --arg c "$c" "[$packages] | length" "$DEFINITION")"
  offered="$(jqr --arg c "$c" "[$packages | select(.internal == false)] | length" "$DEFINITION")"
  held="$(jqr --arg c "$c" "[$packages | select(.internal == true and .signed == false)] | length" "$DEFINITION")"
  unsigned="$(jqr --arg c "$c" "[$packages | select(.internal == false and .signed != true)] | length" "$DEFINITION")"
  policy="$(jqr --arg c "$c" '.components[$c].signing.policy // ""' "$DEFINITION")"
  origin="$(jqr --arg c "$c" '.components[$c].origin // ""' "$DEFINITION")"
  lifts="$(jqr --arg c "$c" '[.components[$c].signing.until // []] | flatten | length' "$DEFINITION")"

  [ -n "$origin" ] || { echo "::error::$c has no origin, so nothing of it can be published" >&2; exit 1; }

  if [ "$total" -eq 0 ] || { [ "$offered" -eq "$total" ] && [ "$unsigned" -eq 0 ] && [ "$policy" != none ]; }; then
    echo "  ok  $c is publishable"
    exit 0
  fi
  if [ "$policy" = none ] && [ "$held" -eq "$total" ] && [ "$lifts" -ge 1 ]; then
    echo "  $c is declared and not yet publishable: the signing policy is none and all $total package(s) stay internal and unsigned until $(jqr --arg c "$c" '.components[$c].signing.until | join(", ")' "$DEFINITION")"
    exit 2
  fi
  echo "::error::$c is neither publishable nor in the one recorded not-yet state: policy $policy, $offered of $total package(s) offered, $unsigned of those unsigned" >&2
  exit 1
fi

VERSION="${1:-}"
DIR="${2:-}"
[ -n "$VERSION" ] || die "usage: require-release-assets.sh [--component <c>]... <version> <dir>"
[ -n "$DIR" ] || die "usage: require-release-assets.sh [--component <c>]... <version> <dir>"
[ -d "$DIR" ] || die "no directory at $DIR"

# ---------------------------------------------------------------------------
# What each component contributes, and the two numbers it is held to.
# ---------------------------------------------------------------------------

# rid and name, so the floor below can be about RIDs rather than about however many files a RID
# happens to carry. A component publishing two packages for one RID must not read as two RIDs.
pairs_of() {
  jqr --arg v "$VERSION" --arg c "$1" '
    .components[$c] as $comp
    | if ($comp.publishes // "") == "packages"
      then $comp.targets[] | . as $t | (.packages // [])[] | select(.internal == false)
           | .pattern
           | split("{version}") | join($v)
           | split("{rid}")     | join($t.rid)
           | [$t.rid, .]
      else $comp.targets[] | . as $t | $comp.archive_pattern
           | split("{version}") | join($v)
           | split("{rid}")     | join($t.rid)
           | split("{ext}")     | join($t.archive)
           | [$t.rid, .]
      end
    | @tsv' "$DEFINITION"
}

want_of() {
  jqr --arg c "$1" '
    .components[$c] as $comp
    | if ($comp.publishes // "") == "packages"
      then [$comp.targets[] | [(.packages // [])[] | select(.internal == false)] | length] | add // 0
      else $comp.targets | length
      end' "$DEFINITION"
}

# jq's max of an empty list is null, and a component that has published nothing yet has exactly that.
# `// 0` makes an unpublished component the number zero rather than an unreadable answer.
floor_of() {
  jqr --arg c "$1" '[.published[] | select(.component == $c) | .rids | length] | max // 0' "$DEFINITION"
}

provenance_name() {
  jqr --arg v "$VERSION" --arg f "$1" --arg c "$2" \
    '.components[$c].provenance[$f] // empty | split("{version}") | join($v)' "$DEFINITION"
}

# A package is offered for publication by recording internal: false, and that flag is the only thing
# read here. It is what 3.6b flips when signing turns on, so this same code refuses the desktop today
# - nothing is offered - and demands a signature then, rather than being remembered.
offered_packages() {
  local c
  for c in "${COMPONENTS[@]}"; do
    jqr --arg c "$c" '
      .components[$c] as $comp
      | $comp.targets[] | . as $t
      | (.packages // [])[] | select(.internal == false)
      | [$c, $t.rid, (.kind // ""), ($comp.signing.policy // ""), (.signed | tostring)]
      | join("")' "$DEFINITION"
  done
}

bad=0

while IFS="$US" read -r c rid kind policy signed; do
  [ -n "$c" ] || continue
  # One line each, so a fixture can delete either and get a working script that fails open, rather
  # than a syntax error. A control that cannot reproduce the old answer proves nothing (D-0043).
  [ "$signed" = "true" ] || { echo "::error::$c/$rid: the $kind package is offered for publication and is not recorded signed" >&2; bad=1; }
  [ "$policy" != none ] || { echo "::error::$c/$rid: the $kind package is offered for publication while the $c signing policy is none" >&2; bad=1; }
done < <(offered_packages)

expected=""
source_archive="keypaste-${VERSION}-source.tar.gz"
declare -A WANT=() FLOOR=() NAMES_OF=() RIDS_OF=()

for c in "${COMPONENTS[@]}"; do
  pairs="$(pairs_of "$c")"
  NAMES_OF["$c"]="$(printf '%s\n' "$pairs" | sed '/^$/d' | cut -f2)"
  RIDS_OF["$c"]="$(printf '%s\n' "$pairs" | sed '/^$/d' | cut -f1)"
  WANT["$c"]="$(want_of "$c")"
  FLOOR["$c"]="$(floor_of "$c")"
  for n in "${WANT[$c]}" "${FLOOR[$c]}"; do
    case "$n" in '' | *[!0-9]*) die "$DEFINITION gave no count for $c; refusing without a verified answer" ;; esac
  done

  manifest="$(provenance_name manifest_pattern "$c")"
  bundle="$(provenance_name bundle_pattern "$c")"
  [ -n "$manifest" ] && [ -n "$bundle" ] \
    || die "$DEFINITION names no manifest or attestation bundle for $c $VERSION"
  expected="$(printf '%s\n%s\n%s' "$expected" "$manifest" "$bundle")"
  [ -z "${NAMES_OF[$c]}" ] || expected="$(printf '%s\n%s' "$expected" "${NAMES_OF[$c]}")"

  # The corresponding source and the aggregate SHA256SUMS belong to the release rather than to a
  # component, and ride with whichever one publishes the archives.
  if [ "$(jqr --arg c "$c" '.components[$c].publishes // ""' "$DEFINITION")" = archives ]; then
    expected="$(printf '%s\n%s\nSHA256SUMS' "$expected" "$source_archive")"
  fi
done
expected="$(printf '%s\n' "$expected" | sed '/^$/d')"

cd "$DIR"

# dotglob is load-bearing: bash's * skips dotfiles and `aws s3 cp --recursive` uploads them, so
# without it a planted .env walks past this and onto a public origin. Observed once.
shopt -s dotglob nullglob

built=0
for f in *; do
  [ -f "$f" ] || { echo "::error::$f is not a regular file" >&2; bad=1; continue; }
  case "$f" in
    *.sha256)
      [ -f "${f%.sha256}" ] || { echo "::error::$f has no asset beside it" >&2; bad=1; }
      continue
      ;;
  esac
  if printf '%s\n' "$expected" | grep -qxF -- "$f"; then
    built=$((built + 1))
  else
    echo "::error::$f is not a release asset and must not be published" >&2
    bad=1
  fi
done

while IFS= read -r f; do
  [ -n "$f" ] || continue
  [ -f "$f" ] || { echo "::error::$f is missing, and a release published without it can never be verified" >&2; bad=1; }
done < <(printf '%s\n' "$expected" | grep -E '(-source\.tar\.gz|^SHA256SUMS$|-manifest\.json$|\.sigstore\.jsonl$)' || true)

# Every advertised asset is present, with its checksum, and that checksum verifies. Counted per
# component, and by RID, so one component's surplus cannot cover another's gap.
for c in "${COMPONENTS[@]}"; do
  present=0
  while IFS= read -r a; do
    [ -n "$a" ] || continue
    [ -f "$a" ] || { echo "::error::$a is advertised and missing from this release" >&2; bad=1; continue; }
    [ -f "$a.sha256" ] || { echo "::error::$a has no checksum beside it" >&2; bad=1; continue; }
    if command -v sha256sum >/dev/null 2>&1; then
      sha256sum -c "$a.sha256" >/dev/null 2>&1 || { echo "::error::$a does not match its checksum" >&2; bad=1; }
    else
      shasum -a 256 -c "$a.sha256" >/dev/null 2>&1 || { echo "::error::$a does not match its checksum" >&2; bad=1; }
    fi
    present=$((present + 1))
  done < <(printf '%s\n' "${NAMES_OF[$c]}")

  covered=""
  while IFS= read -r r; do
    [ -n "$r" ] || continue
    case " $covered " in *" $r "*) ;; *) covered="$covered $r" ;; esac
  done < <(printf '%s\n' "${RIDS_OF[$c]}")
  rids=0
  for r in $covered; do rids=$((rids + 1)); done

  # Both on one line each, so verify-release-preflight.sh can delete the floor and get the shape
  # equality alone agrees with, rather than a syntax error. A control that cannot reproduce the old
  # answer proves nothing about the new one (D-0043).
  [ "$present" -eq "${WANT[$c]}" ] || { echo "::error::$present built $c assets, $DEFINITION advertises ${WANT[$c]}" >&2; bad=1; }
  [ "$rids" -ge "${FLOOR[$c]}" ] || { echo "::error::$rids $c rids built; ${FLOOR[$c]} were already published and a release cannot shrink" >&2; bad=1; }
done

[ "$bad" -eq 0 ] || die "refusing to publish $DIR"
echo "  ok  $built advertised assets, their checksums, the corresponding source, SHA256SUMS, and a manifest and bundle for: ${COMPONENTS[*]}"
