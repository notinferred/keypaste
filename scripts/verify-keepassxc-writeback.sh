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
#   B. KeePassXC modifies a value -> keypaste must read what KeePassXC wrote, and the
#      variable keypaste wrote on that entry must survive KeePassXC's save.
#   C. KeePassXC adds an entry under env/<project> -> keypaste must read it as an ordinary
#      entry and list it as no variable of the project (D-0416).
#   D. KeePassXC titles an entry with a SEPARATOR in it -> while it shares a path with another
#      entry, keypaste get must refuse that path rather than release either secret, keypaste
#      add must refuse to put a third entry on it (docs/STEPS.md F.1e), and keypaste rm must
#      remove neither (D-0094).
#
# B, C and D are law 4.6 in the direction compat cannot see: what KeePassXC writes into a vault
# keypaste uses is read as KeePassXC means it. Do NOT relax an assertion, add a skip, mark the
# job continue-on-error, or drop an operating system.
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

. "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"
DIE_PREFIX='WRITE-BACK GATE FAILED: '

db=${1:-}
[ -n "$db" ] || die "usage: verify-keepassxc-writeback.sh <writeback.kdbx>"
. "$(dirname "${BASH_SOURCE[0]}")/lib/kpxc.sh"
require jq

kp=$(keypaste_bin)

# BOTH sides need \r stripped. keepassxc-cli is Qt and writes CRLF on Windows; keypaste
# writes through Console.Out, whose NewLine is also CRLF there. Stripping only one side
# produces a diff that fails on windows-latest and nowhere else.
kp_run() { printf '%s\n' "$pw" | "$kp"  "$@" | tr -d '\r'; }

project=compat-app
key=DEMO_KEY
home="env/${project}/.env"

mkdir -p "$(dirname "$db")"
rm -f "$db"          # re-runnable locally, not only on a fresh CI checkout

step "seed: keypaste creates the vault and one variable on the project's home entry"
printf '%s\n%s\n' "$pw" "$pw" | "$kp" init "$db"
project_var "$kp" "$db" "$pw" "$project" "$key" v1-initial

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
hdr=$(header "$db")
[ "${hdr:0:16}" = "03d9a29a67fb4bb5" ] || die "not a KDBX file after a keypaste update (signature ${hdr:0:16})"
[ "${hdr:20:2}" = "04" ]               || die "KDBX major version changed on update: 0x${hdr:20:2}"

info=$(kpxc db-info "$db") || die "KeePassXC cannot open the file keypaste wrote history into"
grep -Eqi '^[[:space:]]*KDF:[[:space:]]*Argon2' <<<"$info" \
  || die "KDF is no longer Argon2 after a keypaste update. Got: $(grep -i '^[[:space:]]*KDF:' <<<"$info" || echo '<no KDF line>')"

after_update=$(kpxc show -a "$key" "$db" "$home") \
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
printf '%s\n' "$pw" | "$cli" edit -g -L 32 -l -U -n "$db" "$home" >/dev/null \
  || die "keepassxc-cli edit -g failed on an entry keypaste wrote"

expected=$(kpxc show -a Password "$db" "$home") || die "keepassxc-cli show failed"

# Without these three checks, a `show` that returned an empty line and a `get` that returned
# an empty line would diff clean and this gate would pass forever having compared nothing.
[ -n "$expected" ]        || die "keepassxc-cli returned an EMPTY password — nothing is being compared"
[ "${#expected}" -ge 16 ] || die "generated password is ${#expected} chars — -g/-L was not honoured"
[ "$expected" != 'v2-rewritten-by-keypaste' ] || die "edit -g did not actually change the value"

actual=$(kp_run get "$home" --show --vault "$db") || die "keypaste get failed"
diff -u <(printf '%s\n' "$expected") <(printf '%s\n' "$actual") \
  || die "keypaste does not read the value KeePassXC wrote (left = KeePassXC, right = keypaste)"
kept=$(kp_run get "$home" --field "$key" --show --vault "$db") || die "keypaste get --field failed"
[ "$kept" = 'v2-rewritten-by-keypaste' ] || die "KeePassXC's save did not keep the variable keypaste wrote: '${kept}'"
echo "keypaste reads the value KeePassXC generated, and its own variable beside it."

# ---------------------------------------------------------------------------------------
# C. KeePassXC adds an entry under env/<project> -> keypaste reads it, and it is no variable.
#
# ORDERING DEPENDENCY: keepassxc-cli `add` cannot create missing groups — it resolves the
# parent group and fails if it is absent. This works only because the seed above created
# env/<project>. Do not reorder these sections.
# ---------------------------------------------------------------------------------------
step "C: KeePassXC adds an entry under env/${project}; keypaste reads it and lists it as no variable"
printf '%s\n' "$pw" | "$cli" add -g -L 20 -l -U -n "$db" "env/${project}/ADDED_BY_KPXC" >/dev/null \
  || die "keepassxc-cli add failed"

added=$(kpxc show -a Password "$db" "env/${project}/ADDED_BY_KPXC") || die "keepassxc-cli show failed for the added entry"
[ -n "$added" ] || die "the added entry has an EMPTY password - nothing would be compared"
[ "$(kp_run get "env/${project}/ADDED_BY_KPXC" --show --vault "$db")" = "$added" ] \
  || die "keypaste does not read the entry KeePassXC added"

