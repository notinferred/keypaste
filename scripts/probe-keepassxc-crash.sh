#!/usr/bin/env bash
# F.68 probe, deleted once its diagnosis closes: opens one vault <tries> times in each KeePassXC version as
# exercise-desktop-acts.sh's keepassxc_recent opens it, a shell's background job given the password on stdin, and
# counts the starts that end by themselves, which KeePassXC #13521 reports of 2.7 on Qt 5.
#
# Usage: probe-keepassxc-crash.sh <tries> <version>...   (on a macOS or Windows runner)
# Writes: $RUNNER_TEMP/kpxc-probe/records, with one line per start in results.txt and macOS's crash reports.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly ROOT
readonly PW='probe-password'

die() { echo "::error::$*" >&2; exit 1; }

[ $# -ge 2 ] || die 'usage: probe-keepassxc-crash.sh <tries> <version>...'
tries="$1"
shift
case "$(uname -s)" in
  Darwin) PLATFORM=macos ;;
  MINGW*|MSYS*) PLATFORM=windows ;;
  *) die 'this probes the macOS and Windows builds of KeePassXC' ;;
esac
WORK="${RUNNER_TEMP:-$PWD}/kpxc-probe"
RECORDS="$WORK/records"
mkdir -p "$RECORDS"

native() { if [ "$PLATFORM" = windows ]; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

# The release's own asset and the digest GitHub lists for it.
asset() {
  case "$PLATFORM-$1" in
    macos-2.7.12) echo 'KeePassXC-2.7.12-arm64.dmg 65f4f63607180c0a15794b4a4068f85e99ed5391c87c1fb9312648f1b36fed40' ;;
    macos-2.8.0) echo 'KeePassXC-2.8.0-arm64.dmg 223d5c71ae6c11d48192c69fd582fd5f10047e91ebcebc014de33a42539b9519' ;;
    windows-2.7.12) echo 'KeePassXC-2.7.12-Win64.zip 958234b0669d757b53eacf42bdd5de0fa1cc1ab7527709ddf4f7e29c06a8305f' ;;
    windows-2.8.0) echo 'KeePassXC-2.8.0-x64.zip 48ff4f088e9ce70e241da1270e5081528dba77c38b17dae4aa6690ffc50fc828' ;;
    *) die "no pinned asset for KeePassXC $1 on $PLATFORM" ;;
  esac
}

# install <version>: puts the version in a folder of its own, its app's path in the folder's app file.
install() {
  local name digest dest app
  read -r name digest <<< "$(asset "$1")"
  dest="$WORK/$1"
  mkdir -p "$dest"
  "$ROOT/scripts/fetch-pinned-asset.sh" "https://github.com/keepassxreboot/keepassxc/releases/download/$1/$name" "$digest" "$dest/$name" >&2
  if [ "$PLATFORM" = macos ]; then
    hdiutil attach -quiet -nobrowse -readonly -mountpoint "$dest/mnt" "$dest/$name" >&2
    ditto "$dest/mnt/KeePassXC.app" "$dest/KeePassXC.app"
    hdiutil detach -quiet "$dest/mnt" >&2
    app="$dest/KeePassXC.app/Contents/MacOS/KeePassXC"
  else
    /c/Windows/System32/tar.exe -xf "$dest/$name" -C "$dest" >&2
    app="$(find "$dest" -name KeePassXC.exe -type f | head -n 1)"
    # A portable build keeps its settings beside itself and ignores KPXC_CONFIG, as keepassxc_recent has it.
    rm -f "$(dirname "$app")/.portable"
  fi
  [ -x "$app" ] || die "KeePassXC $1 has no app at '$app'"
  printf '%s' "$app" > "$dest/app"
}

app_of() { cat "$WORK/$1/app"; }

