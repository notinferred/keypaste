#!/usr/bin/env bash
# Checks keypaste's macOS code as Developer ID signed and notarized, as a downloaded copy meets Gatekeeper:
#
#   keypaste.app.zip  every Mach-O signed by one team under the hardened runtime with a timestamp, keypaste-app without
#                     the library-validation exemption, the seal verified, a changed byte refused, spctl accepting the
#                     bundle, its ticket stapled, and the bundle started through LaunchServices passing --selftest
#                     with the libargon2.1.dylib beside it, as keypaste's doctor derives with it too, and with the
#                     provisioning profile it embeds admitting it to the keychain Touch ID needs.
#   .dmg              the image signed, its ticket stapled and spctl accepting it to open.
#   directory         the CLI archive's keypaste and libargon2.1.dylib signed alike, and a quarantined keypaste
#                     running and its doctor naming the libargon2.1.dylib beside it. Loose code holds no ticket.
#
# Usage: verify-macos-signature.sh <keypaste.app.zip | image.dmg | directory>
set -euo pipefail

die() { echo "::error::$*" >&2; exit 1; }

[ $# -eq 1 ] || die "usage: verify-macos-signature.sh <keypaste.app.zip | image.dmg | directory>"
target="$1"
[ "$(uname -s)" = Darwin ] || die "a macOS signature is checked on macOS"

work="$(cd "$(mktemp -d)" && pwd -P)"
trap 'rm -rf "$work"' EXIT

team_of() { codesign -dv "$1" 2>&1 | sed -n 's/^TeamIdentifier=//p'; }

# Requires one Developer ID signature with the hardened runtime and a secure timestamp, from the team given.
signed_by() {
  local path="$1" team="$2" details
  details="$(codesign -dvv "$path" 2>&1)"
  case "$details" in *'Authority=Developer ID Application: '*) ;; *) die "$(basename "$path") is not signed with a Developer ID Application identity" ;; esac
  case "$details" in *'flags=0x'*'runtime'*) ;; *) die "$(basename "$path") is not signed under the hardened runtime" ;; esac
  case "$details" in *'Timestamp='*) ;; *) die "$(basename "$path") carries no secure timestamp" ;; esac
  [ "$(team_of "$path")" = "$team" ] || die "$(basename "$path") is signed by another team than keypaste-app"
}

refuses_changed_byte() {
  local file="$1" byte
  byte="$(xxd -s 4096 -l 1 -p "$file")"
  printf '%02x' $((0x$byte ^ 1)) | xxd -r -p | dd of="$file" bs=1 seek=4096 conv=notrunc 2> /dev/null
}

doctor_derives_beside() {
  local keypaste="$1" where
  where="$(KEYPASTE_HOME="$work/home" "$keypaste" doctor --json | jq -r '.[] | select(.area == "argon2" and .status == "ok") | .where')" || true
  [ "$where" = "$(cd "$(dirname "$keypaste")" && pwd -P)/libargon2.1.dylib" ] \
    || die "$keypaste's doctor did not derive with the libargon2.1.dylib beside it: ${where:-managed code}"
  echo "doctor derives with $where" >&2
}

quarantine() { xattr -w com.apple.quarantine "0081;$(printf '%x' "$(date +%s)");Safari;" "$1"; }

