#!/usr/bin/env bash
# Signs keypaste's macOS code inside-out under the hardened runtime: each library, then keypaste, then keypaste-app,
# which takes its entitlements. A keypaste.app is signed in its Contents/MacOS and then as a bundle, which seals
# Contents/Resources; a directory is the CLI archive's or the desktop archive's payload.
#
# The identity - signs ad hoc. An ad-hoc signature names no team, and library validation refuses a library whose team
# differs from its loader's, so ad-hoc programs also disable library validation: otherwise none of the bundle's
# libraries load, and keypaste derives Argon2 in managed code without failing. Any other identity is a Developer ID
# whose team signs every library too, so library validation stays on and each signature carries a secure timestamp,
# which notarization requires.
#
# A Developer ID keypaste.app also embeds packaging/macos/embedded.provisionprofile and takes the keychain entitlements
# it authorizes, which Touch ID's quick unlock needs, and any it grants for time-sensitive notices (Apple's TN3137).
# Nothing else gets them: restricted entitlements a profile does not authorize stop a program from starting.
#
# Usage: sign-macos.sh <keypaste.app | directory> <identity>
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT

die() { echo "::error::$*" >&2; exit 1; }

[ $# -eq 2 ] || die "usage: sign-macos.sh <keypaste.app | directory> <identity>"
target="$1" identity="$2"
[ "$(uname -s)" = Darwin ] || die "macOS code is signed on macOS"
[ -n "$identity" ] || die "no signing identity given"
case "$target" in
  *.app) code="$target/Contents/MacOS" ;;
  *) code="$target" ;;
esac
[ -x "$code/keypaste" ] || die "$target carries no keypaste"
case "$target" in *.app) [ -x "$code/keypaste-app" ] || die "$target is not a keypaste.app" ;; esac

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# Prints the path of the entitlements a program is signed with: its own, if it has any, for this identity.
entitlements() {
  local own="$1" out="$work/$2.entitlements"
  if [ -n "$own" ]; then cp "$own" "$out"; else plutil -create xml1 "$out"; fi
  if [ "$identity" = - ]; then /usr/libexec/PlistBuddy -c "Add :com.apple.security.cs.disable-library-validation bool true" "$out" >&2; fi
  echo "$out"
}

options=(--force --options runtime --sign "$identity")
[ "$identity" = - ] || options+=(--timestamp)
sign() { codesign "${options[@]}" "$@" >&2; }

for library in "$code"/*.dylib; do
  [ -e "$library" ] && sign "$library"
done
sign --entitlements "$(entitlements '' keypaste)" "$code/keypaste"
app_entitlements="$(entitlements "$ROOT/packaging/macos/keypaste-app.entitlements" keypaste-app)"

# Prints one value of the decoded profile by PlistBuddy's path, whose colons let a key hold dots, or nothing when it has none.
profile_value() { /usr/libexec/PlistBuddy -c "Print :$1" "$work/profile.plist" 2> /dev/null || true; }

if [ "$identity" != - ] && [[ "$target" == *.app ]]; then
  profile="$ROOT/packaging/macos/embedded.provisionprofile"
  [ -f "$profile" ] || die "packaging/macos/embedded.provisionprofile is absent, and Touch ID needs it in a Developer ID bundle"
  security cms -D -i "$profile" > "$work/profile.plist" 2> /dev/null || die "the provisioning profile does not decode"
  team="$(profile_value TeamIdentifier:0)"
  app_id="$(profile_value Entitlements:com.apple.application-identifier)"
  [ -n "$team" ] && [ "$app_id" = "$team.$(plutil -extract CFBundleIdentifier raw -o - "$target/Contents/Info.plist")" ] \
    || die "the provisioning profile is for ${app_id:-no app}, not this bundle"
  expires="$(plutil -extract ExpirationDate raw -o - "$work/profile.plist" 2> /dev/null || true)"
  [[ "$expires" > "$(date -u +%Y-%m-%dT%H:%M:%SZ)" ]] || die "the provisioning profile expired ${expires:-at an unknown date}"
  cp "$profile" "$target/Contents/embedded.provisionprofile"
  /usr/libexec/PlistBuddy \
    -c "Add :com.apple.application-identifier string $app_id" \
    -c "Add :com.apple.developer.team-identifier string $team" \
    -c "Add :keychain-access-groups array" \
    -c "Add :keychain-access-groups:0 string $app_id" \
    "$app_entitlements" >&2
  if [ "$(profile_value Entitlements:com.apple.developer.usernotifications.time-sensitive)" = true ]; then
    /usr/libexec/PlistBuddy -c "Add :com.apple.developer.usernotifications.time-sensitive bool true" "$app_entitlements" >&2
  fi
fi
case "$target" in
  *.app)
    sign --entitlements "$app_entitlements" "$target"
    if [ -n "${team:-}" ]; then
      [ "$(codesign -dv "$target" 2>&1 | sed -n 's/^TeamIdentifier=//p')" = "$team" ] || die "the bundle is signed by another team than its provisioning profile names"
    fi ;;
  *) if [ -e "$code/keypaste-app" ]; then sign --entitlements "$app_entitlements" "$code/keypaste-app"; fi ;;
esac
