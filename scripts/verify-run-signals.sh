#!/usr/bin/env bash
#
# verify-run-signals.sh
#
# Proves that `keypaste run` relays SIGTERM to its child and reports the child's exit status,
# rather than dying and orphaning it.
#
# This is the case `docker stop` and `timeout` produce: both signal keypaste alone, and neither
# reaches the child by itself. Ctrl+C is different — the terminal driver delivers it to the whole
# foreground process group, so the child already gets it — which is why SignalPolicy relays
# SIGTERM unconditionally and the others only when there is no terminal.
#
# Unix only. There is no kill(1) equivalent on Windows and no tty on a runner, so the Windows
# half of this behaviour is a documented gap in the TEST, not a relaxation of the gate.
# Where it can run it must never be skipped or soft-passed.
#
# Usage:  scripts/verify-run-signals.sh
# Env:    KEYPASTE_BIN  path to the keypaste binary  (default: the Release build)

set -euo pipefail

die() { printf '\nverify-run-signals: FAILED - %s\n' "$*" >&2; exit 1; }

case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) die "this gate is Unix-only and must not be invoked on Windows" ;;
esac

kp=${KEYPASTE_BIN:-}
if [ -z "$kp" ]; then
  kp=artifacts/bin/Keypaste.Cli/release/keypaste
  [ -x "$kp" ] || kp="${kp}.exe"
fi
[ -x "$kp" ] || die "keypaste binary not found at '$kp' (build it, or set KEYPASTE_BIN)"

# This script's own interpreter, by absolute path. See verify-run-injection.sh for why the bare
# word `bash` is not safe to use as a child process.
child=${BASH:-/bin/bash}
[ -x "$child" ] || die "cannot locate the bash that is running this script"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

db="$work/signals.kdbx"
pw='signal-gate-master-pw'
log="$work/child.log"

printf '%s\n%s\n' "$pw" "$pw" | "$kp" init "$db" >/dev/null
printf '%s\n%s\n' "$pw" 'v' | "$kp" env set gate MARKER --vault "$db" >/dev/null

echo "--- SIGTERM sent to keypaste must reach the child, and its status must come back"

# The child sleeps in short bursts rather than once for a long time: a shell does not run a trap
# until the current foreground command returns, so the sleep duration IS the trap latency.
printf '%s\n' "$pw" | "$kp" run gate --vault "$db" -- \
  "$child" -c 'trap "echo GOT_TERM; exit 42" TERM; echo READY; while :; do sleep 0.2; done' \
  >"$log" 2>&1 &
wrapper=$!

waited=0
until grep -q READY "$log" 2>/dev/null; do
  sleep 0.1
  waited=$((waited + 1))
  [ "$waited" -lt 300 ] || die "the child never started (30s); log: $(cat "$log")"
done

kill -TERM "$wrapper"

set +e
wait "$wrapper"
status=$?
set -e

grep -q GOT_TERM "$log" || die "the child never received SIGTERM; keypaste did not relay it"

# 143 is 128+15: keypaste itself died of the signal instead of suppressing it, waiting, and
# reporting what the child said. That is the exact regression removing `context.Cancel = true`
# would cause, so the number is named here rather than left to be worked out.
[ "$status" = "42" ] || die "expected the child's 42, got $status (143 means keypaste died instead of relaying)"
echo "ok: SIGTERM was relayed, and the child's exit code came back"

echo "--- NEGATIVE CONTROL: the wait must be able to observe a different code"

# Without this, a `wait` that always reported 42 would pass the assertion above forever.
set +e
printf '%s\n' "$pw" | "$kp" run gate --vault "$db" -- "$child" -c 'exit 7' >/dev/null 2>&1
status=$?
set -e
[ "$status" = "7" ] || die "expected 7 from an ordinary exit, got $status"
echo "ok: an ordinary exit reports its own code"

printf '\nRUN SIGNAL GATE PASSED - SIGTERM relayed, child status propagated\n'
