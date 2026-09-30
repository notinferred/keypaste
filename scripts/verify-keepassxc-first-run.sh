#!/usr/bin/env bash
# The first run offers the databases real KeePassXC last opened (N.2, V-N.2).
#
# KeePassXC makes three vaults, opens them and closes; one is then deleted. With an empty
# KEYPASTE_HOME the app's welcome, through tests/Keypaste.AppDriver, lists the two left, the last
# active first, and the one chosen unlocks. A copy of KeePassXC's own file with malformed lines in it
# lists the same two. KeePassXC's files and the vaults are byte-identical afterwards.
#
# Where KeePassXC keeps its files decides what this establishes:
# - on a CI runner (CI=true) outside Linux, KeePassXC keeps them where it always does and the driver
#   finds them with keypaste's own locator, which is what establishes the locator;
# - on Linux, XDG_CONFIG_HOME and XDG_CACHE_HOME put them in the work directory and the locator still
#   finds them;
# - anywhere else KeePassXC is pointed at the work directory with --config and --localconfig, and so is
#   the driver, so a developer's own KeePassXC settings are never read or written.
# KeePassXC runs as its own single instance throughout, so a KeePassXC already open is left alone.
#
# NEGATIVE CONTROL: the check the welcome passes fails when KeePassXC's file is absent.
set -euo pipefail

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_PREFIX='FIRST-RUN GATE FAILED: '

dir=${1:-}
[ -n "$dir" ] || die "usage: verify-keepassxc-first-run.sh <work-directory>"
. "$(dirname "${BASH_SOURCE[0]}")/lib/kpxc.sh"
gui=$(keepassxc_app)
drv=$(app_driver)

rm -rf "$dir"
mkdir -p "$dir/home" "$dir/vaults" "$dir/tmp"
dir=$(cd "$dir" && pwd)
export KEYPASTE_HOME="$dir/home"

digest() { if [ -f "$1" ]; then sha256sum "$1" | cut -d' ' -f1; else echo absent; fi; }

case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) os=windows ;;
  Darwin) os=macos ;;
  *) os=linux ;;
esac

# Where KeePassXC 2.7 keeps its roaming and local settings (src/core/Config.cpp), and what the driver is told.
flags=()
driver_ini=()
if [ "$os" = linux ]; then
  mode=xdg
  export XDG_CONFIG_HOME="$dir/xdg-config" XDG_CACHE_HOME="$dir/xdg-cache"
  roaming="$XDG_CONFIG_HOME/keepassxc/keepassxc.ini"
  local_ini="$XDG_CACHE_HOME/keepassxc/keepassxc.ini"
elif [ "${CI:-}" = true ]; then
  mode=default
  if [ "$os" = windows ]; then
    roaming="$(cygpath -u "$APPDATA")/KeePassXC/keepassxc.ini"
    local_ini="$(cygpath -u "$LOCALAPPDATA")/KeePassXC/keepassxc.ini"
    # The pinned archive may be the portable build, which keeps its settings beside itself.
    rm -f "$(dirname "$gui")/.portable"
  else
    roaming="$HOME/Library/Application Support/KeePassXC/keepassxc.ini"
    local_ini="$HOME/Library/Caches/KeePassXC/keepassxc.ini"
  fi
else
  mode=flags
  roaming="$dir/kpxc/keepassxc.ini"
  local_ini="$dir/kpxc/keepassxc_local.ini"
  flags=(--config "$(native "$roaming")" --localconfig "$(native "$local_ini")")
  driver_ini=(--keepassxc-config "$(native "$local_ini")")
fi
echo "KeePassXC settings: $mode; local file expected at $local_ini"
[ "$mode" = flags ] || [ ! -e "$local_ini" ] || die "KeePassXC's local file already exists at $local_ini; this gate needs a fresh one"

