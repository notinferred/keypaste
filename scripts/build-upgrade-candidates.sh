#!/usr/bin/env bash
# Builds, from this commit, one unpublished desktop candidate per numeric version, for installing over one
# another (4.7d, D-0147). Nothing here is signed, attested or published.
#
# Each candidate carries the `upgrade` prerelease suffix, so no real release can be confused with it, and its
# binary must report the version it was asked for before the package is built.
#
# Build logs go to stderr; the paths of the packages are the lines on stdout, in the order asked for.
#
# Usage: build-upgrade-candidates.sh <rid> <out-dir> <numeric-version>...
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT
readonly SUFFIX='upgrade'

die() { echo "::error::$*" >&2; exit 1; }

[ $# -ge 3 ] || die 'usage: build-upgrade-candidates.sh <rid> <out-dir> <numeric-version>...'
rid="$1" out="$2"
shift 2
case "$rid" in win-x64 | linux-x64) ;; *) die "$rid has no desktop package to upgrade" ;; esac

mkdir -p "$out"
for prefix in "$@"; do
  case "$prefix" in
    [0-9]*.[0-9]*.[0-9]*) ;;
    *) die "$prefix is not a numeric version an MSI ProductVersion can carry" ;;
  esac
  version="$prefix-$SUFFIX"
  published="$ROOT/artifacts/app/$rid"
  stage="$ROOT/artifacts/stage"
  rm -rf "$published" "$stage"
  dotnet publish "$ROOT/src/Keypaste.App/Keypaste.App.csproj" -c Release -r "$rid" --self-contained \
    --no-restore -o "$published" -p:VersionPrefix="$prefix" -p:VersionSuffix="$SUFFIX" >&2

  binary="$published/keypaste-app"
  [ "$rid" = win-x64 ] && binary="$published/keypaste-app.exe"
  "$binary" --selftest >&2
  reported="$("$binary" --version | tr -d '[:space:]')"
  [ "$reported" = "$version" ] || die "the binary reports $reported, not $version"

  mkdir -p "$stage"
  cp -R "$published"/. "$stage"/
  rm -rf "$stage"/*.pdb "$stage"/*.dSYM
  cp "$ROOT/LICENSE" "$ROOT/THIRD_PARTY_NOTICES.md" "$stage"/
  if [ "$rid" = win-x64 ]; then
    "$ROOT/scripts/build-windows-installer.sh" "$stage" "$version" "$reported" "$out"
  else
    image="$("$ROOT/scripts/build-linux-appimage.sh" "$stage" "$version" "$out")"
    "$ROOT/scripts/verify-linux-appimage.sh" "$image" "$version" "$reported" "$stage" >&2
    echo "$image"
  fi
done
