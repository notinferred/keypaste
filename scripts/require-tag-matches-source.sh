#!/usr/bin/env bash
# Refuses a tag that names a version the source does not declare.
#
# D-0041: the version lives in Directory.Build.props and the tag only names it. Nothing is passed
# with -p:Version=, so a local publish at the tagged commit produces the inputs the release did, and
# a tag that disagrees with the source is a mistake rather than an override.
#
# Extracted from release.yml's guard for D-0109's reason: the comparison was reachable only on a
# tag, because on a dispatch `publish=false` short-circuits it - so the check that decides whether a
# release is named correctly had never once been run in its refusing direction.
#
# The prerelease suffix is NOT compared. A tag may add one to the source's version - that is what a
# candidate is - so only the base is held to Directory.Build.props.
#
# Usage:
#   require-tag-matches-source.sh <tag-or-version>
#
# Environment:
#   KEYPASTE_VERSION_PREFIX  the declared version, when msbuild should not be asked (fixtures)
#   KEYPASTE_VERSION_PROJECT the project to ask     (default: src/Keypaste.Cli/Keypaste.Cli.csproj)
set -euo pipefail

readonly PROJECT="${KEYPASTE_VERSION_PROJECT:-src/Keypaste.Cli/Keypaste.Cli.csproj}"

die() { echo "::error::$*" >&2; exit 1; }

TAG="${1:-}"
[ -n "$TAG" ] || die "usage: require-tag-matches-source.sh <tag-or-version>"

version="${TAG#v}"
base="${version%%-*}"
[ -n "$base" ] || die "'$TAG' has no version in it"

prefix="${KEYPASTE_VERSION_PREFIX:-}"
if [ -z "$prefix" ]; then
  [ -f "$PROJECT" ] || die "no project at $PROJECT to read a version from"
  prefix="$(dotnet msbuild "$PROJECT" -getProperty:VersionPrefix -nologo | tr -d '[:space:]')"
fi

# An empty answer is refused rather than compared. D-0106: a check must rest on what came back, and
# an unset property reads as "" which would equal a "" base and pass on the emptiest possible input.
[ -n "$prefix" ] || die "could not read VersionPrefix; refusing without a verified answer"

case "$base" in
  '' | *[!0-9.]*) die "'$base' is not a version; the tag names one, it does not invent it" ;;
esac

[ "$base" = "$prefix" ] \
  || die "tag $TAG says $base but the source declares $prefix - fix VersionPrefix or the tag, not this"

echo "  ok  $TAG agrees with the source's $prefix"
