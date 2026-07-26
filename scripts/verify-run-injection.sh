#!/usr/bin/env bash
#
# verify-run-injection.sh
#
# Proves that `keypaste run` actually injects — and that it writes nothing to disk while doing
# it (CORE.md law 3.4: "Injection is into process environment memory, not into files").
#
# This gate exists because `run` streams stdio transparently: the child inherits keypaste's real
# handles, which puts everything it prints beyond CliContext.Stdout and therefore beyond every
# in-process test. RunCommandTests asserts the environment keypaste *builds*; only this script
# can assert what a real child process actually *received*.
#
# The no-temp-file check is the part that must never be softened. It is the one promise keypaste
# makes about injection that is narrow enough to be true and testable, and asserting it in prose
# in SECURITY.md while nothing verifies it would be exactly the overclaim that file warns about.
#
# Usage:  scripts/verify-run-injection.sh
# Env:    KEYPASTE_BIN  path to the keypaste binary  (default: the Release build)

set -euo pipefail

die() { printf '\nverify-run-injection: FAILED - %s\n' "$*" >&2; exit 1; }

# The child is THIS script's own interpreter, by absolute path, never the bare word `bash`. On a
# Windows developer machine `bash` on PATH is usually C:\Windows\System32\bash.exe — the WSL
# launcher — which starts a Linux session with an environment of its own and discards the one it
# was given. A gate written against that reports "injection did not happen" on a build where
# injection works perfectly, which is exactly what it did before this line existed.
child=${BASH:-/bin/bash}
[ -x "$child" ] || die "cannot locate the bash that is running this script"

kp=${KEYPASTE_BIN:-}
if [ -z "$kp" ]; then
  kp=artifacts/bin/Keypaste.Cli/release/keypaste
  [ -x "$kp" ] || kp="${kp}.exe"
fi
[ -x "$kp" ] || die "keypaste binary not found at '$kp' (build it, or set KEYPASTE_BIN)"

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

db="$work/run.kdbx"
pw='run-gate-master-pw'
sentinel='sk_live_9f3c_INJECTED_c08e'

# Console.Out writes CRLF on Windows, so both sides of every comparison are normalised.
kp_run() { printf '%s\n' "$pw" | "$kp" "$@" | tr -d '\r'; }

printf '%s\n%s\n' "$pw" "$pw" | "$kp" init "$db" >/dev/null
printf '%s\n%s\n' "$pw" "$sentinel" | "$kp" env set gate SENTINEL --vault "$db" >/dev/null
printf '%s\n%s\n' "$pw" '' | "$kp" env set gate EMPTY_ONE --vault "$db" >/dev/null

echo "--- (i) the child receives the value, and nothing else lands on stdout"

# Byte-equality is deliberate: it proves the injection AND that keypaste printed nothing of its
# own onto the stdout it is now sharing with the child. Two assertions for the price of one.
observed=$(kp_run run gate --vault "$db" -- "$child" -c 'printf %s "$SENTINEL"')
[ -n "$observed" ] || die "the child printed nothing; injection did not happen"
[ "$observed" = "$sentinel" ] || die "expected the sentinel, got '$observed'"
echo "ok: the child read the injected value from its environment"

echo "--- (ii) NOTHING is written to disk (CORE.md law 3.4)"

# Every temp-directory variable the runtime might consult is pointed at an empty directory, so a
# file written anywhere keypaste would plausibly put one shows up here.
# This check must never be skipped or soft-passed: a gate that quietly stops
# checking anything reports green forever, which is worse than not having it.
scratch="$work/scratch"
mkdir -p "$scratch"
observed=$(TMPDIR="$scratch" TMP="$scratch" TEMP="$scratch" \
  kp_run run gate --vault "$db" -- "$child" -c 'printf %s "$SENTINEL"')
[ "$observed" = "$sentinel" ] || die "injection stopped working under a redirected TMPDIR"

leaked=$(find "$scratch" -type f | wc -l | tr -d ' ')
[ "$leaked" = "0" ] || {
  find "$scratch" -type f >&2
  die "$leaked file(s) were written while injecting; law 3.4 says none"
}
echo "ok: no temporary file was written"

echo "--- (iii) the inherited environment survives, and empty values are real values"

export GATE_PARENT_MARKER='inherited-from-the-parent'
observed=$(kp_run run gate --vault "$db" -- "$child" -c 'printf %s "$GATE_PARENT_MARKER"')
[ "$observed" = "inherited-from-the-parent" ] || die "the parent environment was not inherited"

# `set -u` inside the child turns "unset" into a failure and leaves "set but empty" alone, which
# is the distinction `KEY=` in a .env file depends on.
observed=$(kp_run run gate --vault "$db" -- "$child" -c 'set -u; printf "[%s]" "$EMPTY_ONE"')
[ "$observed" = "[]" ] || die "an empty value was not injected as an empty value, got '$observed'"
echo "ok: inherited variables survive and empty values are set"

echo "--- (iv) exit codes are the child's, verbatim"

set +e
printf '%s\n' "$pw" | "$kp" run gate --vault "$db" -- "$child" -c 'exit 42' >/dev/null 2>&1
status=$?
set -e
[ "$status" = "42" ] || die "expected the child's 42, got $status"

set +e
printf '%s\n' "$pw" | "$kp" run gate --vault "$db" -- definitely-not-a-real-binary-9f3c >/dev/null 2>&1
status=$?
set -e
[ "$status" = "127" ] || die "expected 127 for a missing command, got $status"
echo "ok: 42 passed through, and a missing command reports 127"

echo "--- NEGATIVE CONTROL: the comparison must be able to fail"

# Without this, a `run` that injected nothing and a comparison that expected nothing would agree
# with each other and the gate would be green forever.
observed=$(kp_run run gate --vault "$db" -- "$child" -c 'printf %s "$NOT_IN_THE_VAULT"')
[ -z "$observed" ] || die "a variable that is not in the vault arrived anyway"

set +e
printf '%s\n' "$pw" | "$kp" run no-such-project --vault "$db" -- "$child" -c 'exit 0' >/dev/null 2>&1
status=$?
set -e
[ "$status" = "3" ] || die "an unknown project must exit 3, got $status"
echo "ok: an absent variable stays absent, and an unknown project exits 3"

printf '\nRUN INJECTION GATE PASSED - the child received the value and no file was written\n'
