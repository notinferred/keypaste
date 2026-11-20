#!/usr/bin/env bash
# Gives a macOS runner keypaste's Developer ID for one job, and submits what it signed to Apple's notary service in
# one job and staples Apple's verdict in a later one, so no job waits on Apple.
#
#   identity <policy>     Prints identity=<SHA-1 or -> for $GITHUB_OUTPUT, as the definition's macOS signing policy
#                         says. Under developer-id the identity is imported from MACOS_CERTIFICATE_P12 and
#                         MACOS_CERTIFICATE_PASSWORD into a temporary keychain. A tag or a dispatch, which can count
#                         toward a release, refuses without it; any other run without it, such as a fork's pull
#                         request, signs ad hoc and says so.
#   submit <path>         Submits a .dmg, a .zip of keypaste.app, or a directory of loose code, zipped, with the App
#                         Store Connect key in NOTARY_KEY_P8, NOTARY_KEY_ID and NOTARY_ISSUER_ID, and prints the
#                         submission ID without waiting for Apple's verdict.
#   finish <path> <id>    Asks once for that submission's status. Accepted staples the ticket to a DMG or to the
#                         bundle in the zip, rewritten in place; a bare Mach-O cannot hold a ticket, so loose code is
#                         only notarized and Gatekeeper looks its ticket up online. In Progress exits 75, meaning not
#                         yet; any other status fails with Apple's log.
#   cleanup               Deletes the keychain and the key file.
set -euo pipefail

readonly G2_URL='https://www.apple.com/certificateauthority/DeveloperIDG2CA.cer'
readonly G2_SHA256='f16cd3c54c7f83cea4bf1a3e6a0819c8aaa8e4a1528fd144715f350643d2df3a'

die() { echo "::error::$*" >&2; exit 1; }
mask() { echo "::add-mask::$1" >&2; }

[ "$(uname -s)" = Darwin ] || die "macOS signing runs on macOS"
temp="${RUNNER_TEMP:?macos-signing.sh runs on a runner}"
readonly KEYCHAIN="$temp/keypaste-signing.keychain-db"
readonly NOTARY="$temp/keypaste-notary"

counts_toward_a_release() { [ "${GITHUB_REF_TYPE:-}" = tag ] || [ "${GITHUB_EVENT_NAME:-}" = workflow_dispatch ]; }

identity() {
  local policy="$1"
  case "$policy" in
    none) echo "identity=-"; return ;;
    developer-id) ;;
    *) die "unknown macOS signing policy '$policy'" ;;
  esac
  if [ -z "${MACOS_CERTIFICATE_P12:-}" ] || [ -z "${MACOS_CERTIFICATE_PASSWORD:-}" ]; then
    counts_toward_a_release && die "the macOS policy is developer-id and this run has no Developer ID; nothing was signed"
    echo "::warning::this run has no Developer ID, so its macOS code is signed ad hoc and is no release candidate" >&2
    echo "identity=-"; return
  fi

  local password p12="$temp/keypaste-signing.p12" g2="$temp/DeveloperIDG2CA.cer"
  password="$(openssl rand -hex 24)"
  mask "$password"
  printf '%s' "$MACOS_CERTIFICATE_P12" | base64 --decode > "$p12"
  security create-keychain -p "$password" "$KEYCHAIN"
  security set-keychain-settings -lut 21600 "$KEYCHAIN"
  security unlock-keychain -p "$password" "$KEYCHAIN"
  security import "$p12" -k "$KEYCHAIN" -f pkcs12 -P "$MACOS_CERTIFICATE_PASSWORD" -T /usr/bin/codesign > /dev/null
  rm -f "$p12"
  # The runner image's keychains may lack the intermediate that issues Developer ID G2 certificates.
  curl -fsSL --retry 3 -o "$g2" "$G2_URL"
  [ "$(shasum -a 256 "$g2" | cut -d' ' -f1)" = "$G2_SHA256" ] || die "Apple's Developer ID G2 intermediate is not the one pinned"
  local imported
  if ! imported="$(security import "$g2" -k "$KEYCHAIN" 2>&1)"; then
    case "$imported" in *'already exists'*) ;; *) die "the Developer ID G2 intermediate did not import: $imported" ;; esac
  fi
  security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$password" "$KEYCHAIN" > /dev/null
  local searched
  searched="$(security list-keychains -d user | tr -d '"' | xargs)"
  # shellcheck disable=SC2086 # the search list is a list of paths without spaces
  security list-keychains -d user -s "$KEYCHAIN" $searched

  local found sha name team
  found="$(security find-identity -v -p codesigning "$KEYCHAIN" | grep '"Developer ID Application: ' || true)"
  [ "$(printf '%s' "$found" | grep -c . || true)" = 1 ] || die "the certificate holds $(printf '%s' "$found" | grep -c . || true) valid Developer ID Application identities, not one"
  sha="$(awk '{print $2}' <<< "$found")"
  name="$(sed -E 's/.*"Developer ID Application: (.*) \(([A-Z0-9]+)\)".*/\1/' <<< "$found")"
  team="$(sed -E 's/.*\(([A-Z0-9]+)\)".*/\1/' <<< "$found")"
  mask "$sha"; mask "$name"; mask "$team"
  echo "identity=$sha"
}

