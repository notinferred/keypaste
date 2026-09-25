#!/usr/bin/env bash
# Wraps a staged osx-arm64 desktop payload in the internal keypaste.app bundle release-targets.json declares.
#
# The payload goes to Contents/MacOS, where the apphost finds its runtime and the app finds the keypaste-mcp it
# gives clients; the icon and licences go to Contents/Resources. Nothing is signed or notarized while the app's
# signing policy is none. ditto zips the bundle, keeping its modes in a format notarytool also accepts, and the
# bundle is then checked as unzipped from that archive, including a launch through LaunchServices.
#
# Build logs go to stderr; the path of the archive is the only line on stdout.
#
# Usage: build-macos-app.sh <staged-payload-dir> <version> <binary-version> <out-dir>
# Environment: KEYPASTE_RELEASE_DEFINITION  the definition to read (default: release-targets.json)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT
readonly DEFINITION="${KEYPASTE_RELEASE_DEFINITION:-$ROOT/release-targets.json}"
readonly RID='osx-arm64'

die() { echo "::error::$*" >&2; exit 1; }
jqr() { command jq -r "$@" | tr -d '\r'; }

[ $# -eq 4 ] || die "usage: build-macos-app.sh <staged-payload-dir> <version> <binary-version> <out-dir>"
payload="$1" version="$2" binary_version="$3" out="$4"
[ "$(uname -s)" = Darwin ] || die "an app bundle is built and checked on macOS"
for file in keypaste-app keypaste-mcp LICENSE THIRD_PARTY_NOTICES.md; do
  [ -f "$payload/$file" ] || die "no $file in $payload"
done

# CFBundleVersion and CFBundleShortVersionString take period-separated integers only.
bundle_version="${version%%-*}"
case "$bundle_version" in
  '' | *[!0-9.]* | .* | *. | *..*) die "$version does not start with a numeric bundle version" ;;
esac

package='.components.app.targets[] | select(.rid == "osx-arm64") | .packages[] | select(.kind == "app-bundle")'
[ "$(jqr "[$package] | length" "$DEFINITION" 2>/dev/null || true)" = "1" ] || die "$DEFINITION does not declare exactly one app-bundle package for app/$RID"
name="$(jqr "$package | .pattern" "$DEFINITION" | sed -e "s/{version}/$version/g" -e "s/{rid}/$RID/g")"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

app="$work/keypaste.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$payload"/. "$app/Contents/MacOS/"
mv "$app/Contents/MacOS/LICENSE" "$app/Contents/MacOS/THIRD_PARTY_NOTICES.md" "$app/Contents/Resources/"
cp "$ROOT/packaging/macos/keypaste.icns" "$app/Contents/Resources/"
sed "s/{version}/$bundle_version/g" "$ROOT/packaging/macos/Info.plist" > "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist" >&2
chmod -R go-w "$app"
chmod 755 "$app/Contents/MacOS/keypaste-app" "$app/Contents/MacOS/keypaste-mcp"

mkdir -p "$out"
rm -f "$out/$name"
ditto -c -k --norsrc --noextattr --noacl --keepParent "$app" "$out/$name"

unzipped="$work/unzipped"
ditto -x -k "$out/$name" "$unzipped"
contents="$unzipped/keypaste.app/Contents"
plist() { plutil -extract "$1" raw -o - "$contents/Info.plist"; }
executable="$contents/MacOS/$(plist CFBundleExecutable)"
[ -x "$executable" ] || die "$name names CFBundleExecutable $(plist CFBundleExecutable), which is not executable in Contents/MacOS"
[ -x "$contents/MacOS/keypaste-mcp" ] || die "$name carries no executable keypaste-mcp"
[ -f "$contents/Resources/$(plist CFBundleIconFile).icns" ] || die "$name names icon $(plist CFBundleIconFile) and carries no such .icns"

reported="$("$executable" --version | tr -d '[:space:]')"
[ "$reported" = "$binary_version" ] || die "the bundle's binary reports $reported, not $binary_version"
"$executable" --selftest >&2
case "$("$contents/MacOS/keypaste-mcp" --help 2>&1)" in "usage: keypaste-mcp"*) ;; *) die "the bundle's keypaste-mcp does not answer --help" ;; esac

# LaunchServices, not a shell, is what reads Info.plist to start an app from Finder or `open`.
: > "$work/launched"
open -W -n -g --stdout "$work/launched" --stderr "$work/launched.err" "$unzipped/keypaste.app" --args --version
launched="$(tr -d '[:space:]' < "$work/launched")"
[ "$launched" = "$binary_version" ] || die "LaunchServices started the bundle and it reported '$launched', not $binary_version: $(cat "$work/launched.err")"

echo "$out/$name"
