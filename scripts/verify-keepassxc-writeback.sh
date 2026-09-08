#!/usr/bin/env bash
#
# verify-keepassxc-writeback.sh
#
# PERMANENT COMPATIBILITY GATE — docs/PRODUCT.md law 4.6, the write-back half.
#
# verify-keepassxc-compat.sh proves that a vault keypaste CREATES opens correctly in
# KeePassXC. That is one direction, and one moment in a file's life. This script covers the
# things Stage 1.1 added that it cannot see:
#
#   A. keypaste MODIFIES an entry that already exists, which makes KeePassLib write a
#      <History> element for the first time. KeePassXC must still read the file.
#   B. KeePassXC modifies a value -> keypaste must read what KeePassXC wrote.
#   C. KeePassXC adds a variable -> keypaste env ls must list it.
#   D. KeePassXC titles an entry with a SEPARATOR in it -> keypaste env rm must remove that
#      entry and not the one whose path it shares (docs/STEPS.md F.1a), and while both are
#      present keypaste get must refuse that path rather than release either secret, and
#      keypaste add must refuse to put a third entry on it (docs/STEPS.md F.1e).
#
# B, C and D are the claim DECISIONS.md D-0014 rests on: the env convention was chosen over
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
# D. KeePassXC titles an entry with a separator in it; keypaste must not confuse it with a
#    group of that name.
#
# `nested/TOKEN` sitting directly in env/<project> and `TOKEN` sitting in
# env/<project>/nested are two entries with one path between them. keypaste used to list the
# first and delete the second, reporting success (docs/STEPS.md F.1a).
#
# The collision is authored HERE, by KeePassXC, rather than by the code under test: `add`
# splits its argument on the last slash and would make the group, so the entry is added under
# an ordinary name and then RENAMED with `edit --title`, which puts the slash in the title
# through KeePassXC's own writer. The nested entry's password is captured BEFORE the rename,
# because afterwards keepassxc-cli's own path lookup is ambiguous too.
#
# ORDERING DEPENDENCY, as in C: `add` cannot create a missing parent group, hence `mkdir`.
# ---------------------------------------------------------------------------------------
step "D: KeePassXC authors a title containing a separator; keypaste must tell the two apart"

set +e
top_help=$("$cli" --help 2>&1)
edit_help=$("$cli" edit --help 2>&1)
set -e
kpxc_version=$("$cli" --version)

grep -Eq '^[[:space:]]*mkdir[[:space:]]' <<<"$top_help" \
  || die "keepassxc-cli ${kpxc_version} has no 'mkdir' - this gate cannot author its fixture and must not be skipped"
grep -q -- '--title' <<<"$edit_help" \
  || die "keepassxc-cli ${kpxc_version} has no 'edit --title' - this gate cannot author its fixture and must not be skipped"

printf '%s\n' "$pw" | "$cli" mkdir "$db" "env/${project}/nested" >/dev/null \
  || die "keepassxc-cli mkdir failed"
printf '%s\n' "$pw" | "$cli" add -g -L 20 -l -U -n "$db" "env/${project}/nested/NESTED_KEY" >/dev/null \
  || die "keepassxc-cli add failed for the nested entry"

nested_before=$(kpxc show -a Password "$db" "env/${project}/nested/NESTED_KEY") \
  || die "keepassxc-cli show failed for the nested entry"
[ -n "$nested_before" ] || die "the nested entry has an EMPTY password - nothing would be compared"

printf '%s\n' "$pw" | "$cli" add -g -L 20 -l -U -n "$db" "env/${project}/PLACEHOLDER" >/dev/null \
  || die "keepassxc-cli add failed for the entry about to be renamed"
printf '%s\n' "$pw" | "$cli" edit -t 'nested/NESTED_KEY' "$db" "env/${project}/PLACEHOLDER" >/dev/null \
  || die "keepassxc-cli edit --title failed - the collision cannot be authored"

# keypaste must SEE the KeePassXC-authored name before it can be asked to remove it. The
# listing DRAWS it with the separator replaced and says so on stderr, because a title
# containing one reads as a path it is not (DECISIONS.md D-0084). What is removed below is
# the name the vault holds, not the name the listing drew: sanitizing is display-only.
keys=$(kp_run env ls "$project" --vault "$db") || die "keypaste env ls failed after the rename"
case "$keys" in
  *'nested NESTED_KEY'*) ;;
  *) die "keypaste env ls does not list the title KeePassXC authored: $keys";;