check_bundle() {
  ditto -x -k "$target" "$work"
  local app="$work/keypaste.app" macos="$work/keypaste.app/Contents/MacOS" team
  [ -d "$app" ] || die "$target holds no keypaste.app"
  team="$(team_of "$macos/keypaste-app")"
  [ -n "$team" ] || die "keypaste-app names no team, so it is not Developer ID signed"
  while IFS= read -r -d '' path; do signed_by "$path" "$team"; done < <(find "$macos" -type f -print0)
  if codesign -d --entitlements - --xml "$app" 2> /dev/null | grep -q 'disable-library-validation'; then
    die "keypaste-app is exempt from library validation"
  fi
  [ -f "$app/Contents/embedded.provisionprofile" ] || die "the bundle embeds no provisioning profile, so Touch ID cannot reach the keychain"
  codesign -d --entitlements - --xml "$app" 2> /dev/null | grep -q 'keychain-access-groups' \
    || die "keypaste-app is not signed with the keychain access group Touch ID needs"
  codesign --verify --deep --strict --verbose=2 "$app" >&2 || die "the bundle's signature does not verify"
  spctl -a -vvv -t exec "$app" >&2 || die "Gatekeeper does not accept the bundle"
  xcrun stapler validate "$app" >&2 || die "the bundle carries no stapled ticket"

  ditto "$app" "$work/changed.app"
  refuses_changed_byte "$work/changed.app/Contents/MacOS/libargon2.1.dylib"
  ! codesign --verify --deep --strict "$work/changed.app" 2> /dev/null || die "the bundle's signature accepts a changed libargon2.1.dylib"
  ! spctl -a -t exec "$work/changed.app" 2> /dev/null || die "Gatekeeper accepts the bundle with a changed libargon2.1.dylib"
  rm -rf "$work/changed.app"

  doctor_derives_beside "$macos/keypaste"
  # Unquarantined: a quarantined download's first open waits for a person to confirm it, which spctl's verdict above stands in for.
  : > "$work/launched"
  if ! open -W -n -g --stdout "$work/launched" --stderr "$work/launched.err" "$app" --args --selftest 2> "$work/open.err"; then
    grep -q 'kevent() failed: No such process' "$work/open.err" || die "open could not start the bundle: $(cat "$work/open.err")"
  fi
  grep -q '^keypaste-app: selftest ok ' "$work/launched" \
    || die "the notarized bundle did not pass --selftest: $(cat "$work/launched" "$work/launched.err")"
  grep -q '^keypaste-app: Argon2 derived natively by .*/keypaste\.app/Contents/MacOS/libargon2\.1\.dylib$' "$work/launched" \
    || die "the notarized bundle did not derive with the libargon2.1.dylib in its Contents/MacOS: $(cat "$work/launched")"
  grep -q '^keypaste-app: keychain admits this build ' "$work/launched" \
    || die "the keychain does not admit the notarized bundle, so Touch ID cannot work: $(cat "$work/launched")"
  cat "$work/launched" >&2
}

check_image() {
  codesign --verify --strict --verbose=2 "$target" >&2 || die "$(basename "$target")'s signature does not verify"
  case "$(codesign -dvv "$target" 2>&1)" in *'Authority=Developer ID Application: '*) ;; *) die "$(basename "$target") is not Developer ID signed" ;; esac
  xcrun stapler validate "$target" >&2 || die "$(basename "$target") carries no stapled ticket"
  spctl -a -vvv -t open --context context:primary-signature "$target" >&2 || die "Gatekeeper does not accept $(basename "$target") to open"
  cp "$target" "$work/changed.dmg"
  refuses_changed_byte "$work/changed.dmg"
  ! codesign --verify --strict "$work/changed.dmg" 2> /dev/null || die "$(basename "$target")'s signature accepts a changed byte"
}

check_directory() {
  local team
  ditto "$target" "$work/cli"
  team="$(team_of "$work/cli/keypaste")"
  [ -n "$team" ] || die "keypaste names no team, so it is not Developer ID signed"
  signed_by "$work/cli/keypaste" "$team"
  signed_by "$work/cli/libargon2.1.dylib" "$team"
  codesign --verify --strict --verbose=2 "$work/cli/keypaste" "$work/cli/libargon2.1.dylib" >&2 || die "the CLI's signatures do not verify"
  quarantine "$work/cli/keypaste"
  quarantine "$work/cli/libargon2.1.dylib"
  "$work/cli/keypaste" --version >&2 || die "Gatekeeper stopped the quarantined keypaste"
  doctor_derives_beside "$work/cli/keypaste"
  cp "$work/cli/keypaste" "$work/changed"
  refuses_changed_byte "$work/changed"
  ! codesign --verify --strict "$work/changed" 2> /dev/null || die "keypaste's signature accepts a changed byte"
}

if [ -d "$target" ]; then
  check_directory
else
  case "$target" in
    *.zip) check_bundle ;;
    *.dmg) check_image ;;
    *) die "$target is neither a keypaste.app zip, a .dmg nor a directory" ;;
  esac
fi
echo "ok: $(basename "$target") is Developer ID signed and notarized" >&2