readonly NOT_YET=75

# Sets the notarytool arguments in auth from the App Store Connect key.
notary_auth() {
  local var
  for var in NOTARY_KEY_P8 NOTARY_KEY_ID NOTARY_ISSUER_ID; do
    [ -n "${!var:-}" ] || die "$var is absent, so nothing can be notarized"
  done
  mkdir -p "$NOTARY"
  local key="$NOTARY/AuthKey.p8"
  (umask 077 && printf '%s' "$NOTARY_KEY_P8" | base64 --decode > "$key")
  auth=(--key "$key" --key-id "$NOTARY_KEY_ID" --issuer "$NOTARY_ISSUER_ID")
}

submit() {
  local path="$1" submission="$1" auth result id
  case "$path" in
    *.dmg | *.zip) [ -f "$path" ] || die "nothing to notarize at $path" ;;
    *)
      [ -d "$path" ] || die "$path is neither a .dmg, a .zip nor a directory"
      mkdir -p "$NOTARY"
      submission="$NOTARY/$(basename "$path").zip"
      rm -f "$submission"
      ditto -c -k "$path" "$submission" ;;
  esac
  notary_auth
  result="$(xcrun notarytool submit "$submission" "${auth[@]}" --output-format json)" || die "notarytool could not submit $(basename "$path"): $result"
  id="$(jq -r '.id // empty' <<< "$result")"
  [ -n "$id" ] || die "notarytool gave no submission id for $(basename "$path"): $result"
  echo "notarization of $(basename "$path"): submission $id" >&2
  echo "$id"
}

finish() {
  local path="$1" id="$2" auth result status
  [ -e "$path" ] || die "nothing to staple at $path"
  notary_auth
  result="$(xcrun notarytool info "$id" "${auth[@]}" --output-format json)" || die "notarytool could not read submission $id: $result"
  status="$(jq -r '.status // empty' <<< "$result")"
  echo "notarization of $(basename "$path"): submission $id, status ${status:-none}" >&2
  case "$status" in
    Accepted) ;;
    'In Progress') exit "$NOT_YET" ;;
    *)
      xcrun notarytool log "$id" "${auth[@]}" >&2 || true
      die "Apple did not accept $(basename "$path"): ${status:-$result}" ;;
  esac
  xcrun notarytool log "$id" "${auth[@]}" | jq -c '{status, statusSummary, issues}' >&2

  case "$path" in
    *.dmg)
      xcrun stapler staple "$path" >&2
      xcrun stapler validate "$path" >&2 ;;
    *.zip)
      local unzipped="$NOTARY/unzipped"
      rm -rf "$unzipped"; mkdir -p "$unzipped"
      ditto -x -k "$path" "$unzipped"
      [ -d "$unzipped/keypaste.app" ] || die "$(basename "$path") holds no keypaste.app"
      xcrun stapler staple "$unzipped/keypaste.app" >&2
      xcrun stapler validate "$unzipped/keypaste.app" >&2
      rm -f "$path"
      ditto -c -k --keepParent "$unzipped/keypaste.app" "$path"
      rm -rf "$unzipped" ;;
    *) echo "$(basename "$path") is bare Mach-O code, which cannot be stapled; Gatekeeper looks its ticket up online" >&2 ;;
  esac
}

cleanup() {
  rm -rf "$NOTARY" "$temp/keypaste-signing.p12"
  if [ -e "$KEYCHAIN" ]; then
    security delete-keychain "$KEYCHAIN"
  fi
}

case "${1:-}" in
  identity) [ $# -eq 2 ] || die "usage: macos-signing.sh identity <policy>"; identity "$2" ;;
  submit) [ $# -eq 2 ] || die "usage: macos-signing.sh submit <path>"; submit "$2" ;;
  finish) [ $# -eq 3 ] || die "usage: macos-signing.sh finish <path> <id>"; finish "$2" "$3" ;;
  cleanup) cleanup ;;
  *) die "usage: macos-signing.sh identity <policy> | submit <path> | finish <path> <id> | cleanup" ;;
esac
