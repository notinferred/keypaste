#!/usr/bin/env bash
# 8.3a3: keypaste fills a login in real browsers, with keypaste's extension as built. tests/Keypaste.AppDriver holds a vault of
# logins and answers each prompt; every browser pairs, then visits pages a local HTTPS server serves under test names, and the
# page's fields and the owner's log must show what keypaste's matching rules allow (extension/tests/fill-in-browsers.js).
#
# Usage: verify-browser-fill.sh, on Linux, after `node extension/build.js` and `npx playwright install chromium` in extension/:
#          Playwright's Chromium, pinned by extension/package-lock.json, and Edge, Firefox ESR and geckodriver pinned below.
#        verify-browser-fill.sh --branded, on a GitHub-hosted Windows, macOS or Linux runner, after `node extension/build.js`:
#          the runner's own Chrome, Edge and Firefox, with its geckodriver, and Brave pinned below, keypaste turned on in each
#          browser's real places for this user. Preinstalled browsers change with the runner's image, so this runs through
#          dev.yml for a record's evidence and is no gate of any run.
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
readonly EDGE_VERSION='157.0.4341.60-1'
readonly EDGE_URL="https://packages.microsoft.com/repos/edge/pool/main/m/microsoft-edge-stable/microsoft-edge-stable_${EDGE_VERSION}_amd64.deb"
# The SHA256 line of the package in the repository's binary-amd64 Packages index.
readonly EDGE_SHA256='492b2d336d9c337814db72c51293807038ee0104654427f283fd344bca17c500'
readonly BRAVE_VERSION='1.99.57'
readonly BRAVE_RELEASE="https://github.com/brave/brave-browser/releases/download/v$BRAVE_VERSION"
# The digests GitHub records for the release's assets.
readonly BRAVE_LINUX_SHA256='734bd9e1d24bc0a94c03b0a43845107dd8a29f572c652023cf7177f5fc3a5f38'
readonly BRAVE_WINDOWS_SHA256='42ba3a5445f717b429fb3fe457e7e531cd1c208f06a2dc9315b1fa31c09bc8b9'
readonly BRAVE_MACOS_SHA256='be8bb3332e45359c96219ae67102eb1d9a88ff1ef8906e6709efc5790705449b'
readonly MASTER='browser-fill-master-pw'

branded=false
case "${1:-}" in
  '') ;;
  --branded) branded=true ;;
  *) die "usage: verify-browser-fill.sh [--branded]" ;;
esac
os="$(uname -s)"
$branded || [ "$os" = Linux ] || die "this gate runs on Linux; --branded runs on any runner"

require node
require dotnet
require tar
require openssl
for build in chromium firefox; do
  [ -f "artifacts/extension/$build/manifest.json" ] || die "no unpacked $build build; run node extension/build.js first"
done
id_of() { node extension/keys/ids.js | awk -v store="$1" '$1 == store { print $2 }'; }
chrome_id="$(id_of chrome)"
edge_id="$(id_of edge)"
[ -n "$chrome_id" ] && [ -n "$edge_id" ] || die "extension/keys/ids.js gave no Chrome or Edge ID"

watchdog 1500 node Keypaste.AppDriver keypaste chrome msedge brave firefox geckodriver

step "Build libsodium, keypaste and the app driver"
bash scripts/build-libsodium.sh >/dev/null
for project in src/Keypaste.Cli/Keypaste.Cli.csproj tests/Keypaste.AppDriver/Keypaste.AppDriver.csproj; do
  dotnet restore "$project" --locked-mode
  dotnet build "$project" --no-restore -c Release -warnaserror
done
dotnet build-server shutdown >/dev/null 2>&1 || true
# A browser starts its host only from an absolute path.
cli="$(native "$(cd "$(dirname "$(keypaste_bin)")" && pwd)/$(basename "$(keypaste_bin)")")"
driver="$(native "$(cd "$(dirname "$(app_driver)")" && pwd)/$(basename "$(app_driver)")")"

work="$(mktemp -d)"
cleanup() {
  # shellcheck disable=SC2046 # one process id per word
  end_tree $(pgrep -P "$$" 2>/dev/null) 2>/dev/null || true
  if [ -n "${brave_volume:-}" ]; then hdiutil detach "$brave_volume" -quiet || true; fi
  rm -rf "$work"
}
trap cleanup EXIT

# The Edge Add-ons build is the Chromium build under Microsoft Edge Add-ons' key, so Edge names the ID keypaste knows for that store.
step "Make the Edge build"
cp -R artifacts/extension/chromium "$work/edge-build"
node -e '
  const fs = require("node:fs");
  const [ manifest, key ] = process.argv.slice(1);
  fs.writeFileSync(manifest, JSON.stringify({ ...JSON.parse(fs.readFileSync(manifest, "utf8")), key }, null, 4));
' "$work/edge-build/manifest.json" "$(node extension/keys/ids.js | awk '$1 == "edge" { print $3 }')"