# Its own instance, which never asks about updates.
mkdir -p "$(dirname "$roaming")"
printf '[General]\nSingleInstance=false\nUpdateCheckMessageShown=true\n\n[GUI]\nCheckForUpdates=false\n' > "$roaming"
export USER="keypaste-gate-$$" USERNAME="keypaste-gate-$$" TMPDIR="$dir/tmp"
[ "$os" = windows ] && export TMP="$(native "$dir/tmp")" TEMP="$(native "$dir/tmp")"
[ "$os" = linux ] && [ -z "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ] && export QT_QPA_PLATFORM=offscreen

step "KeePassXC makes three vaults, opens them and closes"
for name in first gone last; do
  printf '%s\n%s\n' "$pw" "$pw" | "$cli" db-create -q -p "$(native "$dir/vaults/$name.kdbx")" >/dev/null 2>&1 \
    || die "keepassxc-cli could not make $name.kdbx"
done

printf '%s\n%s\n%s\n' "$pw" "$pw" "$pw" \
  | "$gui" ${flags[@]+"${flags[@]}"} --pw-stdin \
      "$(native "$dir/vaults/first.kdbx")" "$(native "$dir/vaults/gone.kdbx")" "$(native "$dir/vaults/last.kdbx")" \
      >"$dir/keepassxc.out" 2>&1 &
kpxc=$!

# KeePassXC records each database in LastDatabases as it opens it.
for _ in $(seq 1 120); do
  if [ -f "$local_ini" ] && grep -q 'LastDatabases=.*first\.kdbx' "$local_ini" \
    && grep -q 'LastDatabases=.*gone\.kdbx' "$local_ini" && grep -q 'LastDatabases=.*last\.kdbx' "$local_ini"; then
    break
  fi
  kill -0 "$kpxc" 2>/dev/null || die "KeePassXC exited before opening the vaults: $(cat "$dir/keepassxc.out")"
  sleep 0.5
done
grep -q 'LastDatabases=.*last\.kdbx' "$local_ini" 2>/dev/null || {
  kill -9 "$kpxc" 2>/dev/null || true
  found=$(find "$HOME" "$dir" -name 'keepassxc*.ini' 2>/dev/null | head -5 | tr '\n' ' ')
  die "KeePassXC never listed the three vaults in $local_ini; settings files found: ${found:-none}"
}
sleep 2

# A graceful close, which is when KeePassXC writes LastOpenedDatabases and LastActiveDatabase.
if [ "$os" = windows ]; then
  taskkill //PID "$(cat "/proc/$kpxc/winpid")" >/dev/null
else
  kill -TERM "$kpxc"
fi
for _ in $(seq 1 60); do kill -0 "$kpxc" 2>/dev/null || break; sleep 0.5; done
if kill -0 "$kpxc" 2>/dev/null; then kill -9 "$kpxc" 2>/dev/null || true; die "KeePassXC did not close within 30 seconds"; fi
wait "$kpxc" 2>/dev/null || true

for key in LastActiveDatabase LastOpenedDatabases LastDatabases; do
  grep -q "^$key=" "$local_ini" || die "KeePassXC closed without writing $key to $local_ini"
done
echo "KeePassXC wrote its recent databases to $local_ini"

rm "$dir/vaults/gone.kdbx"
before="$(digest "$roaming") $(digest "$local_ini") $(digest "$dir/vaults/first.kdbx") $(digest "$dir/vaults/last.kdbx")"

# The welcome lists exactly last.kdbx then first.kdbx, each an existing file.
welcome_lists() {
  local out
  out=$(KEYPASTE_DRIVER_PASSWORD='' "$drv" welcome "$@" | tr -d '\r') || return 1
  printf '%s\n' "$out" | sed 's/^/  offered: /'
  [ "$(printf '%s\n' "$out" | wc -l | tr -d ' ')" = 2 ] || return 1
  [ "$(printf '%s\n' "$out" | sed -n 1p | sed 's#.*[/\\]##')" = last.kdbx ] || return 1
  [ "$(printf '%s\n' "$out" | sed -n 2p | sed 's#.*[/\\]##')" = first.kdbx ] || return 1
  while IFS= read -r path; do [ -f "$path" ] || [ -f "$(cygpath -u "$path" 2>/dev/null || echo "$path")" ] || return 1; done <<<"$out"
}

step "NEGATIVE CONTROL: with KeePassXC's file absent the welcome's check fails"
if welcome_lists --keepassxc-config "$(native "$dir/absent.ini")"; then
  die "the welcome's check passed with no KeePassXC settings, so it cannot fail"
fi

step "the welcome lists the two KeePassXC left, the last active first, and not the deleted one"
welcome_lists ${driver_ini[@]+"${driver_ini[@]}"} || die "the welcome did not list last.kdbx then first.kdbx"

step "choosing the first offered opens it through the ordinary unlock"
opened=$(KEYPASTE_DRIVER_PASSWORD=$pw "$drv" open-offered 1 ${driver_ini[@]+"${driver_ini[@]}"} | tr -d '\r') \
  || die "the offered vault did not open: $opened"
case "$opened" in *last.kdbx) echo "  $opened" ;; *) die "the first offered was not last.kdbx: $opened" ;; esac

step "a copy of KeePassXC's file with malformed lines lists the same two"
general=$(grep -n '^\[General\]' "$local_ini" | head -1 | cut -d: -f1)
[ -n "$general" ] || die "KeePassXC's file has no [General] section"
{
  head -n "$general" "$local_ini"
  printf 'this line is not a setting\n[General\n\303(\n'
  tail -n "+$((general + 1))" "$local_ini"
} > "$dir/malformed.ini"
cmp -s "$local_ini" "$dir/malformed.ini" && die "the malformed copy is the same as KeePassXC's file"
rm -rf "$KEYPASTE_HOME" && mkdir -p "$KEYPASTE_HOME"
welcome_lists --keepassxc-config "$(native "$dir/malformed.ini")" || die "a malformed line cost the rest of the file"

step "KeePassXC's files and the vaults are byte-identical"
after="$(digest "$roaming") $(digest "$local_ini") $(digest "$dir/vaults/first.kdbx") $(digest "$dir/vaults/last.kdbx")"
[ "$before" = "$after" ] || die "a KeePassXC file or vault changed: $before -> $after"

echo
echo "ok: KeePassXC $("$cli" --version | tr -d '\r') ($mode settings) opened three vaults and closed; the welcome offered"
echo "    the two left, the last active first, opened the one chosen, read past malformed lines, and wrote nothing of KeePassXC's"
