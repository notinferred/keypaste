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

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=scripts/lib/terminal.sh
. "$ROOT/scripts/lib/terminal.sh"

die() { echo "::error::$*" >&2; exit 1; }

[ $# -eq 2 ] || die "usage: verify-macos-cli-link.sh <app-zip> <binary-version>"
zip="$1" binary_version="$2"
[ "$(uname -s)" = Darwin ] || die "the macOS link is checked on macOS"
[ -f "$zip" ] || die "no app bundle archive at $zip"

work="$(cd "$(mktemp -d)" && pwd -P)"
linked=false
cleanup() {
  if [ "$linked" = true ]; then sudo -n /bin/rm -f "$MAC_CLI_LINK" || true; fi
  rm -rf "$work"
}
trap cleanup EXIT

ditto -x -k "$zip" "$work"
target="$work/keypaste.app/Contents/MacOS/keypaste"
[ -x "$target" ] || die "$(basename "$zip") carries no executable Contents/MacOS/keypaste"

made="$(mac_cli_link "$target")" || die "$made"
linked=true
said="$(terminal_keypaste macos)" || die "a new login shell failed: $said"
why="$(terminal_mismatch macos "$said" "$MAC_CLI_LINK" "$binary_version")"
[ -z "$why" ] || die "$why"

echo "$MAC_CLI_LINK -> $target: a new terminal runs keypaste $binary_version from it, and its bridge answers."
