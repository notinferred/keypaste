#!/usr/bin/env bash
# Wraps a staged osx-arm64 desktop payload in the internal keypaste.app bundle and zips it as keypaste.app.zip,
# the build intermediate build-macos-dmg.sh wraps in the DMG release-targets.json declares. The zip is no release
# package, so nothing uploads or attests it (D-0513).
#
# The payload goes to Contents/MacOS, where the apphost finds its runtime and the app finds the keypaste it
# gives clients; the icon, licences and Terminal's launcher go to Contents/Resources. Nothing is signed or
# notarized while the app's signing policy is none. ditto zips the bundle, keeping its modes in a format notarytool
# also accepts, and the bundle is then checked as unzipped from that archive, including a --selftest started
# through LaunchServices.
#
# Build logs go to stderr; the path of the archive is the only line on stdout.
#
# Usage: build-macos-app.sh <staged-payload-dir> <version> <binary-version> <out-dir>
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT
readonly NAME='keypaste.app.zip'

die() { echo "::error::$*" >&2; exit 1; }

[ $# -eq 4 ] || die "usage: build-macos-app.sh <staged-payload-dir> <version> <binary-version> <out-dir>"
payload="$1" version="$2" binary_version="$3" out="$4"
[ "$(uname -s)" = Darwin ] || die "an app bundle is built and checked on macOS"
for file in keypaste-app keypaste LICENSE THIRD_PARTY_NOTICES.md; do
  [ -f "$payload/$file" ] || die "no $file in $payload"
done

# CFBundleVersion and CFBundleShortVersionString take period-separated integers only.
bundle_version="${version%%-*}"
case "$bundle_version" in
  '' | *[!0-9.]* | .* | *. | *..*) die "$version does not start with a numeric bundle version" ;;
esac

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

app="$work/keypaste.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$payload"/. "$app/Contents/MacOS/"
mv "$app/Contents/MacOS/LICENSE" "$app/Contents/MacOS/THIRD_PARTY_NOTICES.md" "$app/Contents/Resources/"
cp "$ROOT/packaging/macos/keypaste.icns" "$ROOT/packaging/macos/keypaste-launch.command" "$app/Contents/Resources/"
sed "s/{version}/$bundle_version/g" "$ROOT/packaging/macos/Info.plist" > "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist" >&2
# The runtime pack's files arrive 0744, and only a Mach-O executable and the launcher Terminal runs need an exec bit.
find "$app" -type d -exec chmod 755 {} +
find "$app" -type f -exec chmod 644 {} +
while IFS= read -r -d '' path; do
  case "$(file -b "$path")" in *Mach-O*executable*) chmod 755 "$path" ;; esac
done < <(find "$app/Contents/MacOS" -type f -print0)
chmod 755 "$app/Contents/Resources/keypaste-launch.command"

mkdir -p "$out"
rm -f "$out/$NAME"
ditto -c -k --norsrc --noextattr --noacl --keepParent "$app" "$out/$NAME"

unzipped="$work/unzipped"
ditto -x -k "$out/$NAME" "$unzipped"
contents="$unzipped/keypaste.app/Contents"
plist() { plutil -extract "$1" raw -o - "$contents/Info.plist"; }
executable="$contents/MacOS/$(plist CFBundleExecutable)"
[ -x "$executable" ] || die "$NAME names CFBundleExecutable $(plist CFBundleExecutable), which is not executable in Contents/MacOS"
[ -x "$contents/MacOS/keypaste" ] || die "$NAME carries no executable keypaste"
[ -x "$contents/Resources/keypaste-launch.command" ] || die "$NAME carries no executable Contents/Resources/keypaste-launch.command"
cmp -s "$ROOT/packaging/macos/keypaste-launch.command" "$contents/Resources/keypaste-launch.command" \
  || die "$NAME's keypaste-launch.command differs from packaging/macos's"
[ -f "$contents/Resources/$(plist CFBundleIconFile).icns" ] || die "$NAME names icon $(plist CFBundleIconFile) and carries no such .icns"

reported="$("$executable" --version | tr -d '[:space:]')"
[ "$reported" = "$binary_version" ] || die "the bundle's binary reports $reported, not $binary_version"
case "$("$contents/MacOS/keypaste" mcp --help 2>&1)" in "usage: keypaste mcp"*) ;; *) die "the bundle's keypaste does not answer mcp --help" ;; esac

# LaunchServices, not a shell, reads Info.plist and starts the app from Finder or `open`; its exit code is not open's.
: > "$work/launched"
open -W -n -g --stdout "$work/launched" --stderr "$work/launched.err" "$unzipped/keypaste.app" --args --selftest
grep -q '^keypaste-app: selftest ok ' "$work/launched" \
  || die "the bundle started through LaunchServices did not pass --selftest: $(cat "$work/launched" "$work/launched.err")"
cat "$work/launched" >&2

echo "$out/$NAME"
