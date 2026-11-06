#!/usr/bin/env bash
# The published Linux desktop starts on a machine with no ICU, falling back to invariant globalization
# (src/Keypaste.App/Globalization/IcuFallback.cs, Q.16). Without that fallback .NET ends the process at
# its first use of a culture, and the app would not open at all.
#
# Usage: verify-no-icu.sh <directory holding the published keypaste-app | keypaste-app AppImage>
#
# In a container that has never had libicu, --selftest must pass and show that dates read as ISO and text
# sorts by code point. Then, with an X server whose packages bring libicu in and with every libicu removed
# again, the app itself must open its window and stay up. An AppImage is held to the selftest alone.
set -euo pipefail

target="${1:?usage: verify-no-icu.sh <directory holding keypaste-app | AppImage>}"

image='ubuntu:24.04'

# The selftest's own lines, from whichever program runs it in the container.
selftest_with_no_icu='
  if ls /usr/lib/*/libicu* /lib/*/libicu* 2>/dev/null | grep .; then
    echo "this image already holds libicu, so it proves nothing" >&2
    exit 1
  fi
  "$@" > /tmp/selftest.out
  cat /tmp/selftest.out
  grep -q "^keypaste-app: selftest ok " /tmp/selftest.out
  grep -qx "keypaste-app: text sorts item 10 before item 2" /tmp/selftest.out
  grep -qx "keypaste-app: a date reads 2026-11-05 15:04" /tmp/selftest.out
  echo "selftest passed with no ICU"
'

if [ -f "$target" ]; then
  appimage="$(cd "$(dirname "$target")" && pwd)/$(basename "$target")"
  docker run --rm -v "$appimage:/keypaste.AppImage:ro" "$image" sh -ec "$selftest_with_no_icu" -- /keypaste.AppImage --appimage-extract-and-run --selftest
  exit 0
fi

payload="$(cd "$target" && pwd)"
[ -x "$payload/keypaste-app" ] || { echo "no keypaste-app in $payload" >&2; exit 2; }

docker run --rm -v "$payload:/app:ro" "$image" sh -ec "$selftest_with_no_icu" -- /app/keypaste-app --selftest

docker run --rm -v "$payload:/app:ro" "$image" sh -ec '
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq >/dev/null
  apt-get install -y -qq --no-install-recommends xvfb x11-utils libx11-6 libice6 libsm6 libfontconfig1 libxcursor1 libxrandr2 libxi6 libxext6 libxrender1 >/dev/null
  rm -f /usr/lib/*/libicu* /lib/*/libicu*
  ldconfig
  if ldconfig -p | grep libicu; then echo "libicu is still here" >&2; exit 1; fi
  Xvfb :99 -screen 0 1280x800x24 >/dev/null 2>&1 &
  sleep 2
  export DISPLAY=:99 KEYPASTE_HOME=/tmp/keypaste-home HOME=/tmp/home
  mkdir -p "$HOME" "$KEYPASTE_HOME"
  /app/keypaste-app > /tmp/app.log 2>&1 &
  app=$!
  sleep 15
  if ! kill -0 "$app" 2>/dev/null; then
    echo "the app did not stay up with no ICU:" >&2
    cat /tmp/app.log >&2
    exit 1
  fi
  if ! xwininfo -root -tree | grep -q "\"keypaste\""; then
    echo "the app is up with no ICU and opened no window:" >&2
    xwininfo -root -tree >&2
    cat /tmp/app.log >&2
    exit 1
  fi
  kill "$app"
  echo "the app opened its window and stayed up for 15 seconds with no ICU"
'
