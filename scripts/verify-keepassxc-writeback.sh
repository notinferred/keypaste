#!/usr/bin/env bash
#
# verify-keepassxc-writeback.sh
#
# PERMANENT COMPATIBILITY GATE — docs/PRODUCT.md law 4.6, the write-back half.
#
# verify-keepassxc-compat.sh proves that a vault keypaste CREATES opens correctly in
# KeePassXC. That is one direction, and one moment in a file's life. This script covers the
# two things Stage 1.1 added that it cannot see:
#
#   A. keypaste MODIFIES an entry that already exists, which makes KeePassLib write a
#      <History> element for the first time. KeePassXC must still read the file.
#   B. KeePassXC modifies a value -> keypaste must read what KeePassXC wrote.
#   C. KeePassXC adds a variable -> keypaste env ls must list it.
#
# B and C are the claim DECISIONS.md D-0014 rests on: the env convention was chosen over
# custom string fields precisely BECAUSE keepassxc-cli can perform them. If this file ever
# has to be deleted to make CI green, the convention itself is wrong — change the convention,
# not this gate. Do NOT relax an assertion, add a skip, mark the job continue-on-error, or
# drop an operating system.
#
# Usage:  scripts/verify-keepassxc-writeback.sh <writeback.kdbx>
# Env:    KP_COMPAT_PASSWORD  master password for the fixture   (required)
#         KPXC_CLI            path to keepassxc-cli             (default: PATH lookup)
#         KEYPASTE_BIN        path to the keypaste binary       (default: the Release build)
#
# This builds its OWN database rather than reusing the compat fixture. That fixture is
# asserted against an exact `ls -R -f` tree; adding env entries to it would break the other
# gate, and mutating it here would make the two order-dependent.

set -euo pipefail

die()  { printf '\nWRITE-BACK GATE FAILED: %s\n' "$*" >&2; exit 1; }
step() { printf '\n--- %s\n' "$*"; }

db=${1:-}
[ -n "$db" ] || die "usage: verify-keepassxc-writeback.sh <writeback.kdbx>"
pw=${KP_COMPAT_PASSWORD:-}
[ -n "$pw" ] || die "KP_COMPAT_PASSWORD is not set"
cli=${KPXC_CLI:-keepassxc-cli}

# Absence of the tool is a FAILURE, never a skip — see verify-keepassxc-compat.sh (i).
if ! command -v "$cli" >/dev/null 2>&1 && [ ! -x "$cli" ]; then
  die "keepassxc-cli not found (KPXC_CLI='${cli}'). This gate must never be skipped or soft-passed."
fi

kp=${KEYPASTE_BIN:-}
if [ -z "$kp" ]; then
  kp=artifacts/bin/Keypaste.Cli/release/keypaste
  [ -x "$kp" ] || kp="${kp}.exe"
fi
[ -x "$kp" ] || die "keypaste binary not found at '$kp' (build it, or set KEYPASTE_BIN)"

# BOTH sides need \r stripped. keepassxc-cli is Qt and writes CRLF on Windows; keypaste
# writes through Console.Out, whose NewLine is also CRLF there. Stripping only one side
# produces a diff that fails on windows-latest and nowhere else.
kpxc()   { printf '%s\n' "$pw" | "$cli" "$@" | tr -d '\r'; }
kp_run() { printf '%s\n' "$pw" | "$kp"  "$@" | tr -d '\r'; }

project=compat-app
key=DEMO_KEY

mkdir -p "$(dirname "$db")"
rm -f "$db"          # re-runnable locally, not only on a fresh CI checkout

step "seed: keypaste creates the vault and one env variable"
printf '%s\n%s\n' "$pw" "$pw" | "$kp" init "$db"
printf '%s\n%s\n' "$pw" 'v1-initial' | "$kp" env set "$project" "$key" --vault "$db"

# ---------------------------------------------------------------------------------------
# A. keypaste modifies an entry — the first KDBX <History> element this codebase ever writes.
#
# Setting the same variable twice makes KeePassLib snapshot the previous value into the
# entry's history. That serialization path was unreachable before Stage 1.1, so nothing had
# ever checked that KeePassXC can read the result. If the two disagree, keypaste ships a file
# that its own compatibility promise says must open, and does not.
# ---------------------------------------------------------------------------------------
step "A: keypaste rewrites the value (writes entry history)"
printf '%s\n%s\n' "$pw" 'v2-rewritten-by-keypaste' | "$kp" env set "$project" "$key" --vault "$db"

# The container is re-checked AFTER a keypaste modify-save, not only after a create. A format
# or KDF shift on the update path would round-trip through keypaste perfectly and be invisible
# everywhere except here.
hdr=$(od -An -v -tx1 -N12 "$db" | tr -d ' \n' | tr 'A-Z' 'a-z')
[ "${hdr:0:16}" = "03d9a29a67fb4bb5" ] || die "not a KDBX file after a keypaste update (signature ${hdr:0:16})"
[ "${hdr:20:2}" = "04" ]               || die "KDBX major version changed on update: 0x${hdr:20:2}"

info=$(kpxc db-info "$db") || die "KeePassXC cannot open the file keypaste wrote history into"
grep -Eqi '^[[:space:]]*KDF:[[:space:]]*Argon2' <<<"$info" \
  || die "KDF is no longer Argon2 after a keypaste update. Got: $(grep -i '^[[:space:]]*KDF:' <<<"$info" || echo '<no KDF line>')"

