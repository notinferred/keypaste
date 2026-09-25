#!/usr/bin/env bash
# Wraps a staged linux-x64 desktop payload in the internal AppImage release-targets.json declares (4.7a3).
#
# appimagetool and the type2 runtime are both fetched against the definition's SHA-256 pins, and the
# runtime is always passed with --runtime-file: without it appimagetool downloads the unpinned
# `continuous` runtime (D-0203). The tool is itself an AppImage, so it is extracted and its AppRun
# started, which needs no FUSE; the package it builds is never run here.
#
# Build logs go to stderr; the path of the AppImage is the only line on stdout.
#
# Usage: build-linux-appimage.sh <staged-payload-dir> <version> <out-dir>
# Environment: KEYPASTE_RELEASE_DEFINITION  the definition to read (default: release-targets.json)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT
readonly DEFINITION="${KEYPASTE_RELEASE_DEFINITION:-$ROOT/release-targets.json}"
readonly RID='linux-x64'
readonly ID='com.keypaste.app'

die() { echo "::error::$*" >&2; exit 1; }
jqr() { command jq -r "$@" | tr -d '\r'; }

[ $# -eq 3 ] || die "usage: build-linux-appimage.sh <staged-payload-dir> <version> <out-dir>"
payload="$1" version="$2" out="$3"
[ -f "$payload/keypaste-app" ] || die "no keypaste-app in $payload"
for required in desktop-file-validate file; do
  command -v "$required" >/dev/null 2>&1 || die "appimagetool refuses to run without $required"
done

package='.components.app.targets[] | select(.rid == "linux-x64") | .packages[] | select(.kind == "appimage")'
[ "$(jqr "[$package] | length" "$DEFINITION" 2>/dev/null || true)" = "1" ] || die "$DEFINITION does not declare exactly one appimage package for app/$RID"
read_package() { jqr "$package | .$1 // empty" "$DEFINITION"; }

name="$(read_package pattern | sed -e "s/{version}/$version/g" -e "s/{rid}/$RID/g")"
tool_url="https://github.com/AppImage/appimagetool/releases/download/$(read_package tool_version)/appimagetool-x86_64.AppImage"
runtime_url="https://github.com/AppImage/type2-runtime/releases/download/$(read_package runtime_version)/runtime-x86_64"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
"$ROOT/scripts/fetch-pinned-asset.sh" "$tool_url" "$(read_package tool_sha256)" "$work/appimagetool.AppImage" >&2
"$ROOT/scripts/fetch-pinned-asset.sh" "$runtime_url" "$(read_package runtime_sha256)" "$work/runtime" >&2
chmod +x "$work/appimagetool.AppImage"
(cd "$work" && ./appimagetool.AppImage --appimage-extract >/dev/null)

appdir="$work/AppDir"
icons="$appdir/usr/share/icons/hicolor"
mkdir -p "$appdir/usr/bin" "$appdir/usr/share/metainfo" "$appdir/usr/share/applications" \
  "$icons/scalable/apps" "$icons/256x256/apps"
cp -R "$payload"/. "$appdir/usr/bin/"
install -m 755 "$ROOT/packaging/linux/AppRun" "$appdir/AppRun"
cp "$ROOT/packaging/linux/$ID.svg" "$appdir/"
cp "$ROOT/packaging/linux/$ID.svg" "$icons/scalable/apps/"
cp "$ROOT/src/Keypaste.App/Assets/keypaste-256.png" "$icons/256x256/apps/$ID.png"
# Thumbnailers read .DirIcon as a PNG; left absent, appimagetool links it to the SVG.
cp "$icons/256x256/apps/$ID.png" "$appdir/.DirIcon"
sed "s/{version}/$version/g" "$ROOT/packaging/linux/$ID.desktop" > "$appdir/$ID.desktop"
cp "$appdir/$ID.desktop" "$appdir/usr/share/applications/"
sed "s/{version}/$version/g" "$ROOT/packaging/linux/$ID.appdata.xml" > "$appdir/usr/share/metainfo/$ID.appdata.xml"

# appimagetool validates AppStream only through appstreamcli, so its absence is not a pass.
appstream=()
command -v appstreamcli >/dev/null 2>&1 || appstream=(--no-appstream)

mkdir -p "$out"
ARCH=x86_64 VERSION="$version" "$work/squashfs-root/AppRun" "${appstream[@]}" \
  --runtime-file "$work/runtime" "$appdir" "$out/$name" >&2
[ -f "$out/$name" ] || die "appimagetool produced no $out/$name"
echo "$out/$name"