esac
case "$keys" in
  *'nested/NESTED_KEY'*) die "env ls drew a slash in a title unsanitized";;
esac

# F.1e, and this is the only moment in the script where the collision exists: two entries now
# answer to env/<project>/nested/NESTED_KEY. A read of that path cannot pick one — whichever the
# file lists first is a guess, and unlike a guessed removal a guessed read hands the wrong secret
# over and says nothing. Neither the read nor the refused add may write, so the bytes say so.
before_ambiguous=$(od -An -v -tx1 "$db" | tr -d ' \n')

set +e
collide_out=$(printf '%s\n' "$pw" | "$kp" get "env/${project}/nested/NESTED_KEY" --show --vault "$db" 2>/dev/null | tr -d '\r')
collide_rc=$?
set -e
[ "$collide_rc" -ne 0 ] || die "keypaste get exited 0 on a path TWO entries answer to"
[ -z "$collide_out" ] || die "keypaste get released a secret for an ambiguous path: '${collide_out}'"

set +e
printf '%s\n' "$pw" | "$kp" add "env/${project}/nested/NESTED_KEY" --generate --vault "$db" >/dev/null 2>&1
collide_add_rc=$?
set -e
[ "$collide_add_rc" -ne 0 ] || die "keypaste add exited 0 on a path TWO entries already answer to"

[ "$(od -An -v -tx1 "$db" | tr -d ' \n')" = "$before_ambiguous" ] || die "a refused read or a refused add rewrote the vault"
printf 'ambiguous read refused (get exit %s, add exit %s, vault byte-identical)\n' "$collide_rc" "$collide_add_rc"

printf '%s\n' "$pw" | "$kp" env rm "$project" 'nested/NESTED_KEY' --yes --vault "$db" >/dev/null 2>&1 \
  || die "keypaste env rm refused a name KeePassXC authored and keypaste listed"

nested_after=$(kpxc show -a Password "$db" "env/${project}/nested/NESTED_KEY") \
  || die "keypaste env rm DELETED THE NEIGHBOUR: env/${project}/nested/NESTED_KEY is gone"
diff -u <(printf '%s\n' "$nested_before") <(printf '%s\n' "$nested_after") \
  || die "keypaste env rm removed the wrong entry (left = before, right = after)"

keys=$(kp_run env ls "$project" --vault "$db") || die "keypaste env ls failed after the removal"
case "$keys" in
  *'nested NESTED_KEY'*) die "keypaste env rm reported success but the entry is still listed: $keys";;
esac
echo "keypaste removed the slash-titled entry and left the nested one untouched."

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

if diff -q <(printf '%s\n' "${nested_before}-CORRUPTED") <(printf '%s\n' "$nested_after") >/dev/null 2>&1; then
  die "a deliberately corrupted expectation still matched D - that comparison is not gating"
fi

# Two entries with one title in one group: KDBX allows it, KeePassXC makes it, and there is no
# answer to which one was meant. keypaste must refuse and leave the file alone.
printf '%s\n' "$pw" | "$cli" add -g -L 20 -l -U -n "$db" "env/${project}/DUPE" >/dev/null \
  || die "keepassxc-cli add failed for the first duplicate"
printf '%s\n' "$pw" | "$cli" add -g -L 20 -l -U -n "$db" "env/${project}/PLACEHOLDER2" >/dev/null \
  || die "keepassxc-cli add failed for the second duplicate"
printf '%s\n' "$pw" | "$cli" edit -t 'DUPE' "$db" "env/${project}/PLACEHOLDER2" >/dev/null \
  || die "keepassxc-cli edit --title failed - the duplicate cannot be authored"

before_refusal=$(od -An -v -tx1 "$db" | tr -d ' \n')

set +e
printf '%s\n' "$pw" | "$kp" env rm "$project" DUPE --yes --vault "$db" >/dev/null 2>&1
dupe_rc=$?
set -e
[ "$dupe_rc" -ne 0 ] || die "keypaste env rm exited 0 on a name TWO entries answer to"
[ "$(od -An -v -tx1 "$db" | tr -d ' \n')" = "$before_refusal" ] \
  || die "a refused removal rewrote the vault"
printf 'ambiguous removal refused (exit %s, vault byte-identical)\n' "$dupe_rc"

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
