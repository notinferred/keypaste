#!/usr/bin/env bash
# Puts the pinned KeePassXC on a runner and prints the path of its keepassxc-cli, once it reports the pinned version.
#
# One owner for the versions and the digests: every workflow that needs KeePassXC calls this, so no copy can drift from
# the one the compat gate proves. Windows and macOS take upstream's 2.8.0; Linux takes Ubuntu's own package at an exact
# version, because upstream's AppImage needs glibc 2.38, which ubuntu-22.04 lacks.
#
# Install logs go to stderr; the path of keepassxc-cli is the only line on stdout.
#
# Usage: install-keepassxc.sh [dest-dir]   (default: the working directory; on macOS the app goes to /Applications,
#        on Linux apt installs it)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT
readonly VERSION='2.8.0'
# In-repo checksums remain independent of the download origin.
readonly WINDOWS_SHA256='48ff4f088e9ce70e241da1270e5081528dba77c38b17dae4aa6690ffc50fc828'
readonly MACOS_SHA256='85f0e75e258ef513f0fc7f5bf470cad0943b7d7e8191b2171f9898ad32e62b9c'
readonly RELEASE="https://github.com/keepassxreboot/keepassxc/releases/download/$VERSION"
# Ubuntu's archive per release; 22.04 offers no 2.7, so the 2.7 of law 14 is tested on 24.04.
readonly UBUNTU_2204_PACKAGE='2.6.6+dfsg.1-1'
readonly UBUNTU_2404_PACKAGE='2.7.6+dfsg.1-1build3'

die() { echo "::error::$*" >&2; exit 1; }

[ $# -le 1 ] || die 'usage: install-keepassxc.sh [dest-dir]'
dest="${1:-$PWD}"
mkdir -p "$dest"
expected=$VERSION

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
    # Upstream's rebuild: the first 2.8.0 image's Qt traps on its wait cursor on macOS 15.8.2 (keepassxc#13822).
    dmg="KeePassXC-$VERSION-1-arm64.dmg"
    "$ROOT/scripts/fetch-pinned-asset.sh" "$RELEASE/$dmg" "$MACOS_SHA256" "$dest/$dmg" >&2
    mount="$dest/keepassxc-dmg"
    hdiutil attach -quiet -nobrowse -readonly -mountpoint "$mount" "$dest/$dmg" >&2
    rm -rf /Applications/KeePassXC.app
    ditto "$mount/KeePassXC.app" /Applications/KeePassXC.app
    hdiutil detach -quiet "$mount" >&2
    cli=/Applications/KeePassXC.app/Contents/MacOS/keepassxc-cli
    ;;
  Linux)
    # Read, not sourced: os-release sets VERSION, which this script holds read-only, even in a subshell.
    os_field() { sed -n "s/^$1=\"\{0,1\}\([^\"]*\)\"\{0,1\}\$/\1/p" /etc/os-release; }
    release="$(os_field ID)-$(os_field VERSION_ID)"
    case "$release" in
      ubuntu-22.04) package=$UBUNTU_2204_PACKAGE ;;
      ubuntu-24.04) package=$UBUNTU_2404_PACKAGE ;;
      *) die "no KeePassXC package is pinned for $release" ;;
    esac
    "$ROOT/scripts/apt-install.sh" --no-install-recommends "keepassxc=$package" >&2
    cli="$(command -v keepassxc-cli)" || die "keepassxc=$package installed no keepassxc-cli"
    expected=${package%%+*}
    ;;
  *) die 'this installs KeePassXC on Windows, macOS or Ubuntu' ;;
esac

[ -x "$cli" ] || die "the pinned build holds no keepassxc-cli at $cli"
# macOS answers "KeePassXC 2.8.0", Linux "keepassxc-cli 2.7.6", and some builds the bare version.
reported="$("$cli" --version | tr -d '\r' | sed -n 1p)"
case "$reported" in
  "$expected" | "KeePassXC $expected" | "keepassxc-cli $expected") ;;
  *) die "keepassxc-cli at $cli reports '$reported', not the pinned $expected" ;;
esac
echo "$cli"
