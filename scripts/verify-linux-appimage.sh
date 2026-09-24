#!/usr/bin/env bash
# Holds a built Linux AppImage to what release-targets.json declares about it (4.7a3, D-0142).
#
# What it refuses:
#   - a file name other than the declared appimage pattern expanded for this version;
#   - an image that is not a 64-bit little-endian ELF runtime followed by a squashfs;
#   - a desktop entry or AppStream record that drops the full version, the internal and unsigned
#     label or keypaste as developer, or an AppRun other than the repository's;
#   - a payload whose files differ from the staged payload, whose binary reports another version
#     or fails --selftest, or whose binaries do not publish as keypaste;
#   - an image whose AppRun does not start the bridge it carries for `mcp`, which is what a client
#     connected from the app is told to run (2.6a).
#
# The image itself is never executed. The squashfs starts where the runtime's ELF ends, at its section
# header table's end, and `unsquashfs -o` reads it from there. Installing it is 4.7b.
#
# <version> names the package; <binary-version> is what the payload reports. They differ only on a
# dispatch, where app.yml names the package <binary-version>-dryrun.
#
# Usage: verify-linux-appimage.sh <appimage> <version> <binary-version> <staged-payload-dir>
#        verify-linux-appimage.sh --selftest
# Environment: KEYPASTE_RELEASE_DEFINITION  the definition to read (default: release-targets.json)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT
readonly DEFINITION="${KEYPASTE_RELEASE_DEFINITION:-$ROOT/release-targets.json}"
readonly RID='linux-x64'
readonly ID='com.keypaste.app'

die() { echo "::error::$*" >&2; exit 1; }
jqr() { command jq -r "$@" | tr -d '\r'; }

field() { od -An -v --endian=little -t "u$3" -j "$2" -N "$3" "$1" | tr -d ' '; }

# e_shoff + e_shentsize * e_shnum, from an ELF64 LSB header; empty when the file is not one.
elf_payload_offset() {
  local file="$1" magic shoff shentsize shnum
  magic="$(od -An -v -t x1 -N 6 "$file" | tr -d ' \n')"
  [ "$magic" = "7f454c460201" ] || return 1
  shoff="$(field "$file" 40 8)"
  shentsize="$(field "$file" 58 2)"
  shnum="$(field "$file" 60 2)"
  case "$shoff$shentsize$shnum" in '' | *[!0-9]*) return 1 ;; esac
  [ "$shoff" -gt 0 ] && [ "$shentsize" -gt 0 ] && [ "$shnum" -gt 0 ] || return 1
  echo $((shoff + shentsize * shnum))
}

selftest() {
  work="$(mktemp -d)"
  trap 'rm -rf "$work"' EXIT
  header() {
    printf '\177ELF\002\001\001\000' > "$1"
    printf '\000%.0s' $(seq 1 32) >> "$1"
    printf '\350\003\000\000\000\000\000\000' >> "$1"
    printf '\000%.0s' $(seq 1 10) >> "$1"
    printf '\100\000\003\000' >> "$1"
    printf '\000%.0s' $(seq 1 2) >> "$1"
  }

  header "$work/elf64"
  [ "$(elf_payload_offset "$work/elf64")" = "1192" ] || die "selftest: 1000 + 64 * 3 was not read as 1192"

  header "$work/elf32"
  printf '\001' | dd of="$work/elf32" bs=1 seek=4 conv=notrunc 2>/dev/null
  if elf_payload_offset "$work/elf32" >/dev/null; then die "selftest: a 32-bit ELF was given an offset"; fi

  header "$work/bigendian"
  printf '\002' | dd of="$work/bigendian" bs=1 seek=5 conv=notrunc 2>/dev/null
  if elf_payload_offset "$work/bigendian" >/dev/null; then die "selftest: a big-endian ELF was given an offset"; fi

  printf 'hsqs not an elf at all' > "$work/squashfs"
  if elf_payload_offset "$work/squashfs" >/dev/null; then die "selftest: a file with no ELF header was given an offset"; fi

  header "$work/no-sections"
  printf '\000\000' | dd of="$work/no-sections" bs=1 seek=60 conv=notrunc 2>/dev/null
  if elf_payload_offset "$work/no-sections" >/dev/null; then die "selftest: a runtime with no section headers was given an offset"; fi

  echo "verify-linux-appimage.sh: offset read from an ELF64 LSB header; 32-bit, big-endian, non-ELF and section-less inputs refused"
}

