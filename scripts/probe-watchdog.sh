#!/usr/bin/env bash
# Temporary (F.63): fires the watchdog on a fake gate, with no build, and checks what it ends.
set -euo pipefail
. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
lib="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/lib/common.sh"

work=$(mktemp -d)
ext=''
if command -v cygpath >/dev/null 2>&1; then ext=.exe; fi
for name in kpp-orphan kpp-bystand; do cp "$(command -v sleep)$ext" "$work/$name$ext"; done
alive() {
  if [ -n "$ext" ]; then tasklist //NH //FI "IMAGENAME eq $1.exe" | grep -q "$1"; else pgrep -x "$1" >/dev/null; fi
}
cleanup() {
  for name in kpp-orphan kpp-bystand; do
    if [ -n "$ext" ]; then taskkill //F //IM "$name.exe" >/dev/null 2>&1 || true; else pkill -x "$name" || true; fi
  done
}
trap cleanup EXIT

step "started is stable and tells processes apart"
a=$(started $$)
b=$(started $$)
sleep 1.1
sleep 5 &
other=$!
c=$(started "$other")
kill "$other"
echo "this shell: '$a', again: '$b'; a process started later: '$c'"
[ -n "$a" ] && [ "$a" = "$b" ] && [ "$a" != "$c" ] || die "started is not a stable identity"

step "the watcher leaves a process that is not its gate alone"
sleep 30 &
victim=$!
(WATCHDOG_PHASE=$(mktemp) && watch_gate "$victim" not-its-start 2 '' ' ' kpp-orphan) || true
kill -0 "$victim" 2>/dev/null || die "the watcher ended a process that was not its gate"
kill "$victim"

step "a listing that runs too long is ended"
t=$SECONDS
if bounded 2 sleep 30 >/dev/null; then die "bounded let a 30-second command run to its end"; fi
[ $((SECONDS - t)) -le 6 ] || die "bounded took $((SECONDS - t))s to end a command it gave 2s"

step "a gate past its deadline is frozen, listed and ended with what it lost track of"
("$work/kpp-bystand$ext" 900 </dev/null >/dev/null 2>&1 &)
sleep 1
cat >"$work/gate.sh" <<EOF
set -euo pipefail
. "$lib"
watchdog 10 kpp-orphan kpp-bystand
step holding
("$work/kpp-orphan$ext" 901 </dev/null >/dev/null 2>&1 &)
while :; do echo tick >>"$work/ticks"; sleep 0.2; done
EOF
bash "$work/gate.sh" >"$work/log" 2>&1 &
gate=$!
for _ in $(seq 1 150); do grep -q '::error::' "$work/log" && break; sleep 0.2; done
at_error=$(lines_in "$work/ticks")
code=0
wait "$gate" || code=$?
at_end=$(lines_in "$work/ticks")
for _ in $(seq 1 50); do alive kpp-orphan || break; sleep 0.2; done
sleep 2
cat "$work/log"
echo "the gate exited $code; ticks at the error line: $at_error, at its end: $at_end"
[ "$code" = 137 ] || die "the gate exited $code, not 137"
[ $((at_end - at_error)) -le 2 ] || die "the gate ran on for $((at_end - at_error)) ticks after its deadline"
grep -q 'PPID\|ParentProcessId' "$work/log" || die "the watcher printed no process list"
grep -q 'lost track of' "$work/log" || die "the watcher named no stray"
if alive kpp-orphan; then die "the orphan outlived the gate"; fi
alive kpp-bystand || die "the bystander was ended"
echo "ok"