# What the platform kept of a start that ended by itself.
crash_report() {
  local report
  [ "$PLATFORM" = macos ] || return 0
  for _ in $(seq 1 30); do
    report="$(find "$HOME/Library/Logs/DiagnosticReports" -maxdepth 1 -name 'KeePassXC*' -newer "$1" 2> /dev/null | head -n 1)"
    [ -z "$report" ] || break
    sleep 0.5
  done
  [ -n "$report" ] || { printf '; no crash report within 15 s'; return 0; }
  cp "$report" "$2.ips"
  printf '; %s' "$(grep -o '"exception" *: *{[^}]*}' "$report" | head -n 1)"
}

close() {
  if [ "$PLATFORM" = windows ]; then taskkill //PID "$(cat "/proc/$1/winpid")" > /dev/null 2>&1 || true; else kill -TERM "$1"; fi
  for _ in $(seq 1 60); do
    kill -0 "$1" 2> /dev/null || break
    sleep 0.5
  done
  kill -9 "$1" 2> /dev/null || true
  wait "$1" 2> /dev/null || true
}

# open <version> <app> <try>: one start, kept 6 s after KeePassXC lists the vault and then closed, or its own end.
open() {
  local dir="$WORK/try-$1-$3" pid code recorded='' started outcome listing
  mkdir -p "$dir"
  printf '[General]\nSingleInstance=false\nUpdateCheckMessageShown=true\n\n[GUI]\nCheckForUpdates=false\n' > "$dir/keepassxc.ini"
  started=$SECONDS
  printf '%s\n' "$PW" | KPXC_CONFIG="$(native "$dir/keepassxc.ini")" KPXC_CONFIG_LOCAL="$(native "$dir/local.ini")" \
    "$2" --pw-stdin "$(native "$VAULT")" > "$dir/keepassxc.log" 2>&1 &
  pid=$!
  outcome=''
  for _ in $(seq 1 60); do
    if ! kill -0 "$pid" 2> /dev/null; then
      code=0
      wait "$pid" 2> /dev/null || code=$?
      if [ -n "$recorded" ]; then listing=after; else listing=before; fi
      outcome="ended by itself after $((SECONDS - started)) s, status $code, $listing listing the vault"
      outcome+="$(crash_report "$dir/keepassxc.log" "$RECORDS/$1-$3")"
      break
    fi
    if [ -z "$recorded" ] && grep -q "LastDatabases=.*probe.kdbx" "$dir/local.ini" 2> /dev/null; then recorded=$SECONDS; fi
    if [ -n "$recorded" ] && [ $((SECONDS - recorded)) -ge 6 ]; then
      close "$pid"
      outcome='closed'
      break
    fi
    sleep 0.5
  done
  if [ -z "$outcome" ]; then
    close "$pid"
    outcome="closed after 30 s without listing the vault: $(tr -d '\r' < "$dir/keepassxc.log" | tail -n 3 | tr '\n' ' ')"
  fi
  echo "$1 try $3: $outcome" | tee -a "$RECORDS/results.txt"
}

for version in "$@"; do install "$version"; done

# One vault, made by the first version's CLI as the macOS acts make theirs, opened by every version.
cli="$(dirname "$(app_of "$1")")/keepassxc-cli"
[ "$PLATFORM" = windows ] && cli+='.exe'
VAULT="$WORK/probe.kdbx"
printf '%s\n%s\n' "$PW" "$PW" | "$cli" db-create -q -p "$(native "$VAULT")" > /dev/null

for version in "$@"; do
  for i in $(seq 1 "$tries"); do open "$version" "$(app_of "$version")" "$i"; done
done

{
  echo "### KeePassXC opened $tries times per version on $(uname -s)"
  echo
  for version in "$@"; do
    ended="$(grep -c "^$version try .*ended by itself" "$RECORDS/results.txt" || true)"
    echo "- $version: $ended of $tries ended by themselves"
  done
} | tee -a "${GITHUB_STEP_SUMMARY:-/dev/null}"
