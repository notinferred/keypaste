#!/usr/bin/env bash
# Puts the pinned KeePassXC build on a Windows runner and prints the path of its keepassxc-cli.
#
# One owner for the version and the digest: ci.yml, release.yml and upgrade-desktop.yml all call this,
# so a third copy of the download cannot drift from the two the compat gate proves (D-0208).
#
# Download logs go to stderr; the path of keepassxc-cli.exe is the only line on stdout.
#
# Usage: install-keepassxc-windows.sh [dest-dir]   (default: the working directory)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT
readonly VERSION='2.7.12'
# An in-repo checksum remains independent of the download origin.
readonly SHA256='958234b0669d757b53eacf42bdd5de0fa1cc1ab7527709ddf4f7e29c06a8305f'

die() { echo "::error::$*" >&2; exit 1; }

[ $# -le 1 ] || die 'usage: install-keepassxc-windows.sh [dest-dir]'
dest="${1:-$PWD}"
case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) ;; *) die 'this installs the Windows build of KeePassXC' ;; esac

zip="KeePassXC-$VERSION-Win64.zip"
mkdir -p "$dest"
"$ROOT/scripts/fetch-pinned-asset.sh" \
  "https://github.com/keepassxreboot/keepassxc/releases/download/$VERSION/$zip" "$SHA256" "$dest/$zip" >&2

# Git Bash's GNU tar cannot read ZIP files; Windows supplies bsdtar.
bsdtar='/c/Windows/System32/tar.exe'
[ -x "$bsdtar" ] || die "$bsdtar not found"
"$bsdtar" -xf "$dest/$zip" -C "$dest" >&2

# Printed in POSIX form, whatever form the destination came in: every caller runs it from bash.
cli="$(cygpath -u "$dest/KeePassXC-$VERSION-Win64/keepassxc-cli.exe")"
[ -x "$cli" ] || die "the pinned archive holds no keepassxc-cli.exe at $cli"
echo "$cli"
