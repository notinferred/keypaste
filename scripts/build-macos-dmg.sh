#!/usr/bin/env bash
# Wraps the keypaste.app that build-macos-app.sh zipped and checked in the internal disk image release-targets.json
# declares: the bundle beside a link to /Applications, built by hdiutil alone and left unsigned while the app's
# signing policy is none. The image is then attached read-only and checked as a person would open it: its entries,
# the bundle's bytes and modes against the zip's, the plist versions, the binary's version, the bridge, and a
# --selftest started through LaunchServices from the image, which is detached afterwards.
#
# Build logs go to stderr; the path of the image is the only line on stdout.
#
# Usage: build-macos-dmg.sh <app-zip> <version> <binary-version> <out-dir>
# Environment: KEYPASTE_RELEASE_DEFINITION  the definition to read (default: release-targets.json)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT
readonly DEFINITION="${KEYPASTE_RELEASE_DEFINITION:-$ROOT/release-targets.json}"
readonly RID='osx-arm64'

die() { echo "::error::$*" >&2; exit 1; }
jqr() { command jq -r "$@" | tr -d '\r'; }

[ $# -eq 4 ] || die "usage: build-macos-dmg.sh <app-zip> <version> <binary-version> <out-dir>"
zip="$1" version="$2" binary_version="$3" out="$4"
[ "$(uname -s)" = Darwin ] || die "a disk image is built and checked on macOS"
[ -f "$zip" ] || die "no app bundle archive at $zip"

# The numeric prefix build-macos-app.sh writes into Info.plist.
bundle_version="${version%%-*}"

package='.components.app.targets[] | select(.rid == "osx-arm64") | .packages[] | select(.kind == "dmg")'
[ "$(jqr "[$package] | length" "$DEFINITION" 2>/dev/null || true)" = "1" ] || die "$DEFINITION does not declare exactly one dmg package for app/$RID"
name="$(jqr "$package | .pattern" "$DEFINITION" | sed -e "s/{version}/$version/g" -e "s/{rid}/$RID/g")"

# Resolved, because mount(8) reports /var/folders under /private.
work="$(cd "$(mktemp -d)" && pwd -P)"
volume="$work/volume"
attached() { mount | grep -qF " on $volume ("; }
cleanup() {
  if attached; then hdiutil detach "$volume" -force >&2 || true; fi
  attached || rm -rf "$work"
}
trap cleanup EXIT
entries() { (cd "$1" && find . -mindepth 1 -maxdepth 1 | sed 's|^\./||' | LC_ALL=C sort | tr '\n' ' '); }

stage="$work/stage"
mkdir -p "$stage"
ditto -x -k "$zip" "$stage"
[ "$(entries "$stage")" = "keypaste.app " ] || die "$zip holds $(entries "$stage")rather than keypaste.app alone"
ln -s /Applications "$stage/Applications"

mkdir -p "$out"
rm -f "$out/$name"
hdiutil create -volname keypaste -srcfolder "$stage" -fs HFS+ -format UDZO "$out/$name" >&2
hdiutil verify "$out/$name" >&2

mkdir -p "$volume"
hdiutil attach "$out/$name" -readonly -nobrowse -noautoopen -mountpoint "$volume" >&2
attached || die "$name did not attach at $volume"
[ "$(entries "$volume")" = "Applications keypaste.app " ] || die "$name holds $(entries "$volume")rather than keypaste.app beside Applications"
[ -L "$volume/Applications" ] && [ "$(readlink "$volume/Applications")" = /Applications ] \
  || die "$name's Applications is not a link to /Applications"

diff -r "$stage/keypaste.app" "$volume/keypaste.app" >&2 || die "the keypaste.app in $name differs from the one in $(basename "$zip")"
modes() { (cd "$1" && find . -print0 | xargs -0 stat -f '%Lp %N' | LC_ALL=C sort); }
[ "$(modes "$stage/keypaste.app")" = "$(modes "$volume/keypaste.app")" ] \
  || die "the keypaste.app in $name carries other file modes than the one in $(basename "$zip")"

contents="$volume/keypaste.app/Contents"
plist() { plutil -extract "$1" raw -o - "$contents/Info.plist"; }
for key in CFBundleShortVersionString CFBundleVersion; do
  [ "$(plist "$key")" = "$bundle_version" ] || die "$name's Info.plist says $key $(plist "$key"), not $bundle_version"
done
executable="$contents/MacOS/$(plist CFBundleExecutable)"
[ -x "$executable" ] || die "$name's bundle names CFBundleExecutable $(plist CFBundleExecutable), which is not executable"
reported="$("$executable" --version | tr -d '[:space:]')"
[ "$reported" = "$binary_version" ] || die "the binary in $name reports $reported, not $binary_version"
[ -x "$contents/MacOS/keypaste" ] || die "$name carries no executable keypaste"
case "$("$contents/MacOS/keypaste" mcp --help 2>&1)" in "usage: keypaste mcp"*) ;; *) die "the keypaste in $name does not answer mcp --help" ;; esac

: > "$work/launched"
open -W -n -g --stdout "$work/launched" --stderr "$work/launched.err" "$volume/keypaste.app" --args --selftest
grep -q '^keypaste-app: selftest ok ' "$work/launched" \
  || die "the bundle started from $name through LaunchServices did not pass --selftest: $(cat "$work/launched" "$work/launched.err")"
cat "$work/launched" >&2

hdiutil detach "$volume" >&2 || die "$name did not detach from $volume after its checks"
! attached || die "$name is still attached at $volume after its checks"

echo "$out/$name"