step "Fetch the browsers"
brave=''
case "$os" in
  Linux)
    scripts/fetch-pinned-asset.sh "$BRAVE_RELEASE/brave-browser-$BRAVE_VERSION-linux-amd64.zip" "$BRAVE_LINUX_SHA256" "$work/brave.zip"
    mkdir "$work/brave" && unzip -q "$work/brave.zip" -d "$work/brave"
    brave="$work/brave/brave"
    ;;
  Darwin)
    scripts/fetch-pinned-asset.sh "$BRAVE_RELEASE/Brave-Browser-universal.dmg" "$BRAVE_MACOS_SHA256" "$work/brave.dmg"
    brave_volume="$work/brave-volume"
    hdiutil attach "$work/brave.dmg" -nobrowse -readonly -mountpoint "$brave_volume" -quiet
    cp -R "$brave_volume/Brave Browser.app" "$work/"
    hdiutil detach "$brave_volume" -quiet && brave_volume=''
    brave="$work/Brave Browser.app/Contents/MacOS/Brave Browser"
    ;;
  *)
    scripts/fetch-pinned-asset.sh "$BRAVE_RELEASE/brave-v$BRAVE_VERSION-win32-x64.zip" "$BRAVE_WINDOWS_SHA256" "$work/brave.zip"
    mkdir "$work/brave" && unzip -q "$work/brave.zip" -d "$work/brave"
    brave="$(find "$work/brave" -iname brave.exe | head -1)"
    ;;
esac
[ -x "$brave" ] || [ -f "$brave" ] || die "Brave $BRAVE_VERSION has no browser at $brave"

if $branded; then
  case "$os" in
    Linux)
      chrome="$(command -v google-chrome)"; edge="$(command -v microsoft-edge)"; firefox="$(command -v firefox)"
      geckodriver="${GECKOWEBDRIVER:+$GECKOWEBDRIVER/}geckodriver"
      ;;
    Darwin)
      chrome='/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'
      edge='/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge'
      firefox='/Applications/Firefox.app/Contents/MacOS/firefox'
      geckodriver="$(command -v geckodriver)"
      ;;
    *)
      chrome="$(cygpath -u 'C:\Program Files\Google\Chrome\Application\chrome.exe')"
      edge="$(cygpath -u 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe')"
      firefox="$(cygpath -u 'C:\Program Files\Mozilla Firefox\firefox.exe')"
      geckodriver="$(cygpath -u "${GECKOWEBDRIVER:-C:\SeleniumWebDrivers\GeckoDriver}")/geckodriver.exe"
      ;;
  esac
  for program in "$chrome" "$edge" "$firefox" "$geckodriver"; do
    [ -f "$program" ] || command -v "$program" >/dev/null || die "this runner has no $program"
  done
  browsers=("chrome:$chrome" "edge:$edge" "brave:$brave" "firefox:$firefox")
else
  scripts/fetch-pinned-asset.sh "$FIREFOX_URL" "$FIREFOX_SHA256" "$work/firefox.tar.xz"
  scripts/fetch-pinned-asset.sh "$GECKODRIVER_URL" "$GECKODRIVER_SHA256" "$work/geckodriver.tar.gz"
  scripts/fetch-pinned-asset.sh "$EDGE_URL" "$EDGE_SHA256" "$work/edge.deb"
  tar -xJf "$work/firefox.tar.xz" -C "$work"
  tar -xzf "$work/geckodriver.tar.gz" -C "$work"
  dpkg-deb -x "$work/edge.deb" "$work/edge"
  geckodriver="$work/geckodriver"
  chromium="$(cd extension && node -e 'console.log(require("@playwright/test").chromium.executablePath())')"
  browsers=("chromium:$chromium" "edge:$work/edge/opt/microsoft/msedge/msedge" "brave:$brave" "firefox:$work/firefox/firefox")
fi

step "Make the pages' certificate"
names=(shop.test www.shop.test login.shop.test shop.test.evil.test pay.other.test mail.test alice.github.io bob.github.io router.test)
san="$(printf 'DNS:%s,' "${names[@]}")"
# Git Bash would rewrite the subject as a Windows path, so it rewrites nothing and the files are named natively.
MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -days 2 -subj '/CN=keypaste browser fill test' \
  -addext "subjectAltName=${san%,}" -keyout "$(native "$work/key.pem")" -out "$(native "$work/cert.pem")" 2>"$work/openssl.log" \
  || { cat "$work/openssl.log" >&2; die "openssl made no certificate"; }

step "Make the vault"
root="$work/root"
mkdir -p "$root/home" "$root/config" "$root/keypaste-home"
vault="$root/vault.kdbx"
printf '%s\n%s\n' "$MASTER" "$MASTER" | KEYPASTE_HOME="$root/keypaste-home" "$cli" init "$vault" >/dev/null

given=()
for browser in "${browsers[@]}"; do given+=("${browser%%:*}:$(native "${browser#*:}")"); done

# The settings fill-in-browsers.js reads: where everything is, and each browser's program.
node -e '
  const fs = require("node:fs");
  const [ out, root, driver, keypaste, vault, real, geckodriver, cert, key, chromiumBuild, edgeBuild, firefoxBuild, chromeId, edgeId, ...browsers ] = process.argv.slice(1);
  fs.writeFileSync(out, JSON.stringify({
    root, driver, keypaste, vault, real: real === "true", geckodriver, cert, key, chromiumBuild, edgeBuild, firefoxBuild, chromeId, edgeId,
    browsers: browsers.map((given) => ({ name: given.slice(0, given.indexOf(":")), executable: given.slice(given.indexOf(":") + 1) })),
  }, null, 2));
' "$work/settings.json" "$(native "$root")" "$driver" "$cli" "$(native "$vault")" "$branded" "$(native "$geckodriver")" \
  "$(native "$work/cert.pem")" "$(native "$work/key.pem")" "$(native "$PWD/artifacts/extension/chromium")" \
  "$(native "$work/edge-build")" "$(native "$PWD/artifacts/extension/firefox")" "$chrome_id" "$edge_id" "${given[@]}"

step "Fill in each browser"
KEYPASTE_DRIVER_PASSWORD="$MASTER" node extension/tests/fill-in-browsers.js "$(native "$work/settings.json")"

echo "every browser filled and refused as keypaste's rules say"
