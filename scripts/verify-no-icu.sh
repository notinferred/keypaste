#!/usr/bin/env bash
# The published Linux desktop starts on a machine with no ICU, falling back to invariant globalization
# (src/Keypaste.App/Globalization/IcuFallback.cs, Q.16). Without that fallback .NET ends the process at
# its first use of a culture, and the app would not open at all.
#
# Usage: verify-no-icu.sh <directory holding the published keypaste-app | keypaste-app AppImage>
#        verify-no-icu.sh --unlock-and-save
#
# In a container that has never had libicu, --selftest must pass and show that dates read as ISO and text
# sorts by code point. Then, with an X server whose packages bring libicu in and with every libicu removed
# again, the app itself must open its window and stay up. An AppImage is held to the selftest alone.
#
# With --unlock-and-save, from the built tests/Keypaste.AppDriver and keypaste (verify.sh desktop-gates on Linux): a vault made
# on this machine with ICU, with non-ASCII master password, titles and values, is unlocked, changed and saved through the app's
# screens in a container that has never had libicu, and read back here by other processes. The driver itself must report that
# ICU does not load and that text compares ordinally there, and report the opposite here, so the check cannot pass vacuously.
#
# NEGATIVE CONTROL: libicu in the container, or a process still using ICU there, fails on the driver's line; the driver without
# IcuFallback ends at its first culture; and a changed or lost byte of any value fails the read-back.
set -euo pipefail

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"

image='ubuntu:24.04'

# Run in the container as root for its packages, then as this machine's user, so what it writes stays removable here.
unlock_and_save_with_no_icu='
  export DEBIAN_FRONTEND=noninteractive
  apt_options="-o Acquire::Retries=3 -o Acquire::http::Timeout=30 -o Acquire::https::Timeout=30"
  apt-get $apt_options update -qq >/dev/null
  apt-get $apt_options install -y -qq --no-install-recommends libssl3t64 libfontconfig1 >/dev/null

  found="$(find / \( -path /proc -o -path /sys \) -prune -o -name "libicu*" -print)"
  if [ -n "$found" ] || ldconfig -p | grep libicu; then
    echo "this container holds libicu, so it proves nothing: $found" >&2
    exit 1
  fi

  person() {
    setpriv --reuid="$PERSON_UID" --regid="$PERSON_GID" --clear-groups \
      env DOTNET_ROOT=/dotnet HOME=/work/person KEYPASTE_HOME=/work/no-icu-home /driver/Keypaste.AppDriver "$@"
  }

  said="$(person globalization)"
  echo "$said"
  [ "$said" = "globalization icu=none collation=ordinal" ] || { echo "the process still has ICU" >&2; exit 1; }

  export KEYPASTE_DRIVER_PASSWORD="$MASTER"
  person edit /work/vault.kdbx "$MADE_TITLE" "$EDITED_NOTES" "$EDITED_URL"
  KEYPASTE_DRIVER_NEW_PASSWORD="$ADDED_PASSWORD" person item-new /work/vault.kdbx login "" "$ADDED_TITLE" --username "$ADDED_USER" --notes "$ADDED_NOTES"
'