keys=$(kp_run env ls "$project" --vault "$db") || die "keypaste env ls failed"
printf '%s\n' "$keys"
diff -u <(printf '%s\n' '  dev' "    ${home}" "      ${key}") <(printf '%s\n' "$keys") \
  || die "keypaste env ls lists something other than the project's one variable"

projects=$(kp_run env ls --vault "$db") || die "keypaste env ls (projects) failed"
diff -u <(printf '%s\n' "$project" '  dev' "    ${home}") <(printf '%s\n' "$projects") \
  || die "keypaste env ls does not report the project as its tag makes it"

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
# through KeePassXC's own writer.
#
# ORDERING DEPENDENCY, as in C: `add` cannot create a missing parent group, hence `mkdir`.
# ---------------------------------------------------------------------------------------
step "D: KeePassXC authors a title containing a separator; keypaste lists both and refuses the path they share"

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
printf '%s\n' "$pw" | "$cli" add -g -L 20 -l -U -n "$db" "env/${project}/PLACEHOLDER" >/dev/null \
  || die "keepassxc-cli add failed for the entry about to be renamed"
printf '%s\n' "$pw" | "$cli" edit -t 'nested/NESTED_KEY' "$db" "env/${project}/PLACEHOLDER" >/dev/null \
  || die "keepassxc-cli edit --title failed - the collision cannot be authored"

# keypaste lists both as the vault holds them: the raw group and title, which only a parser reads.
kp_run ls --json --vault "$db" | jq -e --arg group "env/${project}" '
  any(.[]; .type == "entry" and .group == $group and .title == "nested/NESTED_KEY")
  and any(.[]; .type == "entry" and .group == ($group + "/nested") and .title == "NESTED_KEY")' >/dev/null \
  || die "keypaste ls --json does not list both entries KeePassXC authored"

# F.1e: two entries answer to env/<project>/nested/NESTED_KEY. A read of that path cannot pick
# one — whichever the file lists first is a guess, and a guessed read hands the wrong secret over
# and says nothing; a guessed removal deletes the wrong entry (F.1a). None of the refused acts may
# write, so the bytes say so.
before_ambiguous=$(bytes "$db")

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

set +e
printf '%s\n' "$pw" | "$kp" rm "env/${project}/nested/NESTED_KEY" --yes --vault "$db" >/dev/null 2>&1
collide_rm_rc=$?
set -e
[ "$collide_rm_rc" -ne 0 ] || die "keypaste rm exited 0 on a path TWO entries answer to"

[ "$(bytes "$db")" = "$before_ambiguous" ] || die "a refused read, add or removal rewrote the vault"
printf 'ambiguous path refused (get exit %s, add exit %s, rm exit %s, vault byte-identical)\n' "$collide_rc" "$collide_add_rc" "$collide_rm_rc"

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
  || die "a missing entry exited ${missing_rc}, expected 3 — the not-found path is broken"
[ -z "$missing_out" ] || die "a missing entry produced stdout: '${missing_out}'"

# Two entries with one title in one group: KDBX allows it, KeePassXC makes it, and there is no
# answer to which one was meant. keypaste must refuse and leave the file alone.
printf '%s\n' "$pw" | "$cli" add -g -L 20 -l -U -n "$db" "env/${project}/DUPE" >/dev/null \
  || die "keepassxc-cli add failed for the first duplicate"
printf '%s\n' "$pw" | "$cli" add -g -L 20 -l -U -n "$db" "env/${project}/PLACEHOLDER2" >/dev/null \
  || die "keepassxc-cli add failed for the second duplicate"
printf '%s\n' "$pw" | "$cli" edit -t 'DUPE' "$db" "env/${project}/PLACEHOLDER2" >/dev/null \
  || die "keepassxc-cli edit --title failed - the duplicate cannot be authored"

before_refusal=$(bytes "$db")

set +e
printf '%s\n' "$pw" | "$kp" rm "env/${project}/DUPE" --yes --vault "$db" >/dev/null 2>&1
dupe_rc=$?
set -e
[ "$dupe_rc" -ne 0 ] || die "keypaste rm exited 0 on a name TWO entries answer to"
[ "$(bytes "$db")" = "$before_refusal" ] \
  || die "a refused removal rewrote the vault"
printf 'ambiguous removal refused (exit %s, vault byte-identical)\n' "$dupe_rc"

set +e
wrong_out=$(printf '%s\n' "${pw}-DELIBERATELY-WRONG" | "$cli" ls -R -f "$db" 2>&1 | tr -d '\r')
wrong_rc=$?
set -e
[ "$wrong_rc" -ne 0 ] || die "keepassxc-cli exited 0 with a WRONG password against the write-back vault"
case "$wrong_out" in
  *"$project"*) die "a wrong password still produced entry names — the gate is not gating";;
esac
printf 'wrong password rejected (exit %s, no entry names emitted)\n' "$wrong_rc"

printf '\nWRITE-BACK GATE PASSED — %s round-trips through KeePassXC %s in both directions\n' \
  "$db" "$("$cli" --version)"
