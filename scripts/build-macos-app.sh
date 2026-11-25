#!/usr/bin/env bash
# Wraps a staged osx-arm64 desktop payload in the keypaste.app bundle and zips it as keypaste.app.zip,
# the build intermediate build-macos-dmg.sh wraps in the DMG release-targets.json declares. The zip is no release
# package: app.yml uploads it only for notarize.yml to notarize, and nothing attests it.
#
# The payload goes to Contents/MacOS, which must hold Mach-O code alone: the single-file app, the keypaste it gives
# clients and the libraries they load. The icon, licences and Terminal's launcher go to Contents/Resources.
# sign-macos.sh signs the bundle under the hardened runtime with KEYPASTE_MACOS_IDENTITY, ad hoc by default, and the
# notarize.yml notarizes the zip. ditto zips the bundle, keeping its modes in a format notarytool also accepts, and the bundle is then
# checked as unzipped from that archive: its seal holds and refuses a changed byte, and a --selftest started through
# LaunchServices, like the keypaste's doctor, must derive with the libargon2.1.dylib beside it.
#
# Build logs go to stderr; the path of the archive is the only line on stdout.
#
# Usage: build-macos-app.sh <staged-payload-dir> <version> <binary-version> <out-dir>
# Environment: KEYPASTE_MACOS_IDENTITY  the codesign identity (default: -, ad hoc)
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
# The copyright the assemblies carry, from Directory.Build.props, so About keypaste and Finder say the same.
copyright=$(sed -n 's:.*<Copyright>\(.*\)</Copyright>.*:\1:p' "$ROOT/Directory.Build.props")
[ -n "$copyright" ] || die 'Directory.Build.props names no Copyright'
sed -e "s/{version}/$bundle_version/g" -e "s/{copyright}/$copyright/g" "$ROOT/packaging/macos/Info.plist" > "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist" >&2
# The runtime pack's files arrive 0744, and only a Mach-O executable and the launcher Terminal runs need an exec bit.
find "$app" -type d -exec chmod 755 {} +
find "$app" -type f -exec chmod 644 {} +
while IFS= read -r -d '' path; do
  case "$(file -b "$path")" in
    *Mach-O*executable*) chmod 755 "$path" ;;
    *Mach-O*) ;;
    *) die "$(basename "$path") is no Mach-O code, which codesign cannot seal in Contents/MacOS" ;;
  esac
done < <(find "$app/Contents/MacOS" -type f -print0)
chmod 755 "$app/Contents/Resources/keypaste-launch.command"
"$ROOT/scripts/sign-macos.sh" "$app" "${KEYPASTE_MACOS_IDENTITY:--}"

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
# Alternate, so taking .kdbx never takes the default from KeePassXC.
[ "$(plist CFBundleDocumentTypes.0.LSHandlerRank)" = Alternate ] || die "$NAME declares .kdbx at rank $(plist CFBundleDocumentTypes.0.LSHandlerRank), not Alternate"
[ "$(plist CFBundleDocumentTypes.0.CFBundleTypeExtensions.0)" = kdbx ] || die "$NAME declares no .kdbx document type"

codesign --verify --deep --strict "$unzipped/keypaste.app" >&2 || die "$NAME's signature does not verify"
while IFS= read -r -d '' path; do
  case "$(codesign -dv "$path" 2>&1)" in *'flags=0x'*'runtime'*) ;; *) die "$(basename "$path") is not signed under the hardened runtime" ;; esac
done < <(find "$contents/MacOS" -type f -print0)
ditto "$unzipped/keypaste.app" "$work/changed.app"
changed="$work/changed.app/Contents/MacOS/libargon2.1.dylib"
byte="$(xxd -s 4096 -l 1 -p "$changed")"
printf '%02x' $((0x$byte ^ 1)) | xxd -r -p | dd of="$changed" bs=1 seek=4096 conv=notrunc 2> /dev/null
! codesign --verify --deep --strict "$work/changed.app" 2> /dev/null || die "$NAME's signature accepts a changed libargon2.1.dylib"

reported="$("$executable" --version | tr -d '[:space:]')"
[ "$reported" = "$binary_version" ] || die "the bundle's binary reports $reported, not $binary_version"
case "$("$contents/MacOS/keypaste" mcp --help 2>&1)" in "the MCP server your AI tools start"*) ;; *) die "the bundle's keypaste does not answer mcp --help" ;; esac
where="$(KEYPASTE_HOME="$work/home" "$contents/MacOS/keypaste" doctor --json | jq -r '.[] | select(.area == "argon2" and .status == "ok") | .where')" || true
case "$where" in */keypaste.app/Contents/MacOS/libargon2.1.dylib) ;; *) die "the bundle's keypaste did not derive with the libargon2.1.dylib beside it" ;; esac

# LaunchServices, not a shell, reads Info.plist and starts the app from Finder or `open`; its exit code is not open's.
: > "$work/launched"
# When the selftest ends before open starts watching it, open -W reports that and fails; what the app printed still decides.
if ! open -W -n -g --stdout "$work/launched" --stderr "$work/launched.err" "$unzipped/keypaste.app" --args --selftest 2> "$work/open.err"; then
  grep -q 'kevent() failed: No such process' "$work/open.err" || die "open could not start the bundle through LaunchServices: $(cat "$work/open.err")"
fi
grep -q '^keypaste-app: selftest ok ' "$work/launched" \
  || die "the bundle started through LaunchServices did not pass --selftest: $(cat "$work/launched" "$work/launched.err")"
grep -q '^keypaste-app: Argon2 derived natively by .*/keypaste\.app/Contents/MacOS/libargon2\.1\.dylib$' "$work/launched" \
  || die "the bundle did not derive with the libargon2.1.dylib in its Contents/MacOS: $(cat "$work/launched")"
grep -Eq '^keypaste-app: notifications (not asked|denied|allowed)$' "$work/launched" \
  || die "the bundle did not reach the notification center: $(cat "$work/launched")"
grep -q '^keypaste-app: notifications allowed$' "$work/launched" \
  || grep -Eq '^keypaste-app: notification add (accepted|refused)$' "$work/launched" \
  || die "the bundle's add to the notification center was never answered: $(cat "$work/launched")"
cat "$work/launched" >&2

echo "$out/$NAME"
