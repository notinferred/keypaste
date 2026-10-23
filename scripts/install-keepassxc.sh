#!/usr/bin/env bash
# Puts the pinned KeePassXC build on a Windows or macOS runner and prints the path of its keepassxc-cli.
#
# One owner for the version and the digests: every workflow that needs KeePassXC on Windows or macOS calls this, so no
# copy of the download can drift from the one the compat gate proves (D-0208, D-0526). Linux runners keep Ubuntu's.
#
# Download logs go to stderr; the path of keepassxc-cli is the only line on stdout.
#
# Usage: install-keepassxc.sh [dest-dir]   (default: the working directory; on macOS the app goes to /Applications)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT
readonly VERSION='2.8.0'
# In-repo checksums remain independent of the download origin.
readonly WINDOWS_SHA256='48ff4f088e9ce70e241da1270e5081528dba77c38b17dae4aa6690ffc50fc828'
readonly MACOS_SHA256='223d5c71ae6c11d48192c69fd582fd5f10047e91ebcebc014de33a42539b9519'
readonly RELEASE="https://github.com/keepassxreboot/keepassxc/releases/download/$VERSION"

die() { echo "::error::$*" >&2; exit 1; }

[ $# -le 1 ] || die 'usage: install-keepassxc.sh [dest-dir]'
dest="${1:-$PWD}"
mkdir -p "$dest"

case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*)
    zip="KeePassXC-$VERSION-x64.zip"
    "$ROOT/scripts/fetch-pinned-asset.sh" "$RELEASE/$zip" "$WINDOWS_SHA256" "$dest/$zip" >&2
    # Git Bash's GNU tar cannot read ZIP files; Windows supplies bsdtar.
    bsdtar='/c/Windows/System32/tar.exe'
    [ -x "$bsdtar" ] || die "$bsdtar not found"
    "$bsdtar" -xf "$dest/$zip" -C "$dest" >&2
    # Printed in POSIX form, whatever form the destination came in: every caller runs it from bash.
    cli="$(cygpath -u "$dest/KeePassXC-$VERSION-x64/keepassxc-cli.exe")"
    ;;
  Darwin)
    [ "$(uname -m)" = arm64 ] || die 'the pinned macOS build is for Apple silicon'
    dmg="KeePassXC-$VERSION-arm64.dmg"
    "$ROOT/scripts/fetch-pinned-asset.sh" "$RELEASE/$dmg" "$MACOS_SHA256" "$dest/$dmg" >&2
    mount="$dest/keepassxc-dmg"
    hdiutil attach -quiet -nobrowse -readonly -mountpoint "$mount" "$dest/$dmg" >&2
    rm -rf /Applications/KeePassXC.app
    ditto "$mount/KeePassXC.app" /Applications/KeePassXC.app
    hdiutil detach -quiet "$mount" >&2
    cli=/Applications/KeePassXC.app/Contents/MacOS/keepassxc-cli
    ;;
  *) die 'this installs the Windows or macOS build of KeePassXC' ;;
esac

[ -x "$cli" ] || die "the pinned build holds no keepassxc-cli at $cli"
echo "$cli"
