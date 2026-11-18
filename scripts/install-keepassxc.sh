#!/usr/bin/env bash
# Puts the pinned KeePassXC build on a Windows, macOS or Linux runner and prints the path of its keepassxc-cli.
#
# One owner for the version and the digests: every workflow that needs KeePassXC calls this, so no copy of the download
# can drift from the one the compat gate proves.
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
readonly MACOS_SHA256='85f0e75e258ef513f0fc7f5bf470cad0943b7d7e8191b2171f9898ad32e62b9c'
readonly LINUX_X86_64_SHA256='3ab1bfbb422272334b48057e6bfc984c839375d9a1cd9350c4ea62ca0b55ca5f'
readonly LINUX_AARCH64_SHA256='ead773e5b2903e23695157c0c955fe294761b1bd00b78c697b67727fd13e61cc'
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
    arch="$(uname -m)"
    case "$arch" in
      x86_64) sha256="$LINUX_X86_64_SHA256" ;;
      aarch64) sha256="$LINUX_AARCH64_SHA256" ;;
      *) die "no pinned Linux build for $arch" ;;
    esac
    # Upstream's rebuild, as on macOS.
    appimage="KeePassXC-$VERSION-1-$arch.AppImage"
    "$ROOT/scripts/fetch-pinned-asset.sh" "$RELEASE/$appimage" "$sha256" "$dest/$appimage" >&2
    chmod +x "$dest/$appimage"
    # Extracted rather than mounted, because hosted runners may lack FUSE.
    rm -rf "$dest/squashfs-root"
    (cd "$dest" && "./$appimage" --appimage-extract >/dev/null)
    cli="$dest/squashfs-root/usr/bin/keepassxc-cli"
    # Libraries the AppImage expects the host to provide.
    "$ROOT/scripts/apt-install.sh" --no-install-recommends \
      libegl1 libgl1 libopengl0 libfontconfig1 libfreetype6 libxkbcommon0 libdbus-1-3 libx11-6 libx11-xcb1 libxcb1 >&2
    ;;
  *) die 'this installs the Windows, macOS or Linux build of KeePassXC' ;;
esac

[ -x "$cli" ] || die "the pinned build holds no keepassxc-cli at $cli"
reported="$("$cli" --version 2>&1 | tr -d '\r')" || true
case "$reported" in
  "$VERSION"* | "keepassxc-cli $VERSION"*) ;;
  *)
    printf '%s\n' "$reported" >&2
    if [ "$(uname -s)" = Linux ]; then ldd "$cli" | grep 'not found' >&2 || true; fi
    die "$cli does not run as KeePassXC $VERSION"
    ;;
esac
echo "$cli"
