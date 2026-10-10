#!/usr/bin/env bash
# Links the keypaste a built keypaste.app carries at /usr/local/bin/keypaste, as Settings does, and holds a new
# terminal to running it from there (G.5).
#
# A runner has nobody to type an administrator's password, so the link is made with sudo exactly as the
# dialog's fixed program makes it; MacCliLinkTests cover that program and how the paths reach it. The shell is
# a login zsh started from an empty environment with a PATH that lacks /usr/local/bin, so it finds keypaste
# only as a new Terminal window does, through /etc/paths and the account's profile. The link is removed on exit.
#
# Usage: verify-macos-cli-link.sh <app-zip> <binary-version>
set -euo pipefail

readonly LINK=/usr/local/bin/keypaste

die() { echo "::error::$*" >&2; exit 1; }

[ $# -eq 2 ] || die "usage: verify-macos-cli-link.sh <app-zip> <binary-version>"
zip="$1" binary_version="$2"
[ "$(uname -s)" = Darwin ] || die "the macOS link is checked on macOS"
[ -f "$zip" ] || die "no app bundle archive at $zip"
if [ -e "$LINK" ] || [ -L "$LINK" ]; then
  die "$LINK already exists on this runner, so a terminal finding keypaste there would prove nothing"
fi

work="$(cd "$(mktemp -d)" && pwd -P)"
linked=false
cleanup() {
  if [ "$linked" = true ]; then sudo -n /bin/rm -f "$LINK" || true; fi
  rm -rf "$work"
}
trap cleanup EXIT

ditto -x -k "$zip" "$work"
target="$work/keypaste.app/Contents/MacOS/keypaste"
[ -x "$target" ] || die "$(basename "$zip") carries no executable Contents/MacOS/keypaste"

sudo -n /bin/mkdir -p "$(dirname "$LINK")"
linked=true
sudo -n /bin/ln -sfn "$target" "$LINK"
[ "$(readlink "$LINK")" = "$target" ] || die "$LINK names $(readlink "$LINK"), not $target"

user="$(id -un)"
# shellcheck disable=SC2016
shell="$(env -i HOME="$HOME" USER="$user" LOGNAME="$user" SHELL=/bin/zsh TERM=dumb PATH=/usr/bin:/bin:/usr/sbin:/sbin \
  /bin/zsh -l -c 'printf "found=%s\n" "$(command -v keypaste)"
    printf "version=%s\n" "$(keypaste --version 2>&1)"
    printf "usage=%s\n" "$(keypaste mcp --help 2>&1 | head -n 1)"
    printf "path=%s\n" "$PATH"')" \
  || die "a new login shell failed: $shell"
said() { sed -n "s/^$1=//p" <<< "$shell" | head -n 1; }

[ "$(said found)" = "$LINK" ] || die "a new terminal finds keypaste at '$(said found)', not $LINK; its PATH is $(said path)"
[ "$(said version | tr -d '[:space:]')" = "$binary_version" ] \
  || die "keypaste --version in a new terminal reports '$(said version)', expected $binary_version"
case "$(said usage)" in "usage: keypaste mcp"*) ;; *) die "keypaste mcp --help in a new terminal printed '$(said usage)'" ;; esac

echo "$LINK -> $target: a new terminal runs keypaste $binary_version from it, and its bridge answers."
