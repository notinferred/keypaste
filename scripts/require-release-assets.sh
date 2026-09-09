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
#   - the wrong NUMBER of assets, against TWO numbers. Equality with what release-targets.json
#     advertises catches a target that failed to build. A floor taken from the largest set any
#     release has already published catches the edit that drops a target from the definition, which
#     equality alone would happily agree with (D-0108).
#   - a file that is not a release asset. The upload is recursive, so a stray vault fixture, a build
#     log or an .env picked up by a future edit to packaging would be published silently. The
#     expected names come from the definition's archive_pattern, not from a list written out here.
#   - an asset with no checksum beside it, and a checksum with no asset beside it. An allowlist
#     alone is satisfied by an empty directory and by half a release.
#
# Usage:
#   require-release-assets.sh <version> <dir>
#
# Environment:
#   KEYPASTE_RELEASE_DEFINITION  the definition to read (default: release-targets.json)
set -euo pipefail

readonly DEFINITION="${KEYPASTE_RELEASE_DEFINITION:-release-targets.json}"

die() { echo "::error::$*" >&2; exit 1; }

VERSION="${1:-}"
DIR="${2:-}"
[ -n "$VERSION" ] || die "usage: require-release-assets.sh <version> <dir>"
[ -n "$DIR" ] || die "usage: require-release-assets.sh <version> <dir>"
[ -d "$DIR" ] || die "no directory at $DIR"
[ -f "$DEFINITION" ] || die "no release definition at $DEFINITION"
command -v jq >/dev/null 2>&1 || die "no jq; this gate reads the definition and cannot run without it"

# Windows jq writes CRLF, and a CR riding on a filename makes every advertised archive both absent
# and unrecognised at once - which reads like a broken release rather than a broken read. Stripped
# at every jq call here for the reason verify-release-matrix.sh strips it at one: a call site is a
# thing somebody adds without.
CR="$(printf '\r')"

archives="$(jq -r --arg v "$VERSION" '
  .components.cli as $c
  | $c.targets[]
  | . as $t
  | $c.archive_pattern
  | split("{version}") | join($v)
  | split("{rid}")     | join($t.rid)
  | split("{ext}")     | join($t.archive)
' "$DEFINITION" | tr -d "$CR")"
[ -n "$archives" ] || die "$DEFINITION named no archives for $VERSION"

want="$(jq -r '.components.cli.targets | length' "$DEFINITION" | tr -d "$CR")"
floor="$(jq -r '[.published[] | select(.component == "cli") | .rids | length] | max' "$DEFINITION" | tr -d "$CR")"
for n in want floor; do
  case "${!n}" in
    '' | *[!0-9]*) die "$DEFINITION gave no $n; refusing without a verified answer" ;;
  esac
done

source_archive="keypaste-${VERSION}-source.tar.gz"
expected="$(printf '%s\n%s\nSHA256SUMS\n' "$archives" "$source_archive")"

cd "$DIR"

# dotglob is load-bearing: bash's * skips dotfiles and `aws s3 cp --recursive` uploads them, so
# without it a planted .env walks past this and onto a public origin. Observed once.
shopt -s dotglob nullglob

bad=0
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
    case "$f" in
      SHA256SUMS | "$source_archive") ;;
      *) built=$((built + 1)) ;;
    esac
  else
    echo "::error::$f is not a release asset and must not be published" >&2
    bad=1
  fi
done

# Every advertised archive is present, with its checksum, and that checksum verifies.
while IFS= read -r a; do
  [ -n "$a" ] || continue
  [ -f "$a" ] || { echo "::error::$a is advertised and missing from this release" >&2; bad=1; continue; }
  [ -f "$a.sha256" ] || { echo "::error::$a has no checksum beside it" >&2; bad=1; continue; }
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum -c "$a.sha256" >/dev/null 2>&1 || { echo "::error::$a does not match its checksum" >&2; bad=1; }
  else
    shasum -a 256 -c "$a.sha256" >/dev/null 2>&1 || { echo "::error::$a does not match its checksum" >&2; bad=1; }
  fi
done <<ARCHIVES
$archives
ARCHIVES

# Both on one line each, so verify-release-preflight.sh can delete the floor and get the shape
# equality alone agrees with, rather than a syntax error. A control that cannot reproduce the old
# answer proves nothing about the new one (D-0043).
[ "$built" -eq "$want" ] || { echo "::error::$built built assets, $DEFINITION advertises $want" >&2; bad=1; }
[ "$built" -ge "$floor" ] || { echo "::error::$built built assets; $floor were already published and a release cannot shrink" >&2; bad=1; }

[ "$bad" -eq 0 ] || die "refusing to publish $DIR"
echo "  ok  $built advertised assets, their checksums, the corresponding source and SHA256SUMS"