unlock_and_save() {
  require docker
  require jq
  DIE_FILES='ACTS_OUT READ_ERR'

  CLI="$(keypaste_bin)"
  DRV="$(app_driver)"
  local driver_dir runtime said
  driver_dir="$(cd "$(dirname "$DRV")" && pwd)"
  runtime="$(dirname "$(readlink -f "$(command -v dotnet)")")"
  [ -d "$runtime/shared/Microsoft.NETCore.App" ] || die "no .NET runtime at $runtime"

  WORK="$(mktemp -d)"
  trap 'rm -rf "$WORK"' EXIT
  mkdir -p "$WORK/home" "$WORK/no-icu-home" "$WORK/person"
  ACTS_OUT="$WORK/acts.out"
  READ_ERR="$WORK/read.err"
  export KEYPASTE_HOME="$WORK/home"
  VAULT="$WORK/vault.kdbx"

  export MASTER='pässwörd-İstanbul-東京'
  export MADE_TITLE='Grüße İstanbul 東京'
  export EDITED_NOTES='geändert ohne ICU: straße ıİ 日本語'
  export EDITED_URL='https://例え.example/geändert'
  export ADDED_TITLE='Añadido sın ICU Привет'
  export ADDED_USER='üser-ñ-Ж'
  export ADDED_PASSWORD='sécret-ß-İ-秘密'
  export ADDED_NOTES='notas: çğüşö ΑΩ'
  local made_user='müller-İ' made_password='gehéim-ß-秘密' made_url='https://例え.example/パス' made_notes='vor dem Speichern'

  step 'the driver tells a machine with ICU from one without: here it has ICU'
  said="$("$DRV" globalization)" || die "the driver did not report its globalization here"
  [ "$said" = 'globalization icu=loads collation=linguistic' ] \
    || die "this machine's driver said '$said', so the same line from the container would prove nothing"

  step 'a vault made with ICU, its master password and an item not ASCII'
  KEYPASTE_DRIVER_NEW_PASSWORD="$MASTER" "$DRV" create "$VAULT"
  KEYPASTE_DRIVER_PASSWORD="$MASTER" KEYPASTE_DRIVER_NEW_PASSWORD="$made_password" \
    "$DRV" item-new "$VAULT" login '' "$MADE_TITLE" --username "$made_user" --url "$made_url" --notes "$made_notes"

  step 'with no ICU in the container: unlock, change an item and save, unlock again, add an item and save'
  docker run --rm -v "$driver_dir:/driver:ro" -v "$runtime:/dotnet:ro" -v "$WORK:/work" \
    -e PERSON_UID="$(id -u)" -e PERSON_GID="$(id -g)" \
    -e MASTER -e MADE_TITLE -e EDITED_NOTES -e EDITED_URL -e ADDED_TITLE -e ADDED_USER -e ADDED_PASSWORD -e ADDED_NOTES \
    "$image" bash -euo pipefail -c "$unlock_and_save_with_no_icu" >"$ACTS_OUT" 2>&1 \
    || die 'unlocking and saving with no ICU failed'
  cat "$ACTS_OUT"

  step 'the save reads back in other processes on this machine, which has ICU'
  said="$(KEYPASTE_DRIVER_PASSWORD="$MASTER" "$DRV" open "$VAULT")" || die "the app could not open what was saved with no ICU: $said"

  read_back "$MADE_TITLE" username "$made_user"
  read_back "$MADE_TITLE" password "$made_password"
  read_back "$MADE_TITLE" notes "$EDITED_NOTES"
  read_back "$MADE_TITLE" url "$EDITED_URL"
  read_back "$ADDED_TITLE" username "$ADDED_USER"
  read_back "$ADDED_TITLE" password "$ADDED_PASSWORD"
  read_back "$ADDED_TITLE" notes "$ADDED_NOTES"

  echo 'a vault unlocked, changed and saved with no ICU reads back whole'
}

# Item $1's field $2 as the saved vault holds it must be exactly $3.
read_back() {
  local held
  held="$(printf '%s\n' "$MASTER" | "$CLI" show "$1" --reveal --json --vault "$VAULT" 2>"$READ_ERR" \
    | jq -er --arg field "$2" '.[0].fields[] | select(.name == $field) | .value')" \
    || die "keypaste could not read $2 of '$1' from what was saved with no ICU"
  [ "$held" = "$3" ] || die "$2 of '$1' came back changed"
}

if [ "${1:-}" = --unlock-and-save ]; then
  unlock_and_save
  exit 0
fi

target="${1:?usage: verify-no-icu.sh <directory holding keypaste-app | AppImage> | --unlock-and-save}"

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