after_update=$(kpxc show -a Password "$db" "env/${project}/${key}") \
  || die "keepassxc-cli show failed after a keypaste update"
diff -u <(printf '%s\n' 'v2-rewritten-by-keypaste') <(printf '%s\n' "$after_update") \
  || die "KeePassXC does not see the value keypaste wrote"
echo "KeePassXC reads the updated entry, history and all."

# ---------------------------------------------------------------------------------------
# B. KeePassXC modifies a value -> keypaste must read it.
#
# `edit -g` makes KEEPASSXC generate the new value. That matters twice: the expectation is a
# string keypaste has never seen (so no shared constant can make both sides agree by
# accident), and it needs no second stdin line, so this does not depend on how many prompts a
# given keepassxc-cli build issues for -p.
#
# Alphanumeric only (-l -U -n, no -s): the value passes through shell variables and diff, and
# a generated leading '-' or quote would break the harness rather than the code under test.
# `edit` requires at least one field option, which -g satisfies.
# ---------------------------------------------------------------------------------------
step "B: KeePassXC generates a new value; keypaste must read it"
printf '%s\n' "$pw" | "$cli" edit -g -L 32 -l -U -n "$db" "env/${project}/${key}" >/dev/null \
  || die "keepassxc-cli edit -g failed — the convention's central claim (KeePassXC can edit an env value) is broken"

expected=$(kpxc show -a Password "$db" "env/${project}/${key}") || die "keepassxc-cli show failed"

# Without these three checks, a `show` that returned an empty line and a `get` that returned
# an empty line would diff clean and this gate would pass forever having compared nothing.
[ -n "$expected" ]        || die "keepassxc-cli returned an EMPTY password — nothing is being compared"
[ "${#expected}" -ge 16 ] || die "generated password is ${#expected} chars — -g/-L was not honoured"
[ "$expected" != 'v2-rewritten-by-keypaste' ] || die "edit -g did not actually change the value"

actual=$(kp_run get "env/${project}/${key}" --show --vault "$db") || die "keypaste get failed"
diff -u <(printf '%s\n' "$expected") <(printf '%s\n' "$actual") \
  || die "keypaste does not read the value KeePassXC wrote (left = KeePassXC, right = keypaste)"
echo "keypaste reads the value KeePassXC generated."

# ---------------------------------------------------------------------------------------
# C. KeePassXC adds a variable -> keypaste env ls must list it.
#
# ORDERING DEPENDENCY: keepassxc-cli `add` cannot create missing groups — it resolves the
# parent group and fails if it is absent. This works only because the seed above created
# env/<project>. Do not reorder these sections.
# ---------------------------------------------------------------------------------------
step "C: KeePassXC adds a variable; keypaste must list it"
printf '%s\n' "$pw" | "$cli" add -g -L 20 -l -U -n "$db" "env/${project}/ADDED_BY_KPXC" >/dev/null \
  || die "keepassxc-cli add failed"

keys=$(kp_run env ls "$project" --vault "$db") || die "keypaste env ls failed"
printf '%s\n' "$keys"
diff -u <(printf '%s\n' 'ADDED_BY_KPXC' "$key") <(printf '%s\n' "$keys") \
  || die "keypaste env ls disagrees with KeePassXC about the project's variables"

projects=$(kp_run env ls --vault "$db") || die "keypaste env ls (projects) failed"
diff -u <(printf '%s\n' "$project") <(printf '%s\n' "$projects") \
  || die "keypaste env ls does not report the project"

# ---------------------------------------------------------------------------------------
# NEGATIVE CONTROL.
#
# Everything above only means something if this gate is still capable of failing. See
# verify-keepassxc-compat.sh (v) for why this is the cheapest insurance in the repository.
# Never remove it.
# ---------------------------------------------------------------------------------------
step "NEGATIVE CONTROL: the comparison must be able to fail"
if diff -q <(printf '%s\n' "${expected}-CORRUPTED") <(printf '%s\n' "$actual") >/dev/null 2>&1; then
  die "a deliberately corrupted expectation still matched — this gate is not gating"
fi

set +e
missing_out=$(printf '%s\n' "$pw" | "$kp" get "env/${project}/NO_SUCH_KEY" --show --vault "$db" 2>/dev/null | tr -d '\r')
missing_rc=$?
set -e
[ "$missing_rc" -eq 3 ] \
  || die "a missing env variable exited ${missing_rc}, expected 3 — the not-found path is broken"
[ -z "$missing_out" ] || die "a missing env variable produced stdout: '${missing_out}'"

set +e
wrong_out=$(printf '%s\n' "${pw}-DELIBERATELY-WRONG" | "$cli" ls -R -f "$db" 2>&1 | tr -d '\r')
wrong_rc=$?
set -e
[ "$wrong_rc" -ne 0 ] || die "keepassxc-cli exited 0 with a WRONG password against the write-back vault"
case "$wrong_out" in
  *"$key"*) die "a wrong password still produced variable names — the gate is not gating";;
esac
printf 'wrong password rejected (exit %s, no variable names emitted)\n' "$wrong_rc"

printf '\nWRITE-BACK GATE PASSED — %s round-trips through KeePassXC %s in both directions\n' \
  "$db" "$("$cli" --version)"
