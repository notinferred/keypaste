#!/usr/bin/env bash
# 8.1f: both builds of keypaste's extension, loaded unpacked in a real Chromium and a real Firefox, pair with a vault
# tests/Keypaste.AppDriver holds. The driver turns the host on for both under a temporary home and arms pairing; each
# browser starts the built keypaste through that host, its options page presses Connect, the driver allows the prompt,
# and the page and Settings' list must show the pairing under that build (extension/tests/pair-in-browsers.js).
#
# Usage: verify-extension-load.sh, on Linux, after `node extension/build.js` and `npx playwright install chromium` in extension/.
# Chromium is Playwright's, pinned by extension/package-lock.json; Firefox ESR and geckodriver are pinned below.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=scripts/lib/common.sh
. "$ROOT/scripts/lib/common.sh"
cd "$ROOT"

readonly FIREFOX_VERSION='153.6.0esr'
readonly FIREFOX_URL="https://archive.mozilla.org/pub/firefox/releases/$FIREFOX_VERSION/linux-x86_64/en-US/firefox-$FIREFOX_VERSION.tar.xz"
# The release's SHA256SUMS line for that archive.
readonly FIREFOX_SHA256='31321cd9e2cef73df678417e810cdd1b5760aa5cc8063c8465669e5869cb8498'
readonly GECKODRIVER_VERSION='0.37.1'
readonly GECKODRIVER_URL="https://github.com/mozilla/geckodriver/releases/download/v$GECKODRIVER_VERSION/geckodriver-v$GECKODRIVER_VERSION-linux64.tar.gz"
# The digest GitHub records for the release asset.
readonly GECKODRIVER_SHA256='e815130ea95983e162ae91843b48d3a3ce991735635fce83a647afde21e09f7e'
readonly MASTER='extension-load-master-pw'

[ "$(uname -s)" = Linux ] || die "this gate runs on Linux"
require node
require dotnet
require tar
for build in chromium firefox; do
  [ -f "artifacts/extension/$build/manifest.json" ] || die "no unpacked $build build; run node extension/build.js first"
done
chrome_id="$(node extension/test-keys/ids.js | awk '$1 == "chrome" { print $2 }')"
[ -n "$chrome_id" ] || die "extension/test-keys/ids.js gave no Chrome ID"

watchdog 720 node Keypaste.AppDriver keypaste chrome firefox geckodriver

step "Build libsodium, keypaste and the app driver"
bash scripts/build-libsodium.sh >/dev/null
for project in src/Keypaste.Cli/Keypaste.Cli.csproj tests/Keypaste.AppDriver/Keypaste.AppDriver.csproj; do
  dotnet restore "$project" --locked-mode
  dotnet build "$project" --no-restore -c Release -warnaserror
done
dotnet build-server shutdown >/dev/null 2>&1 || true
cli="$(keypaste_bin)"
driver="$(app_driver)"

work="$(mktemp -d)"
cleanup() {
  # shellcheck disable=SC2046 # one process id per word
  end_tree $(pgrep -P "$$" 2>/dev/null) 2>/dev/null || true
  rm -rf "$work"
}
trap cleanup EXIT

step "Fetch Firefox $FIREFOX_VERSION and geckodriver $GECKODRIVER_VERSION"
scripts/fetch-pinned-asset.sh "$FIREFOX_URL" "$FIREFOX_SHA256" "$work/firefox.tar.xz"
scripts/fetch-pinned-asset.sh "$GECKODRIVER_URL" "$GECKODRIVER_SHA256" "$work/geckodriver.tar.gz"
tar -xJf "$work/firefox.tar.xz" -C "$work"
tar -xzf "$work/geckodriver.tar.gz" -C "$work"

step "Make the vault"
root="$work/root"
mkdir -p "$root/home" "$root/config" "$root/keypaste-home"
vault="$root/vault.kdbx"
printf '%s\n%s\n' "$MASTER" "$MASTER" | KEYPASTE_HOME="$root/keypaste-home" "$cli" init "$vault" >/dev/null

step "Pair Chromium and Firefox with the held vault"
KEYPASTE_DRIVER_PASSWORD="$MASTER" node extension/tests/pair-in-browsers.js "$root" "$driver" "$cli" "$vault" \
  "$PWD/artifacts/extension/chromium" "$PWD/artifacts/extension/firefox" "$work/firefox/firefox" "$work/geckodriver" "$chrome_id"

echo "both builds paired with keypaste under their builds' IDs"