if [ "${1:-}" = "--selftest" ]; then
  [ $# -eq 1 ] || die "usage: verify-linux-appimage.sh --selftest"
  selftest
  exit 0
fi

[ $# -eq 4 ] || die "usage: verify-linux-appimage.sh <appimage> <version> <binary-version> <staged-payload-dir>"
image="$1" version="$2" binary_version="$3" payload="$4"
[ -f "$image" ] || die "no AppImage at $image"
[ -d "$payload" ] || die "no staged payload at $payload"
command -v unsquashfs >/dev/null 2>&1 || die "no unsquashfs (squashfs-tools); the image cannot be read without running it"

package='.components.app.targets[] | select(.rid == "linux-x64") | .packages[] | select(.kind == "appimage")'
pattern="$(jqr "[$package] | if length == 1 then .[0].pattern else empty end" "$DEFINITION" 2>/dev/null || true)"
[ -n "$pattern" ] || die "$DEFINITION does not declare exactly one appimage package for app/$RID"
expected_name="$(printf '%s' "$pattern" | sed -e "s/{version}/$version/g" -e "s/{rid}/$RID/g")"
[ "$(basename "$image")" = "$expected_name" ] \
  || die "AppImage is named $(basename "$image"); the definition names it $expected_name"

offset="$(elf_payload_offset "$image")" || die "$(basename "$image") does not start with an ELF64 LSB runtime"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
unsquashfs -no-progress -o "$offset" -d "$work/root" "$image" >/dev/null \
  || die "unsquashfs found no squashfs at offset $offset in $(basename "$image")"
root="$work/root"

has_line() { grep -qxF -- "$2" "$1" || die "$(basename "$1") lacks the line: $2"; }
desktop="$root/$ID.desktop"
appdata="$root/usr/share/metainfo/$ID.appdata.xml"
[ -f "$desktop" ] || die "the image holds no $ID.desktop"
[ -f "$appdata" ] || die "the image holds no usr/share/metainfo/$ID.appdata.xml"
has_line "$desktop" "Name=keypaste"
has_line "$desktop" "Comment=keypaste $version internal unsigned candidate"
has_line "$desktop" "X-AppImage-Version=$version"
has_line "$appdata" "  <developer id=\"com.keypaste\">"
has_line "$appdata" "    <name>keypaste</name>"
has_line "$appdata" "    <p>keypaste $version internal unsigned candidate. Not for distribution.</p>"
cmp -s "$desktop" "$root/usr/share/applications/$ID.desktop" || die "the image's two desktop entries differ"
cmp -s "$root/AppRun" "$ROOT/packaging/linux/AppRun" || die "the image's AppRun is not packaging/linux/AppRun"

listing() { (cd "$1" && find . -type f -exec sha256sum {} + | sort -k2); }
diff <(listing "$payload") <(listing "$root/usr/bin") > "$work/payload.diff" \
  || { cat "$work/payload.diff" >&2; die "the image's usr/bin is not the staged payload"; }

host="$root/usr/bin/keypaste-app"
[ -x "$host" ] || die "the image's keypaste-app is not executable"
"$host" --selftest || die "the unpacked keypaste-app failed --selftest"
reported="$("$host" --version | tr -d '[:space:]')"
[ "$reported" = "$binary_version" ] || die "the unpacked binary reports $reported, expected $binary_version"
[ -x "$root/usr/bin/keypaste-mcp" ] || die "the image carries no keypaste-mcp for a connected client to start"
case "$("$root/AppRun" mcp --help 2>&1)" in "usage: keypaste-mcp"*) ;; *) die "the image's AppRun does not start keypaste-mcp for mcp" ;; esac
"$ROOT/scripts/verify-publisher-metadata.sh" "$root/usr/bin"

echo "$(basename "$image"): keypaste $version, internal and unsigned; $(listing "$payload" | wc -l | tr -d ' ') payload files unpack byte-identical at offset $offset and pass --selftest."
